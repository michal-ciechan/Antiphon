using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

[Category("Unit")]
public class ClaudeStartupReadinessTests
{
    [Test]
    public async Task Redraw_is_not_clearance_before_the_probe()
    {
        var fake = new EffortTestScreen();
        var phase = -1;
        fake.AfterWrite = (_, key) => { if (key == "\r" && phase < 0) phase = 0; };
        fake.OnSnapshot = f =>
        {
            if (phase < 0 || phase > 4) return;
            f.Override = phase++ switch { 0 => "", 1 => ">\n? for shortcuts", 2 => f.Template, _ => null };
        };
        var premature = false;
        var original = fake.AfterWrite;
        fake.AfterWrite = (f, key) => { original(f, key); if (key.StartsWith("zz") && phase < 5) premature = true; };
        var result = await fake.ReadyAsync();
        result.Ready.ShouldBeTrue(fake.Evidence);
        premature.ShouldBeFalse(fake.Evidence);
        fake.ClearObservations.ShouldBeGreaterThanOrEqualTo(2);
        fake.Composer.ShouldBeEmpty();
        fake.Raw.ShouldContain("Keep xhigh");
    }

    [Test]
    public async Task A_contradictory_resulting_effort_fails_readiness()
    {
        var fake = new EffortTestScreen { ResultBanner = "high" };
        (await fake.ReadyAsync()).Outcome.ShouldBe(ClaudeReadinessOutcome.EffortFailed, fake.Evidence);
        fake.TokenWrites.ShouldBe(0);
    }

    [Test]
    public async Task A_scrolled_away_effort_banner_does_not_block_confirmed_clearance()
    {
        var fake = new EffortTestScreen { ShowBanner = false };
        (await fake.ReadyAsync()).Ready.ShouldBeTrue(fake.Evidence);
        fake.AppliedEffort.ShouldBe("xhigh");
        fake.TokenWrites.ShouldBe(1);
        fake.ClearObservations.ShouldBeGreaterThanOrEqualTo(2);
        fake.Composer.ShouldBeEmpty();
    }

    [Test, Arguments("after-echo"), Arguments("before-echo")]
    public async Task A_late_dialog_requires_a_fresh_post_clearance_round_trip(string row)
    {
        var fake = new EffortTestScreen { Dialog = false };
        var pending = false;
        fake.AfterWrite = (f, key) =>
        {
            if (!key.StartsWith("zz") || f.TokenWrites != 1) return;
            if (row == "before-echo") f.Dialog = true;
            else pending = true;
        };
        var echoed = false;
        fake.OnSnapshot = f =>
        {
            if (!pending) return;
            if (!echoed) { echoed = true; return; }
            f.Template += "\nHistorical zzdeadbeef";
            f.Dialog = true;
            pending = false;
        };
        (await fake.ReadyAsync()).Ready.ShouldBeTrue(fake.Evidence);
        fake.TokenWrites.ShouldBe(2, "a new post-dialog round trip is required\n" + fake.Evidence);
        fake.Writes.Count(w => w.Key == "\x15" && !w.Dialog).ShouldBe(1);
        fake.Composer.ShouldBeEmpty();
    }

    [Test, Arguments("trust-effort"), Arguments("effort-trust"), Arguments("effort-permission"), Arguments("effort-choice")]
    public async Task Startup_dialog_chains_preserve_each_gate(string row)
    {
        const string trust = "Do you trust this folder?\n1. Yes, I trust this folder\n2. No";
        var fake = new EffortTestScreen();
        if (row == "trust-effort") fake.Override = trust;
        fake.AfterWrite = (f, key) =>
        {
            if (key == "1") { f.Override = null; return; }
            if (key != "\r") return;
            f.Override = row switch { "effort-trust" => trust, "effort-permission" => "Do you want to proceed?\n1. Yes", "effort-choice" => "Choose an option?\nEnter to confirm", _ => null };
        };
        var result = await fake.ReadyAsync();
        result.Ready.ShouldBeTrue(fake.Evidence);
        fake.AppliedEffort.ShouldBe("xhigh");
        if (row is "effort-permission" or "effort-choice")
        {
            result.Outcome.ShouldBe(ClaudeReadinessOutcome.NotAnswerable);
            fake.Writes.Select(w => w.Key).ShouldBe(["\r"]);
        }
        else { fake.Writes.Select(w => w.Key).ShouldContain("1"); fake.TokenWrites.ShouldBe(1); fake.Composer.ShouldBeEmpty(); }
    }

    [Test]
    public async Task Recurring_dialogs_cannot_renew_the_readiness_deadline()
    {
        var fake = new EffortTestScreen { Dialog = false };
        fake.AfterWrite = (f, key) => { if (key.StartsWith("zz")) f.Dialog = true; };
        using var cts = new CancellationTokenSource();
        var task = fake.ReadyAsync(totalMs: 5500, maxWrites: 100, ct: cts.Token);
        try
        {
            (await Task.WhenAny(task, Task.Delay(8000))).ShouldBe(task, "operation completed before independent 8-second watchdog");
            (await task).Ready.ShouldBeFalse(fake.Evidence);
            fake.Clock.Elapsed.TotalMilliseconds.ShouldBeLessThan(6500);
        }
        finally { await cts.CancelAsync(); try { await task; } catch (OperationCanceledException) { } }
    }

    [Test]
    public async Task Interrupted_probes_share_the_token_write_limit()
    {
        var fake = new EffortTestScreen { Dialog = false };
        fake.AfterWrite = (f, key) => { if (key.StartsWith("zz")) f.Dialog = true; };
        (await fake.ReadyAsync(totalMs: 20000, maxWrites: 2)).Ready.ShouldBeFalse(fake.Evidence);
        fake.TokenWrites.ShouldBe(2);
    }

    [Test, Arguments("settle"), Arguments("navigation"), Arguments("clearance"), Arguments("exit")]
    public async Task Cancellation_and_exit_stop_startup_input(string row)
    {
        var fake = new EffortTestScreen { Highlight = row == "navigation" ? 2 : 1, SwallowEnters = 100 };
        using var cts = new CancellationTokenSource();
        var entered = new TaskCompletionSource();
        var exited = new TaskCompletionSource();
        fake.AfterWrite = (_, _) => entered.TrySetResult();
        var task = fake.ReadyAsync(exited: exited.Task, ct: cts.Token);
        if (row is "navigation" or "clearance") await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var count = fake.Writes.Count;
        if (row == "exit") { exited.SetResult(); (await task).Outcome.ShouldBe(ClaudeReadinessOutcome.Exited); }
        else { await cts.CancelAsync(); await Should.ThrowAsync<OperationCanceledException>(async () => await task); }
        fake.Writes.Count.ShouldBe(count);
    }
}
