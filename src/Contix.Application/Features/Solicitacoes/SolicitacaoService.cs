using System.Text.Json;
using Contix.Application.Common.Abstractions;
using Contix.Application.Common.Exceptions;
using Contix.Application.Common.Models;
using Contix.Application.Features.Documentos;
using Contix.Application.Features.Empresas;
using Contix.Application.Features.Usuarios;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Contix.Application.Features.Solicitacoes;

public interface ISolicitacaoService
{
    IReadOnlyList<TipoSolicitacaoDto> ListarTipos();
    /// <summary>Fila do escritório: solicitações de todas as empresas.</summary>
    Task<PagedResult<SolicitacaoResumoDto>> PesquisarAsync(SolicitacaoFiltro filtro, CancellationToken ct = default);
    Task<PagedResult<SolicitacaoResumoDto>> PesquisarDaEmpresaAsync(Guid empresaId, SolicitacaoFiltro filtro, CancellationToken ct = default);
    Task<SolicitacaoDto> ObterAsync(Guid empresaId, Guid solicitacaoId, CancellationToken ct = default);
    Task<SolicitacaoDto> AbrirAsync(Guid empresaId, AbrirSolicitacaoCommand command, CancellationToken ct = default);
    Task<SolicitacaoDto> ComentarAsync(Guid empresaId, Guid solicitacaoId, ComentarSolicitacaoCommand command, CancellationToken ct = default);
    Task<SolicitacaoDto> AlterarStatusAsync(Guid empresaId, Guid solicitacaoId, AlterarStatusRequest request, CancellationToken ct = default);
    Task<SolicitacaoDto> AtribuirAsync(Guid empresaId, Guid solicitacaoId, AtribuirSolicitacaoRequest request, CancellationToken ct = default);
    Task<ArquivoParaDownload> BaixarAnexoAsync(Guid empresaId, Guid solicitacaoId, Guid anexoId, CancellationToken ct = default);
}

public sealed class SolicitacaoService(
    ISolicitacaoRepository solicitacoes,
    IUsuarioRepository usuarios,
    IAcessoEmpresaService acesso,
    IFileStorage storage,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IValidator<AbrirSolicitacaoCommand> abrirValidator,
    IValidator<ComentarSolicitacaoCommand> comentarValidator,
    ILogger<SolicitacaoService> logger) : ISolicitacaoService
{
    private DateOnly Hoje => DateOnly.FromDateTime(clock.UtcNow);

    public IReadOnlyList<TipoSolicitacaoDto> ListarTipos() => CatalogoSolicitacoes.Tipos;

    public Task<PagedResult<SolicitacaoResumoDto>> PesquisarAsync(SolicitacaoFiltro filtro, CancellationToken ct = default)
    {
        if (currentUser.EhCliente) throw new ForbiddenException();
        return solicitacoes.PesquisarAsync(filtro, filtro.EmpresaId, Hoje, ct);
    }

    public async Task<PagedResult<SolicitacaoResumoDto>> PesquisarDaEmpresaAsync(Guid empresaId, SolicitacaoFiltro filtro, CancellationToken ct = default)
    {
        await acesso.GarantirAcessoAsync(empresaId, ct);
        return await solicitacoes.PesquisarAsync(filtro, empresaId, Hoje, ct);
    }

    public async Task<SolicitacaoDto> ObterAsync(Guid empresaId, Guid solicitacaoId, CancellationToken ct = default)
    {
        await acesso.GarantirAcessoAsync(empresaId, ct);
        return ToDto(await CarregarAsync(empresaId, solicitacaoId, ct));
    }

    public async Task<SolicitacaoDto> AbrirAsync(Guid empresaId, AbrirSolicitacaoCommand command, CancellationToken ct = default)
    {
        await abrirValidator.ValidateAndThrowAsync(command, ct);
        var empresa = await acesso.GarantirAcessoAsync(empresaId, ct);
        var tipo = CatalogoSolicitacoes.Obter(command.Tipo);
        var formulario = FormulariosSolicitacao.Processar(command.Tipo, command.Dados);
        var usuarioId = currentUser.RequireUserId();
        var agora = clock.UtcNow;

        var solicitacao = new Solicitacao
        {
            TenantId = empresa.TenantId,
            EmpresaId = empresaId,
            Numero = await solicitacoes.ProximoNumeroAsync(empresa.TenantId, ct),
            Tipo = command.Tipo,
            Departamento = command.Departamento ?? tipo.Departamento,
            Assunto = string.IsNullOrWhiteSpace(command.Assunto) ? formulario.AssuntoSugerido : command.Assunto.Trim(),
            Descricao = string.IsNullOrWhiteSpace(command.Descricao) ? null : command.Descricao.Trim(),
            Dados = formulario.DadosJson,
            SolicitanteId = usuarioId
        };
        var abertura = solicitacao.RegistrarAbertura(agora, Hoje.AddDays(tipo.PrazoDias));

        await SalvarComAnexosAsync(solicitacao, abertura, command.Anexos, async () =>
        {
            await solicitacoes.AddAsync(solicitacao, ct);
        }, ct);

        logger.LogInformation("Solicitação {Numero} ({Tipo}) aberta na empresa {EmpresaId} pelo usuário {UsuarioId}",
            solicitacao.Numero, solicitacao.Tipo, empresaId, usuarioId);
        return await ObterAsync(empresaId, solicitacao.Id, ct);
    }

    public async Task<SolicitacaoDto> ComentarAsync(Guid empresaId, Guid solicitacaoId, ComentarSolicitacaoCommand command, CancellationToken ct = default)
    {
        await comentarValidator.ValidateAndThrowAsync(command, ct);
        await acesso.GarantirAcessoAsync(empresaId, ct);
        var solicitacao = await CarregarAsync(empresaId, solicitacaoId, ct);

        var interacao = solicitacao.Comentar(currentUser.RequireUserId(), currentUser.EhCliente, command.Mensagem,
            command.Anexos.Count > 0, clock.UtcNow);
        await SalvarComAnexosAsync(solicitacao, interacao, command.Anexos, () => Task.CompletedTask, ct);
        return await ObterAsync(empresaId, solicitacaoId, ct);
    }

    public async Task<SolicitacaoDto> AlterarStatusAsync(Guid empresaId, Guid solicitacaoId, AlterarStatusRequest request, CancellationToken ct = default)
    {
        await acesso.GarantirAcessoAsync(empresaId, ct);
        var solicitacao = await CarregarAsync(empresaId, solicitacaoId, ct);
        var anterior = solicitacao.Status;

        solicitacao.AlterarStatus(request.Status, currentUser.RequireUserId(), currentUser.EhCliente, request.Mensagem, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation("Solicitação {SolicitacaoId}: status {Anterior} -> {Novo} pelo usuário {UsuarioId}",
            solicitacaoId, anterior, request.Status, currentUser.UserId);
        return await ObterAsync(empresaId, solicitacaoId, ct);
    }

    public async Task<SolicitacaoDto> AtribuirAsync(Guid empresaId, Guid solicitacaoId, AtribuirSolicitacaoRequest request, CancellationToken ct = default)
    {
        if (currentUser.EhCliente) throw new ForbiddenException();
        await acesso.GarantirAcessoAsync(empresaId, ct);
        var solicitacao = await CarregarAsync(empresaId, solicitacaoId, ct);

        string? nome = null;
        if (request.ResponsavelId is { } responsavelId)
        {
            var responsavel = await usuarios.GetByIdAsync(responsavelId, ct);
            if (responsavel is null || !responsavel.EhDoEscritorio || !responsavel.Ativo)
                throw new ValidationException([new FluentValidation.Results.ValidationFailure(
                    "responsavelId", "Selecione um colaborador ativo do escritório.")]);
            nome = responsavel.Nome;
        }

        solicitacao.Atribuir(request.ResponsavelId, nome, request.Prazo, currentUser.RequireUserId(), clock.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);
        return await ObterAsync(empresaId, solicitacaoId, ct);
    }

    public async Task<ArquivoParaDownload> BaixarAnexoAsync(Guid empresaId, Guid solicitacaoId, Guid anexoId, CancellationToken ct = default)
    {
        await acesso.GarantirAcessoAsync(empresaId, ct);
        var anexo = await solicitacoes.ObterAnexoAsync(empresaId, solicitacaoId, anexoId, ct)
                    ?? throw new NotFoundException("Anexo", anexoId);
        var stream = await storage.OpenReadAsync(anexo.StorageKey, ct);
        return new ArquivoParaDownload(stream, anexo.ContentType, anexo.NomeArquivo);
    }

    /* ---------- Auxiliares ---------- */

    private async Task<Solicitacao> CarregarAsync(Guid empresaId, Guid solicitacaoId, CancellationToken ct) =>
        await solicitacoes.ObterCompletaAsync(empresaId, solicitacaoId, ct)
        ?? throw new NotFoundException("Solicitação", solicitacaoId);

    /// <summary>Grava os arquivos no storage e persiste; em caso de falha remove os arquivos já gravados.</summary>
    private async Task SalvarComAnexosAsync(
        Solicitacao solicitacao, SolicitacaoInteracao interacao, IReadOnlyList<ArquivoUpload> arquivos,
        Func<Task> antesDeSalvar, CancellationToken ct)
    {
        var usuarioId = currentUser.RequireUserId();
        var gravados = new List<string>();
        try
        {
            foreach (var arquivo in arquivos)
            {
                var anexo = new SolicitacaoAnexo
                {
                    SolicitacaoId = solicitacao.Id,
                    InteracaoId = interacao.Id,
                    NomeArquivo = Path.GetFileName(arquivo.NomeArquivo),
                    ContentType = string.IsNullOrWhiteSpace(arquivo.ContentType) ? "application/octet-stream" : arquivo.ContentType,
                    TamanhoBytes = arquivo.Tamanho,
                    EnviadoPorId = usuarioId
                };
                anexo.StorageKey = $"{solicitacao.TenantId:N}/{solicitacao.EmpresaId:N}/solicitacoes/{solicitacao.Id:N}/{anexo.Id:N}" +
                                   Path.GetExtension(arquivo.NomeArquivo).ToLowerInvariant();

                await using (var stream = arquivo.AbrirLeitura())
                    await storage.SaveAsync(anexo.StorageKey, stream, anexo.ContentType, ct);
                gravados.Add(anexo.StorageKey);
                solicitacao.Anexos.Add(anexo);
            }

            await antesDeSalvar();
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch
        {
            foreach (var key in gravados)
            {
                try { await storage.DeleteAsync(key, CancellationToken.None); }
                catch (Exception ex) { logger.LogWarning(ex, "Falha ao remover anexo órfão {StorageKey}", key); }
            }
            throw;
        }
    }

    private SolicitacaoDto ToDto(Solicitacao s)
    {
        var anexosPorInteracao = s.Anexos
            .OrderBy(a => a.CreatedAt)
            .GroupBy(a => a.InteracaoId ?? Guid.Empty)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<AnexoDto>)g
                .Select(a => new AnexoDto(a.Id, a.NomeArquivo, a.ContentType, a.TamanhoBytes, a.EnviadoPor?.Nome ?? "", a.CreatedAt))
                .ToList());

        var historico = s.Interacoes
            .OrderBy(i => i.CreatedAt).ThenBy(i => i.Id)
            .Select(i => new InteracaoDto(
                i.Id, i.Tipo, i.Mensagem, i.StatusAnterior, i.StatusNovo, i.AutorId, i.Autor?.Nome ?? "",
                i.Autor?.EhDoEscritorio ?? false, i.CreatedAt,
                anexosPorInteracao.GetValueOrDefault(i.Id) ?? []))
            .ToList();

        return new SolicitacaoDto(
            s.Id, s.Numero, s.EmpresaId, s.Empresa?.NomeFantasia ?? s.Empresa?.RazaoSocial ?? "",
            s.Tipo, CatalogoSolicitacoes.Obter(s.Tipo).Nome, s.Assunto, s.Departamento, s.Descricao,
            s.Dados is null ? null : JsonDocument.Parse(s.Dados).RootElement.Clone(),
            s.Status, s.SolicitanteId, s.Solicitante?.Nome ?? "", s.ResponsavelId, s.Responsavel?.Nome,
            s.Prazo, s.EstaAtrasada(Hoje), s.FinalizadaEm, s.CreatedAt, s.UpdatedAt,
            Solicitacao.TransicoesPermitidas(s.Status, currentUser.EhCliente).ToList(),
            historico);
    }
}
