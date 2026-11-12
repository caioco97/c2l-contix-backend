using Contix.Application.Common.Models;
using Contix.Application.Common.Validation;
using Contix.Domain.Entities;
using FluentValidation;

namespace Contix.Application.Features.Escritorios;

public sealed record EscritorioDto(
    Guid Id, string RazaoSocial, string? NomeFantasia, string Cnpj, string? Crc,
    string? Email, string? Telefone, EnderecoDto? Endereco, string? LogoUrl)
{
    public static EscritorioDto From(Escritorio e) => new(
        e.Id, e.RazaoSocial, e.NomeFantasia, e.Cnpj, e.Crc, e.Email, e.Telefone,
        EnderecoDto.From(e.Endereco), e.LogoUrl);
}

public sealed record AtualizarEscritorioRequest(
    string RazaoSocial, string? NomeFantasia, string? Crc, string? Email,
    string? Telefone, EnderecoDto? Endereco, string? LogoUrl);

public sealed class AtualizarEscritorioRequestValidator : AbstractValidator<AtualizarEscritorioRequest>
{
    public AtualizarEscritorioRequestValidator()
    {
        RuleFor(x => x.RazaoSocial).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NomeFantasia).MaximumLength(200);
        RuleFor(x => x.Crc).MaximumLength(30);
        RuleFor(x => x.Email).EmailAddress().MaximumLength(200).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Telefone).MaximumLength(30);
        RuleFor(x => x.LogoUrl).MaximumLength(500);
        RuleFor(x => x.Endereco!).SetValidator(new EnderecoValidator()).When(x => x.Endereco is not null);
    }
}
