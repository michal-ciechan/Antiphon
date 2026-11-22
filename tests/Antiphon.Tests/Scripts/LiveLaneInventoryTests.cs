using System.Diagnostics;
using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1545 S1a. Ordinary Unit roster for the six live identities.
/// Classification is Unit, not Integration: the method is parameterless inventory
/// and does not host a session. Matrices stay inside this one method.
/// The gate matrix (W2-nightly S23, LiveLaneGateScan.cs) and the roster (W2-nightly S24, LiveLaneRoster.cs) run
/// in-process; the roster still lists this assembly's tests in a child (dotnet exec --list-tests). The census cases
/// still run the manifest driver until the census is compiled (S25).
/// </summary>
[Category("Unit")]
public sealed class LiveLaneInventoryTests
{
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void ExactCoreRoster()
    {
        var result = LiveLaneProcess.Roster("ExactCoreRoster", LiveLaneRoster.TestsProject, typeof(LiveLaneInventoryTests).Assembly.Location);
        result.Failures.ShouldBeEmpty(string.Join('\n', result.Failures));
    }

    [Test]
    public void EveryLiveGateHasDisposition()
    {
        var failures = Antiphon.Nightly.LiveLaneGateScan.Matrix(LiveLaneProcess.FindRepo());
        failures.ShouldBeEmpty(string.Join('\n', failures));
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task CensusAdditiveIdentity()
    {
        var result = await LiveLaneProcess.RunAsync("CensusAdditiveIdentity").ConfigureAwait(false);
        result.Failures.ShouldBeEmpty(string.Join('\n', result.Failures));
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task CensusTargetScope()
    {
        var result = await LiveLaneProcess.RunAsync("CensusTargetScope").ConfigureAwait(false);
        result.Failures.ShouldBeEmpty(string.Join('\n', result.Failures));
    }

    internal static class LiveLaneProcess
    {
        // Hard wall for the driver child. Loaded-host n=3 roster totals 122603, 159105, 127989 ms
        // (p95 = 159105). 320000 ms is about 2x that p95.
        public const int ChildBudgetMs = 320_000;

        /// <summary>
        /// The roster case in-process (LiveLaneRoster.RunCase, the old ExactCoreRoster / ExactPtyCoreRoster of the
        /// driver): the assembly is listed into .antiphon/live-lane-capture under the listing's own watchdog (160 s).
        /// </summary>
        public static LiveLaneCaseResult Roster(string caseName, string project, string assembly)
        {
            var repo = FindRepo();
            var clock = Stopwatch.StartNew();
            assembly.ShouldNotBeNullOrWhiteSpace("C1545 assembly-location");
            var result = LiveLaneRoster.RunCase(repo, project, assembly);
            var phase = ReadPhase(repo);
            result.ExitCode.ShouldBe(0, $"C1545 {caseName} discovery exit error={result.Error} elapsedMs={clock.ElapsedMilliseconds} phase={phase}");
            result.DiscoveryOk.ShouldBeTrue($"C1545 {caseName} discoveryOk error={result.Error} elapsedMs={clock.ElapsedMilliseconds} phase={phase}");
            return result;
        }

        public static async Task<LiveLaneScriptResult> RunAsync(string caseName)
        {
            var repo = FindRepo();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var script = Path.Combine(repo, "scripts", "test-live-lane-manifest.ps1");
            File.Exists(script).ShouldBeTrue("C1545 manifest-script");

            var psi = new ProcessStartInfo("pwsh")
            {
                WorkingDirectory = repo,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-File");
            psi.ArgumentList.Add(script);
            psi.ArgumentList.Add("-Case");
            psi.ArgumentList.Add(caseName);
            psi.ArgumentList.Add("-RepoRoot");
            psi.ArgumentList.Add(repo);

            using var process = Process.Start(psi)!;
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            var timedOut = !process.WaitForExit(ChildBudgetMs);
            if (timedOut)
            {
                try { process.Kill(entireProcessTree: true); } catch { process.Kill(); }
                process.WaitForExit();
            }
            else
            {
                process.WaitForExit();
            }

            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            LiveLaneScriptResult? parsed = null;
            try
            {
                var json = LastJson(stdout);
                if (!string.IsNullOrWhiteSpace(json))
                    parsed = JsonSerializer.Deserialize<LiveLaneScriptResult>(json, JsonOptions);
            }
            catch (JsonException)
            {
                parsed = null;
            }

            parsed.ShouldNotBeNull($"C1545 {caseName} result json exit={process.ExitCode} timedOut={timedOut} elapsedMs={clock.ElapsedMilliseconds} stderr={Tail(stderr)} stdout={Tail(stdout)}");
            process.ExitCode.ShouldBe(0, $"C1545 {caseName} exit stderr={Tail(stderr)} error={parsed!.Error}");
            parsed!.DiscoveryOk.ShouldBeTrue($"C1545 {caseName} discoveryOk error={parsed.Error} stderr={Tail(stderr)}");
            parsed.Failures ??= [];
            return parsed;
        }

        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        private static string LastJson(string stdout)
        {
            var start = stdout.LastIndexOf("\n{", StringComparison.Ordinal);
            if (start < 0 && stdout.TrimStart().StartsWith('{'))
                return stdout.Trim();
            if (start < 0)
                return "";
            return stdout[(start + 1)..].Trim();
        }

        private static string ReadPhase(string repo)
        {
            var path = Path.Combine(repo, ".antiphon", "live-lane-capture", "phase.log");
            if (!File.Exists(path))
                return "";
            var text = File.ReadAllText(path).Trim();
            return text.Length <= 800 ? text : text[^800..];
        }

        private static string Tail(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            var trimmed = text.Trim();
            return trimmed.Length <= 1200 ? trimmed : trimmed[^1200..];
        }

        internal static string FindRepo()
        {
            foreach (var start in new[] { Directory.GetCurrentDirectory(), Path.GetDirectoryName(typeof(LiveLaneInventoryTests).Assembly.Location) })
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

    public sealed class LiveLaneScriptResult
    {
        public bool DiscoveryOk { get; set; }
        public string? Error { get; set; }
        public string[]? Failures { get; set; }
        public int LaunchCount { get; set; }
    }
}
