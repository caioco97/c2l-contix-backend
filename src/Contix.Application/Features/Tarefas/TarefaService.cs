using Contix.Application.Common.Abstractions;
using Contix.Application.Common.Exceptions;
using Contix.Application.Common.Models;
using Contix.Application.Features.Documentos;
using Contix.Application.Features.Empresas;
using Contix.Application.Features.Notificacoes;
using Contix.Application.Features.Usuarios;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Contix.Application.Features.Tarefas;

/// <summary>Tarefas internas do escritório, por empresa. Exclusivo do escritório: o cliente não vê tarefas.</summary>
public interface ITarefaService
{
    /// <summary>Todas as tarefas do escritório (todas as empresas).</summary>
    Task<PagedResult<TarefaResumoDto>> PesquisarAsync(TarefaFiltro filtro, CancellationToken ct = default);
    Task<PagedResult<TarefaResumoDto>> PesquisarDaEmpresaAsync(Guid empresaId, TarefaFiltro filtro, CancellationToken ct = default);
    Task<TarefaDto> ObterAsync(Guid empresaId, Guid tarefaId, CancellationToken ct = default);
    Task<TarefaDto> CriarAsync(Guid empresaId, CriarTarefaCommand command, CancellationToken ct = default);
    Task<TarefaDto> EditarAsync(Guid empresaId, Guid tarefaId, EditarTarefaRequest request, CancellationToken ct = default);
    Task<TarefaDto> ComentarAsync(Guid empresaId, Guid tarefaId, ComentarTarefaCommand command, CancellationToken ct = default);
    Task<TarefaDto> AlterarStatusAsync(Guid empresaId, Guid tarefaId, AlterarStatusTarefaRequest request, CancellationToken ct = default);
    Task<ArquivoParaDownload> BaixarAnexoAsync(Guid empresaId, Guid tarefaId, Guid anexoId, CancellationToken ct = default);
}

public sealed class TarefaService(
    ITarefaRepository tarefas,
    IUsuarioRepository usuarios,
    IAcessoEmpresaService acesso,
    IFileStorage storage,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    INotificador notificador,
    INotificacaoRepository destinatarios,
    IValidator<CriarTarefaCommand> criarValidator,
    IValidator<EditarTarefaRequest> editarValidator,
    IValidator<ComentarTarefaCommand> comentarValidator,
    IValidator<AlterarStatusTarefaRequest> statusValidator,
    ILogger<TarefaService> logger) : ITarefaService
{
    private DateOnly Hoje => DateOnly.FromDateTime(clock.UtcNow);

    public Task<PagedResult<TarefaResumoDto>> PesquisarAsync(TarefaFiltro filtro, CancellationToken ct = default)
    {
        GarantirEscritorio();
        return tarefas.PesquisarAsync(filtro, filtro.EmpresaId, Hoje, ct);
    }

    public async Task<PagedResult<TarefaResumoDto>> PesquisarDaEmpresaAsync(Guid empresaId, TarefaFiltro filtro, CancellationToken ct = default)
    {
        await GarantirAcessoAsync(empresaId, ct);
        return await tarefas.PesquisarAsync(filtro, empresaId, Hoje, ct);
    }

    public async Task<TarefaDto> ObterAsync(Guid empresaId, Guid tarefaId, CancellationToken ct = default)
    {
        await GarantirAcessoAsync(empresaId, ct);
        return ToDto(await CarregarAsync(empresaId, tarefaId, ct));
    }

    public async Task<TarefaDto> CriarAsync(Guid empresaId, CriarTarefaCommand command, CancellationToken ct = default)
    {
        await criarValidator.ValidateAndThrowAsync(command, ct);
        var empresa = await GarantirAcessoAsync(empresaId, ct);
        var responsavel = await ObterResponsavelAsync(command.ResponsavelId, ct);
        var usuarioId = currentUser.RequireUserId();

        var tarefa = Tarefa.Criar(empresa.TenantId, empresaId, await tarefas.ProximoNumeroAsync(empresa.TenantId, ct),
            command.Titulo, command.Descricao, command.Departamento, responsavel.Id,
            command.PrazoInterno!.Value, command.PrazoLegal, usuarioId, clock.UtcNow);
        tarefa.Empresa = empresa;

        await SalvarComAnexosAsync(tarefa, tarefa.Interacoes.Single(), command.Anexos, async () =>
        {
            await tarefas.AddAsync(tarefa, ct);
            await NotificarResponsavelAsync(tarefa, responsavel.Id, ct);
        }, ct);

        logger.LogInformation("Tarefa {Numero} criada na empresa {EmpresaId} pelo usuário {UsuarioId}", tarefa.Numero, empresaId, usuarioId);
        return await ObterAsync(empresaId, tarefa.Id, ct);
    }

    public async Task<TarefaDto> EditarAsync(Guid empresaId, Guid tarefaId, EditarTarefaRequest request, CancellationToken ct = default)
    {
        await editarValidator.ValidateAndThrowAsync(request, ct);
        await GarantirAcessoAsync(empresaId, ct);
        var tarefa = await CarregarAsync(empresaId, tarefaId, ct);
        var responsavelAnterior = tarefa.ResponsavelId;
        var responsavel = request.ResponsavelId == responsavelAnterior
            ? tarefa.Responsavel!
            : await ObterResponsavelAsync(request.ResponsavelId, ct);

        var edicao = tarefa.Editar(request.Titulo, request.Descricao, request.Departamento, responsavel.Id, responsavel.Nome,
            request.PrazoInterno!.Value, request.PrazoLegal, currentUser.RequireUserId(), clock.UtcNow);
        if (edicao is null) return ToDto(tarefa);

        if (responsavel.Id != responsavelAnterior) await NotificarResponsavelAsync(tarefa, responsavel.Id, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return await ObterAsync(empresaId, tarefaId, ct);
    }

    public async Task<TarefaDto> ComentarAsync(Guid empresaId, Guid tarefaId, ComentarTarefaCommand command, CancellationToken ct = default)
    {
        await comentarValidator.ValidateAndThrowAsync(command, ct);
        await GarantirAcessoAsync(empresaId, ct);
        var tarefa = await CarregarAsync(empresaId, tarefaId, ct);

        var interacao = tarefa.Comentar(currentUser.RequireUserId(), command.Mensagem, command.Anexos.Count > 0, clock.UtcNow);
        await SalvarComAnexosAsync(tarefa, interacao, command.Anexos, () => Task.CompletedTask, ct);
        return await ObterAsync(empresaId, tarefaId, ct);
    }

    public async Task<TarefaDto> AlterarStatusAsync(Guid empresaId, Guid tarefaId, AlterarStatusTarefaRequest request, CancellationToken ct = default)
    {
        await statusValidator.ValidateAndThrowAsync(request, ct);
        await GarantirAcessoAsync(empresaId, ct);
        var tarefa = await CarregarAsync(empresaId, tarefaId, ct);
        var anterior = tarefa.Status;

        tarefa.AlterarStatus(request.Status, currentUser.RequireUserId(), request.Mensagem, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation("Tarefa {TarefaId}: status {Anterior} -> {Novo} pelo usuário {UsuarioId}",
            tarefaId, anterior, request.Status, currentUser.UserId);
        return await ObterAsync(empresaId, tarefaId, ct);
    }

    public async Task<ArquivoParaDownload> BaixarAnexoAsync(Guid empresaId, Guid tarefaId, Guid anexoId, CancellationToken ct = default)
    {
        await GarantirAcessoAsync(empresaId, ct);
        var anexo = await tarefas.ObterAnexoAsync(empresaId, tarefaId, anexoId, ct)
                    ?? throw new NotFoundException("Anexo", anexoId);
        var stream = await storage.OpenReadAsync(anexo.StorageKey, ct);
        return new ArquivoParaDownload(stream, anexo.ContentType, anexo.NomeArquivo);
    }

    /* ---------- Auxiliares ---------- */

    private void GarantirEscritorio()
    {
        if (currentUser.EhCliente) throw new ForbiddenException();
    }

    private async Task<Empresa> GarantirAcessoAsync(Guid empresaId, CancellationToken ct)
    {
        GarantirEscritorio();
        return await acesso.GarantirAcessoAsync(empresaId, ct);
    }

    private async Task<Usuario> ObterResponsavelAsync(Guid responsavelId, CancellationToken ct)
    {
        var responsavel = await usuarios.GetByIdAsync(responsavelId, ct);
        if (responsavel is null || !responsavel.EhDoEscritorio || !responsavel.Ativo)
            throw new ValidationException([new FluentValidation.Results.ValidationFailure(
                "responsavelId", "Selecione um colaborador ativo do escritório.")]);
        return responsavel;
    }

    private async Task NotificarResponsavelAsync(Tarefa t, Guid responsavelId, CancellationToken ct)
    {
        var alvo = await destinatarios.ListarPorIdsAsync(t.TenantId, [responsavelId], ct);
        var prazo = $"Prazo interno: {t.PrazoInterno:dd/MM/yyyy}" + (t.PrazoLegal is { } l ? $" · prazo legal: {l:dd/MM/yyyy}." : ".");
        await notificador.NotificarAsync(t.TenantId, alvo, new EventoNotificacao(
            TipoNotificacao.TarefaAtribuida,
            $"Tarefa #{t.Numero} atribuída a você",
            $"\"{t.Titulo}\" — empresa {t.Empresa?.NomeFantasia ?? t.Empresa?.RazaoSocial}. {prazo}",
            t.EmpresaId, t.Id, _ => $"/tarefas/{t.EmpresaId}/{t.Id}", "Ver tarefa"), ct);
    }

    private async Task<Tarefa> CarregarAsync(Guid empresaId, Guid tarefaId, CancellationToken ct) =>
        await tarefas.ObterCompletaAsync(empresaId, tarefaId, ct) ?? throw new NotFoundException("Tarefa", tarefaId);

    /// <summary>Grava os arquivos no storage e persiste; em caso de falha remove os arquivos já gravados.</summary>
    private async Task SalvarComAnexosAsync(
        Tarefa tarefa, TarefaInteracao interacao, IReadOnlyList<ArquivoUpload> arquivos, Func<Task> antesDeSalvar, CancellationToken ct)
    {
        var usuarioId = currentUser.RequireUserId();
        var gravados = new List<string>();
        try
        {
            foreach (var arquivo in arquivos)
            {
                var anexo = new TarefaAnexo
                {
                    TarefaId = tarefa.Id,
                    InteracaoId = interacao.Id,
                    NomeArquivo = Path.GetFileName(arquivo.NomeArquivo),
                    ContentType = string.IsNullOrWhiteSpace(arquivo.ContentType) ? "application/octet-stream" : arquivo.ContentType,
                    TamanhoBytes = arquivo.Tamanho,
                    EnviadoPorId = usuarioId
                };
                anexo.StorageKey = $"{tarefa.TenantId:N}/{tarefa.EmpresaId:N}/tarefas/{tarefa.Id:N}/{anexo.Id:N}" +
                                   Path.GetExtension(arquivo.NomeArquivo).ToLowerInvariant();

                await using (var stream = arquivo.AbrirLeitura())
                    await storage.SaveAsync(anexo.StorageKey, stream, anexo.ContentType, ct);
                gravados.Add(anexo.StorageKey);
                tarefa.Anexos.Add(anexo);
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

    private TarefaDto ToDto(Tarefa t)
    {
        var anexosPorInteracao = t.Anexos
            .OrderBy(a => a.CreatedAt)
            .GroupBy(a => a.InteracaoId ?? Guid.Empty)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<AnexoTarefaDto>)g
                .Select(a => new AnexoTarefaDto(a.Id, a.NomeArquivo, a.ContentType, a.TamanhoBytes, a.EnviadoPor?.Nome ?? "", a.CreatedAt))
                .ToList());

        var historico = t.Interacoes
            .OrderBy(i => i.CreatedAt).ThenBy(i => i.Id)
            .Select(i => new InteracaoTarefaDto(i.Id, i.Tipo, i.Mensagem, i.StatusAnterior, i.StatusNovo,
                i.AutorId, i.Autor?.Nome ?? "", i.CreatedAt, anexosPorInteracao.GetValueOrDefault(i.Id) ?? []))
            .ToList();

        return new TarefaDto(
            t.Id, t.Numero, t.EmpresaId, t.Empresa?.NomeFantasia ?? t.Empresa?.RazaoSocial ?? "",
            t.Titulo, t.Descricao, t.Departamento, t.Status, t.ResponsavelId, t.Responsavel?.Nome ?? "",
            t.CriadoPorId, t.CriadoPor?.Nome ?? "", t.PrazoInterno, t.PrazoLegal,
            t.EstaAtrasada(Hoje), t.PrazoLegalVencido(Hoje), t.ConcluidaEm, t.CreatedAt, t.UpdatedAt,
            Tarefa.TransicoesPermitidas(t.Status).ToList(), historico);
    }
}
