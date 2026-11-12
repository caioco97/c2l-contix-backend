using Contix.Application.Common.Abstractions;
using Contix.Application.Common.Exceptions;
using Contix.Application.Features.Documentos;
using Contix.Application.Features.Empresas;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Contix.Application.Features.Certidoes;

public interface ICertidaoService
{
    Task<IReadOnlyList<CertidaoDto>> ListarAsync(Guid empresaId, CertidaoFiltro filtro, CancellationToken ct = default);
    Task<CertidaoDto> ObterAsync(Guid empresaId, Guid certidaoId, CancellationToken ct = default);
    Task<CertidaoDto> CadastrarAsync(Guid empresaId, SalvarCertidaoCommand command, CancellationToken ct = default);
    Task<CertidaoDto> AtualizarAsync(Guid empresaId, Guid certidaoId, SalvarCertidaoCommand command, CancellationToken ct = default);
    Task ExcluirAsync(Guid empresaId, Guid certidaoId, CancellationToken ct = default);
    Task<ArquivoParaDownload> BaixarAsync(Guid empresaId, Guid certidaoId, CancellationToken ct = default);
    /// <summary>Painel de vencimentos do escritório.</summary>
    Task<PainelCertidoesDto> PainelAsync(int dias, CancellationToken ct = default);
}

public sealed class CertidaoService(
    ICertidaoRepository certidoes,
    IAcessoEmpresaService acesso,
    IFileStorage storage,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IValidator<SalvarCertidaoCommand> validator,
    ILogger<CertidaoService> logger) : ICertidaoService
{
    private DateOnly Hoje => DateOnly.FromDateTime(clock.UtcNow);

    public async Task<IReadOnlyList<CertidaoDto>> ListarAsync(Guid empresaId, CertidaoFiltro filtro, CancellationToken ct = default)
    {
        await acesso.GarantirAcessoAsync(empresaId, ct);
        return await certidoes.ListarDaEmpresaAsync(empresaId, filtro, Hoje, ct);
    }

    public async Task<CertidaoDto> ObterAsync(Guid empresaId, Guid certidaoId, CancellationToken ct = default)
    {
        await acesso.GarantirAcessoAsync(empresaId, ct);
        return await certidoes.ObterDtoAsync(empresaId, certidaoId, Hoje, ct) ?? throw new NotFoundException("Certidão", certidaoId);
    }

    public async Task<CertidaoDto> CadastrarAsync(Guid empresaId, SalvarCertidaoCommand command, CancellationToken ct = default)
    {
        GarantirEscritorio();
        await validator.ValidateAndThrowAsync(command, ct);
        var empresa = await acesso.GarantirAcessoAsync(empresaId, ct);

        var certidao = new Certidao
        {
            TenantId = empresa.TenantId, EmpresaId = empresaId, CadastradoPorId = currentUser.RequireUserId()
        };
        Aplicar(certidao, command);

        string? gravado = null;
        try
        {
            gravado = await GravarArquivoAsync(certidao, command.Arquivo, ct);
            await certidoes.AddAsync(certidao, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch
        {
            await RemoverArquivoAsync(gravado);
            throw;
        }

        logger.LogInformation("Certidão {Tipo} cadastrada na empresa {EmpresaId} (validade {Validade}) pelo usuário {UsuarioId}",
            certidao.Tipo, empresaId, certidao.Validade, certidao.CadastradoPorId);
        return await ObterAsync(empresaId, certidao.Id, ct);
    }

    public async Task<CertidaoDto> AtualizarAsync(Guid empresaId, Guid certidaoId, SalvarCertidaoCommand command, CancellationToken ct = default)
    {
        GarantirEscritorio();
        await validator.ValidateAndThrowAsync(command, ct);
        await acesso.GarantirAcessoAsync(empresaId, ct);
        var certidao = await CarregarAsync(empresaId, certidaoId, ct);

        Aplicar(certidao, command);
        var anterior = (command.RemoverArquivo || command.Arquivo is not null) ? certidao.StorageKey : null;
        if (anterior is not null) LimparArquivo(certidao);

        string? gravado = null;
        try
        {
            gravado = await GravarArquivoAsync(certidao, command.Arquivo, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch
        {
            await RemoverArquivoAsync(gravado);
            throw;
        }

        await RemoverArquivoAsync(anterior);
        return await ObterAsync(empresaId, certidaoId, ct);
    }

    public async Task ExcluirAsync(Guid empresaId, Guid certidaoId, CancellationToken ct = default)
    {
        GarantirEscritorio();
        await acesso.GarantirAcessoAsync(empresaId, ct);
        var certidao = await CarregarAsync(empresaId, certidaoId, ct);
        certidoes.Remove(certidao); // soft delete; arquivo mantido para auditoria
        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation("Certidão {CertidaoId} excluída pelo usuário {UsuarioId}", certidaoId, currentUser.UserId);
    }

    public async Task<ArquivoParaDownload> BaixarAsync(Guid empresaId, Guid certidaoId, CancellationToken ct = default)
    {
        await acesso.GarantirAcessoAsync(empresaId, ct);
        var certidao = await CarregarAsync(empresaId, certidaoId, ct);
        if (certidao.StorageKey is null) throw new NotFoundException("Arquivo da certidão");
        var stream = await storage.OpenReadAsync(certidao.StorageKey, ct);
        return new ArquivoParaDownload(stream, certidao.ContentType ?? "application/octet-stream",
            certidao.NomeArquivo ?? $"certidao{Path.GetExtension(certidao.StorageKey)}");
    }

    public async Task<PainelCertidoesDto> PainelAsync(int dias, CancellationToken ct = default)
    {
        GarantirEscritorio();
        dias = Math.Clamp(dias, 1, 365);
        var hoje = Hoje;
        var alertas = await certidoes.ListarAlertasAsync(hoje, hoje.AddDays(Math.Max(dias, Certidao.DiasAlertaVencimento)), ct);

        return new PainelCertidoesDto(
            Vencidas: alertas.Count(c => c.StatusValidade == ValidadeCertidao.Vencida),
            AVencer15Dias: alertas.Count(c => c.DiasParaVencer is >= 0 and <= 15),
            AVencer30Dias: alertas.Count(c => c.DiasParaVencer is >= 0 and <= 30),
            Irregulares: alertas.Count(c => c.Situacao == SituacaoCertidao.Positiva && c.StatusValidade != ValidadeCertidao.Vencida),
            Itens: alertas.Where(c => c.DiasParaVencer <= dias || c.Situacao == SituacaoCertidao.Positiva).ToList());
    }

    /* ---------- Auxiliares ---------- */

    private void GarantirEscritorio()
    {
        if (currentUser.EhCliente) throw new ForbiddenException("Somente o escritório cadastra certidões.");
    }

    private static void Aplicar(Certidao certidao, SalvarCertidaoCommand command)
    {
        certidao.Tipo = command.Tipo;
        certidao.Situacao = command.Situacao;
        certidao.Numero = string.IsNullOrWhiteSpace(command.Numero) ? null : command.Numero.Trim();
        certidao.Observacao = string.IsNullOrWhiteSpace(command.Observacao) ? null : command.Observacao.Trim();
        certidao.DefinirDatas(command.Emissao, command.Validade);
    }

    private static void LimparArquivo(Certidao c)
    {
        c.StorageKey = null;
        c.NomeArquivo = null;
        c.ContentType = null;
        c.TamanhoBytes = null;
    }

    private async Task<string?> GravarArquivoAsync(Certidao certidao, ArquivoUpload? arquivo, CancellationToken ct)
    {
        if (arquivo is null) return null;
        var key = $"{certidao.TenantId:N}/{certidao.EmpresaId:N}/certidoes/{certidao.Id:N}-{Guid.CreateVersion7():N}" +
                  Path.GetExtension(arquivo.NomeArquivo).ToLowerInvariant();
        var contentType = string.IsNullOrWhiteSpace(arquivo.ContentType) ? "application/octet-stream" : arquivo.ContentType;
        await using (var stream = arquivo.AbrirLeitura())
            await storage.SaveAsync(key, stream, contentType, ct);

        certidao.StorageKey = key;
        certidao.NomeArquivo = Path.GetFileName(arquivo.NomeArquivo);
        certidao.ContentType = contentType;
        certidao.TamanhoBytes = arquivo.Tamanho;
        return key;
    }

    private async Task RemoverArquivoAsync(string? key)
    {
        if (key is null) return;
        try { await storage.DeleteAsync(key, CancellationToken.None); }
        catch (Exception ex) { logger.LogWarning(ex, "Falha ao remover arquivo de certidão {StorageKey}", key); }
    }

    private async Task<Certidao> CarregarAsync(Guid empresaId, Guid certidaoId, CancellationToken ct)
    {
        var certidao = await certidoes.GetByIdAsync(certidaoId, ct);
        if (certidao is null || certidao.EmpresaId != empresaId) throw new NotFoundException("Certidão", certidaoId);
        return certidao;
    }
}
