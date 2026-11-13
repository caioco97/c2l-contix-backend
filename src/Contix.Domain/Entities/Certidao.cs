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

    /* Avisos de vencimento já enviados (um por marco). Zerados quando a validade muda. */
    public DateTime? AvisoVencimento30Em { get; private set; }
    public DateTime? AvisoVencimento15Em { get; private set; }
    public DateTime? AvisoVencidaEm { get; private set; }

    public Guid CadastradoPorId { get; set; }
    public Usuario? CadastradoPor { get; set; }

    public bool PossuiArquivo => StorageKey is not null;

    public void DefinirDatas(DateOnly emissao, DateOnly validade)
    {
        if (validade < emissao) throw new DomainException("A validade não pode ser anterior à emissão.");
        if (validade != Validade)
        {
            AvisoVencimento30Em = null;
            AvisoVencimento15Em = null;
            AvisoVencidaEm = null;
        }
        Emissao = emissao;
        Validade = validade;
    }

    /// <summary>Marco de aviso devido hoje, ou null. Certidões vencidas há mais de <paramref name="diasVencidaMax"/> dias não geram aviso.</summary>
    public static MarcoAvisoCertidao? MarcoDevido(DateOnly validade, DateOnly hoje, bool aviso30, bool aviso15, bool avisoVencida, int diasVencidaMax = 30)
    {
        var dias = validade.DayNumber - hoje.DayNumber;
        if (dias < 0) return !avisoVencida && -dias <= diasVencidaMax ? MarcoAvisoCertidao.Vencida : null;
        if (dias <= 15) return aviso15 ? null : MarcoAvisoCertidao.Dias15;
        if (dias <= 30) return aviso30 ? null : MarcoAvisoCertidao.Dias30;
        return null;
    }

    /// <summary>Registra o aviso do marco (e dos anteriores, que não fazem mais sentido).</summary>
    public void RegistrarAviso(MarcoAvisoCertidao marco, DateTime agora)
    {
        AvisoVencimento30Em ??= agora;
        if (marco is MarcoAvisoCertidao.Dias15 or MarcoAvisoCertidao.Vencida) AvisoVencimento15Em ??= agora;
        if (marco == MarcoAvisoCertidao.Vencida) AvisoVencidaEm ??= agora;
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

public enum MarcoAvisoCertidao { Dias30 = 30, Dias15 = 15, Vencida = 0 }
