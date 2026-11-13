using Contix.Domain.Common;
using Contix.Domain.Enums;
using Contix.Domain.Exceptions;

namespace Contix.Domain.Entities;

/// <summary>
/// Agendamento da emissão automática de certidões de uma empresa: quais tipos emitir,
/// com quantos dias de antecedência renovar e quem avisar quando der errado.
/// </summary>
public class ConfiguracaoEmissaoCertidao : TenantEntity
{
    public const int AntecedenciaPadrao = 10;
    public const int AntecedenciaMaxima = 60;

    public Guid EmpresaId { get; set; }
    public Empresa? Empresa { get; set; }

    public bool Ativa { get; set; }
    public List<TipoCertidao> Tipos { get; set; } = [];
    /// <summary>Renova quando faltam até estes dias para a certidão atual vencer.</summary>
    public int DiasAntecedencia { get; private set; } = AntecedenciaPadrao;

    /// <summary>Colaborador avisado das falhas. Nulo: toda a equipe do escritório.</summary>
    public Guid? ResponsavelId { get; set; }
    public Usuario? Responsavel { get; set; }

    public void DefinirAntecedencia(int dias)
    {
        if (dias is < 0 or > AntecedenciaMaxima)
            throw new DomainException($"A antecedência deve ficar entre 0 e {AntecedenciaMaxima} dias.");
        DiasAntecedencia = dias;
    }

    /// <summary>
    /// A certidão do tipo precisa ser emitida hoje? Sim quando não há certidão, quando a atual já entrou na
    /// janela de antecedência, ou quando a atual é positiva (vale tentar de novo: a pendência pode ter sido regularizada).
    /// </summary>
    public bool PrecisaEmitir(DateOnly? validadeAtual, SituacaoCertidao? situacaoAtual, DateOnly hoje) =>
        validadeAtual is null
        || validadeAtual.Value.DayNumber - hoje.DayNumber <= DiasAntecedencia
        || situacaoAtual == SituacaoCertidao.Positiva;
}

/// <summary>Uma tentativa (com retentativas) de emitir uma certidão pela integração. Fica como histórico.</summary>
public class EmissaoCertidao : TenantEntity
{
    public const int MaxTentativas = 3;
    /// <summary>Espera antes da 2ª e da 3ª tentativa.</summary>
    public static readonly TimeSpan[] Esperas = [TimeSpan.FromMinutes(30), TimeSpan.FromHours(2)];

    public Guid EmpresaId { get; set; }
    public Empresa? Empresa { get; set; }

    public TipoCertidao Tipo { get; set; }
    public OrigemEmissaoCertidao Origem { get; set; }
    public StatusEmissaoCertidao Status { get; private set; } = StatusEmissaoCertidao.Pendente;
    public int Tentativas { get; private set; }
    /// <summary>Quando pode ser processada (agora, ou a hora da próxima tentativa).</summary>
    public DateTime ProximaTentativaEm { get; set; }
    public DateTime? ConcluidaEm { get; private set; }
    /// <summary>Integrador usado na última tentativa.</summary>
    public string? Provedor { get; private set; }
    /// <summary>Retorno do órgão/integrador (erro ou observação).</summary>
    public string? Mensagem { get; private set; }

    /// <summary>Certidão gerada quando a emissão deu certo.</summary>
    public Guid? CertidaoId { get; private set; }
    public Certidao? Certidao { get; set; }

    /// <summary>Quem pediu (emissões manuais).</summary>
    public Guid? SolicitadoPorId { get; set; }
    public Usuario? SolicitadoPor { get; set; }

    public bool EmAberto => Status == StatusEmissaoCertidao.Pendente;

    public void RegistrarSucesso(Guid certidaoId, string provedor, string? mensagem, DateTime agora)
    {
        GarantirEmAberto();
        Tentativas++;
        Status = StatusEmissaoCertidao.Emitida;
        CertidaoId = certidaoId;
        Provedor = provedor;
        Mensagem = Limitar(mensagem);
        ConcluidaEm = agora;
    }

    /// <summary>
    /// Registra a falha da tentativa. Se ainda couber (e o erro for temporário), volta para a fila com espera;
    /// senão vira falha definitiva.
    /// </summary>
    /// <returns>true quando a falha é definitiva (hora de avisar o responsável).</returns>
    public bool RegistrarFalha(string provedor, string mensagem, bool retentavel, DateTime agora)
    {
        GarantirEmAberto();
        Tentativas++;
        Provedor = provedor;
        Mensagem = Limitar(mensagem);
        if (retentavel && Tentativas < MaxTentativas)
        {
            ProximaTentativaEm = agora + Esperas[Math.Min(Tentativas, Esperas.Length) - 1];
            return false;
        }
        Status = StatusEmissaoCertidao.Falha;
        ConcluidaEm = agora;
        return true;
    }

    private void GarantirEmAberto()
    {
        if (!EmAberto) throw new DomainException("A emissão já foi concluída.");
    }

    private static string? Limitar(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? null : texto.Trim() is { Length: > 1000 } t ? t[..1000] : texto.Trim();
}
