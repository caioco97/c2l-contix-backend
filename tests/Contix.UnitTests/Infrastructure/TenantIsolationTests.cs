using Contix.Application.Common.Abstractions;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using Contix.Infrastructure.Persistence;
using Contix.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Contix.UnitTests.Infrastructure;

public class TenantIsolationTests
{
    private static (ContixDbContext Db, ICurrentUser User) CriarContexto(string nomeBanco)
    {
        var user = Substitute.For<ICurrentUser>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        var options = new DbContextOptionsBuilder<ContixDbContext>()
            .UseInMemoryDatabase(nomeBanco)
            .AddInterceptors(new AuditInterceptor(user, clock))
            .Options;
        return (new ContixDbContext(options, user), user);
    }

    [Fact]
    public async Task Consultas_retornam_apenas_dados_do_tenant_atual()
    {
        var (db, user) = CriarContexto(Guid.NewGuid().ToString());
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        db.Empresas.AddRange(
            new Empresa { TenantId = tenantA, RazaoSocial = "A", Documento = "1", RegimeTributario = RegimeTributario.MEI },
            new Empresa { TenantId = tenantB, RazaoSocial = "B", Documento = "2", RegimeTributario = RegimeTributario.MEI });
        await db.SaveChangesAsync();

        user.TenantId.Returns(tenantA);
        Assert.Equal(["A"], await db.Empresas.Select(e => e.RazaoSocial).ToListAsync());

        user.TenantId.Returns(tenantB);
        Assert.Equal(["B"], await db.Empresas.Select(e => e.RazaoSocial).ToListAsync());

        user.TenantId.Returns((Guid?)null);
        Assert.Empty(await db.Empresas.ToListAsync());
    }

    [Fact]
    public async Task Exclusao_e_logica_e_some_das_consultas()
    {
        var (db, user) = CriarContexto(Guid.NewGuid().ToString());
        var tenant = Guid.NewGuid();
        user.TenantId.Returns(tenant);

        var empresa = new Empresa { RazaoSocial = "A", Documento = "1", RegimeTributario = RegimeTributario.MEI };
        db.Empresas.Add(empresa);
        await db.SaveChangesAsync();
        Assert.Equal(tenant, empresa.TenantId); // atribuído pelo interceptor

        db.Empresas.Remove(empresa);
        await db.SaveChangesAsync();

        Assert.Empty(await db.Empresas.ToListAsync());
        var excluida = await db.Empresas.IgnoreQueryFilters().SingleAsync();
        Assert.True(excluida.IsDeleted);
        Assert.NotNull(excluida.DeletedAt);
    }

    [Fact]
    public async Task Nova_entidade_sem_tenant_falha()
    {
        var (db, _) = CriarContexto(Guid.NewGuid().ToString());
        db.Empresas.Add(new Empresa { RazaoSocial = "A", Documento = "1" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
}
