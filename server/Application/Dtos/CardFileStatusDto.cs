using System.Text.Json.Serialization;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

public record CardFileStatusDto
{
    public Guid BoardId { get; init; }
    public bool Enabled { get; init; }
    public bool SyncCardFiles { get; init; }
    public RepositoryVisibility RepositoryVisibility { get; init; }
    public string VisibilitySource { get; init; } = "Unknown";
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? RepositoryPath { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Directory { get; init; }
    public bool Eligible { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Reason { get; init; }
    public string[] Warnings { get; init; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool? Ignored { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool? WorkingTreeRemovalPending { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool? GitRemovalPending { get; init; }
    public bool AutoCommit { get; init; }
    public int IntervalSeconds { get; init; }
    public bool RemovalPending => WorkingTreeRemovalPending != false || GitRemovalPending != false;
}

public sealed record CardFileCardStatusDto : CardFileStatusDto
{
    public CardFileVisibility CardFileVisibility { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? RelativeFile { get; init; }
}

public sealed record CardPrivateNotesDto(
    Guid CardId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? PrivateNotes,
    Guid ConcurrencyToken,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] int? RevisionNumber);
