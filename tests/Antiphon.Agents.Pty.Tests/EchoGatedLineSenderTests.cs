using System.Diagnostics;
using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>Pure timing and no-retry pins for <see cref="EchoGatedLineSender"/>.</summary>
public class EchoGatedLineSenderTests
{
    [Test]
    public async Task Evidence_delays_the_one_CR_until_after_the_pre_submit_pause()
    {
        const string body = "body whose TAIL is visible";
        var terminal = new ScriptedTerminal();
        var evidenceAt = TimeSpan.Zero;
        var options = new SendLineGateOptions(
            PreSubmitPause: TimeSpan.FromMilliseconds(50),
            EvidenceTimeout: TimeSpan.FromSeconds(1),
            PollInterval: TimeSpan.FromMilliseconds(10));

        var outcome = await EchoGatedLineSender.SendAsync(
            body,
            _ =>
            {
                if (terminal.BodyWasWritten)
                    evidenceAt = terminal.Elapsed;
                return Task.FromResult(terminal.BodyWasWritten ? body : string.Empty);
            },
            terminal.WriteAsync,
            options,
            CancellationToken.None);

        outcome.ShouldBe(SendLineGateOutcome.EvidenceSeen);
        terminal.Writes.Select(w => w.Text).ShouldBe([body, "\r"]);
        (terminal.Writes[^1].At - evidenceAt).ShouldBeGreaterThanOrEqualTo(options.PreSubmitPause);
    }

    [Test]
    public async Task Missing_evidence_times_out_then_writes_exactly_one_CR()
    {
        const string body = "body that no composer echoes";
        var terminal = new ScriptedTerminal();
        var options = new SendLineGateOptions(
            PreSubmitPause: TimeSpan.FromMilliseconds(20),
            EvidenceTimeout: TimeSpan.FromMilliseconds(80),
            PollInterval: TimeSpan.FromMilliseconds(10));

        var outcome = await EchoGatedLineSender.SendAsync(
            body,
            _ => Task.FromResult(string.Empty),
            terminal.WriteAsync,
            options,
            CancellationToken.None);

        outcome.ShouldBe(SendLineGateOutcome.TimedOutProceeded);
        terminal.Writes.Count(w => w.Text == "\r").ShouldBe(1, "the bounded fallback has no retry");
        (terminal.Writes[^1].At - terminal.Writes[0].At).ShouldBeGreaterThanOrEqualTo(options.EvidenceTimeout);
    }

    [Test]
    public async Task Instant_evidence_keeps_the_existing_body_write_to_CR_pause_floor()
    {
        const string body = "instantly visible body";
        var terminal = new ScriptedTerminal();
        var options = new SendLineGateOptions(
            PreSubmitPause: TimeSpan.FromMilliseconds(50),
            EvidenceTimeout: TimeSpan.FromSeconds(1),
            PollInterval: TimeSpan.FromMilliseconds(10));

        await EchoGatedLineSender.SendAsync(
            body,
            _ => Task.FromResult(terminal.BodyWasWritten ? body : string.Empty),
            terminal.WriteAsync,
            options,
            CancellationToken.None);

        (terminal.Writes[^1].At - terminal.Writes[0].At).ShouldBeGreaterThanOrEqualTo(options.PreSubmitPause);
    }

    [Test]
    public async Task Cancellation_during_the_evidence_poll_never_writes_CR()
    {
        var terminal = new ScriptedTerminal();
        using var cts = new CancellationTokenSource();
        var snapshots = 0;
        var options = new SendLineGateOptions(
            PreSubmitPause: TimeSpan.FromMilliseconds(20),
            EvidenceTimeout: TimeSpan.FromSeconds(1),
            PollInterval: TimeSpan.FromMilliseconds(20));

        await Should.ThrowAsync<OperationCanceledException>(() => EchoGatedLineSender.SendAsync(
            "body that remains absent",
            _ =>
            {
                if (Interlocked.Increment(ref snapshots) == 3)
                    cts.Cancel();
                return Task.FromResult(string.Empty);
            },
            terminal.WriteAsync,
            options,
            cts.Token));

        terminal.Writes.ShouldNotContain(w => w.Text == "\r");
    }

    private sealed class ScriptedTerminal
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        public List<(string Text, TimeSpan At)> Writes { get; } = [];

        public bool BodyWasWritten => Writes.Count > 0;

        public TimeSpan Elapsed => _clock.Elapsed;

        public Task WriteAsync(string text, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Writes.Add((text, _clock.Elapsed));
            return Task.CompletedTask;
        }
    }
}
