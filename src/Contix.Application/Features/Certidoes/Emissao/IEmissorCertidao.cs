using Contix.Domain.Enums;

namespace Contix.Application.Features.Certidoes.Emissao;

/// <summary>Dados da empresa enviados ao órgão/integrador.</summary>
/// <param name="Documento">CNPJ ou CPF, só dígitos.</param>
public sealed record DadosEmpresaEmissao(
    string Documento, string RazaoSocial, string? Uf, string? Municipio, string? InscricaoEstadual, string? InscricaoMunicipal)
{
    public bool EhCnpj => Documento.Length == 14;
}

/// <summary>Certidão devolvida pelo órgão, já com o PDF.</summary>
public sealed record CertidaoEmitida(
    SituacaoCertidao Situacao, string? Numero, DateOnly Emissao, DateOnly Validade,
    byte[] Pdf, string NomeArquivo, string? Observacao = null);

/// <summary>Resultado de uma chamada ao emissor.</summary>
/// <param name="Retentavel">Falha temporária (fonte fora do ar, timeout): vale tentar de novo mais tarde.</param>
public sealed record ResultadoEmissao(CertidaoEmitida? Certidao, string? Erro, bool Retentavel)
{
    public bool Sucesso => Certidao is not null;
    public static ResultadoEmissao Ok(CertidaoEmitida certidao) => new(certidao, null, false);
    public static ResultadoEmissao Falha(string erro, bool retentavel) => new(null, erro, retentavel);
}

/// <summary>
/// Emite certidões nos órgãos. Os portais oficiais (RFB/PGFN, Caixa, TST, SEFAZ, prefeituras) não têm API pública
/// para empresas e usam captcha, por isso a emissão passa por um integrador contratado (ex.: Infosimples).
/// </summary>
public interface IEmissorCertidao
{
    /// <summary>Nome do provedor, gravado no histórico.</summary>
    string Nome { get; }
    /// <summary>false quando a emissão automática não está configurada neste ambiente.</summary>
    bool Habilitado { get; }
    IReadOnlyCollection<TipoCertidao> TiposSuportados { get; }
    /// <summary>Não lança para falhas do órgão: devolve <see cref="ResultadoEmissao.Falha"/>.</summary>
    Task<ResultadoEmissao> EmitirAsync(TipoCertidao tipo, DadosEmpresaEmissao empresa, CancellationToken ct = default);
}
