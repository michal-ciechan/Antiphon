using System.Diagnostics;
using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1545 S1a. Ordinary Unit roster for the six live identities.
/// Classification is Unit, not Integration: the method is parameterless inventory
/// and does not host a session. Matrices stay inside this one method.
/// </summary>
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LiveLaneInventoryTests
{
    [Test]
    public async Task ExactCoreRoster()
    {
        var result = await LiveLaneProcess.RunAsync("ExactCoreRoster", "Antiphon.Tests").ConfigureAwait(false);
        result.Failures.ShouldBeEmpty(string.Join('\n', result.Failures));
    }

    [Test]
    public async Task EveryLiveGateHasDisposition()
    {
        var result = await LiveLaneProcess.RunAsync("EveryLiveGateHasDisposition", "Antiphon.Tests").ConfigureAwait(false);
        result.Failures.ShouldBeEmpty(string.Join('\n', result.Failures));
    }

    internal static class LiveLaneProcess
    {
        public static async Task<LiveLaneScriptResult> RunAsync(string caseName, string project)
        {
            var repo = FindRepo();
            var assembly = typeof(LiveLaneInventoryTests).Assembly.Location;
            assembly.ShouldNotBeNullOrWhiteSpace("C1545 assembly-location");
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
            psi.ArgumentList.Add("-Project");
            psi.ArgumentList.Add(project);
            psi.ArgumentList.Add("-AssemblyPath");
            psi.ArgumentList.Add(assembly);
            psi.ArgumentList.Add("-RepoRoot");
            psi.ArgumentList.Add(repo);

            using var process = Process.Start(psi)!;
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(150_000))
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

            parsed.ShouldNotBeNull($"C1545 {caseName} result json exit={process.ExitCode} stderr={Tail(stderr)} stdout={Tail(stdout)}");
            process.ExitCode.ShouldBe(0, $"C1545 {caseName} discovery exit stderr={Tail(stderr)} error={parsed!.Error}");
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

        private static string Tail(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            var trimmed = text.Trim();
            return trimmed.Length <= 1200 ? trimmed : trimmed[^1200..];
        }

        private static string FindRepo()
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
