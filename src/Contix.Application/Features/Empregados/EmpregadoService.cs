using Contix.Application.Common.Abstractions;
using Contix.Application.Common.Exceptions;
using Contix.Application.Features.Empresas;
using Contix.Application.Features.Solicitacoes;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using Contix.Domain.ValueObjects;

namespace Contix.Application.Features.Empregados;

/// <summary>
/// Cadastro de empregados por empresa. O escritório mantém o cadastro; o usuário do cliente com o
/// módulo Solicitações consulta a lista para escolher o empregado nos formulários.
/// </summary>
public interface IEmpregadoService
{
    Task<IReadOnlyList<EmpregadoDto>> ListarAsync(Guid empresaId, EmpregadoFiltro filtro, CancellationToken ct = default);
    Task<EmpregadoDto> ObterAsync(Guid empresaId, Guid id, CancellationToken ct = default);
    /// <summary>Cadastro mais férias, afastamentos, desligamento e rubricas vindos das solicitações.</summary>
    Task<FichaEmpregadoDto> ObterFichaAsync(Guid empresaId, Guid id, CancellationToken ct = default);
    Task<EmpregadoDto> CriarAsync(Guid empresaId, SalvarEmpregadoRequest request, CancellationToken ct = default);
    Task<EmpregadoDto> AtualizarAsync(Guid empresaId, Guid id, SalvarEmpregadoRequest request, CancellationToken ct = default);
    Task ExcluirAsync(Guid empresaId, Guid id, CancellationToken ct = default);
}

public sealed class EmpregadoService(
    IEmpregadoRepository empregados,
    ISolicitacaoRepository solicitacoes,
    IAcessoEmpresaService acesso,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : IEmpregadoService
{
    public async Task<FichaEmpregadoDto> ObterFichaAsync(Guid empresaId, Guid id, CancellationToken ct = default)
    {
        await acesso.GarantirModuloAsync(empresaId, ModuloPortal.Solicitacoes, ct);
        var empregado = await CarregarAsync(empresaId, id, ct);
        var doEmpregado = await solicitacoes.ListarDoEmpregadoAsync(empresaId, id, ct);
        return FichaEmpregado.Montar(empregado, doEmpregado, DateOnly.FromDateTime(clock.UtcNow));
    }

    public async Task<IReadOnlyList<EmpregadoDto>> ListarAsync(Guid empresaId, EmpregadoFiltro filtro, CancellationToken ct = default)
    {
        await acesso.GarantirModuloAsync(empresaId, ModuloPortal.Solicitacoes, ct);
        return (await empregados.ListarAsync(empresaId, filtro, ct)).Select(EmpregadoDto.From).ToList();
    }

    public async Task<EmpregadoDto> ObterAsync(Guid empresaId, Guid id, CancellationToken ct = default)
    {
        await acesso.GarantirModuloAsync(empresaId, ModuloPortal.Solicitacoes, ct);
        return EmpregadoDto.From(await CarregarAsync(empresaId, id, ct));
    }

    public async Task<EmpregadoDto> CriarAsync(Guid empresaId, SalvarEmpregadoRequest request, CancellationToken ct = default)
    {
        var empresa = await GarantirEscritorioAsync(empresaId, ct);
        var empregado = new Empregado { TenantId = empresa.TenantId, EmpresaId = empresaId };
        await AplicarAsync(empregado, request, ct);
        await empregados.AddAsync(empregado, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return EmpregadoDto.From(empregado);
    }

    public async Task<EmpregadoDto> AtualizarAsync(Guid empresaId, Guid id, SalvarEmpregadoRequest request, CancellationToken ct = default)
    {
        await GarantirEscritorioAsync(empresaId, ct);
        var empregado = await CarregarAsync(empresaId, id, ct);
        await AplicarAsync(empregado, request, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return EmpregadoDto.From(empregado);
    }

    /// <summary>Exclusão lógica. As solicitações já abertas mantêm o nome gravado nos dados.</summary>
    public async Task ExcluirAsync(Guid empresaId, Guid id, CancellationToken ct = default)
    {
        await GarantirEscritorioAsync(empresaId, ct);
        empregados.Remove(await CarregarAsync(empresaId, id, ct));
        await unitOfWork.SaveChangesAsync(ct);
    }

    private async Task<Empresa> GarantirEscritorioAsync(Guid empresaId, CancellationToken ct)
    {
        if (currentUser.EhCliente) throw new ForbiddenException();
        return await acesso.GarantirAcessoAsync(empresaId, ct);
    }

    private async Task<Empregado> CarregarAsync(Guid empresaId, Guid id, CancellationToken ct) =>
        await empregados.ObterAsync(empresaId, id, ct) ?? throw new NotFoundException("Empregado", id);

    private async Task AplicarAsync(Empregado e, SalvarEmpregadoRequest r, CancellationToken ct)
    {
        var cpf = string.IsNullOrWhiteSpace(r.Cpf) ? null : DocumentoFiscal.SomenteDigitos(r.Cpf);
        if (cpf is not null && await empregados.ObterPorCpfAsync(e.EmpresaId, cpf, ct) is { } outro && outro.Id != e.Id)
            throw new ConflictException($"Já existe um empregado com este CPF nesta empresa ({outro.Nome}).");

        e.Nome = r.Nome.Trim();
        e.Cpf = cpf;
        e.Matricula = Limpar(r.Matricula);
        e.Cargo = Limpar(r.Cargo);
        e.Vinculo = r.Vinculo;
        e.Situacao = r.Situacao;
        e.DataAdmissao = r.DataAdmissao;
        e.DataDesligamento = r.Situacao == SituacaoEmpregado.Desligado ? r.DataDesligamento : null;
        e.Email = Limpar(r.Email)?.ToLowerInvariant();
        e.Telefone = DadosBrasileiros.NormalizarTelefone(r.Telefone);
    }

    private static string? Limpar(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}
