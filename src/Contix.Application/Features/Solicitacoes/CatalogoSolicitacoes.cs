using Contix.Domain.Enums;

namespace Contix.Application.Features.Solicitacoes;

/// <summary>Tipo de solicitação exibido no catálogo do portal.</summary>
/// <param name="PrazoDias">Prazo padrão de atendimento (dias corridos a partir da abertura).</param>
/// <param name="ExigeAnexo">Se a abertura exige ao menos um arquivo (ex.: documentos pessoais, atestado).</param>
public sealed record TipoSolicitacaoDto(
    TipoSolicitacao Codigo, string Nome, string Descricao, Departamento Departamento,
    int PrazoDias, bool ExigeAnexo, string? OrientacaoAnexo);

public static class CatalogoSolicitacoes
{
    public static readonly IReadOnlyList<TipoSolicitacaoDto> Tipos =
    [
        new(TipoSolicitacao.Geral, "Solicitação Geral",
            "Peça qualquer serviço ao escritório que não esteja no catálogo.", Departamento.Contabil, 5, false, null),
        new(TipoSolicitacao.CadastroEmpregado, "Cadastro de Empregado",
            "Envie os dados de admissão de um novo empregado.", Departamento.Pessoal, 3, true,
            "Anexe RG, CPF, CTPS, comprovante de residência e exame admissional."),
        new(TipoSolicitacao.CadastroContribuinte, "Cadastro de Contribuinte",
            "Cadastre sócios (pró-labore) ou autônomos para recolhimento.", Departamento.Pessoal, 3, true,
            "Anexe RG, CPF e comprovante de residência."),
        new(TipoSolicitacao.CadastroEstagiario, "Cadastro de Estagiário",
            "Envie os dados para o termo de compromisso de estágio.", Departamento.Pessoal, 3, true,
            "Anexe RG, CPF, comprovante de matrícula e termo de compromisso, se houver."),
        new(TipoSolicitacao.AvisoFerias, "Aviso Prévio de Férias",
            "Informe as férias de um empregado para emissão do aviso.", Departamento.Pessoal, 5, false, null),
        new(TipoSolicitacao.CalculoFerias, "Cálculo de Férias",
            "Solicite o cálculo e o recibo de férias de um empregado.", Departamento.Pessoal, 3, false, null),
        new(TipoSolicitacao.AvisoRescisao, "Aviso Prévio de Rescisão",
            "Informe o desligamento de um empregado para emissão do aviso prévio.", Departamento.Pessoal, 2, false, null),
        new(TipoSolicitacao.CalculoRescisao, "Cálculo de Rescisão",
            "Solicite o cálculo das verbas rescisórias de um empregado.", Departamento.Pessoal, 3, false, null),
        new(TipoSolicitacao.Afastamento, "Afastamento de Empregado",
            "Informe afastamentos por doença, acidente, licenças e outros.", Departamento.Pessoal, 2, true,
            "Anexe o atestado ou documento que comprove o afastamento."),
        new(TipoSolicitacao.LancamentoRubricas, "Lançamento de Rubricas",
            "Informe horas extras, faltas, comissões e outros eventos da folha.", Departamento.Pessoal, 5, false, null),
    ];

    private static readonly Dictionary<TipoSolicitacao, TipoSolicitacaoDto> PorCodigo = Tipos.ToDictionary(t => t.Codigo);

    public static TipoSolicitacaoDto Obter(TipoSolicitacao tipo) =>
        PorCodigo.TryGetValue(tipo, out var t) ? t : throw new ArgumentOutOfRangeException(nameof(tipo), tipo, null);

    public static string NomeDepartamento(Departamento d) => d switch
    {
        Departamento.Contabil => "Contábil",
        Departamento.Societario => "Societário",
        _ => d.ToString()
    };
}
