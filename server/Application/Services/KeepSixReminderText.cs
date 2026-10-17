namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1320 S3. One body for the WhenIdle reminder. It names the shortfall and the hold script.
/// It does not tell the orchestrator to reply, and it does not dispatch.
/// </summary>
public static class KeepSixReminderText
{
    public const string NoteHeader = "[keep-six]";

    public sealed record HostRemaining(string HostId, int? Remaining);

    public sealed record StageRemaining(string Role, int? ParallelRemaining);

    public sealed record Facts(
        int CountedOpen,
        int EffectiveTarget,
        int DispatchableBacklog,
        int Shortfall,
        IReadOnlyList<string> TopCards,
        IReadOnlyList<HostRemaining> Hosts,
        IReadOnlyList<StageRemaining> Stages);

    public static string Compose(Facts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var cards = facts.TopCards.Count == 0 ? "none" : string.Join(", ", facts.TopCards);
        var hosts = facts.Hosts.Count == 0
            ? "none"
            : string.Join(", ", facts.Hosts.Select(host => host.HostId + " " + Count(host.Remaining)));
        var stages = facts.Stages.Count == 0
            ? "none"
            : string.Join(", ", facts.Stages.Select(stage => stage.Role + " " + Count(stage.ParallelRemaining)));
        return string.Join('\n',
            $"Open {facts.CountedOpen}, shortfall {facts.Shortfall}, effective target {facts.EffectiveTarget}, dispatchable backlog {facts.DispatchableBacklog}.",
            $"Cards: {cards}.",
            "Antiphon has not dispatched.",
            $"Host remaining: {hosts}.",
            $"Stage parallel remaining: {stages}.",
            "Declare a hold with scripts/keep-six.ps1.");
    }

    private static string Count(int? value) => value?.ToString() ?? "n/a";
}
