using System.Security.Cryptography;
using System.Security.Cryptography.Xml;
using System.Text.Json;
using System.Xml;
using Contix.Application.Features.NotasFiscais;
using Contix.Application.Features.NotasFiscais.Sefaz;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using Contix.Domain.Exceptions;
using Contix.Infrastructure.Cnpj;
using Contix.Infrastructure.NotasFiscais;
using Contix.Infrastructure.Security;
using Contix.Tests.Support;
using Microsoft.Extensions.Options;

namespace Contix.UnitTests.NotasFiscais;

public class LeitorXmlNFeTests
{
    [Fact]
    public void Le_nfeProc_completa()
    {
        var emit = CertificadoTeste.NovoCnpj();
        var dest = CertificadoTeste.NovoCnpj();
        var chave = CertificadoTeste.Chave(emit, 4321);

        var lido = Assert.IsType<NotaLida>(LeitorXmlNFe.Ler(CertificadoTeste.NfeProc(chave, emit, dest, 1234.5m)));

        Assert.True(lido.Completa);
        Assert.Equal(chave, lido.Dados.ChaveAcesso);
        Assert.Equal(emit, lido.Dados.EmitenteDocumento);
        Assert.Equal(dest, lido.Dados.DestinatarioDocumento);
        Assert.Equal(1234.5m, lido.Dados.ValorTotal);
        Assert.Equal(new DateTime(2026, 10, 5, 13, 30, 0, DateTimeKind.Utc), lido.Dados.DataEmissaoUtc);
        Assert.Equal("135260000012345", lido.Dados.Protocolo);
        Assert.Equal(SituacaoNFe.Autorizada, lido.Dados.Situacao);
    }

    [Fact]
    public void Le_resumo_e_evento_de_cancelamento_da_distribuicao()
    {
        const string ns = "http://www.portalfiscal.inf.br/nfe";
        var chave = CertificadoTeste.Chave(CertificadoTeste.NovoCnpj(), 7);
        var res = $"<resNFe xmlns=\"{ns}\" versao=\"1.01\"><chNFe>{chave}</chNFe><CNPJ>11222333000181</CNPJ><xNome>Fornecedor</xNome>"
                  + "<dhEmi>2026-11-01T08:00:00-03:00</dhEmi><tpNF>1</tpNF><vNF>10.00</vNF><nProt>1</nProt><cSitNFe>3</cSitNFe></resNFe>";
        var resumo = Assert.IsType<NotaLida>(LeitorXmlNFe.Ler(res));
        Assert.False(resumo.Completa);
        Assert.Null(resumo.Dados.DestinatarioDocumento);
        Assert.Equal(SituacaoNFe.Cancelada, resumo.Dados.Situacao);

        var evento = Assert.IsType<EventoLido>(LeitorXmlNFe.Ler(
            $"<resEvento xmlns=\"{ns}\" versao=\"1.01\"><chNFe>{chave}</chNFe><tpEvento>110111</tpEvento></resEvento>"));
        Assert.Equal(LeitorXmlNFe.EventoCancelamento, evento.TipoEvento);
    }

    [Theory]
    [InlineData("não é xml", "XML válido")]
    [InlineData("<pedido><id>1</id></pedido>", "não é de uma NF-e")]
    [InlineData("<!DOCTYPE x [<!ENTITY a \"b\">]><nfeProc/>", "XML válido")]
    public void Recusa_arquivo_que_nao_e_nfe(string xml, string mensagem)
    {
        var ex = Assert.Throws<FormatException>(() => LeitorXmlNFe.Ler(xml));
        Assert.Contains(mensagem, ex.Message);
    }
}

public class NotaFiscalDominioTests
{
    private static readonly DateTime Agora = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);

    private static NotaFiscal Nota()
    {
        var n = new NotaFiscal { ChaveAcesso = CertificadoTeste.Chave("11222333000181", 98765) };
        n.PreencherDaChave();
        return n;
    }

    [Fact]
    public void Extrai_modelo_serie_e_numero_da_chave()
    {
        var n = Nota();
        Assert.Equal(55, n.Modelo);
        Assert.Equal(1, n.Serie);
        Assert.Equal(98765, n.Numero);
    }

    [Fact]
    public void Ciencia_admite_manifestacao_conclusiva_depois_e_conclusiva_e_final()
    {
        var n = Nota();
        n.Manifestar(ManifestacaoNFe.Ciencia, null, null, "1", Agora);
        Assert.Throws<DomainException>(() => n.Manifestar(ManifestacaoNFe.Ciencia, null, null, null, Agora));

        n.Manifestar(ManifestacaoNFe.Confirmada, null, Guid.NewGuid(), "2", Agora);
        Assert.Equal(ManifestacaoNFe.Confirmada, n.Manifestacao);
        Assert.Throws<DomainException>(() => n.Manifestar(ManifestacaoNFe.Desconhecida, null, null, null, Agora));
    }

    [Fact]
    public void Operacao_nao_realizada_exige_justificativa_e_nota_cancelada_nao_e_manifestada()
    {
        var n = Nota();
        Assert.Throws<DomainException>(() => n.Manifestar(ManifestacaoNFe.NaoRealizada, "curta", null, null, Agora));
        n.Manifestar(ManifestacaoNFe.NaoRealizada, "  Mercadoria nunca foi entregue  ", null, null, Agora);
        Assert.Equal("Mercadoria nunca foi entregue", n.JustificativaManifestacao);

        var cancelada = Nota();
        cancelada.Situacao = SituacaoNFe.Cancelada;
        Assert.Throws<DomainException>(() => cancelada.Manifestar(ManifestacaoNFe.Ciencia, null, null, null, Agora));
    }

    [Fact]
    public void Sincronizacao_respeita_espera_da_sefaz()
    {
        var s = new SincronizacaoNFe { Ativa = true };
        s.RegistrarRetorno("138", "Documentos localizados", 50, 120, 50, Agora);
        Assert.Equal(Agora, s.ProximaConsultaEm); // ainda há documentos na fila
        Assert.False(s.EmEsperaObrigatoria(Agora));

        s.RegistrarRetorno("138", "Documentos localizados", 120, 120, 70, Agora);
        Assert.Equal(Agora.AddHours(1), s.ProximaConsultaEm);

        s.RegistrarRetorno("137", "Nenhum documento localizado", 120, 120, 0, Agora);
        Assert.True(s.EmEsperaObrigatoria(Agora.AddMinutes(59)));
        Assert.False(s.EmEsperaObrigatoria(Agora.AddMinutes(61)));
        Assert.Equal(120, s.UltimoNsu);
        Assert.Equal(120, s.DocumentosRecebidos);

        s.RegistrarRetorno("656", "Consumo indevido", 0, 0, 0, Agora);
        Assert.Equal(120, s.UltimoNsu); // NSU nunca volta
        Assert.True(s.UltimaConsultaFalhou);
    }
}

public class CertificadoA1Tests
{
    [Fact]
    public void Extrai_cnpj_do_subject_alt_name_icp_brasil()
    {
        var pfx = CertificadoTeste.Gerar("segredo", cnpjNoSan: "11222333000181");
        var info = LeitorCertificadoA1.Ler(pfx, "segredo");
        Assert.Equal("11222333000181", info.Cnpj);
        Assert.Equal("EMPRESA TESTE LTDA", info.Titular);
        Assert.True(info.ValidoAte > DateTime.UtcNow);
    }

    [Fact]
    public void Extrai_cnpj_do_cn_quando_nao_ha_san()
    {
        var info = LeitorCertificadoA1.Ler(CertificadoTeste.Gerar("x", cnpjNoCn: "11222333000181"), "x");
        Assert.Equal("11222333000181", info.Cnpj);
    }

    [Fact]
    public void Senha_errada_vira_erro_de_negocio()
    {
        var pfx = CertificadoTeste.Gerar("certa", cnpjNoSan: "11222333000181");
        var ex = Assert.Throws<DomainException>(() => LeitorCertificadoA1.Ler(pfx, "errada"));
        Assert.Contains("senha", ex.Message);
    }
}

public class CriptografiaAesGcmTests
{
    private static CriptografiaAesGcm Cripto() =>
        new(Options.Create(new CriptografiaOptions { Chave = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }));

    [Fact]
    public void Ida_e_volta_e_deteccao_de_adulteracao()
    {
        var c = Cripto();
        var dados = "senha-do-certificado"u8.ToArray();
        var protegido = c.Proteger(dados);
        Assert.NotEqual(dados, protegido[^dados.Length..]);
        Assert.Equal(dados, c.Desproteger(protegido));

        protegido[^1] ^= 0xFF;
        Assert.ThrowsAny<CryptographicException>(() => c.Desproteger(protegido));
        Assert.ThrowsAny<CryptographicException>(() => Cripto().Desproteger(c.Proteger(dados)));
    }

    [Fact]
    public void Chave_ausente_so_falha_no_uso()
    {
        var c = new CriptografiaAesGcm(Options.Create(new CriptografiaOptions()));
        Assert.Throws<InvalidOperationException>(() => c.Proteger([1]));
    }
}

public class MensagensSefazTests
{
    [Fact]
    public void Evento_de_manifestacao_sai_assinado_e_a_assinatura_confere()
    {
        var pfx = CertificadoTeste.Gerar("s", cnpjNoSan: "11222333000181");
        using var cert = LeitorCertificadoA1.Carregar(pfx, "s");
        var chave = CertificadoTeste.Chave("99888777000166", 1);

        var xml = MensagensSefaz.EnvEventoAssinado(2, "11222333000181", chave, ManifestacaoNFe.NaoRealizada,
            "Mercadoria nao foi entregue", new DateTimeOffset(2026, 11, 13, 15, 0, 0, TimeSpan.Zero), cert);

        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(xml);
        Assert.Contains($"Id=\"ID210240{chave}01\"", xml);
        Assert.Contains("<dhEvento>2026-11-13T12:00:00-03:00</dhEvento>", xml);
        Assert.Contains("<xJust>Mercadoria nao foi entregue</xJust>", xml);
        Assert.Contains("<cOrgao>91</cOrgao>", xml);

        var assinatura = (XmlElement)doc.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl)[0]!;
        Assert.Equal("evento", assinatura.ParentNode!.LocalName);
        var signed = new SignedXml(doc);
        signed.LoadXml(assinatura);
        Assert.True(signed.CheckSignature(cert, verifySignatureOnly: true));
        Assert.Equal(SignedXml.XmlDsigRSASHA1Url, signed.SignatureMethod);
    }

    [Fact]
    public void Le_retorno_da_distribuicao_com_documentos_compactados()
    {
        var res = "<resNFe xmlns=\"http://www.portalfiscal.inf.br/nfe\"><chNFe>1</chNFe></resNFe>";
        var soap = $"""
            <soap:Envelope xmlns:soap="http://www.w3.org/2003/05/soap-envelope"><soap:Body>
            <nfeDistDFeInteresseResponse xmlns="http://www.portalfiscal.inf.br/nfe/wsdl/NFeDistribuicaoDFe"><nfeDistDFeInteresseResult>
            <retDistDFeInt xmlns="http://www.portalfiscal.inf.br/nfe" versao="1.01"><tpAmb>2</tpAmb><cStat>138</cStat><xMotivo>Documento localizado</xMotivo>
            <ultNSU>000000000000042</ultNSU><maxNSU>000000000000100</maxNSU>
            <loteDistDFeInt><docZip NSU="000000000000042" schema="resNFe_v1.01.xsd">{MensagensSefaz.Compactar(res)}</docZip></loteDistDFeInt>
            </retDistDFeInt></nfeDistDFeInteresseResult></nfeDistDFeInteresseResponse></soap:Body></soap:Envelope>
            """;
        var r = MensagensSefaz.LerRetornoDistribuicao(soap);
        Assert.Equal("138", r.CStat);
        Assert.Equal(42, r.UltimoNsu);
        Assert.Equal(100, r.MaxNsu);
        var doc = Assert.Single(r.Documentos);
        Assert.Equal(42, doc.Nsu);
        Assert.Equal(res, doc.Xml);
    }

    [Theory]
    [InlineData("128", "135", true)]
    [InlineData("128", "573", true)]
    [InlineData("128", "596", false)]
    [InlineData("215", null, false)]
    public void Le_retorno_do_evento(string cStatLote, string? cStatEvento, bool registrado)
    {
        var ret = cStatEvento is null ? "" :
            $"<retEvento versao=\"1.00\"><infEvento><cStat>{cStatEvento}</cStat><xMotivo>m</xMotivo><nProt>891260000000001</nProt></infEvento></retEvento>";
        var soap = $"<Envelope><Body><retEnvEvento xmlns=\"http://www.portalfiscal.inf.br/nfe\"><cStat>{cStatLote}</cStat><xMotivo>lote</xMotivo>{ret}</retEnvEvento></Body></Envelope>";
        var r = MensagensSefaz.LerRetornoEvento(soap);
        Assert.Equal(registrado, r.Registrado);
        Assert.Equal(cStatEvento ?? cStatLote, r.CStat);
    }

    [Fact]
    public void Distribuicao_pede_o_nsu_com_15_digitos()
    {
        var xml = MensagensSefaz.DistDFeInt(2, 35, "11222333000181", 42);
        Assert.Contains("<ultNSU>000000000000042</ultNSU>", xml);
        Assert.Contains("<cUFAutor>35</cUFAutor>", xml);
        Assert.Contains("versao=\"1.01\"", xml);
    }
}

public class ConsultaCnpjParserTests
{
    [Fact]
    public void Le_resposta_da_brasilapi()
    {
        using var doc = JsonDocument.Parse("""
            {"cnpj":"19131243000197","razao_social":"OPEN KNOWLEDGE BRASIL","nome_fantasia":"REDE PELO CONHECIMENTO LIVRE",
             "descricao_situacao_cadastral":"ATIVA","descricao_tipo_de_logradouro":"AVENIDA","logradouro":"PAULISTA 37",
             "numero":"37","complemento":"ANDAR 4","bairro":"BELA VISTA","municipio":"SAO PAULO","uf":"SP","cep":"01311902",
             "ddd_telefone_1":"1123851939","email":null,"opcao_pelo_simples":false,"opcao_pelo_mei":false,
             "cnae_fiscal_descricao":"Atividades de associações","data_inicio_atividade":"2013-10-03"}
            """);
        var d = ConsultaCnpjBrasilApi.Ler(doc.RootElement, "19131243000197", "BrasilAPI");
        Assert.Equal("OPEN KNOWLEDGE BRASIL", d.RazaoSocial);
        Assert.True(d.Ativa);
        Assert.Equal("Ativa", d.SituacaoCadastral);
        Assert.Equal("Avenida Paulista 37", d.Endereco!.Logradouro);
        Assert.Equal("01311902", d.Endereco.Cep);
        Assert.Equal("SP", d.Endereco.Uf);
        Assert.Equal("1123851939", d.Telefone);
        Assert.Null(d.RegimeSugerido);
        Assert.Equal(new DateOnly(2013, 10, 3), d.InicioAtividade);
    }

    [Fact]
    public void Le_resposta_da_receitaws_com_simples()
    {
        using var doc = JsonDocument.Parse("""
            {"status":"OK","nome":"PADARIA EXEMPLO LTDA","fantasia":"","situacao":"ATIVA","cep":"01.001-000","logradouro":"PRACA DA SE",
             "numero":"1","municipio":"SAO PAULO","uf":"SP","telefone":"(11) 3333-4444/ (11) 5555-6666","email":"X@Y.COM",
             "simples":{"optante":true},"simei":{"optante":false},"abertura":"01/02/2010",
             "atividade_principal":[{"code":"10.91-1-02","text":"Padaria"}]}
            """);
        var d = ConsultaCnpjReceitaWs.Ler(doc.RootElement, "11222333000181", "ReceitaWS");
        Assert.Equal(RegimeTributario.SimplesNacional, d.RegimeSugerido);
        Assert.Null(d.NomeFantasia);
        Assert.Equal("01001000", d.Endereco!.Cep);
        Assert.Equal("1133334444", d.Telefone);
        Assert.Equal("x@y.com", d.Email);
        Assert.Equal("Padaria", d.AtividadePrincipal);
        Assert.Equal(new DateOnly(2010, 2, 1), d.InicioAtividade);
    }
}
