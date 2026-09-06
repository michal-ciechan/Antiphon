using System.Text.Json.Serialization;

namespace Antiphon.Server.Application.Dtos;

/// <summary>
/// Outcome of syncing one board's cards into <c>docs/cards/&lt;slug&gt;/</c> (CARD-0004).
/// </summary>
public sealed record CardFileSyncBoardResult(
    Guid BoardId,
    string BoardName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Directory,
    int Written,
    int Deleted,
    int Unchanged,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? CommitSha,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? WriteSkipReason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? CommitSkipReason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Error,
    bool DryRun)
{
    public int EligibleCards { get; init; }
    public int ExcludedCards { get; init; }
    public CardFileStatusDto Policy { get; init; } = new();
    public string[] Warnings { get; init; } = [];
}
