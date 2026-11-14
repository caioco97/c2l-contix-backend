using Contix.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Contix.Infrastructure.Persistence.Configurations;

public sealed class DepartamentoEscritorioConfiguration : IEntityTypeConfiguration<DepartamentoEscritorio>
{
    public void Configure(EntityTypeBuilder<DepartamentoEscritorio> b)
    {
        b.ToTable("departamentos");
        b.Property(x => x.Codigo).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Nome).HasMaxLength(80).IsRequired();
        b.HasOne<Escritorio>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.TenantId, x.Codigo }).IsUnique().HasFilter("is_deleted = false");
    }
}

public sealed class ResponsavelEmpresaConfiguration : IEntityTypeConfiguration<ResponsavelEmpresa>
{
    public void Configure(EntityTypeBuilder<ResponsavelEmpresa> b)
    {
        b.ToTable("empresa_responsaveis");
        b.Property(x => x.Departamento).HasConversion<string>().HasMaxLength(30);
        b.HasOne(x => x.Empresa).WithMany().HasForeignKey(x => x.EmpresaId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Usuario).WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Escritorio>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        // Um responsável por empresa e departamento; a busca na abertura de solicitações usa este índice.
        b.HasIndex(x => new { x.EmpresaId, x.Departamento }).IsUnique().HasFilter("is_deleted = false");
        b.HasIndex(x => new { x.TenantId, x.UsuarioId });
    }
}

public sealed class ObrigacaoRecorrenteConfiguration : IEntityTypeConfiguration<ObrigacaoRecorrente>
{
    public void Configure(EntityTypeBuilder<ObrigacaoRecorrente> b)
    {
        b.ToTable("obrigacoes_recorrentes");
        b.Property(x => x.Titulo).HasMaxLength(200).IsRequired();
        b.Property(x => x.Descricao).HasMaxLength(5000);
        b.Property(x => x.Departamento).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Periodicidade).HasConversion<string>().HasMaxLength(20);
        b.PrimitiveCollection(x => x.Regimes).ElementType(e => e.HasConversion<string>());
        b.HasOne(x => x.ResponsavelPadrao).WithMany().HasForeignKey(x => x.ResponsavelPadraoId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<Escritorio>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.TenantId, x.Ativa });
    }
}
