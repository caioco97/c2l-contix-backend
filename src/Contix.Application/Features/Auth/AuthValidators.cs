using Contix.Application.Common.Validation;
using FluentValidation;

namespace Contix.Application.Features.Auth;

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Senha).NotEmpty();
    }
}

public sealed class RefreshTokenRequestValidator : AbstractValidator<RefreshTokenRequest>
{
    public RefreshTokenRequestValidator() => RuleFor(x => x.RefreshToken).NotEmpty();
}

public sealed class RegistrarEscritorioRequestValidator : AbstractValidator<RegistrarEscritorioRequest>
{
    public RegistrarEscritorioRequestValidator()
    {
        RuleFor(x => x.RazaoSocial).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NomeFantasia).MaximumLength(200);
        RuleFor(x => x.Cnpj).NotEmpty().Cnpj();
        RuleFor(x => x.Crc).MaximumLength(30);
        RuleFor(x => x.Telefone).MaximumLength(30);
        RuleFor(x => x.Endereco!).SetValidator(new EnderecoValidator()).When(x => x.Endereco is not null);
        RuleFor(x => x.NomeAdministrador).NotEmpty().MaximumLength(150);
        RuleFor(x => x.EmailAdministrador).NotEmpty().EmailAddress().MaximumLength(200);
        RuleFor(x => x.Senha).Senha();
    }
}

public sealed class EsqueciSenhaRequestValidator : AbstractValidator<EsqueciSenhaRequest>
{
    public EsqueciSenhaRequestValidator() => RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(200);
}

public sealed class RedefinirSenhaRequestValidator : AbstractValidator<RedefinirSenhaRequest>
{
    public RedefinirSenhaRequestValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NovaSenha).Senha();
    }
}
