using Contix.Application.Common.Abstractions;
using Contix.Application.Common.Exceptions;
using Contix.Application.Features.Dispositivos;
using Contix.Application.Features.Escritorios;
using Contix.Application.Features.Notificacoes;
using Contix.Application.Features.Usuarios;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using Contix.Domain.Exceptions;
using Contix.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Contix.Application.Features.Auth;

public sealed class AuthService(
    IUsuarioRepository usuarios,
    IEscritorioRepository escritorios,
    IRefreshTokenRepository refreshTokens,
    ITokenRedefinicaoSenhaRepository tokensRedefinicao,
    IDispositivoPushRepository dispositivos,
    IEmailQueue emails,
    IOptions<NotificacaoOptions> notificacaoOptions,
    ITokenService tokenService,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    ILogger<AuthService> logger) : IAuthService
{
    private const string CredenciaisInvalidas = "E-mail ou senha inválidos.";
    private const string SemEmpresaAtiva = "Seu acesso ao portal foi encerrado. Fale com o seu escritório contábil.";

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var agora = clock.UtcNow;
        var usuario = await usuarios.GetByEmailIgnorandoTenantAsync(request.Email, ct);

        if (usuario is null || !usuario.Ativo)
            throw new UnauthorizedException(CredenciaisInvalidas);

        if (usuario.EstaBloqueado(agora))
            throw new UnauthorizedException("Usuário temporariamente bloqueado por excesso de tentativas. Tente novamente mais tarde.");

        if (!passwordHasher.Verify(usuario.SenhaHash, request.Senha))
        {
            usuario.RegistrarFalhaLogin(agora);
            await unitOfWork.SaveChangesAsync(ct);
            logger.LogWarning("Falha de login para o usuário {UsuarioId}", usuario.Id);
            throw new UnauthorizedException(CredenciaisInvalidas);
        }

        if (!await PodeEntrarAsync(usuario, ct))
            throw new UnauthorizedException(SemEmpresaAtiva);

        usuario.RegistrarLoginComSucesso(agora);
        var response = await EmitirTokensAsync(usuario, Guid.NewGuid(), ct);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation("Login do usuário {UsuarioId} no tenant {TenantId}", usuario.Id, usuario.TenantId);
        return response;
    }

    public async Task<AuthResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken ct = default)
    {
        var agora = clock.UtcNow;
        var hash = tokenService.HashRefreshToken(request.RefreshToken);
        var atual = await refreshTokens.GetByHashAsync(hash, ct)
                    ?? throw new UnauthorizedException("Refresh token inválido.");

        if (atual.RevogadoEm is not null)
        {
            // Reutilização de token já rotacionado: possível roubo. Revoga a cadeia inteira.
            var cadeia = await refreshTokens.ListarAtivosDaFamiliaAsync(atual.Familia, agora, ct);
            foreach (var t in cadeia) t.Revogar(agora, "Reutilização de refresh token revogado");
            await unitOfWork.SaveChangesAsync(ct);
            logger.LogWarning("Reutilização de refresh token detectada para o usuário {UsuarioId}", atual.UsuarioId);
            throw new UnauthorizedException("Refresh token inválido.");
        }

        if (atual.ExpiraEm <= agora)
            throw new UnauthorizedException("Refresh token expirado.");

        var usuario = await usuarios.GetByIdIgnorandoTenantAsync(atual.UsuarioId, ct);
        if (usuario is null || !usuario.Ativo || !await PodeEntrarAsync(usuario, ct))
        {
            atual.Revogar(agora, "Usuário inativo");
            await unitOfWork.SaveChangesAsync(ct);
            throw new UnauthorizedException("Refresh token inválido.");
        }

        var response = await EmitirTokensAsync(usuario, atual.Familia, ct);
        atual.Revogar(agora, "Rotacionado", tokenService.HashRefreshToken(response.RefreshToken));
        await unitOfWork.SaveChangesAsync(ct);
        return response;
    }

    public async Task LogoutAsync(RefreshTokenRequest request, CancellationToken ct = default)
    {
        var token = await refreshTokens.GetByHashAsync(tokenService.HashRefreshToken(request.RefreshToken), ct);
        if (token is null) return;

        token.Revogar(clock.UtcNow, "Logout");
        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<AuthResponse> RegistrarEscritorioAsync(RegistrarEscritorioRequest request, CancellationToken ct = default)
    {
        var cnpj = DocumentoFiscal.SomenteDigitos(request.Cnpj);
        if (await escritorios.CnpjExisteAsync(cnpj, ct))
            throw new ConflictException("Já existe um escritório cadastrado com este CNPJ.");

        var email = request.EmailAdministrador.Trim().ToLowerInvariant();
        if (await usuarios.EmailExisteIgnorandoTenantAsync(email, null, ct))
            throw new ConflictException("Este e-mail já está em uso.");

        var escritorio = new Escritorio
        {
            RazaoSocial = request.RazaoSocial.Trim(),
            NomeFantasia = request.NomeFantasia?.Trim(),
            Cnpj = cnpj,
            Crc = request.Crc?.Trim(),
            Email = email,
            Telefone = request.Telefone?.Trim(),
            Endereco = request.Endereco?.ToEntity()
        };

        var admin = new Usuario
        {
            TenantId = escritorio.Id,
            Nome = request.NomeAdministrador.Trim(),
            Email = email,
            Perfil = PerfilUsuario.Administrador,
            SenhaHash = passwordHasher.Hash(request.Senha),
            Escritorio = escritorio
        };

        await escritorios.AddAsync(escritorio, ct);
        await usuarios.AddAsync(admin, ct);
        var response = await EmitirTokensAsync(admin, Guid.NewGuid(), ct);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation("Escritório {EscritorioId} registrado", escritorio.Id);
        return response;
    }

    public async Task SolicitarRedefinicaoSenhaAsync(EsqueciSenhaRequest request, CancellationToken ct = default)
    {
        var agora = clock.UtcNow;
        var usuario = await usuarios.GetByEmailIgnorandoTenantAsync(request.Email, ct);
        if (usuario is null || !usuario.Ativo || usuario.Escritorio is not { Ativo: true })
        {
            logger.LogInformation("Pedido de redefinição de senha para e-mail sem conta ativa");
            return;
        }

        // Apenas o link mais recente vale.
        foreach (var anterior in await tokensRedefinicao.ListarValidosDoUsuarioAsync(usuario.Id, agora, ct))
            anterior.Invalidar(agora);

        var opcoes = notificacaoOptions.Value;
        var validade = TimeSpan.FromMinutes(opcoes.ValidadeLinkRedefinicaoMinutos);
        var token = tokenService.GerarTokenOpaco();
        await tokensRedefinicao.AddAsync(new TokenRedefinicaoSenha
        {
            UsuarioId = usuario.Id,
            TokenHash = token.Hash,
            ExpiraEm = agora.Add(validade),
            CriadoPorIp = currentUser.IpAddress
        }, ct);

        var escritorio = usuario.Escritorio;
        var link = EmailTemplates.Url(opcoes.UrlFrontend, $"/redefinir-senha?token={Uri.EscapeDataString(token.Token)}");
        await emails.EnfileirarAsync(EmailTemplates.Montar(
            usuario.Email, usuario.Nome, "Redefinição de senha do Contix",
            new EmailTemplates.Marca(escritorio.NomeFantasia ?? escritorio.RazaoSocial, escritorio.LogoUrl),
            "Redefinição de senha",
            ["Recebemos um pedido para redefinir a senha da sua conta no Contix. Clique no botão abaixo para criar uma nova senha.",
             $"O link vale por {FormatarValidade(validade)} e só pode ser usado uma vez."],
            "Criar nova senha", link,
            "Se você não fez este pedido, ignore este e-mail: sua senha atual continua valendo."), ct);

        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation("Link de redefinição de senha enviado para o usuário {UsuarioId}", usuario.Id);
    }

    public async Task RedefinirSenhaAsync(RedefinirSenhaRequest request, CancellationToken ct = default)
    {
        var agora = clock.UtcNow;
        var token = await tokensRedefinicao.GetByHashAsync(tokenService.HashToken(request.Token), ct);
        if (token is null || !token.EstaValido(agora))
            throw new DomainException("Link de redefinição inválido ou expirado.");

        var usuario = await usuarios.GetByIdIgnorandoTenantAsync(token.UsuarioId, ct);
        if (usuario is null || !usuario.Ativo)
            throw new DomainException("Link de redefinição inválido ou expirado.");

        token.MarcarComoUsado(agora);
        usuario.RedefinirSenha(passwordHasher.Hash(request.NovaSenha), agora);

        // Encerra as sessões abertas: quem tinha a senha antiga perde o acesso.
        foreach (var t in await refreshTokens.ListarAtivosDoUsuarioAsync(usuario.Id, agora, ct))
            t.Revogar(agora, "Senha redefinida");
        foreach (var outro in await tokensRedefinicao.ListarValidosDoUsuarioAsync(usuario.Id, agora, ct))
            if (outro.Id != token.Id) outro.Invalidar(agora);
        // Aparelhos com sessão encerrada também deixam de receber push.
        foreach (var dispositivo in await dispositivos.ListarDoUsuarioAsync(usuario.Id, ct))
            dispositivos.Remove(dispositivo);

        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation("Senha redefinida para o usuário {UsuarioId}", usuario.Id);
    }

    public async Task<AuthResponse> TrocarSenhaAsync(TrocarSenhaRequest request, CancellationToken ct = default)
    {
        var agora = clock.UtcNow;
        var usuario = await usuarios.GetByIdIgnorandoTenantAsync(currentUser.RequireUserId(), ct);
        if (usuario is null || !usuario.Ativo) throw new UnauthorizedException();

        if (!passwordHasher.Verify(usuario.SenhaHash, request.SenhaAtual))
        {
            logger.LogWarning("Troca de senha com senha atual incorreta para o usuário {UsuarioId}", usuario.Id);
            throw new FluentValidation.ValidationException([new(nameof(TrocarSenhaRequest.SenhaAtual), "Senha atual incorreta.")]);
        }

        usuario.TrocarSenha(passwordHasher.Hash(request.NovaSenha));

        // Encerra as outras sessões (inclusive aparelhos) e abre uma nova para quem trocou a senha.
        foreach (var t in await refreshTokens.ListarAtivosDoUsuarioAsync(usuario.Id, agora, ct))
            t.Revogar(agora, "Senha trocada pelo usuário");
        foreach (var token in await tokensRedefinicao.ListarValidosDoUsuarioAsync(usuario.Id, agora, ct))
            token.Invalidar(agora);
        foreach (var dispositivo in await dispositivos.ListarDoUsuarioAsync(usuario.Id, ct))
            dispositivos.Remove(dispositivo);

        var response = await EmitirTokensAsync(usuario, Guid.NewGuid(), ct);
        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation("Senha trocada pelo usuário {UsuarioId}", usuario.Id);
        return response;
    }

    /// <summary>Usuário do cliente só entra enquanto tiver ao menos uma empresa não excluída.</summary>
    private async Task<bool> PodeEntrarAsync(Usuario usuario, CancellationToken ct) =>
        usuario.Perfil != PerfilUsuario.Cliente || await usuarios.PossuiEmpresaAtivaIgnorandoTenantAsync(usuario.Id, ct);

    private static string FormatarValidade(TimeSpan t) =>
        t.TotalMinutes < 60 ? $"{(int)t.TotalMinutes} minutos"
        : t.TotalHours == 1 ? "1 hora"
        : $"{(int)t.TotalHours} horas";

    public async Task<UsuarioAutenticadoDto> ObterUsuarioAtualAsync(CancellationToken ct = default)
    {
        var usuario = await usuarios.GetByIdIgnorandoTenantAsync(currentUser.RequireUserId(), ct)
                      ?? throw new UnauthorizedException();
        return UsuarioAutenticadoDto.From(usuario);
    }

    private async Task<AuthResponse> EmitirTokensAsync(Usuario usuario, Guid familia, CancellationToken ct)
    {
        var access = tokenService.GerarAccessToken(usuario);
        var refresh = tokenService.GerarRefreshToken();

        await refreshTokens.AddAsync(new RefreshToken
        {
            UsuarioId = usuario.Id,
            TokenHash = refresh.Hash,
            Familia = familia,
            ExpiraEm = refresh.ExpiraEm,
            CriadoPorIp = currentUser.IpAddress
        }, ct);

        return new AuthResponse(access.Token, access.ExpiraEm, refresh.Token, refresh.ExpiraEm,
            UsuarioAutenticadoDto.From(usuario));
    }
}
