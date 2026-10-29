using System.Diagnostics;
using System.Text.RegularExpressions;
using Antiphon.LandGraphAudit;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class LandGraphEvaluationTests
{
    [Test]
    public void PerPassClocksApproveWhenEachPassUsesSeventyPercent()
    {
        var clock = new ManualPhaseClock();
        var output = new StringWriter();
        var pass = TimeSpan.FromSeconds(10);
        var timeouts = 0;
        var code = AuditPhases.Run("Antiphon.sln", output, clock, pass, pass,
            () => { clock.Advance(TimeSpan.FromSeconds(7)); return 0; },
            () => { clock.Advance(TimeSpan.FromSeconds(7)); return 0; },
            () => { clock.Advance(TimeSpan.FromSeconds(7)); return 0; },
            () => timeouts++);
        code.ShouldBe(0);
        timeouts.ShouldBe(0);
        ApprovedLines(output).ShouldBe(1);
    }

    [Test]
    public void Pass1TimeoutExits2AndNamesThePass()
    {
        var observed = RunPhaseTimeout(pass1: TimeSpan.FromSeconds(11), copy: TimeSpan.FromSeconds(1), pass2: TimeSpan.FromSeconds(1));
        observed.Code.ShouldBe(2);
        observed.Text.ShouldContain("evaluation-failed: evaluating Antiphon.sln pass 1 exceeded 10 s", Case.Sensitive);
        observed.Text.ShouldNotContain("approved", Case.Sensitive);
        observed.Text.ShouldNotContain("pass 2", Case.Sensitive);
        observed.Timeouts.ShouldBe(1);
        observed.Pass2Ran.ShouldBeFalse();
    }

    [Test]
    public void Pass2TimeoutExits2NamesThePassAndRemovesCanary()
    {
        var observed = RunPhaseTimeout(pass1: TimeSpan.FromSeconds(1), copy: TimeSpan.FromSeconds(1), pass2: TimeSpan.FromSeconds(11));
        observed.Code.ShouldBe(2);
        observed.Text.ShouldContain("evaluation-failed: evaluating Antiphon.sln pass 2 exceeded 10 s", Case.Sensitive);
        observed.Text.ShouldNotContain("approved", Case.Sensitive);
        observed.Timeouts.ShouldBe(1);
        observed.Pass2Ran.ShouldBeTrue();
    }

    [Test]
    public void CopyTimeoutExits2NamesTheCopyAndRemovesCanary()
    {
        var observed = RunPhaseTimeout(pass1: TimeSpan.FromSeconds(1), copy: TimeSpan.FromSeconds(11), pass2: TimeSpan.FromSeconds(1));
        observed.Code.ShouldBe(2);
        observed.Text.ShouldContain("evaluation-failed: evaluating Antiphon.sln copy exceeded 10 s", Case.Sensitive);
        observed.Text.ShouldNotContain("approved", Case.Sensitive);
        observed.Timeouts.ShouldBe(1);
        observed.Pass2Ran.ShouldBeFalse();
    }

    [Test]
    public void RefusingEvaluatorIsNotApproved()
    {
        var clock = new ManualPhaseClock();
        var output = new StringWriter();
        var timeouts = 0;
        var code = AuditPhases.Run("Antiphon.sln", output, clock, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10),
            () => 1, () => 0, () => 0, () => timeouts++);
        code.ShouldBe(1);
        timeouts.ShouldBe(0);
        output.ToString().ShouldNotContain("approved", Case.Sensitive);
    }

    [Test]
    [Arguments("--witness-clocks")]
    [Arguments("--pass-budget-ms")]
    [Arguments("--copy-budget-ms")]
    [Arguments("--hold-pass1-ms")]
    [Arguments("--hold-copy-ms")]
    [Arguments("--hold-pass2-ms")]
    [Arguments("--touch-canary")]
    [Timeout(60_000)]
    public async Task RemovedWitnessArgumentsCannotApprove(string flag)
    {
        var dll = ToolDll();
        File.Exists(dll).ShouldBeTrue(dll);
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(dll);
        psi.ArgumentList.Add(flag);
        using var process = Process.Start(psi)!;
        var text = await process.StandardOutput.ReadToEndAsync() + await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        process.ExitCode.ShouldBe(2, flag + ": " + text);
        text.ShouldContain("unknown or incomplete argument", Case.Sensitive);
        text.ShouldNotContain("approved", Case.Sensitive);
    }

    [Test]
    public async Task ProductionSurfaceHasNoWitnessClock()
    {
        var root = RepoRoot();
        var files = Directory.GetFiles(Path.Combine(root, "tools", "Antiphon.LandGraphAudit"), "*.cs")
            .Concat(Directory.GetFiles(Path.Combine(root, "tools", "Antiphon.LandGraphAudit.Core"), "*.cs"))
            .Append(Path.Combine(root, "scripts", "audit-land-verification-graph.ps1"));
        foreach (var file in files)
        {
            var text = await File.ReadAllTextAsync(file);
            text.ShouldNotContain("WitnessClock", Case.Sensitive, file);
            text.ShouldNotContain("witness-clocks", Case.Sensitive, file);
            text.ShouldNotContain("--touch-canary", Case.Sensitive, file);
            text.ShouldNotContain("--pass-budget-ms", Case.Sensitive, file);
            text.ShouldNotContain("--copy-budget-ms", Case.Sensitive, file);
            text.ShouldNotContain("--hold-pass1-ms", Case.Sensitive, file);
            text.ShouldNotContain("--hold-copy-ms", Case.Sensitive, file);
            text.ShouldNotContain("--hold-pass2-ms", Case.Sensitive, file);
        }

        var names = new SortedSet<string>(StringComparer.Ordinal);
        var pattern = new Regex(@"GetEnvironmentVariable\(\s*""([^""]+)""\s*\)", RegexOptions.CultureInvariant);
        foreach (var file in Directory.GetFiles(Path.Combine(root, "tools", "Antiphon.LandGraphAudit"), "*.cs"))
        {
            foreach (Match match in pattern.Matches(await File.ReadAllTextAsync(file)))
                names.Add(match.Groups[1].Value);
        }

        names.Count.ShouldBe(1);
        names.Single().ShouldBe("NUGET_PACKAGES");
    }

    private static int ApprovedLines(StringWriter output) =>
        output.ToString().Split('\n').Count(line => line.Trim() == "approved");

    private static PhaseTimeout RunPhaseTimeout(TimeSpan pass1, TimeSpan copy, TimeSpan pass2)
    {
        var clock = new ManualPhaseClock();
        var output = new StringWriter();
        var budget = TimeSpan.FromSeconds(10);
        var timeouts = 0;
        var pass2Ran = false;
        var code = AuditPhases.Run("Antiphon.sln", output, clock, budget, budget,
            () => { clock.Advance(pass1); return 0; },
            () => { clock.Advance(copy); return 0; },
            () => { pass2Ran = true; clock.Advance(pass2); return 0; },
            () => timeouts++);
        return new PhaseTimeout(code, output.ToString(), timeouts, pass2Ran);
    }

    private readonly record struct PhaseTimeout(int Code, string Text, int Timeouts, bool Pass2Ran);

    /// <summary>Elapsed time moves only when the fake evaluator advances it. This clock cannot approve.</summary>
    private sealed class ManualPhaseClock : IPhaseClock
    {
        private TimeSpan _elapsed;

        public void Advance(TimeSpan by) => _elapsed += by;

        public bool TryComplete(TimeSpan budget, Action work)
        {
            var start = _elapsed;
            work();
            return _elapsed - start <= budget;
        }
    }
}
