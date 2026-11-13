using Contix.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Contix.Infrastructure.Persistence.Configurations;

public sealed class EmpregadoConfiguration : IEntityTypeConfiguration<Empregado>
{
    public void Configure(EntityTypeBuilder<Empregado> b)
    {
        b.ToTable("empregados");
        b.Property(x => x.Nome).HasMaxLength(150).IsRequired();
        b.Property(x => x.Cpf).HasMaxLength(11);
        b.Property(x => x.Matricula).HasMaxLength(30);
        b.Property(x => x.Cargo).HasMaxLength(100);
        b.Property(x => x.Vinculo).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Situacao).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Email).HasMaxLength(200);
        b.Property(x => x.Telefone).HasMaxLength(30);
        b.HasOne(x => x.Empresa).WithMany().HasForeignKey(x => x.EmpresaId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Escritorio>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.TenantId, x.EmpresaId, x.Nome });
        // CPF único por empresa entre empregados não excluídos.
        b.HasIndex(x => new { x.EmpresaId, x.Cpf }).IsUnique().HasFilter("cpf IS NOT NULL AND is_deleted = false");
    }
}
