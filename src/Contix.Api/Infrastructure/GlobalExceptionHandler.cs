using Contix.Application.Common.Exceptions;
using Contix.Domain.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Contix.Api.Infrastructure;

/// <summary>Converte exceções em respostas ProblemDetails (RFC 9457) sem vazar detalhes internos.</summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetails,
    IHostEnvironment env,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, title) = exception switch
        {
            ValidationException => (StatusCodes.Status400BadRequest, "Requisição inválida"),
            UnauthorizedException => (StatusCodes.Status401Unauthorized, "Não autorizado"),
            ForbiddenException => (StatusCodes.Status403Forbidden, "Acesso negado"),
            NotFoundException => (StatusCodes.Status404NotFound, "Recurso não encontrado"),
            ConflictException => (StatusCodes.Status409Conflict, "Conflito"),
            DomainException => (StatusCodes.Status422UnprocessableEntity, "Regra de negócio violada"),
            OperationCanceledException when context.RequestAborted.IsCancellationRequested => (499, "Requisição cancelada"),
            _ => (StatusCodes.Status500InternalServerError, "Erro interno")
        };

        if (status >= 500)
            logger.LogError(exception, "Erro não tratado ao processar {Method} {Path}", context.Request.Method, context.Request.Path);
        else
            logger.LogInformation("Requisição finalizada com {Status}: {Message}", status, exception.Message);

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = status >= 500 && !env.IsDevelopment() ? "Ocorreu um erro inesperado. Tente novamente mais tarde." : exception.Message,
            Instance = context.Request.Path
        };

        if (exception is ValidationException validation)
        {
            problem.Detail = "Um ou mais campos são inválidos.";
            problem.Extensions["errors"] = validation.Errors
                .GroupBy(e => string.IsNullOrEmpty(e.PropertyName) ? "" : char.ToLowerInvariant(e.PropertyName[0]) + e.PropertyName[1..])
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());
        }

        if (status >= 500 && env.IsDevelopment())
            problem.Extensions["exception"] = exception.ToString();

        context.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem,
            Exception = exception
        });
    }
}
