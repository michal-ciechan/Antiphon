namespace Antiphon.Server.Application.Dtos;

/// <summary>CARD-0179 R1. Body of <c>POST /api/diagnostics/bundle</c>.</summary>
public sealed record BugReportRequest(
    string? Route = null,
    Guid? AgentId = null,
    Guid? SessionId = null,
    string? ScreenshotPngBase64 = null,
    IReadOnlyList<ConsoleEntry>? Console = null,
    bool IncludePaths = false,
    string? Note = null);

public sealed record ConsoleEntry(
    DateTime At,
    string Level,
    string Message,
    string? Url = null,
    int? Status = null,
    int? Ms = null);
