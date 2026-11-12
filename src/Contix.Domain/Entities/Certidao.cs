using Contix.Domain.Common;
using Contix.Domain.Enums;
using Contix.Domain.Exceptions;

namespace Contix.Domain.Entities;

/// <summary>
/// Certidão (CND) de uma empresa. O histórico é mantido: a certidão "atual" de cada tipo
/// é a de validade mais recente.
/// </summary>
public class Certidao : TenantEntity
{
    /// <summary>Antecedência com que uma certidão passa a ser considerada "a vencer".</summary>
    public const int DiasAlertaVencimento = 30;

    public Guid EmpresaId { get; set; }
    public Empresa? Empresa { get; set; }

    public TipoCertidao Tipo { get; set; }
    public SituacaoCertidao Situacao { get; set; }
    /// <summary>Número/código de controle da certidão, quando houver.</summary>
    public string? Numero { get; set; }
    public DateOnly Emissao { get; private set; }
    public DateOnly Validade { get; private set; }
    public string? Observacao { get; set; }

    public string? NomeArquivo { get; set; }
    public string? ContentType { get; set; }
    public long? TamanhoBytes { get; set; }
    public string? StorageKey { get; set; }

    public Guid CadastradoPorId { get; set; }
    public Usuario? CadastradoPor { get; set; }

    public bool PossuiArquivo => StorageKey is not null;

    public void DefinirDatas(DateOnly emissao, DateOnly validade)
    {
        if (validade < emissao) throw new DomainException("A validade não pode ser anterior à emissão.");
        Emissao = emissao;
        Validade = validade;
    }

    public ValidadeCertidao CalcularValidade(DateOnly hoje) => Calcular(Validade, hoje);

    public static ValidadeCertidao Calcular(DateOnly validade, DateOnly hoje) =>
        validade < hoje ? ValidadeCertidao.Vencida
        : validade.DayNumber - hoje.DayNumber <= DiasAlertaVencimento ? ValidadeCertidao.AVencer
        : ValidadeCertidao.Vigente;

    /// <summary>Regular = vigente (ou a vencer) e sem débitos exigíveis.</summary>
    public static bool EhRegular(SituacaoCertidao situacao, DateOnly validade, DateOnly hoje) =>
        situacao != SituacaoCertidao.Positiva && validade >= hoje;
}
