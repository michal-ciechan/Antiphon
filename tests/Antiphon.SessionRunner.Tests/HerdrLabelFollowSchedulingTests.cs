using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public class HerdrLabelFollowSchedulingTests
{
    [Test]
    public async Task Concurrent_triggers_share_one_persisted_attempt()
    {
        await using var f = new HerdrLabelFollowFixture(); await f.StartAsync();
        var gate = f.Fake.GateMethod("tab.get"); var first = f.FollowAsync();
        try
        {
            await HerdrLabelFollowFixture.WaitAsync(() => f.GetterCount == 1);
            await Task.WhenAll(f.FollowAsync(), f.FollowAsync()).WaitAsync(TimeSpan.FromSeconds(2));
            f.Saved.LabelFollow!.Sequence.ShouldBe(1);
        }
        finally { gate.Release(); await first; }
        f.GetterCount.ShouldBe(2); // initial + final validation, exactly one batch
        f.Saved.TabLabel.ShouldBe("New");
    }

    [Test]
    public async Task Inflight_triggers_return_without_waiting() => await Concurrent_triggers_share_one_persisted_attempt();

    [Test]
    public async Task Attempt_claim_is_durable_before_io()
    {
        await using var f = new HerdrLabelFollowFixture(); await f.StartAsync();
        var gate = f.Fake.GateMethod("tab.get"); using var abort = new CancellationTokenSource();
        var run = f.FollowAsync(abort.Token);
        try
        {
            await HerdrLabelFollowFixture.WaitAsync(() => f.GetterCount == 1);
            f.Saved.LabelFollow!.Sequence.ShouldBe(1);
            f.Saved.LabelFollow.NextDueAtUtc.ShouldBe(f.Clock.GetUtcNow().UtcDateTime.AddHours(1));
            f.Saved.LabelFollow.Observation.ShouldBeNull();
            abort.Cancel(); await run;
        }
        finally { gate.Drop(); await run; }
        await f.RecreateChildAsync(); await f.FollowAsync(); f.GetterCount.ShouldBe(1);
        f.Clock.Advance(TimeSpan.FromHours(1)); await f.FollowAsync(); f.Saved.TabLabel.ShouldBe("New");
    }

    [Test][Arguments("success")][Arguments("equal")][Arguments("refused")][Arguments("failed")]
    public async Task Boundary_and_failure_attempts_obey_the_same_hour(string arm)
    {
        await using var f = new HerdrLabelFollowFixture(); await f.StartAsync();
        if (arm == "equal") { f.Tab.Label = "Old"; f.Workspace.Label = "Old workspace"; }
        if (arm == "refused") f.Tab.ReportedPaneCount = 2;
        if (arm == "failed") f.Fake.FailMethod("tab.get", "unavailable");
        await f.FollowAsync(); var reads = f.GetterCount;
        f.Tab.Label = "intermediate"; f.Clock.Advance(TimeSpan.FromMinutes(30)); await f.FollowAsync();
        f.Tab.Label = "Latest"; f.Tab.ReportedPaneCount = null;
        f.Clock.Advance(TimeSpan.FromMinutes(30) - TimeSpan.FromTicks(1)); await f.FollowAsync();
        f.GetterCount.ShouldBe(reads);
        f.Clock.Advance(TimeSpan.FromTicks(1)); await f.FollowAsync();
        f.GetterCount.ShouldBe(reads + 2); f.Saved.TabLabel.ShouldBe("Latest"); f.Saved.LabelFollow!.Sequence.ShouldBe(2);
    }

    [Test]
    public async Task New_attempt_clears_old_candidate_before_io()
    {
        await using var f = new HerdrLabelFollowFixture(); await f.StartAsync(); await f.FollowAsync();
        (await f.ReadAsync()).ShouldNotBeNull(); f.Clock.Advance(TimeSpan.FromHours(1));
        var gate = f.Fake.GateMethod("tab.get"); var task = f.FollowAsync();
        try
        {
            await HerdrLabelFollowFixture.WaitAsync(() => f.GetterCount == 3);
            f.Saved.LabelFollow!.Observation.ShouldBeNull(); (await f.ReadAsync()).ShouldBeNull();
        }
        finally { gate.Release(); await task; }
    }

    [Test]
    public async Task Restart_preserves_cooldown_and_requires_fresh_validation()
    {
        await using var f = new HerdrLabelFollowFixture(); await f.StartAsync(); await f.FollowAsync();
        var due = f.Saved.LabelFollow!.NextDueAtUtc; f.Clock.Advance(TimeSpan.FromMinutes(30));
        await f.RecreateChildAsync(); (await f.ReadAsync()).ShouldBeNull(); await f.FollowAsync();
        f.GetterCount.ShouldBe(2); f.Saved.LabelFollow!.NextDueAtUtc.ShouldBe(due);
        f.Clock.Advance(TimeSpan.FromMinutes(30)); await f.FollowAsync();
        (await f.ReadAsync())!.TabLabel.ShouldBe("New"); f.Saved.LabelFollow.Sequence.ShouldBe(2);
    }

    [Test]
    public async Task New_generation_is_immediately_eligible()
    {
        await using var f = new HerdrLabelFollowFixture(); await f.StartAsync(); await f.FollowAsync();
        f.Clock.Advance(TimeSpan.FromMinutes(30)); await f.Child.DisposeAsync();
        (f.Binding with { AcceptedStartedAt = f.Clock.GetUtcNow().UtcDateTime }).SaveAtomic(f.Path);
        await f.RecreateChildAsync(); await f.FollowAsync(); f.GetterCount.ShouldBe(4); f.Saved.LabelFollow!.Sequence.ShouldBe(1);
    }

    [Test][Arguments("cooldown", 0)][Arguments("cooldown", -1)][Arguments("timeout", 0)][Arguments("timeout", -1)]
    public void Invalid_settings_refuse_startup(string setting, int value)
    {
        var s = new HerdrSettings(); s.LabelFollowCooldownMinutes.ShouldBe(60); s.LabelFollowObservationTimeoutSeconds.ShouldBe(10);
        if (setting == "cooldown") s.LabelFollowCooldownMinutes = value; else s.LabelFollowObservationTimeoutSeconds = value;
        Should.Throw<OptionsValidationException>(s.ValidateLabelFollow);
    }

    [Test][Arguments("lease")][Arguments("getter")]
    public async Task Stalled_getter_times_out_and_releases_the_pane_lease(string arm)
    {
        await using var f = new HerdrLabelFollowFixture(); await f.StartAsync();
        var lease = arm == "lease" ? await f.Coordinator.LockPaneAsync(f.Binding.PaneId, CancellationToken.None) : null;
        var gate = arm == "getter" ? f.Fake.GateMethod("tab.get") : null;
        var task = f.FollowAsync();
        try
        {
            await HerdrLabelFollowFixture.WaitAsync(() => arm == "lease" ? f.Saved.LabelFollow!.Sequence == 1 : f.GetterCount == 1);
            f.Clock.Advance(TimeSpan.FromSeconds(10)); await task.WaitAsync(TimeSpan.FromSeconds(2));
            f.Saved.LabelFollow!.Observation!.ResultCode.ShouldBe("timeout"); f.Saved.TabLabel.ShouldBe("Old");
        }
        finally { gate?.Drop(); if (lease is not null) await lease.DisposeAsync(); await task; }
        await using var next = await f.Coordinator.LockPaneAsync(f.Binding.PaneId, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Test][Arguments("unreachable")][Arguments("missing-child")]
    public async Task Cached_candidate_requires_positive_current_get(string arm)
    {
        await using var f = new HerdrLabelFollowFixture(); await f.StartAsync(); await f.FollowAsync();
        if (arm == "unreachable") f.Fake.FailMethod("pane.get", "unavailable");
        else f.Fake.SetPaneProcessInfo(f.Binding.PaneId, 4242, Array.Empty<(int, string)>());
        (await f.ReadAsync()).ShouldBeNull(); f.GetterCount.ShouldBe(2);
    }

    [Test][Arguments("pane")][Arguments("tab")][Arguments("workspace")]
    public async Task Cached_candidate_is_suppressed_after_move(string arm)
    {
        await using var f = new HerdrLabelFollowFixture(); await f.StartAsync(); await f.FollowAsync();
        f.Fake.TransformResult = (method, json) =>
        {
            if (method != "pane.get") return json;
            var node = System.Text.Json.Nodes.JsonNode.Parse(json)!; node["pane"]![arm + "_id"] = "moved"; return node.ToJsonString();
        };
        (await f.ReadAsync()).ShouldBeNull(); f.GetterCount.ShouldBe(2);
    }

    [Test]
    public async Task Idle_healthy_stream_checks_without_turns_or_gets()
    {
        await using var f = new HerdrLabelFollowFixture(); await f.StartAsync(); await using var runtime = await f.AdoptRuntimeAsync();
        using var pump = new HerdrEventPumpService(runtime, f.Client, Options.Create(f.HerdrSettings), NullLogger<HerdrEventPumpService>.Instance, f.Clock);
        await pump.StartAsync(CancellationToken.None);
        try
        {
            await HerdrLabelFollowFixture.WaitAsync(() => f.Fake.SubscriptionRecords.Count == 1);
            f.Saved.TabLabel.ShouldBe("New"); f.Tab.Label = "Later";
            f.Clock.Advance(TimeSpan.FromHours(1));
            await HerdrLabelFollowFixture.WaitAsync(() => f.Saved.TabLabel == "Later");
            f.Saved.LabelFollow!.Sequence.ShouldBe(2); runtime.Get(f.Binding.SessionId).Status.ShouldBe("Running");
        }
        finally { await pump.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2)); }
    }

    [Test][Arguments(false)][Arguments(true)]
    public async Task Disabled_or_stopped_pump_does_no_label_work(bool enabled)
    {
        await using var f = new HerdrLabelFollowFixture(); await f.StartAsync(); await using var runtime = await f.AdoptRuntimeAsync();
        var settings = new HerdrSettings { Enabled = enabled };
        using var pump = new HerdrEventPumpService(runtime, f.Client, Options.Create(settings), NullLogger<HerdrEventPumpService>.Instance, f.Clock);
        await pump.StartAsync(CancellationToken.None);
        if (enabled) await HerdrLabelFollowFixture.WaitAsync(() => f.Fake.SubscriptionRecords.Count == 1);
        await pump.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
        var count = f.GetterCount; f.Clock.Advance(TimeSpan.FromHours(2)); await Task.Yield(); f.GetterCount.ShouldBe(count);
        if (!enabled) count.ShouldBe(0);
    }

    [Test]
    public async Task Stop_joins_stream_and_timer()
    {
        await using var f = new HerdrLabelFollowFixture(); await f.StartAsync(); await using var runtime = await f.AdoptRuntimeAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var pump = new HerdrEventPumpService(runtime, f.Client, Options.Create(f.HerdrSettings), NullLogger<HerdrEventPumpService>.Instance, f.Clock)
        { TimerFinalizing = async () => { entered.TrySetResult(); await release.Task; } };
        await pump.StartAsync(CancellationToken.None);
        await HerdrLabelFollowFixture.WaitAsync(() => f.Fake.SubscriptionRecords.Count == 1);
        var stopping = pump.StopAsync(CancellationToken.None);
        try { await entered.Task.WaitAsync(TimeSpan.FromSeconds(2)); stopping.IsCompleted.ShouldBeFalse(); }
        finally { release.TrySetResult(); await stopping.WaitAsync(TimeSpan.FromSeconds(2)); }
    }
}
