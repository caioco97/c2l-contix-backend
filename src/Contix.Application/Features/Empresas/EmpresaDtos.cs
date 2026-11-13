using Contix.Application.Common.Models;
using Contix.Application.Common.Validation;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using FluentValidation;

namespace Contix.Application.Features.Empresas;

public sealed record EmpresaDto(
    Guid Id, string RazaoSocial, string? NomeFantasia, string? Apelido, string Documento,
    string? InscricaoEstadual, string? InscricaoMunicipal, RegimeTributario RegimeTributario,
    SituacaoEmpresa Situacao, string? Email, string? Telefone, EnderecoDto? Endereco,
    DateTime CreatedAt, DateTime? UpdatedAt, IdentidadeVisualDto? IdentidadeVisual = null)
{
    public static EmpresaDto From(Empresa e) => new(
        e.Id, e.RazaoSocial, e.NomeFantasia, e.Apelido, e.Documento, e.InscricaoEstadual,
        e.InscricaoMunicipal, e.RegimeTributario, e.Situacao, e.Email, e.Telefone,
        EnderecoDto.From(e.Endereco), e.CreatedAt, e.UpdatedAt, IdentidadeVisualDto.From(e));
}

/// <param name="Modulos">Para o usuário do cliente: módulos do portal liberados nesta empresa. Nulo para o escritório.</param>
public sealed record EmpresaResumoDto(
    Guid Id, string RazaoSocial, string? NomeFantasia, string? Apelido, string Documento,
    RegimeTributario RegimeTributario, SituacaoEmpresa Situacao, IReadOnlyList<ModuloPortal>? Modulos = null,
    IdentidadeVisualDto? IdentidadeVisual = null);

public sealed record EmpresaFiltro : PageQuery
{
    /// <summary>Busca por razão social, nome fantasia, apelido ou CNPJ/CPF.</summary>
    public string? Busca { get; init; }
    public RegimeTributario? Regime { get; init; }
    public SituacaoEmpresa? Situacao { get; init; }
}

public record SalvarEmpresaRequest(
    string RazaoSocial, string? NomeFantasia, string? Apelido, string Documento,
    string? InscricaoEstadual, string? InscricaoMunicipal, RegimeTributario RegimeTributario,
    SituacaoEmpresa Situacao, string? Email, string? Telefone, EnderecoDto? Endereco);

public sealed class SalvarEmpresaRequestValidator : AbstractValidator<SalvarEmpresaRequest>
{
    public SalvarEmpresaRequestValidator()
    {
        RuleFor(x => x.RazaoSocial).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NomeFantasia).MaximumLength(200);
        RuleFor(x => x.Apelido).MaximumLength(60);
        RuleFor(x => x.Documento).NotEmpty().CpfOuCnpj();
        RuleFor(x => x.InscricaoEstadual).MaximumLength(30).InscricaoEstadual();
        RuleFor(x => x.InscricaoMunicipal).MaximumLength(30).InscricaoMunicipal();
        RuleFor(x => x.RegimeTributario).IsInEnum();
        RuleFor(x => x.Situacao).IsInEnum();
        RuleFor(x => x.Email).EmailAddress().MaximumLength(200).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Telefone).MaximumLength(30).Telefone();
        RuleFor(x => x.Endereco!).SetValidator(new EnderecoValidator()).When(x => x.Endereco is not null);
    }
}
