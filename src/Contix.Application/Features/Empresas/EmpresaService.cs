using Contix.Application.Common.Abstractions;
using Contix.Application.Common.Exceptions;
using Contix.Application.Common.Models;
using Contix.Application.Features.Pastas;
using Contix.Domain.Entities;
using Contix.Domain.ValueObjects;

namespace Contix.Application.Features.Empresas;

public interface IEmpresaService
{
    Task<PagedResult<EmpresaResumoDto>> PesquisarAsync(EmpresaFiltro filtro, CancellationToken ct = default);
    Task<EmpresaDto> ObterAsync(Guid id, CancellationToken ct = default);
    Task<EmpresaDto> CriarAsync(SalvarEmpresaRequest request, CancellationToken ct = default);
    Task<EmpresaDto> AtualizarAsync(Guid id, SalvarEmpresaRequest request, CancellationToken ct = default);
    Task ExcluirAsync(Guid id, CancellationToken ct = default);
}

public sealed class EmpresaService(
    IEmpresaRepository empresas,
    IPastaRepository pastas,
    IAcessoEmpresaService acesso,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : IEmpresaService
{
    public Task<PagedResult<EmpresaResumoDto>> PesquisarAsync(EmpresaFiltro filtro, CancellationToken ct = default) =>
        empresas.PesquisarAsync(filtro, currentUser.EhCliente ? currentUser.RequireUserId() : null, ct);

    public async Task<EmpresaDto> ObterAsync(Guid id, CancellationToken ct = default) =>
        EmpresaDto.From(await acesso.GarantirAcessoAsync(id, ct));

    public async Task<EmpresaDto> CriarAsync(SalvarEmpresaRequest request, CancellationToken ct = default)
    {
        var documento = DocumentoFiscal.SomenteDigitos(request.Documento);
        await GarantirDocumentoUnicoAsync(documento, null, ct);

        var empresa = new Empresa { TenantId = currentUser.RequireTenantId(), Documento = documento };
        Aplicar(empresa, request);

        await empresas.AddAsync(empresa, ct);
        await pastas.AddRangeAsync(empresa.CriarPastasPadrao(), ct);
        await unitOfWork.SaveChangesAsync(ct);
        return EmpresaDto.From(empresa);
    }

    public async Task<EmpresaDto> AtualizarAsync(Guid id, SalvarEmpresaRequest request, CancellationToken ct = default)
    {
        var empresa = await empresas.GetByIdAsync(id, ct) ?? throw new NotFoundException("Empresa", id);
        var documento = DocumentoFiscal.SomenteDigitos(request.Documento);
        await GarantirDocumentoUnicoAsync(documento, id, ct);

        empresa.Documento = documento;
        Aplicar(empresa, request);
        await unitOfWork.SaveChangesAsync(ct);
        return EmpresaDto.From(empresa);
    }

    public async Task ExcluirAsync(Guid id, CancellationToken ct = default)
    {
        var empresa = await empresas.GetByIdAsync(id, ct) ?? throw new NotFoundException("Empresa", id);
        empresas.Remove(empresa); // soft delete via interceptor
        await unitOfWork.SaveChangesAsync(ct);
    }

    private async Task GarantirDocumentoUnicoAsync(string documento, Guid? exceto, CancellationToken ct)
    {
        if (await empresas.DocumentoExisteAsync(documento, exceto, ct))
            throw new ConflictException("Já existe uma empresa cadastrada com este CNPJ/CPF.");
    }

    private static void Aplicar(Empresa e, SalvarEmpresaRequest r)
    {
        e.RazaoSocial = r.RazaoSocial.Trim();
        e.NomeFantasia = r.NomeFantasia?.Trim();
        e.Apelido = r.Apelido?.Trim();
        e.InscricaoEstadual = r.InscricaoEstadual?.Trim();
        e.InscricaoMunicipal = r.InscricaoMunicipal?.Trim();
        e.RegimeTributario = r.RegimeTributario;
        e.Situacao = r.Situacao;
        e.Email = r.Email?.Trim().ToLowerInvariant();
        e.Telefone = r.Telefone?.Trim();
        e.Endereco = r.Endereco?.ToEntity();
    }
}
