namespace Contix.Application.Common.Exceptions;

/// <summary>Recurso não encontrado (404).</summary>
public class NotFoundException(string recurso, object? chave = null)
    : Exception(chave is null ? $"{recurso} não encontrado(a)." : $"{recurso} '{chave}' não encontrado(a).");

/// <summary>Conflito de estado, ex.: registro duplicado (409).</summary>
public class ConflictException(string message) : Exception(message);

/// <summary>Usuário autenticado sem permissão (403).</summary>
public class ForbiddenException(string message = "Você não tem permissão para executar esta ação.") : Exception(message);

/// <summary>Falha de autenticação (401).</summary>
public class UnauthorizedException(string message = "Não autenticado.") : Exception(message);
