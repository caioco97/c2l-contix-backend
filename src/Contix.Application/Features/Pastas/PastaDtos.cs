using FluentValidation;

namespace Contix.Application.Features.Pastas;

public sealed record PastaDto(
    Guid Id, string Nome, Guid? PastaPaiId, int Ordem, bool PermiteUploadCliente,
    int DocumentosNaoLidos, IReadOnlyList<PastaDto> Subpastas);

public sealed record CriarPastaRequest(string Nome, Guid? PastaPaiId, bool PermiteUploadCliente);

public sealed record AtualizarPastaRequest(string Nome, bool PermiteUploadCliente);

public sealed class CriarPastaRequestValidator : AbstractValidator<CriarPastaRequest>
{
    public CriarPastaRequestValidator() => RuleFor(x => x.Nome).NotEmpty().MaximumLength(120)
        .Must(n => n.IndexOfAny(['/', '\\']) < 0).WithMessage("O nome da pasta não pode conter '/' ou '\\'.");
}

public sealed class AtualizarPastaRequestValidator : AbstractValidator<AtualizarPastaRequest>
{
    public AtualizarPastaRequestValidator() => RuleFor(x => x.Nome).NotEmpty().MaximumLength(120)
        .Must(n => n.IndexOfAny(['/', '\\']) < 0).WithMessage("O nome da pasta não pode conter '/' ou '\\'.");
}
