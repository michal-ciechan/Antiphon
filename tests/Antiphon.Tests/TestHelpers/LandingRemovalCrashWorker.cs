using System.Text.Json;

namespace Antiphon.Tests.TestHelpers;

/// <summary>Owned removal worker: uses the parent's repository and database, then waits to be killed.</summary>
internal static class LandingRemovalCrashWorker
{
    internal const string Marker = "ANTIPHON_C665_REMOVAL_WORKER";
    internal sealed record Request(string Root, Guid TaskId, string Cut, string Ready);

    internal static async Task RunAsync(string encoded)
    {
        var request = JsonSerializer.Deserialize<Request>(encoded)
            ?? throw new InvalidOperationException("Missing removal worker request");
        var root = Path.GetFullPath(request.Root);
        var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        if (!root.StartsWith(temp, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(root).StartsWith("antiphon-c448-", StringComparison.Ordinal)
            || !string.Equals(Path.GetDirectoryName(Path.GetFullPath(request.Ready)), root, StringComparison.OrdinalIgnoreCase)
            || (await File.ReadAllTextAsync(Path.Combine(root, "canonical", "fixture-owner.txt"))).Trim() != request.TaskId.ToString("N")
            || request.Cut is not ("C665-before-drop" or "C665-after-drop"))
            throw new InvalidOperationException("Unowned removal worker");
        await LandingSafetyHarness.RunCrashWorkerAsync(root, request.TaskId.ToString(), request.Cut, request.Ready);
    }
}
