using Contix.Application.Features.Solicitacoes;
using Contix.Application.Common.Abstractions;
using Contix.Application.Features.Auth;
using Contix.Application.Features.Documentos;
using Contix.Application.Features.Empresas;
using Contix.Application.Features.Escritorios;
using Contix.Application.Features.Notificacoes;
using Contix.Application.Features.Pastas;
using Contix.Application.Features.Usuarios;
using Contix.Infrastructure.Common;
using Contix.Infrastructure.Email;
using Contix.Infrastructure.Jobs;
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
        services.AddScoped<IPastaRepository, PastaRepository>();
        services.AddScoped<IDocumentoRepository, DocumentoRepository>();
        services.AddScoped<ISolicitacaoRepository, SolicitacaoRepository>();
        services.AddScoped<INotificacaoRepository, NotificacaoRepository>();
        services.AddScoped<ITokenRedefinicaoSenhaRepository, TokenRedefinicaoSenhaRepository>();

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
        services.AddHostedService<LembreteVencimentoJob>();

        services.AddScoped<DbInitializer>();
        services.AddHealthChecks().AddDbContextCheck<ContixDbContext>("database");

        return services;
    }
}
