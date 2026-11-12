using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Contix.Application.Features.Notificacoes;
using Contix.Infrastructure.Email;
using Contix.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Contix.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed partial class NotificacoesTests(ContixApiFactory factory)
{
    private const string Senha = "Contix@123";

    private async Task<HttpClient> LoginAsync(string email, string senha = Senha)
    {
        var http = factory.CreateClient();
        var response = await http.PostAsJsonAsync("/api/v1/auth/login", new { email, senha });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!["accessToken"]!.GetValue<string>());
        return http;
    }

    private static async Task<JsonNode> JsonAsync(HttpResponseMessage r) => JsonNode.Parse(await r.Content.ReadAsStringAsync())!;

    private async Task<T> NoBancoAsync<T>(Func<ContixDbContext, Task<T>> consulta)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await consulta(scope.ServiceProvider.GetRequiredService<ContixDbContext>());
    }

    private async Task<(string EmpresaId, string PastaId)> EmpresaEPastaAsync(HttpClient http, string pasta = "Fiscal")
    {
        var empresaId = (await JsonAsync(await http.GetAsync("/api/v1/empresas?busca=DEMO")))["items"]![0]!["id"]!.GetValue<string>();
        var pastas = (await JsonAsync(await http.GetAsync($"/api/v1/empresas/{empresaId}/pastas"))).AsArray();
        return (empresaId, pastas.First(p => p!["nome"]!.GetValue<string>() == pasta)!["id"]!.GetValue<string>());
    }

    private static async Task<string> UploadAsync(HttpClient http, string empresaId, string pastaId, string nome, DateOnly? vencimento = null)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(pastaId), "pastaId" },
            { new ByteArrayContent("conteúdo"u8.ToArray()) { Headers = { ContentType = new("text/plain") } }, "arquivos", nome }
        };
        if (vencimento is { } v) form.Add(new StringContent(v.ToString("yyyy-MM-dd")), "dataVencimento");
        var r = await http.PostAsync($"/api/v1/empresas/{empresaId}/documentos", form);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await JsonAsync(r))[0]!["id"]!.GetValue<string>();
    }

    private static async Task<JsonArray> NotificacoesAsync(HttpClient http) =>
        (await JsonAsync(await http.GetAsync("/api/v1/notificacoes?pageSize=100")))["items"]!.AsArray();

    [GeneratedRegex(@"redefinir-senha\?token=([A-Za-z0-9_\-]+)")]
    private static partial Regex TokenRegex();

    /* ---------- Recuperação de senha ---------- */

    [Fact]
    public async Task Recuperacao_de_senha_ponta_a_ponta()
    {
        var admin = await LoginAsync("admin@contix.dev");
        var email = $"rec-{Guid.NewGuid():N}@contix.dev";
        var criado = await admin.PostAsJsonAsync("/api/v1/usuarios",
            new { nome = "Recupera", email, perfil = "Colaborador", senha = "Antiga@123" });
        Assert.Equal(HttpStatusCode.Created, criado.StatusCode);
        var sessaoAntiga = await (await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email, senha = "Antiga@123" }))
            .Content.ReadFromJsonAsync<JsonObject>();

        var anon = factory.CreateClient();
        // Resposta neutra: e-mail inexistente também recebe 202, e nada é enfileirado.
        Assert.Equal(HttpStatusCode.Accepted,
            (await anon.PostAsJsonAsync("/api/v1/auth/esqueci-senha", new { email = "nao-existe@contix.dev" })).StatusCode);
        Assert.False(await NoBancoAsync(db => db.EmailsPendentes.AnyAsync(e => e.Para == "nao-existe@contix.dev")));

        Assert.Equal(HttpStatusCode.Accepted, (await anon.PostAsJsonAsync("/api/v1/auth/esqueci-senha", new { email })).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await anon.PostAsJsonAsync("/api/v1/auth/esqueci-senha", new { email })).StatusCode);

        var corpos = await NoBancoAsync(db => db.EmailsPendentes.Where(e => e.Para == email)
            .OrderBy(e => e.CreatedAt).Select(e => e.CorpoTexto).ToListAsync());
        Assert.Equal(2, corpos.Count);
        Assert.Contains("https://app.contix.test/redefinir-senha?token=", corpos[1]);
        var primeiroToken = TokenRegex().Match(corpos[0]).Groups[1].Value;
        var token = TokenRegex().Match(corpos[1]).Groups[1].Value;

        // Só o link mais recente vale.
        var antigo = await anon.PostAsJsonAsync("/api/v1/auth/redefinir-senha", new { token = primeiroToken, novaSenha = "Nova@1234" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, antigo.StatusCode);

        var fraca = await anon.PostAsJsonAsync("/api/v1/auth/redefinir-senha", new { token, novaSenha = "123" });
        Assert.Equal(HttpStatusCode.BadRequest, fraca.StatusCode);

        var ok = await anon.PostAsJsonAsync("/api/v1/auth/redefinir-senha", new { token, novaSenha = "Nova@1234" });
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);

        // Uso único.
        var reuso = await anon.PostAsJsonAsync("/api/v1/auth/redefinir-senha", new { token, novaSenha = "Outra@1234" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, reuso.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anon.PostAsJsonAsync("/api/v1/auth/login", new { email, senha = "Antiga@123" })).StatusCode);
        await LoginAsync(email, "Nova@1234");

        // Sessões abertas com a senha antiga foram encerradas.
        var refresh = await anon.PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken = sessaoAntiga!["refreshToken"]!.GetValue<string>() });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Fila_de_emails_marca_como_enviado()
    {
        await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/esqueci-senha", new { email = "colaborador@contix.dev" });
        var processor = factory.Services.GetRequiredService<EmailOutboxProcessor>();
        while (await processor.ProcessarAsync() > 0) { }

        Assert.False(await NoBancoAsync(db => db.EmailsPendentes.AnyAsync(e => e.EnviadoEm == null && e.Tentativas == 0)));
    }

    /* ---------- Notificações ---------- */

    [Fact]
    public async Task Documento_publicado_notifica_cliente_e_upload_do_cliente_notifica_escritorio()
    {
        var admin = await LoginAsync("admin@contix.dev");
        var cliente = await LoginAsync("cliente@contix.dev");
        var (empresaId, pastaId) = await EmpresaEPastaAsync(admin);
        var nome = $"guia-{Guid.NewGuid():N}";

        var docId = await UploadAsync(admin, empresaId, pastaId, nome + ".txt");

        var doCliente = await NotificacoesAsync(cliente);
        var n = doCliente.First(x => x!["referenciaId"]?.GetValue<string>() == docId)!;
        Assert.Equal("NovoDocumento", n["tipo"]!.GetValue<string>());
        Assert.Equal($"/documentos/{pastaId}", n["link"]!.GetValue<string>());
        Assert.Equal(empresaId, n["empresaId"]!.GetValue<string>());
        Assert.False(n["lida"]!.GetValue<bool>());
        // Quem publicou não é notificado.
        Assert.DoesNotContain(await NotificacoesAsync(admin), x => x!["referenciaId"]?.GetValue<string>() == docId);
        Assert.True(await NoBancoAsync(db => db.EmailsPendentes.AnyAsync(e => e.Para == "cliente@contix.dev" && e.Assunto.Contains(nome))));

        // Cliente envia para pasta que permite upload (Financeiro, no seed): escritório é avisado.
        var (_, pastaCliente) = await EmpresaEPastaAsync(admin, "Financeiro");
        var docCliente = await UploadAsync(cliente, empresaId, pastaCliente, "extrato.txt");
        Assert.Contains(await NotificacoesAsync(admin), x => x!["referenciaId"]?.GetValue<string>() == docCliente);

        // Marcar como lida e contador.
        var antes = (await JsonAsync(await cliente.GetAsync("/api/v1/notificacoes/contador")))["naoLidas"]!.GetValue<int>();
        Assert.True(antes > 0);
        Assert.Equal(HttpStatusCode.NoContent, (await cliente.PostAsync($"/api/v1/notificacoes/{n["id"]}/lida", null)).StatusCode);
        var depois = (await JsonAsync(await cliente.GetAsync("/api/v1/notificacoes/contador")))["naoLidas"]!.GetValue<int>();
        Assert.Equal(antes - 1, depois);

        // Ninguém marca a notificação de outro usuário.
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"/api/v1/notificacoes/{n["id"]}/lida", null)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await cliente.PostAsync("/api/v1/notificacoes/lidas", null)).StatusCode);
        Assert.Equal(0, (await JsonAsync(await cliente.GetAsync("/api/v1/notificacoes/contador")))["naoLidas"]!.GetValue<int>());
    }

    [Fact]
    public async Task Preferencia_desliga_email_mas_mantem_notificacao_in_app()
    {
        var admin = await LoginAsync("admin@contix.dev");
        var cliente = await LoginAsync("cliente@contix.dev");

        var prefs = (await JsonAsync(await cliente.GetAsync("/api/v1/notificacoes/preferencias"))).AsArray();
        Assert.All(prefs, p => Assert.True(p!["receberEmail"]!.GetValue<bool>()));

        var put = await cliente.PutAsJsonAsync("/api/v1/notificacoes/preferencias",
            new { preferencias = new[] { new { tipo = "NovoDocumento", receberEmail = false } } });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        try
        {
            var (empresaId, pastaId) = await EmpresaEPastaAsync(admin);
            var nome = $"sem-email-{Guid.NewGuid():N}";
            var docId = await UploadAsync(admin, empresaId, pastaId, nome + ".txt");

            Assert.Contains(await NotificacoesAsync(cliente), x => x!["referenciaId"]?.GetValue<string>() == docId);
            Assert.False(await NoBancoAsync(db => db.EmailsPendentes.AnyAsync(e => e.Para == "cliente@contix.dev" && e.Assunto.Contains(nome))));

            // "Enviar ao cliente" é explícito: envia o e-mail mesmo com a preferência desligada.
            var envio = await admin.PostAsJsonAsync($"/api/v1/empresas/{empresaId}/documentos/enviar",
                new { documentoIds = new[] { docId }, mensagem = "Segue a guia do mês." });
            Assert.Equal(HttpStatusCode.OK, envio.StatusCode);
            Assert.Equal(1, (await JsonAsync(envio))["destinatarios"]!.GetValue<int>());
            Assert.True(await NoBancoAsync(db => db.EmailsPendentes.AnyAsync(e =>
                e.Para == "cliente@contix.dev" && e.Assunto.Contains(nome) && e.CorpoTexto.Contains("Segue a guia do mês."))));

            Assert.Equal(HttpStatusCode.Forbidden, (await cliente.PostAsJsonAsync($"/api/v1/empresas/{empresaId}/documentos/enviar",
                new { documentoIds = new[] { docId } })).StatusCode);
        }
        finally
        {
            await cliente.PutAsJsonAsync("/api/v1/notificacoes/preferencias",
                new { preferencias = new[] { new { tipo = "NovoDocumento", receberEmail = true } } });
        }
    }

    [Fact]
    public async Task Mudanca_de_status_da_solicitacao_notifica_o_cliente()
    {
        var cliente = await LoginAsync("cliente@contix.dev");
        var colaborador = await LoginAsync("colaborador@contix.dev");
        var (empresaId, _) = await EmpresaEPastaAsync(colaborador);

        using var form = new MultipartFormDataContent
        {
            { new StringContent("Geral"), "tipo" },
            { new StringContent("Dúvida sobre DAS"), "assunto" },
            { new StringContent("Fiscal"), "departamento" },
            { new StringContent("Qual o valor deste mês?"), "descricao" }
        };
        var abertura = await cliente.PostAsync($"/api/v1/empresas/{empresaId}/solicitacoes", form);
        Assert.Equal(HttpStatusCode.Created, abertura.StatusCode);
        var id = (await JsonAsync(abertura))["id"]!.GetValue<string>();

        // Abertura pelo cliente avisa a equipe, com link para o atendimento.
        var daEquipe = (await NotificacoesAsync(colaborador)).First(x => x!["referenciaId"]?.GetValue<string>() == id)!;
        Assert.Equal("NovaSolicitacao", daEquipe["tipo"]!.GetValue<string>());
        Assert.Equal($"/atendimento/{empresaId}/{id}", daEquipe["link"]!.GetValue<string>());

        var status = await colaborador.PostAsJsonAsync($"/api/v1/empresas/{empresaId}/solicitacoes/{id}/status",
            new { status = "EmAnalise" });
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);

        var doCliente = (await NotificacoesAsync(cliente)).First(x =>
            x!["referenciaId"]?.GetValue<string>() == id && x["tipo"]!.GetValue<string>() == "SolicitacaoAtualizada")!;
        Assert.Contains("Em análise", doCliente["titulo"]!.GetValue<string>());
        Assert.Equal($"/portal/solicitacoes/{id}", doCliente["link"]!.GetValue<string>());
    }

    [Fact]
    public async Task Lembrete_de_vencimento_e_gerado_uma_unica_vez()
    {
        var admin = await LoginAsync("admin@contix.dev");
        var cliente = await LoginAsync("cliente@contix.dev");
        var (empresaId, pastaId) = await EmpresaEPastaAsync(admin);
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
        var proximo = await UploadAsync(admin, empresaId, pastaId, "das-proximo.txt", hoje.AddDays(2));
        var distante = await UploadAsync(admin, empresaId, pastaId, "das-distante.txt", hoje.AddDays(20));

        async Task ProcessarAsync()
        {
            await using var scope = factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ILembreteVencimentoService>().ProcessarAsync();
        }

        await ProcessarAsync();
        await ProcessarAsync();

        var lembretes = (await NotificacoesAsync(cliente))
            .Where(x => x!["tipo"]!.GetValue<string>() == "VencimentoProximo").ToList();
        var doProximo = Assert.Single(lembretes, x => x!["referenciaId"]?.GetValue<string>() == proximo)!;
        Assert.Contains("vence em 2 dias", doProximo["mensagem"]!.GetValue<string>());
        Assert.DoesNotContain(lembretes, x => x!["referenciaId"]?.GetValue<string>() == distante);
    }
}
