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
    /// <summary>Quem visualizou ou baixou o documento (somente escritório).</summary>
    Task<AcessosDocumentoDto> ListarAcessosAsync(Guid empresaId, Guid documentoId, CancellationToken ct = default);
    /// <summary>Histórico de ações do documento (somente escritório).</summary>
    Task<IReadOnlyList<EventoDocumentoDto>> ListarHistoricoAsync(Guid empresaId, Guid documentoId, CancellationToken ct = default);
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
        var (_, visiveis) = await AcessoDocumentosAsync(empresaId, ct);
        if (filtro.PastaId is { } pastaId && visiveis is not null && !visiveis.Contains(pastaId))
            throw new NotFoundException("Pasta", pastaId);
        return await documentos.PesquisarAsync(empresaId, filtro, currentUser.RequireUserId(), visiveis, ct);
    }

    public async Task<DocumentoDto> ObterAsync(Guid empresaId, Guid documentoId, CancellationToken ct = default)
    {
        var (_, visiveis) = await AcessoDocumentosAsync(empresaId, ct);
        var dto = await documentos.ObterDtoAsync(empresaId, documentoId, currentUser.RequireUserId(), ct);
        if (dto is null || (visiveis is not null && !visiveis.Contains(dto.PastaId))) throw new NotFoundException("Documento", documentoId);
        return dto;
    }

    public async Task<IReadOnlyList<DocumentoDto>> EnviarAsync(Guid empresaId, UploadDocumentosCommand command, CancellationToken ct = default)
    {
        await FluentValidation.DefaultValidatorExtensions.ValidateAndThrowAsync(uploadValidator, command, ct);
        var (empresa, visiveis) = await AcessoDocumentosAsync(empresaId, ct);
        var pasta = await pastas.GetByIdAsync(command.PastaId, ct);
        if (pasta is null || pasta.EmpresaId != empresaId || (visiveis is not null && !visiveis.Contains(pasta.Id)))
            throw new NotFoundException("Pasta", command.PastaId);
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
                    AdicionadoPorId = usuarioId,
                    Origem = currentUser.EhCliente ? OrigemDocumento.Cliente : OrigemDocumento.Escritorio
                };
                doc.Eventos.Add(DocumentoEvento.Criar(doc.Id, TipoEventoDocumento.Upload, usuarioId, agora, $"Pasta {pasta.Nome}"));
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
        var (_, visiveis) = await AcessoDocumentosAsync(empresaId, ct);
        var doc = await CarregarAsync(empresaId, documentoId, visiveis, ct);

        var stream = await storage.OpenReadAsync(doc.StorageKey, ct);
        var usuarioId = currentUser.RequireUserId();
        var agora = clock.UtcNow;
        await documentos.MarcarComoLidosAsync([doc.Id], usuarioId, agora, ct);
        await documentos.RegistrarEventosAsync([DocumentoEvento.Criar(doc.Id, TipoEventoDocumento.Download, usuarioId, agora)], ct);
        await unitOfWork.SaveChangesAsync(ct);

        return new ArquivoParaDownload(stream, doc.ContentType, doc.NomeArquivoOriginal);
    }

    public async Task<ArquivoParaDownload> BaixarZipAsync(Guid empresaId, DownloadDocumentosRequest request, CancellationToken ct = default)
    {
        var (_, visiveis) = await AcessoDocumentosAsync(empresaId, ct);
        var ids = request.DocumentoIds.Distinct().ToList();
        var docs = await documentos.ListarPorIdsAsync(empresaId, ids, ct);
        if (docs.Count != ids.Count || (visiveis is not null && docs.Any(d => !visiveis.Contains(d.PastaId))))
            throw new NotFoundException("Um ou mais documentos selecionados");

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

        var usuarioId = currentUser.RequireUserId();
        var agora = clock.UtcNow;
        await documentos.MarcarComoLidosAsync(ids, usuarioId, agora, ct);
        var detalhe = $"Download em lote ({ids.Count} arquivos)";
        await documentos.RegistrarEventosAsync(
            ids.Select(id => DocumentoEvento.Criar(id, TipoEventoDocumento.Download, usuarioId, agora, detalhe)), ct);
        await unitOfWork.SaveChangesAsync(ct);

        return new ArquivoParaDownload(zip, "application/zip", $"documentos-{clock.UtcNow:yyyyMMddHHmmss}.zip");
    }

    public async Task MarcarComoLidoAsync(Guid empresaId, Guid documentoId, CancellationToken ct = default)
    {
        var (_, visiveis) = await AcessoDocumentosAsync(empresaId, ct);
        var doc = await CarregarAsync(empresaId, documentoId, visiveis, ct);
        var usuarioId = currentUser.RequireUserId();
        var agora = clock.UtcNow;
        await documentos.MarcarComoLidosAsync([doc.Id], usuarioId, agora, ct);
        await documentos.RegistrarEventosAsync([DocumentoEvento.Criar(doc.Id, TipoEventoDocumento.Visualizacao, usuarioId, agora)], ct);
        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task ExcluirAsync(Guid empresaId, Guid documentoId, CancellationToken ct = default)
    {
        var (_, visiveis) = await AcessoDocumentosAsync(empresaId, ct);
        var doc = await CarregarAsync(empresaId, documentoId, visiveis, ct);

        if (currentUser.EhCliente &&
            (!options.Value.ClientePodeExcluirProprios || doc.AdicionadoPorId != currentUser.RequireUserId()))
            throw new ForbiddenException("Você só pode excluir documentos enviados por você.");

        await documentos.RegistrarEventosAsync(
            [DocumentoEvento.Criar(doc.Id, TipoEventoDocumento.Exclusao, currentUser.RequireUserId(), clock.UtcNow)], ct);
        documentos.Remove(doc); // soft delete; o arquivo é mantido para auditoria
        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation("Documento {DocumentoId} excluído pelo usuário {UsuarioId}", doc.Id, currentUser.UserId);
    }

    public async Task<IReadOnlyList<VencimentoDto>> ListarVencimentosAsync(Guid empresaId, DateOnly inicio, DateOnly fim, CancellationToken ct = default)
    {
        var (_, visiveis) = await AcessoDocumentosAsync(empresaId, ct);
        if (fim < inicio) (inicio, fim) = (fim, inicio);
        if (fim.DayNumber - inicio.DayNumber > 366)
            throw new FluentValidation.ValidationException("O período consultado deve ter no máximo 1 ano.");

        var itens = await documentos.ListarVencimentosAsync(empresaId, inicio, fim, currentUser.RequireUserId(), visiveis, ct);
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

        var clientes = await ClientesComAcessoAsync(empresa, docs.Select(d => d.PastaId), ct);
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

        var avisados = clientes.Where(c => c.Id != currentUser.UserId).ToList();
        var detalheEnvio = $"Enviado para {avisados.Count} usuário(s): {string.Join(", ", avisados.Select(c => c.Nome))}";
        var agoraEnvio = clock.UtcNow;
        var remetente = currentUser.RequireUserId();
        await documentos.RegistrarEventosAsync(
            docs.Select(d => DocumentoEvento.Criar(d.Id, TipoEventoDocumento.Envio, remetente, agoraEnvio, detalheEnvio)), ct);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation("{Quantidade} documento(s) da empresa {EmpresaId} enviados a {Destinatarios} usuário(s)",
            docs.Count, empresaId, clientes.Count);
        return clientes.Count(c => c.Id != currentUser.UserId);
    }

    public async Task<AcessosDocumentoDto> ListarAcessosAsync(Guid empresaId, Guid documentoId, CancellationToken ct = default)
    {
        if (currentUser.EhCliente) throw new ForbiddenException();
        var empresa = await acesso.GarantirAcessoAsync(empresaId, ct);
        var doc = await CarregarAsync(empresaId, documentoId, null, ct);

        // Destinatários: quem está do outro lado de quem publicou.
        var esperados = doc.Origem == OrigemDocumento.Cliente
            ? await destinatarios.ListarEquipeDoEscritorioAsync(empresa.TenantId, ct)
            : await destinatarios.ListarClientesComAcessoAPastaAsync(empresa.TenantId, empresaId, doc.PastaId, ct);
        var interacoes = (await documentos.ListarInteracoesAsync(doc.Id, ct)).ToDictionary(i => i.UsuarioId);

        var usuarios = new List<AcessoDocumentoDto>();
        foreach (var d in esperados.Where(d => d.Id != doc.AdicionadoPorId))
        {
            interacoes.Remove(d.Id, out var i);
            usuarios.Add(new AcessoDocumentoDto(d.Id, d.Nome, d.Email, d.EhDoEscritorio,
                i?.LidoEm, i?.UltimoDownloadEm, i?.Downloads ?? 0, Destinatario: true));
        }
        // Demais usuários que abriram o documento (ex.: colegas do escritório), exceto quem publicou.
        foreach (var i in interacoes.Values.Where(i => i.UsuarioId != doc.AdicionadoPorId))
            usuarios.Add(new AcessoDocumentoDto(i.UsuarioId, i.Nome, i.Email, i.Perfil != PerfilUsuario.Cliente,
                i.LidoEm, i.UltimoDownloadEm, i.Downloads, Destinatario: false));

        var ordenados = usuarios
            .OrderByDescending(u => u.Destinatario)
            .ThenBy(u => u.VisualizadoEm is null)
            .ThenBy(u => u.Nome, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var destinatariosTotal = ordenados.Count(u => u.Destinatario);
        return new AcessosDocumentoDto(doc.Id, doc.Origem, destinatariosTotal,
            ordenados.Count(u => u.Destinatario && u.VisualizadoEm is not null), ordenados);
    }

    public async Task<IReadOnlyList<EventoDocumentoDto>> ListarHistoricoAsync(Guid empresaId, Guid documentoId, CancellationToken ct = default)
    {
        if (currentUser.EhCliente) throw new ForbiddenException();
        var (_, visiveis) = await AcessoDocumentosAsync(empresaId, ct);
        var doc = await CarregarAsync(empresaId, documentoId, visiveis, ct);
        return await documentos.ListarEventosAsync(doc.Id, ct);
    }

    private async Task NotificarNovosDocumentosAsync(Empresa empresa, Pasta pasta, IReadOnlyList<Documento> novos, CancellationToken ct)
    {
        // Escritório publicou: avisa o cliente. Cliente enviou: avisa a equipe do escritório.
        var alvo = currentUser.EhCliente
            ? await destinatarios.ListarEquipeDoEscritorioAsync(empresa.TenantId, ct)
            : await destinatarios.ListarClientesComAcessoAPastaAsync(empresa.TenantId, empresa.Id, pasta.Id, ct);
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

    /// <summary>Acesso ao módulo de documentos e pastas visíveis ao usuário (null = todas).</summary>
    private async Task<(Empresa Empresa, IReadOnlySet<Guid>? Visiveis)> AcessoDocumentosAsync(Guid empresaId, CancellationToken ct)
    {
        var (empresa, permissoes) = await acesso.GarantirModuloAsync(empresaId, ModuloPortal.Documentos, ct);
        var visiveis = permissoes.VeTodasAsPastas ? null : permissoes.PastasVisiveis(await pastas.ListarPorEmpresaAsync(empresaId, ct));
        return (empresa, visiveis);
    }

    /// <summary>Usuários do cliente que enxergam ao menos uma das pastas.</summary>
    private async Task<IReadOnlyList<Destinatario>> ClientesComAcessoAsync(Empresa empresa, IEnumerable<Guid> pastaIds, CancellationToken ct)
    {
        var todos = new List<Destinatario>();
        foreach (var pastaId in pastaIds.Distinct())
            todos.AddRange(await destinatarios.ListarClientesComAcessoAPastaAsync(empresa.TenantId, empresa.Id, pastaId, ct));
        return todos.DistinctBy(d => d.Id).ToList();
    }

    private async Task<Documento> CarregarAsync(Guid empresaId, Guid documentoId, IReadOnlySet<Guid>? visiveis, CancellationToken ct)
    {
        var doc = await documentos.GetByIdAsync(documentoId, ct);
        if (doc is null || doc.EmpresaId != empresaId || (visiveis is not null && !visiveis.Contains(doc.PastaId)))
            throw new NotFoundException("Documento", documentoId);
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
