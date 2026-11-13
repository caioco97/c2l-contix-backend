using Contix.Application.Features.Solicitacoes;
using Contix.Domain.Entities;
using Contix.Domain.Enums;

namespace Contix.Application.Features.Empregados;

/// <summary>Referência à solicitação de onde a informação da ficha veio.</summary>
public sealed record OrigemFichaDto(Guid SolicitacaoId, int Numero, TipoSolicitacao Tipo, StatusSolicitacao Status, DateTime CreatedAt);

/// <param name="Fim">Último dia de gozo (início + dias - 1).</param>
public sealed record FeriasFichaDto(
    OrigemFichaDto Origem, DateOnly Inicio, DateOnly Fim, int Dias,
    DateOnly PeriodoAquisitivoInicio, DateOnly PeriodoAquisitivoFim, bool AbonoPecuniario, bool Adiantamento13);

public sealed record AfastamentoFichaDto(
    OrigemFichaDto Origem, MotivoAfastamento Motivo, DateOnly Inicio, DateOnly? PrevisaoRetorno, string? Observacoes);

public sealed record DesligamentoFichaDto(
    OrigemFichaDto Origem, TipoDesligamento TipoDesligamento, DateOnly DataDesligamento, TipoAvisoPrevio AvisoPrevio);

public sealed record RubricaFichaDto(OrigemFichaDto Origem, DateOnly Competencia, string Rubrica, decimal? Quantidade, decimal? Valor);

/// <summary>O que vale hoje, considerando as solicitações não canceladas.</summary>
public sealed record SituacaoHojeDto(FeriasFichaDto? FeriasEmCurso, FeriasFichaDto? ProximasFerias, AfastamentoFichaDto? AfastamentoEmCurso);

/// <summary>Ficha do empregado: cadastro e o histórico do departamento pessoal a partir das solicitações.</summary>
public sealed record FichaEmpregadoDto(
    EmpregadoDto Empregado, SituacaoHojeDto Hoje,
    IReadOnlyList<FeriasFichaDto> Ferias, IReadOnlyList<AfastamentoFichaDto> Afastamentos,
    IReadOnlyList<DesligamentoFichaDto> Desligamentos, IReadOnlyList<RubricaFichaDto> Rubricas,
    int TotalSolicitacoes);

public static class FichaEmpregado
{
    public static FichaEmpregadoDto Montar(Empregado empregado, IReadOnlyList<Solicitacao> solicitacoes, DateOnly hoje)
    {
        var ferias = new List<FeriasFichaDto>();
        var afastamentos = new List<AfastamentoFichaDto>();
        var desligamentos = new List<DesligamentoFichaDto>();
        var rubricas = new List<RubricaFichaDto>();

        foreach (var s in solicitacoes.OrderByDescending(s => s.CreatedAt))
        {
            var origem = new OrigemFichaDto(s.Id, s.Numero, s.Tipo, s.Status, s.CreatedAt);
            switch (s.Tipo)
            {
                case TipoSolicitacao.AvisoFerias or TipoSolicitacao.CalculoFerias
                    when FormulariosSolicitacao.Ler<DadosFerias>(s.Dados) is { } f && f.DataInicio != default && f.Dias > 0:
                    ferias.Add(new FeriasFichaDto(origem, f.DataInicio, f.DataInicio.AddDays(f.Dias - 1), f.Dias,
                        f.PeriodoAquisitivoInicio, f.PeriodoAquisitivoFim, f.AbonoPecuniario, f.Adiantamento13));
                    break;
                case TipoSolicitacao.Afastamento
                    when FormulariosSolicitacao.Ler<DadosAfastamento>(s.Dados) is { } a && a.DataInicio != default:
                    afastamentos.Add(new AfastamentoFichaDto(origem, a.Motivo, a.DataInicio, a.PrevisaoRetorno, a.Observacoes));
                    break;
                case TipoSolicitacao.AvisoRescisao or TipoSolicitacao.CalculoRescisao
                    when FormulariosSolicitacao.Ler<DadosRescisao>(s.Dados) is { } r && r.DataDesligamento != default:
                    desligamentos.Add(new DesligamentoFichaDto(origem, r.TipoDesligamento, r.DataDesligamento, r.AvisoPrevio));
                    break;
                case TipoSolicitacao.LancamentoRubricas
                    when FormulariosSolicitacao.Ler<DadosRubricas>(s.Dados) is { } d:
                    rubricas.AddRange(d.Lancamentos.Where(l => l.EmpregadoId == empregado.Id)
                        .Select(l => new RubricaFichaDto(origem, d.Competencia, l.Rubrica, l.Quantidade, l.Valor)));
                    break;
            }
        }

        static bool Vale(OrigemFichaDto o) => o.Status != StatusSolicitacao.Cancelada;
        var hojeDto = new SituacaoHojeDto(
            ferias.Where(f => Vale(f.Origem) && f.Inicio <= hoje && f.Fim >= hoje).MinBy(f => f.Inicio),
            ferias.Where(f => Vale(f.Origem) && f.Inicio > hoje).MinBy(f => f.Inicio),
            afastamentos.Where(a => Vale(a.Origem) && a.Inicio <= hoje && (a.PrevisaoRetorno is null || a.PrevisaoRetorno >= hoje))
                .MaxBy(a => a.Inicio));

        return new FichaEmpregadoDto(
            EmpregadoDto.From(empregado), hojeDto,
            ferias.OrderByDescending(f => f.Inicio).ToList(),
            afastamentos.OrderByDescending(a => a.Inicio).ToList(),
            desligamentos.OrderByDescending(d => d.DataDesligamento).ToList(),
            rubricas.OrderByDescending(r => r.Competencia).ThenBy(r => r.Rubrica).ToList(),
            solicitacoes.Count);
    }
}
