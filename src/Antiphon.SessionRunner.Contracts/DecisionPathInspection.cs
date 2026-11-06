namespace Antiphon.SessionRunner.Contracts;

/// <summary>Read-only, point-in-time containment of exact files in an accepted session's cwd.</summary>
public sealed record DecisionPathInspectionRequest(Guid SessionId, Guid RunnerStoreId,
    DateTime AcceptedStartedAt, string Root, IReadOnlyList<string> Paths);

public sealed record DecisionPathInspectionResult(bool Contained, string? Reason,
    DecisionPathInspectionRequest Binding);
