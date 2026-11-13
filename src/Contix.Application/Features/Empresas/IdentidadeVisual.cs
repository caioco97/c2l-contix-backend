using Contix.Application.Common.Abstractions;
using Contix.Application.Common.Exceptions;
using Contix.Application.Features.Documentos;
using Contix.Domain.Entities;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Contix.Application.Features.Empresas;

/// <summary>
/// Identidade visual da empresa no portal do cliente: cores e logotipo. Campos nulos usam o padrão do Contix.
/// </summary>
/// <param name="LogoVersao">Muda a cada troca de logotipo; nulo quando a empresa não tem logotipo.</param>
public sealed record IdentidadeVisualDto(string? CorPrimaria, string? CorSecundaria, bool PossuiLogo, string? LogoVersao)
{
    public static IdentidadeVisualDto From(Empresa e) =>
        new(e.CorPrimaria, e.CorSecundaria, e.LogoStorageKey != null, Versao(e.LogoStorageKey, e.LogoAtualizadoEm));

    /// <summary>Versão do logotipo a partir dos campos (também usada nas projeções do EF).</summary>
    public static string? Versao(string? chave, DateTime? atualizadoEm) =>
        chave == null || atualizadoEm == null ? null : atualizadoEm.Value.Ticks.ToString();
}

public sealed record SalvarCoresRequest(string? CorPrimaria, string? CorSecundaria);

public sealed class SalvarCoresRequestValidator : AbstractValidator<SalvarCoresRequest>
{
    private const string Hex = "^#[0-9a-fA-F]{6}$";

    public SalvarCoresRequestValidator()
    {
        RuleFor(x => x.CorPrimaria).Matches(Hex).WithMessage("Informe a cor no formato #RRGGBB.")
            .When(x => !string.IsNullOrWhiteSpace(x.CorPrimaria));
        RuleFor(x => x.CorSecundaria).Matches(Hex).WithMessage("Informe a cor no formato #RRGGBB.")
            .When(x => !string.IsNullOrWhiteSpace(x.CorSecundaria));
    }
}

public sealed record LogoArquivo(Stream Conteudo, string ContentType);

public interface IIdentidadeVisualService
{
    Task<IdentidadeVisualDto> SalvarCoresAsync(Guid empresaId, SalvarCoresRequest request, CancellationToken ct = default);
    Task<IdentidadeVisualDto> EnviarLogoAsync(Guid empresaId, ArquivoUpload arquivo, CancellationToken ct = default);
    Task<IdentidadeVisualDto> RemoverLogoAsync(Guid empresaId, CancellationToken ct = default);
    /// <summary>Logotipo da empresa, para o escritório e para os usuários vinculados a ela.</summary>
    Task<LogoArquivo> ObterLogoAsync(Guid empresaId, CancellationToken ct = default);
}

public sealed class IdentidadeVisualService(
    IEmpresaRepository empresas,
    IAcessoEmpresaService acesso,
    IFileStorage storage,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock,
    ILogger<IdentidadeVisualService> logger) : IIdentidadeVisualService
{
    public const long TamanhoMaximoLogo = 1024 * 1024;

    // SVG fica de fora: pode conter script e o arquivo é servido pela própria API.
    private static readonly Dictionary<string, string> Formatos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = ".png",
        ["image/jpeg"] = ".jpg",
        ["image/webp"] = ".webp",
    };

    public async Task<IdentidadeVisualDto> SalvarCoresAsync(Guid empresaId, SalvarCoresRequest request, CancellationToken ct = default)
    {
        var empresa = await ObterAsync(empresaId, ct);
        empresa.CorPrimaria = Normalizar(request.CorPrimaria);
        empresa.CorSecundaria = Normalizar(request.CorSecundaria);
        await unitOfWork.SaveChangesAsync(ct);
        return IdentidadeVisualDto.From(empresa);
    }

    public async Task<IdentidadeVisualDto> EnviarLogoAsync(Guid empresaId, ArquivoUpload arquivo, CancellationToken ct = default)
    {
        if (!Formatos.TryGetValue(arquivo.ContentType, out var extensao))
            throw new FluentValidation.ValidationException([new("arquivo", "Envie o logotipo em PNG, JPG ou WEBP.")]);
        if (arquivo.Tamanho <= 0 || arquivo.Tamanho > TamanhoMaximoLogo)
            throw new FluentValidation.ValidationException([new("arquivo", "O logotipo deve ter até 1 MB.")]);

        var empresa = await ObterAsync(empresaId, ct);
        var anterior = empresa.LogoStorageKey;
        var chave = $"{empresa.TenantId:N}/{empresa.Id:N}/identidade/logo-{Guid.NewGuid():N}{extensao}";

        await using (var stream = arquivo.AbrirLeitura())
            await storage.SaveAsync(chave, stream, arquivo.ContentType, ct);

        empresa.LogoStorageKey = chave;
        empresa.LogoContentType = arquivo.ContentType.ToLowerInvariant();
        empresa.LogoAtualizadoEm = Agora();
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch
        {
            await ApagarAsync(chave);
            throw;
        }

        if (anterior != null) await ApagarAsync(anterior);
        return IdentidadeVisualDto.From(empresa);
    }

    public async Task<IdentidadeVisualDto> RemoverLogoAsync(Guid empresaId, CancellationToken ct = default)
    {
        var empresa = await ObterAsync(empresaId, ct);
        var anterior = empresa.LogoStorageKey;
        empresa.LogoStorageKey = null;
        empresa.LogoContentType = null;
        empresa.LogoAtualizadoEm = Agora();
        await unitOfWork.SaveChangesAsync(ct);
        if (anterior != null) await ApagarAsync(anterior);
        return IdentidadeVisualDto.From(empresa);
    }

    public async Task<LogoArquivo> ObterLogoAsync(Guid empresaId, CancellationToken ct = default)
    {
        var empresa = await acesso.GarantirAcessoAsync(empresaId, ct);
        if (empresa.LogoStorageKey is null) throw new NotFoundException("Logotipo da empresa");
        return new LogoArquivo(await storage.OpenReadAsync(empresa.LogoStorageKey, ct), empresa.LogoContentType ?? "application/octet-stream");
    }

    private async Task<Empresa> ObterAsync(Guid id, CancellationToken ct) =>
        await empresas.GetByIdAsync(id, ct) ?? throw new NotFoundException("Empresa", id);

    // O PostgreSQL guarda microssegundos: truncar mantém a versão igual antes e depois de salvar.
    private DateTime Agora()
    {
        var agora = clock.UtcNow;
        return new DateTime(agora.Ticks - agora.Ticks % 10, DateTimeKind.Utc);
    }

    private static string? Normalizar(string? cor) =>
        string.IsNullOrWhiteSpace(cor) ? null : cor.Trim().ToLowerInvariant();

    private async Task ApagarAsync(string chave)
    {
        try { await storage.DeleteAsync(chave, CancellationToken.None); }
        catch (Exception ex) { logger.LogWarning(ex, "Falha ao remover logotipo {StorageKey}", chave); }
    }
}
