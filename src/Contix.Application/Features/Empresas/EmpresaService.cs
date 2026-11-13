using Contix.Application.Common.Abstractions;
using Contix.Application.Common.Exceptions;
using Contix.Application.Common.Models;
using Contix.Application.Features.Auth;
using Contix.Application.Features.Pastas;
using Contix.Application.Features.Usuarios;
using Contix.Domain.Entities;
using Contix.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

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
    IUsuarioRepository usuarios,
    IRefreshTokenRepository refreshTokens,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    ILogger<EmpresaService> logger) : IEmpresaService
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
        var agora = clock.UtcNow;

        // Usuários do portal que só tinham esta empresa perdem o acesso: as sessões abertas são encerradas
        // e o login passa a ser recusado. Quem tem outras empresas segue acessando as demais.
        var semEmpresa = await usuarios.ListarClientesExclusivosDaEmpresaAsync(id, ct);
        foreach (var usuarioId in semEmpresa)
            foreach (var token in await refreshTokens.ListarAtivosDoUsuarioAsync(usuarioId, agora, ct))
                token.Revogar(agora, "Empresa excluída");

        // Exclusão lógica (o interceptor marca IsDeleted/DeletedAt): documentos, solicitações,
        // certidões e histórico continuam no banco, mas a empresa some das listas e das consultas.
        empresa.ExcluidaPorId = currentUser.UserId;
        empresas.Remove(empresa);
        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation("Empresa {EmpresaId} excluída por {UsuarioId}; {Quantidade} usuário(s) do portal sem acesso",
            id, currentUser.UserId, semEmpresa.Count);
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
        e.InscricaoEstadual = DadosBrasileiros.NormalizarInscricaoEstadual(r.InscricaoEstadual);
        e.InscricaoMunicipal = DadosBrasileiros.NormalizarInscricaoMunicipal(r.InscricaoMunicipal);
        e.RegimeTributario = r.RegimeTributario;
        e.Situacao = r.Situacao;
        e.Email = r.Email?.Trim().ToLowerInvariant();
        e.Telefone = DadosBrasileiros.NormalizarTelefone(r.Telefone);
        e.Endereco = r.Endereco?.ToEntity();
    }
}
