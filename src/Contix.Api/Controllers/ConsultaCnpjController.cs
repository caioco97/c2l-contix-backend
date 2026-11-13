using Contix.Api.Infrastructure;
using Contix.Application.Features.Empresas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Contix.Api.Controllers;

/// <summary>Consulta de CNPJ em API pública, para preencher o cadastro da empresa.</summary>
[Route("api/v1/consulta-cnpj")]
[Authorize(Policy = Policies.Escritorio)]
public sealed class ConsultaCnpjController(IConsultaCnpjService service) : ApiControllerBase
{
    /// <summary>Razão social, nome fantasia, situação, endereço, contatos e regime (Simples/MEI) do CNPJ.</summary>
    [HttpGet("{cnpj}")]
    [ProducesResponseType(typeof(DadosCnpjDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public Task<DadosCnpjDto> Consultar(string cnpj, CancellationToken ct) => service.ConsultarAsync(cnpj, ct);
}
