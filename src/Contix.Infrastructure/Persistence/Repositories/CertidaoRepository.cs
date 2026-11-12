using Contix.Application.Features.Certidoes;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Contix.Infrastructure.Persistence.Repositories;

public sealed class CertidaoRepository(ContixDbContext context) : Repository<Certidao>(context), ICertidaoRepository
{
    public async Task<IReadOnlyList<CertidaoDto>> ListarDaEmpresaAsync(Guid empresaId, CertidaoFiltro filtro, DateOnly hoje, CancellationToken ct = default)
    {
        var q = Completas().Where(c => c.EmpresaId == empresaId);
        if (filtro.Tipo is { } tipo) q = q.Where(c => c.Tipo == tipo);
        var todas = await q.ToListAsync(ct);

        var atuais = todas.GroupBy(c => c.Tipo).Select(g => MaisRecente(g).Id).ToHashSet();
        return todas
            .Where(c => !filtro.SomenteAtuais || atuais.Contains(c.Id))
            .OrderBy(c => c.Tipo).ThenByDescending(c => c.Validade).ThenByDescending(c => c.Emissao)
            .Select(c => ToDto(c, atuais.Contains(c.Id), hoje))
            .ToList();
    }

    public async Task<CertidaoDto?> ObterDtoAsync(Guid empresaId, Guid certidaoId, DateOnly hoje, CancellationToken ct = default)
    {
        var certidao = await Completas().FirstOrDefaultAsync(c => c.Id == certidaoId && c.EmpresaId == empresaId, ct);
        if (certidao is null) return null;
        var mesmoTipo = await Set.AsNoTracking().Where(c => c.EmpresaId == empresaId && c.Tipo == certidao.Tipo).ToListAsync(ct);
        return ToDto(certidao, MaisRecente(mesmoTipo).Id == certidao.Id, hoje);
    }

    public async Task<IReadOnlyList<CertidaoDto>> ListarAlertasAsync(DateOnly hoje, DateOnly ate, CancellationToken ct = default)
    {
        // Candidatas: vencendo até a data ou positivas. Só interessam as atuais (a última de cada tipo por empresa):
        // uma certidão vencida que já foi renovada não é alerta.
        var candidatas = await Completas()
            .Where(c => c.Validade <= ate || c.Situacao == SituacaoCertidao.Positiva)
            .Where(c => !Set.Any(o => o.EmpresaId == c.EmpresaId && o.Tipo == c.Tipo && o.Id != c.Id
                                      && (o.Validade > c.Validade
                                          || (o.Validade == c.Validade && o.Emissao > c.Emissao)
                                          || (o.Validade == c.Validade && o.Emissao == c.Emissao && o.CreatedAt > c.CreatedAt))))
            .ToListAsync(ct);

        return candidatas
            .OrderBy(c => c.Validade).ThenBy(c => c.Empresa!.NomeFantasia ?? c.Empresa!.RazaoSocial)
            .Select(c => ToDto(c, true, hoje))
            .ToList();
    }

    private IQueryable<Certidao> Completas() =>
        Set.AsNoTracking().Include(c => c.Empresa).Include(c => c.CadastradoPor);

    private static Certidao MaisRecente(IEnumerable<Certidao> certidoes) =>
        certidoes.OrderByDescending(c => c.Validade).ThenByDescending(c => c.Emissao).ThenByDescending(c => c.CreatedAt).First();

    private static CertidaoDto ToDto(Certidao c, bool atual, DateOnly hoje) => new(
        c.Id, c.EmpresaId, c.Empresa?.NomeFantasia ?? c.Empresa?.RazaoSocial ?? "", c.Tipo, CatalogoCertidoes.Nome(c.Tipo),
        c.Situacao, c.Numero, c.Emissao, c.Validade, c.CalcularValidade(hoje), c.Validade.DayNumber - hoje.DayNumber,
        Certidao.EhRegular(c.Situacao, c.Validade, hoje), atual, c.Observacao, c.PossuiArquivo, c.NomeArquivo, c.TamanhoBytes,
        c.CadastradoPor?.Nome ?? "", c.CreatedAt, c.UpdatedAt);
}
