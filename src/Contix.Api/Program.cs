using Contix.Api.Extensions;
using Contix.Api.Infrastructure;
using Contix.Application;
using Contix.Infrastructure;
using Contix.Infrastructure.Persistence.Seed;
using Serilog;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, config) => config
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "Contix.Api"));

    builder.Services
        .AddApplication()
        .AddInfrastructure(builder.Configuration)
        .AddApi(builder.Configuration);

    var app = builder.Build();

    await app.InicializarBancoAsync();

    app.UseForwardedHeaders();
    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseSerilogRequestLogging(o =>
        o.EnrichDiagnosticContext = (diag, http) => diag.Set("UserId", http.User.FindFirst("sub")?.Value));
    app.UseExceptionHandler();
    app.UseStatusCodePages();

    if (app.Configuration.GetValue("Swagger:Enabled", app.Environment.IsDevelopment()))
    {
        app.UseSwagger();
        app.UseSwaggerUI(o =>
        {
            o.SwaggerEndpoint("/swagger/v1/swagger.json", "Contix API v1");
            o.DocumentTitle = "Contix API";
        });
    }

    if (!app.Environment.IsDevelopment()) app.UseHsts();

    app.UseCors();
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();
    app.MapHealthChecks("/health").AllowAnonymous();

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "A aplicação foi encerrada inesperadamente");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>Exposto para testes de integração (WebApplicationFactory).</summary>
public partial class Program;

internal static class DatabaseStartup
{
    public static async Task InicializarBancoAsync(this WebApplication app)
    {
        var migrar = app.Configuration.GetValue("Database:MigrateOnStartup", false);
        var seed = app.Configuration.GetValue("Database:SeedDemoData", false);
        if (!migrar && !seed) return;

        await using var scope = app.Services.CreateAsyncScope();
        var initializer = scope.ServiceProvider.GetRequiredService<DbInitializer>();
        if (migrar) await initializer.MigrateAsync();
        if (seed) await initializer.SeedDemoAsync();
    }
}
