using Antiphon.Server.Domain.Entities;

namespace Antiphon.Server.Application.Services;

/// <summary>Request-row writer identity is committed in the same update as its concurrency token.</summary>
internal static class LandRequestWriteProvenance
{
    private static readonly HashSet<string> Labels = new(StringComparer.Ordinal)
    {
        "admission", "start", "hold", "yield", "terminal", "race-retry", "sweep-cancel",
        "request-repair", "request-supersede", "source-checkpoint", "operation-attach",
        "monitor-sweep", "protocol-progress", "merge-supersession", "source-child",
    };

    public static void Stamp(AgentTaskLandRequest request, string label, TimeProvider clock)
        => Stamp(request, label, clock.GetUtcNow().UtcDateTime);

    public static void Stamp(AgentTaskLandRequest request, string label, DateTime at)
    {
        if (!Labels.Contains(label)) throw new ArgumentOutOfRangeException(nameof(label));
        var token = Guid.NewGuid();
        request.ConcurrencyToken = token;
        request.LastWriterToken = token;
        request.LastWriterOperation = label;
        request.LastWriterAt = at.Kind == DateTimeKind.Utc ? at : at.ToUniversalTime();
    }

    public static string ObservedLabel(AgentTaskLandRequest request)
        => request.LastWriterToken == request.ConcurrencyToken
            && request.LastWriterOperation is { } label && Labels.Contains(label)
                ? label : "unknown";
}
