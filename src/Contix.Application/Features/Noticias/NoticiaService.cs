using Contix.Application.Common.Abstractions;
using Contix.Application.Common.Exceptions;
using Contix.Application.Common.Models;
using Contix.Application.Features.Documentos;
using Contix.Application.Features.Empresas;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Contix.Application.Features.Noticias;

public interface INoticiaService
{
    /* Escritório */
    Task<PagedResult<NoticiaResumoDto>> PesquisarAsync(NoticiaFiltro filtro, CancellationToken ct = default);
    Task<NoticiaDto> ObterAsync(Guid noticiaId, CancellationToken ct = default);
    Task<NoticiaDto> CriarAsync(SalvarNoticiaCommand command, CancellationToken ct = default);
    Task<NoticiaDto> AtualizarAsync(Guid noticiaId, SalvarNoticiaCommand command, CancellationToken ct = default);
    Task ExcluirAsync(Guid noticiaId, CancellationToken ct = default);
    Task<IReadOnlyList<LeituraNoticiaDto>> ListarLeiturasAsync(Guid noticiaId, CancellationToken ct = default);
    Task<ArquivoParaDownload> BaixarImagemAsync(Guid noticiaId, CancellationToken ct = default);
    Task<ArquivoParaDownload> BaixarAnexoAsync(Guid noticiaId, Guid anexoId, CancellationToken ct = default);

    /* Portal da empresa */
    Task<PagedResult<NoticiaPortalDto>> PesquisarDaEmpresaAsync(Guid empresaId, NoticiaPortalFiltro filtro, CancellationToken ct = default);
    Task<NoticiaPortalDetalheDto> ObterDaEmpresaAsync(Guid empresaId, Guid noticiaId, CancellationToken ct = default);
    Task MarcarComoLidaAsync(Guid empresaId, Guid noticiaId, CancellationToken ct = default);
    Task<ArquivoParaDownload> BaixarImagemDaEmpresaAsync(Guid empresaId, Guid noticiaId, CancellationToken ct = default);
    Task<ArquivoParaDownload> BaixarAnexoDaEmpresaAsync(Guid empresaId, Guid noticiaId, Guid anexoId, CancellationToken ct = default);
}

public sealed class NoticiaService(
    INoticiaRepository noticias,
    IEmpresaRepository empresas,
    IAcessoEmpresaService acesso,
    IFileStorage storage,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IValidator<SalvarNoticiaCommand> validator,
    ILogger<NoticiaService> logger) : INoticiaService
{
    /// <summary>Tamanho do resumo gerado a partir do conteúdo quando o escritório não escreve um.</summary>
    public const int TamanhoResumoAutomatico = 200;

    /* ---------- Escritório ---------- */

    public async Task<PagedResult<NoticiaResumoDto>> PesquisarAsync(NoticiaFiltro filtro, CancellationToken ct = default)
    {
        GarantirEscritorio();
        var pagina = await noticias.PesquisarAsync(filtro, clock.UtcNow, ct);
        return pagina with { Items = pagina.Items.Select(n => n with { Resumo = Resumir(n.Resumo) }).ToList() };
    }

    public async Task<NoticiaDto> ObterAsync(Guid noticiaId, CancellationToken ct = default)
    {
        GarantirEscritorio();
        return ToDto(await CarregarAsync(noticiaId, ct));
    }

    public async Task<NoticiaDto> CriarAsync(SalvarNoticiaCommand command, CancellationToken ct = default)
    {
        GarantirEscritorio();
        await validator.ValidateAndThrowAsync(command, ct);
        await ValidarEmpresasAsync(command, ct);

        var noticia = new Noticia { TenantId = currentUser.RequireTenantId(), AutorId = currentUser.RequireUserId() };
        Aplicar(noticia, command);

        var arquivosNovos = new List<string>();
        try
        {
            await GravarArquivosAsync(noticia, command, arquivosNovos, ct);
            await noticias.AddAsync(noticia, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch
        {
            await RemoverArquivosAsync(arquivosNovos);
            throw;
        }

        logger.LogInformation("Notícia {NoticiaId} criada pelo usuário {UsuarioId} (público {Publico}, publicação {PublicarEm:o})",
            noticia.Id, noticia.AutorId, noticia.Publico, noticia.PublicarEm);
        return await ObterAsync(noticia.Id, ct);
    }

    public async Task<NoticiaDto> AtualizarAsync(Guid noticiaId, SalvarNoticiaCommand command, CancellationToken ct = default)
    {
        GarantirEscritorio();
        await validator.ValidateAndThrowAsync(command, ct);
        await ValidarEmpresasAsync(command, ct);
        var noticia = await CarregarAsync(noticiaId, ct);

        Aplicar(noticia, command);

        var arquivosRemovidos = new List<string>();
        foreach (var anexo in noticia.Anexos.Where(a => command.AnexosRemovidos.Contains(a.Id)).ToList())
        {
            noticia.Anexos.Remove(anexo);
            arquivosRemovidos.Add(anexo.StorageKey);
        }
        if ((command.RemoverImagem || command.Imagem is not null) && noticia.ImagemStorageKey is { } imagemAnterior)
        {
            arquivosRemovidos.Add(imagemAnterior);
            noticia.ImagemStorageKey = null;
            noticia.ImagemContentType = null;
        }

        var arquivosNovos = new List<string>();
        try
        {
            await GravarArquivosAsync(noticia, command, arquivosNovos, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch
        {
            await RemoverArquivosAsync(arquivosNovos);
            throw;
        }

        // Só depois de salvar: se o banco falhar, os arquivos antigos continuam válidos.
        await RemoverArquivosAsync(arquivosRemovidos);
        logger.LogInformation("Notícia {NoticiaId} alterada pelo usuário {UsuarioId}", noticiaId, currentUser.UserId);
        return await ObterAsync(noticiaId, ct);
    }

    public async Task ExcluirAsync(Guid noticiaId, CancellationToken ct = default)
    {
        GarantirEscritorio();
        var noticia = await noticias.GetByIdAsync(noticiaId, ct) ?? throw new NotFoundException("Notícia", noticiaId);
        noticias.Remove(noticia); // soft delete; arquivos mantidos para auditoria
        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation("Notícia {NoticiaId} excluída pelo usuário {UsuarioId}", noticiaId, currentUser.UserId);
    }

    public async Task<IReadOnlyList<LeituraNoticiaDto>> ListarLeiturasAsync(Guid noticiaId, CancellationToken ct = default)
    {
        GarantirEscritorio();
        var noticia = await CarregarAsync(noticiaId, ct);
        return await noticias.ListarLeiturasAsync(noticia, ct);
    }

    public async Task<ArquivoParaDownload> BaixarImagemAsync(Guid noticiaId, CancellationToken ct = default)
    {
        GarantirEscritorio();
        return await AbrirImagemAsync(await CarregarAsync(noticiaId, ct), ct);
    }

    public async Task<ArquivoParaDownload> BaixarAnexoAsync(Guid noticiaId, Guid anexoId, CancellationToken ct = default)
    {
        GarantirEscritorio();
        return await AbrirAnexoAsync(await CarregarAsync(noticiaId, ct), anexoId, ct);
    }

    /* ---------- Portal da empresa ---------- */

    public async Task<PagedResult<NoticiaPortalDto>> PesquisarDaEmpresaAsync(Guid empresaId, NoticiaPortalFiltro filtro, CancellationToken ct = default)
    {
        var empresa = await acesso.GarantirAcessoAsync(empresaId, ct);
        var pagina = await noticias.PesquisarDaEmpresaAsync(empresa, filtro, currentUser.RequireUserId(), clock.UtcNow, ct);
        return pagina with { Items = pagina.Items.Select(n => n with { Resumo = Resumir(n.Resumo) }).ToList() };
    }

    public async Task<NoticiaPortalDetalheDto> ObterDaEmpresaAsync(Guid empresaId, Guid noticiaId, CancellationToken ct = default)
    {
        var (empresa, noticia) = await CarregarVisivelAsync(empresaId, noticiaId, ct);
        var lidaEm = await noticias.ObterLeituraAsync(noticia.Id, empresa.Id, currentUser.RequireUserId(), ct);
        return new NoticiaPortalDetalheDto(
            noticia.Id, noticia.Titulo, Resumir(noticia.Resumo ?? noticia.Conteudo), ConteudoNoticia.ParaExibir(noticia.Conteudo), noticia.PublicarEm,
            noticia.ImagemStorageKey is not null, ToAnexosDto(noticia), lidaEm is not null, lidaEm);
    }

    public async Task MarcarComoLidaAsync(Guid empresaId, Guid noticiaId, CancellationToken ct = default)
    {
        var (empresa, noticia) = await CarregarVisivelAsync(empresaId, noticiaId, ct);
        await noticias.MarcarComoLidaAsync(noticia.Id, empresa.Id, currentUser.RequireUserId(), clock.UtcNow, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<ArquivoParaDownload> BaixarImagemDaEmpresaAsync(Guid empresaId, Guid noticiaId, CancellationToken ct = default)
    {
        var (_, noticia) = await CarregarVisivelAsync(empresaId, noticiaId, ct);
        return await AbrirImagemAsync(noticia, ct);
    }

    public async Task<ArquivoParaDownload> BaixarAnexoDaEmpresaAsync(Guid empresaId, Guid noticiaId, Guid anexoId, CancellationToken ct = default)
    {
        var (_, noticia) = await CarregarVisivelAsync(empresaId, noticiaId, ct);
        return await AbrirAnexoAsync(noticia, anexoId, ct);
    }

    /* ---------- Auxiliares ---------- */

    /// <summary>Texto do resumo: o informado ou o início do conteúdo (sem HTML), cortado em uma palavra inteira.</summary>
    public static string Resumir(string? texto)
    {
        texto = ConteudoNoticia.ParaTexto(texto);
        if (string.IsNullOrWhiteSpace(texto)) return string.Empty;
        var limpo = string.Join(' ', texto.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (limpo.Length <= TamanhoResumoAutomatico) return limpo;
        var corte = limpo.LastIndexOf(' ', TamanhoResumoAutomatico);
        return limpo[..(corte > TamanhoResumoAutomatico / 2 ? corte : TamanhoResumoAutomatico)].TrimEnd(',', '.', ';', ':') + "…";
    }

    private void GarantirEscritorio()
    {
        if (currentUser.EhCliente) throw new ForbiddenException();
    }

    private async Task ValidarEmpresasAsync(SalvarNoticiaCommand command, CancellationToken ct)
    {
        if (command.Publico != PublicoNoticia.EmpresasSelecionadas) return;
        var ids = command.EmpresaIds.Distinct().ToList();
        if (await empresas.ContarExistentesAsync(ids, ct) != ids.Count)
            throw new ValidationException([new FluentValidation.Results.ValidationFailure(
                "empresaIds", "Uma ou mais empresas selecionadas não foram encontradas.")]);
    }

    private void Aplicar(Noticia noticia, SalvarNoticiaCommand command)
    {
        noticia.Titulo = command.Titulo.Trim();
        noticia.Resumo = string.IsNullOrWhiteSpace(command.Resumo) ? null : command.Resumo.Trim();
        noticia.Conteudo = ConteudoNoticia.ParaGravar(command.Conteudo);
        noticia.DefinirPeriodo(command.PublicarEm?.ToUniversalTime() ?? clock.UtcNow, command.ExpiraEm?.ToUniversalTime());
        noticia.DefinirPublico(command.Publico, command.EmpresaIds, command.Regimes);
    }

    private async Task GravarArquivosAsync(Noticia noticia, SalvarNoticiaCommand command, List<string> gravados, CancellationToken ct)
    {
        var prefixo = $"{noticia.TenantId:N}/noticias/{noticia.Id:N}";

        if (command.Imagem is { } imagem)
        {
            var key = $"{prefixo}/imagem-{Guid.CreateVersion7():N}{Path.GetExtension(imagem.NomeArquivo).ToLowerInvariant()}";
            var contentType = string.IsNullOrWhiteSpace(imagem.ContentType) ? "application/octet-stream" : imagem.ContentType;
            await using (var stream = imagem.AbrirLeitura())
                await storage.SaveAsync(key, stream, contentType, ct);
            gravados.Add(key);
            noticia.ImagemStorageKey = key;
            noticia.ImagemContentType = contentType;
        }

        foreach (var arquivo in command.Anexos)
        {
            var anexo = new NoticiaAnexo
            {
                NoticiaId = noticia.Id,
                NomeArquivo = Path.GetFileName(arquivo.NomeArquivo),
                ContentType = string.IsNullOrWhiteSpace(arquivo.ContentType) ? "application/octet-stream" : arquivo.ContentType,
                TamanhoBytes = arquivo.Tamanho
            };
            anexo.StorageKey = $"{prefixo}/{anexo.Id:N}{Path.GetExtension(arquivo.NomeArquivo).ToLowerInvariant()}";
            await using (var stream = arquivo.AbrirLeitura())
                await storage.SaveAsync(anexo.StorageKey, stream, anexo.ContentType, ct);
            gravados.Add(anexo.StorageKey);
            noticia.Anexos.Add(anexo);
        }
    }

    private async Task RemoverArquivosAsync(IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            try { await storage.DeleteAsync(key, CancellationToken.None); }
            catch (Exception ex) { logger.LogWarning(ex, "Falha ao remover arquivo de notícia {StorageKey}", key); }
        }
    }

    private async Task<Noticia> CarregarAsync(Guid noticiaId, CancellationToken ct) =>
        await noticias.ObterCompletaAsync(noticiaId, ct) ?? throw new NotFoundException("Notícia", noticiaId);

    /// <summary>Notícias agendadas, expiradas ou de outro público respondem 404 no portal.</summary>
    private async Task<(Empresa, Noticia)> CarregarVisivelAsync(Guid empresaId, Guid noticiaId, CancellationToken ct)
    {
        var empresa = await acesso.GarantirAcessoAsync(empresaId, ct);
        var noticia = await noticias.ObterVisivelAsync(empresa, noticiaId, clock.UtcNow, ct)
                      ?? throw new NotFoundException("Notícia", noticiaId);
        return (empresa, noticia);
    }

    private async Task<ArquivoParaDownload> AbrirImagemAsync(Noticia noticia, CancellationToken ct)
    {
        if (noticia.ImagemStorageKey is null) throw new NotFoundException("Imagem da notícia");
        var stream = await storage.OpenReadAsync(noticia.ImagemStorageKey, ct);
        return new ArquivoParaDownload(stream, noticia.ImagemContentType ?? "application/octet-stream",
            "imagem" + Path.GetExtension(noticia.ImagemStorageKey));
    }

    private async Task<ArquivoParaDownload> AbrirAnexoAsync(Noticia noticia, Guid anexoId, CancellationToken ct)
    {
        var anexo = noticia.Anexos.FirstOrDefault(a => a.Id == anexoId) ?? throw new NotFoundException("Anexo", anexoId);
        var stream = await storage.OpenReadAsync(anexo.StorageKey, ct);
        return new ArquivoParaDownload(stream, anexo.ContentType, anexo.NomeArquivo);
    }

    private static IReadOnlyList<NoticiaAnexoDto> ToAnexosDto(Noticia n) =>
        n.Anexos.OrderBy(a => a.CreatedAt).ThenBy(a => a.NomeArquivo)
            .Select(a => new NoticiaAnexoDto(a.Id, a.NomeArquivo, a.ContentType, a.TamanhoBytes)).ToList();

    private NoticiaDto ToDto(Noticia n) => new(
        n.Id, n.Titulo, n.Resumo, ConteudoNoticia.ParaGravar(n.Conteudo), n.PublicarEm, n.ExpiraEm, n.Situacao(clock.UtcNow), n.Publico, n.Regimes,
        n.Empresas.Select(v => new EmpresaRefDto(v.EmpresaId, v.Empresa?.NomeFantasia ?? v.Empresa?.RazaoSocial ?? ""))
            .OrderBy(e => e.Nome).ToList(),
        n.ImagemStorageKey is not null, ToAnexosDto(n), n.AutorId, n.Autor?.Nome ?? "", n.CreatedAt, n.UpdatedAt);
}
