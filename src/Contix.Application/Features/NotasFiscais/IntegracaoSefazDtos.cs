using Contix.Domain.Entities;

namespace Contix.Application.Features.NotasFiscais;

public sealed record CertificadoDigitalDto(
    Guid Id, string NomeArquivo, string Titular, string? Cnpj, string Emissor, DateTime ValidoDe, DateTime ValidoAte,
    bool Vencido, int DiasParaVencer, DateTime EnviadoEm, string? EnviadoPorNome);

/// <summary>Integração da empresa com a SEFAZ: certificado, busca automática e situação da última consulta.</summary>
/// <param name="IntegracaoHabilitada">false: o ambiente não tem integração com a SEFAZ configurada.</param>
/// <param name="Pendencias">O que falta para ligar a busca automática (vazio: pode ligar).</param>
public sealed record IntegracaoSefazDto(
    bool IntegracaoHabilitada, string? Provedor, string? Ambiente,
    CertificadoDigitalDto? Certificado,
    bool Ativa, bool CienciaAutomatica,
    long UltimoNsu, long MaxNsu, DateTime? UltimaConsultaEm, DateTime? ProximaConsultaEm,
    string? UltimoStatus, string? UltimaMensagem, bool UltimaConsultaFalhou, int DocumentosRecebidos,
    IReadOnlyList<string> Pendencias);

public sealed record SalvarIntegracaoSefazCommand(bool Ativa, bool CienciaAutomatica);

public sealed record EnviarCertificadoCommand(string NomeArquivo, long Tamanho, Func<Stream> AbrirLeitura, string? Senha);

public interface IIntegracaoSefazRepository
{
    Task<CertificadoDigitalDto?> ObterCertificadoDtoAsync(Guid empresaId, DateTime agora, CancellationToken ct = default);
    Task<CertificadoDigital?> ObterCertificadoAsync(Guid empresaId, bool ignorarTenant, CancellationToken ct = default);
    Task AdicionarCertificadoAsync(CertificadoDigital certificado, CancellationToken ct = default);
    void RemoverCertificado(CertificadoDigital certificado);

    Task<SincronizacaoNFe?> ObterSincronizacaoAsync(Guid empresaId, CancellationToken ct = default);
    Task AdicionarSincronizacaoAsync(SincronizacaoNFe sincronizacao, CancellationToken ct = default);

    /// <summary>Job: ids das buscas ativas cuja hora chegou, de empresas e escritórios ativos.</summary>
    Task<IReadOnlyList<Guid>> ListarProntasAsync(DateTime agora, int limite, CancellationToken ct = default);
    /// <summary>Job: a busca rastreada e com a empresa carregada, fora do filtro de tenant.</summary>
    Task<SincronizacaoNFe?> ObterParaProcessarAsync(Guid sincronizacaoId, CancellationToken ct = default);
    /// <summary>Job: resumos vindos da SEFAZ, autorizados e ainda sem manifestação, emitidos a partir de <paramref name="emitidasDesde"/>
    /// (as mais recentes primeiro, para uma recusa antiga não travar as novas), para dar a ciência automática que ficou pendente.</summary>
    Task<IReadOnlyList<NotaFiscal>> ListarPendentesCienciaAsync(Guid empresaId, DateTime emitidasDesde, int limite, CancellationToken ct = default);
}
