using Contix.Application.Common.Models;
using Contix.Application.Features.NotasFiscais;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using Contix.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Contix.Infrastructure.Persistence.Repositories;

public sealed class NotaFiscalRepository(ContixDbContext context) : Repository<NotaFiscal>(context), INotaFiscalRepository
{
    /// <summary>Datas do filtro são dias no horário de Brasília.</summary>
    private static readonly TimeSpan FusoBrasilia = TimeSpan.FromHours(-3);

    public async Task<PagedResult<NotaFiscalDto>> ListarAsync(Guid empresaId, NotaFiscalFiltro f, CancellationToken ct = default)
    {
        var q = Set.AsNoTracking().Where(n => n.EmpresaId == empresaId);
        if (f.De is { } de) { var inicio = Inicio(de); q = q.Where(n => n.DataEmissao >= inicio); }
        if (f.Ate is { } ate) { var fim = Inicio(ate.AddDays(1)); q = q.Where(n => n.DataEmissao < fim); }
        if (f.Manifestacao is { } m) q = q.Where(n => n.Manifestacao == m);
        if (f.Situacao is { } s) q = q.Where(n => n.Situacao == s);
        if (!string.IsNullOrWhiteSpace(f.Busca))
        {
            var termo = f.Busca.Trim();
            var digitos = DocumentoFiscal.SomenteDigitos(termo);
            var like = $"%{termo.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
            q = digitos.Length > 0 && digitos.Length == termo.Count(char.IsLetterOrDigit)
                ? q.Where(n => n.EmitenteDocumento.Contains(digitos) || n.ChaveAcesso.Contains(digitos) || n.Numero.ToString() == digitos.TrimStart('0'))
                : q.Where(n => EF.Functions.ILike(n.EmitenteNome, like));
        }

        var total = await q.CountAsync(ct);
        IOrderedQueryable<NotaFiscal> ordenada = (f.OrderBy?.ToLowerInvariant(), f.Desc) switch
        {
            ("valor", true) => q.OrderByDescending(n => n.ValorTotal),
            ("valor", false) => q.OrderBy(n => n.ValorTotal),
            ("emitente", true) => q.OrderByDescending(n => n.EmitenteNome),
            ("emitente", false) => q.OrderBy(n => n.EmitenteNome),
            ("numero", true) => q.OrderByDescending(n => n.Numero),
            ("numero", false) => q.OrderBy(n => n.Numero),
            ("dataemissao", false) => q.OrderBy(n => n.DataEmissao),
            _ => q.OrderByDescending(n => n.DataEmissao)
        };
        var itens = await Projetar(ordenada.ThenBy(n => n.Id).Skip(f.Skip).Take(f.PageSize)).ToListAsync(ct);
        return new PagedResult<NotaFiscalDto>(itens, f.Page, f.PageSize, total);
    }

    public async Task<ResumoNotasFiscaisDto> ResumoAsync(Guid empresaId, CancellationToken ct = default)
    {
        var grupos = await Set.AsNoTracking().Where(n => n.EmpresaId == empresaId)
            .GroupBy(n => new { n.Manifestacao, Cancelada = n.Situacao == SituacaoNFe.Cancelada })
            .Select(g => new { g.Key.Manifestacao, g.Key.Cancelada, Total = g.Count() }).ToListAsync(ct);
        // Total: todas as notas. Por manifestação: só as não canceladas (cancelada não se manifesta).
        int Qtd(ManifestacaoNFe m) => grupos.Where(g => g.Manifestacao == m && !g.Cancelada).Sum(g => g.Total);
        return new ResumoNotasFiscaisDto(grupos.Sum(g => g.Total), Qtd(ManifestacaoNFe.SemManifestacao), Qtd(ManifestacaoNFe.Ciencia),
            Qtd(ManifestacaoNFe.Confirmada), Qtd(ManifestacaoNFe.Desconhecida), Qtd(ManifestacaoNFe.NaoRealizada));
    }

    public Task<NotaFiscalDto?> ObterDtoAsync(Guid empresaId, Guid id, CancellationToken ct = default) =>
        Projetar(Set.AsNoTracking().Where(n => n.EmpresaId == empresaId && n.Id == id)).FirstOrDefaultAsync(ct);

    public Task<NotaFiscal?> ObterAsync(Guid empresaId, Guid id, CancellationToken ct = default) =>
        Set.FirstOrDefaultAsync(n => n.EmpresaId == empresaId && n.Id == id, ct);

    public async Task<IReadOnlyList<NotaFiscal>> ListarPorIdsAsync(Guid empresaId, IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
        await Set.AsNoTracking().Where(n => n.EmpresaId == empresaId && ids.Contains(n.Id)).ToListAsync(ct);

    public async Task<NotaFiscal?> ObterPorChaveAsync(Guid empresaId, string chaveAcesso, bool ignorarTenant, CancellationToken ct = default)
    {
        // Inclui as notas adicionadas nesta mesma operação (ainda não salvas), ex.: resumo e cancelamento no mesmo lote.
        var local = Set.Local.FirstOrDefault(n => n.EmpresaId == empresaId && n.ChaveAcesso == chaveAcesso && !n.IsDeleted);
        if (local is not null) return local;
        var q = ignorarTenant ? Set.IgnoreQueryFilters().Where(n => !n.IsDeleted) : Set;
        return await q.FirstOrDefaultAsync(n => n.EmpresaId == empresaId && n.ChaveAcesso == chaveAcesso, ct);
    }

    private static DateTime Inicio(DateOnly dia) =>
        new DateTimeOffset(dia.ToDateTime(TimeOnly.MinValue), FusoBrasilia).UtcDateTime;

    private static IQueryable<NotaFiscalDto> Projetar(IQueryable<NotaFiscal> q) => q.Select(n => new NotaFiscalDto(
        n.Id, n.EmpresaId, n.ChaveAcesso, n.Modelo, n.Serie, n.Numero,
        n.EmitenteDocumento, n.EmitenteNome, n.EmitenteIe,
        n.ValorTotal, n.DataEmissao, n.TipoOperacao, n.Protocolo, n.Situacao,
        n.Origem, n.XmlCompleto, n.XmlStorageKey != null,
        n.Manifestacao, n.ManifestadaEm, n.ManifestadaPor != null ? n.ManifestadaPor.Nome : null,
        n.Manifestacao != ManifestacaoNFe.SemManifestacao && n.ManifestadaPorId == null,
        n.JustificativaManifestacao, n.ProtocoloManifestacao, n.ProtocoloManifestacao != null,
        n.CreatedAt));
}

public sealed class IntegracaoSefazRepository(ContixDbContext context) : IIntegracaoSefazRepository
{
    public async Task<CertificadoDigitalDto?> ObterCertificadoDtoAsync(Guid empresaId, DateTime agora, CancellationToken ct = default)
    {
        var c = await context.CertificadosDigitais.AsNoTracking()
            .Where(x => x.EmpresaId == empresaId)
            .Select(x => new { x.Id, x.NomeArquivo, x.Titular, x.Documento, x.Emissor, x.ValidoDe, x.ValidoAte, x.CreatedAt, Nome = x.EnviadoPor != null ? x.EnviadoPor.Nome : null })
            .FirstOrDefaultAsync(ct);
        return c is null ? null : new CertificadoDigitalDto(c.Id, c.NomeArquivo, c.Titular, c.Documento, c.Emissor, c.ValidoDe, c.ValidoAte,
            agora >= c.ValidoAte, (int)Math.Floor((c.ValidoAte - agora).TotalDays), c.CreatedAt, c.Nome);
    }

    public Task<CertificadoDigital?> ObterCertificadoAsync(Guid empresaId, bool ignorarTenant, CancellationToken ct = default) =>
        (ignorarTenant ? context.CertificadosDigitais.IgnoreQueryFilters().Where(c => !c.IsDeleted) : context.CertificadosDigitais)
        .FirstOrDefaultAsync(c => c.EmpresaId == empresaId, ct);

    public async Task AdicionarCertificadoAsync(CertificadoDigital certificado, CancellationToken ct = default) =>
        await context.CertificadosDigitais.AddAsync(certificado, ct);

    public void RemoverCertificado(CertificadoDigital certificado)
    {
        // O conteúdo criptografado não fica guardado depois da exclusão lógica.
        certificado.ArquivoCriptografado = [];
        certificado.SenhaCriptografada = [];
        context.CertificadosDigitais.Remove(certificado);
    }

    public Task<SincronizacaoNFe?> ObterSincronizacaoAsync(Guid empresaId, CancellationToken ct = default) =>
        context.SincronizacoesNFe.FirstOrDefaultAsync(s => s.EmpresaId == empresaId, ct);

    public async Task AdicionarSincronizacaoAsync(SincronizacaoNFe sincronizacao, CancellationToken ct = default) =>
        await context.SincronizacoesNFe.AddAsync(sincronizacao, ct);

    public async Task<IReadOnlyList<Guid>> ListarProntasAsync(DateTime agora, int limite, CancellationToken ct = default) =>
        await (from s in context.SincronizacoesNFe.IgnoreQueryFilters()
               join e in context.Empresas.IgnoreQueryFilters() on s.EmpresaId equals e.Id
               join o in context.Escritorios on s.TenantId equals o.Id
               where s.Ativa && !s.IsDeleted && !e.IsDeleted && e.Situacao == SituacaoEmpresa.Ativa && o.Ativo
                     && (s.ProximaConsultaEm == null || s.ProximaConsultaEm <= agora)
               orderby s.ProximaConsultaEm
               select s.Id).Take(limite).ToListAsync(ct);

    public Task<SincronizacaoNFe?> ObterParaProcessarAsync(Guid sincronizacaoId, CancellationToken ct = default) =>
        context.SincronizacoesNFe.IgnoreQueryFilters().Include(s => s.Empresa)
            .FirstOrDefaultAsync(s => s.Id == sincronizacaoId && !s.IsDeleted, ct);

    public async Task<IReadOnlyList<NotaFiscal>> ListarPendentesCienciaAsync(Guid empresaId, DateTime emitidasDesde, int limite, CancellationToken ct = default) =>
        await context.NotasFiscais.IgnoreQueryFilters()
            .Where(n => n.EmpresaId == empresaId && !n.IsDeleted && n.Origem == OrigemNotaFiscal.Sefaz && !n.XmlCompleto
                        && n.Situacao == SituacaoNFe.Autorizada && n.Manifestacao == ManifestacaoNFe.SemManifestacao
                        && n.DataEmissao >= emitidasDesde)
            .OrderByDescending(n => n.DataEmissao).Take(limite).ToListAsync(ct);
}
