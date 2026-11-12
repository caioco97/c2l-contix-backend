using Contix.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Contix.Infrastructure.Persistence.Configurations;

internal static class EnderecoMapping
{
    public static void Map<T>(OwnedNavigationBuilder<T, Endereco> e) where T : class
    {
        e.Property(x => x.Cep).HasMaxLength(9);
        e.Property(x => x.Logradouro).HasMaxLength(200);
        e.Property(x => x.Numero).HasMaxLength(20);
        e.Property(x => x.Complemento).HasMaxLength(100);
        e.Property(x => x.Bairro).HasMaxLength(100);
        e.Property(x => x.Cidade).HasMaxLength(100);
        e.Property(x => x.Uf).HasMaxLength(2);
    }
}

public sealed class EscritorioConfiguration : IEntityTypeConfiguration<Escritorio>
{
    public void Configure(EntityTypeBuilder<Escritorio> b)
    {
        b.ToTable("escritorios");
        b.Property(x => x.RazaoSocial).HasMaxLength(200).IsRequired();
        b.Property(x => x.NomeFantasia).HasMaxLength(200);
        b.Property(x => x.Cnpj).HasMaxLength(14).IsRequired();
        b.HasIndex(x => x.Cnpj).IsUnique();
        b.Property(x => x.Crc).HasMaxLength(30);
        b.Property(x => x.Email).HasMaxLength(200);
        b.Property(x => x.Telefone).HasMaxLength(30);
        b.Property(x => x.LogoUrl).HasMaxLength(500);
        b.OwnsOne(x => x.Endereco, EnderecoMapping.Map);
    }
}

public sealed class UsuarioConfiguration : IEntityTypeConfiguration<Usuario>
{
    public void Configure(EntityTypeBuilder<Usuario> b)
    {
        b.ToTable("usuarios");
        b.Property(x => x.Nome).HasMaxLength(150).IsRequired();
        b.Property(x => x.Email).HasMaxLength(200).IsRequired();
        // E-mail único globalmente: o login acontece antes de se conhecer o tenant.
        b.HasIndex(x => x.Email).IsUnique();
        b.HasIndex(x => x.TenantId);
        b.Property(x => x.SenhaHash).HasMaxLength(500).IsRequired();
        b.Property(x => x.Perfil).HasConversion<string>().HasMaxLength(20);
        b.HasOne(x => x.Escritorio).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class UsuarioEmpresaConfiguration : IEntityTypeConfiguration<UsuarioEmpresa>
{
    public void Configure(EntityTypeBuilder<UsuarioEmpresa> b)
    {
        b.ToTable("usuario_empresas");
        b.HasKey(x => new { x.UsuarioId, x.EmpresaId });
        b.HasOne(x => x.Usuario).WithMany(x => x.Empresas).HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Empresa).WithMany(x => x.Usuarios).HasForeignKey(x => x.EmpresaId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.EmpresaId);
    }
}

public sealed class EmpresaConfiguration : IEntityTypeConfiguration<Empresa>
{
    public void Configure(EntityTypeBuilder<Empresa> b)
    {
        b.ToTable("empresas");
        b.Property(x => x.RazaoSocial).HasMaxLength(200).IsRequired();
        b.Property(x => x.NomeFantasia).HasMaxLength(200);
        b.Property(x => x.Apelido).HasMaxLength(60);
        b.Property(x => x.Documento).HasMaxLength(14).IsRequired();
        b.Property(x => x.InscricaoEstadual).HasMaxLength(30);
        b.Property(x => x.InscricaoMunicipal).HasMaxLength(30);
        b.Property(x => x.RegimeTributario).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Situacao).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Email).HasMaxLength(200);
        b.Property(x => x.Telefone).HasMaxLength(30);
        b.OwnsOne(x => x.Endereco, EnderecoMapping.Map);
        // Único por tenant entre empresas não excluídas.
        b.HasIndex(x => new { x.TenantId, x.Documento }).IsUnique().HasFilter("is_deleted = false");
        b.HasOne<Escritorio>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PastaConfiguration : IEntityTypeConfiguration<Pasta>
{
    public void Configure(EntityTypeBuilder<Pasta> b)
    {
        b.ToTable("pastas");
        b.Property(x => x.Nome).HasMaxLength(120).IsRequired();
        b.HasOne(x => x.Empresa).WithMany(x => x.Pastas).HasForeignKey(x => x.EmpresaId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.PastaPai).WithMany(x => x.Subpastas).HasForeignKey(x => x.PastaPaiId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.TenantId, x.EmpresaId });
    }
}

public sealed class DocumentoConfiguration : IEntityTypeConfiguration<Documento>
{
    public void Configure(EntityTypeBuilder<Documento> b)
    {
        b.ToTable("documentos");
        b.Property(x => x.Nome).HasMaxLength(255).IsRequired();
        b.Property(x => x.NomeArquivoOriginal).HasMaxLength(255).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(150).IsRequired();
        b.Property(x => x.StorageKey).HasMaxLength(500).IsRequired();
        b.Property(x => x.Comentario).HasMaxLength(1000);
        b.HasOne(x => x.Empresa).WithMany().HasForeignKey(x => x.EmpresaId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Pasta).WithMany(x => x.Documentos).HasForeignKey(x => x.PastaId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.AdicionadoPor).WithMany().HasForeignKey(x => x.AdicionadoPorId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.TenantId, x.EmpresaId, x.PastaId });
        b.HasIndex(x => new { x.EmpresaId, x.DataVencimento });
    }
}

public sealed class DocumentoLeituraConfiguration : IEntityTypeConfiguration<DocumentoLeitura>
{
    public void Configure(EntityTypeBuilder<DocumentoLeitura> b)
    {
        b.ToTable("documento_leituras");
        b.HasKey(x => new { x.DocumentoId, x.UsuarioId });
        b.HasOne(x => x.Documento).WithMany(x => x.Leituras).HasForeignKey(x => x.DocumentoId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Usuario).WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("refresh_tokens");
        b.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => x.Familia);
        b.Property(x => x.SubstituidoPorHash).HasMaxLength(128);
        b.Property(x => x.MotivoRevogacao).HasMaxLength(200);
        b.Property(x => x.CriadoPorIp).HasMaxLength(64);
        b.HasOne(x => x.Usuario).WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Cascade);
    }
}
