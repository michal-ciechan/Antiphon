using System.Security.Cryptography;
using System.Text.Json;
using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Unit")]
public sealed class HostCleanupCheckpointCompatibilityTests
{
    [Test]
    public async Task wait_uses_custody_not_progress_pid()
    {
        var f = new HostCleanupToolFixture();
        f.Owner.Phase = "launch-attempted"; f.Owner.Launched = null;
        RunOwnershipStore.Write(f.Run, f.Owner, f.Context);
        f.Receipt.State.ExecutorPid = int.MaxValue;
        f.Io.Put(f.Receipt.StatePath, JsonSerializer.Serialize(f.Receipt.State, RunStateStore.Json));
        (await f.Wait()).ShouldBe(75);
        f.Io.DirectoryExists(f.Tool).ShouldBeTrue();
        f.Io.Text(f.Receipt.MarkdownPath).ShouldContain("GREEN");
    }

    [Test]
    public void next_start_preserves_all_pre_existing_sibling_tools()
    {
        var f = new HostCleanupToolFixture();
        var siblings = new[] { f.Files.Parent + "/.antiphon/checkpoints/dead/tool", f.Files.Parent + "/.antiphon/checkpoints/unknown/tool", f.Files.Parent + "/other/tool" };
        foreach (var p in siblings) { f.Io.CreateDirectory(p); f.Io.Put(p + "/sentinel", "keep"); }
        var run = CheckpointApp.CreateRun(HostCleanupToolFixture.Manifest(), new() { Branch = "virtual" }, f.Files.Parent, f.Runtime());
        f.Io.DirectoryExists(run).ShouldBeTrue();
        foreach (var p in siblings) f.Io.Text(p + "/sentinel").ShouldBe("keep");
        f.Io.Count("unlink").ShouldBe(0); f.Io.Count("rmdir").ShouldBe(0);
    }

    [Test]
    public async Task stop_validates_generation_before_signaling()
    {
        var f = new HostCleanupToolFixture(); var control = new CountingControl();
        f.Owner.Launched = new ProcessIdentityProbe(f.Context).Current() with { StartUtcTicks = 1 };
        RunOwnershipStore.Write(f.Run, f.Owner, f.Context);
        (await Antiphon.Checkpoints.Program.RunAsync(["stop", "--run", f.Run, "--repo-root", f.Files.Parent], f.Runtime(control))).ShouldBe(75);
        control.Calls.ShouldBe(0); f.Io.DirectoryExists(f.Tool).ShouldBeTrue();
    }

    [Test]
    public async Task stop_waits_for_confirmed_exit()
    {
        var f = new HostCleanupToolFixture(); var control = new CountingControl();
        f.Owner.Launched = new ProcessIdentityProbe(f.Context).Current();
        RunOwnershipStore.Write(f.Run, f.Owner, f.Context);
        f.Receipt.State.Phase = "running";
        f.Io.Put(f.Receipt.StatePath, JsonSerializer.Serialize(f.Receipt.State, RunStateStore.Json));
        (await Antiphon.Checkpoints.Program.RunAsync(["stop", "--run", f.Run, "--repo-root", f.Files.Parent], f.Runtime(control))).ShouldBe(75);
        control.Calls.ShouldBe(1);
        new RunStateStore(f.Context).TryRead(f.Receipt.StatePath)!.Phase.ShouldBe("running");
        f.Io.DirectoryExists(f.Tool).ShouldBeTrue();
    }

    [Test]
    public void age_clean_cannot_bypass_live_custody()
    {
        var f = new HostCleanupToolFixture(); var results = f.Files.Root + "/results";
        f.Io.CreateDirectory(results);
        var identities = new[] { f.Dead, new ProcessIdentityProbe(f.Context).Current(), new ProcessIdentityProbe(f.Context).Current() with { StartUtcTicks = 1 }, f.Dead with { Host = "foreign" } };
        for (var i = 0; i < identities.Length; i++)
        {
            var run = results + "/run" + i; f.Io.CreateDirectory(run); f.Io.Put(run + "/sentinel", "keep");
            RunOwnershipStore.Write(run, new() { RunId = Path.GetFileName(run), RunDirectory = run, Starter = f.Dead, Launched = identities[i], Phase = "launched" }, f.Context);
            f.Io.Nodes[run].Stat = f.Io.Nodes[run].Stat with { Modified = (f.Files.Clock.GetUtcNow().AddDays(-8).UtcTicks - DateTime.UnixEpoch.Ticks) * 100 };
        }
        var result = new CheckpointOutputCleanup(f.Context).RemoveOlderRuns(results, TimeSpan.FromDays(7), false);
        result.Deleted.ShouldBeEmpty(); result.Refusals.ShouldContain("output-ledger-missing");
        for (var i = 0; i < identities.Length; i++) f.Io.Text(results + "/run" + i + "/sentinel").ShouldBe("keep");
    }

    [Test]
    public void evidence_write_has_no_raw_delete_bypass()
    {
        var f = new HostCleanupToolFixture(); f.Io.Nodes.Remove(f.Run + "/ownership.json");
        EvidenceFolder.Write(f.Run, new() { RunId = Path.GetFileName(f.Run) }, true, context: f.Context);
        f.Io.DirectoryExists(f.Tool).ShouldBeTrue(); f.Io.Text(f.Payload).ShouldBe("keep");
    }

    [Test]
    public void automatic_recovery_preserves_all_evidence()
    {
        var f = new HostCleanupToolFixture();
        foreach (var n in new[] { "request.json", "manifest.resolved.yaml", "run.trx", "executor.log" }) f.Io.Put(f.Run + "/" + n, "evidence-" + n);
        var before = f.Io.Nodes.Where(p => Path.GetDirectoryName(p.Key) == f.Run && p.Value.Stat.Kind == "file").ToDictionary(p => p.Key, p => Hash(p.Value.Bytes));
        f.Cleanup().Remove(f.Run).Outcome.ShouldBe("Removed");
        f.Io.DirectoryExists(f.Tool).ShouldBeFalse();
        foreach (var (path, hash) in before) Hash(f.Io.Nodes[path].Bytes).ShouldBe(hash);
    }

    [Test]
    public void sweep_never_signals_a_process()
    {
        var f = new HostCleanupToolFixture();
        f.Owner.Launched = new ProcessIdentityProbe(f.Context).Current();
        RunOwnershipStore.Write(f.Run, f.Owner, f.Context);
        f.Cleanup().Sweep(Path.GetDirectoryName(f.Run)!).ShouldBe(0);
        HostCleanupCheckpointFixture.AssertNoSignalCalls(typeof(ToolCopyCleanup));
        f.Io.Processes[f.Io.CurrentProcessId].HasExited.ShouldBeFalse();
        f.Io.DirectoryExists(f.Tool).ShouldBeTrue();
        f.Io.Count("process", f.Io.CurrentProcessId.ToString()).ShouldBeGreaterThan(0);
    }

    [Test]
    public Task failed_delete_receipt_is_truthful_and_retryable() => RetryDelayProof("C987.failed_delete_receipt_is_truthful_and_retryable.retry");

    internal static async Task RetryDelayProof(string label)
    {
        var f = new HostCleanupToolFixture();
        var attempts = 0;
        var task = Task.Run(() => f.Cleanup(beforeDelete: _ => { Interlocked.Increment(ref attempts); throw new IOException("delete-denied"); }).Remove(f.Run));
        for (var i = 0; i < 9; i++)
        {
            var timer = await f.Files.Clock.WaitForTimerAsync(e => e.Action == "create" && e.DueTime == TimeSpan.FromMilliseconds(50 * (i + 1)));
            f.Files.Clock.Advance(timer.DueTime);
        }
        (await task).Outcome.ShouldBe("Failed", label); attempts.ShouldBe(10, label);
        f.Io.DirectoryExists(f.Tool).ShouldBeTrue();
        f.Cleanup().Remove(f.Run).Outcome.ShouldBe("Removed"); f.Io.DirectoryExists(f.Tool).ShouldBeFalse();
    }

    [Test]
    [Arguments(0, false)] [Arguments(0, true)] [Arguments(1, false)] [Arguments(1, true)]
    public async Task completed_wait_removes_only_tool(int exitCode, bool keepOutputs)
    {
        var f = new HostCleanupToolFixture(exitCode);
        f.Io.Put(f.Run + "/request.json", JsonSerializer.Serialize(new RunRequest { KeepOutputs = keepOutputs }));
        var hash = Hash(f.Io.Nodes[f.Receipt.MarkdownPath].Bytes);
        (await f.Wait()).ShouldBe(exitCode);
        f.Io.DirectoryExists(f.Tool).ShouldBeFalse(); Hash(f.Io.Nodes[f.Receipt.MarkdownPath].Bytes).ShouldBe(hash);
        f.Io.DirectoryExists(f.Receipt.Output).ShouldBeTrue();
    }

    [Test]
    public void already_absent_is_idempotent()
    {
        var f = new HostCleanupToolFixture();
        f.Cleanup().Remove(f.Run).Outcome.ShouldBe("Removed");
        f.Cleanup().Remove(f.Run).Outcome.ShouldBe("AlreadyAbsent");
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    internal sealed class CountingControl : IProcessControl
    {
        public int Calls { get; private set; }
        public bool StopAndWait(ProcessIdentity identity, TimeSpan timeout) { Calls++; return false; }
    }
}
