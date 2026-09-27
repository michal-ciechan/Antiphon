using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.ValueObjects;

/// <summary>One immutable step in a pipeline definition revision.</summary>
public sealed record PipelineStageSpec(
    AgentTaskRole Role,
    string BundleKey,
    IReadOnlyList<string> AllowedNext);
