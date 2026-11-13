using Contix.Application.Common.Abstractions;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Contix.Application.Features.Dispositivos;

/// <summary>Registro do aparelho para push, feito pelo app após o login.</summary>
public sealed record RegistrarDispositivoRequest(string Token, PlataformaDispositivo Plataforma, string? Nome);

public sealed class RegistrarDispositivoRequestValidator : AbstractValidator<RegistrarDispositivoRequest>
{
    public RegistrarDispositivoRequestValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(DispositivoPush.TamanhoMaximoToken)
            .Must(TokenExpoValido).WithMessage("Token de push inválido.");
        RuleFor(x => x.Plataforma).IsInEnum();
        RuleFor(x => x.Nome).MaximumLength(100);
    }

    public static bool TokenExpoValido(string? token) =>
        token is not null
        && (token.StartsWith("ExponentPushToken[", StringComparison.Ordinal) || token.StartsWith("ExpoPushToken[", StringComparison.Ordinal))
        && token.EndsWith(']');
}

public interface IDispositivoPushRepository : IRepository<DispositivoPush>
{
    Task<DispositivoPush?> ObterPorTokenAsync(string token, CancellationToken ct = default);
    Task<IReadOnlyList<DispositivoPush>> ListarDoUsuarioAsync(Guid usuarioId, CancellationToken ct = default);
}

public interface IDispositivoService
{
    Task RegistrarAsync(RegistrarDispositivoRequest request, CancellationToken ct = default);
    /// <summary>Remove o aparelho do usuário atual (logout no app). Sem efeito se o token não for dele.</summary>
    Task RemoverAsync(string token, CancellationToken ct = default);
}

public sealed class DispositivoService(
    IDispositivoPushRepository dispositivos,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock,
    ILogger<DispositivoService> logger) : IDispositivoService
{
    public async Task RegistrarAsync(RegistrarDispositivoRequest request, CancellationToken ct = default)
    {
        var usuarioId = currentUser.RequireUserId();
        var agora = clock.UtcNow;
        var nome = string.IsNullOrWhiteSpace(request.Nome) ? null : request.Nome.Trim();

        var existente = await dispositivos.ObterPorTokenAsync(request.Token, ct);
        if (existente is null)
        {
            var novo = new DispositivoPush { Token = request.Token };
            novo.AtribuirA(usuarioId, request.Plataforma, nome, agora);
            await dispositivos.AddAsync(novo, ct);
            logger.LogInformation("Aparelho {Plataforma} registrado para push do usuário {UsuarioId}", request.Plataforma, usuarioId);
        }
        else
        {
            // Mesmo aparelho com outra conta: o push passa a ser só do usuário que entrou agora.
            if (existente.UsuarioId != usuarioId)
                logger.LogInformation("Aparelho de push transferido do usuário {Anterior} para {UsuarioId}", existente.UsuarioId, usuarioId);
            existente.AtribuirA(usuarioId, request.Plataforma, nome, agora);
        }

        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task RemoverAsync(string token, CancellationToken ct = default)
    {
        var usuarioId = currentUser.RequireUserId();
        var dispositivo = await dispositivos.ObterPorTokenAsync(token, ct);
        if (dispositivo is null || dispositivo.UsuarioId != usuarioId) return;

        dispositivos.Remove(dispositivo);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
