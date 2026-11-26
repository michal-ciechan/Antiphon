using Antiphon.Agents.Pty.Tests;
using System.Diagnostics;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests.Invariants;

/// <summary>
/// CARD-1545 S1a. Ordinary Unit roster for the five compiled PTY live identities.
/// Classification is Unit, not Integration. The method does not launch ConPTY or Claude.
/// The roster runs in-process (W2-nightly S24, LiveLaneRoster.cs in scripts/lib/Antiphon.Nightly); it still lists this
/// assembly's tests in a child (dotnet exec --list-tests) under the listing's own watchdog (160 s).
/// </summary>
[Category("RepositoryInvariant")]
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LiveClaudeInventoryTests
{
    [Test]
    public void ExactPtyCoreRoster()
    {
        var repo = FindRepo();
        var clock = Stopwatch.StartNew();
        var assembly = typeof(LiveClaudeInventoryTests).Assembly.Location;
        assembly.ShouldNotBeNullOrWhiteSpace("C1545 assembly-location");
        var result = LiveLaneRoster.RunCase(repo, LiveLaneRoster.PtyProject, assembly);
        var phase = ReadPhase(repo);
        result.ExitCode.ShouldBe(0, $"C1545 ExactPtyCoreRoster discovery exit error={result.Error} elapsedMs={clock.ElapsedMilliseconds} phase={phase}");
        result.DiscoveryOk.ShouldBeTrue($"C1545 ExactPtyCoreRoster discoveryOk error={result.Error} elapsedMs={clock.ElapsedMilliseconds} phase={phase}");
        result.Failures.ShouldBeEmpty(string.Join('\n', result.Failures));
    }

    private static string ReadPhase(string repo)
    {
        var path = Path.Combine(repo, ".antiphon", "live-lane-capture", "phase.log");
        if (!File.Exists(path))
            return "";
        var text = File.ReadAllText(path).Trim();
        return text.Length <= 800 ? text : text[^800..];
    }

    private static string FindRepo()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), Path.GetDirectoryName(typeof(LiveClaudeInventoryTests).Assembly.Location) })
        {
            if (string.IsNullOrWhiteSpace(start))
                continue;
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
                    return dir.FullName;
                dir = dir.Parent;
            }
        }

        throw new InvalidOperationException("C1545 repo-root-missing");
    }
}
