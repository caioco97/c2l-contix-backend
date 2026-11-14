using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Contix.IntegrationTests;

/// <summary>Tela Meu perfil (CTX-117): dados, foto e troca de senha do usuário logado.</summary>
[Collection(nameof(ApiCollection))]
public sealed class PerfilTests(ContixApiFactory factory)
{
    private const string Senha = "Contix@123";

    private async Task<(HttpClient Http, JsonObject Sessao)> LoginAsync(string email, string senha = Senha)
    {
        var http = factory.CreateClient();
        var response = await http.PostAsJsonAsync("/api/v1/auth/login", new { email, senha });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body["accessToken"]!.GetValue<string>());
        return (http, body);
    }

    private static async Task<JsonNode> JsonAsync(HttpResponseMessage r) => JsonNode.Parse(await r.Content.ReadAsStringAsync())!;

    /// <summary>Cria um colaborador descartável para não alterar os usuários de demonstração.</summary>
    private async Task<string> NovoColaboradorAsync(string senha = Senha)
    {
        var (admin, _) = await LoginAsync("admin@contix.dev");
        var email = $"perfil-{Guid.NewGuid():N}@contix.dev";
        var r = await admin.PostAsJsonAsync("/api/v1/usuarios", new { nome = "Perfil Teste", email, perfil = "Colaborador", senha });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return email;
    }

    [Fact]
    public async Task Atualiza_nome_email_e_telefone()
    {
        var email = await NovoColaboradorAsync();
        var (http, _) = await LoginAsync(email);

        var perfil = await JsonAsync(await http.GetAsync("/api/v1/perfil"));
        Assert.Equal(email, perfil["email"]!.GetValue<string>());
        Assert.Null(perfil["telefone"]);

        var novoEmail = $"novo-{Guid.NewGuid():N}@contix.dev";
        var r = await http.PutAsJsonAsync("/api/v1/perfil", new { nome = "  Nome Novo ", email = novoEmail.ToUpperInvariant(), telefone = "(11) 98765-4321" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var atualizado = await JsonAsync(r);
        Assert.Equal("Nome Novo", atualizado["nome"]!.GetValue<string>());
        Assert.Equal(novoEmail, atualizado["email"]!.GetValue<string>());
        Assert.Equal("11987654321", atualizado["telefone"]!.GetValue<string>());

        // /auth/me reflete a mudança e o login passa a ser pelo e-mail novo.
        var me = await JsonAsync(await http.GetAsync("/api/v1/auth/me"));
        Assert.Equal("Nome Novo", me["nome"]!.GetValue<string>());
        await LoginAsync(novoEmail);
    }

    [Fact]
    public async Task Rejeita_email_de_outra_conta_e_telefone_invalido()
    {
        var (http, _) = await LoginAsync(await NovoColaboradorAsync());

        var duplicado = await http.PutAsJsonAsync("/api/v1/perfil", new { nome = "X", email = "admin@contix.dev", telefone = (string?)null });
        Assert.Equal(HttpStatusCode.Conflict, duplicado.StatusCode);

        var invalido = await http.PutAsJsonAsync("/api/v1/perfil", new { nome = "X", email = "x@contix.dev", telefone = "123" });
        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);
        Assert.NotNull((await JsonAsync(invalido))["errors"]!["telefone"]);
    }

    [Fact]
    public async Task Cliente_tambem_acessa_o_proprio_perfil()
    {
        var (http, _) = await LoginAsync("cliente@contix.dev");
        var perfil = await JsonAsync(await http.GetAsync("/api/v1/perfil"));
        Assert.Equal("cliente@contix.dev", perfil["email"]!.GetValue<string>());
        Assert.Equal("Cliente", perfil["perfil"]!.GetValue<string>());
    }

    [Fact]
    public async Task Envia_troca_e_remove_a_foto()
    {
        var (http, _) = await LoginAsync(await NovoColaboradorAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/api/v1/perfil/foto")).StatusCode);

        static MultipartFormDataContent Foto(string contentType, byte[] bytes) => new()
        {
            { new ByteArrayContent(bytes) { Headers = { ContentType = new(contentType) } }, "arquivo", "foto.png" }
        };

        var svg = await http.PutAsync("/api/v1/perfil/foto", Foto("image/svg+xml", "<svg/>"u8.ToArray()));
        Assert.Equal(HttpStatusCode.BadRequest, svg.StatusCode);

        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };
        var enviada = await http.PutAsync("/api/v1/perfil/foto", Foto("image/png", png));
        Assert.Equal(HttpStatusCode.OK, enviada.StatusCode);
        Assert.NotNull((await JsonAsync(enviada))["fotoVersao"]);

        var baixada = await http.GetAsync("/api/v1/perfil/foto");
        Assert.Equal(HttpStatusCode.OK, baixada.StatusCode);
        Assert.Equal("image/png", baixada.Content.Headers.ContentType!.MediaType);
        Assert.Equal(png, await baixada.Content.ReadAsByteArrayAsync());
        Assert.NotNull((await JsonAsync(await http.GetAsync("/api/v1/auth/me")))["fotoVersao"]);

        var removida = await http.DeleteAsync("/api/v1/perfil/foto");
        Assert.Equal(HttpStatusCode.OK, removida.StatusCode);
        Assert.Null((await JsonAsync(removida))["fotoVersao"]);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/api/v1/perfil/foto")).StatusCode);
    }

    [Fact]
    public async Task Troca_senha_exige_a_atual_e_encerra_as_outras_sessoes()
    {
        var email = await NovoColaboradorAsync();
        var (http, _) = await LoginAsync(email);
        var (_, outraSessao) = await LoginAsync(email);

        var errada = await http.PostAsJsonAsync("/api/v1/auth/trocar-senha", new { senhaAtual = "Errada@123", novaSenha = "Nova@1234" });
        Assert.Equal(HttpStatusCode.BadRequest, errada.StatusCode);
        Assert.NotNull((await JsonAsync(errada))["errors"]!["senhaAtual"]);

        var fraca = await http.PostAsJsonAsync("/api/v1/auth/trocar-senha", new { senhaAtual = Senha, novaSenha = "fraca" });
        Assert.Equal(HttpStatusCode.BadRequest, fraca.StatusCode);

        var ok = await http.PostAsJsonAsync("/api/v1/auth/trocar-senha", new { senhaAtual = Senha, novaSenha = "Nova@1234" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var novaSessao = await JsonAsync(ok);
        Assert.False(string.IsNullOrEmpty(novaSessao["refreshToken"]!.GetValue<string>()));

        // A outra sessão perde o refresh; a nova sessão continua valendo.
        var anon = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken = outraSessao["refreshToken"]!.GetValue<string>() })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anon.PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken = novaSessao["refreshToken"]!.GetValue<string>() })).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/api/v1/auth/login", new { email, senha = Senha })).StatusCode);
        await LoginAsync(email, "Nova@1234");
    }
}
