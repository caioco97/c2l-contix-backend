using Contix.Domain.ValueObjects;
using Contix.Application.Common.Abstractions;
using Contix.Application.Common.Exceptions;
using Contix.Domain.Entities;

namespace Contix.Application.Features.Escritorios;

public interface IEscritorioRepository : IRepository<Escritorio>
{
    Task<bool> CnpjExisteAsync(string cnpj, CancellationToken ct = default);
}

public interface IEscritorioService
{
    Task<EscritorioDto> ObterAtualAsync(CancellationToken ct = default);
    Task<EscritorioDto> AtualizarAtualAsync(AtualizarEscritorioRequest request, CancellationToken ct = default);
}

public sealed class EscritorioService(
    IEscritorioRepository escritorios,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : IEscritorioService
{
    public async Task<EscritorioDto> ObterAtualAsync(CancellationToken ct = default) =>
        EscritorioDto.From(await CarregarAsync(ct));

    public async Task<EscritorioDto> AtualizarAtualAsync(AtualizarEscritorioRequest request, CancellationToken ct = default)
    {
        var e = await CarregarAsync(ct);
        e.RazaoSocial = request.RazaoSocial.Trim();
        e.NomeFantasia = request.NomeFantasia?.Trim();
        e.Crc = request.Crc?.Trim();
        e.Email = request.Email?.Trim().ToLowerInvariant();
        e.Telefone = DadosBrasileiros.NormalizarTelefone(request.Telefone);
        e.Endereco = request.Endereco?.ToEntity();
        e.LogoUrl = request.LogoUrl;
        if (request.DiasLembreteVencimento is { } dias) e.DiasLembreteVencimento = dias;
        await unitOfWork.SaveChangesAsync(ct);
        return EscritorioDto.From(e);
    }

    private async Task<Escritorio> CarregarAsync(CancellationToken ct) =>
        await escritorios.GetByIdAsync(currentUser.RequireTenantId(), ct)
        ?? throw new NotFoundException("Escritório");
}
