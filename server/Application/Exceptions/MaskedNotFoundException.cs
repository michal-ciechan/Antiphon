namespace Antiphon.Server.Application.Exceptions;

/// <summary>A 404 with a fixed body for resources whose existence is scoped to the caller.</summary>
public sealed class MaskedNotFoundException(string entityName, object id)
    : NotFoundException(entityName, id, $"{entityName} was not found.");
