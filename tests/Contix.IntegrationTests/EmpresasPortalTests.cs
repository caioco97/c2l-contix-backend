using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Contix.Application.Features.Notificacoes;
using Contix.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Contix.IntegrationTests;

/// <summary>Usuários do portal (convite e permissões), painel e histórico da empresa, avisos de notícia e CND.</summary>
[Collection(nameof(ApiCollection))]
public sealed partial class EmpresasPortalTests(ContixApiFactory factory)
{
    private const string NovaSenha = "Portal@2026";

    private async Task<HttpClient> LoginAsync(string email, string senha = "Contix@123")
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

    [GeneratedRegex(@"redefinir-senha\?token=([A-Za-z0-9_\-]+)&convite=1")]
    private static partial Regex ConviteRegex();

    /// <summary>CNPJ válido e aleatório, para criar empresas isoladas por teste.</summary>
    private static string NovoCnpj()
    {
        var d = Enumerable.Range(0, 12).Select(_ => Random.Shared.Next(10)).ToList();
        int Dv(IReadOnlyList<int> n, int[] pesos) { var r = n.Select((x, i) => x * pesos[i]).Sum() % 11; return r < 2 ? 0 : 11 - r; }
        d.Add(Dv(d, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]));
        d.Add(Dv(d, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]));
        return string.Concat(d);
    }

    private static async Task<string> NovaEmpresaAsync(HttpClient escritorio, string nome)
    {
        var r = await escritorio.PostAsJsonAsync("/api/v1/empresas", new
        {
            razaoSocial = nome, documento = NovoCnpj(), regimeTributario = "SimplesNacional", situacao = "Ativa"
        });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await JsonAsync(r))["id"]!.GetValue<string>();
    }

    private static async Task<Dictionary<string, string>> PastasAsync(HttpClient http, string empresaId) =>
        (await JsonAsync(await http.GetAsync($"/api/v1/empresas/{empresaId}/pastas"))).AsArray()
        .ToDictionary(p => p!["nome"]!.GetValue<string>(), p => p!["id"]!.GetValue<string>());

    private static async Task<string> UploadAsync(HttpClient http, string empresaId, string pastaId, string nome)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(pastaId), "pastaId" },
            { new ByteArrayContent("conteúdo"u8.ToArray()) { Headers = { ContentType = new("text/plain") } }, "arquivos", nome }
        };
        var r = await http.PostAsync($"/api/v1/empresas/{empresaId}/documentos", form);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await JsonAsync(r))[0]!["id"]!.GetValue<string>();
    }

    /// <summary>Convida, aceita o convite pelo link do e-mail e devolve o usuário logado.</summary>
    private async Task<(HttpClient Http, string UsuarioId, string Email)> ConvidarEAceitarAsync(
        HttpClient escritorio, string empresaId, object permissoes)
    {
        var email = $"portal-{Guid.NewGuid():N}@cliente.test";
        var body = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(permissoes))!.AsObject();
        body["nome"] = "Usuário do Portal";
        body["email"] = email;
        var convite = await escritorio.PostAsJsonAsync($"/api/v1/empresas/{empresaId}/usuarios", body);
        Assert.Equal(HttpStatusCode.Created, convite.StatusCode);
        var usuario = await JsonAsync(convite);
        Assert.True(usuario["convitePendente"]!.GetValue<bool>());

        var corpo = await NoBancoAsync(db => db.EmailsPendentes.Where(e => e.Para == email).Select(e => e.CorpoTexto).SingleAsync());
        var token = ConviteRegex().Match(corpo).Groups[1].Value;
        Assert.NotEmpty(token);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.CreateClient()
            .PostAsJsonAsync("/api/v1/auth/redefinir-senha", new { token, novaSenha = NovaSenha })).StatusCode);

        return (await LoginAsync(email, NovaSenha), usuario["id"]!.GetValue<string>(), email);
    }

    [Fact]
    public async Task Identidade_visual_da_empresa_chega_ao_portal()
    {
        var colaborador = await LoginAsync("colaborador@contix.dev");
        var empresaId = await NovaEmpresaAsync(colaborador, "Marca Própria Ltda");
        var (cliente, _, _) = await ConvidarEAceitarAsync(colaborador, empresaId,
            new { modulos = new[] { "Documentos" }, todasAsPastas = true });

        // Cores inválidas são recusadas.
        Assert.Equal(HttpStatusCode.BadRequest, (await colaborador.PutAsJsonAsync(
            $"/api/v1/empresas/{empresaId}/identidade-visual", new { corPrimaria = "azul" })).StatusCode);

        var cores = await colaborador.PutAsJsonAsync($"/api/v1/empresas/{empresaId}/identidade-visual",
            new { corPrimaria = "#1E40AF", corSecundaria = "#f59e0b" });
        Assert.Equal(HttpStatusCode.OK, cores.StatusCode);
        Assert.Equal("#1e40af", (await JsonAsync(cores))["corPrimaria"]!.GetValue<string>());

        // Só imagens PNG/JPG/WEBP.
        using (var svg = new MultipartFormDataContent
               { { new ByteArrayContent("<svg/>"u8.ToArray()) { Headers = { ContentType = new("image/svg+xml") } }, "arquivo", "logo.svg" } })
            Assert.Equal(HttpStatusCode.BadRequest, (await colaborador.PutAsync($"/api/v1/empresas/{empresaId}/identidade-visual/logo", svg)).StatusCode);

        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
        using (var form = new MultipartFormDataContent
               { { new ByteArrayContent(png) { Headers = { ContentType = new("image/png") } }, "arquivo", "logo.png" } })
        {
            var r = await colaborador.PutAsync($"/api/v1/empresas/{empresaId}/identidade-visual/logo", form);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.True((await JsonAsync(r))["possuiLogo"]!.GetValue<bool>());
        }

        // O portal recebe a identidade na lista de empresas e baixa o logotipo.
        var empresa = (await JsonAsync(await cliente.GetAsync("/api/v1/empresas")))["items"]!.AsArray().Single()!;
        var visual = empresa["identidadeVisual"]!;
        Assert.Equal("#1e40af", visual["corPrimaria"]!.GetValue<string>());
        Assert.Equal("#f59e0b", visual["corSecundaria"]!.GetValue<string>());
        Assert.True(visual["possuiLogo"]!.GetValue<bool>());
        Assert.NotNull(visual["logoVersao"]);

        var logo = await cliente.GetAsync($"/api/v1/empresas/{empresaId}/identidade-visual/logo");
        Assert.Equal(HttpStatusCode.OK, logo.StatusCode);
        Assert.Equal("image/png", logo.Content.Headers.ContentType!.MediaType);
        Assert.Equal(png, await logo.Content.ReadAsByteArrayAsync());

        // O cliente não altera a identidade visual.
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.PutAsJsonAsync(
            $"/api/v1/empresas/{empresaId}/identidade-visual", new { corPrimaria = "#000000" })).StatusCode);

        // Outra empresa não enxerga o logotipo desta.
        var outro = await LoginAsync("cliente@contix.dev");
        Assert.Equal(HttpStatusCode.NotFound, (await outro.GetAsync($"/api/v1/empresas/{empresaId}/identidade-visual/logo")).StatusCode);

        // Remover o logotipo e as cores volta ao padrão.
        Assert.Equal(HttpStatusCode.OK, (await colaborador.DeleteAsync($"/api/v1/empresas/{empresaId}/identidade-visual/logo")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await cliente.GetAsync($"/api/v1/empresas/{empresaId}/identidade-visual/logo")).StatusCode);
        var limpa = await JsonAsync(await colaborador.PutAsJsonAsync($"/api/v1/empresas/{empresaId}/identidade-visual",
            new { corPrimaria = (string?)null, corSecundaria = "" }));
        Assert.Null(limpa["corPrimaria"]);
        Assert.Null(limpa["corSecundaria"]);
        Assert.False(limpa["possuiLogo"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Convite_por_email_e_permissao_por_modulo()
    {
        var colaborador = await LoginAsync("colaborador@contix.dev");
        var empresaId = await NovaEmpresaAsync(colaborador, "Somente Solicitações Ltda");
        var (cliente, usuarioId, _) = await ConvidarEAceitarAsync(colaborador, empresaId,
            new { modulos = new[] { "Solicitacoes" }, todasAsPastas = true });

        // Convite aceito: deixa de estar pendente.
        var lista = (await JsonAsync(await colaborador.GetAsync($"/api/v1/empresas/{empresaId}/usuarios"))).AsArray();
        var item = lista.Single(u => u!["id"]!.GetValue<string>() == usuarioId)!;
        Assert.False(item["convitePendente"]!.GetValue<bool>());
        Assert.Equal(["Solicitacoes"], item["modulos"]!.AsArray().Select(m => m!.GetValue<string>()));

        // A empresa informa os módulos liberados ao portal.
        var empresas = (await JsonAsync(await cliente.GetAsync("/api/v1/empresas")))["items"]!.AsArray();
        Assert.Equal(["Solicitacoes"], empresas.Single()!["modulos"]!.AsArray().Select(m => m!.GetValue<string>()));

        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync($"/api/v1/empresas/{empresaId}/documentos")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync($"/api/v1/empresas/{empresaId}/pastas")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync($"/api/v1/empresas/{empresaId}/solicitacoes")).StatusCode);

        // Liberar documentos passa a permitir o acesso.
        Assert.Equal(HttpStatusCode.OK, (await colaborador.PutAsJsonAsync($"/api/v1/empresas/{empresaId}/usuarios/{usuarioId}",
            new { modulos = new[] { "Solicitacoes", "Documentos" }, todasAsPastas = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync($"/api/v1/empresas/{empresaId}/documentos")).StatusCode);

        // O cliente não gerencia usuários.
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync($"/api/v1/empresas/{empresaId}/usuarios")).StatusCode);

        // Inativar encerra o acesso.
        Assert.Equal(HttpStatusCode.OK, (await colaborador.PutAsJsonAsync($"/api/v1/usuarios/{usuarioId}/situacao", new { ativo = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login",
            new { email = item["email"]!.GetValue<string>(), senha = NovaSenha })).StatusCode);
    }

    [Fact]
    public async Task Permissao_por_pasta_restringe_documentos_e_avisos()
    {
        var admin = await LoginAsync("admin@contix.dev");
        var empresaId = await NovaEmpresaAsync(admin, "Só Fiscal S.A.");
        var pastas = await PastasAsync(admin, empresaId);

        // Pasta que não é raiz da empresa é recusada.
        var sub = await JsonAsync(await admin.PostAsJsonAsync($"/api/v1/empresas/{empresaId}/pastas",
            new { nome = "Guias", pastaPaiId = pastas["Fiscal"] }));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/v1/empresas/{empresaId}/usuarios", new
        {
            nome = "X", email = $"x-{Guid.NewGuid():N}@cliente.test", modulos = new[] { "Documentos" },
            todasAsPastas = false, pastaIds = new[] { sub["id"]!.GetValue<string>() }
        })).StatusCode);

        var (cliente, usuarioId, _) = await ConvidarEAceitarAsync(admin, empresaId,
            new { modulos = new[] { "Documentos" }, todasAsPastas = false, pastaIds = new[] { pastas["Fiscal"] } });

        var fiscal = await UploadAsync(admin, empresaId, sub["id"]!.GetValue<string>(), "guia-icms.txt");
        var pessoal = await UploadAsync(admin, empresaId, pastas["Pessoal"], "holerite.txt");

        // Árvore: só Fiscal e a subpasta dela.
        var arvore = (await JsonAsync(await cliente.GetAsync($"/api/v1/empresas/{empresaId}/pastas"))).AsArray();
        Assert.Equal(["Fiscal"], arvore.Select(p => p!["nome"]!.GetValue<string>()));
        Assert.Equal(["Guias"], arvore[0]!["subpastas"]!.AsArray().Select(p => p!["nome"]!.GetValue<string>()));

        var docs = (await JsonAsync(await cliente.GetAsync($"/api/v1/empresas/{empresaId}/documentos")))["items"]!.AsArray();
        Assert.Equal([fiscal], docs.Select(d => d!["id"]!.GetValue<string>()));
        Assert.Equal(HttpStatusCode.NotFound, (await cliente.GetAsync($"/api/v1/empresas/{empresaId}/documentos/{pessoal}/download")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await cliente.GetAsync($"/api/v1/empresas/{empresaId}/documentos?pastaId={pastas["Pessoal"]}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync($"/api/v1/empresas/{empresaId}/documentos/{fiscal}/download")).StatusCode);

        // Aviso de novo documento só para a pasta que ele enxerga.
        var notificacoes = (await JsonAsync(await cliente.GetAsync("/api/v1/notificacoes?pageSize=50")))["items"]!.AsArray();
        var referencias = notificacoes.Select(n => n!["referenciaId"]?.GetValue<string>()).ToList();
        Assert.Contains(fiscal, referencias);
        Assert.DoesNotContain(pessoal, referencias);

        // Painel e histórico da empresa (escritório).
        var painel = await JsonAsync(await admin.GetAsync($"/api/v1/empresas/{empresaId}/painel"));
        Assert.Equal(1, painel["usuariosAtivos"]!.GetValue<int>());
        Assert.Equal(1, painel["documentosNaoVistosPeloCliente"]!.GetValue<int>()); // holerite; a guia foi baixada
        var historico = await JsonAsync(await admin.GetAsync($"/api/v1/empresas/{empresaId}/historico?limite=3"));
        var itens = historico["itens"]!.AsArray();
        Assert.Equal(3, itens.Count);
        Assert.Equal("Download", itens[0]!["tipo"]!.GetValue<string>());
        Assert.NotNull(historico["proximoAntesDe"]);
        var resto = await JsonAsync(await admin.GetAsync(
            $"/api/v1/empresas/{empresaId}/historico?limite=50&antesDe={Uri.EscapeDataString(historico["proximoAntesDe"]!.GetValue<string>())}"));
        Assert.Contains(resto["itens"]!.AsArray(), i => i!["categoria"]!.GetValue<string>() == "Usuario");
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync($"/api/v1/empresas/{empresaId}/historico")).StatusCode);
        _ = usuarioId;
    }

    [Fact]
    public async Task Usuario_existente_e_vinculado_a_outra_empresa_sem_novo_convite()
    {
        var admin = await LoginAsync("admin@contix.dev");
        var a = await NovaEmpresaAsync(admin, "Grupo A Ltda");
        var b = await NovaEmpresaAsync(admin, "Grupo B Ltda");
        var (cliente, usuarioId, email) = await ConvidarEAceitarAsync(admin, a, new { modulos = new[] { "Documentos" }, todasAsPastas = true });

        var vinculo = await admin.PostAsJsonAsync($"/api/v1/empresas/{b}/usuarios",
            new { nome = "Outro nome", email = email.ToUpperInvariant(), modulos = new[] { "Solicitacoes" }, todasAsPastas = true });
        Assert.Equal(HttpStatusCode.Created, vinculo.StatusCode);
        Assert.Equal(usuarioId, (await JsonAsync(vinculo))["id"]!.GetValue<string>());
        Assert.Equal(1, await NoBancoAsync(db => db.EmailsPendentes.CountAsync(e => e.Para == email)));

        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/v1/empresas/{b}/usuarios",
            new { nome = "x", email, modulos = new[] { "Documentos" }, todasAsPastas = true })).StatusCode);
        // E-mail da equipe do escritório não vira usuário do cliente.
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/v1/empresas/{b}/usuarios",
            new { nome = "x", email = "colaborador@contix.dev", modulos = new[] { "Documentos" }, todasAsPastas = true })).StatusCode);

        var empresas = (await JsonAsync(await cliente.GetAsync("/api/v1/empresas")))["items"]!.AsArray();
        Assert.Equal(2, empresas.Count);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/empresas/{b}/usuarios/{usuarioId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/v1/empresas/{a}/usuarios/{usuarioId}")).StatusCode);

        // Convite já aceito não pode ser reenviado.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsync($"/api/v1/usuarios/{usuarioId}/convite", null)).StatusCode);
    }

    [Fact]
    public async Task Excluir_empresa_esconde_das_listas_e_encerra_o_portal_de_quem_so_tinha_ela()
    {
        var admin = await LoginAsync("admin@contix.dev");
        var colaborador = await LoginAsync("colaborador@contix.dev");
        var a = await NovaEmpresaAsync(admin, "Excluir A Ltda");
        var b = await NovaEmpresaAsync(admin, "Excluir B Ltda");
        var (soA, _, emailSoA) = await ConvidarEAceitarAsync(admin, a, new { modulos = new[] { "Documentos", "Solicitacoes" }, todasAsPastas = true });
        var (doisVinculos, usuarioAB, emailAB) = await ConvidarEAceitarAsync(admin, a, new { modulos = new[] { "Documentos" }, todasAsPastas = true });
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync($"/api/v1/empresas/{b}/usuarios",
            new { nome = "x", email = emailAB, modulos = new[] { "Documentos" }, todasAsPastas = true })).StatusCode);
        var pastas = await PastasAsync(admin, a);
        await UploadAsync(admin, a, pastas["Fiscal"], "guia.txt");
        Assert.Equal(HttpStatusCode.Created, (await soA.PostAsync($"/api/v1/empresas/{a}/solicitacoes", new MultipartFormDataContent
        {
            { new StringContent("Geral"), "tipo" }, { new StringContent("Pedido da empresa A"), "assunto" }, { new StringContent("Fiscal"), "departamento" },
            { new StringContent("Texto"), "descricao" }
        })).StatusCode);

        // Só o administrador exclui.
        Assert.Equal(HttpStatusCode.Forbidden, (await colaborador.DeleteAsync($"/api/v1/empresas/{a}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/empresas/{a}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/v1/empresas/{a}")).StatusCode);

        // Some da lista e das consultas, inclusive das solicitações gerais.
        var lista = (await JsonAsync(await admin.GetAsync("/api/v1/empresas?pageSize=100")))["items"]!.AsArray();
        Assert.DoesNotContain(lista, e => e!["id"]!.GetValue<string>() == a);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/empresas/{a}")).StatusCode);
        var solicitacoes = (await JsonAsync(await admin.GetAsync("/api/v1/solicitacoes?pageSize=100")))["items"]!.AsArray();
        Assert.DoesNotContain(solicitacoes, s => s!["empresaId"]!.GetValue<string>() == a);

        // Os dados continuam no banco, com a autoria da exclusão.
        var excluida = await NoBancoAsync(db => db.Empresas.IgnoreQueryFilters().SingleAsync(e => e.Id == Guid.Parse(a)));
        Assert.True(excluida.IsDeleted);
        Assert.NotNull(excluida.DeletedAt);
        Assert.NotNull(excluida.ExcluidaPorId);
        Assert.Equal(1, await NoBancoAsync(db => db.Documentos.IgnoreQueryFilters().CountAsync(d => d.EmpresaId == Guid.Parse(a))));

        // Quem só tinha a empresa excluída não entra mais; quem tem outra empresa segue com ela.
        var login = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email = emailSoA, senha = NovaSenha });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.Contains("acesso ao portal foi encerrado", await login.Content.ReadAsStringAsync());
        Assert.Equal(0, await NoBancoAsync(db => db.RefreshTokens.CountAsync(t =>
            t.RevogadoEm == null && db.Usuarios.IgnoreQueryFilters().Any(u => u.Id == t.UsuarioId && u.Email == emailSoA))));
        _ = soA;

        var outra = await LoginAsync(emailAB, NovaSenha);
        var empresasDoUsuario = (await JsonAsync(await outra.GetAsync("/api/v1/empresas")))["items"]!.AsArray();
        Assert.Equal([b], empresasDoUsuario.Select(e => e!["id"]!.GetValue<string>()));
        Assert.Equal(HttpStatusCode.NotFound, (await doisVinculos.GetAsync($"/api/v1/empresas/{a}/documentos")).StatusCode);
        _ = usuarioAB;

        // O CNPJ fica livre para um novo cadastro.
        var cnpj = excluida.Documento;
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/v1/empresas", new
        {
            razaoSocial = "Excluir A Recadastrada", documento = cnpj, regimeTributario = "SimplesNacional", situacao = "Ativa"
        })).StatusCode);
    }

    [Fact]
    public async Task Usuario_criado_sem_senha_recebe_convite_e_pode_reenviar()
    {
        var admin = await LoginAsync("admin@contix.dev");
        var email = $"equipe-{Guid.NewGuid():N}@contix.dev";
        var criado = await JsonAsync(await admin.PostAsJsonAsync("/api/v1/usuarios", new { nome = "Nova colega", email, perfil = "Colaborador" }));
        Assert.True(criado["convitePendente"]!.GetValue<bool>());
        var id = criado["id"]!.GetValue<string>();

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/v1/usuarios/{id}/convite", null)).StatusCode);
        var corpos = await NoBancoAsync(db => db.EmailsPendentes.Where(e => e.Para == email).OrderBy(e => e.CreatedAt).Select(e => e.CorpoTexto).ToListAsync());
        Assert.Equal(2, corpos.Count);
        Assert.Contains("equipe do escritório", corpos[0]);

        // Só o link mais recente vale.
        var antigo = ConviteRegex().Match(corpos[0]).Groups[1].Value;
        var atual = ConviteRegex().Match(corpos[1]).Groups[1].Value;
        var anon = factory.CreateClient();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await anon.PostAsJsonAsync("/api/v1/auth/redefinir-senha", new { token = antigo, novaSenha = NovaSenha })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await anon.PostAsJsonAsync("/api/v1/auth/redefinir-senha", new { token = atual, novaSenha = NovaSenha })).StatusCode);
        await LoginAsync(email, NovaSenha);
    }

    [Fact]
    public async Task Nova_noticia_avisa_o_publico_uma_unica_vez()
    {
        var colaborador = await LoginAsync("colaborador@contix.dev");
        var cliente = await LoginAsync("cliente@contix.dev");

        var publicada = await JsonAsync(await colaborador.PostAsync("/api/v1/noticias", new MultipartFormDataContent
        {
            { new StringContent("Aviso: novo prazo do DAS"), "titulo" }, { new StringContent("O prazo mudou."), "conteudo" },
            { new StringContent("Todos"), "publico" }
        }));
        var agendada = await JsonAsync(await colaborador.PostAsync("/api/v1/noticias", new MultipartFormDataContent
        {
            { new StringContent("Aviso agendado"), "titulo" }, { new StringContent("Texto"), "conteudo" },
            { new StringContent("Todos"), "publico" }, { new StringContent(DateTime.UtcNow.AddDays(2).ToString("O")), "publicarEm" }
        }));
        var publicadaId = publicada["id"]!.GetValue<string>();
        var agendadaId = agendada["id"]!.GetValue<string>();

        async Task<List<JsonNode>> AvisosAsync(string id) =>
            (await JsonAsync(await cliente.GetAsync("/api/v1/notificacoes?pageSize=100")))["items"]!.AsArray()
            .Where(n => n!["referenciaId"]?.GetValue<string>() == id).Select(n => n!).ToList();

        var aviso = Assert.Single(await AvisosAsync(publicadaId));
        Assert.Equal("NovaNoticia", aviso["tipo"]!.GetValue<string>());
        Assert.Equal($"/?noticia={publicadaId}", aviso["link"]!.GetValue<string>());
        Assert.Empty(await AvisosAsync(agendadaId));

        // Chega a data da agendada: o job avisa; rodar de novo não duplica.
        await NoBancoAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE noticias SET publicar_em = now() - interval '1 minute' WHERE id = {Guid.Parse(agendadaId)}"));
        for (var i = 0; i < 2; i++)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IAvisoNoticiaService>().ProcessarAsync();
        }
        Assert.Single(await AvisosAsync(agendadaId));
        Assert.Single(await AvisosAsync(publicadaId));
    }

    [Fact]
    public async Task Cnd_a_vencer_avisa_escritorio_e_cliente_por_marco()
    {
        var admin = await LoginAsync("admin@contix.dev");
        var empresaId = await NovaEmpresaAsync(admin, "CND Avisos Ltda");
        var (cliente, _, _) = await ConvidarEAceitarAsync(admin, empresaId, new { modulos = new[] { "Documentos" }, todasAsPastas = true });
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);

        async Task<string> CadastrarAsync(string tipo, DateOnly validade) =>
            (await JsonAsync(await admin.PostAsync($"/api/v1/empresas/{empresaId}/certidoes", new MultipartFormDataContent
            {
                { new StringContent(tipo), "tipo" }, { new StringContent("Negativa"), "situacao" },
                { new StringContent(validade.AddDays(-90).ToString("yyyy-MM-dd")), "emissao" },
                { new StringContent(validade.ToString("yyyy-MM-dd")), "validade" }
            })))["id"]!.GetValue<string>();

        var fgts = await CadastrarAsync("Fgts", hoje.AddDays(25));          // marco de 30 dias
        var trabalhista = await CadastrarAsync("Trabalhista", hoje.AddDays(-3)); // vencida
        await CadastrarAsync("Municipal", hoje.AddDays(-3));
        var municipalRenovada = await CadastrarAsync("Municipal", hoje.AddDays(200)); // renovada: sem aviso da antiga
        _ = municipalRenovada;

        async Task<int> ProcessarAsync()
        {
            await using var scope = factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IAvisoCertidaoService>().ProcessarAsync();
        }
        await ProcessarAsync();
        await ProcessarAsync();

        async Task<List<JsonNode>> DoTipoAsync(HttpClient http) =>
            (await JsonAsync(await http.GetAsync("/api/v1/notificacoes?pageSize=100")))["items"]!.AsArray()
            .Where(n => n!["tipo"]!.GetValue<string>() == "CertidaoVencimento" && n["empresaId"]?.GetValue<string>() == empresaId)
            .Select(n => n!).ToList();

        var doCliente = await DoTipoAsync(cliente);
        Assert.Equal(2, doCliente.Count);
        Assert.Contains(doCliente, n => n["referenciaId"]!.GetValue<string>() == fgts && n["titulo"]!.GetValue<string>().StartsWith("CND a vencer"));
        Assert.Contains(doCliente, n => n["referenciaId"]!.GetValue<string>() == trabalhista && n["titulo"]!.GetValue<string>().StartsWith("CND vencida"));
        Assert.All(doCliente, n => Assert.Equal("/certidoes", n["link"]!.GetValue<string>()));

        var doEscritorio = await DoTipoAsync(await LoginAsync("colaborador@contix.dev"));
        Assert.Equal(2, doEscritorio.Count);
        Assert.All(doEscritorio, n => Assert.Equal($"/empresas/{empresaId}?aba=cnd", n["link"]!.GetValue<string>()));

        // Dentro de 15 dias, a mesma certidão gera o segundo marco (uma vez).
        await NoBancoAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE certidoes SET validade = {hoje.AddDays(10)} WHERE id = {Guid.Parse(fgts)}"));
        await ProcessarAsync();
        await ProcessarAsync();
        Assert.Equal(2, (await DoTipoAsync(cliente)).Count(n => n["referenciaId"]!.GetValue<string>() == fgts));
    }
}
