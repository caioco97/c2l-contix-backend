using Contix.Application.Common.Abstractions;
using Contix.Application.Common.Exceptions;
using Contix.Application.Features.Documentos;
using Contix.Application.Features.Empresas;
using Contix.Domain.Entities;
using Contix.Domain.Enums;

namespace Contix.Application.Features.Pastas;

public interface IPastaService
{
    Task<IReadOnlyList<PastaDto>> ListarArvoreAsync(Guid empresaId, CancellationToken ct = default);
    Task<PastaDto> CriarAsync(Guid empresaId, CriarPastaRequest request, CancellationToken ct = default);
    Task<PastaDto> AtualizarAsync(Guid empresaId, Guid pastaId, AtualizarPastaRequest request, CancellationToken ct = default);
    Task ExcluirAsync(Guid empresaId, Guid pastaId, CancellationToken ct = default);
}

public sealed class PastaService(
    IPastaRepository pastas,
    IDocumentoRepository documentos,
    IAcessoEmpresaService acesso,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : IPastaService
{
    public async Task<IReadOnlyList<PastaDto>> ListarArvoreAsync(Guid empresaId, CancellationToken ct = default)
    {
        var (_, permissoes) = await acesso.GarantirModuloAsync(empresaId, ModuloPortal.Documentos, ct);
        var todas = await pastas.ListarPorEmpresaAsync(empresaId, ct);
        if (permissoes.PastasVisiveis(todas) is { } visiveis)
            todas = todas.Where(p => visiveis.Contains(p.Id)).ToList();
        var naoLidos = await documentos.ContarNaoLidosPorPastaAsync(empresaId, currentUser.RequireUserId(), ct);
        return PastaTree.Montar(todas, naoLidos);
    }

    public async Task<PastaDto> CriarAsync(Guid empresaId, CriarPastaRequest request, CancellationToken ct = default)
    {
        var empresa = await acesso.GarantirAcessoAsync(empresaId, ct);
        if (request.PastaPaiId is { } paiId)
            await CarregarAsync(empresaId, paiId, ct);

        var nome = request.Nome.Trim();
        if (await pastas.NomeExisteAsync(empresaId, request.PastaPaiId, nome, null, ct))
            throw new ConflictException("Já existe uma pasta com este nome neste local.");

        var pasta = new Pasta
        {
            TenantId = empresa.TenantId,
            EmpresaId = empresaId,
            PastaPaiId = request.PastaPaiId,
            Nome = nome,
            // Pastas criadas manualmente aparecem após as padrão, em ordem alfabética.
            Ordem = 100,
            PermiteUploadCliente = request.PermiteUploadCliente
        };
        await pastas.AddAsync(pasta, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return ToDto(pasta);
    }

    public async Task<PastaDto> AtualizarAsync(Guid empresaId, Guid pastaId, AtualizarPastaRequest request, CancellationToken ct = default)
    {
        await acesso.GarantirAcessoAsync(empresaId, ct);
        var pasta = await CarregarAsync(empresaId, pastaId, ct);

        var nome = request.Nome.Trim();
        if (await pastas.NomeExisteAsync(empresaId, pasta.PastaPaiId, nome, pasta.Id, ct))
            throw new ConflictException("Já existe uma pasta com este nome neste local.");

        pasta.Nome = nome;
        pasta.PermiteUploadCliente = request.PermiteUploadCliente;
        await unitOfWork.SaveChangesAsync(ct);
        return ToDto(pasta);
    }

    public async Task ExcluirAsync(Guid empresaId, Guid pastaId, CancellationToken ct = default)
    {
        await acesso.GarantirAcessoAsync(empresaId, ct);
        var pasta = await CarregarAsync(empresaId, pastaId, ct);
        if (await pastas.PossuiConteudoAsync(pasta.Id, ct))
            throw new ConflictException("A pasta possui subpastas ou documentos e não pode ser excluída.");

        pastas.Remove(pasta);
        await unitOfWork.SaveChangesAsync(ct);
    }

    private async Task<Pasta> CarregarAsync(Guid empresaId, Guid pastaId, CancellationToken ct)
    {
        var pasta = await pastas.GetByIdAsync(pastaId, ct);
        if (pasta is null || pasta.EmpresaId != empresaId) throw new NotFoundException("Pasta", pastaId);
        return pasta;
    }

    private static PastaDto ToDto(Pasta p) =>
        new(p.Id, p.Nome, p.PastaPaiId, p.Ordem, p.PermiteUploadCliente, 0, []);
}
