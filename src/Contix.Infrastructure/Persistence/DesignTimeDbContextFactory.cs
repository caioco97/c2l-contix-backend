using Contix.Application.Common.Abstractions;
using Contix.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Contix.Infrastructure.Persistence;

/// <summary>Usado apenas pelo `dotnet ef` para gerar migrations.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ContixDbContext>
{
    public ContixDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
                               ?? "Host=localhost;Port=5432;Database=contix;Username=contix;Password=contix";
        var options = new DbContextOptionsBuilder<ContixDbContext>()
            .UseNpgsql(connectionString, o => o.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new ContixDbContext(options, new DesignTimeUser());
    }

    private sealed class DesignTimeUser : ICurrentUser
    {
        public bool IsAuthenticated => false;
        public Guid? UserId => null;
        public Guid? TenantId => null;
        public PerfilUsuario? Perfil => null;
        public string? IpAddress => null;
    }
}
