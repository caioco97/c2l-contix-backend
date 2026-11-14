using System.Linq.Expressions;
using Contix.Application.Common.Abstractions;
using Contix.Domain.Common;
using Contix.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Contix.Infrastructure.Persistence;

public class ContixDbContext(DbContextOptions<ContixDbContext> options, ICurrentUser currentUser)
    : DbContext(options), IUnitOfWork
{
    public DbSet<Escritorio> Escritorios => Set<Escritorio>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<UsuarioEmpresa> UsuarioEmpresas => Set<UsuarioEmpresa>();
    public DbSet<Empresa> Empresas => Set<Empresa>();
    public DbSet<Empregado> Empregados => Set<Empregado>();
    public DbSet<Pasta> Pastas => Set<Pasta>();
    public DbSet<Documento> Documentos => Set<Documento>();
    public DbSet<DocumentoLeitura> DocumentoLeituras => Set<DocumentoLeitura>();
    public DbSet<DocumentoEvento> DocumentoEventos => Set<DocumentoEvento>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Solicitacao> Solicitacoes => Set<Solicitacao>();
    public DbSet<SolicitacaoInteracao> SolicitacaoInteracoes => Set<SolicitacaoInteracao>();
    public DbSet<SolicitacaoAnexo> SolicitacaoAnexos => Set<SolicitacaoAnexo>();
    public DbSet<Tarefa> Tarefas => Set<Tarefa>();
    public DbSet<TarefaInteracao> TarefaInteracoes => Set<TarefaInteracao>();
    public DbSet<TarefaAnexo> TarefaAnexos => Set<TarefaAnexo>();
    public DbSet<TokenRedefinicaoSenha> TokensRedefinicaoSenha => Set<TokenRedefinicaoSenha>();
    public DbSet<Notificacao> Notificacoes => Set<Notificacao>();
    public DbSet<PreferenciaNotificacao> PreferenciasNotificacao => Set<PreferenciaNotificacao>();
    public DbSet<EmailPendente> EmailsPendentes => Set<EmailPendente>();
    public DbSet<PushPendente> PushesPendentes => Set<PushPendente>();
    public DbSet<DispositivoPush> DispositivosPush => Set<DispositivoPush>();
    public DbSet<Noticia> Noticias => Set<Noticia>();
    public DbSet<NoticiaEmpresa> NoticiaEmpresas => Set<NoticiaEmpresa>();
    public DbSet<NoticiaLeitura> NoticiaLeituras => Set<NoticiaLeitura>();
    public DbSet<NoticiaAnexo> NoticiaAnexos => Set<NoticiaAnexo>();
    public DbSet<Certidao> Certidoes => Set<Certidao>();
    public DbSet<ConfiguracaoEmissaoCertidao> ConfiguracoesEmissaoCertidao => Set<ConfiguracaoEmissaoCertidao>();
    public DbSet<EmissaoCertidao> EmissoesCertidao => Set<EmissaoCertidao>();
    public DbSet<NotaFiscal> NotasFiscais => Set<NotaFiscal>();
    public DbSet<CertificadoDigital> CertificadosDigitais => Set<CertificadoDigital>();
    public DbSet<SincronizacaoNFe> SincronizacoesNFe => Set<SincronizacaoNFe>();
    public DbSet<DepartamentoEscritorio> Departamentos => Set<DepartamentoEscritorio>();
    public DbSet<ResponsavelEmpresa> ResponsaveisEmpresa => Set<ResponsavelEmpresa>();
    public DbSet<ObrigacaoRecorrente> ObrigacoesRecorrentes => Set<ObrigacaoRecorrente>();

    /// <summary>
    /// Tenant da requisição. Avaliado a cada consulta pelo filtro global.
    /// Sem usuário autenticado, vale <see cref="Guid.Empty"/> e nenhum dado de tenant é retornado.
    /// </summary>
    public Guid CurrentTenantId => currentUser.TenantId ?? Guid.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ContixDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(TenantEntity).IsAssignableFrom(entityType.ClrType) && entityType.BaseType is null)
            {
                var method = typeof(ContixDbContext)
                    .GetMethod(nameof(AplicarFiltroTenant), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .MakeGenericMethod(entityType.ClrType);
                method.Invoke(this, [modelBuilder]);
            }
        }

        base.OnModelCreating(modelBuilder);
    }

    private void AplicarFiltroTenant<T>(ModelBuilder modelBuilder) where T : TenantEntity
    {
        Expression<Func<T, bool>> filtro = e => !e.IsDeleted && e.TenantId == CurrentTenantId;
        modelBuilder.Entity<T>().HasQueryFilter(filtro);
    }
}
