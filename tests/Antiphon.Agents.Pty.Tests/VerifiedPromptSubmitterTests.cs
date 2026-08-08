using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// Pure-logic coverage of the verified boot-prompt submit (the DeliverAsync contract ported to
/// adapter prompt delivery, live miss 2026-08-08): the body goes in PtyInputEncoding form, the
/// Enter is withheld from a composer that never shows the body, a swallowed Enter is re-pressed,
/// and persistent failure throws instead of stranding the prompt silently.
/// </summary>
public class VerifiedPromptSubmitterTests
{
    private static readonly VerifiedSubmitOptions FastOptions = new(
        EvidenceTimeout: TimeSpan.FromMilliseconds(300),
        PollInterval: TimeSpan.FromMilliseconds(10),
        PostSubmitAdvanceTimeout: TimeSpan.FromMilliseconds(200),
        SubmitAttempts: 3);

    [Test]
    public async Task Happy_path_writes_encoded_body_then_separate_enter()
    {
        var terminal = new ScriptedTerminal(echoBodyToScreen: true, advanceOnEnter: 1);

        await VerifiedPromptSubmitter.SubmitAsync(
            "Work on card CARD-0001\r\n\r\nDescription:\r\ndo the thing",
            terminal.SnapshotScreen, terminal.OutputMark, terminal.Write,
            FastOptions, log: null, CancellationToken.None);

        terminal.Writes.Count.ShouldBe(2);
        terminal.Writes[0].ShouldBe("\x1b[200~Work on card CARD-0001\n\nDescription:\ndo the thing\x1b[201~");
        terminal.Writes[1].ShouldBe("\r");
    }

    [Test]
    public async Task Dead_composer_withholds_the_enter_and_throws()
    {
        var terminal = new ScriptedTerminal(echoBodyToScreen: false, advanceOnEnter: 1);

        var ex = await Should.ThrowAsync<PromptDeliveryException>(() =>
            VerifiedPromptSubmitter.SubmitAsync(
                "the prompt body that never renders",
                terminal.SnapshotScreen, terminal.OutputMark, terminal.Write,
                FastOptions, log: null, CancellationToken.None));

        ex.Message.ShouldContain("no composer evidence");
        terminal.Writes.ShouldNotContain("\r", "the Enter must be withheld from a dead composer");
    }

    [Test]
    public async Task Swallowed_enter_is_pressed_again_and_logged()
    {
        // The 2026-08-08 shape: body lands in the composer, but the first Enter is eaten.
        var terminal = new ScriptedTerminal(echoBodyToScreen: true, advanceOnEnter: 2);
        var logged = new List<string>();

        await VerifiedPromptSubmitter.SubmitAsync(
            "stubborn prompt",
            terminal.SnapshotScreen, terminal.OutputMark, terminal.Write,
            FastOptions, logged.Add, CancellationToken.None);

        terminal.Writes.Count(w => w == "\r").ShouldBe(2);
        logged.ShouldHaveSingleItem().ShouldContain("pressing Enter again");
    }

    [Test]
    public async Task Enter_that_never_lands_throws_after_the_configured_attempts()
    {
        var terminal = new ScriptedTerminal(echoBodyToScreen: true, advanceOnEnter: int.MaxValue);

        var ex = await Should.ThrowAsync<PromptDeliveryException>(() =>
            VerifiedPromptSubmitter.SubmitAsync(
                "never submits",
                terminal.SnapshotScreen, terminal.OutputMark, terminal.Write,
                FastOptions, log: null, CancellationToken.None));

        ex.Message.ShouldContain("no output");
        terminal.Writes.Count(w => w == "\r").ShouldBe(FastOptions.SubmitAttempts);
    }

    [Test]
    public async Task Single_line_body_is_written_unwrapped()
    {
        var terminal = new ScriptedTerminal(echoBodyToScreen: true, advanceOnEnter: 1);

        await VerifiedPromptSubmitter.SubmitAsync(
            "/rename Antiphon",
            terminal.SnapshotScreen, terminal.OutputMark, terminal.Write,
            FastOptions, log: null, CancellationToken.None);

        terminal.Writes.ShouldBe(["/rename Antiphon", "\r"]);
    }

    /// <summary>
    /// Minimal composer model: written body text lands on the "screen" when echoing is on; the
    /// output mark advances only on the Nth Enter (simulating swallowed Enters before that).
    /// </summary>
    private sealed class ScriptedTerminal(bool echoBodyToScreen, int advanceOnEnter)
    {
        private string _screen = "";
        private long _mark;
        private int _enters;

        public List<string> Writes { get; } = [];

        public Task<string> SnapshotScreen(CancellationToken ct) => Task.FromResult(_screen);

        public Task<long> OutputMark(CancellationToken ct) => Task.FromResult(_mark);

        public Task Write(string data, CancellationToken ct)
        {
            Writes.Add(data);
            if (data == "\r")
            {
                if (++_enters >= advanceOnEnter && advanceOnEnter != int.MaxValue)
                    _mark++;
            }
            else if (echoBodyToScreen)
            {
                _screen += data;
            }
            return Task.CompletedTask;
        }
    }
}
