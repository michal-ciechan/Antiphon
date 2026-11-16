using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using Antiphon.TestSupport;

namespace Antiphon.Tests.Scripts;

[NativeLinuxOnly("C994/C1008 real Docker lifecycle qualification requires native Linux inside its owned daemon (POSIX paths and process groups).")]
[Category("Integration")]
[Category("Slow")]
public sealed class RollingVolumeRecycleDockerTests
{
    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public Task C1008_Real_docker_comparison() => RunShard(1);

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public Task C1008_Real_docker_comparison_part_2() => RunShard(2);

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public Task C1008_Real_docker_comparison_part_3() => RunShard(3);

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public Task C1008_Real_docker_comparison_part_4() => RunShard(4);

    private static async Task RunShard(int shard)
    {
        NativeLinuxGate.Require("C994/C1008 real Docker lifecycle qualification requires native Linux inside its owned daemon (POSIX paths and process groups).");
        var psi = new ProcessStartInfo("node") {
            WorkingDirectory = DelegateScriptRunner.RepoRoot,
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        };
        psi.ArgumentList.Add(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/fixtures/c1008-recycle-real-cases.mjs"));
        psi.ArgumentList.Add("--shard");
        psi.ArgumentList.Add(shard.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var child = Process.Start(psi)!;
        var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
        // CARD-1955: split the former 618/709/827s row into four closed pieces.
        // Each child gets 8 minutes plus at most 1 minute of owned cleanup.
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(8));
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
        int[] totals = [0, 12, 8, 7, 7], baselines = [0, 4, 0, 0, 1];
        var total = totals[shard]; var baseline = baselines[shard];
        var match = Regex.Match(output, $@"C1008_REAL cases={total} base={baseline} changed={total-baseline} failures=0 cleanup=absent evidence=(.+)");
        match.Success.ShouldBeTrue("recycle-real-comparison: exact shard outcomes and checked cleanup; " + output);
        var evidence = JsonNode.Parse(File.ReadAllText(match.Groups[1].Value.Trim()))!;
        var rows = evidence["results"]!.AsArray();
        rows.Count.ShouldBe(total);
        rows.Select(x => x!["name"]!.GetValue<string>()).Distinct().Count().ShouldBe(total);
        rows.Count(x => x!["version"]!.GetValue<string>() == "B").ShouldBe(baseline);
        rows.Count(x => x!["version"]!.GetValue<string>() == "C").ShouldBe(total-baseline);
        if (shard == 4)
        {
            rows.ShouldContain(x => x!["name"]!.GetValue<string>() == "C-previous-generation-main" && x["accepted"]!.GetValue<bool>());
            rows.ShouldContain(x => x!["name"]!.GetValue<string>() == "C-previous-generation-foreign-mount"
                && x["diagnosis"]!.GetValue<string>() == "RecycleContainerStateUnknown"
                && x["accepted"]!.GetValue<bool>() == false);
        }
        evidence["failure"].ShouldBeNull();
        evidence["realDf"]!.GetValue<string>().ShouldContain("Filesystem");
        Console.WriteLine(output);
    }
}
