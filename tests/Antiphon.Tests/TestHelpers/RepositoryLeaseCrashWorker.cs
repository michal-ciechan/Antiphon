using System.Diagnostics;
using System.Text.Json;
using Antiphon.Server.Infrastructure.Git;

namespace Antiphon.Tests.TestHelpers;

internal static class RepositoryLeaseCrashWorker
{
    internal const string Marker = "ANTIPHON_C448_LEASE_CRASH_WORKER";
    internal sealed record Request(string Root, string Source, string Main, string? Evidence, string Mode);

    internal static async Task RunAsync(string encoded)
    {
        var request = JsonSerializer.Deserialize<Request>(encoded)
            ?? throw new InvalidOperationException("Missing lease worker request");
        var root = Path.GetFullPath(request.Root);
        var tempRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        if (!root.StartsWith(tempRoot, StringComparison.Ordinal)
            || !Path.GetFileName(root).StartsWith("antiphon-c448-", StringComparison.Ordinal)
            || !Path.GetFullPath(request.Source).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || !Path.GetFullPath(request.Main).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || request.Mode is not ("start" or "admit"))
            throw new InvalidOperationException("Unowned lease crash worker");

        var git = new LandingGit();
        if (request.Mode == "start")
        {
            var result = await git.RunAsync(request.Source, ["commit", "--allow-empty", "-m", "owned child"],
                CancellationToken.None);
            if (!result.Succeeded) throw new InvalidOperationException("fixture_" + result.Diagnostic);
            return;
        }

        if (request.Evidence is null || !Path.GetFullPath(request.Evidence).StartsWith(root + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
            throw new InvalidOperationException("Unowned lease worker evidence");
        var leases = new RepositoryMutationLease(git);
        foreach (var repository in new[] { request.Source, request.Main })
        {
            await using var lease = await leases.TryAcquireAsync(repository, CancellationToken.None);
            if (lease is not null) throw new InvalidOperationException("Live child admitted another worker");
        }
        using var process = Process.GetCurrentProcess();
        await File.WriteAllTextAsync(request.Evidence, JsonSerializer.Serialize(new
        {
            Worker = process.Id,
            StartTicks = process.StartTime.ToUniversalTime().Ticks,
            HeldSource = true,
            HeldMain = true,
        }));
    }
}
