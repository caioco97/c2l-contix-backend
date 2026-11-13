using System.Text.Json;
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
    INotificador notificador,
    INotificacaoRepository destinatarios,
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
        await acesso.GarantirModuloAsync(empresaId, ModuloPortal.Solicitacoes, ct);
        return await solicitacoes.PesquisarAsync(filtro, empresaId, Hoje, ct);
    }

    public async Task<SolicitacaoDto> ObterAsync(Guid empresaId, Guid solicitacaoId, CancellationToken ct = default)
    {
        await acesso.GarantirModuloAsync(empresaId, ModuloPortal.Solicitacoes, ct);
        return ToDto(await CarregarAsync(empresaId, solicitacaoId, ct));
    }

    public async Task<SolicitacaoDto> AbrirAsync(Guid empresaId, AbrirSolicitacaoCommand command, CancellationToken ct = default)
    {
        await abrirValidator.ValidateAndThrowAsync(command, ct);
        var (empresa, _) = await acesso.GarantirModuloAsync(empresaId, ModuloPortal.Solicitacoes, ct);
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
            var alvo = currentUser.EhCliente
                ? await destinatarios.ListarEquipeDoEscritorioAsync(empresa.TenantId, ct)
                : await destinatarios.ListarClientesDaEmpresaAsync(empresa.TenantId, empresaId, ModuloPortal.Solicitacoes, ct);
            await NotificarAsync(solicitacao, alvo, TipoNotificacao.NovaSolicitacao,
                $"Nova solicitação #{solicitacao.Numero}: {solicitacao.Assunto}",
                $"{tipo.Nome} aberta para a empresa {empresa.NomeFantasia ?? empresa.RazaoSocial}.", ct);
        }, ct);

        logger.LogInformation("Solicitação {Numero} ({Tipo}) aberta na empresa {EmpresaId} pelo usuário {UsuarioId}",
            solicitacao.Numero, solicitacao.Tipo, empresaId, usuarioId);
        return await ObterAsync(empresaId, solicitacao.Id, ct);
    }

    public async Task<SolicitacaoDto> ComentarAsync(Guid empresaId, Guid solicitacaoId, ComentarSolicitacaoCommand command, CancellationToken ct = default)
    {
        await comentarValidator.ValidateAndThrowAsync(command, ct);
        await acesso.GarantirModuloAsync(empresaId, ModuloPortal.Solicitacoes, ct);
        var solicitacao = await CarregarAsync(empresaId, solicitacaoId, ct);

        var interacao = solicitacao.Comentar(currentUser.RequireUserId(), currentUser.EhCliente, command.Mensagem,
            command.Anexos.Count > 0, clock.UtcNow);
        await SalvarComAnexosAsync(solicitacao, interacao, command.Anexos, () => NotificarAtualizacaoAsync(solicitacao,
            $"Nova mensagem na solicitação #{solicitacao.Numero}",
            string.IsNullOrWhiteSpace(command.Mensagem)
                ? $"Foram adicionados anexos à solicitação \"{solicitacao.Assunto}\"."
                : $"\"{Resumir(command.Mensagem)}\" — solicitação \"{solicitacao.Assunto}\".", ct), ct);
        return await ObterAsync(empresaId, solicitacaoId, ct);
    }

    public async Task<SolicitacaoDto> AlterarStatusAsync(Guid empresaId, Guid solicitacaoId, AlterarStatusRequest request, CancellationToken ct = default)
    {
        await acesso.GarantirModuloAsync(empresaId, ModuloPortal.Solicitacoes, ct);
        var solicitacao = await CarregarAsync(empresaId, solicitacaoId, ct);
        var anterior = solicitacao.Status;

        solicitacao.AlterarStatus(request.Status, currentUser.RequireUserId(), currentUser.EhCliente, request.Mensagem, clock.UtcNow);
        await NotificarAtualizacaoAsync(solicitacao,
            $"Solicitação #{solicitacao.Numero}: {NomeStatus(request.Status)}",
            $"A solicitação \"{solicitacao.Assunto}\" passou de {NomeStatus(anterior)} para {NomeStatus(request.Status)}." +
            (string.IsNullOrWhiteSpace(request.Mensagem) ? "" : $" Mensagem: {Resumir(request.Mensagem)}"), ct);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation("Solicitação {SolicitacaoId}: status {Anterior} -> {Novo} pelo usuário {UsuarioId}",
            solicitacaoId, anterior, request.Status, currentUser.UserId);
        return await ObterAsync(empresaId, solicitacaoId, ct);
    }

    public async Task<SolicitacaoDto> AtribuirAsync(Guid empresaId, Guid solicitacaoId, AtribuirSolicitacaoRequest request, CancellationToken ct = default)
    {
        if (currentUser.EhCliente) throw new ForbiddenException();
        await acesso.GarantirModuloAsync(empresaId, ModuloPortal.Solicitacoes, ct);
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

        var responsavelAnterior = solicitacao.ResponsavelId;
        solicitacao.Atribuir(request.ResponsavelId, nome, request.Prazo, currentUser.RequireUserId(), clock.UtcNow);
        if (request.ResponsavelId is { } novo && novo != responsavelAnterior)
        {
            var alvo = await destinatarios.ListarPorIdsAsync(solicitacao.TenantId, [novo], ct);
            await NotificarAsync(solicitacao, alvo, TipoNotificacao.SolicitacaoAtualizada,
                $"Solicitação #{solicitacao.Numero} atribuída a você",
                $"\"{solicitacao.Assunto}\" — empresa {solicitacao.Empresa?.NomeFantasia ?? solicitacao.Empresa?.RazaoSocial}." +
                (request.Prazo is { } prazo ? $" Prazo: {prazo:dd/MM/yyyy}." : ""), ct);
        }
        await unitOfWork.SaveChangesAsync(ct);
        return await ObterAsync(empresaId, solicitacaoId, ct);
    }

    public async Task<ArquivoParaDownload> BaixarAnexoAsync(Guid empresaId, Guid solicitacaoId, Guid anexoId, CancellationToken ct = default)
    {
        await acesso.GarantirModuloAsync(empresaId, ModuloPortal.Solicitacoes, ct);
        var anexo = await solicitacoes.ObterAnexoAsync(empresaId, solicitacaoId, anexoId, ct)
                    ?? throw new NotFoundException("Anexo", anexoId);
        var stream = await storage.OpenReadAsync(anexo.StorageKey, ct);
        return new ArquivoParaDownload(stream, anexo.ContentType, anexo.NomeArquivo);
    }

    /* ---------- Auxiliares ---------- */

    /// <summary>
    /// Avisa os participantes (solicitante e responsável). Quando o cliente interage numa solicitação
    /// ainda sem responsável, avisa a equipe do escritório.
    /// </summary>
    private async Task NotificarAtualizacaoAsync(Solicitacao s, string titulo, string mensagem, CancellationToken ct)
    {
        var ids = new List<Guid> { s.SolicitanteId };
        if (s.ResponsavelId is { } r) ids.Add(r);
        var alvo = (await destinatarios.ListarPorIdsAsync(s.TenantId, ids, ct)).ToList();
        if (currentUser.EhCliente && s.ResponsavelId is null)
            alvo.AddRange(await destinatarios.ListarEquipeDoEscritorioAsync(s.TenantId, ct));
        await NotificarAsync(s, alvo, TipoNotificacao.SolicitacaoAtualizada, titulo, mensagem, ct);
    }

    private Task NotificarAsync(Solicitacao s, IEnumerable<Destinatario> alvo, TipoNotificacao tipo, string titulo, string mensagem, CancellationToken ct) =>
        notificador.NotificarAsync(s.TenantId, alvo, new EventoNotificacao(tipo, titulo, mensagem, s.EmpresaId, s.Id,
            d => d.EhDoEscritorio ? $"/atendimento/{s.EmpresaId}/{s.Id}" : $"/portal/solicitacoes/{s.Id}",
            "Ver solicitação"), ct);

    internal static string NomeStatus(StatusSolicitacao status) => status switch
    {
        StatusSolicitacao.Aberta => "Aberta",
        StatusSolicitacao.EmAnalise => "Em análise",
        StatusSolicitacao.AguardandoCliente => "Aguardando cliente",
        StatusSolicitacao.Concluida => "Concluída",
        StatusSolicitacao.Cancelada => "Cancelada",
        _ => status.ToString()
    };

    private static string Resumir(string texto) =>
        texto.Trim().Length <= 200 ? texto.Trim() : texto.Trim()[..197] + "...";

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
