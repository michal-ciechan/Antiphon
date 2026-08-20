using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-0103 slice 1. The probe is the fourth rung of the readiness ladder and the only one that
/// asks the input side anything: CARD-0052 proved the child produced output, CARD-0047 proved no
/// modal is standing, CARD-0048 proved the console host finished its handshake — and a Claude TUI
/// that is painted but not yet draining stdin satisfies all three while silently buffering
/// everything typed into it for up to 200 seconds (measured 2026-08-20).
///
/// <para>These drive the helper through delegates, with no terminal, so each rule is pinned on its
/// own: the token has to RENDER, a late render still counts, a token that never renders fails
/// (rather than passing quietly, which is the bug), and a composer that will not empty fails too —
/// appending a boot prompt to a line we could not clear is how a body arrives spliced onto junk.
/// The end-to-end shape through a real ConPTY is <c>FakeClaudeContractTests</c>'s deaf-start arms.</para>
/// </summary>
public class ComposerInputProbeTests
{
    private static ComposerProbeOptions FastOptions(
        int timeoutMs = 2000, int retypeMs = 100_000, int maxWrites = 3, int clearMs = 1000) =>
        ComposerProbeOptions.FromMilliseconds(
            timeoutMs, pollIntervalMs: 10, retypeIntervalMs: retypeMs, clearTimeoutMs: clearMs,
            maxWrites: maxWrites);

    [Test]
    public async Task A_reading_composer_answers_with_one_write_and_one_kill_line()
    {
        var composer = new FakeComposer();

        var result = await ComposerInputProbe.RunAsync(
            "zzdeadbeef", composer.SnapshotAsync, composer.WriteAsync, FastOptions(), null,
            CancellationToken.None);

        result.Outcome.ShouldBe(ComposerProbeOutcome.Responsive);
        result.Writes.ShouldBe(1, "a healthy TUI answers the first token");
        composer.Writes.ShouldBe(["zzdeadbeef", ComposerInputProbe.KillLine]);
        composer.Screen.ShouldBeEmpty("the probe must leave the composer as it found it");
    }

    [Test]
    public async Task The_probe_never_sends_a_carriage_return()
    {
        var composer = new FakeComposer();

        await ComposerInputProbe.RunAsync(
            "zzdeadbeef", composer.SnapshotAsync, composer.WriteAsync, FastOptions(), null,
            CancellationToken.None);

        composer.Writes.ShouldNotContain(w => w.Contains('\r') || w.Contains('\n'),
            "nothing the probe writes may submit — it runs before anybody owns the session");
    }

    // The measured shape: the TUI is deaf, the bytes sit in the retained ConPTY buffer, and they are
    // processed in order when it wakes. The probe's whole job is to be still waiting at that moment.
    [Test]
    public async Task A_token_that_renders_late_is_still_a_pass()
    {
        var composer = new FakeComposer { DeafForFirstReads = 25 };

        var result = await ComposerInputProbe.RunAsync(
            "zzdeadbeef", composer.SnapshotAsync, composer.WriteAsync, FastOptions(), null,
            CancellationToken.None);

        result.Outcome.ShouldBe(ComposerProbeOutcome.Responsive);
        composer.Reads.ShouldBeGreaterThan(25, "the probe kept polling instead of giving up early");
    }

    // The bug this replaces: a token that never renders used to be indistinguishable from a healthy
    // launch, because nothing ever asked. It must be a FALSE verdict, not a quiet pass.
    [Test]
    public async Task A_token_that_never_renders_fails_and_the_launch_is_told()
    {
        var composer = new FakeComposer { DeafForever = true };

        var result = await ComposerInputProbe.RunAsync(
            "zzdeadbeef", composer.SnapshotAsync, composer.WriteAsync, FastOptions(timeoutMs: 300),
            null, CancellationToken.None);

        result.Outcome.ShouldBe(ComposerProbeOutcome.NeverAppeared);
        result.Responsive.ShouldBeFalse();
    }

    [Test]
    public async Task An_unrendered_token_is_re_typed_up_to_the_write_cap_and_no_further()
    {
        var composer = new FakeComposer { DeafForever = true };

        var result = await ComposerInputProbe.RunAsync(
            "zzdeadbeef", composer.SnapshotAsync, composer.WriteAsync,
            FastOptions(timeoutMs: 1000, retypeMs: 100, maxWrites: 3), null, CancellationToken.None);

        result.Writes.ShouldBe(3, "three writes inside the budget, including the first");
        composer.Writes.Count(w => w == "zzdeadbeef").ShouldBe(3);
    }

    // A composer we cannot empty must not have a boot prompt appended to it — the body would arrive
    // spliced onto the probe token, which is worse than a failed launch.
    [Test]
    public async Task A_composer_that_will_not_clear_fails_even_though_the_token_rendered()
    {
        var composer = new FakeComposer { IgnoreKillLine = true };

        var result = await ComposerInputProbe.RunAsync(
            "zzdeadbeef", composer.SnapshotAsync, composer.WriteAsync, FastOptions(clearMs: 300),
            null, CancellationToken.None);

        result.Outcome.ShouldBe(ComposerProbeOutcome.NeverCleared);
        composer.Writes.Count(w => w == ComposerInputProbe.KillLine)
            .ShouldBe(3, "Ctrl+U is re-sent up to the attempt cap before the launch is failed");
    }

    [Test]
    public async Task A_lingering_token_that_clears_on_a_later_kill_line_still_passes()
    {
        var composer = new FakeComposer { IgnoreKillLines = 1 };

        var result = await ComposerInputProbe.RunAsync(
            "zzdeadbeef", composer.SnapshotAsync, composer.WriteAsync, FastOptions(clearMs: 900),
            null, CancellationToken.None);

        result.Outcome.ShouldBe(ComposerProbeOutcome.Responsive);
        composer.Writes.Count(w => w == ComposerInputProbe.KillLine).ShouldBe(2);
    }

    // The token must be safe to type into a live TUI: a leading '/' is a slash command, '!' is the
    // bash shortcut and '#' is the memory shortcut. Letters and digits only settles all three.
    [Test]
    public void The_token_can_never_be_read_as_a_command()
    {
        for (var i = 0; i < 200; i++)
        {
            var token = ComposerInputProbe.TokenFor(Guid.NewGuid());
            token.ShouldAllBe(c => char.IsAsciiLetterOrDigit(c));
            token.Length.ShouldBe(10);
            char.IsAsciiLetter(token[0]).ShouldBeTrue();
        }
    }

    [Test]
    public void The_token_is_derived_from_the_session_so_a_log_line_is_attributable()
    {
        var id = Guid.Parse("8eb8aa9c-2c93-481c-bba6-8ab5a4825c7f");
        ComposerInputProbe.TokenFor(id).ShouldBe("zz8eb8aa9c");
    }

    /// <summary>
    /// A composer that echoes what is typed and honours Ctrl+U — the two behaviours the probe
    /// depends on — with each way of breaking them switchable.
    /// </summary>
    private sealed class FakeComposer
    {
        private readonly object _gate = new();
        private string _screen = string.Empty;

        /// <summary>Reads that render nothing, modelling the buffered-but-not-drained window.</summary>
        public int DeafForFirstReads { get; init; }

        public bool DeafForever { get; init; }
        public bool IgnoreKillLine { get; init; }

        /// <summary>How many Ctrl+U presses are eaten before one works.</summary>
        public int IgnoreKillLines { get; init; }

        public List<string> Writes { get; } = [];
        public int Reads { get; private set; }

        public string Screen
        {
            get { lock (_gate) return _screen; }
        }

        public Task<string> SnapshotAsync(CancellationToken ct)
        {
            lock (_gate)
            {
                Reads++;
                if (DeafForever || Reads <= DeafForFirstReads)
                    return Task.FromResult("[chrome] a painted composer and nothing else");
                return Task.FromResult(_screen);
            }
        }

        public Task WriteAsync(string input, CancellationToken ct)
        {
            lock (_gate)
            {
                Writes.Add(input);
                if (input == ComposerInputProbe.KillLine)
                {
                    var eaten = Writes.Count(w => w == ComposerInputProbe.KillLine) <= IgnoreKillLines;
                    if (!IgnoreKillLine && !eaten)
                        _screen = string.Empty;
                }
                else
                {
                    _screen += input;
                }
            }
            return Task.CompletedTask;
        }
    }
}
