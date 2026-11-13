using Contix.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Contix.Infrastructure.Persistence.Configurations;

public sealed class NoticiaConfiguration : IEntityTypeConfiguration<Noticia>
{
    public void Configure(EntityTypeBuilder<Noticia> b)
    {
        b.ToTable("noticias");
        b.Property(x => x.Titulo).HasMaxLength(200).IsRequired();
        b.Property(x => x.Resumo).HasMaxLength(500);
        b.Property(x => x.Conteudo).HasMaxLength(20_000).IsRequired();
        b.Property(x => x.Publico).HasConversion<string>().HasMaxLength(30);
        // text[] com os nomes dos regimes, igual à coluna empresas.regime_tributario (comparável direto no SQL).
        b.PrimitiveCollection(x => x.Regimes).ElementType(e => e.HasConversion<string>());
        b.Property(x => x.ImagemStorageKey).HasMaxLength(500);
        b.Property(x => x.ImagemContentType).HasMaxLength(150);
        b.HasOne(x => x.Autor).WithMany().HasForeignKey(x => x.AutorId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Empresas).WithOne().HasForeignKey(x => x.NoticiaId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Anexos).WithOne().HasForeignKey(x => x.NoticiaId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Leituras).WithOne().HasForeignKey(x => x.NoticiaId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Escritorio>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.TenantId, x.PublicarEm });
    }
}

public sealed class NoticiaEmpresaConfiguration : IEntityTypeConfiguration<NoticiaEmpresa>
{
    public void Configure(EntityTypeBuilder<NoticiaEmpresa> b)
    {
        b.ToTable("noticia_empresas");
        b.HasKey(x => new { x.NoticiaId, x.EmpresaId });
        b.HasOne(x => x.Empresa).WithMany().HasForeignKey(x => x.EmpresaId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.EmpresaId);
    }
}

public sealed class NoticiaLeituraConfiguration : IEntityTypeConfiguration<NoticiaLeitura>
{
    public void Configure(EntityTypeBuilder<NoticiaLeitura> b)
    {
        b.ToTable("noticia_leituras");
        b.HasKey(x => new { x.NoticiaId, x.EmpresaId, x.UsuarioId });
        b.HasOne<Empresa>().WithMany().HasForeignKey(x => x.EmpresaId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Usuario).WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class NoticiaAnexoConfiguration : IEntityTypeConfiguration<NoticiaAnexo>
{
    public void Configure(EntityTypeBuilder<NoticiaAnexo> b)
    {
        b.ToTable("noticia_anexos");
        // Id gerado no domínio: sem isso o EF trataria anexos novos de uma notícia existente como já gravados.
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.NomeArquivo).HasMaxLength(255).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(150).IsRequired();
        b.Property(x => x.StorageKey).HasMaxLength(500).IsRequired();
        b.HasIndex(x => x.NoticiaId);
    }
}

public sealed class CertidaoConfiguration : IEntityTypeConfiguration<Certidao>
{
    public void Configure(EntityTypeBuilder<Certidao> b)
    {
        b.ToTable("certidoes");
        b.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Situacao).HasConversion<string>().HasMaxLength(40);
        b.Property(x => x.Numero).HasMaxLength(100);
        b.Property(x => x.Observacao).HasMaxLength(1000);
        b.Property(x => x.NomeArquivo).HasMaxLength(255);
        b.Property(x => x.ContentType).HasMaxLength(150);
        b.Property(x => x.StorageKey).HasMaxLength(500);
        b.Ignore(x => x.PossuiArquivo);
        b.Property(x => x.AvisoVencimento30Em).HasColumnName("aviso_vencimento_30_em");
        b.Property(x => x.AvisoVencimento15Em).HasColumnName("aviso_vencimento_15_em");
        b.HasOne(x => x.Empresa).WithMany().HasForeignKey(x => x.EmpresaId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CadastradoPor).WithMany().HasForeignKey(x => x.CadastradoPorId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.TenantId, x.EmpresaId, x.Tipo, x.Validade });
        b.HasIndex(x => new { x.TenantId, x.Validade });
    }
}
