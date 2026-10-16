using System.Security.Cryptography;
using System.Text.Json;
using Antiphon.Checkpoints;
using Antiphon.Tests.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Unit")]
public sealed class HostCleanupCheckpointCompatibilityTests
{
    [Test]
    public void nested_live_executor_vetoes_whole_root()
    {
        var f = new SweepFixture();
        f.MarkRun(new ProcessIdentityProbe(f.Files.Context).Current());
        f.Files.Io.Put(f.Root + "/sentinel", "keep");
        f.ClearEvents();

        var receipt = f.Sweep.SweepOnce();
        receipt.Examined.ShouldBe(1, "C987.nested-live-reached");
        receipt.CompletedRoots.ShouldBe(0, "nested-live-retained");
        receipt.Skips.GetValueOrDefault("nested-identity-alive").ShouldBe(1, "C987.nested-live-guard");
        f.Files.Io.Text(f.Root + "/sentinel").ShouldBe("keep", "nested-live-retained");
        f.AssertGateAndNoDelete();
        f.Files.Io.Count("open-read", f.Run + "/executor-ownership.json").ShouldBeGreaterThan(0);

        // Removing only the live-owner veto must let this same candidate finish.
        f.MarkRun(f.Dead);
        f.Sweep.Register(f.Root);
        f.Sweep.SweepOnce().CompletedRoots.ShouldBe(1, "C987.nested-live-positive-pair");
        f.Files.Io.DirectoryExists(f.Root).ShouldBeFalse();
        f.Files.Io.RecursiveIntents.ShouldBe(0);
    }

    [Test]
    public void unknown_nested_custody_vetoes_whole_root()
    {
        var f = new SweepFixture();
        var sentinel = f.Run + "/tool/sentinel";
        f.Files.Io.Put(sentinel, "keep");
        f.ClearEvents();

        var receipt = f.Sweep.SweepOnce();
        receipt.Examined.ShouldBe(1, "C987.nested-missing-reached");
        receipt.CompletedRoots.ShouldBe(0, "nested-missing-retained");
        receipt.Skips.GetValueOrDefault("nested-custody-missing").ShouldBe(1, "C987.nested-missing-guard");
        f.Files.Io.Text(sentinel).ShouldBe("keep", "nested-missing-retained");
        f.AssertGateAndNoDelete();
        f.Files.Io.Count("file-exists", f.Run + "/ownership.json").ShouldBeGreaterThan(0);

        // Supply real serialized custody, never a canned deletion decision.
        f.MarkRun(f.Dead);
        f.Sweep.Register(f.Root);
        f.Sweep.SweepOnce().CompletedRoots.ShouldBe(1, "C987.nested-missing-positive-pair");
        f.Files.Io.DirectoryExists(f.Root).ShouldBeFalse();
        f.Files.Io.RecursiveIntents.ShouldBe(0);
    }

    private sealed class SweepFixture
    {
        public HostCleanupCheckpointFixture Files { get; } = new();
        public string Root { get; }
        public string Run => Root + "/custom-results/run";
        public ProcessIdentity Dead => new(999, 1, Files.Io.MachineName, Files.Io.BootId, Files.Io.PidNamespace);
        public CheckpointTempRootSweep Sweep { get; }

        public SweepFixture()
        {
            Root = Files.Parent + "/c723-" + Guid.NewGuid().ToString("N");
            Files.Io.CreateDirectory(Run + "/tool");
            TestRootGuard.Write(Root, new()
            {
                RootId = Path.GetFileName(Root)["c723-".Length..], RootPath = Root,
                AttemptId = "virtual-attempt", AssemblyInvocationId = "virtual-assembly",
                CreatedAt = Files.Clock.GetUtcNow().AddHours(-1), Owner = Dead
            }, Files.Context);
            var index = Files.Parent + "/.checkpoint-temp-roots";
            Files.Io.CreateDirectory(index);
            Files.Io.Put(index + "/" + Path.GetFileName(Root) + ".json", JsonSerializer.Serialize(Root));
            Sweep = new(Files.Context, Files.Parent, options: new() { Interval = TimeSpan.Zero });
        }

        public void MarkRun(ProcessIdentity executor)
        {
            RunOwnershipStore.Write(Run, new()
            {
                RunId = "run", RunDirectory = Run, Starter = Dead, Launched = executor, Phase = "launched"
            }, Files.Context);
            ExecutorOwnershipStore.Write(Run, executor, Files.Context);
        }

        public void ClearEvents() { Files.Io.Events.Clear(); Files.Io.Opens.Clear(); }

        public void AssertGateAndNoDelete()
        {
            Files.Io.Opens.ShouldContain(o => o.Path == Root + "/" + TestRootGuard.LockName
                && o.Share == FileShare.None, "C987.nested-guard-under-root-gate");
            Files.Io.Count("unlink").ShouldBe(0);
            Files.Io.Count("rmdir").ShouldBe(0);
            TestRootGuard.Read(Root, Files.Context)!.State.ShouldBe("active");
        }
    }

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
        foreach (var p in siblings)
        {
            var siblingRun = Path.GetDirectoryName(p)!;
            SafeWrite.Require(f.Files.Write.CreateDirectory(siblingRun, f.Files.Write.Under(f.Files.Parent)));
            SafeWrite.Require(f.Files.Write.CreateFreshDirectory(p, f.Files.Write.Under(siblingRun)));
            SafeWrite.Require(f.Files.Write.Text(p + "/sentinel", "keep", f.Files.Write.Under(p)));
            SafeWrite.Require(f.Files.Ledger.Seal(p));
            RunOwnershipStore.Write(siblingRun, new()
            {
                RunId = Path.GetFileName(siblingRun), RunDirectory = siblingRun,
                Starter = f.Dead, Launched = p == siblings[1] ? null : f.Dead,
                Phase = p == siblings[1] ? "launch-attempted" : "launched"
            }, f.Context);
        }
        // The dead sibling is deletable under this context: startup must not sweep it.
        f.Files.Ledger.Check(siblings[0]).ShouldBeNull();
        f.Cleanup().ObserveExecutor(Path.GetDirectoryName(siblings[0])!).Verdict.ShouldBe(ProcessVerdict.Dead);
        f.Receipt.ClearEvents();
        var run = CheckpointApp.CreateRun(HostCleanupToolFixture.Manifest(), new() { Branch = "virtual" }, f.Files.Parent, f.Runtime());
        f.Io.DirectoryExists(run).ShouldBeTrue();
        foreach (var p in siblings)
        {
            f.Io.DirectoryExists(p).ShouldBeTrue();
            f.Io.FileExists(p + "/sentinel").ShouldBeTrue();
            f.Io.Text(p + "/sentinel").ShouldBe("keep");
        }
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
        foreach (var (path, hash) in before)
        {
            f.Io.FileExists(path).ShouldBeTrue("preserved evidence: " + path);
            Hash(f.Io.Nodes[path].Bytes).ShouldBe(hash);
        }
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
            using var observing = new CancellationTokenSource();
            var timerTask = f.Files.Clock.WaitForTimerAsync(
                e => e.Action == "create" && e.DueTime == TimeSpan.FromMilliseconds(50 * (i + 1)), ct: observing.Token);
            var winner = await Task.WhenAny(task, timerTask);
            if (winner != timerTask)
            {
                observing.Cancel();
                try { await timerTask; } catch (OperationCanceledException) { }
                await task;
                (winner == timerTask).ShouldBeTrue(label + ": cleanup completed without scheduling the injected delay");
            }
            var timer = await timerTask;
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
        f.Io.DirectoryExists(f.Tool).ShouldBeFalse();
        f.Io.FileExists(f.Receipt.MarkdownPath).ShouldBeTrue();
        Hash(f.Io.Nodes[f.Receipt.MarkdownPath].Bytes).ShouldBe(hash);
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
