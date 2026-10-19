using System.Text.RegularExpressions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class RunnerReadSourcePinTests
{
    [Test]
    public void S2_queue_service_awaits_runner_reads()
    {
        AssertNoBlockingRunnerReads(
        [
            "server/Application/Services/SessionMessageQueueService.cs",
            "server/Application/Services/SessionMessageQueueService.Expectations.cs",
            "server/Application/Services/SessionMessageQueueService.DispatchBrief.cs",
        ]);
    }

    private static void AssertNoBlockingRunnerReads(IEnumerable<string> relativePaths)
    {
        var root = RepoRoot;
        var retiredRead = new Regex(
            @"\b(TryGetLiveSnapshot|TryGetLiveMetadata|IsLiveOrUnknown|GetBufferSnapshot)\s*\("
            + @"|\b(ListLiveSessions|ListLiveOrUnknownSessions|ListUnknownSessions|ListLocalLiveSessions)\s*\(\s*\)");
        var runnerRead = new Regex(@"\b(_runnerClient|_runner)\s*\.\s*(ListAsync|GetAsync|GetBufferAsync|GetSnapshotAsync)\s*\(");
        var blockingWait = new Regex(@"\.\s*GetAwaiter\s*\(\s*\)\s*\.\s*GetResult\s*\(\s*\)|\.\s*Result\b");
        var offenders = new List<string>();

        foreach (var relativePath in relativePaths)
        {
            var path = Path.Combine(root, relativePath);
            File.Exists(path).ShouldBeTrue($"source pin target must exist: {relativePath}");
            var lineNumber = 0;
            foreach (var line in File.ReadLines(path))
            {
                lineNumber++;
                var code = line.TrimStart();
                if (code.StartsWith("//", StringComparison.Ordinal) || code.StartsWith('*'))
                    continue;
                if (retiredRead.IsMatch(code) || (runnerRead.IsMatch(code) && blockingWait.IsMatch(code)))
                    offenders.Add($"{relativePath}:{lineNumber}: {code}");
            }
        }

        offenders.ShouldBeEmpty("runner reads must use the bounded async runtime API. Offenders:\n"
            + string.Join('\n', offenders));
    }

    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Antiphon.sln")))
                directory = directory.Parent;
            return directory?.FullName
                ?? throw new DirectoryNotFoundException("Could not locate repo root (Antiphon.sln) from test base dir.");
        }
    }
}
