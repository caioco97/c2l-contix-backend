using Contix.Application.Common.Abstractions;
using Contix.Application.Features.Auth;
using Contix.Application.Features.Escritorios;
using Contix.Application.Features.Notificacoes;
using Contix.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Contix.Application.Features.Usuarios;

/// <summary>
/// Convite de primeiro acesso: envia por e-mail um link de uso único para o usuário definir a própria senha.
/// Reaproveita o token de redefinição de senha, com validade maior.
/// </summary>
public interface IConviteService
{
    /// <summary>Gera o link e enfileira o e-mail. Não salva: grava no próximo SaveChanges.</summary>
    Task EnviarAsync(Usuario usuario, string? nomeEmpresa, CancellationToken ct = default);
}

public sealed class ConviteService(
    ITokenRedefinicaoSenhaRepository tokens,
    IEscritorioRepository escritorios,
    ITokenService tokenService,
    IEmailQueue emails,
    IOptions<NotificacaoOptions> options,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    ILogger<ConviteService> logger) : IConviteService
{
    public async Task EnviarAsync(Usuario usuario, string? nomeEmpresa, CancellationToken ct = default)
    {
        var agora = clock.UtcNow;
        usuario.RegistrarConvite(agora);

        // Só o convite mais recente vale.
        foreach (var anterior in await tokens.ListarValidosDoUsuarioAsync(usuario.Id, agora, ct))
            anterior.Invalidar(agora);

        var opcoes = options.Value;
        var dias = Math.Max(1, opcoes.ValidadeConviteDias);
        var token = tokenService.GerarTokenOpaco();
        await tokens.AddAsync(new TokenRedefinicaoSenha
        {
            UsuarioId = usuario.Id,
            TokenHash = token.Hash,
            ExpiraEm = agora.AddDays(dias),
            CriadoPorIp = currentUser.IpAddress
        }, ct);

        var escritorio = usuario.Escritorio ?? await escritorios.GetByIdAsync(usuario.TenantId, ct);
        var nomeEscritorio = escritorio?.NomeFantasia ?? escritorio?.RazaoSocial ?? "Contix";
        var link = EmailTemplates.Url(opcoes.UrlFrontend,
            $"/redefinir-senha?token={Uri.EscapeDataString(token.Token)}&convite=1");
        var acesso = usuario.EhDoEscritorio
            ? $"{nomeEscritorio} convidou você para acessar o Contix como parte da equipe do escritório."
            : nomeEmpresa is null
                ? $"{nomeEscritorio} convidou você para acessar o portal do cliente no Contix."
                : $"{nomeEscritorio} convidou você para acessar o portal do cliente da empresa {nomeEmpresa} no Contix.";

        await emails.EnfileirarAsync(EmailTemplates.Montar(
            usuario.Email, usuario.Nome, $"Convite para acessar o Contix — {nomeEscritorio}",
            new EmailTemplates.Marca(nomeEscritorio, escritorio?.LogoUrl),
            "Você foi convidado para o Contix",
            [acesso,
             $"Clique no botão abaixo para criar a sua senha. O link vale por {dias} dia{(dias == 1 ? "" : "s")} e só pode ser usado uma vez.",
             $"Depois, entre com o e-mail {usuario.Email} e a senha que você criou."],
            "Criar minha senha", link,
            "Se você não esperava este convite, ignore este e-mail."), ct);

        logger.LogInformation("Convite de primeiro acesso enviado para o usuário {UsuarioId}", usuario.Id);
    }
}
