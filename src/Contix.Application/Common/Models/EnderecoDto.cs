using Contix.Domain.Entities;

namespace Contix.Application.Common.Models;

public sealed record EnderecoDto(
    string? Cep, string? Logradouro, string? Numero, string? Complemento,
    string? Bairro, string? Cidade, string? Uf)
{
    public static EnderecoDto? From(Endereco? e) =>
        e is null ? null : new(e.Cep, e.Logradouro, e.Numero, e.Complemento, e.Bairro, e.Cidade, e.Uf);

    public Endereco ToEntity() => new()
    {
        Cep = Cep, Logradouro = Logradouro, Numero = Numero, Complemento = Complemento,
        Bairro = Bairro, Cidade = Cidade, Uf = Uf?.ToUpperInvariant()
    };
}
