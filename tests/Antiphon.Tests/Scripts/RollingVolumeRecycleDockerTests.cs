using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[Category("Slow")]
public sealed class RollingVolumeRecycleDockerTests
{
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1008_Real_docker_comparison()
    {
        var psi = new ProcessStartInfo("node") {
            WorkingDirectory = DelegateScriptRunner.RepoRoot,
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        };
        psi.ArgumentList.Add(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/fixtures/c1008-recycle-real-cases.mjs"));
        using var child = Process.Start(psi)!;
        var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(12));
        try { await child.WaitForExitAsync(deadline.Token); }
        catch {
            if (!child.HasExited) { child.Kill(true); await child.WaitForExitAsync(); }
            var captured = await stdout;
            var ownedRoot = Regex.Match(captured, @"(?m)^C1008_REAL_ROOT=(/tmp/c1008-real-[A-Za-z0-9]+)$");
            if (ownedRoot.Success) {
                var cleanupInfo = new ProcessStartInfo("node") { UseShellExecute = false,
                    RedirectStandardOutput = true, RedirectStandardError = true };
                cleanupInfo.ArgumentList.Add(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/fixtures/c1008-recycle-cleanup.mjs"));
                cleanupInfo.ArgumentList.Add(ownedRoot.Groups[1].Value);
                using var cleanup = Process.Start(cleanupInfo)!;
                var cleanupOutput = cleanup.StandardOutput.ReadToEndAsync();
                var cleanupError = cleanup.StandardError.ReadToEndAsync();
                using var cleanupDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                try { await cleanup.WaitForExitAsync(cleanupDeadline.Token); }
                catch { if (!cleanup.HasExited) { cleanup.Kill(true); await cleanup.WaitForExitAsync(); } throw; }
                cleanup.ExitCode.ShouldBe(0, "owned deadline cleanup: " + await cleanupOutput + await cleanupError);
            }
            throw;
        }
        var output = await stdout + await stderr;
        child.ExitCode.ShouldBe(0, "recycle-real-comparison: real Docker/Git outcomes; " + output);
        var match = Regex.Match(output, @"C1008_REAL cases=32 base=5 changed=27 failures=0 cleanup=absent evidence=(.+)");
        match.Success.ShouldBeTrue("recycle-real-comparison: 32 outcomes and checked cleanup; " + output);
        var evidence = JsonNode.Parse(File.ReadAllText(match.Groups[1].Value.Trim()))!;
        var rows = evidence["results"]!.AsArray();
        rows.Count.ShouldBe(32);
        rows.Select(x => x!["name"]!.GetValue<string>()).Distinct().Count().ShouldBe(32);
        rows.Count(x => x!["version"]!.GetValue<string>() == "B").ShouldBe(5);
        rows.Count(x => x!["version"]!.GetValue<string>() == "C").ShouldBe(27);
        evidence["failure"].ShouldBeNull();
        evidence["realDf"]!.GetValue<string>().ShouldContain("Filesystem");
        Console.WriteLine(output);
    }
}
