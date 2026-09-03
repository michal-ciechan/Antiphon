using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Exceptions;

/// <summary>
/// CARD-0147: a create that would push the fleet or this role past the in-flight cap.
/// HTTP 409, <c>code: concurrency_limit</c>, with a <c>concurrency</c> problem-details extension.
/// </summary>
public sealed class ConcurrencyLimitException : HttpException
{
    public const string ErrorCode = "concurrency_limit";
    public const string OverrideFlag = "ignoreConcurrencyLimit";
    public const string Coda =
        "Prefer working through these sequentially before starting more. Re-send with "
        + "ignoreConcurrencyLimit=true if the user asked for parallel work this turn.";

    public const int OccupantListCap = 12;

    public ConcurrencyLimitProblemDto Concurrency { get; }

    public ConcurrencyLimitException(ConcurrencyLimitProblemDto concurrency)
        : base(409, FormatDetail(concurrency), ErrorCode, BuildExtensions(concurrency))
    {
        Concurrency = concurrency;
    }

    public static IReadOnlyDictionary<string, object?> BuildExtensions(ConcurrencyLimitProblemDto concurrency) =>
        new Dictionary<string, object?> { ["concurrency"] = concurrency };

    public static string FormatDetail(ConcurrencyLimitProblemDto concurrency)
    {
        var axis = concurrency.Axis == "role" && concurrency.Role is { Length: > 0 } role
            ? $"{concurrency.Count} {role} task{(concurrency.Count == 1 ? "" : "s")} already in flight (limit {concurrency.Limit})"
            : $"{concurrency.Count} task{(concurrency.Count == 1 ? "" : "s")} already in flight (limit {concurrency.Limit})";

        var shown = concurrency.Open.Take(OccupantListCap).Select(FormatOccupant).ToList();
        var extra = concurrency.Open.Count - shown.Count;
        var occupants = shown.Count == 0
            ? string.Empty
            : extra > 0
                ? ": " + string.Join(", ", shown) + $" and {extra} more"
                : ": " + string.Join(", ", shown);

        return axis + occupants + ". " + Coda;
    }

    public static string FormatOccupant(ConcurrencyLimitOccupantDto occupant)
    {
        var text = $"{occupant.ShortId} {occupant.Role} {occupant.Status}";
        return string.IsNullOrWhiteSpace(occupant.Stuck)
            ? text
            : text + $" (stuck: {occupant.Stuck})";
    }

    public static string FormatOverrideWarning(
        int openCount,
        int absoluteLimit,
        AgentTaskRole role,
        int roleCount,
        int? roleLimit)
    {
        var absolute = $"{openCount}/{absoluteLimit} open (limit {absoluteLimit})";
        var roleBit = roleLimit is int limit
            ? $"{roleCount}/{limit} {role} (limit {limit})"
            : $"{role} has no per-role cap";
        return
            $"Concurrency limit ignored: {absolute}, {roleBit}. Proceeding because {OverrideFlag}=true.";
    }
}

/// <summary>The <c>concurrency</c> problem-details extension. Property names camelCase on the wire.</summary>
public sealed record ConcurrencyLimitProblemDto(
    string Axis,
    string? Role,
    int Count,
    int Limit,
    IReadOnlyList<ConcurrencyLimitOccupantDto> Open,
    string Override);

/// <summary>One occupant named by a 409 <c>concurrency_limit</c>.</summary>
public sealed record ConcurrencyLimitOccupantDto(
    Guid TaskId,
    string ShortId,
    string Role,
    string Status,
    string Title,
    string? Stuck);
