using System.Text.Json;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0890 retirement crash child. Same admission shape as the land worker, with the
/// retirement cut whitelist kept separate from the land whitelist.
/// </summary>
internal static class WorktreeRetirementCrashWorker
{
    internal const string Marker = "ANTIPHON_C459_RETIREMENT_WORKER";

    internal static readonly string[] AllowedCuts =
    [
        "release-before", "release-after", "claim-before", "claim-after",
        "intent-before", "intent-after", "git-exit", "directory-result",
        "registration-result", "branch-cas", "terminal-before", "terminal-after",
        "run-projection", "resume",
    ];

    internal sealed record Request(string Root, Guid TaskId, string Cut, string Ready);

    internal static async Task RunAsync(string encoded)
    {
        var request = JsonSerializer.Deserialize<Request>(encoded)
            ?? throw new InvalidOperationException(CrashWorkerProcess.StderrSentinel + ": Missing retirement worker request");
        var rejection = Rejection(request);
        if (rejection is not null)
            throw new InvalidOperationException(CrashWorkerProcess.StderrSentinel + ": " + rejection);

        CrashWorkerProcess.WriteStdoutSentinel();
        await LandingSafetyHarness.RunRetirementCrashWorkerAsync(
            Path.GetFullPath(request.Root), request.TaskId.ToString("D"), request.Cut, Path.GetFullPath(request.Ready));
    }

    /// <summary>Null when the request is owned. Does not open a database or a process.</summary>
    internal static string? Rejection(Request request)
    {
        var root = Path.GetFullPath(request.Root);
        var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()))
            + Path.DirectorySeparatorChar;
        if (!root.StartsWith(temp, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(root).StartsWith("antiphon-c448-", StringComparison.Ordinal))
            return "unowned root";

        var ready = Path.GetFullPath(request.Ready);
        if (!string.Equals(Path.GetDirectoryName(ready), root, StringComparison.OrdinalIgnoreCase))
            return "ready outside root";

        var ownerPath = Path.Combine(root, "canonical", "fixture-owner.txt");
        if (!File.Exists(ownerPath))
            return "missing fixture owner";
        var owner = File.ReadAllText(ownerPath).Trim();
        if (!string.Equals(owner, request.TaskId.ToString("N"), StringComparison.Ordinal))
            return "task is not the fixture owner";

        if (Array.IndexOf(AllowedCuts, request.Cut) < 0)
            return "cut is not a retirement cut";

        return null;
    }
}
