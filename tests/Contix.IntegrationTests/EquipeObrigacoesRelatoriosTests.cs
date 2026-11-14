using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Contix.Tests.Support;

namespace Contix.IntegrationTests;

/// <summary>Departamentos, responsáveis, obrigações recorrentes, entrega de tarefas, painel, relatórios e DANFE.</summary>
[Collection(nameof(ApiCollection))]
public sealed class EquipeObrigacoesRelatoriosTests(ContixApiFactory factory)
{
    private async Task<HttpClient> LoginAsync(string email)
    {
        var http = factory.CreateClient();
        var body = await (await http.PostAsJsonAsync("/api/v1/auth/login", new { email, senha = "Contix@123" }))
            .Content.ReadFromJsonAsync<JsonObject>();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!["accessToken"]!.GetValue<string>());
        return http;
    }

    private static async Task<JsonNode> JsonAsync(HttpResponseMessage r)
    {
        var texto = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {texto}");
        return JsonNode.Parse(texto)!;
    }

    private static async Task<string> IdDoUsuarioAsync(HttpClient http) =>
        (await JsonAsync(await http.GetAsync("/api/v1/auth/me")))["id"]!.GetValue<string>();

    private static async Task<string> NovaEmpresaAsync(HttpClient admin, string regime = "Isento") =>
        (await JsonAsync(await admin.PostAsJsonAsync("/api/v1/empresas", new
        {
            razaoSocial = "Equipe Teste " + Guid.NewGuid().ToString("N")[..6], documento = CertificadoTeste.NovoCnpj(),
            regimeTributario = regime, situacao = "Ativa"
        })))["id"]!.GetValue<string>();

    [Fact]
    public async Task Departamentos_sao_renomeados_e_colaborador_atua_em_varios()
    {
        var admin = await LoginAsync("admin@contix.dev");
        var colaborador = await LoginAsync("colaborador@contix.dev");

        var lista = (await JsonAsync(await admin.GetAsync("/api/v1/departamentos"))).AsArray();
        Assert.Equal(5, lista.Count);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await colaborador.PutAsJsonAsync("/api/v1/departamentos/Societario", new { nome = "Legal", ativo = true })).StatusCode);
        var renomeado = await JsonAsync(await admin.PutAsJsonAsync("/api/v1/departamentos/Societario", new { nome = "Societário e Legal", ativo = true }));
        Assert.Equal("Societário e Legal", renomeado["nome"]!.GetValue<string>());

        var colaboradorId = await IdDoUsuarioAsync(colaborador);
        var usuario = await JsonAsync(await admin.GetAsync($"/api/v1/usuarios/{colaboradorId}"));
        var atualizado = await JsonAsync(await admin.PutAsJsonAsync($"/api/v1/usuarios/{colaboradorId}", new
        {
            nome = usuario["nome"]!.GetValue<string>(), perfil = "Colaborador", ativo = true, empresaIds = Array.Empty<string>(),
            departamentos = new[] { "Fiscal", "Pessoal" }
        }));
        Assert.Equal(["Fiscal", "Pessoal"], atualizado["departamentos"]!.AsArray().Select(d => d!.GetValue<string>()));

        var doFiscal = await JsonAsync(await admin.GetAsync("/api/v1/usuarios?escritorio=true&departamento=Fiscal"));
        Assert.Contains(doFiscal["items"]!.AsArray(), u => u!["id"]!.GetValue<string>() == colaboradorId);
    }

    [Fact]
    public async Task Solicitacao_nova_vai_para_o_responsavel_do_departamento()
    {
        var admin = await LoginAsync("admin@contix.dev");
        var colaboradorId = await IdDoUsuarioAsync(await LoginAsync("colaborador@contix.dev"));
        var empresaId = await NovaEmpresaAsync(admin);

        var responsaveis = (await JsonAsync(await admin.PutAsJsonAsync($"/api/v1/empresas/{empresaId}/responsaveis", new
        {
            responsaveis = new[] { new { departamento = "Fiscal", usuarioId = (string?)colaboradorId } }
        }))).AsArray();
        Assert.Equal(5, responsaveis.Count);
        Assert.Equal("Colaborador Demo", responsaveis.Single(r => r!["departamento"]!.GetValue<string>() == "Fiscal")!["usuarioNome"]!.GetValue<string>());

        // Um usuário do cliente da nova empresa abre a solicitação.
        var email = $"cli-{Guid.NewGuid():N}@teste.dev";
        await JsonAsync(await admin.PostAsJsonAsync("/api/v1/usuarios", new
        {
            nome = "Cliente Equipe", email, perfil = "Cliente", senha = "Contix@123", empresaIds = new[] { empresaId }
        }));
        var clienteNovo = await LoginAsync(email);
        var clienteId = await IdDoUsuarioAsync(clienteNovo);

        var aberta = await JsonAsync(await clienteNovo.PostAsync($"/api/v1/empresas/{empresaId}/solicitacoes", new MultipartFormDataContent
        {
            { new StringContent("Geral"), "tipo" },
            { new StringContent("Fiscal"), "departamento" },
            { new StringContent("Dúvida sobre ICMS"), "assunto" },
            { new StringContent("Como fica a apuração?"), "descricao" }
        }));
        Assert.Equal(colaboradorId, aberta["responsavelId"]!.GetValue<string>());
        Assert.NotEqual(clienteId, colaboradorId);

        // Remover o responsável.
        var removido = (await JsonAsync(await admin.PutAsJsonAsync($"/api/v1/empresas/{empresaId}/responsaveis", new
        {
            responsaveis = new[] { new { departamento = "Fiscal", usuarioId = (string?)null } }
        }))).AsArray();
        Assert.Null(removido.Single(r => r!["departamento"]!.GetValue<string>() == "Fiscal")!["usuarioId"]);

        // Tarefa criada a partir da solicitação mantém o vínculo.
        var solicitacaoId = aberta["id"]!.GetValue<string>();
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
        var tarefa = await JsonAsync(await admin.PostAsync($"/api/v1/empresas/{empresaId}/tarefas", new MultipartFormDataContent
        {
            { new StringContent("Responder dúvida de ICMS"), "titulo" },
            { new StringContent("Fiscal"), "departamento" },
            { new StringContent(colaboradorId), "responsavelId" },
            { new StringContent(hoje.AddDays(3).ToString("O")), "prazoInterno" },
            { new StringContent(solicitacaoId), "solicitacaoId" }
        }));
        Assert.Equal(solicitacaoId, tarefa["solicitacao"]!["id"]!.GetValue<string>());
        var vinculadas = (await JsonAsync(await admin.GetAsync($"/api/v1/empresas/{empresaId}/solicitacoes/{solicitacaoId}/tarefas"))).AsArray();
        Assert.Single(vinculadas);
    }

    [Fact]
    public async Task Obrigacao_recorrente_gera_tarefas_uma_vez_por_competencia()
    {
        var admin = await LoginAsync("admin@contix.dev");
        var colaborador = await LoginAsync("colaborador@contix.dev");
        var colaboradorId = await IdDoUsuarioAsync(colaborador);
        var adminId = await IdDoUsuarioAsync(admin);
        var comResponsavel = await NovaEmpresaAsync(admin, "Imune");
        var semResponsavel = await NovaEmpresaAsync(admin, "Imune");
        await JsonAsync(await admin.PutAsJsonAsync($"/api/v1/empresas/{comResponsavel}/responsaveis", new
        {
            responsaveis = new[] { new { departamento = "Pessoal", usuarioId = (string?)colaboradorId } }
        }));

        var payload = new
        {
            titulo = "DCTFWeb " + Guid.NewGuid().ToString("N")[..4], descricao = "Transmitir a DCTFWeb.", departamento = "Pessoal",
            periodicidade = "Mensal", diaVencimento = 20, mesesAposCompetencia = 1, mesReferencia = 1, diasAntecedencia = 5,
            regimes = new[] { "Imune" }, responsavelPadraoId = (string?)null, ativa = true
        };
        Assert.Equal(HttpStatusCode.Forbidden, (await colaborador.PostAsJsonAsync("/api/v1/obrigacoes", payload)).StatusCode);
        var criada = await admin.PostAsJsonAsync("/api/v1/obrigacoes", payload);
        Assert.Equal(HttpStatusCode.Created, criada.StatusCode);
        var obrigacao = await JsonAsync(criada);
        var id = obrigacao["id"]!.GetValue<string>();
        Assert.NotNull(obrigacao["proximoVencimento"]);

        var competencia = DateOnly.FromDateTime(DateTime.UtcNow);
        var geracao = await JsonAsync(await admin.PostAsJsonAsync($"/api/v1/obrigacoes/{id}/geracoes", new { competencia = competencia.ToString("O") }));
        Assert.True(geracao["empresasAplicaveis"]!.GetValue<int>() >= 2);
        Assert.True(geracao["geradas"]!.GetValue<int>() >= 1);
        Assert.True(geracao["semResponsavel"]!.GetValue<int>() >= 1);
        var primeiras = (await JsonAsync(await colaborador.GetAsync($"/api/v1/tarefas?obrigacaoId={id}&pageSize=100")))["items"]!.AsArray();
        Assert.Contains(primeiras, t => t!["empresaId"]!.GetValue<string>() == comResponsavel);
        Assert.DoesNotContain(primeiras, t => t!["empresaId"]!.GetValue<string>() == semResponsavel);

        // Com responsável padrão, a empresa sem responsável também recebe; a que já tem a tarefa é ignorada.
        await JsonAsync(await admin.PutAsJsonAsync($"/api/v1/obrigacoes/{id}", payload with { responsavelPadraoId = adminId }));
        var segunda = await JsonAsync(await admin.PostAsJsonAsync($"/api/v1/obrigacoes/{id}/geracoes", new { competencia = competencia.ToString("O") }));
        Assert.Equal(primeiras.Count, segunda["jaExistentes"]!.GetValue<int>());
        Assert.True(segunda["geradas"]!.GetValue<int>() >= 1);

        var tarefas = await JsonAsync(await colaborador.GetAsync($"/api/v1/tarefas?obrigacaoId={id}&pageSize=100"));
        var daEmpresa = tarefas["items"]!.AsArray().Single(t => t!["empresaId"]!.GetValue<string>() == comResponsavel)!;
        Assert.Equal(colaboradorId, daEmpresa["responsavelId"]!.GetValue<string>());
        Assert.Equal(new DateOnly(competencia.Year, competencia.Month, 1).ToString("O"), daEmpresa["competencia"]!.GetValue<string>());
        Assert.Contains(tarefas["items"]!.AsArray(), t => t!["empresaId"]!.GetValue<string>() == semResponsavel);

        // Concluir com entrega: publica o documento na pasta da empresa.
        var pastaId = (await JsonAsync(await admin.GetAsync($"/api/v1/empresas/{comResponsavel}/pastas"))).AsArray()
            .First(p => p!["nome"]!.GetValue<string>() == "Pessoal")!["id"]!.GetValue<string>();
        var tarefaId = daEmpresa["id"]!.GetValue<string>();
        var conclusao = new MultipartFormDataContent
        {
            { new StringContent("Transmitida."), "mensagem" },
            { new StringContent(pastaId), "pastaId" },
            { new StringContent(competencia.AddDays(10).ToString("O")), "dataVencimento" },
            { new ByteArrayContent("recibo"u8.ToArray()) { Headers = { ContentType = new("application/pdf") } }, "arquivos", "recibo-dctfweb.pdf" }
        };
        var concluida = await JsonAsync(await colaborador.PostAsync($"/api/v1/empresas/{comResponsavel}/tarefas/{tarefaId}/conclusao", conclusao));
        Assert.Equal("Concluida", concluida["status"]!.GetValue<string>());
        var entregue = Assert.Single(concluida["documentosEntregues"]!.AsArray())!;
        Assert.Equal("recibo-dctfweb", entregue["nome"]!.GetValue<string>());
        var docs = await JsonAsync(await admin.GetAsync($"/api/v1/empresas/{comResponsavel}/documentos?pastaId={pastaId}"));
        Assert.Contains(docs["items"]!.AsArray(), d => d!["id"]!.GetValue<string>() == entregue["id"]!.GetValue<string>());

        // Concluir de novo é recusado.
        var denovo = await colaborador.PostAsync($"/api/v1/empresas/{comResponsavel}/tarefas/{tarefaId}/conclusao",
            new MultipartFormDataContent { { new StringContent("De novo"), "mensagem" } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, denovo.StatusCode);
    }

    [Fact]
    public async Task Painel_e_relatorios()
    {
        var admin = await LoginAsync("admin@contix.dev");
        var cliente = await LoginAsync("cliente@contix.dev");

        var painel = await JsonAsync(await admin.GetAsync("/api/v1/painel"));
        Assert.True(painel["clientesAtivos"]!.GetValue<int>() >= 1);
        Assert.NotNull(painel["solicitacoesPorStatus"]);
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync("/api/v1/painel")).StatusCode);

        var de = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-1).ToString("O");
        foreach (var tipo in new[] { "Documentos", "Solicitacoes", "Tarefas" })
        {
            var previa = await JsonAsync(await admin.GetAsync($"/api/v1/relatorios/{tipo}?de={de}"));
            Assert.NotEmpty(previa["colunas"]!.AsArray());
            Assert.NotEmpty(previa["resumo"]!.AsArray());

            var csv = await admin.GetAsync($"/api/v1/relatorios/{tipo}?de={de}&formato=Csv");
            Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
            Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
            var bytes = await csv.Content.ReadAsByteArrayAsync();
            Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);

            var pdf = await admin.GetAsync($"/api/v1/relatorios/{tipo}?de={de}&formato=Pdf");
            Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
            Assert.Equal("%PDF", Encoding.ASCII.GetString((await pdf.Content.ReadAsByteArrayAsync())[..4]));
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/v1/relatorios/Tarefas?status=Inexistente")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync("/api/v1/relatorios/Tarefas")).StatusCode);

        // Portal do cliente: só as solicitações da própria empresa.
        var empresaId = (await JsonAsync(await cliente.GetAsync("/api/v1/empresas")))["items"]![0]!["id"]!.GetValue<string>();
        var doCliente = await JsonAsync(await cliente.GetAsync($"/api/v1/empresas/{empresaId}/relatorios/solicitacoes?de={de}"));
        Assert.Equal("Relatório de solicitações", doCliente["titulo"]!.GetValue<string>());
        var outra = await NovaEmpresaAsync(admin);
        Assert.Equal(HttpStatusCode.NotFound, (await cliente.GetAsync($"/api/v1/empresas/{outra}/relatorios/solicitacoes")).StatusCode);
    }

    [Fact]
    public async Task Danfe_e_download_em_lote()
    {
        var admin = await LoginAsync("admin@contix.dev");
        var empresa = (await JsonAsync(await admin.GetAsync("/api/v1/empresas?busca=DEMO")))["items"]![0]!;
        var empresaId = empresa["id"]!.GetValue<string>();
        var cnpj = empresa["documento"]!.GetValue<string>();
        var url = $"/api/v1/empresas/{empresaId}/notas-fiscais";

        var form = new MultipartFormDataContent();
        foreach (var n in new[] { Random.Shared.Next(100_000, 999_999), Random.Shared.Next(100_000, 999_999) })
        {
            var emit = CertificadoTeste.NovoCnpj();
            var xml = CertificadoTeste.NfeProc(CertificadoTeste.Chave(emit, n), emit, cnpj);
            form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(xml)) { Headers = { ContentType = new("application/xml") } }, "arquivos", $"{n}.xml");
        }
        var importacao = await JsonAsync(await admin.PostAsync($"{url}/importacao", form));
        var chaves = importacao["arquivos"]!.AsArray().Select(r => r!["chaveAcesso"]!.GetValue<string>()).ToHashSet();
        var ids = (await JsonAsync(await admin.GetAsync($"{url}?pageSize=100")))["items"]!.AsArray()
            .Where(n => chaves.Contains(n!["chaveAcesso"]!.GetValue<string>()))
            .Select(n => n!["id"]!.GetValue<string>()).ToList();
        Assert.Equal(2, ids.Count);

        var danfe = await admin.GetAsync($"{url}/{ids[0]}/danfe");
        Assert.Equal(HttpStatusCode.OK, danfe.StatusCode);
        Assert.Equal("application/pdf", danfe.Content.Headers.ContentType!.MediaType);

        var lote = await admin.PostAsJsonAsync($"{url}/download", new { notaIds = ids, conteudo = "XmlEDanfe" });
        Assert.Equal(HttpStatusCode.OK, lote.StatusCode);
        using var zip = new System.IO.Compression.ZipArchive(await lote.Content.ReadAsStreamAsync());
        Assert.Equal(2, zip.Entries.Count(e => e.FullName.StartsWith("xml/")));
        Assert.Equal(2, zip.Entries.Count(e => e.FullName.StartsWith("danfe/")));

        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.PostAsJsonAsync($"{url}/download", new { notaIds = new[] { Guid.NewGuid() } })).StatusCode);
    }
}
