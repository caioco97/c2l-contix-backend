using Contix.Application.Common.Models;
using Contix.Application.Features.Auth;
using Contix.Application.Features.Dispositivos;
using Contix.Application.Features.Documentos;
using Contix.Application.Features.Empresas;
using Contix.Application.Features.Escritorios;
using Contix.Application.Features.Pastas;
using Contix.Application.Features.Usuarios;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using Contix.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Contix.Infrastructure.Persistence.Repositories;

public sealed class EscritorioRepository(ContixDbContext context) : Repository<Escritorio>(context), IEscritorioRepository
{
    public Task<bool> CnpjExisteAsync(string cnpj, CancellationToken ct = default) =>
        Set.AnyAsync(e => e.Cnpj == cnpj, ct);
}

public sealed class RefreshTokenRepository(ContixDbContext context) : Repository<RefreshToken>(context), IRefreshTokenRepository
{
    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default) =>
        Set.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task<IReadOnlyList<RefreshToken>> ListarAtivosDaFamiliaAsync(Guid familia, DateTime agora, CancellationToken ct = default) =>
        await Set.Where(t => t.Familia == familia && t.RevogadoEm == null && t.ExpiraEm > agora).ToListAsync(ct);

    public async Task<IReadOnlyList<RefreshToken>> ListarAtivosDoUsuarioAsync(Guid usuarioId, DateTime agora, CancellationToken ct = default) =>
        await Set.Where(t => t.UsuarioId == usuarioId && t.RevogadoEm == null && t.ExpiraEm > agora).ToListAsync(ct);
}

public sealed class DispositivoPushRepository(ContixDbContext context) : Repository<DispositivoPush>(context), IDispositivoPushRepository
{
    public Task<DispositivoPush?> ObterPorTokenAsync(string token, CancellationToken ct = default) =>
        Set.FirstOrDefaultAsync(d => d.Token == token, ct);

    public async Task<IReadOnlyList<DispositivoPush>> ListarDoUsuarioAsync(Guid usuarioId, CancellationToken ct = default) =>
        await Set.Where(d => d.UsuarioId == usuarioId).ToListAsync(ct);
}

public sealed class UsuarioRepository(ContixDbContext context) : Repository<Usuario>(context), IUsuarioRepository
{
    public Task<Usuario?> GetByEmailIgnorandoTenantAsync(string email, CancellationToken ct = default)
    {
        var normalizado = email.Trim().ToLowerInvariant();
        return Set.IgnoreQueryFilters().Include(u => u.Escritorio).FirstOrDefaultAsync(u => u.Email == normalizado && !u.IsDeleted, ct);
    }

    public Task<Usuario?> GetByIdIgnorandoTenantAsync(Guid id, CancellationToken ct = default) =>
        Set.IgnoreQueryFilters().Include(u => u.Escritorio).FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted, ct);

    public Task<bool> EmailExisteIgnorandoTenantAsync(string email, Guid? exceto, CancellationToken ct = default) =>
        Set.IgnoreQueryFilters().AnyAsync(u => u.Email == email && !u.IsDeleted && (exceto == null || u.Id != exceto), ct);

    public Task<Usuario?> GetComEmpresasAsync(Guid id, CancellationToken ct = default) =>
        Set.Include(u => u.Empresas).FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task<IReadOnlyList<Usuario>> ListarClientesDaEmpresaAsync(Guid empresaId, CancellationToken ct = default) =>
        await Set.AsNoTracking().Include(u => u.Empresas)
            .Where(u => u.Perfil == PerfilUsuario.Cliente && u.Empresas.Any(e => e.EmpresaId == empresaId))
            .ToListAsync(ct);

    public Task<bool> PossuiEmpresaAtivaIgnorandoTenantAsync(Guid usuarioId, CancellationToken ct = default) =>
        Context.UsuarioEmpresas.IgnoreQueryFilters()
            .AnyAsync(v => v.UsuarioId == usuarioId && Context.Empresas.IgnoreQueryFilters().Any(e => e.Id == v.EmpresaId && !e.IsDeleted), ct);

    public async Task<IReadOnlyList<Guid>> ListarClientesExclusivosDaEmpresaAsync(Guid empresaId, CancellationToken ct = default) =>
        await Set.AsNoTracking()
            .Where(u => u.Perfil == PerfilUsuario.Cliente
                        && u.Empresas.Any(v => v.EmpresaId == empresaId)
                        && !u.Empresas.Any(v => v.EmpresaId != empresaId && Context.Empresas.Any(e => e.Id == v.EmpresaId)))
            .Select(u => u.Id)
            .ToListAsync(ct);

    public Task<PagedResult<Usuario>> PesquisarAsync(UsuarioFiltro filtro, CancellationToken ct = default)
    {
        var q = Set.AsNoTracking().Include(u => u.Empresas).AsQueryable();

        if (!string.IsNullOrWhiteSpace(filtro.Busca))
        {
            var like = QueryableExtensions.LikePattern(filtro.Busca);
            q = q.Where(u => EF.Functions.ILike(u.Nome, like) || EF.Functions.ILike(u.Email, like));
        }
        if (filtro.Perfil is { } perfil) q = q.Where(u => u.Perfil == perfil);
        if (filtro.Ativo is { } ativo) q = q.Where(u => u.Ativo == ativo);
        if (filtro.EmpresaId is { } empresaId) q = q.Where(u => u.Empresas.Any(e => e.EmpresaId == empresaId));
        if (filtro.Escritorio == true) q = q.Where(u => u.Perfil != PerfilUsuario.Cliente);
        if (filtro.Departamento is { } departamento) q = q.Where(u => u.Departamentos.Contains(departamento));

        q = (filtro.OrderBy?.ToLowerInvariant(), filtro.Desc) switch
        {
            ("email", false) => q.OrderBy(u => u.Email),
            ("email", true) => q.OrderByDescending(u => u.Email),
            ("createdat", false) => q.OrderBy(u => u.CreatedAt),
            ("createdat", true) => q.OrderByDescending(u => u.CreatedAt),
            (_, true) => q.OrderByDescending(u => u.Nome),
            _ => q.OrderBy(u => u.Nome)
        };
        return q.ToPagedAsync(filtro, ct);
    }
}

public sealed class EmpresaRepository(ContixDbContext context) : Repository<Empresa>(context), IEmpresaRepository
{
    public Task<bool> DocumentoExisteAsync(string documento, Guid? exceto, CancellationToken ct = default) =>
        Set.AnyAsync(e => e.Documento == documento && (exceto == null || e.Id != exceto), ct);

    public Task<int> ContarExistentesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
        Set.CountAsync(e => ids.Contains(e.Id), ct);

    public Task<bool> UsuarioVinculadoAsync(Guid usuarioId, Guid empresaId, CancellationToken ct = default) =>
        Context.UsuarioEmpresas.AnyAsync(v => v.UsuarioId == usuarioId && v.EmpresaId == empresaId, ct);

    public Task<UsuarioEmpresa?> ObterVinculoAsync(Guid usuarioId, Guid empresaId, CancellationToken ct = default) =>
        Context.UsuarioEmpresas.AsNoTracking().FirstOrDefaultAsync(v => v.UsuarioId == usuarioId && v.EmpresaId == empresaId, ct);

    public Task<PagedResult<EmpresaResumoDto>> PesquisarAsync(EmpresaFiltro filtro, Guid? somenteDoUsuario, CancellationToken ct = default)
    {
        var q = Set.AsNoTracking();

        if (somenteDoUsuario is { } usuarioId)
            q = q.Where(e => e.Usuarios.Any(v => v.UsuarioId == usuarioId));

        if (!string.IsNullOrWhiteSpace(filtro.Busca))
        {
            var like = QueryableExtensions.LikePattern(filtro.Busca);
            var digitos = DocumentoFiscal.SomenteDigitos(filtro.Busca);
            var likeDoc = digitos.Length > 0 ? QueryableExtensions.LikePattern(digitos) : null;
            q = q.Where(e => EF.Functions.ILike(e.RazaoSocial, like)
                             || (e.NomeFantasia != null && EF.Functions.ILike(e.NomeFantasia, like))
                             || (e.Apelido != null && EF.Functions.ILike(e.Apelido, like))
                             || (likeDoc != null && EF.Functions.Like(e.Documento, likeDoc)));
        }
        if (filtro.Regime is { } regime) q = q.Where(e => e.RegimeTributario == regime);
        if (filtro.Situacao is { } situacao) q = q.Where(e => e.Situacao == situacao);

        q = (filtro.OrderBy?.ToLowerInvariant(), filtro.Desc) switch
        {
            ("documento", false) => q.OrderBy(e => e.Documento),
            ("documento", true) => q.OrderByDescending(e => e.Documento),
            ("apelido", false) => q.OrderBy(e => e.Apelido),
            ("apelido", true) => q.OrderByDescending(e => e.Apelido),
            ("createdat", false) => q.OrderBy(e => e.CreatedAt),
            ("createdat", true) => q.OrderByDescending(e => e.CreatedAt),
            (_, true) => q.OrderByDescending(e => e.RazaoSocial),
            _ => q.OrderBy(e => e.RazaoSocial)
        };

        if (somenteDoUsuario is { } cliente)
            return q.Select(e => new EmpresaResumoDto(e.Id, e.RazaoSocial, e.NomeFantasia, e.Apelido, e.Documento,
                    e.RegimeTributario, e.Situacao,
                    e.Usuarios.Where(v => v.UsuarioId == cliente).Select(v => v.Modulos).FirstOrDefault(),
                    new IdentidadeVisualDto(e.CorPrimaria, e.CorSecundaria, e.LogoStorageKey != null,
                        e.LogoStorageKey != null && e.LogoAtualizadoEm != null ? e.LogoAtualizadoEm.Value.Ticks.ToString() : null)))
                .ToPagedAsync(filtro, ct);

        return q.Select(e => new EmpresaResumoDto(e.Id, e.RazaoSocial, e.NomeFantasia, e.Apelido, e.Documento,
                e.RegimeTributario, e.Situacao, null,
                new IdentidadeVisualDto(e.CorPrimaria, e.CorSecundaria, e.LogoStorageKey != null,
                    e.LogoStorageKey != null && e.LogoAtualizadoEm != null ? e.LogoAtualizadoEm.Value.Ticks.ToString() : null)))
            .ToPagedAsync(filtro, ct);
    }
}

public sealed class PastaRepository(ContixDbContext context) : Repository<Pasta>(context), IPastaRepository
{
    public async Task<IReadOnlyList<Pasta>> ListarPorEmpresaAsync(Guid empresaId, CancellationToken ct = default) =>
        await Set.AsNoTracking().Where(p => p.EmpresaId == empresaId).ToListAsync(ct);

    public async Task<bool> PossuiConteudoAsync(Guid pastaId, CancellationToken ct = default) =>
        await Set.AnyAsync(p => p.PastaPaiId == pastaId, ct)
        || await Context.Documentos.AnyAsync(d => d.PastaId == pastaId, ct);

    public Task<bool> NomeExisteAsync(Guid empresaId, Guid? pastaPaiId, string nome, Guid? exceto, CancellationToken ct = default) =>
        Set.AnyAsync(p => p.EmpresaId == empresaId && p.PastaPaiId == pastaPaiId
                          && p.Nome.ToLower() == nome.ToLower() && (exceto == null || p.Id != exceto), ct);
}

public sealed class DocumentoRepository(ContixDbContext context) : Repository<Documento>(context), IDocumentoRepository
{
    public Task<PagedResult<DocumentoDto>> PesquisarAsync(Guid empresaId, DocumentoFiltro filtro, Guid usuarioId,
        IReadOnlyCollection<Guid>? pastasPermitidas = null, CancellationToken ct = default)
    {
        var q = Set.AsNoTracking().Where(d => d.EmpresaId == empresaId);
        if (pastasPermitidas is not null) q = q.Where(d => pastasPermitidas.Contains(d.PastaId));

        if (filtro.PastaId is { } pastaId) q = q.Where(d => d.PastaId == pastaId);
        if (!string.IsNullOrWhiteSpace(filtro.Busca))
        {
            var like = QueryableExtensions.LikePattern(filtro.Busca);
            q = q.Where(d => EF.Functions.ILike(d.Nome, like) || (d.Comentario != null && EF.Functions.ILike(d.Comentario, like)));
        }
        q = filtro.Status switch
        {
            StatusLeitura.Lidos => q.Where(d => d.Leituras.Any(l => l.UsuarioId == usuarioId)),
            StatusLeitura.NaoLidos => q.Where(d => !d.Leituras.Any(l => l.UsuarioId == usuarioId)),
            _ => q
        };
        if (filtro.Competencia is { } c)
        {
            var mes = new DateOnly(c.Year, c.Month, 1);
            q = q.Where(d => d.Competencia == mes);
        }
        if (filtro.Origem is { } origem) q = q.Where(d => d.Origem == origem);

        q = (filtro.OrderBy?.ToLowerInvariant(), filtro.Desc) switch
        {
            ("nome", false) => q.OrderBy(d => d.Nome),
            ("competencia", false) => q.OrderBy(d => d.Competencia),
            ("competencia", true) => q.OrderByDescending(d => d.Competencia),
            ("nome", true) => q.OrderByDescending(d => d.Nome),
            ("datavencimento", false) => q.OrderBy(d => d.DataVencimento),
            ("datavencimento", true) => q.OrderByDescending(d => d.DataVencimento),
            ("modificado", false) => q.OrderBy(d => d.UpdatedAt ?? d.PublicadoEm),
            ("modificado", true) => q.OrderByDescending(d => d.UpdatedAt ?? d.PublicadoEm),
            ("publicadoem", false) => q.OrderBy(d => d.PublicadoEm),
            _ => q.OrderByDescending(d => d.PublicadoEm)
        };

        return Projetar(q, usuarioId).ToPagedAsync(filtro, ct);
    }

    public Task<DocumentoDto?> ObterDtoAsync(Guid empresaId, Guid documentoId, Guid usuarioId, CancellationToken ct = default) =>
        Projetar(Set.AsNoTracking().Where(d => d.EmpresaId == empresaId && d.Id == documentoId), usuarioId)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<DocumentoDto>> ListarDtosAsync(Guid empresaId, IReadOnlyCollection<Guid> ids, Guid usuarioId, CancellationToken ct = default) =>
        await Projetar(Set.AsNoTracking().Where(d => d.EmpresaId == empresaId && ids.Contains(d.Id)), usuarioId).ToListAsync(ct);

    public async Task<IReadOnlyList<Documento>> ListarPorIdsAsync(Guid empresaId, IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
        await Set.AsNoTracking().Where(d => d.EmpresaId == empresaId && ids.Contains(d.Id)).ToListAsync(ct);

    public async Task<IReadOnlyList<VencimentoDto>> ListarVencimentosAsync(Guid empresaId, DateOnly inicio, DateOnly fim, Guid usuarioId,
        IReadOnlyCollection<Guid>? pastasPermitidas = null, CancellationToken ct = default) =>
        await Set.AsNoTracking()
            .Where(d => d.EmpresaId == empresaId && d.DataVencimento != null && d.DataVencimento >= inicio && d.DataVencimento <= fim)
            .Where(d => pastasPermitidas == null || pastasPermitidas.Contains(d.PastaId))
            .OrderBy(d => d.DataVencimento).ThenBy(d => d.Nome)
            .Select(d => new VencimentoDto(d.Id, d.Nome, d.DataVencimento!.Value, d.PastaId, string.Empty,
                d.Leituras.Any(l => l.UsuarioId == usuarioId)))
            .ToListAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, int>> ContarNaoLidosPorPastaAsync(Guid empresaId, Guid usuarioId, CancellationToken ct = default) =>
        await Set.AsNoTracking()
            .Where(d => d.EmpresaId == empresaId && !d.Leituras.Any(l => l.UsuarioId == usuarioId))
            .GroupBy(d => d.PastaId)
            .Select(g => new { g.Key, Total = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Total, ct);

    public async Task<IReadOnlyList<EventoDocumentoDto>> ListarEventosAsync(Guid documentoId, CancellationToken ct = default) =>
        await Context.DocumentoEventos.AsNoTracking()
            .Where(e => e.DocumentoId == documentoId)
            .OrderByDescending(e => e.OcorridoEm).ThenByDescending(e => e.Id)
            .Select(e => new EventoDocumentoDto(e.Id, e.Tipo, e.OcorridoEm, e.UsuarioId, e.Usuario!.Nome,
                e.Usuario.Perfil != PerfilUsuario.Cliente, e.Detalhe, e.Ip))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<InteracaoUsuarioDocumento>> ListarInteracoesAsync(Guid documentoId, CancellationToken ct = default)
    {
        var leituras = await Context.DocumentoLeituras.AsNoTracking()
            .Where(l => l.DocumentoId == documentoId)
            .Select(l => new { l.UsuarioId, l.LidoEm })
            .ToListAsync(ct);
        var downloads = await Context.DocumentoEventos.AsNoTracking()
            .Where(e => e.DocumentoId == documentoId && e.Tipo == TipoEventoDocumento.Download)
            .GroupBy(e => e.UsuarioId)
            .Select(g => new { UsuarioId = g.Key, Ultimo = g.Max(e => e.OcorridoEm), Total = g.Count() })
            .ToListAsync(ct);

        var ids = leituras.Select(l => l.UsuarioId).Union(downloads.Select(d => d.UsuarioId)).ToList();
        var usuarios = await Context.Usuarios.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.Nome, u.Email, u.Perfil })
            .ToListAsync(ct);

        return usuarios.Select(u =>
        {
            var d = downloads.FirstOrDefault(x => x.UsuarioId == u.Id);
            return new InteracaoUsuarioDocumento(u.Id, u.Nome, u.Email, u.Perfil,
                leituras.FirstOrDefault(l => l.UsuarioId == u.Id)?.LidoEm, d?.Ultimo, d?.Total ?? 0);
        }).ToList();
    }

    public Task RegistrarEventosAsync(IEnumerable<DocumentoEvento> eventos, CancellationToken ct = default) =>
        Context.DocumentoEventos.AddRangeAsync(eventos, ct);

    public async Task MarcarComoLidosAsync(IReadOnlyCollection<Guid> documentoIds, Guid usuarioId, DateTime agora, CancellationToken ct = default)
    {
        var jaLidos = await Context.DocumentoLeituras
            .Where(l => l.UsuarioId == usuarioId && documentoIds.Contains(l.DocumentoId))
            .Select(l => l.DocumentoId)
            .ToListAsync(ct);

        var novos = documentoIds.Except(jaLidos)
            .Select(id => new DocumentoLeitura { DocumentoId = id, UsuarioId = usuarioId, LidoEm = agora });
        await Context.DocumentoLeituras.AddRangeAsync(novos, ct);
    }

    private static IQueryable<DocumentoDto> Projetar(IQueryable<Documento> q, Guid usuarioId) =>
        q.Select(d => new DocumentoDto(
            d.Id, d.EmpresaId, d.PastaId, d.Nome, d.NomeArquivoOriginal, d.ContentType, d.TamanhoBytes,
            d.Comentario, d.Competencia, d.DataVencimento, d.PublicadoEm, d.AdicionadoPorId,
            d.AdicionadoPor!.Nome, d.UpdatedAt,
            d.Leituras.Any(l => l.UsuarioId == usuarioId),
            d.Leituras.Where(l => l.UsuarioId == usuarioId).Select(l => (DateTime?)l.LidoEm).FirstOrDefault(),
            d.Origem,
            d.Leituras.Count(l => (l.Usuario!.Perfil == PerfilUsuario.Cliente) == (d.Origem == OrigemDocumento.Escritorio))));
}
