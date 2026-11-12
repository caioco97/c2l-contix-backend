namespace Contix.Domain.Exceptions;

/// <summary>Violação de regra de negócio (mapeada para HTTP 422).</summary>
public class DomainException(string message) : Exception(message);
