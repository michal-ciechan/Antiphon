using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0890 land crash child. The parent launches it with <c>dotnet</c> and one owned marker.
/// Validation is file and string checks only; the database opens inside the harness after admission.
/// </summary>
internal static class LandProtocolCrashWorker
{
    internal const string Marker = "ANTIPHON_C448_LAND_WORKER";

    internal static readonly string[] AllowedCuts =
        ["C03", "C05", "C09", "C12", "C14", "C16", "C17", "resume"];

    internal sealed record Request(string Root, Guid TaskId, string Cut, string Ready);

    internal static async Task RunAsync(string encoded)
    {
        var request = JsonSerializer.Deserialize<Request>(encoded)
            ?? throw new InvalidOperationException(CrashWorkerProcess.StderrSentinel + ": Missing land worker request");
        var rejection = Rejection(request);
        if (rejection is not null)
            throw new InvalidOperationException(CrashWorkerProcess.StderrSentinel + ": " + rejection);

        Console.WriteLine(CrashWorkerProcess.StdoutSentinel);
        Console.Out.Flush();
        var root = Path.GetFullPath(request.Root);
        var ready = Path.GetFullPath(request.Ready);
        if (request.Cut != "resume")
        {
            var witness = StartWitness();
            await File.WriteAllTextAsync(
                ready + ".witness", witness.Id.ToString(CultureInfo.InvariantCulture));
        }

        await LandingSafetyHarness.RunCrashWorkerAsync(root, request.TaskId.ToString("D"), request.Cut, ready);
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
            return "cut is not a land cut";

        return null;
    }

    private static Process StartWitness()
    {
        var start = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("Start-Sleep -Seconds 180");
        return Process.Start(start) ?? throw new InvalidOperationException("land witness did not start");
    }
}
