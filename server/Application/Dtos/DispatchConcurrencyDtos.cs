using System.Text.Json;

namespace Antiphon.Server.Application.Dtos;

public sealed record DispatchConcurrencyRangeDto(int ParallelMin, int ParallelMax, int QueuedMin, int QueuedMax);

public sealed record DispatchConcurrencySeedRoleDto(string Role, int? MaxParallel, int? MaxQueued);

public sealed record DispatchConcurrencySeedDto(
    string Origin,
    DateTime ImportedAt,
    string Mode,
    int MaxParallel,
    int? MaxQueued,
    IReadOnlyList<DispatchConcurrencySeedRoleDto> Roles);

public sealed record DispatchConcurrencyRoleDto(
    string Role,
    int? MaxParallel,
    string MaxParallelSource,
    int? MaxQueued,
    string MaxQueuedSource,
    int? CombinedParallel);

public sealed record DispatchConcurrencyEffectiveDto(
    string Mode,
    string ModeSource,
    int MaxParallel,
    string MaxParallelSource,
    int? MaxQueued,
    string MaxQueuedSource,
    IReadOnlyList<DispatchConcurrencyRoleDto> Roles,
    long GlobalRevision,
    long ProjectRevision);

public sealed record DispatchConcurrencyRoleOccupancyDto(
    string Role,
    int Open,
    int Parallel,
    int Queued,
    int? ParallelRemaining,
    int? QueuedRemaining,
    int ParallelOverage,
    int QueuedOverage);

public sealed record DispatchConcurrencyOccupancyDto(
    int Open,
    int Parallel,
    int Queued,
    int? ParallelRemaining,
    int? QueuedRemaining,
    int ParallelOverage,
    int QueuedOverage,
    bool ExceedsBounds,
    IReadOnlyList<DispatchConcurrencyRoleOccupancyDto> Roles);

public sealed record DispatchConcurrencyGlobalDto(
    DispatchConcurrencySeedDto Seed,
    JsonElement Overrides,
    DispatchConcurrencyEffectiveDto Effective,
    DispatchConcurrencyEffectiveDto NullProject,
    IReadOnlyList<string> SupportedRoles,
    DispatchConcurrencyRangeDto Ranges,
    long Revision,
    DateTime UpdatedAt,
    string Reason,
    string Provenance,
    Guid? CallerTaskId,
    DispatchConcurrencyOccupancyDto Occupancy);

public sealed record DispatchConcurrencyProjectDto(
    Guid ProjectId,
    JsonElement Overrides,
    DispatchConcurrencyEffectiveDto Inherited,
    DispatchConcurrencyEffectiveDto Effective,
    IReadOnlyList<string> SupportedRoles,
    DispatchConcurrencyRangeDto Ranges,
    long Revision,
    long GlobalRevision,
    DateTime? UpdatedAt,
    string? Reason,
    string? Provenance,
    Guid? CallerTaskId,
    DispatchConcurrencyOccupancyDto Occupancy);

public sealed record DispatchConcurrencyRevisionDto(
    long Revision,
    long? PreviousRevision,
    JsonElement Snapshot,
    DateTime CreatedAt,
    string Reason,
    string Provenance,
    Guid? CallerTaskId);

public sealed record DispatchConcurrencyRevisionPageDto(
    IReadOnlyList<DispatchConcurrencyRevisionDto> Revisions,
    long? NextBeforeRevision);

public sealed record PutDispatchConcurrencyRequest(
    long ExpectedRevision,
    long? ExpectedGlobalRevision,
    JsonElement Overrides,
    string Reason,
    string Provenance);
