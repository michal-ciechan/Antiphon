namespace Antiphon.Server.Application.Exceptions;

/// <summary>
/// Thrown when a requested entity is not found. Maps to HTTP 404.
/// </summary>
public class NotFoundException : HttpException
{
    public string EntityName { get; }
    public object EntityId { get; }

    public NotFoundException(string entityName, object id)
        : base(404, $"{entityName} with id '{id}' was not found.")
    {
        EntityName = entityName;
        EntityId = id;
    }
}
