namespace Contix.Infrastructure.NotasFiscais;

public sealed class SefazOptions
{
    public const string Secao = "NFe:Sefaz";

    /// <summary>Desabilitado (padrão), Simulado (desenvolvimento) ou Sefaz.</summary>
    public string Provider { get; set; } = "Desabilitado";
    /// <summary>Homologacao ou Producao (tpAmb 2/1).</summary>
    public string Ambiente { get; set; } = "Homologacao";
    /// <summary>Liga o job da busca automática.</summary>
    public bool ProcessarFila { get; set; } = true;
    /// <summary>De quanto em quanto tempo o job procura buscas cuja hora chegou.</summary>
    public int IntervaloMinutos { get; set; } = 5;
    public int TimeoutSegundos { get; set; } = 60;

    /// <summary>
    /// Pasta com os certificados (.crt/.cer/.pem) das ACs raiz e intermediárias da ICP-Brasil. Os servidores da SEFAZ usam
    /// certificados ICP-Brasil, que não vêm no repositório de confiança padrão do Linux. Vazio: usa só o repositório do sistema.
    /// </summary>
    public string? CadeiaIcpBrasil { get; set; }

    public SefazUrls Producao { get; set; } = new()
    {
        DistribuicaoDfe = "https://www1.nfe.fazenda.gov.br/NFeDistribuicaoDFe/NFeDistribuicaoDFe.asmx",
        RecepcaoEvento = "https://www.nfe.fazenda.gov.br/NFeRecepcaoEvento4/NFeRecepcaoEvento4.asmx"
    };

    public SefazUrls Homologacao { get; set; } = new()
    {
        DistribuicaoDfe = "https://hom1.nfe.fazenda.gov.br/NFeDistribuicaoDFe/NFeDistribuicaoDFe.asmx",
        RecepcaoEvento = "https://hom1.nfe.fazenda.gov.br/NFeRecepcaoEvento4/NFeRecepcaoEvento4.asmx"
    };

    public bool EhProducao => string.Equals(Ambiente, "Producao", StringComparison.OrdinalIgnoreCase);
    public SefazUrls Urls => EhProducao ? Producao : Homologacao;
}

public sealed class SefazUrls
{
    public string DistribuicaoDfe { get; set; } = "";
    /// <summary>Recepção de evento do Ambiente Nacional (cOrgao 91), usada na manifestação do destinatário.</summary>
    public string RecepcaoEvento { get; set; } = "";
}
