using System.IO.Compression;
using Contix.Application.Common.Abstractions;
using Contix.Application.Common.Exceptions;
using Contix.Application.Common.Models;
using Contix.Application.Features.Empresas;
using Contix.Application.Features.Notificacoes;
using Contix.Application.Features.Pastas;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Contix.Application.Features.Documentos;

public interface IDocumentoService
{
    Task<PagedResult<DocumentoDto>> PesquisarAsync(Guid empresaId, DocumentoFiltro filtro, CancellationToken ct = default);
    Task<DocumentoDto> ObterAsync(Guid empresaId, Guid documentoId, CancellationToken ct = default);
    Task<IReadOnlyList<DocumentoDto>> EnviarAsync(Guid empresaId, UploadDocumentosCommand command, CancellationToken ct = default);
    Task<ArquivoParaDownload> BaixarAsync(Guid empresaId, Guid documentoId, CancellationToken ct = default);
    Task<ArquivoParaDownload> BaixarZipAsync(Guid empresaId, DownloadDocumentosRequest request, CancellationToken ct = default);
    Task MarcarComoLidoAsync(Guid empresaId, Guid documentoId, CancellationToken ct = default);
    Task ExcluirAsync(Guid empresaId, Guid documentoId, CancellationToken ct = default);
    Task<IReadOnlyList<VencimentoDto>> ListarVencimentosAsync(Guid empresaId, DateOnly inicio, DateOnly fim, CancellationToken ct = default);
    /// <returns>Quantidade de usuários do cliente avisados.</returns>
    Task<int> EnviarAoClienteAsync(Guid empresaId, EnviarDocumentosRequest request, CancellationToken ct = default);
}

public sealed class DocumentoService(
    IDocumentoRepository documentos,
    IPastaRepository pastas,
    IAcessoEmpresaService acesso,
    IFileStorage storage,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IOptions<DocumentoOptions> options,
    INotificador notificador,
    INotificacaoRepository destinatarios,
    FluentValidation.IValidator<UploadDocumentosCommand> uploadValidator,
    FluentValidation.IValidator<EnviarDocumentosRequest> enviarValidator,
    ILogger<DocumentoService> logger) : IDocumentoService
{
    public async Task<PagedResult<DocumentoDto>> PesquisarAsync(Guid empresaId, DocumentoFiltro filtro, CancellationToken ct = default)
    {
        await acesso.GarantirAcessoAsync(empresaId, ct);
        return await documentos.PesquisarAsync(empresaId, filtro, currentUser.RequireUserId(), ct);
    }

    public async Task<DocumentoDto> ObterAsync(Guid empresaId, Guid documentoId, CancellationToken ct = default)
    {
        await acesso.GarantirAcessoAsync(empresaId, ct);
        return await documentos.ObterDtoAsync(empresaId, documentoId, currentUser.RequireUserId(), ct)
               ?? throw new NotFoundException("Documento", documentoId);
    }

    public async Task<IReadOnlyList<DocumentoDto>> EnviarAsync(Guid empresaId, UploadDocumentosCommand command, CancellationToken ct = default)
    {
        await FluentValidation.DefaultValidatorExtensions.ValidateAndThrowAsync(uploadValidator, command, ct);
        var empresa = await acesso.GarantirAcessoAsync(empresaId, ct);
        var pasta = await pastas.GetByIdAsync(command.PastaId, ct);
        if (pasta is null || pasta.EmpresaId != empresaId) throw new NotFoundException("Pasta", command.PastaId);
        if (currentUser.EhCliente && !pasta.PermiteUploadCliente)
            throw new ForbiddenException("Esta pasta não permite envio de documentos pelo cliente.");

        var usuarioId = currentUser.RequireUserId();
        var agora = clock.UtcNow;
        var competencia = command.Competencia is { } c ? new DateOnly(c.Year, c.Month, 1) : (DateOnly?)null;
        var salvos = new List<Documento>();

        try
        {
            foreach (var arquivo in command.Arquivos)
            {
                var doc = new Documento
                {
                    TenantId = empresa.TenantId,
                    EmpresaId = empresaId,
                    PastaId = pasta.Id,
                    Nome = Path.GetFileNameWithoutExtension(arquivo.NomeArquivo),
                    NomeArquivoOriginal = Path.GetFileName(arquivo.NomeArquivo),
                    ContentType = string.IsNullOrWhiteSpace(arquivo.ContentType) ? "application/octet-stream" : arquivo.ContentType,
                    TamanhoBytes = arquivo.Tamanho,
                    Comentario = command.Comentario?.Trim(),
                    Competencia = competencia,
                    DataVencimento = command.DataVencimento,
                    PublicadoEm = agora,
                    AdicionadoPorId = usuarioId
                };
                // Quem enviou já conhece o arquivo: não deve aparecer como "não lido" para ele.
                doc.Leituras.Add(new DocumentoLeitura { DocumentoId = doc.Id, UsuarioId = usuarioId, LidoEm = agora });
                doc.StorageKey = $"{empresa.TenantId:N}/{empresaId:N}/{doc.Id:N}{Path.GetExtension(arquivo.NomeArquivo).ToLowerInvariant()}";

                await using (var stream = arquivo.AbrirLeitura())
                    await storage.SaveAsync(doc.StorageKey, stream, doc.ContentType, ct);

                salvos.Add(doc);
                await documentos.AddAsync(doc, ct);
            }

            await NotificarNovosDocumentosAsync(empresa, pasta, salvos, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch
        {
            foreach (var d in salvos)
            {
                try { await storage.DeleteAsync(d.StorageKey, CancellationToken.None); }
                catch (Exception ex) { logger.LogWarning(ex, "Falha ao remover arquivo órfão {StorageKey}", d.StorageKey); }
            }
            throw;
        }

        logger.LogInformation("{Quantidade} documento(s) enviados para a pasta {PastaId} da empresa {EmpresaId}",
            salvos.Count, pasta.Id, empresaId);

        var resultado = new List<DocumentoDto>(salvos.Count);
        foreach (var d in salvos)
            resultado.Add((await documentos.ObterDtoAsync(empresaId, d.Id, usuarioId, ct))!);
        return resultado;
    }

    public async Task<ArquivoParaDownload> BaixarAsync(Guid empresaId, Guid documentoId, CancellationToken ct = default)
    {
        await acesso.GarantirAcessoAsync(empresaId, ct);
        var doc = await CarregarAsync(empresaId, documentoId, ct);

        var stream = await storage.OpenReadAsync(doc.StorageKey, ct);
        await documentos.MarcarComoLidosAsync([doc.Id], currentUser.RequireUserId(), clock.UtcNow, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return new ArquivoParaDownload(stream, doc.ContentType, doc.NomeArquivoOriginal);
    }

    public async Task<ArquivoParaDownload> BaixarZipAsync(Guid empresaId, DownloadDocumentosRequest request, CancellationToken ct = default)
    {
        await acesso.GarantirAcessoAsync(empresaId, ct);
        var ids = request.DocumentoIds.Distinct().ToList();
        var docs = await documentos.ListarPorIdsAsync(empresaId, ids, ct);
        if (docs.Count != ids.Count) throw new NotFoundException("Um ou mais documentos selecionados");

        var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            var nomesUsados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var doc in docs)
            {
                var entry = archive.CreateEntry(NomeUnico(doc.NomeArquivoOriginal, nomesUsados), CompressionLevel.Fastest);
                await using var origem = await storage.OpenReadAsync(doc.StorageKey, ct);
                await using var destino = entry.Open();
                await origem.CopyToAsync(destino, ct);
            }
        }
        zip.Position = 0;

        await documentos.MarcarComoLidosAsync(ids, currentUser.RequireUserId(), clock.UtcNow, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return new ArquivoParaDownload(zip, "application/zip", $"documentos-{clock.UtcNow:yyyyMMddHHmmss}.zip");
    }

    public async Task MarcarComoLidoAsync(Guid empresaId, Guid documentoId, CancellationToken ct = default)
    {
        await acesso.GarantirAcessoAsync(empresaId, ct);
        var doc = await CarregarAsync(empresaId, documentoId, ct);
        await documentos.MarcarComoLidosAsync([doc.Id], currentUser.RequireUserId(), clock.UtcNow, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task ExcluirAsync(Guid empresaId, Guid documentoId, CancellationToken ct = default)
    {
        await acesso.GarantirAcessoAsync(empresaId, ct);
        var doc = await CarregarAsync(empresaId, documentoId, ct);

        if (currentUser.EhCliente &&
            (!options.Value.ClientePodeExcluirProprios || doc.AdicionadoPorId != currentUser.RequireUserId()))
            throw new ForbiddenException("Você só pode excluir documentos enviados por você.");

        documentos.Remove(doc); // soft delete; o arquivo é mantido para auditoria
        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation("Documento {DocumentoId} excluído pelo usuário {UsuarioId}", doc.Id, currentUser.UserId);
    }

    public async Task<IReadOnlyList<VencimentoDto>> ListarVencimentosAsync(Guid empresaId, DateOnly inicio, DateOnly fim, CancellationToken ct = default)
    {
        await acesso.GarantirAcessoAsync(empresaId, ct);
        if (fim < inicio) (inicio, fim) = (fim, inicio);
        if (fim.DayNumber - inicio.DayNumber > 366)
            throw new FluentValidation.ValidationException("O período consultado deve ter no máximo 1 ano.");

        var itens = await documentos.ListarVencimentosAsync(empresaId, inicio, fim, currentUser.RequireUserId(), ct);
        if (itens.Count == 0) return itens;

        var mapa = (await pastas.ListarPorEmpresaAsync(empresaId, ct)).ToDictionary(p => p.Id);
        return itens.Select(i => i with { CaminhoPasta = PastaTree.Caminho(i.PastaId, mapa) }).ToList();
    }

    public async Task<int> EnviarAoClienteAsync(Guid empresaId, EnviarDocumentosRequest request, CancellationToken ct = default)
    {
        if (currentUser.EhCliente) throw new ForbiddenException();
        await FluentValidation.DefaultValidatorExtensions.ValidateAndThrowAsync(enviarValidator, request, ct);
        var empresa = await acesso.GarantirAcessoAsync(empresaId, ct);
        var ids = request.DocumentoIds.Distinct().ToList();
        var docs = await documentos.ListarPorIdsAsync(empresaId, ids, ct);
        if (docs.Count != ids.Count) throw new NotFoundException("Um ou mais documentos selecionados");

        var clientes = await destinatarios.ListarClientesDaEmpresaAsync(empresa.TenantId, empresaId, ct);
        if (clientes.Count == 0)
            throw new Contix.Domain.Exceptions.DomainException("A empresa não possui usuários ativos no portal para receber os documentos.");

        var nomeEmpresa = NomeEmpresa(empresa);
        var (titulo, mensagem) = docs.Count == 1
            ? ($"Documento disponível: {docs[0].Nome}", $"O documento \"{docs[0].Nome}\" da empresa {nomeEmpresa} está disponível no portal.")
            : ($"{docs.Count} documentos disponíveis", $"{docs.Count} documentos da empresa {nomeEmpresa} estão disponíveis no portal: " +
                                                     string.Join(", ", docs.Take(5).Select(d => d.Nome)) + (docs.Count > 5 ? "..." : "."));
        var pastaId = docs.Select(d => d.PastaId).Distinct().Count() == 1 ? docs[0].PastaId : (Guid?)null;

        await notificador.NotificarAsync(empresa.TenantId, clientes, new EventoNotificacao(
            TipoNotificacao.NovoDocumento, titulo, mensagem, empresaId, docs.Count == 1 ? docs[0].Id : null,
            _ => pastaId is { } p ? $"/documentos/{p}" : "/documentos", "Ver documentos",
            IgnorarPreferenciaEmail: true,
            MensagemAdicional: string.IsNullOrWhiteSpace(request.Mensagem) ? null : request.Mensagem.Trim()), ct);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation("{Quantidade} documento(s) da empresa {EmpresaId} enviados a {Destinatarios} usuário(s)",
            docs.Count, empresaId, clientes.Count);
        return clientes.Count(c => c.Id != currentUser.UserId);
    }

    private async Task NotificarNovosDocumentosAsync(Empresa empresa, Pasta pasta, IReadOnlyList<Documento> novos, CancellationToken ct)
    {
        // Escritório publicou: avisa o cliente. Cliente enviou: avisa a equipe do escritório.
        var alvo = currentUser.EhCliente
            ? await destinatarios.ListarEquipeDoEscritorioAsync(empresa.TenantId, ct)
            : await destinatarios.ListarClientesDaEmpresaAsync(empresa.TenantId, empresa.Id, ct);
        if (alvo.Count == 0) return;

        var nomeEmpresa = NomeEmpresa(empresa);
        var origem = currentUser.EhCliente ? $"A empresa {nomeEmpresa} enviou" : "Foi publicado";
        var (titulo, mensagem) = novos.Count == 1
            ? ($"Novo documento: {novos[0].Nome}", $"{origem} o documento \"{novos[0].Nome}\" na pasta {pasta.Nome}.")
            : ($"{novos.Count} novos documentos em {pasta.Nome}", $"{origem} {novos.Count} documentos na pasta {pasta.Nome}.");
        if (!currentUser.EhCliente) mensagem += $" Empresa: {nomeEmpresa}.";

        await notificador.NotificarAsync(empresa.TenantId, alvo, new EventoNotificacao(
            TipoNotificacao.NovoDocumento, titulo, mensagem, empresa.Id, novos.Count == 1 ? novos[0].Id : null,
            _ => $"/documentos/{pasta.Id}", "Ver documentos"), ct);
    }

    private static string NomeEmpresa(Empresa e) => e.NomeFantasia ?? e.RazaoSocial;

    private async Task<Documento> CarregarAsync(Guid empresaId, Guid documentoId, CancellationToken ct)
    {
        var doc = await documentos.GetByIdAsync(documentoId, ct);
        if (doc is null || doc.EmpresaId != empresaId) throw new NotFoundException("Documento", documentoId);
        return doc;
    }

    private static string NomeUnico(string nome, HashSet<string> usados)
    {
        var candidato = nome;
        var i = 1;
        while (!usados.Add(candidato))
            candidato = $"{Path.GetFileNameWithoutExtension(nome)} ({i++}){Path.GetExtension(nome)}";
        return candidato;
    }
}
