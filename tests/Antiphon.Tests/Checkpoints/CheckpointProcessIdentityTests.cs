using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class CheckpointProcessIdentityTests : CheckpointTestBase
{
    [Test]
    public void same_live_generation_is_a_veto()
    {
        var probe = new ProcessIdentityProbe();
        var first = probe.Current();
        for (var i = 0; i < 32; i++)
        {
            var repeat = probe.Capture(Environment.ProcessId);
            repeat.StartUtcTicks.ShouldBe(first.StartUtcTicks);
            probe.Observe(repeat).Verdict.ShouldBe(ProcessVerdict.AliveSame);
        }
        var run = Run(alive: true);
        var receipt = new ToolCopyCleanup().Remove(run);
        receipt.Outcome.ShouldBe("Retained");
        receipt.Reason.ShouldBe("identity-alive");
        Directory.Exists(Path.Combine(run, "tool")).ShouldBeTrue();
    }

    [Test]
    public void unknown_probe_is_a_veto()
    {
        var run = Run(alive: false);
        var receipt = new ToolCopyCleanup(new ScriptedProbe(ProcessVerdict.Unknown)).Remove(run);
        receipt.Outcome.ShouldBe("Retained");
        receipt.Reason.ShouldBe("identity-unknown");
        Directory.Exists(Path.Combine(run, "tool")).ShouldBeTrue();
    }

    [Test]
    public void reused_pid_is_retained_and_never_signaled()
    {
        var run = Run(alive: false);
        var receipt = new ToolCopyCleanup(new ScriptedProbe(ProcessVerdict.ReusedPid)).Remove(run);
        receipt.Outcome.ShouldBe("Retained");
        receipt.Reason.ShouldBe("identity-reused");
        Directory.Exists(Path.Combine(run, "tool")).ShouldBeTrue();
    }

    [Test]
    public void foreign_host_is_not_local_dead() => Foreign(identity => identity with { Host = "foreign-host" });

    [Test]
    public void foreign_boot_is_not_local_dead() => Foreign(identity => identity with { Boot = "foreign-boot" });

    [Test]
    public void foreign_pid_namespace_is_not_local_dead() => Foreign(identity => identity with { PidNamespace = "foreign-namespace" });

    [Test]
    public void confirmed_dead_local_identity_permits_cleanup()
    {
        var run = Run(alive: false);
        var evidence = Path.Combine(run, "report.md");
        File.WriteAllText(evidence, "report-sentinel");
        var receipt = new ToolCopyCleanup().Remove(run);
        receipt.Outcome.ShouldBe("Removed");
        Directory.Exists(Path.Combine(run, "tool")).ShouldBeFalse();
        File.ReadAllText(evidence).ShouldBe("report-sentinel");
    }

    private string Run(bool alive)
    {
        var run = TempDir();
        CheckpointFixtures.MarkRun(run, alive);
        var tool = Path.Combine(run, "tool");
        Directory.CreateDirectory(tool);
        File.WriteAllText(Path.Combine(tool, "sentinel.dll"), "keep");
        return run;
    }

    private void Foreign(Func<ProcessIdentity, ProcessIdentity> change)
    {
        var run = Run(alive: false);
        try
        {
            var record = RunOwnershipStore.Read(run)!;
            record.Launched = change(record.Launched!);
            RunOwnershipStore.Write(run, record);
            var receipt = new ToolCopyCleanup().Remove(run);
            receipt.Outcome.ShouldBe("Retained");
            receipt.Reason.ShouldBe("identity-foreign");
            Directory.Exists(Path.Combine(run, "tool")).ShouldBeTrue();
        }
        finally { File.Delete(Path.Combine(run, RunOwnershipStore.FileName)); }
    }

    private sealed class ScriptedProbe(ProcessVerdict verdict) : ProcessIdentityProbe
    {
        public override ProcessObservation Observe(ProcessIdentity? expected) => new(verdict, verdict switch
        {
            ProcessVerdict.Unknown => "identity-unknown",
            ProcessVerdict.ReusedPid => "identity-reused",
            _ => "identity-alive",
        });
    }
}
