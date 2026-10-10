namespace Antiphon.Server.Application.Services;

/// <summary>
/// Plain data the renderer and the delta read. Occupancy and observation timestamps may be
/// present and are neither rendered nor part of the content hash.
/// </summary>
public sealed record OrchestratorInstructionsSnapshot(
    PipelineCapsSection Caps,
    IReadOnlyList<RunnerInstructionRow> Runners,
    RunnerDefaultsSection Defaults,
    IReadOnlyList<HoldInstructionRow> Holds,
    IReadOnlyList<PinInstructionRow> Pins,
    IReadOnlyList<LevelInstructionRow> Levels,
    IReadOnlyList<string> StandingLines);

public sealed record PipelineCapsSection(
    int EffectiveMaxConcurrentTasks,
    string MaxConcurrentSource,
    int MaxOpenTasks,
    string DefaultWorkerWorkspace,
    string MinOrchestratorLevel,
    IReadOnlyList<RoleInstructionRow> Roles);

public sealed record RoleInstructionRow(
    string Role,
    int? RecommendedInFlight,
    string Level,
    string? EscalateTo,
    string? Kind);

/// <param name="Occupied">Not rendered. A dispatch-only change must not rewrite the file.</param>
/// <param name="ObservedAt">Not rendered. Observation time is not a setting.</param>
public sealed record RunnerInstructionRow(
    string RunnerId,
    string? Platform,
    bool DispatchEligible,
    int? DeclaredCapacity,
    int? EffectiveBudget,
    string? BudgetSource,
    bool Draining,
    bool Retired,
    IReadOnlyList<string> Features,
    int? Occupied = null,
    string? ObservedAt = null);

public sealed record RunnerDefaultsSection(
    long Revision,
    string? Provenance,
    string? GlobalRunnerId,
    IReadOnlyList<KindDefaultInstruction> KindDefaults,
    string? LastReason);

public sealed record KindDefaultInstruction(string Kind, string? RunnerId);

public sealed record HoldInstructionRow(
    string Kind,
    string Alias,
    string Source,
    string? Until,
    string? Reason,
    DateTimeOffset? ObservedAt = null);

public sealed record PinInstructionRow(
    bool StageWide,
    string? BoardName,
    string? CardIdentifier,
    string Role,
    IReadOnlyList<string> Candidates,
    IReadOnlyList<string> ForbiddenAliases,
    string? Strength,
    string? Provenance,
    string? NotBefore,
    string? NotAfter,
    string? Reason,
    DateTimeOffset CreatedAt);

public sealed record LevelInstructionRow(string Kind, string Level, string Alias);
