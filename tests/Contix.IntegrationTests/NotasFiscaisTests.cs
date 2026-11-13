using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Contix.Application.Features.NotasFiscais;
using Contix.Infrastructure.Persistence;
using Contix.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Contix.IntegrationTests;

/// <summary>NF-e: importação de XML, lista, manifestação, certificado A1 e busca automática na SEFAZ simulada; consulta de CNPJ.</summary>
[Collection(nameof(ApiCollection))]
public sealed class NotasFiscaisTests(ContixApiFactory factory)
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> Tokens = new();
    private const string EmpresaDemo = "44555666000181";

    private async Task<HttpClient> LoginAsync(string email)
    {
        var http = factory.CreateClient();
        if (!Tokens.TryGetValue(email, out var token))
        {
            var body = await (await http.PostAsJsonAsync("/api/v1/auth/login", new { email, senha = "Contix@123" }))
                .Content.ReadFromJsonAsync<JsonObject>();
            token = Tokens[email] = body!["accessToken"]!.GetValue<string>();
        }
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private static async Task<JsonNode> JsonAsync(HttpResponseMessage r)
    {
        var texto = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {texto}");
        return JsonNode.Parse(texto)!;
    }

    private static async Task<(string Id, string Cnpj)> NovaEmpresaAsync(HttpClient admin, string nome, string? uf = "SP")
    {
        var cnpj = CertificadoTeste.NovoCnpj();
        var id = (await JsonAsync(await admin.PostAsJsonAsync("/api/v1/empresas", new
        {
            razaoSocial = nome, documento = cnpj, regimeTributario = "SimplesNacional", situacao = "Ativa",
            endereco = uf is null ? null : new { cep = "01310100", cidade = "São Paulo", uf }
        })))["id"]!.GetValue<string>();
        return (id, cnpj);
    }

    private static MultipartFormDataContent Xmls(params (string Nome, string Conteudo)[] arquivos)
    {
        var form = new MultipartFormDataContent();
        foreach (var (nome, conteudo) in arquivos)
            form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(conteudo)) { Headers = { ContentType = new("application/xml") } }, "arquivos", nome);
        return form;
    }

    private static MultipartFormDataContent Certificado(byte[] pfx, string senha, string nome = "empresa.pfx") => new()
    {
        { new ByteArrayContent(pfx) { Headers = { ContentType = new("application/x-pkcs12") } }, "arquivo", nome },
        { new StringContent(senha), "senha" }
    };

    private async Task<string> IdSincronizacaoAsync(string empresaId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ContixDbContext>();
        return (await db.SincronizacoesNFe.IgnoreQueryFilters().SingleAsync(s => s.EmpresaId == Guid.Parse(empresaId))).Id.ToString();
    }

    private async Task<int> ProcessarAsync(string sincronizacaoId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var p = scope.ServiceProvider.GetRequiredService<IProcessadorSincronizacaoNFe>();
        Assert.Contains(Guid.Parse(sincronizacaoId), await p.ListarProntasAsync());
        return await p.ProcessarAsync(Guid.Parse(sincronizacaoId));
    }

    [Fact]
    public async Task Importacao_de_xml_alimenta_a_lista_sem_duplicar()
    {
        var admin = await LoginAsync("admin@contix.dev");
        var (empresaId, cnpj) = await NovaEmpresaAsync(admin, "Importação NF-e Ltda");
        var url = $"/api/v1/empresas/{empresaId}/notas-fiscais";
        var fornecedorA = CertificadoTeste.NovoCnpj();
        var chaveA = CertificadoTeste.Chave(fornecedorA, 101);
        var chaveB = CertificadoTeste.Chave(CertificadoTeste.NovoCnpj(), 202);
        var xmlA = CertificadoTeste.NfeProc(chaveA, fornecedorA, cnpj, 1500m, "2026-10-01T09:00:00-03:00", "Papelaria Alfa Ltda");

        var r = await JsonAsync(await admin.PostAsync($"{url}/importacao", Xmls(
            ("a.xml", xmlA),
            ("b.xml", CertificadoTeste.NfeProc(chaveB, CertificadoTeste.NovoCnpj(), cnpj, 99.9m, "2026-11-02T09:00:00-03:00", "Mercado Beta")),
            ("a-copia.xml", xmlA),
            ("outra.xml", CertificadoTeste.NfeProc(CertificadoTeste.Chave(fornecedorA, 303), fornecedorA, CertificadoTeste.NovoCnpj())),
            ("lixo.xml", "<pedido/>"),
            ("nota.pdf", "%PDF"))));
        Assert.Equal(2, r["importadas"]!.GetValue<int>());
        Assert.Equal(2, r["ignoradas"]!.GetValue<int>());
        Assert.Equal(2, r["erros"]!.GetValue<int>());
        var arquivos = r["arquivos"]!.AsArray();
        Assert.Contains("destinatário é outro", arquivos.Single(a => a!["arquivo"]!.GetValue<string>() == "outra.xml")!["mensagem"]!.GetValue<string>());

        // Reimportar não duplica.
        var de_novo = await JsonAsync(await admin.PostAsync($"{url}/importacao", Xmls(("a.xml", xmlA))));
        Assert.Equal(1, de_novo["ignoradas"]!.GetValue<int>());

        var lista = await JsonAsync(await admin.GetAsync(url));
        Assert.Equal(2, lista["total"]!.GetValue<int>());
        var primeira = lista["items"]!.AsArray()[0]!;
        Assert.Equal(chaveB, primeira["chaveAcesso"]!.GetValue<string>()); // mais recente primeiro
        Assert.Equal("ImportacaoXml", primeira["origem"]!.GetValue<string>());
        Assert.Equal("SemManifestacao", primeira["manifestacao"]!.GetValue<string>());
        Assert.Equal(202, primeira["numero"]!.GetValue<long>());

        Assert.Equal(1, (await JsonAsync(await admin.GetAsync($"{url}?busca=alfa")))["total"]!.GetValue<int>());
        Assert.Equal(1, (await JsonAsync(await admin.GetAsync($"{url}?busca={fornecedorA}")))["total"]!.GetValue<int>());
        Assert.Equal(1, (await JsonAsync(await admin.GetAsync($"{url}?de=2026-11-01&ate=2026-11-30")))["total"]!.GetValue<int>());
        Assert.Equal(2, (await JsonAsync(await admin.GetAsync($"{url}/resumo")))["semManifestacao"]!.GetValue<int>());

        var notaA = lista["items"]!.AsArray().Single(n => n!["chaveAcesso"]!.GetValue<string>() == chaveA)!["id"]!.GetValue<string>();
        var xml = await admin.GetAsync($"{url}/{notaA}/xml");
        Assert.Equal(HttpStatusCode.OK, xml.StatusCode);
        Assert.Contains(chaveA, await xml.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Manifestacao_sem_certificado_fica_registrada_no_contix_e_cliente_manifesta_no_portal()
    {
        var admin = await LoginAsync("admin@contix.dev");
        var cliente = await LoginAsync("cliente@contix.dev");
        var empresaId = (await JsonAsync(await admin.GetAsync($"/api/v1/empresas?busca={EmpresaDemo}")))["items"]![0]!["id"]!.GetValue<string>();
        var url = $"/api/v1/empresas/{empresaId}/notas-fiscais";
        var chave = CertificadoTeste.Chave(CertificadoTeste.NovoCnpj(), 404);
        await JsonAsync(await admin.PostAsync($"{url}/importacao", Xmls(("n.xml", CertificadoTeste.NfeProc(chave, CertificadoTeste.NovoCnpj(), EmpresaDemo)))));
        var notaId = (await JsonAsync(await cliente.GetAsync($"{url}?busca={chave}")))["items"]![0]!["id"]!.GetValue<string>();

        // Cliente vê e manifesta, mas não importa nem mexe na integração.
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.PostAsync($"{url}/importacao", Xmls(("n.xml", "x")))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync($"/api/v1/empresas/{empresaId}/integracao-sefaz")).StatusCode);

        var ciencia = await JsonAsync(await cliente.PostAsJsonAsync($"{url}/{notaId}/manifestacao", new { tipo = "Ciencia" }));
        Assert.Equal("Ciencia", ciencia["manifestacao"]!.GetValue<string>());
        Assert.False(ciencia["manifestacaoEnviadaSefaz"]!.GetValue<bool>());
        Assert.Equal("Cliente Demo", ciencia["manifestadaPorNome"]!.GetValue<string>());

        var curta = await cliente.PostAsJsonAsync($"{url}/{notaId}/manifestacao", new { tipo = "NaoRealizada", justificativa = "curta" });
        Assert.Equal(HttpStatusCode.BadRequest, curta.StatusCode);

        var nr = await JsonAsync(await cliente.PostAsJsonAsync($"{url}/{notaId}/manifestacao",
            new { tipo = "NaoRealizada", justificativa = "A mercadoria nunca foi entregue." }));
        Assert.Equal("NaoRealizada", nr["manifestacao"]!.GetValue<string>());
        Assert.Equal("A mercadoria nunca foi entregue.", nr["justificativaManifestacao"]!.GetValue<string>());

        var final = await admin.PostAsJsonAsync($"{url}/{notaId}/manifestacao", new { tipo = "Confirmada" });
        Assert.Equal((HttpStatusCode)422, final.StatusCode);
    }

    [Fact]
    public async Task Certificado_a1_e_validado_e_guardado_criptografado()
    {
        var admin = await LoginAsync("admin@contix.dev");
        var (empresaId, cnpj) = await NovaEmpresaAsync(admin, "Certificado A1 Ltda", uf: null);
        var url = $"/api/v1/empresas/{empresaId}/integracao-sefaz";

        var inicial = await JsonAsync(await admin.GetAsync(url));
        Assert.True(inicial["integracaoHabilitada"]!.GetValue<bool>());
        Assert.Equal("Simulado", inicial["provedor"]!.GetValue<string>());
        Assert.Null(inicial["certificado"]);
        Assert.Equal(2, inicial["pendencias"]!.AsArray().Count); // UF e certificado

        var senhaErrada = await admin.PostAsync($"{url}/certificado", Certificado(CertificadoTeste.Gerar("certa", cnpjNoSan: cnpj), "errada"));
        Assert.Equal((HttpStatusCode)422, senhaErrada.StatusCode);
        var outroCnpj = await admin.PostAsync($"{url}/certificado", Certificado(CertificadoTeste.Gerar("s", cnpjNoSan: "11222333000181"), "s"));
        Assert.Equal((HttpStatusCode)422, outroCnpj.StatusCode);
        Assert.Contains("não pertence", await outroCnpj.Content.ReadAsStringAsync());
        var vencido = await admin.PostAsync($"{url}/certificado", Certificado(CertificadoTeste.Gerar("s", cnpjNoSan: cnpj,
            inicio: DateTimeOffset.UtcNow.AddYears(-2), fim: DateTimeOffset.UtcNow.AddDays(-1)), "s"));
        Assert.Equal((HttpStatusCode)422, vencido.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync($"{url}/certificado", Certificado([1, 2], "s", "cert.txt"))).StatusCode);

        var ok = await JsonAsync(await admin.PostAsync($"{url}/certificado", Certificado(CertificadoTeste.Gerar("s3nh@", cnpjNoSan: cnpj), "s3nh@")));
        Assert.Equal(cnpj, ok["certificado"]!["cnpj"]!.GetValue<string>());
        Assert.Equal("empresa.pfx", ok["certificado"]!["nomeArquivo"]!.GetValue<string>());
        Assert.False(ok["certificado"]!["vencido"]!.GetValue<bool>());
        Assert.Single(ok["pendencias"]!.AsArray());

        // Sem UF não liga a busca.
        var ligar = await admin.PutAsJsonAsync(url, new { ativa = true, cienciaAutomatica = true });
        Assert.Equal(HttpStatusCode.BadRequest, ligar.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ContixDbContext>();
            var cert = await db.CertificadosDigitais.IgnoreQueryFilters().SingleAsync(c => c.EmpresaId == Guid.Parse(empresaId) && !c.IsDeleted);
            Assert.DoesNotContain("s3nh@", Encoding.UTF8.GetString(cert.SenhaCriptografada));
            Assert.True(cert.ArquivoCriptografado.Length > 100);
        }

        var removido = await JsonAsync(await admin.DeleteAsync($"{url}/certificado"));
        Assert.Null(removido["certificado"]);
    }

    [Fact]
    public async Task Busca_automatica_na_sefaz_simulada_traz_resumos_da_ciencia_e_depois_o_xml_completo()
    {
        var admin = await LoginAsync("admin@contix.dev");
        var (empresaId, cnpj) = await NovaEmpresaAsync(admin, "Busca SEFAZ Ltda");
        var url = $"/api/v1/empresas/{empresaId}/integracao-sefaz";
        var notas = $"/api/v1/empresas/{empresaId}/notas-fiscais";

        Assert.Equal((HttpStatusCode)422, (await admin.PostAsync($"{url}/sincronizacoes", null)).StatusCode); // desligada
        await JsonAsync(await admin.PostAsync($"{url}/certificado", Certificado(CertificadoTeste.Gerar("s", cnpjNoSan: cnpj), "s")));
        var ligada = await JsonAsync(await admin.PutAsJsonAsync(url, new { ativa = true, cienciaAutomatica = true }));
        Assert.True(ligada["ativa"]!.GetValue<bool>());
        Assert.Empty(ligada["pendencias"]!.AsArray());
        var sincId = await IdSincronizacaoAsync(empresaId);

        // 1ª busca: três resumos e o cancelamento de um deles; ciência automática nos outros dois.
        Assert.Equal(4, await ProcessarAsync(sincId));
        var lista = (await JsonAsync(await admin.GetAsync(notas)))["items"]!.AsArray();
        Assert.Equal(3, lista.Count);
        Assert.All(lista, n => Assert.Equal("Sefaz", n!["origem"]!.GetValue<string>()));
        Assert.All(lista, n => Assert.False(n!["xmlCompleto"]!.GetValue<bool>()));
        var cancelada = lista.Single(n => n!["situacao"]!.GetValue<string>() == "Cancelada")!;
        Assert.Equal("SemManifestacao", cancelada["manifestacao"]!.GetValue<string>());
        var comCiencia = lista.Where(n => n!["manifestacao"]!.GetValue<string>() == "Ciencia").ToList();
        Assert.Equal(2, comCiencia.Count);
        Assert.All(comCiencia, n => Assert.True(n!["manifestacaoAutomatica"]!.GetValue<bool>()));
        Assert.All(comCiencia, n => Assert.True(n!["manifestacaoEnviadaSefaz"]!.GetValue<bool>()));

        var estado = await JsonAsync(await admin.GetAsync(url));
        Assert.Equal("138", estado["ultimoStatus"]!.GetValue<string>());
        Assert.Equal(4, estado["ultimoNsu"]!.GetValue<long>());

        // "Buscar agora": a ciência liberou o XML completo das duas notas.
        await JsonAsync(await admin.PostAsync($"{url}/sincronizacoes", null));
        Assert.Equal(2, await ProcessarAsync(sincId));
        lista = (await JsonAsync(await admin.GetAsync(notas)))["items"]!.AsArray();
        Assert.Equal(3, lista.Count);
        Assert.Equal(2, lista.Count(n => n!["xmlCompleto"]!.GetValue<bool>()));

        // Manifestação conclusiva vai para a SEFAZ com o certificado.
        var notaId = comCiencia[0]!["id"]!.GetValue<string>();
        var confirmada = await JsonAsync(await admin.PostAsJsonAsync($"{notas}/{notaId}/manifestacao", new { tipo = "Confirmada" }));
        Assert.True(confirmada["manifestacaoEnviadaSefaz"]!.GetValue<bool>());
        Assert.NotNull(confirmada["protocoloManifestacao"]);
        var xml = await (await admin.GetAsync($"{notas}/{notaId}/xml")).Content.ReadAsStringAsync();
        Assert.Contains("nfeProc", xml);

        // Sem documentos novos: a SEFAZ exige 1 hora antes de consultar de novo.
        await JsonAsync(await admin.PostAsync($"{url}/sincronizacoes", null));
        Assert.Equal(0, await ProcessarAsync(sincId));
        Assert.Equal("137", (await JsonAsync(await admin.GetAsync(url)))["ultimoStatus"]!.GetValue<string>());
        var cedo = await admin.PostAsync($"{url}/sincronizacoes", null);
        Assert.Equal((HttpStatusCode)422, cedo.StatusCode);
        Assert.Contains("1 hora", await cedo.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Consulta_de_cnpj_preenche_o_cadastro()
    {
        var admin = await LoginAsync("admin@contix.dev");
        var cliente = await LoginAsync("cliente@contix.dev");

        var dados = await JsonAsync(await admin.GetAsync("/api/v1/consulta-cnpj/11.222.333.0001-81"));
        Assert.Equal("11222333000181", dados["cnpj"]!.GetValue<string>());
        Assert.StartsWith("Empresa Simulada", dados["razaoSocial"]!.GetValue<string>());
        Assert.Equal("SP", dados["endereco"]!["uf"]!.GetValue<string>());
        Assert.Equal("SimplesNacional", dados["regimeSugerido"]!.GetValue<string>());

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/v1/consulta-cnpj/11222333000100")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/v1/consulta-cnpj/00000000000191")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync("/api/v1/consulta-cnpj/11222333000181")).StatusCode);
    }
}
