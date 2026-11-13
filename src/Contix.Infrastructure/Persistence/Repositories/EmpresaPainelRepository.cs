using Contix.Application.Features.Empresas;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Contix.Infrastructure.Persistence.Repositories;

/// <summary>Consultas da página de detalhe da empresa. A empresa já foi validada no tenant pelo serviço.</summary>
public sealed class EmpresaPainelRepository(ContixDbContext context) : IEmpresaPainelRepository
{
    public async Task<PainelEmpresaDto> ObterPainelAsync(Guid empresaId, DateOnly hoje, CancellationToken ct = default)
    {
        var usuarios = await context.Usuarios.AsNoTracking()
            .Where(u => u.Perfil == PerfilUsuario.Cliente && u.Empresas.Any(v => v.EmpresaId == empresaId))
            .Select(u => new { u.Ativo, Pendente = u.ConviteEnviadoEm != null && u.ConviteAceitoEm == null })
            .ToListAsync(ct);

        var docs = context.Documentos.AsNoTracking().Where(d => d.EmpresaId == empresaId);
        var naoVistos = await docs.CountAsync(d => d.Origem == OrigemDocumento.Escritorio
                                                   && !d.Leituras.Any(l => l.Usuario!.Perfil == PerfilUsuario.Cliente), ct);
        var recebidos = await docs.CountAsync(d => d.Origem == OrigemDocumento.Cliente
                                                   && !d.Leituras.Any(l => l.Usuario!.Perfil != PerfilUsuario.Cliente), ct);

        var abertas = context.Solicitacoes.AsNoTracking()
            .Where(s => s.EmpresaId == empresaId && s.Status != StatusSolicitacao.Concluida && s.Status != StatusSolicitacao.Cancelada);
        var emAberto = await abertas.CountAsync(ct);
        var atrasadas = await abertas.CountAsync(s => s.Prazo != null && s.Prazo < hoje, ct);

        var tarefasAbertas = context.Tarefas.AsNoTracking()
            .Where(t => t.EmpresaId == empresaId && t.Status != StatusTarefa.Concluida && t.Status != StatusTarefa.Cancelada);
        var tarefasEmAberto = await tarefasAbertas.CountAsync(ct);
        var tarefasAtrasadas = await tarefasAbertas.CountAsync(t => t.PrazoInterno < hoje, ct);

        // Certidão atual de cada tipo = a de validade mais recente.
        var certidoes = await context.Certidoes.AsNoTracking()
            .Where(c => c.EmpresaId == empresaId)
            .Select(c => new { c.Tipo, c.Validade })
            .ToListAsync(ct);
        var atuais = certidoes.GroupBy(c => c.Tipo).Select(g => g.Max(c => c.Validade)).ToList();

        return new PainelEmpresaDto(
            UsuariosAtivos: usuarios.Count(u => u.Ativo),
            ConvitesPendentes: usuarios.Count(u => u.Ativo && u.Pendente),
            DocumentosNaoVistosPeloCliente: naoVistos,
            DocumentosRecebidosNaoLidos: recebidos,
            SolicitacoesEmAberto: emAberto,
            SolicitacoesAtrasadas: atrasadas,
            CertidoesVencidas: atuais.Count(v => Certidao.Calcular(v, hoje) == ValidadeCertidao.Vencida),
            CertidoesAVencer: atuais.Count(v => Certidao.Calcular(v, hoje) == ValidadeCertidao.AVencer),
            TarefasEmAberto: tarefasEmAberto,
            TarefasAtrasadas: tarefasAtrasadas);
    }

    public async Task<IReadOnlyList<EventoDocumentoEmpresa>> ListarEventosDocumentosAsync(Guid empresaId, DateTime antesDe, int limite, CancellationToken ct = default) =>
        // Inclui documentos excluídos: a exclusão também faz parte do histórico.
        await (from e in context.DocumentoEventos.AsNoTracking()
               join d in context.Documentos.IgnoreQueryFilters() on e.DocumentoId equals d.Id
               where d.EmpresaId == empresaId && e.OcorridoEm < antesDe
               orderby e.OcorridoEm descending, e.Id descending
               select new EventoDocumentoEmpresa(e.Id, e.Tipo, e.OcorridoEm, e.Usuario!.Nome,
                   e.Usuario.Perfil != PerfilUsuario.Cliente, e.Detalhe, d.Id, d.Nome, d.PastaId))
            .Take(limite).ToListAsync(ct);

    public async Task<IReadOnlyList<EventoSolicitacaoEmpresa>> ListarEventosSolicitacoesAsync(Guid empresaId, DateTime antesDe, int limite, CancellationToken ct = default) =>
        await (from i in context.SolicitacaoInteracoes.AsNoTracking()
               join s in context.Solicitacoes on i.SolicitacaoId equals s.Id
               where s.EmpresaId == empresaId && i.CreatedAt < antesDe
               orderby i.CreatedAt descending, i.Id descending
               select new EventoSolicitacaoEmpresa(i.Id, i.Tipo, i.CreatedAt, i.Autor!.Nome,
                   i.Autor.Perfil != PerfilUsuario.Cliente, i.Mensagem, i.StatusAnterior, i.StatusNovo, s.Id, s.Numero, s.Assunto))
            .Take(limite).ToListAsync(ct);

    public async Task<IReadOnlyList<EventoCertidaoEmpresa>> ListarEventosCertidoesAsync(Guid empresaId, DateTime antesDe, int limite, CancellationToken ct = default) =>
        await context.Certidoes.AsNoTracking()
            .Where(c => c.EmpresaId == empresaId && c.CreatedAt < antesDe)
            .OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id)
            .Select(c => new EventoCertidaoEmpresa(c.Id, c.CreatedAt, c.CadastradoPor != null ? c.CadastradoPor.Nome : "Emissão automática", c.Tipo, c.Situacao, c.Validade))
            .Take(limite).ToListAsync(ct);

    public async Task<IReadOnlyList<EventoUsuarioEmpresa>> ListarVinculosUsuariosAsync(Guid empresaId, DateTime antesDe, int limite, CancellationToken ct = default) =>
        await context.UsuarioEmpresas.AsNoTracking()
            .Where(v => v.EmpresaId == empresaId && v.VinculadoEm < antesDe)
            .OrderByDescending(v => v.VinculadoEm)
            .Select(v => new EventoUsuarioEmpresa(v.UsuarioId, v.VinculadoEm, v.Usuario!.Nome, v.Usuario.Email))
            .Take(limite).ToListAsync(ct);
}
