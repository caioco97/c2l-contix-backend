using Contix.Application.Common.Abstractions;
using Contix.Application.Common.Exceptions;
using Contix.Application.Features.Auth;
using Contix.Application.Features.Escritorios;
using Contix.Application.Features.Notificacoes;
using Contix.Application.Features.Usuarios;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Contix.Domain.Exceptions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Contix.UnitTests.Application;

public class AuthServiceTests
{
    private readonly IUsuarioRepository _usuarios = Substitute.For<IUsuarioRepository>();
    private readonly IRefreshTokenRepository _tokens = Substitute.For<IRefreshTokenRepository>();
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly ITokenRedefinicaoSenhaRepository _tokensRedefinicao = Substitute.For<ITokenRedefinicaoSenhaRepository>();
    private readonly IEmailQueue _emails = Substitute.For<IEmailQueue>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly DateTime _agora = new(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);
    private readonly AuthService _sut;

    public AuthServiceTests()
    {
        _clock.UtcNow.Returns(_agora);
        _tokenService.GerarAccessToken(Arg.Any<Usuario>()).Returns(new AccessToken("jwt", _agora.AddMinutes(15)));
        _tokenService.GerarRefreshToken().Returns(new GeneratedRefreshToken("novo", "hash-novo", _agora.AddDays(7)));
        _tokenService.HashRefreshToken(Arg.Any<string>()).Returns(c => "hash-" + c.Arg<string>());
        _tokenService.HashToken(Arg.Any<string>()).Returns(c => "th-" + c.Arg<string>());
        _tokenService.GerarTokenOpaco().Returns(new GeneratedOpaqueToken("abc", "th-abc"));
        _sut = new AuthService(_usuarios, Substitute.For<IEscritorioRepository>(), _tokens, _tokensRedefinicao, _emails,
            Options.Create(new NotificacaoOptions { UrlFrontend = "https://app.contix.dev" }), _tokenService, _hasher,
            _uow, Substitute.For<ICurrentUser>(), _clock, NullLogger<AuthService>.Instance);
    }

    private static Usuario NovoUsuario() => new()
    {
        Nome = "Ana", Email = "ana@x.dev", SenhaHash = "h", Perfil = PerfilUsuario.Administrador,
        Escritorio = new Escritorio { RazaoSocial = "Esc", Cnpj = "11222333000181" }
    };

    [Fact]
    public async Task Login_com_senha_errada_registra_falha()
    {
        var usuario = NovoUsuario();
        _usuarios.GetByEmailIgnorandoTenantAsync("ana@x.dev").Returns(usuario);
        _hasher.Verify("h", "errada").Returns(false);

        await Assert.ThrowsAsync<UnauthorizedException>(() => _sut.LoginAsync(new LoginRequest("ana@x.dev", "errada")));

        Assert.Equal(1, usuario.TentativasLoginFalhas);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Login_valido_emite_tokens()
    {
        var usuario = NovoUsuario();
        _usuarios.GetByEmailIgnorandoTenantAsync("ana@x.dev").Returns(usuario);
        _hasher.Verify("h", "Senha@123").Returns(true);

        var response = await _sut.LoginAsync(new LoginRequest("ana@x.dev", "Senha@123"));

        Assert.Equal("jwt", response.AccessToken);
        Assert.Equal("novo", response.RefreshToken);
        Assert.Equal("Esc", response.Usuario.Escritorio.Nome);
        await _tokens.Received(1).AddAsync(Arg.Is<RefreshToken>(t => t.TokenHash == "hash-novo"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Login_de_usuario_do_cliente_sem_empresa_ativa_e_recusado()
    {
        var usuario = NovoUsuario();
        usuario.Perfil = PerfilUsuario.Cliente;
        _usuarios.GetByEmailIgnorandoTenantAsync("ana@x.dev").Returns(usuario);
        _usuarios.PossuiEmpresaAtivaIgnorandoTenantAsync(usuario.Id).Returns(false);
        _hasher.Verify("h", "Senha@123").Returns(true);

        var erro = await Assert.ThrowsAsync<UnauthorizedException>(() => _sut.LoginAsync(new LoginRequest("ana@x.dev", "Senha@123")));

        Assert.Contains("encerrado", erro.Message);
        await _tokens.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }

    [Fact]
    public async Task Refresh_de_usuario_do_cliente_sem_empresa_ativa_revoga_o_token()
    {
        var usuario = NovoUsuario();
        usuario.Perfil = PerfilUsuario.Cliente;
        var atual = new RefreshToken { UsuarioId = usuario.Id, Familia = Guid.NewGuid(), TokenHash = "hash-atual", ExpiraEm = _agora.AddDays(1) };
        _tokens.GetByHashAsync("hash-atual").Returns(atual);
        _usuarios.GetByIdIgnorandoTenantAsync(usuario.Id).Returns(usuario);

        await Assert.ThrowsAsync<UnauthorizedException>(() => _sut.RefreshAsync(new RefreshTokenRequest("atual")));

        Assert.False(atual.EstaAtivo(_agora));
    }

    [Fact]
    public async Task Reutilizar_refresh_revogado_revoga_toda_a_cadeia()
    {
        var familia = Guid.NewGuid();
        var antigo = new RefreshToken { Familia = familia, TokenHash = "hash-velho", ExpiraEm = _agora.AddDays(1) };
        antigo.Revogar(_agora.AddMinutes(-5), "Rotacionado");
        var ativo = new RefreshToken { Familia = familia, TokenHash = "hash-atual", ExpiraEm = _agora.AddDays(1) };

        _tokens.GetByHashAsync("hash-velho").Returns(antigo);
        _tokens.ListarAtivosDaFamiliaAsync(familia, _agora).Returns([ativo]);

        await Assert.ThrowsAsync<UnauthorizedException>(() => _sut.RefreshAsync(new RefreshTokenRequest("velho")));

        Assert.False(ativo.EstaAtivo(_agora));
    }

    [Fact]
    public async Task Refresh_rotaciona_token()
    {
        var usuario = NovoUsuario();
        var atual = new RefreshToken { UsuarioId = usuario.Id, Familia = Guid.NewGuid(), TokenHash = "hash-atual", ExpiraEm = _agora.AddDays(1) };
        _tokens.GetByHashAsync("hash-atual").Returns(atual);
        _usuarios.GetByIdIgnorandoTenantAsync(usuario.Id).Returns(usuario);

        var response = await _sut.RefreshAsync(new RefreshTokenRequest("atual"));

        Assert.Equal("novo", response.RefreshToken);
        Assert.False(atual.EstaAtivo(_agora));
        Assert.Equal("hash-novo", atual.SubstituidoPorHash);
        await _tokens.Received(1).AddAsync(Arg.Is<RefreshToken>(t => t.Familia == atual.Familia), Arg.Any<CancellationToken>());
    }

    /* ---------- Recuperação de senha ---------- */

    [Fact]
    public async Task Esqueci_senha_com_email_inexistente_nao_envia_nada()
    {
        await _sut.SolicitarRedefinicaoSenhaAsync(new EsqueciSenhaRequest("ninguem@x.dev"));

        await _emails.DidNotReceiveWithAnyArgs().EnfileirarAsync(default!);
        await _tokensRedefinicao.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }

    [Fact]
    public async Task Esqueci_senha_gera_token_com_expiracao_e_envia_link()
    {
        var usuario = NovoUsuario();
        var anterior = new TokenRedefinicaoSenha { UsuarioId = usuario.Id, TokenHash = "th-velho", ExpiraEm = _agora.AddMinutes(30) };
        _usuarios.GetByEmailIgnorandoTenantAsync("ana@x.dev").Returns(usuario);
        _tokensRedefinicao.ListarValidosDoUsuarioAsync(usuario.Id, _agora).Returns([anterior]);

        await _sut.SolicitarRedefinicaoSenhaAsync(new EsqueciSenhaRequest("ana@x.dev"));

        Assert.False(anterior.EstaValido(_agora));
        await _tokensRedefinicao.Received(1).AddAsync(
            Arg.Is<TokenRedefinicaoSenha>(t => t.TokenHash == "th-abc" && t.ExpiraEm == _agora.AddHours(1)), Arg.Any<CancellationToken>());
        await _emails.Received(1).EnfileirarAsync(
            Arg.Is<EmailMensagem>(m => m.Para == "ana@x.dev" && m.CorpoTexto.Contains("https://app.contix.dev/redefinir-senha?token=abc")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Redefinir_senha_troca_hash_usa_token_e_revoga_sessoes()
    {
        var usuario = NovoUsuario();
        for (var i = 0; i < Usuario.MaxTentativasLogin; i++) usuario.RegistrarFalhaLogin(_agora);
        var token = new TokenRedefinicaoSenha { UsuarioId = usuario.Id, TokenHash = "th-abc", ExpiraEm = _agora.AddMinutes(10) };
        var sessao = new RefreshToken { UsuarioId = usuario.Id, TokenHash = "r", ExpiraEm = _agora.AddDays(1) };
        _tokensRedefinicao.GetByHashAsync("th-abc").Returns(token);
        _usuarios.GetByIdIgnorandoTenantAsync(usuario.Id).Returns(usuario);
        _tokens.ListarAtivosDoUsuarioAsync(usuario.Id, _agora).Returns([sessao]);
        _hasher.Hash("Nova@1234").Returns("novo-hash");

        await _sut.RedefinirSenhaAsync(new RedefinirSenhaRequest("abc", "Nova@1234"));

        Assert.Equal("novo-hash", usuario.SenhaHash);
        Assert.False(usuario.EstaBloqueado(_agora));
        Assert.NotNull(token.UsadoEm);
        Assert.NotNull(sessao.RevogadoEm);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Redefinir_senha_com_token_expirado_falha()
    {
        var token = new TokenRedefinicaoSenha { UsuarioId = Guid.NewGuid(), TokenHash = "th-abc", ExpiraEm = _agora.AddSeconds(-1) };
        _tokensRedefinicao.GetByHashAsync("th-abc").Returns(token);

        await Assert.ThrowsAsync<DomainException>(() => _sut.RedefinirSenhaAsync(new RedefinirSenhaRequest("abc", "Nova@1234")));
        await _uow.DidNotReceiveWithAnyArgs().SaveChangesAsync();
    }
}
