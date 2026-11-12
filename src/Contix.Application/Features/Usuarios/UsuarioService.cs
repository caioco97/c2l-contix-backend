using Contix.Application.Common.Abstractions;
using Contix.Application.Common.Exceptions;
using Contix.Application.Common.Models;
using Contix.Application.Features.Auth;
using Contix.Application.Features.Empresas;
using Contix.Domain.Entities;
using Contix.Domain.Enums;

namespace Contix.Application.Features.Usuarios;

public interface IUsuarioService
{
    Task<PagedResult<UsuarioDto>> PesquisarAsync(UsuarioFiltro filtro, CancellationToken ct = default);
    Task<UsuarioDto> ObterAsync(Guid id, CancellationToken ct = default);
    Task<UsuarioDto> CriarAsync(CriarUsuarioRequest request, CancellationToken ct = default);
    Task<UsuarioDto> AtualizarAsync(Guid id, AtualizarUsuarioRequest request, CancellationToken ct = default);
}

/// <summary>
/// Gestão de usuários do escritório e do portal do cliente.
/// Administrador gerencia todos; colaborador gerencia apenas usuários de clientes.
/// </summary>
public sealed class UsuarioService(
    IUsuarioRepository usuarios,
    IEmpresaRepository empresas,
    IRefreshTokenRepository refreshTokens,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : IUsuarioService
{
    public async Task<PagedResult<UsuarioDto>> PesquisarAsync(UsuarioFiltro filtro, CancellationToken ct = default)
    {
        var page = await usuarios.PesquisarAsync(filtro, ct);
        return new PagedResult<UsuarioDto>(page.Items.Select(UsuarioDto.From).ToList(), page.Page, page.PageSize, page.Total);
    }

    public async Task<UsuarioDto> ObterAsync(Guid id, CancellationToken ct = default) =>
        UsuarioDto.From(await CarregarAsync(id, ct));

    public async Task<UsuarioDto> CriarAsync(CriarUsuarioRequest request, CancellationToken ct = default)
    {
        GarantirPodeGerenciarPerfil(request.Perfil);

        var email = request.Email.Trim().ToLowerInvariant();
        if (await usuarios.EmailExisteIgnorandoTenantAsync(email, null, ct))
            throw new ConflictException("Este e-mail já está em uso.");

        var usuario = new Usuario
        {
            Nome = request.Nome.Trim(),
            Email = email,
            Perfil = request.Perfil,
            SenhaHash = passwordHasher.Hash(request.Senha)
        };
        await DefinirEmpresasAsync(usuario, request.Perfil, request.EmpresaIds, ct);

        await usuarios.AddAsync(usuario, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return UsuarioDto.From(usuario);
    }

    public async Task<UsuarioDto> AtualizarAsync(Guid id, AtualizarUsuarioRequest request, CancellationToken ct = default)
    {
        var usuario = await CarregarAsync(id, ct);
        GarantirPodeGerenciarPerfil(usuario.Perfil);
        GarantirPodeGerenciarPerfil(request.Perfil);

        if (usuario.Id == currentUser.UserId && (!request.Ativo || request.Perfil != usuario.Perfil))
            throw new ConflictException("Você não pode inativar ou alterar o perfil do seu próprio usuário.");

        usuario.Nome = request.Nome.Trim();
        usuario.Perfil = request.Perfil;
        await DefinirEmpresasAsync(usuario, request.Perfil, request.EmpresaIds, ct);

        if (usuario.Ativo && !request.Ativo)
        {
            var agora = clock.UtcNow;
            foreach (var t in await refreshTokens.ListarAtivosDoUsuarioAsync(usuario.Id, agora, ct))
                t.Revogar(agora, "Usuário inativado");
        }
        usuario.Ativo = request.Ativo;

        await unitOfWork.SaveChangesAsync(ct);
        return UsuarioDto.From(usuario);
    }

    private async Task<Usuario> CarregarAsync(Guid id, CancellationToken ct) =>
        await usuarios.GetComEmpresasAsync(id, ct) ?? throw new NotFoundException("Usuário", id);

    private void GarantirPodeGerenciarPerfil(PerfilUsuario perfil)
    {
        if (currentUser.EhAdministrador) return;
        if (currentUser.Perfil == PerfilUsuario.Colaborador && perfil == PerfilUsuario.Cliente) return;
        throw new ForbiddenException("Apenas administradores podem gerenciar usuários do escritório.");
    }

    private async Task DefinirEmpresasAsync(Usuario usuario, PerfilUsuario perfil, IReadOnlyList<Guid>? empresaIds, CancellationToken ct)
    {
        var desejadas = perfil == PerfilUsuario.Cliente
            ? (empresaIds ?? []).Distinct().ToHashSet()
            : [];

        if (desejadas.Count > 0 && await empresas.ContarExistentesAsync(desejadas, ct) != desejadas.Count)
            throw new NotFoundException("Uma ou mais empresas informadas");

        foreach (var vinculo in usuario.Empresas.Where(v => !desejadas.Contains(v.EmpresaId)).ToList())
            usuario.Empresas.Remove(vinculo);

        var atuais = usuario.Empresas.Select(v => v.EmpresaId).ToHashSet();
        foreach (var empresaId in desejadas.Where(id => !atuais.Contains(id)))
            usuario.Empresas.Add(new UsuarioEmpresa { UsuarioId = usuario.Id, EmpresaId = empresaId, VinculadoEm = clock.UtcNow });
    }
}
