using System.Text;
using Contix.Application.Common.Abstractions;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Contix.Infrastructure.Persistence.Seed;

/// <summary>Aplica migrations e, opcionalmente, popula dados de demonstração (somente desenvolvimento).</summary>
public sealed class DbInitializer(
    ContixDbContext context,
    IPasswordHasher passwordHasher,
    IFileStorage storage,
    IDateTimeProvider clock,
    ILogger<DbInitializer> logger)
{
    public const string SenhaDemo = "Contix@123";

    public async Task MigrateAsync(CancellationToken ct = default)
    {
        if (context.Database.IsRelational())
            await context.Database.MigrateAsync(ct);
        else
            await context.Database.EnsureCreatedAsync(ct);
    }

    public async Task SeedDemoAsync(CancellationToken ct = default)
    {
        if (await context.Escritorios.AnyAsync(ct)) return;

        var agora = clock.UtcNow;
        var escritorio = new Escritorio
        {
            RazaoSocial = "Contix Demo Serviços Contábeis Ltda",
            NomeFantasia = "Contix Demo Contabilidade",
            Cnpj = "11222333000181",
            Crc = "SP-000000/O-0",
            Email = "contato@contix.dev",
            Telefone = "(11) 4000-0000"
        };

        Usuario NovoUsuario(string nome, string email, PerfilUsuario perfil) => new()
        {
            TenantId = escritorio.Id, Nome = nome, Email = email, Perfil = perfil,
            SenhaHash = passwordHasher.Hash(SenhaDemo)
        };

        var admin = NovoUsuario("Administrador Demo", "admin@contix.dev", PerfilUsuario.Administrador);
        var colaborador = NovoUsuario("Colaborador Demo", "colaborador@contix.dev", PerfilUsuario.Colaborador);
        var cliente = NovoUsuario("Cliente Demo", "cliente@contix.dev", PerfilUsuario.Cliente);

        var empresa = new Empresa
        {
            TenantId = escritorio.Id,
            RazaoSocial = "Empresa Demonstração Ltda",
            NomeFantasia = "Empresa Demo",
            Apelido = "DEMO",
            Documento = "44555666000181",
            RegimeTributario = RegimeTributario.SimplesNacional,
            Email = "financeiro@empresademo.dev"
        };
        var pastas = empresa.CriarPastasPadrao();
        var pessoal = pastas.Single(p => p.Nome == "Pessoal");
        pastas.Single(p => p.Nome == "Financeiro").PermiteUploadCliente = true;

        var darf = new Pasta
        {
            TenantId = escritorio.Id, EmpresaId = empresa.Id, PastaPaiId = pessoal.Id,
            Nome = "DARF DCTFWeb", Ordem = 100
        };

        cliente.Empresas.Add(new UsuarioEmpresa { UsuarioId = cliente.Id, EmpresaId = empresa.Id, VinculadoEm = agora });

        var competencia = new DateOnly(agora.Year, agora.Month, 1).AddMonths(-1);
        var documento = new Documento
        {
            TenantId = escritorio.Id,
            EmpresaId = empresa.Id,
            PastaId = darf.Id,
            Nome = $"Guia DARF DCTFWeb {competencia:MM_yyyy}",
            NomeArquivoOriginal = $"Guia DARF DCTFWeb {competencia:MM_yyyy}.pdf",
            ContentType = "application/pdf",
            Competencia = competencia,
            DataVencimento = new DateOnly(agora.Year, agora.Month, Math.Min(20, DateTime.DaysInMonth(agora.Year, agora.Month))),
            PublicadoEm = agora,
            AdicionadoPorId = colaborador.Id,
            Comentario = "Documento de exemplo gerado pelo seed."
        };
        var pdf = Encoding.ASCII.GetBytes(PdfExemplo);
        documento.TamanhoBytes = pdf.Length;
        documento.StorageKey = $"{escritorio.Id:N}/{empresa.Id:N}/{documento.Id:N}.pdf";
        await using (var ms = new MemoryStream(pdf))
            await storage.SaveAsync(documento.StorageKey, ms, documento.ContentType, ct);

        context.Escritorios.Add(escritorio);
        context.Usuarios.AddRange(admin, colaborador, cliente);
        context.Empresas.Add(empresa);
        context.Pastas.AddRange(pastas);
        context.Pastas.Add(darf);
        context.Documentos.Add(documento);
        context.Solicitacoes.AddRange(SolicitacoesDemo(escritorio, empresa, cliente, colaborador, agora));
        await context.SaveChangesAsync(ct);

        logger.LogInformation(
            "Dados de demonstração criados. Usuários: admin@contix.dev, colaborador@contix.dev, cliente@contix.dev (senha {Senha})",
            SenhaDemo);
    }

    private static IEnumerable<Solicitacao> SolicitacoesDemo(
        Escritorio escritorio, Empresa empresa, Usuario cliente, Usuario colaborador, DateTime agora)
    {
        var hoje = DateOnly.FromDateTime(agora);

        var geral = new Solicitacao
        {
            TenantId = escritorio.Id, EmpresaId = empresa.Id, Numero = 1, Tipo = TipoSolicitacao.Geral,
            Departamento = Departamento.Fiscal, Assunto = "Dúvida sobre a apuração do Simples Nacional",
            Descricao = "Gostaríamos de entender por que o valor do DAS aumentou neste mês.", SolicitanteId = cliente.Id
        };
        geral.RegistrarAbertura(agora.AddDays(-2), hoje.AddDays(3));
        geral.Atribuir(colaborador.Id, colaborador.Nome, hoje.AddDays(3), colaborador.Id, agora.AddDays(-1));
        geral.AlterarStatus(StatusSolicitacao.EmAnalise, colaborador.Id, false, "Estamos verificando o faturamento acumulado.", agora.AddDays(-1));

        var ferias = new Solicitacao
        {
            TenantId = escritorio.Id, EmpresaId = empresa.Id, Numero = 2, Tipo = TipoSolicitacao.AvisoFerias,
            Departamento = Departamento.Pessoal, Assunto = "Aviso Prévio de Férias - Maria Souza", SolicitanteId = cliente.Id,
            Dados = $$"""{"empregado":"Maria Souza","periodoAquisitivoInicio":"{{hoje.AddYears(-1):yyyy-MM-dd}}","periodoAquisitivoFim":"{{hoje.AddDays(-1):yyyy-MM-dd}}","dataInicio":"{{hoje.AddDays(35):yyyy-MM-dd}}","dias":30,"abonoPecuniario":false,"adiantamento13":true}"""
        };
        ferias.RegistrarAbertura(agora.AddHours(-5), hoje.AddDays(5));
        ferias.AlterarStatus(StatusSolicitacao.AguardandoCliente, colaborador.Id, false,
            "Pode confirmar se a Maria vai vender 10 dias (abono pecuniário)?", agora.AddHours(-1));

        escritorio.UltimoNumeroSolicitacao = 2;
        return [geral, ferias];
    }

    private const string PdfExemplo =
        "%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n" +
        "3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]/Contents 4 0 R/Resources<</Font<</F1 5 0 R>>>>>>endobj\n" +
        "4 0 obj<</Length 58>>stream\nBT /F1 24 Tf 72 760 Td (Contix - documento de exemplo) Tj ET\nendstream endobj\n" +
        "5 0 obj<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n";
}
