namespace Contix.Application.Features.Auth;

public interface IAuthService
{
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<AuthResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken ct = default);
    Task LogoutAsync(RefreshTokenRequest request, CancellationToken ct = default);
    Task<AuthResponse> RegistrarEscritorioAsync(RegistrarEscritorioRequest request, CancellationToken ct = default);
    /// <summary>Envia o link de redefinição, se o e-mail existir. Nunca revela se a conta existe.</summary>
    Task SolicitarRedefinicaoSenhaAsync(EsqueciSenhaRequest request, CancellationToken ct = default);
    Task RedefinirSenhaAsync(RedefinirSenhaRequest request, CancellationToken ct = default);
    Task<UsuarioAutenticadoDto> ObterUsuarioAtualAsync(CancellationToken ct = default);
}
