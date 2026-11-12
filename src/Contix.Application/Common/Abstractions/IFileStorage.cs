namespace Contix.Application.Common.Abstractions;

/// <summary>Abstração de armazenamento de arquivos (disco local em dev; S3/Azure Blob em produção).</summary>
public interface IFileStorage
{
    Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default);
    Task<Stream> OpenReadAsync(string key, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
}
