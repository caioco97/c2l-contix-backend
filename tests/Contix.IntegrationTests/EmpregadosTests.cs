using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Contix.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class EmpregadosTests(ContixApiFactory factory)
{
    private async Task<HttpClient> LoginAsync(string email)
    {
        var http = factory.CreateClient();
        var body = await (await http.PostAsJsonAsync("/api/v1/auth/login", new { email, senha = "Contix@123" }))
            .Content.ReadFromJsonAsync<JsonObject>();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!["accessToken"]!.GetValue<string>());
        return http;
    }

    private static async Task<JsonNode> JsonAsync(HttpResponseMessage r) => JsonNode.Parse(await r.Content.ReadAsStringAsync())!;

    private static async Task<string> EmpresaDemoAsync(HttpClient http) =>
        (await JsonAsync(await http.GetAsync("/api/v1/empresas")))["items"]![0]!["id"]!.GetValue<string>();

    private static string NovoCpf()
    {
        var d = Enumerable.Range(0, 9).Select(_ => Random.Shared.Next(10)).ToList();
        int Dv(IReadOnlyList<int> n) { var soma = n.Select((x, i) => x * (n.Count + 1 - i)).Sum(); var r = soma * 10 % 11; return r == 10 ? 0 : r; }
        d.Add(Dv(d));
        d.Add(Dv(d));
        return string.Concat(d);
    }

    private static async Task<string> CriarAsync(HttpClient escritorio, string empresaId, string nome, string? cpf = null)
    {
        var r = await escritorio.PostAsJsonAsync($"/api/v1/empresas/{empresaId}/empregados", new
        {
            nome, cpf, cargo = "Auxiliar", vinculo = "Empregado", situacao = "Ativo", dataAdmissao = "2025-03-01"
        });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await JsonAsync(r))["id"]!.GetValue<string>();
    }

    private static Task<HttpResponseMessage> AbrirAsync(HttpClient http, string empresaId, string tipo, string dados, bool anexo = false)
    {
        var form = new MultipartFormDataContent { { new StringContent(tipo), "tipo" }, { new StringContent(dados), "dados" } };
        if (anexo)
            form.Add(new ByteArrayContent("doc"u8.ToArray()) { Headers = { ContentType = new("application/pdf") } }, "anexos", "doc.pdf");
        return http.PostAsync($"/api/v1/empresas/{empresaId}/solicitacoes", form);
    }

    [Fact]
    public async Task Escritorio_mantem_cadastro_e_cliente_apenas_consulta()
    {
        var colaborador = await LoginAsync("colaborador@contix.dev");
        var cliente = await LoginAsync("cliente@contix.dev");
        var empresaId = await EmpresaDemoAsync(cliente);
        var cpf = NovoCpf();

        var id = await CriarAsync(colaborador, empresaId, "Bruno Teste", cpf);

        var duplicado = await colaborador.PostAsJsonAsync($"/api/v1/empresas/{empresaId}/empregados",
            new { nome = "Outro", cpf, vinculo = "Empregado", situacao = "Ativo" });
        Assert.Equal(HttpStatusCode.Conflict, duplicado.StatusCode);

        var invalido = await colaborador.PostAsJsonAsync($"/api/v1/empresas/{empresaId}/empregados",
            new { nome = "", cpf = "111.111.111-11", vinculo = "Empregado", situacao = "Desligado" });
        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);
        var erros = (await JsonAsync(invalido))["errors"]!.AsObject();
        Assert.True(erros.ContainsKey("nome") && erros.ContainsKey("cpf") && erros.ContainsKey("dataDesligamento"));

        // Cliente vê a lista (para escolher nos formulários), mas não altera o cadastro.
        var lista = (await JsonAsync(await cliente.GetAsync($"/api/v1/empresas/{empresaId}/empregados?busca=bruno"))).AsArray();
        Assert.Equal(id, Assert.Single(lista)!["id"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.PostAsJsonAsync($"/api/v1/empresas/{empresaId}/empregados",
            new { nome = "X", vinculo = "Empregado", situacao = "Ativo" })).StatusCode);

        var desligado = await colaborador.PutAsJsonAsync($"/api/v1/empresas/{empresaId}/empregados/{id}", new
        {
            nome = "Bruno Teste", cpf, vinculo = "Empregado", situacao = "Desligado", dataDesligamento = "2026-10-31"
        });
        Assert.Equal("Desligado", (await JsonAsync(desligado))["situacao"]!.GetValue<string>());
        var ativos = (await JsonAsync(await cliente.GetAsync($"/api/v1/empresas/{empresaId}/empregados?ativos=true"))).AsArray();
        Assert.DoesNotContain(ativos, e => e!["id"]!.GetValue<string>() == id);

        // Desligado não pode ser escolhido em uma nova solicitação.
        var ferias = await AbrirAsync(cliente, empresaId, "AvisoFerias",
            $$"""{"empregadoId":"{{id}}","periodoAquisitivoInicio":"2025-01-01","periodoAquisitivoFim":"2025-12-31","dataInicio":"2026-12-01","dias":30}""");
        Assert.Equal(HttpStatusCode.BadRequest, ferias.StatusCode);
        Assert.NotNull((await JsonAsync(ferias))["errors"]!["dados.empregadoId"]);

        Assert.Equal(HttpStatusCode.NoContent, (await colaborador.DeleteAsync($"/api/v1/empresas/{empresaId}/empregados/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await colaborador.GetAsync($"/api/v1/empresas/{empresaId}/empregados/{id}")).StatusCode);
    }

    [Fact]
    public async Task Solicitacao_nao_aceita_empregado_de_outra_empresa()
    {
        var admin = await LoginAsync("admin@contix.dev");
        var cliente = await LoginAsync("cliente@contix.dev");
        var empresaId = await EmpresaDemoAsync(cliente);
        var outra = await admin.PostAsJsonAsync("/api/v1/empresas", new
        {
            razaoSocial = "Outra Empregadora Ltda", documento = "11222333000181", regimeTributario = "SimplesNacional", situacao = "Ativa"
        });
        var outraId = (await JsonAsync(outra))["id"]!.GetValue<string>();
        var deOutra = await CriarAsync(admin, outraId, "Empregado de Outra");

        var r = await AbrirAsync(cliente, empresaId, "CalculoRescisao",
            $$"""{"empregadoId":"{{deOutra}}","tipoDesligamento":"SemJustaCausa","dataDesligamento":"2026-11-30","avisoPrevio":"Indenizado"}""");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("Selecione um empregado cadastrado nesta empresa.",
            (await JsonAsync(r))["errors"]!["dados.empregadoId"]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task Concluir_cadastro_cria_empregado_e_concluir_rescisao_desliga()
    {
        var cliente = await LoginAsync("cliente@contix.dev");
        var colaborador = await LoginAsync("colaborador@contix.dev");
        var empresaId = await EmpresaDemoAsync(cliente);
        var cpf = NovoCpf();

        var cadastro = await AbrirAsync(cliente, empresaId, "CadastroEmpregado",
            $$"""{"nome":"Helena Admitida","cpf":"{{cpf}}","dataNascimento":"1995-05-05","cargo":"Caixa","salario":2100,"jornada":"44h","dataAdmissao":"2026-11-01"}""",
            anexo: true);
        Assert.Equal(HttpStatusCode.Created, cadastro.StatusCode);
        var cadastroId = (await JsonAsync(cadastro))["id"]!.GetValue<string>();

        var concluido = await JsonAsync(await colaborador.PostAsJsonAsync(
            $"/api/v1/empresas/{empresaId}/solicitacoes/{cadastroId}/status", new { status = "Concluida" }));
        var empregadoId = concluido["empregadoId"]!.GetValue<string>();
        Assert.Equal("Helena Admitida", concluido["empregadoNome"]!.GetValue<string>());

        var empregado = await JsonAsync(await cliente.GetAsync($"/api/v1/empresas/{empresaId}/empregados/{empregadoId}"));
        Assert.Equal(cpf, empregado["cpf"]!.GetValue<string>());
        Assert.Equal("Caixa", empregado["cargo"]!.GetValue<string>());
        Assert.Equal("2026-11-01", empregado["dataAdmissao"]!.GetValue<string>());
        Assert.Equal(cadastroId, empregado["solicitacaoOrigemId"]!.GetValue<string>());

        var rescisao = await AbrirAsync(cliente, empresaId, "AvisoRescisao",
            $$"""{"empregadoId":"{{empregadoId}}","tipoDesligamento":"PedidoDemissao","dataDesligamento":"2026-12-15","avisoPrevio":"Trabalhado"}""");
        var rescisaoId = (await JsonAsync(rescisao))["id"]!.GetValue<string>();

        // Rubricas também aparecem no filtro por empregado.
        var rubricas = await AbrirAsync(cliente, empresaId, "LancamentoRubricas",
            $$"""{"competencia":"2026-11-01","lancamentos":[{"empregadoId":"{{empregadoId}}","rubrica":"Faltas","quantidade":1}]}""");
        Assert.Equal(HttpStatusCode.Created, rubricas.StatusCode);
        var doEmpregado = (await JsonAsync(await cliente.GetAsync(
            $"/api/v1/empresas/{empresaId}/solicitacoes?empregadoId={empregadoId}&pageSize=100")))["items"]!.AsArray();
        Assert.Equal(3, doEmpregado.Count);

        await colaborador.PostAsJsonAsync($"/api/v1/empresas/{empresaId}/solicitacoes/{rescisaoId}/status", new { status = "Concluida" });
        empregado = await JsonAsync(await cliente.GetAsync($"/api/v1/empresas/{empresaId}/empregados/{empregadoId}"));
        Assert.Equal("Desligado", empregado["situacao"]!.GetValue<string>());
        Assert.Equal("2026-12-15", empregado["dataDesligamento"]!.GetValue<string>());
    }

    [Fact]
    public async Task Ficha_mostra_ferias_afastamento_e_rubricas_do_empregado()
    {
        var cliente = await LoginAsync("cliente@contix.dev");
        var colaborador = await LoginAsync("colaborador@contix.dev");
        var empresaId = await EmpresaDemoAsync(cliente);
        var id = await CriarAsync(colaborador, empresaId, "Ficha Teste", NovoCpf());
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);

        Assert.Equal(HttpStatusCode.Created, (await AbrirAsync(cliente, empresaId, "AvisoFerias",
            $$"""{"empregadoId":"{{id}}","periodoAquisitivoInicio":"2025-01-01","periodoAquisitivoFim":"2025-12-31","dataInicio":"{{hoje.AddDays(-2):yyyy-MM-dd}}","dias":10}""")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await AbrirAsync(cliente, empresaId, "Afastamento",
            $$"""{"empregadoId":"{{id}}","motivo":"Doenca","dataInicio":"2025-03-01","previsaoRetorno":"2025-03-05"}""", anexo: true)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await AbrirAsync(cliente, empresaId, "LancamentoRubricas",
            $$"""{"competencia":"2026-10-01","lancamentos":[{"empregadoId":"{{id}}","rubrica":"Comissões","valor":300}]}""")).StatusCode);

        var ficha = await JsonAsync(await cliente.GetAsync($"/api/v1/empresas/{empresaId}/empregados/{id}/ficha"));
        Assert.Equal("Ficha Teste", ficha["empregado"]!["nome"]!.GetValue<string>());
        Assert.Equal(3, ficha["totalSolicitacoes"]!.GetValue<int>());
        var feriasHoje = ficha["hoje"]!["feriasEmCurso"]!;
        Assert.Equal(hoje.AddDays(7).ToString("yyyy-MM-dd"), feriasHoje["fim"]!.GetValue<string>());
        Assert.Null(ficha["hoje"]!["afastamentoEmCurso"]);
        Assert.Equal("Doenca", ficha["afastamentos"]![0]!["motivo"]!.GetValue<string>());
        Assert.Equal(300m, ficha["rubricas"]![0]!["valor"]!.GetValue<decimal>());
    }

    [Fact]
    public async Task Rubricas_por_planilha_modelo_e_validacao_por_linha()
    {
        var colaborador = await LoginAsync("colaborador@contix.dev");
        var cliente = await LoginAsync("cliente@contix.dev");
        var empresaId = await EmpresaDemoAsync(cliente);
        var cpf = NovoCpf();
        await CriarAsync(colaborador, empresaId, "Planilha Teste", cpf);

        var modelo = await cliente.GetAsync($"/api/v1/empresas/{empresaId}/rubricas/modelo?formato=Csv");
        Assert.Equal(HttpStatusCode.OK, modelo.StatusCode);
        Assert.Contains(cpf[..3], await modelo.Content.ReadAsStringAsync());
        var xlsx = await cliente.GetAsync($"/api/v1/empresas/{empresaId}/rubricas/modelo");
        Assert.Equal(HttpStatusCode.OK, xlsx.StatusCode);
        Assert.Equal((byte)'P', (await xlsx.Content.ReadAsByteArrayAsync())[0]); // zip

        var csv = "CPF;Matrícula;Empregado;Rubrica;Quantidade;Valor\r\n"
                  + $"{cpf};;Planilha Teste;Horas extras 50%;10;\r\n"
                  + $"{cpf};;Planilha Teste;Comissão;;1.234,56\r\n"
                  + "000.000.000-00;;Ninguém;Faltas;1;\r\n"
                  + $"{cpf};;Planilha Teste;;abc;\r\n";
        var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(csv)) { Headers = { ContentType = new("text/csv") } }, "arquivo", "rubricas.csv" }
        };
        var r = await JsonAsync(await cliente.PostAsync($"/api/v1/empresas/{empresaId}/rubricas/importacao", form));
        Assert.Equal(4, r["totalLinhas"]!.GetValue<int>());
        var validos = r["validos"]!.AsArray();
        Assert.Equal(2, validos.Count);
        Assert.Equal(1234.56m, validos[1]!["valor"]!.GetValue<decimal>());
        var erros = r["erros"]!.AsArray();
        Assert.Contains(erros, e => e!["linha"]!.GetValue<int>() == 4 && e["campo"]!.GetValue<string>() == "empregado");
        Assert.Contains(erros, e => e!["linha"]!.GetValue<int>() == 5 && e["campo"]!.GetValue<string>() == "rubrica");
        Assert.Contains(erros, e => e!["linha"]!.GetValue<int>() == 5 && e["campo"]!.GetValue<string>() == "quantidade");

        var invalido = new MultipartFormDataContent
        {
            { new ByteArrayContent("x"u8.ToArray()) { Headers = { ContentType = new("application/pdf") } }, "arquivo", "rubricas.pdf" }
        };
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await cliente.PostAsync($"/api/v1/empresas/{empresaId}/rubricas/importacao", invalido)).StatusCode);
    }
}
