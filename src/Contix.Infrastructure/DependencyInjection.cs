using Contix.Application.Features.Solicitacoes;
using Contix.Application.Features.Tarefas;
using Contix.Application.Common.Abstractions;
using Contix.Application.Features.Auth;
using Contix.Application.Features.Certidoes;
using Contix.Application.Features.Dispositivos;
using Contix.Application.Features.Documentos;
using Contix.Application.Features.Empregados;
using Contix.Application.Features.Empresas;
using Contix.Application.Features.Equipe;
using Contix.Application.Features.Obrigacoes;
using Contix.Application.Features.Painel;
using Contix.Application.Features.Relatorios;
using Contix.Infrastructure.Pdf;
using Contix.Application.Features.Escritorios;
using Contix.Application.Features.Notificacoes;
using Contix.Application.Features.Noticias;
using Contix.Application.Features.Pastas;
using Contix.Application.Features.Usuarios;
using Contix.Application.Features.Certidoes.Emissao;
using Contix.Application.Features.NotasFiscais;
using Contix.Application.Features.NotasFiscais.Sefaz;
using Contix.Infrastructure.Certidoes;
using Contix.Infrastructure.Cnpj;
using Contix.Infrastructure.NotasFiscais;
using Contix.Infrastructure.Common;
using Contix.Infrastructure.Email;
using Contix.Infrastructure.Jobs;
using Contix.Infrastructure.Push;
using Contix.Infrastructure.Persistence;
using Contix.Infrastructure.Persistence.Interceptors;
using Contix.Infrastructure.Persistence.Repositories;
using Contix.Infrastructure.Persistence.Seed;
using Contix.Infrastructure.Security;
using Contix.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Contix.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>().Bind(configuration.GetSection(JwtOptions.Secao))
            .Validate(o => o.SigningKey.Length >= 32, "Jwt:SigningKey deve ter ao menos 32 caracteres.")
            .ValidateOnStart();
        services.AddOptions<FileStorageOptions>().Bind(configuration.GetSection(FileStorageOptions.Secao));
        services.AddOptions<DocumentoOptions>().Bind(configuration.GetSection(DocumentoOptions.Secao));
        services.AddOptions<NotificacaoOptions>().Bind(configuration.GetSection(NotificacaoOptions.Secao));
        services.AddOptions<LembreteVencimentoOptions>().Bind(configuration.GetSection(LembreteVencimentoOptions.Secao));
        services.AddOptions<EmailOptions>().Bind(configuration.GetSection(EmailOptions.Secao))
            .Validate(o => o.Provider is "Smtp" or "Log", "Email:Provider deve ser \"Smtp\" ou \"Log\".")
            .ValidateOnStart();
        services.AddOptions<PushOptions>().Bind(configuration.GetSection(PushOptions.Secao))
            .Validate(o => o.Provider is "Expo" or "Log", "Push:Provider deve ser \"Expo\" ou \"Log\".")
            .ValidateOnStart();

        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddScoped<AuditInterceptor>();

        services.AddDbContext<ContixDbContext>((sp, options) =>
        {
            var connectionString = configuration.GetConnectionString("Default")
                                   ?? throw new InvalidOperationException("ConnectionStrings:Default não configurada.");
            options.UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsHistoryTable("__ef_migrations_history"))
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(sp.GetRequiredService<AuditInterceptor>());
        });
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ContixDbContext>());

        services.AddScoped<IEscritorioRepository, EscritorioRepository>();
        services.AddScoped<IUsuarioRepository, UsuarioRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IEmpresaRepository, EmpresaRepository>();
        services.AddScoped<IEmpregadoRepository, EmpregadoRepository>();
        services.AddScoped<IEmpresaPainelRepository, EmpresaPainelRepository>();
        services.AddScoped<IAvisoRepository, AvisoRepository>();
        services.AddScoped<IPastaRepository, PastaRepository>();
        services.AddScoped<IDocumentoRepository, DocumentoRepository>();
        services.AddScoped<ISolicitacaoRepository, SolicitacaoRepository>();
        services.AddScoped<ITarefaRepository, TarefaRepository>();
        services.AddScoped<IEquipeRepository, EquipeRepository>();
        services.AddScoped<IObrigacaoRepository, ObrigacaoRepository>();
        services.AddOptions<ObrigacoesOptions>().Bind(configuration.GetSection(ObrigacoesOptions.Secao));
        services.AddHostedService<ObrigacoesRecorrentesJob>();
        services.AddScoped<IRelatorioRepository, RelatorioRepository>();
        services.AddScoped<IPainelRepository, PainelRepository>();
        services.AddSingleton<IExportadorPdf, ExportadorPdf>();
        services.AddSingleton<IGeradorDanfe, GeradorDanfe>();
        services.AddSingleton<Contix.Application.Features.Solicitacoes.IPlanilhaService, Contix.Infrastructure.Planilhas.PlanilhaService>();
        services.AddScoped<INotificacaoRepository, NotificacaoRepository>();
        services.AddScoped<ITokenRedefinicaoSenhaRepository, TokenRedefinicaoSenhaRepository>();
        services.AddScoped<INoticiaRepository, NoticiaRepository>();
        services.AddScoped<ICertidaoRepository, CertidaoRepository>();
        services.AddScoped<IEmissaoCertidaoRepository, EmissaoCertidaoRepository>();

        // Emissão automática de CND: integrador escolhido por configuração.
        services.AddOptions<EmissaoCertidaoOptions>().Bind(configuration.GetSection(EmissaoCertidaoOptions.Secao));
        var provedorCnd = configuration.GetValue<string>($"{EmissaoCertidaoOptions.Secao}:Provider") ?? "Desabilitado";
        if (provedorCnd.Equals("Infosimples", StringComparison.OrdinalIgnoreCase))
            services.AddHttpClient<IEmissorCertidao, EmissorCertidaoInfosimples>(c => c.Timeout = TimeSpan.FromMinutes(11));
        else if (provedorCnd.Equals("Simulado", StringComparison.OrdinalIgnoreCase))
            services.AddScoped<IEmissorCertidao, EmissorCertidaoSimulado>();
        else
            services.AddSingleton<IEmissorCertidao, EmissorCertidaoDesabilitado>();
        services.AddHostedService<EmissaoCertidaoJob>();

        // Certificados A1 e outros segredos guardados no banco.
        services.AddOptions<CriptografiaOptions>().Bind(configuration.GetSection(CriptografiaOptions.Secao));
        services.AddSingleton<ICriptografiaSegredos, CriptografiaAesGcm>();

        // NF-e: Distribuição DF-e e manifestação na SEFAZ (real, simulada ou desligada).
        services.AddScoped<INotaFiscalRepository, NotaFiscalRepository>();
        services.AddScoped<IIntegracaoSefazRepository, IntegracaoSefazRepository>();
        services.AddOptions<SefazOptions>().Bind(configuration.GetSection(SefazOptions.Secao));
        var provedorSefaz = configuration.GetValue<string>($"{SefazOptions.Secao}:Provider") ?? "Desabilitado";
        if (provedorSefaz.Equals("Sefaz", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<ISefazNFeGateway, SefazNFeGateway>();
        else if (provedorSefaz.Equals("Simulado", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<ISefazNFeGateway, SefazNFeSimulada>();
        else
            services.AddSingleton<ISefazNFeGateway, SefazNFeDesabilitada>();
        services.AddHostedService<SincronizacaoNFeJob>();

        // Consulta de CNPJ em APIs públicas, na ordem configurada (o próximo só entra se o anterior estiver fora do ar).
        services.AddMemoryCache();
        services.AddOptions<ConsultaCnpjOptions>().Bind(configuration.GetSection(ConsultaCnpjOptions.Secao));
        var timeoutCnpj = TimeSpan.FromSeconds(Math.Max(2, configuration.GetValue($"{ConsultaCnpjOptions.Secao}:TimeoutSegundos", 10)));
        var provedoresCnpj = configuration.GetSection($"{ConsultaCnpjOptions.Secao}:Provedores").Get<string[]>() ?? ConsultaCnpjOptions.ProvedoresPadrao;
        foreach (var provedor in provedoresCnpj.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (provedor.Equals("BrasilApi", StringComparison.OrdinalIgnoreCase))
            {
                services.AddHttpClient<ConsultaCnpjBrasilApi>(c => c.Timeout = timeoutCnpj);
                services.AddScoped<IProvedorConsultaCnpj>(sp => sp.GetRequiredService<ConsultaCnpjBrasilApi>());
            }
            else if (provedor.Equals("ReceitaWs", StringComparison.OrdinalIgnoreCase))
            {
                services.AddHttpClient<ConsultaCnpjReceitaWs>(c => c.Timeout = timeoutCnpj);
                services.AddScoped<IProvedorConsultaCnpj>(sp => sp.GetRequiredService<ConsultaCnpjReceitaWs>());
            }
            else if (provedor.Equals("Simulado", StringComparison.OrdinalIgnoreCase))
                services.AddSingleton<IProvedorConsultaCnpj, ConsultaCnpjSimulada>();
        }

        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<IFileStorage, LocalFileStorage>();

        // E-mail: caixa de saída gravada junto com a transação e enviada em segundo plano.
        services.AddScoped<IEmailQueue, EmailQueue>();
        if (configuration.GetValue<string>($"{EmailOptions.Secao}:Provider") == "Smtp")
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        else
            services.AddSingleton<IEmailSender, LogEmailSender>();
        services.AddSingleton<EmailOutboxProcessor>();
        services.AddHostedService<EmailOutboxJob>();

        // Push (app mobile): caixa de saída gravada junto com a notificação e enviada em segundo plano pelo Expo.
        services.AddScoped<IDispositivoPushRepository, DispositivoPushRepository>();
        services.AddScoped<IPushQueue, PushQueue>();
        if (configuration.GetValue<string>($"{PushOptions.Secao}:Provider") == "Expo")
            services.AddHttpClient<IPushSender, ExpoPushSender>(c => c.Timeout = TimeSpan.FromSeconds(30));
        else
            services.AddSingleton<IPushSender, LogPushSender>();
        services.AddSingleton<PushOutboxProcessor>();
        services.AddHostedService<PushOutboxJob>();
        services.AddHostedService<LembreteVencimentoJob>();

        services.AddScoped<DbInitializer>();
        services.AddHealthChecks().AddDbContextCheck<ContixDbContext>("database");

        return services;
    }
}
