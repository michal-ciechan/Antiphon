using System.Text.Json;
using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;
using Antiphon.TestSupport;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.SessionRunner.Tests;

[Category("Integration")]
[Category("Slow")]
[NativeLinuxOnly("CARD-1619 requires an owned native cgroup-v1 fixture")]
[NotInParallel("LinuxCustody")]
[ParallelLimiter<ProcessSpawnLimit>]
public class Card1619LinuxCustodyStopIntentTests
{
    private static void RequireV1()
    {
        LinuxRunnerCustodyLifecycleTests.RequireNativeLifecycle();
        if (File.Exists("/sys/fs/cgroup/cgroup.controllers") || !Directory.Exists("/sys/fs/cgroup/pids/antiphon-custody")
            || !Directory.Exists("/sys/fs/cgroup/freezer/antiphon-custody"))
            throw new SkipTestException("Requires delegated cgroup-v1 pids/freezer roots; native-pending.");
    }

    [Test]
    public async Task Explicit_release_records_signal_without_fabricating_custody()
    {
        RequireV1();
        await using var f = await LinuxRunnerCustodyLifecycleTests.NativeHold.StartAsync();
        await f.WaitForDescendantAsync();
        await f.WaitForRootExitAsync();
        var initial = await f.Runtime.ReadCustodyAsync(f.Binding, false, default);
        initial.State.ShouldBe(VerificationCustodyState.Draining);
        initial.Receipt.ShouldBeNull();
        var ns = f.Store.ReadRecord<LinuxNamespaceRecord>(f.Binding, "linux-namespace.json")!;
        var procs = Antiphon.Agents.Pty.LinuxCgroupContainment.ExecutionProcsPath(ns.NamespaceId, ns.ContainerId);
        File.ReadAllLines(procs).ShouldContain(f.DescendantPid.ToString());
        var result = await f.Runtime.ReadCustodyAsync(f.Binding, true, default);
        Directory.Exists("/proc/" + f.DescendantPid).ShouldBeFalse();
        File.ReadAllText(procs).Trim().ShouldBeEmpty();
        result.Receipt.ShouldNotBeNull();
        result.Receipt.ActiveProcesses.ShouldBe(0u);
        result.Receipt.TerminationSucceeded.ShouldBe(true);
        // The observed root exited normally before release; helper success cannot rewrite its wait.
        f.Runtime.Get(f.Binding.Generation.SessionId).ExitSignal.ShouldBe(0);
        var dir = Path.Combine(f.RootPath, "logs", "pty-hosts", "stop-intents");
        var deadline = DateTime.UtcNow.AddSeconds(1);
        while (!Directory.Exists(dir) && DateTime.UtcNow < deadline) await Task.Delay(10);
        var records = Directory.GetFiles(dir, "*.json").Select(path => JsonSerializer.Deserialize<ProcessStopIntentDto>(
            File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))!).ToArray();
        records.ShouldContain(x => x.Target == ProcessStopTarget.Custody && x.AttemptedSignal == 9
            && x.Outcome == ProcessStopOutcome.Issued && x.AcceptedStartedAt == f.Binding.Generation.AcceptedStartedAt);
    }

    [Test]
    public async Task Dead_host_recovery_records_intent_and_remains_unknown()
    {
        RequireV1();
        await using var f = await LinuxRunnerCustodyLifecycleTests.NativeHold.StartAsync();
        await f.WaitForDescendantAsync();
        f.KillHost();
        Directory.Exists("/proc/" + f.DescendantPid).ShouldBeTrue();
        var result = await f.Runtime.ReadCustodyAsync(f.Binding, true, default);
        result.State.ShouldBe(VerificationCustodyState.Unknown);
        result.Receipt.ShouldBeNull();
        Directory.Exists("/proc/" + f.DescendantPid).ShouldBeFalse();
        var intent = f.Runtime.CustodyForTests.StopIntents!.Snapshot(f.Binding.Generation.SessionId,
            f.Binding.Generation.AcceptedStartedAt, 9);
        intent.ShouldNotBeNull();
        intent.Target.ShouldBe(ProcessStopTarget.Custody);
        intent.Outcome.ShouldBe(ProcessStopOutcome.Issued);
        File.Exists(f.Store.PathFor(f.Binding.ExecutionId, "producer-receipt.json")).ShouldBeFalse();
    }
}
