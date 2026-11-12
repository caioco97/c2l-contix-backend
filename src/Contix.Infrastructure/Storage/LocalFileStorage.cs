using Contix.Application.Common.Abstractions;
using Microsoft.Extensions.Options;

namespace Contix.Infrastructure.Storage;

public sealed class FileStorageOptions
{
    public const string Secao = "FileStorage";
    /// <summary>Provider: "Local" (padrão). S3/AzureBlob podem ser adicionados implementando IFileStorage.</summary>
    public string Provider { get; set; } = "Local";
    public string LocalPath { get; set; } = "storage";
}

/// <summary>Armazena arquivos em disco. Indicado para desenvolvimento.</summary>
public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalFileStorage(IOptions<FileStorageOptions> options)
    {
        _root = Path.GetFullPath(options.Value.LocalPath);
        Directory.CreateDirectory(_root);
    }

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        var path = Resolver(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(file, ct);
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default)
    {
        var path = Resolver(key);
        if (!File.Exists(path)) throw new FileNotFoundException("Arquivo não encontrado no armazenamento.", key);
        return Task.FromResult<Stream>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true));
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var path = Resolver(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string Resolver(string key)
    {
        var path = Path.GetFullPath(Path.Combine(_root, key));
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Chave de armazenamento inválida.");
        return path;
    }
}
