namespace Antiphon.Server.Application.Dtos;

public record TemplateGroupDto(
    Guid Id,
    string Name,
    string Description,
    bool IsBuiltIn,
    int TemplateCount,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record CreateTemplateGroupRequest(string Name, string Description);
public record UpdateTemplateGroupRequest(string Name, string Description);
