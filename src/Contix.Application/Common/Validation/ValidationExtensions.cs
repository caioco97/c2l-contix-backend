using Contix.Application.Common.Models;
using Contix.Domain.ValueObjects;
using FluentValidation;

namespace Contix.Application.Common.Validation;

public static class ValidationExtensions
{
    public static IRuleBuilderOptions<T, string?> Cnpj<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(DocumentoFiscal.CnpjValido).WithMessage("CNPJ inválido.");

    public static IRuleBuilderOptions<T, string?> CpfOuCnpj<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(DocumentoFiscal.CpfOuCnpjValido).WithMessage("CPF/CNPJ inválido.");

    public static IRuleBuilderOptions<T, string> Senha<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty()
            .MinimumLength(8).WithMessage("A senha deve ter ao menos 8 caracteres.")
            .Matches("[A-Z]").WithMessage("A senha deve conter ao menos uma letra maiúscula.")
            .Matches("[a-z]").WithMessage("A senha deve conter ao menos uma letra minúscula.")
            .Matches("[0-9]").WithMessage("A senha deve conter ao menos um número.");
}

public sealed class EnderecoValidator : AbstractValidator<EnderecoDto>
{
    public EnderecoValidator()
    {
        RuleFor(x => x.Cep).Matches(@"^\d{5}-?\d{3}$").When(x => !string.IsNullOrWhiteSpace(x.Cep))
            .WithMessage("CEP inválido.");
        RuleFor(x => x.Uf).Length(2).When(x => !string.IsNullOrWhiteSpace(x.Uf));
        RuleFor(x => x.Logradouro).MaximumLength(200);
        RuleFor(x => x.Numero).MaximumLength(20);
        RuleFor(x => x.Complemento).MaximumLength(100);
        RuleFor(x => x.Bairro).MaximumLength(100);
        RuleFor(x => x.Cidade).MaximumLength(100);
    }
}
