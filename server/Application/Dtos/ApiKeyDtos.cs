namespace Antiphon.Server.Application.Dtos;

/// <summary>
/// An API key as the API ever describes it: metadata only (CARD-0106 S1). There is deliberately no
/// value here and no endpoint that returns one — a stored key is write-only, exactly like an
/// agent-TUI managed secret, and the only thing that ever reads a value back is the launch-time
/// resolver on its way into a child process's environment.
/// </summary>
public sealed record ApiKeyDto(
    Guid Id,
    string Name,
    // Null = global.
    Guid? ProjectId,
    string? ProjectName,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>
/// Upsert body. A null ProjectId means the global scope; a project-scoped key with the same name is
/// a DIFFERENT key that overrides the global one at resolution time.
/// </summary>
public sealed record PutApiKeyRequest(string Value, Guid? ProjectId = null);
