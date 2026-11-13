using Contix.Domain.Entities;
using Contix.Domain.ValueObjects;

namespace Contix.Application.Common.Models;

public sealed record EnderecoDto(
    string? Cep, string? Logradouro, string? Numero, string? Complemento,
    string? Bairro, string? Cidade, string? Uf)
{
    public static EnderecoDto? From(Endereco? e) =>
        e is null ? null : new(e.Cep, e.Logradouro, e.Numero, e.Complemento, e.Bairro, e.Cidade, e.Uf);

    public Endereco ToEntity()
    {
        var n = Normalizar();
        return new()
        {
            Cep = n.Cep, Logradouro = n.Logradouro, Numero = n.Numero, Complemento = n.Complemento,
            Bairro = n.Bairro, Cidade = n.Cidade, Uf = n.Uf
        };
    }

    /// <summary>CEP só com dígitos, UF em maiúsculas e textos aparados (vazio vira null).</summary>
    public EnderecoDto Normalizar() => new(
        DadosBrasileiros.NormalizarCep(Cep), Texto(Logradouro), Texto(Numero), Texto(Complemento),
        Texto(Bairro), Texto(Cidade), Texto(Uf)?.ToUpperInvariant());

    private static string? Texto(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}
