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
public sealed class RetiredTempContainerDockerTests
{
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task C994_Real_retired_temp_lifecycle()
    {
        var psi = new ProcessStartInfo("node") {
            WorkingDirectory = DelegateScriptRunner.RepoRoot,
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        };
        psi.ArgumentList.Add(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/fixtures/c994-retired-temp-real-cases.mjs"));
        using var child = Process.Start(psi)!;
        var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(12));
        try { await child.WaitForExitAsync(deadline.Token); }
        catch {
            if (!child.HasExited) { child.Kill(true); await child.WaitForExitAsync(); }
            var captured = await stdout;
            var ownedRoot = Regex.Match(captured, @"(?m)^C994_REAL_ROOT=(/tmp/c994-real-[A-Za-z0-9]+)$");
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
        child.ExitCode.ShouldBe(0, "c994-real-effects: real Docker/Git outcomes; " + output);
        var match = Regex.Match(output, @"C994_REAL cases=10 base=1 changed=9 failures=0 cleanup=absent evidence=(.+)");
        match.Success.ShouldBeTrue("c994-real-effects: 10 outcomes and checked cleanup; " + output);
        var evidence = JsonNode.Parse(File.ReadAllText(match.Groups[1].Value.Trim()))!;
        var rows = evidence["results"]!.AsArray();
        rows.Count.ShouldBe(10);
        rows.Select(x => x!["name"]!.GetValue<string>()).Distinct().Count().ShouldBe(10);
        rows.Count(x => x!["version"]!.GetValue<string>() == "B").ShouldBe(1);
        rows.Count(x => x!["version"]!.GetValue<string>() == "C").ShouldBe(9);
        rows.Select(x => x!["name"]!.GetValue<string>()).ShouldBe(Enumerable.Range(1,10).Select(i=>"RD-"+i), "c994-real-effects: exact RD roster");
        evidence["failure"].ShouldBeNull();
        evidence["realDf"]!.GetValue<string>().ShouldContain("Filesystem");
        Console.WriteLine(output);
    }
}
