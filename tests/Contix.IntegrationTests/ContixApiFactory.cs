using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace Contix.IntegrationTests;

/// <summary>Sobe a API com um PostgreSQL real em container (Testcontainers) e os dados de demonstração.</summary>
public sealed class ContixApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>
    /// Com CONTIX_TEST_CONNECTION definida, usa esse PostgreSQL (banco descartável) em vez de subir um container.
    /// Útil em ambientes sem Docker.
    /// </summary>
    private static readonly string? ConexaoExterna = Environment.GetEnvironmentVariable("CONTIX_TEST_CONNECTION");
    private readonly PostgreSqlContainer? _db = ConexaoExterna is null ? new PostgreSqlBuilder("postgres:17-alpine").Build() : null;
    private readonly string _storage = Path.Combine(Path.GetTempPath(), "contix-tests-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", ConexaoExterna ?? _db!.GetConnectionString());
        builder.UseSetting("Jwt:SigningKey", "integration-tests-signing-key-0123456789abcdef");
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Database:SeedDemoData", "true");
        builder.UseSetting("FileStorage:LocalPath", _storage);
        // Jobs em segundo plano desligados: os testes acionam a fila e os lembretes de forma determinística.
        builder.UseSetting("Email:Provider", "Log");
        builder.UseSetting("RateLimit:AuthPorMinuto", "1000");
        builder.UseSetting("Email:ProcessarFila", "false");
        builder.UseSetting("Notificacoes:LembreteVencimento:Habilitado", "false");
        builder.UseSetting("Notificacoes:UrlFrontend", "https://app.contix.test");
    }

    public Task InitializeAsync() => _db?.StartAsync() ?? Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
        if (Directory.Exists(_storage)) Directory.Delete(_storage, recursive: true);
        await base.DisposeAsync();
    }
}

[CollectionDefinition(nameof(ApiCollection))]
public sealed class ApiCollection : ICollectionFixture<ContixApiFactory>;
