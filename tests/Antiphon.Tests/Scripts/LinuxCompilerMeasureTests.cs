using System.Collections.Specialized;
using System.Diagnostics;
using System.IO.Abstractions;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1885: measure-linux-shared-compiler (Antiphon.Nightly.LinuxCompilerMeasure), which replaced
/// scripts/measure-linux-shared-compiler.ps1 and scripts/lib/linux-compiler-evidence.ps1 in unit 34 of
/// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md. The measurement itself needs native Linux, /proc and a
/// private compiler server; these rows pin what runs anywhere: the options (usage errors), the refusals before any build
/// (the platform first, then the grant, the bounded inventory, the project, the identity and the cache root), the CPU list
/// reader and the full-minute core count. Nothing is built and no process starts.
/// </summary>
[Category("Unit")]
public sealed class LinuxCompilerMeasureTests
{
    private sealed class NoToolset : ILinuxCompilerToolset
    {
        public string Prepare(ProcessStartInfo start, int grant, int idleSeconds, string isolation, bool capped) => throw new InvalidOperationException("no toolset in a refusal");
        public string Probe(string executable, string[] arguments, string workingDirectory, StringDictionary environment) => throw new InvalidOperationException("no probe in a refusal");
        public string StatusValue(string path, string key) => throw new InvalidOperationException("no /proc in a refusal");
    }

    private static LinuxCompilerMeasureSeams Seams(bool linux) =>
        new() { IsLinux = linux, Toolset = new NoToolset(), Files = new FileSystem(), FindTool = _ => null };

    private static LinuxCompilerMeasureOptions Options(string worktree, params string[] arguments) =>
        LinuxCompilerMeasureOptions.Parse(arguments, worktree, "3");

    [Test]
    public void Usage_errors_name_the_flag_and_choices_keep_their_spelling()
    {
        foreach (var (arguments, message) in new (string[], string)[]
                 {
                     ([], "missing --sample"), (["--mode", "Baseline", "--phase", "Prime"], "missing --sample"),
                     (["--phase", "Prime", "--sample", "0"], "missing --mode"), (["--mode", "Baseline", "--sample", "0"], "missing --phase"),
                     (["--mode", "Fast", "--phase", "Prime", "--sample", "0"], "--mode expects Baseline or Candidate"),
                     (["--mode", "Baseline", "--phase", "Warm", "--sample", "0"], "--phase expects Restore or Prime or Measured or Concurrent"),
                     (["--mode", "Baseline", "--phase", "Prime", "--sample", "6"], "--sample expects 0 to 5"),
                     (["--mode", "Baseline", "--phase", "Prime", "--sample", "-1"], "--sample expects 0 to 5"),
                     (["--mode", "Baseline", "--phase", "Prime", "--sample", "one"], "--sample expects 0 to 5"),
                     (["-Mode", "Baseline"], "unexpected argument"),
                 })
            Should.Throw<NightlyUsageException>(() => LinuxCompilerMeasureOptions.Parse(arguments, "/w", "3")).Message.ShouldStartWith(message);
        var options = LinuxCompilerMeasureOptions.Parse(["--mode", "candidate", "--phase", "CONCURRENT", "--sample", "4", "--compiler-cache-root", "/c"], "/w", "2");
        (options.Mode, options.Phase, options.Sample, options.Worktree, options.EvidenceRoot, options.Project, options.ExperimentId, options.CompilerCacheRoot, options.Grant)
            .ShouldBe(("Candidate", "Concurrent", 4, "/w", "", "Antiphon.sln", "measurement-58c3a29f", "/c", "2"));
        LinuxCompilerMeasureOptions.Parse(["--mode", "Baseline", "--phase", "Prime", "--sample", "0", "--worktree", "/elsewhere"], "/w", "").Worktree.ShouldBe("/elsewhere");
    }

    [Test]
    public void Refusals_come_before_any_build_with_the_platform_first()
    {
        using var fixture = NightlyFixture.Create("c1885-measure");
        File.WriteAllText(fixture.PathOf("Antiphon.sln"), "");
        var good = Options(fixture.Root, "--mode", "Candidate", "--phase", "Measured", "--sample", "2");
        LinuxCompilerMeasure.Refusal(good, Seams(linux: false), out _).ShouldBe("Linux native measurement required");
        LinuxCompilerMeasure.Refusal(good, Seams(linux: true), out var grant).ShouldBeNull();
        grant.ShouldBe(3);

        LinuxCompilerMeasure.Refusal(LinuxCompilerMeasureOptions.Parse(["--mode", "Baseline", "--phase", "Prime", "--sample", "0"], fixture.Root, ""), Seams(true), out _)
            .ShouldBe("one outer lease and its typed CPU grant are required");
        LinuxCompilerMeasure.Refusal(LinuxCompilerMeasureOptions.Parse(["--mode", "Baseline", "--phase", "Prime", "--sample", "0"], fixture.Root, "0"), Seams(true), out _)
            .ShouldBe("one outer lease and its typed CPU grant are required");
        foreach (var arguments in new[]
                 {
                     new[] { "--mode", "Baseline", "--phase", "Restore", "--sample", "1" }, new[] { "--mode", "Baseline", "--phase", "Prime", "--sample", "3" },
                     new[] { "--mode", "Baseline", "--phase", "Measured", "--sample", "0" }, new[] { "--mode", "Baseline", "--phase", "Measured", "--sample", "4" },
                     new[] { "--mode", "Baseline", "--phase", "Concurrent", "--sample", "4" }, new[] { "--mode", "Candidate", "--phase", "Concurrent", "--sample", "3" },
                 })
            LinuxCompilerMeasure.Refusal(Options(fixture.Root, arguments), Seams(true), out _).ShouldBe("invocation is outside the bounded experiment inventory", string.Join(" ", arguments));
        LinuxCompilerMeasure.Refusal(Options(fixture.Root, "--mode", "Baseline", "--phase", "Prime", "--sample", "0", "--project", "Missing.sln"), Seams(true), out _)
            .ShouldBe("measurement project does not exist");
        LinuxCompilerMeasure.Refusal(Options(fixture.Root, "--mode", "Baseline", "--phase", "Prime", "--sample", "0", "--experiment-id", "a/b"), Seams(true), out _)
            .ShouldBe("invalid private experiment identity");
        LinuxCompilerMeasure.Refusal(Options(fixture.Root, "--mode", "Baseline", "--phase", "Prime", "--sample", "0", "--compiler-cache-root", "relative"), Seams(true), out _)
            .ShouldBe("compiler cache root must be absolute");

        // The command answers a refusal with one line and exit 1, before git or any build.
        var git = new FakeGit();
        LinuxCompilerMeasure.Run(good, fixture.Host(git), Seams(linux: false)).ShouldBe(1);
        fixture.Error.ToString().Trim().ShouldBe("measure-linux-shared-compiler: Linux native measurement required");
        git.Calls.ShouldBeEmpty();
    }

    [Test]
    public void The_cpu_lists_and_the_full_minute_core_count_read_as_the_script_did()
    {
        LinuxCompilerMeasure.CpuSet("0-3,8,10-11").OrderBy(cpu => cpu).ShouldBe([0, 1, 2, 3, 8, 10, 11]);
        LinuxCompilerMeasure.CpuSet("").ShouldBeEmpty();
        LinuxCompilerMeasure.CpuSet("5").ShouldBe([5]);

        static LinuxCompilerSample Sample(double seconds, long ticks) => new(seconds, ticks, 0, [], 0);
        LinuxCompilerMeasure.MaxFullMinuteCores([Sample(0, 0), Sample(30, 100), Sample(59, 200)], 100).ShouldBeNull("no pair spans a minute");
        // 100 ticks per second is one core. Each sample pairs with the latest earlier one at least a minute back: 60 s pairs
        // with 0 s (12000 ticks, 2 cores), 61 s with 1 s (12050 ticks, 2.0083 cores); the highest counts.
        LinuxCompilerMeasure.MaxFullMinuteCores([Sample(0, 0), Sample(1, 50), Sample(60, 12000), Sample(61, 12100)], 100)!.Value.ShouldBe(12050.0 / 6000, 0.0001);
        LinuxCompilerMeasure.MaxFullMinuteCores([Sample(0, 0), Sample(10, 0), Sample(70, 24000)], 100)!.Value.ShouldBe(4.0, 0.0001);
    }
}
