using Contix.Application.Common.Abstractions;
using Contix.Application.Common.Exceptions;
using Contix.Application.Features.Auth;
using Contix.Application.Features.Escritorios;
using Contix.Application.Features.Usuarios;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Contix.UnitTests.Application;

public class AuthServiceTests
{
    private readonly IUsuarioRepository _usuarios = Substitute.For<IUsuarioRepository>();
    private readonly IRefreshTokenRepository _tokens = Substitute.For<IRefreshTokenRepository>();
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
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
        _sut = new AuthService(_usuarios, Substitute.For<IEscritorioRepository>(), _tokens, _tokenService, _hasher,
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
}
