using Contix.Application.Common.Abstractions;
using Contix.Application.Common.Exceptions;
using Contix.Application.Common.Models;
using Contix.Domain.Enums;
using Contix.Domain.Exceptions;
using Contix.Domain.ValueObjects;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;

namespace Contix.Application.Features.Empresas;

/// <summary>Dados públicos do CNPJ (Receita Federal), para preencher o cadastro da empresa.</summary>
/// <param name="RegimeSugerido">Simples Nacional ou MEI quando a empresa é optante; nulo quando não dá para saber.</param>
public sealed record DadosCnpjDto(
    string Cnpj, string RazaoSocial, string? NomeFantasia, string? SituacaoCadastral, bool Ativa,
    string? Email, string? Telefone, EnderecoDto? Endereco, RegimeTributario? RegimeSugerido,
    string? AtividadePrincipal, DateOnly? InicioAtividade, string Fonte);

/// <summary>Resultado de um provedor de consulta de CNPJ.</summary>
public abstract record ResultadoConsultaCnpj
{
    public sealed record Encontrado(DadosCnpjDto Dados) : ResultadoConsultaCnpj;
    public sealed record NaoEncontrado : ResultadoConsultaCnpj;
    /// <summary>Provedor fora do ar, limite de uso etc.: vale tentar outro provedor.</summary>
    public sealed record Indisponivel(string Motivo) : ResultadoConsultaCnpj;
}

/// <summary>Consulta de CNPJ em API pública (BrasilAPI, ReceitaWS...).</summary>
public interface IProvedorConsultaCnpj
{
    string Nome { get; }
    Task<ResultadoConsultaCnpj> ConsultarAsync(string cnpj, CancellationToken ct = default);
}

public interface IConsultaCnpjService
{
    Task<DadosCnpjDto> ConsultarAsync(string cnpj, CancellationToken ct = default);
}

/// <summary>Consulta os provedores em ordem até um responder (o próximo só é usado se o anterior estiver indisponível).</summary>
public sealed class ConsultaCnpjService(
    IEnumerable<IProvedorConsultaCnpj> provedores, ICurrentUser currentUser, ILogger<ConsultaCnpjService> logger) : IConsultaCnpjService
{
    public async Task<DadosCnpjDto> ConsultarAsync(string cnpj, CancellationToken ct = default)
    {
        if (currentUser.EhCliente) throw new ForbiddenException();
        var digitos = DocumentoFiscal.SomenteDigitos(cnpj);
        if (!DocumentoFiscal.CnpjValido(digitos))
            throw new ValidationException([new ValidationFailure("cnpj", "CNPJ inválido.")]);

        var lista = provedores.ToList();
        if (lista.Count == 0) throw new DomainException("A consulta de CNPJ não está configurada neste ambiente.");

        foreach (var provedor in lista)
        {
            ResultadoConsultaCnpj resultado;
            try { resultado = await provedor.ConsultarAsync(digitos, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Falha na consulta de CNPJ pelo provedor {Provedor}", provedor.Nome);
                continue;
            }
            switch (resultado)
            {
                case ResultadoConsultaCnpj.Encontrado e: return e.Dados;
                case ResultadoConsultaCnpj.NaoEncontrado: throw new NotFoundException("CNPJ na Receita Federal");
                case ResultadoConsultaCnpj.Indisponivel i:
                    logger.LogWarning("Consulta de CNPJ indisponível no provedor {Provedor}: {Motivo}", provedor.Nome, i.Motivo);
                    break;
            }
        }
        throw new DomainException("O serviço de consulta de CNPJ está indisponível agora. Preencha os dados manualmente ou tente de novo em instantes.");
    }
}
