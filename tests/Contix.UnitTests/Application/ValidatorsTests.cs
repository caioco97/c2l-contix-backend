using Contix.Application.Features.Auth;
using Contix.Application.Features.Documentos;
using Contix.Application.Features.Empresas;
using Contix.Application.Features.Usuarios;
using Contix.Domain.Enums;
using Microsoft.Extensions.Options;

namespace Contix.UnitTests.Application;

public class ValidatorsTests
{
    [Fact]
    public void Registro_exige_cnpj_valido_e_senha_forte()
    {
        var result = new RegistrarEscritorioRequestValidator().Validate(new RegistrarEscritorioRequest(
            "Escritório", null, "123", null, null, null, "Ana", "ana@x.dev", "fraca"));

        Assert.Contains(result.Errors, e => e.PropertyName == "Cnpj");
        Assert.Contains(result.Errors, e => e.PropertyName == "Senha");
    }

    [Fact]
    public void Registro_valido()
    {
        var result = new RegistrarEscritorioRequestValidator().Validate(new RegistrarEscritorioRequest(
            "Escritório", null, "11.222.333/0001-81", null, null, null, "Ana", "ana@x.dev", "Senha@123"));
        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    [Fact]
    public void Empresa_aceita_cpf_ou_cnpj()
    {
        var v = new SalvarEmpresaRequestValidator();
        SalvarEmpresaRequest Req(string doc) => new("Empresa", null, null, doc, null, null,
            RegimeTributario.SimplesNacional, SituacaoEmpresa.Ativa, null, null, null);

        Assert.True(v.Validate(Req("52998224725")).IsValid);
        Assert.True(v.Validate(Req("11222333000181")).IsValid);
        Assert.False(v.Validate(Req("11222333000182")).IsValid);
    }

    [Fact]
    public void Usuario_cliente_exige_empresa()
    {
        var result = new CriarUsuarioRequestValidator().Validate(
            new CriarUsuarioRequest("Cli", "cli@x.dev", PerfilUsuario.Cliente, "Senha@123", []));
        Assert.Contains(result.Errors, e => e.PropertyName == "EmpresaIds");
    }

    [Fact]
    public void Upload_rejeita_extensao_e_tamanho()
    {
        var options = Options.Create(new DocumentoOptions { TamanhoMaximoMb = 1, ExtensoesPermitidas = [".pdf"] });
        var v = new UploadDocumentosCommandValidator(options);
        var cmd = new UploadDocumentosCommand(Guid.NewGuid(), null, null, null,
        [
            new ArquivoUpload("virus.exe", "application/octet-stream", 10, () => Stream.Null),
            new ArquivoUpload("grande.pdf", "application/pdf", 2 * 1024 * 1024, () => Stream.Null)
        ]);

        var result = v.Validate(cmd);

        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains(".exe"));
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("1 MB"));
    }
}
