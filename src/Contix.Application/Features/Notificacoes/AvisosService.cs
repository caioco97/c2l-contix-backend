using Contix.Application.Common.Abstractions;
using Contix.Application.Features.Certidoes;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Contix.Application.Features.Notificacoes;

/// <summary>Certidão atual (a de validade mais recente do tipo) que pode precisar de aviso de vencimento.</summary>
public sealed record CertidaoParaAviso(Guid Id, Guid TenantId, Guid EmpresaId, string EmpresaNome, TipoCertidao Tipo,
    DateOnly Validade, bool Aviso30, bool Aviso15, bool AvisoVencida);

/// <summary>Usuário do cliente que deve receber a notícia, com a empresa pela qual ele a recebe.</summary>
public sealed record DestinatarioNoticia(Destinatario Destinatario, Guid EmpresaId);

/// <summary>Consultas entre tenants usadas pelos avisos automáticos (jobs).</summary>
public interface IAvisoRepository
{
    /// <summary>Notícias publicadas, não expiradas e ainda sem aviso, de escritórios ativos.</summary>
    Task<IReadOnlyList<Noticia>> ListarNoticiasParaAvisarAsync(DateTime agora, int limite, CancellationToken ct = default);
    Task<Noticia?> ObterNoticiaParaAvisarAsync(Guid noticiaId, CancellationToken ct = default);
    Task<IReadOnlyList<DestinatarioNoticia>> ListarDestinatariosNoticiaAsync(Noticia noticia, CancellationToken ct = default);

    /// <summary>Certidões atuais com algum marco (30 dias, 15 dias, vencida) ainda sem aviso.</summary>
    Task<IReadOnlyList<CertidaoParaAviso>> ListarCertidoesParaAvisoAsync(DateOnly hoje, int diasVencidaMax, int limite, CancellationToken ct = default);
    /// <summary>Carrega rastreada (ignorando o tenant) para registrar o aviso.</summary>
    Task<Certidao?> ObterCertidaoAsync(Guid certidaoId, CancellationToken ct = default);
}

/// <summary>Avisa o público-alvo quando uma notícia é publicada (inclusive as agendadas, quando chega a hora).</summary>
public interface IAvisoNoticiaService
{
    /// <summary>Avisa uma notícia específica se ela já estiver publicada e sem aviso. Salva.</summary>
    Task<bool> AvisarAsync(Guid noticiaId, CancellationToken ct = default);
    /// <returns>Quantidade de notícias avisadas.</returns>
    Task<int> ProcessarAsync(CancellationToken ct = default);
}

/// <summary>Avisa o escritório e os usuários da empresa sobre CND vencendo em 30 e 15 dias e vencida.</summary>
public interface IAvisoCertidaoService
{
    /// <returns>Quantidade de avisos gerados (certidão × marco).</returns>
    Task<int> ProcessarAsync(CancellationToken ct = default);
}

public sealed class AvisoNoticiaService(
    IAvisoRepository repositorio,
    INotificador notificador,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock,
    ILogger<AvisoNoticiaService> logger) : IAvisoNoticiaService
{
    private const int Lote = 50;

    public async Task<bool> AvisarAsync(Guid noticiaId, CancellationToken ct = default)
    {
        var noticia = await repositorio.ObterNoticiaParaAvisarAsync(noticiaId, ct);
        if (noticia is null || !noticia.PrecisaAvisar(clock.UtcNow)) return false;
        await AvisarAsync(noticia, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> ProcessarAsync(CancellationToken ct = default)
    {
        var total = 0;
        while (!ct.IsCancellationRequested)
        {
            var noticias = await repositorio.ListarNoticiasParaAvisarAsync(clock.UtcNow, Lote, ct);
            if (noticias.Count == 0) break;
            foreach (var n in noticias) await AvisarAsync(n, ct);
            await unitOfWork.SaveChangesAsync(ct);
            total += noticias.Count;
            if (noticias.Count < Lote) break;
        }
        if (total > 0) logger.LogInformation("Avisos de nova notícia gerados para {Quantidade} notícia(s)", total);
        return total;
    }

    private async Task AvisarAsync(Noticia noticia, CancellationToken ct)
    {
        var destinatarios = await repositorio.ListarDestinatariosNoticiaAsync(noticia, ct);
        var resumo = Noticias.NoticiaService.Resumir(noticia.Resumo ?? noticia.Conteudo);
        // A notificação leva a empresa, para o portal abrir a notícia na empresa certa.
        foreach (var porEmpresa in destinatarios.GroupBy(d => d.EmpresaId))
        {
            await notificador.NotificarAsync(noticia.TenantId, porEmpresa.Select(d => d.Destinatario), new EventoNotificacao(
                TipoNotificacao.NovaNoticia,
                $"Nova notícia: {noticia.Titulo}",
                string.IsNullOrWhiteSpace(resumo) ? "O escritório publicou uma nova notícia no portal." : resumo,
                porEmpresa.Key, noticia.Id,
                _ => $"/?noticia={noticia.Id}",
                "Ler notícia"), ct);
        }
        noticia.MarcarNotificada(clock.UtcNow);
        logger.LogInformation("Notícia {NoticiaId} avisada a {Quantidade} usuário(s)", noticia.Id, destinatarios.Count);
    }
}

public sealed class AvisoCertidaoService(
    IAvisoRepository repositorio,
    INotificacaoRepository destinatarios,
    INotificador notificador,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock,
    ILogger<AvisoCertidaoService> logger) : IAvisoCertidaoService
{
    private const int Lote = 200;
    /// <summary>Certidões vencidas há mais tempo que isso não geram aviso (evita uma enxurrada na primeira execução).</summary>
    public const int DiasVencidaMax = 30;

    public async Task<int> ProcessarAsync(CancellationToken ct = default)
    {
        var agora = clock.UtcNow;
        var hoje = DateOnly.FromDateTime(agora);
        var total = 0;

        while (!ct.IsCancellationRequested)
        {
            var certidoes = await repositorio.ListarCertidoesParaAvisoAsync(hoje, DiasVencidaMax, Lote, ct);
            if (certidoes.Count == 0) break;
            var antes = total;

            foreach (var porEmpresa in certidoes.GroupBy(c => (c.TenantId, c.EmpresaId)))
            {
                var (tenantId, empresaId) = porEmpresa.Key;
                var alvo = (await destinatarios.ListarEquipeDoEscritorioAsync(tenantId, ct))
                    .Concat(await destinatarios.ListarClientesDaEmpresaAsync(tenantId, empresaId, null, ct))
                    .ToList();

                foreach (var c in porEmpresa)
                {
                    var certidao = await repositorio.ObterCertidaoAsync(c.Id, ct);
                    var marco = Certidao.MarcoDevido(c.Validade, hoje, c.Aviso30, c.Aviso15, c.AvisoVencida, DiasVencidaMax);
                    if (certidao is null || marco is null) continue;

                    var tipo = CatalogoCertidoes.Nome(c.Tipo);
                    var dias = c.Validade.DayNumber - hoje.DayNumber;
                    var (titulo, mensagem) = marco == MarcoAvisoCertidao.Vencida
                        ? ($"CND vencida: {tipo} — {c.EmpresaNome}",
                           $"A certidão {tipo} da empresa {c.EmpresaNome} venceu em {c.Validade:dd/MM/yyyy}. Providencie a renovação.")
                        : ($"CND a vencer: {tipo} — {c.EmpresaNome}",
                           $"A certidão {tipo} da empresa {c.EmpresaNome} {(dias == 0 ? "vence hoje" : dias == 1 ? "vence amanhã" : $"vence em {dias} dias")} ({c.Validade:dd/MM/yyyy}).");

                    await notificador.NotificarAsync(tenantId, alvo, new EventoNotificacao(
                        TipoNotificacao.CertidaoVencimento, titulo, mensagem, empresaId, c.Id,
                        d => d.EhDoEscritorio ? $"/empresas/{empresaId}?aba=cnd" : "/certidoes",
                        "Ver certidões"), ct);
                    certidao.RegistrarAviso(marco.Value, agora);
                    total++;
                }
            }

            await unitOfWork.SaveChangesAsync(ct);
            // Sem progresso (consulta e regra divergentes): para, em vez de repetir o mesmo lote.
            if (certidoes.Count < Lote || total == antes) break;
        }

        if (total > 0) logger.LogInformation("Avisos de vencimento de CND gerados: {Quantidade}", total);
        return total;
    }
}
