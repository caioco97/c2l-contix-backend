using Contix.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Contix.Infrastructure.Persistence.Configurations;

public sealed class NotaFiscalConfiguration : IEntityTypeConfiguration<NotaFiscal>
{
    public void Configure(EntityTypeBuilder<NotaFiscal> b)
    {
        b.ToTable("notas_fiscais");
        b.Property(x => x.ChaveAcesso).HasMaxLength(44).IsFixedLength().IsRequired();
        b.Property(x => x.EmitenteDocumento).HasMaxLength(14).IsRequired();
        b.Property(x => x.EmitenteNome).HasMaxLength(200).IsRequired();
        b.Property(x => x.EmitenteIe).HasMaxLength(20);
        b.Property(x => x.DestinatarioDocumento).HasMaxLength(14).IsRequired();
        b.Property(x => x.ValorTotal).HasPrecision(15, 2);
        b.Property(x => x.Protocolo).HasMaxLength(20);
        b.Property(x => x.Situacao).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Origem).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.XmlStorageKey).HasMaxLength(500);
        b.Property(x => x.Manifestacao).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.JustificativaManifestacao).HasMaxLength(NotaFiscal.JustificativaMaxima);
        b.Property(x => x.ProtocoloManifestacao).HasMaxLength(20);
        b.HasOne(x => x.Empresa).WithMany().HasForeignKey(x => x.EmpresaId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ManifestadaPor).WithMany().HasForeignKey(x => x.ManifestadaPorId).OnDelete(DeleteBehavior.SetNull);
        // Uma nota por empresa: a importação e a SEFAZ não duplicam.
        b.HasIndex(x => new { x.EmpresaId, x.ChaveAcesso }).IsUnique().HasFilter("is_deleted = false");
        b.HasIndex(x => new { x.TenantId, x.EmpresaId, x.DataEmissao });
    }
}

public sealed class CertificadoDigitalConfiguration : IEntityTypeConfiguration<CertificadoDigital>
{
    public void Configure(EntityTypeBuilder<CertificadoDigital> b)
    {
        b.ToTable("certificados_digitais");
        b.Property(x => x.NomeArquivo).HasMaxLength(255).IsRequired();
        b.Property(x => x.Titular).HasMaxLength(300).IsRequired();
        b.Property(x => x.Documento).HasMaxLength(14);
        b.Property(x => x.Emissor).HasMaxLength(300).IsRequired();
        b.Property(x => x.NumeroSerie).HasMaxLength(100).IsRequired();
        b.Property(x => x.Thumbprint).HasMaxLength(64).IsRequired();
        b.Property(x => x.ArquivoCriptografado).IsRequired();
        b.Property(x => x.SenhaCriptografada).IsRequired();
        b.HasOne(x => x.Empresa).WithMany().HasForeignKey(x => x.EmpresaId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.EnviadoPor).WithMany().HasForeignKey(x => x.EnviadoPorId).OnDelete(DeleteBehavior.SetNull);
        // Um certificado em uso por empresa (o substituído fica excluído logicamente).
        b.HasIndex(x => x.EmpresaId).IsUnique().HasFilter("is_deleted = false");
    }
}

public sealed class SincronizacaoNFeConfiguration : IEntityTypeConfiguration<SincronizacaoNFe>
{
    public void Configure(EntityTypeBuilder<SincronizacaoNFe> b)
    {
        b.ToTable("sincronizacoes_nfe");
        b.Property(x => x.UltimoStatus).HasMaxLength(10);
        b.Property(x => x.UltimaMensagem).HasMaxLength(500);
        b.HasOne(x => x.Empresa).WithMany().HasForeignKey(x => x.EmpresaId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.EmpresaId).IsUnique().HasFilter("is_deleted = false");
        // Fila do job.
        b.HasIndex(x => new { x.Ativa, x.ProximaConsultaEm });
    }
}
