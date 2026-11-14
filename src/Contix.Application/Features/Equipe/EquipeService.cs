using Contix.Application.Common.Abstractions;
using Contix.Application.Common.Exceptions;
using Contix.Application.Features.Empresas;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using FluentValidation;
using FluentValidation.Results;

namespace Contix.Application.Features.Equipe;

/// <summary>Departamentos do escritório e responsáveis por empresa e departamento. Exclusivo do escritório.</summary>
public interface IEquipeService
{
    Task<IReadOnlyList<DepartamentoDto>> ListarDepartamentosAsync(CancellationToken ct = default);
    /// <summary>Renomeia ou ativa/desativa um departamento (somente administradores).</summary>
    Task<DepartamentoDto> AtualizarDepartamentoAsync(Departamento codigo, AtualizarDepartamentoRequest request, CancellationToken ct = default);
    Task<NomesDepartamentos> ObterNomesAsync(CancellationToken ct = default);

    Task<IReadOnlyList<ResponsavelEmpresaDto>> ListarResponsaveisAsync(Guid empresaId, CancellationToken ct = default);
    Task<IReadOnlyList<ResponsavelEmpresaDto>> DefinirResponsaveisAsync(Guid empresaId, DefinirResponsaveisRequest request, CancellationToken ct = default);
}

public sealed class EquipeService(
    IEquipeRepository equipe,
    IAcessoEmpresaService acesso,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IValidator<AtualizarDepartamentoRequest> departamentoValidator,
    IValidator<DefinirResponsaveisRequest> responsaveisValidator) : IEquipeService
{
    public async Task<IReadOnlyList<DepartamentoDto>> ListarDepartamentosAsync(CancellationToken ct = default)
    {
        GarantirEscritorio();
        var nomes = await ObterNomesAsync(ct);
        var colaboradores = await equipe.ContarColaboradoresAsync(ct);
        return Enum.GetValues<Departamento>()
            .Select(d => new DepartamentoDto(d, nomes.Nome(d), nomes.Ativo(d), colaboradores.GetValueOrDefault(d)))
            .ToList();
    }

    public async Task<DepartamentoDto> AtualizarDepartamentoAsync(Departamento codigo, AtualizarDepartamentoRequest request, CancellationToken ct = default)
    {
        if (!currentUser.EhAdministrador) throw new ForbiddenException("Apenas administradores podem alterar os departamentos.");
        if (!Enum.IsDefined(codigo)) throw new NotFoundException("Departamento", codigo);
        await departamentoValidator.ValidateAndThrowAsync(request, ct);

        var personalizados = await equipe.ListarDepartamentosAsync(ct);
        var departamento = personalizados.FirstOrDefault(d => d.Codigo == codigo);
        if (departamento is null)
        {
            departamento = DepartamentoEscritorio.Padrao(currentUser.RequireTenantId(), codigo);
            await equipe.AdicionarDepartamentoAsync(departamento, ct);
        }
        departamento.Atualizar(request.Nome, request.Ativo);
        await unitOfWork.SaveChangesAsync(ct);

        var colaboradores = await equipe.ContarColaboradoresAsync(ct);
        return new DepartamentoDto(codigo, departamento.Nome, departamento.Ativo, colaboradores.GetValueOrDefault(codigo));
    }

    public async Task<NomesDepartamentos> ObterNomesAsync(CancellationToken ct = default) =>
        new((await equipe.ListarDepartamentosAsync(ct)).ToDictionary(d => d.Codigo));

    public async Task<IReadOnlyList<ResponsavelEmpresaDto>> ListarResponsaveisAsync(Guid empresaId, CancellationToken ct = default)
    {
        GarantirEscritorio();
        await acesso.GarantirAcessoAsync(empresaId, ct);
        return Montar(await equipe.ListarResponsaveisAsync(empresaId, ct), await ObterNomesAsync(ct));
    }

    public async Task<IReadOnlyList<ResponsavelEmpresaDto>> DefinirResponsaveisAsync(Guid empresaId, DefinirResponsaveisRequest request, CancellationToken ct = default)
    {
        GarantirEscritorio();
        await responsaveisValidator.ValidateAndThrowAsync(request, ct);
        var empresa = await acesso.GarantirAcessoAsync(empresaId, ct);

        var ids = request.Responsaveis.Where(r => r.UsuarioId is not null).Select(r => r.UsuarioId!.Value).ToHashSet();
        var validos = await equipe.ListarColaboradoresAtivosAsync(ids, ct);
        var invalido = request.Responsaveis.FirstOrDefault(r => r.UsuarioId is { } id && !validos.ContainsKey(id));
        if (invalido is not null)
            throw new ValidationException([new ValidationFailure("responsaveis", "Selecione colaboradores ativos do escritório.")]);

        var atuais = await equipe.ListarResponsaveisAsync(empresaId, ct);
        foreach (var item in request.Responsaveis)
        {
            var atual = atuais.FirstOrDefault(r => r.Departamento == item.Departamento);
            if (item.UsuarioId is not { } usuarioId)
            {
                if (atual is not null) { equipe.RemoverResponsavel(atual); atuais.Remove(atual); }
                continue;
            }
            if (atual is null)
            {
                atual = new ResponsavelEmpresa { TenantId = empresa.TenantId, EmpresaId = empresaId, Departamento = item.Departamento };
                await equipe.AdicionarResponsavelAsync(atual, ct);
                atuais.Add(atual);
            }
            atual.UsuarioId = usuarioId;
            atual.Usuario = null;
        }
        await unitOfWork.SaveChangesAsync(ct);
        return await ListarResponsaveisAsync(empresaId, ct);
    }

    private static List<ResponsavelEmpresaDto> Montar(IReadOnlyList<ResponsavelEmpresa> responsaveis, NomesDepartamentos nomes)
    {
        var porDepartamento = responsaveis.ToDictionary(r => r.Departamento);
        return Enum.GetValues<Departamento>().Select(d =>
        {
            var r = porDepartamento.GetValueOrDefault(d);
            return new ResponsavelEmpresaDto(d, nomes.Nome(d), nomes.Ativo(d), r?.UsuarioId, r?.Usuario?.Nome);
        }).ToList();
    }

    private void GarantirEscritorio()
    {
        if (currentUser.EhCliente) throw new ForbiddenException();
    }
}
