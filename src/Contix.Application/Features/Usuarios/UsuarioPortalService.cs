using Contix.Application.Common.Abstractions;
using Contix.Application.Common.Exceptions;
using Contix.Application.Features.Empresas;
using Contix.Application.Features.Pastas;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Contix.Application.Features.Usuarios;

/// <summary>
/// Usuários do portal do cliente no contexto de uma empresa: convite, vínculo e permissões por módulo e pasta.
/// Somente o escritório (administrador e colaborador) usa este serviço.
/// </summary>
public interface IUsuarioPortalService
{
    Task<IReadOnlyList<UsuarioPortalDto>> ListarAsync(Guid empresaId, CancellationToken ct = default);
    Task<UsuarioPortalDto> ConvidarAsync(Guid empresaId, ConvidarUsuarioPortalRequest request, CancellationToken ct = default);
    Task<UsuarioPortalDto> AtualizarPermissoesAsync(Guid empresaId, Guid usuarioId, PermissoesPortalRequest request, CancellationToken ct = default);
    /// <summary>Remove o acesso do usuário a esta empresa (o usuário continua com as demais).</summary>
    Task DesvincularAsync(Guid empresaId, Guid usuarioId, CancellationToken ct = default);
}

public sealed class UsuarioPortalService(
    IUsuarioRepository usuarios,
    IPastaRepository pastas,
    IAcessoEmpresaService acesso,
    IConviteService convites,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IValidator<PermissoesPortalRequest> permissoesValidator,
    IValidator<ConvidarUsuarioPortalRequest> convidarValidator,
    ILogger<UsuarioPortalService> logger) : IUsuarioPortalService
{
    public async Task<IReadOnlyList<UsuarioPortalDto>> ListarAsync(Guid empresaId, CancellationToken ct = default)
    {
        await GarantirEscritorioAsync(empresaId, ct);
        var lista = await usuarios.ListarClientesDaEmpresaAsync(empresaId, ct);
        return lista
            .Select(u => UsuarioPortalDto.From(u, u.Empresas.First(v => v.EmpresaId == empresaId)))
            .OrderByDescending(u => u.Ativo).ThenBy(u => u.Nome, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<UsuarioPortalDto> ConvidarAsync(Guid empresaId, ConvidarUsuarioPortalRequest request, CancellationToken ct = default)
    {
        await convidarValidator.ValidateAndThrowAsync(request, ct);
        var empresa = await GarantirEscritorioAsync(empresaId, ct);
        await ValidarPastasAsync(empresaId, request, ct);

        var email = request.Email.Trim().ToLowerInvariant();
        var existente = await usuarios.GetByEmailIgnorandoTenantAsync(email, ct);
        Usuario usuario;
        if (existente is not null)
        {
            // Mesmo e-mail em outro escritório ou na equipe: não dá para reaproveitar.
            if (existente.TenantId != empresa.TenantId || existente.Perfil != PerfilUsuario.Cliente)
                throw new ConflictException("Este e-mail já está em uso.");
            usuario = (await usuarios.GetComEmpresasAsync(existente.Id, ct))!;
            if (usuario.Empresas.Any(v => v.EmpresaId == empresaId))
                throw new ConflictException("Este usuário já tem acesso a esta empresa.");
        }
        else
        {
            usuario = new Usuario
            {
                TenantId = empresa.TenantId,
                Nome = request.Nome.Trim(),
                Email = email,
                Perfil = PerfilUsuario.Cliente,
                SenhaHash = passwordHasher.Hash(tokenService.GerarTokenOpaco().Token)
            };
            await usuarios.AddAsync(usuario, ct);
        }

        var vinculo = new UsuarioEmpresa { UsuarioId = usuario.Id, EmpresaId = empresaId, VinculadoEm = clock.UtcNow };
        vinculo.DefinirPermissoes(request.Modulos, request.TodasAsPastas, request.PastaIds);
        usuario.Empresas.Add(vinculo);

        if (existente is null)
            await convites.EnviarAsync(usuario, empresa.NomeFantasia ?? empresa.RazaoSocial, ct);

        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation("Usuário {UsuarioId} {Acao} à empresa {EmpresaId} por {AutorId}",
            usuario.Id, existente is null ? "convidado e vinculado" : "vinculado", empresaId, currentUser.UserId);
        return UsuarioPortalDto.From(usuario, vinculo);
    }

    public async Task<UsuarioPortalDto> AtualizarPermissoesAsync(Guid empresaId, Guid usuarioId, PermissoesPortalRequest request, CancellationToken ct = default)
    {
        await permissoesValidator.ValidateAndThrowAsync(request, ct);
        await GarantirEscritorioAsync(empresaId, ct);
        await ValidarPastasAsync(empresaId, request, ct);
        var (usuario, vinculo) = await CarregarVinculoAsync(empresaId, usuarioId, ct);

        vinculo.DefinirPermissoes(request.Modulos, request.TodasAsPastas, request.PastaIds);
        await unitOfWork.SaveChangesAsync(ct);
        return UsuarioPortalDto.From(usuario, vinculo);
    }

    public async Task DesvincularAsync(Guid empresaId, Guid usuarioId, CancellationToken ct = default)
    {
        await GarantirEscritorioAsync(empresaId, ct);
        var (usuario, vinculo) = await CarregarVinculoAsync(empresaId, usuarioId, ct);
        if (usuario.Empresas.Count == 1)
            throw new ConflictException("Esta é a única empresa do usuário. Inative o acesso dele em vez de remover o vínculo.");

        usuario.Empresas.Remove(vinculo);
        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation("Usuário {UsuarioId} desvinculado da empresa {EmpresaId} por {AutorId}", usuarioId, empresaId, currentUser.UserId);
    }

    /* ---------- Auxiliares ---------- */

    private async Task<Empresa> GarantirEscritorioAsync(Guid empresaId, CancellationToken ct)
    {
        if (currentUser.EhCliente) throw new ForbiddenException();
        return await acesso.GarantirAcessoAsync(empresaId, ct);
    }

    /// <summary>As pastas liberadas precisam ser pastas raiz da própria empresa.</summary>
    private async Task ValidarPastasAsync(Guid empresaId, PermissoesPortalRequest request, CancellationToken ct)
    {
        if (request.TodasAsPastas || request.PastaIds is not { Count: > 0 } ids) return;
        var raizes = (await pastas.ListarPorEmpresaAsync(empresaId, ct)).Where(p => p.PastaPaiId is null).Select(p => p.Id).ToHashSet();
        if (ids.Any(id => !raizes.Contains(id)))
            throw new ValidationException([new FluentValidation.Results.ValidationFailure(
                "pastaIds", "Selecione apenas pastas principais desta empresa.")]);
    }

    private async Task<(Usuario, UsuarioEmpresa)> CarregarVinculoAsync(Guid empresaId, Guid usuarioId, CancellationToken ct)
    {
        var usuario = await usuarios.GetComEmpresasAsync(usuarioId, ct);
        var vinculo = usuario?.Empresas.FirstOrDefault(v => v.EmpresaId == empresaId);
        if (usuario is null || usuario.Perfil != PerfilUsuario.Cliente || vinculo is null)
            throw new NotFoundException("Usuário", usuarioId);
        return (usuario, vinculo);
    }
}
