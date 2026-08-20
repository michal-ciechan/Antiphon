using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-0099 S2: what the REAL Codex composer does with what we type into it. S1's
/// <see cref="CodexCanaryTests"/> pins the rollout RECORD shapes; this pins the INPUT contract on
/// the other side of the pty — the half nothing had ever measured.
///
/// <para>Before this file the queue's normalize-LF/wrap/CR contract (<c>PtyInputEncoding</c>) and
/// CARD-0055's confirm-by-text-match loop were running on answers only ever verified for Claude and
/// Grok. Ground truth here is the same rollout S1 tails — an <c>item_completed</c> row whose
/// <c>item.type</c> is <c>UserMessage</c> (TUI mode) or an <c>event_msg/user_message</c> (exec) —
/// never the screen, because the screen is the redraw signal CARD-0055 exists to replace.</para>
///
/// <para><b>MEASURED 2026-08-20, codex-cli 0.147.0, gpt-5.6-luna, modern ConPTY.</b> The headline:
/// <b>Codex's composer keeps every byte we type, newlines included.</b> It is not Grok (which drops
/// every LF and joins the lines with no separator) and it is not Claude (which clips a large typed
/// body to one ~1 KB read chunk). Verbatim, from the rollout:</para>
///
/// <list type="bullet">
/// <item><b>A — Enter on an empty composer submits nothing.</b> Two bare Enters mid-session added 0
/// prompt rows, so CARD-0055's Enter-only retry is safe here for the same reason it is safe for
/// Claude: a re-press into an empty composer cannot submit a stale body.</item>
/// <item><b>B — text + CR in ONE write is a COIN FLIP.</b> The same body submitted directly on one
/// run and sat untouched in the composer through a 25 s wait on two others, going only once a
/// separate Enter arrived. That is a paste-detection window: a trailing CR inside it folds to a
/// newline, outside it is Enter, and which one you get depends on how ConPTY chunked the write. A
/// caller that concatenates its Enter to the body therefore cannot know whether it submitted —
/// which is precisely why the separate, delayed CR in <c>PtyAgentRunner.SendLineAsync</c> is
/// load-bearing for Codex and not merely harmless.</item>
/// <item><b>C — a typed LF multi-line body lands byte-identical.</b> Sent 61 chars over three lines,
/// recorded 61: <c>"CX-LF-HEAD reply with exactly OK\nline two of three\nCX-LF-TAIL"</c>. A typed
/// <c>\n</c> is a literal newline to this composer; nothing is joined and nothing is lost.</item>
/// <item><b>D — a raw CRLF body does not fragment, it DOUBLES.</b> Sent 61 chars containing two
/// <c>\r\n</c> pairs; recorded as ONE prompt with each pair arriving as <c>\n\n</c>. <b>A different
/// hazard from Claude's</b>, where a mid-body CR submits the fragment before it: here nothing splits
/// and nothing is lost — a blank line is silently inserted between every line instead, a corruption
/// that survives every length, head and tail check.
/// <c>PtyInputEncoding.NormalizeBody</c> is therefore load-bearing for Codex too, for a reason its
/// own doc comment does not give.</item>
/// <item><b>E — a 3 466-char bracketed paste lands WHOLE</b> (3 466 recorded, newlines intact), on
/// one or two Enters depending on the run.</item>
/// <item><b>F — a 5 166-char TYPED body lands WHOLE too</b> (5 166 chars / 5 166 UTF-8 bytes, head
/// and tail both present) and took <b>TWO Enters on every run</b>: the first Enter after a body that
/// size did nothing. There is no CARD-0027 one-chunk clip in this composer — the loss mode is a
/// swallowed Enter, not a swallowed body.</item>
/// </list>
///
/// <para><b>The two-Enter result is the operationally important one</b>, and it is why the
/// conservative spill policy stands even though E and F landed whole. A large body reaches Codex
/// only if the caller (a) waits for the composer to SETTLE before pressing Enter — the runs that
/// pressed Enter on a fixed 1.2 s delay instead left the body stranded in the composer through every
/// later phase, with the screen showing it collapsed into several
/// <c>[Pasted Content N chars]</c> chips — and (b) presses Enter more than once. Production does
/// neither: <c>PtyAgentRunner.SendLineAsync</c> writes the body, waits 20 ms, writes one <c>\r</c>.
/// Under CARD-0055 that costs a delivery attempt on every large body and parks at three. Keeping the
/// inline ceiling at 0 for Codex (<c>PtyDeliveryCeilings.RequiresJoinSafeDelivery</c>) means a brief
/// is a short pointer that submits on the first Enter, and sidesteps all of it.</para>
///
/// <para><b>What S5's FakeCodex must model</b> is exactly the list above: an Enter that is a no-op on
/// an empty composer, a same-write trailing CR that is NOT Enter, LF preserved verbatim, CR doubling
/// a newline rather than submitting, and a large body needing a settle-then-Enter-again to go.</para>
///
/// <para>Headed, opt-in (<c>ANTIPHON_CODEX_HEADED_TESTS=1</c>), <c>[Explicit]</c>: it spends real
/// Codex turns. Launch shape is the production one so the measured TUI is the TUI Antiphon runs.</para>
/// </summary>
[NotInParallel("Headed")]
[Category("Headed")]
[Category("HeadedCanary")]
[Category("Card0099")]
[Explicit]
[ParallelLimiter<ProcessSpawnLimit>]
public class CodexComposerCanaryTests
{
    /// <summary>
    /// The cheapest live delegate tier. The composer is a property of the TERMINAL UI, not of the
    /// model behind it, so a canary about input handling has no reason to spend frontier turns — but
    /// it does have a reason to avoid a DEPRECATED one: launching with <c>-m gpt-5.4-mini</c> clears
    /// the trust dialog straight onto a second modal ("GPT-5.4 Mini will be deprecated soon … 1. Try
    /// new model  2. Use existing model") which swallows input exactly as the first one does.
    /// </summary>
    private const string Model = "gpt-5.6-luna";

    [Test]
    public async Task Composer_submit_contract_on_the_modern_backend()
    {
        CxSession.SkipIfNotEligible();
        var log = CxSession.MeasurementLog(nameof(Composer_submit_contract_on_the_modern_backend));
        var cwd = CxSession.TempCwd();
        var before = CxSession.Rollouts().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var drift = new List<string>();
        var probe = new PromptProbe(before, cwd);

        try
        {
            var (app, args) = CxSession.BuildLaunch(
                CxSession.ResolveCli()!, "-m", Model, "-c", "model_reasoning_effort=\"low\"");
            log($"LAUNCH: {app} {string.Join(" ", args)}");

            await using var runner = new PtyAgentRunner("modern");
            await runner.StartAsync(app, args, cwd: cwd, cols: 120, rows: 30);
            runner.Backend!.Backend.ShouldBe(PtyBackend.ModernConPty,
                "the deployment runs modern; measuring the inbox conhost would measure the wrong pty. "
                + "Reason: " + runner.Backend!.Reason);

            // Answers the startup modals (trust, "Update available") and waits for the composer.
            // Typing before this returns loses the leading characters to whichever modal is standing.
            (await CxSession.WaitForComposerAsync(runner, TimeSpan.FromSeconds(90)))
                .ShouldBeTrue("the composer must render before anything is typed. Screen:\n"
                    + runner.SnapshotScreen());

            // ---- Phase B: text + CR in ONE write ------------------------------------------------
            // Runs FIRST, before the empty-Enter phase, because the rollout file does not exist until
            // the session takes its first TURN — until something submits there is no ground truth to
            // read. The empty-Enter question is better asked of a live session anyway: a re-press is
            // the only shape CARD-0055 ever produces, and it only ever happens mid-session.
            await runner.WriteAsync("CX-ONESHOT reply with exactly OK and nothing else\r");
            var bDirect = await probe.WaitForPromptAsync("CX-ONESHOT", TimeSpan.FromSeconds(25));
            // RECORDED, deliberately not asserted either way: this outcome is NOT STABLE. The same
            // body, the same binary and the same flags submitted directly on one run and sat in the
            // composer through a 25 s wait on two others, going only once a separate Enter arrived.
            // That is the signature of a paste-detection window — a trailing CR inside it folds to a
            // newline, outside it is Enter — and where the CR falls depends on how ConPTY happened to
            // chunk the write, which the caller does not control. Asserting either outcome would pin
            // a coin flip. The non-determinism IS the finding: a caller that concatenates its Enter
            // to the body cannot know whether it submitted, which is exactly why
            // PtyAgentRunner.SendLineAsync sends the CR as a separate delayed write, and why any new
            // path that types into a Codex session must do the same.
            log($"PHASE B text+CR-in-one-write submitted directly: {bDirect is not null} "
                + "(MEASURED 2026-08-20: true on 1 run of 3, false on the other 2 — NOT deterministic)");
            if (bDirect is null)
            {
                var (bRow, bEnters) = await SubmitWithRetriesAsync(runner, probe, "CX-ONESHOT", log, "B");
                if (bRow is null)
                    drift.Add("B: the one-shot body never reached the rollout, with or without separate Enters");
                else
                    log($"PHASE B landed after {bEnters} separate Enter(s): {Visible(Truncate(bRow.Text, 120))}");
            }

            if (probe.Rollout is null)
                throw new SkipTestException(
                    "codex persisted no rollout for this session, so there is no ground truth to "
                    + "measure against. Observed once on 2026-08-20 with three turns visibly "
                    + "answered on screen and no rollout file anywhere in CODEX_HOME — worth "
                    + "knowing, but it makes this run measure nothing.");
            log($"rollout={Path.GetFileName(probe.Rollout)}");

            // ---- Phase A: Enter on an empty composer, mid-session --------------------------------
            var beforeEmptyEnters = probe.PromptCount();
            await runner.WriteAsync("\r");
            await Task.Delay(1500);
            await runner.WriteAsync("\r");
            await Task.Delay(4000);
            var added = probe.PromptCount() - beforeEmptyEnters;
            log($"PHASE A prompt rows added by 2 bare Enters on an empty composer: {added}");
            if (added != 0)
                drift.Add($"A: an Enter on an empty composer submitted something ({added} rows). "
                    + "CARD-0055's retry would burn a turn per attempt, and its never-re-type rule "
                    + "loses the property that makes retrying safe at all");
            await WaitForTurnsAsync(probe, 1, log);

            // ---- Phase C: LF-normalized multi-line body, TYPED (unwrapped) ----------------------
            var lfBody = PtyInputEncoding.NormalizeBody(
                "CX-LF-HEAD reply with exactly OK\nline two of three\nCX-LF-TAIL");
            await runner.WriteAsync(lfBody);
            await Task.Delay(700);
            var (cRow, cEnters) = await SubmitWithRetriesAsync(runner, probe, "CX-LF-HEAD", log, "C");
            if (cRow is null)
            {
                log("PHASE C: no prompt row. Screen:\n" + CxSession.Tail(runner.SnapshotScreen(), 1200));
                drift.Add("C: a typed LF multi-line body never reached the rollout");
            }
            else
            {
                log($"PHASE C sent {lfBody.Length} chars, recorded {cRow.Text!.Length} after {cEnters} Enter(s)");
                log("PHASE C recorded verbatim: " + Visible(cRow.Text));
                // THE headline measurement. If this goes red the composer started joining (Grok's
                // shape) and every delivery rendering decision for Codex has to be revisited.
                if (!cRow.Text.Contains('\n'))
                    drift.Add("C: a typed LF body lost its newlines — Codex now JOINS typed lines the "
                        + "way Grok does");
                if (cRow.Text != lfBody)
                    drift.Add($"C: recorded text differs from what was sent. sent={Visible(lfBody)} "
                        + $"got={Visible(cRow.Text)}");
            }
            await WaitForTurnsAsync(probe, 2, log);

            // ---- Phase D: a CRLF body, typed RAW (deliberately un-normalized) -------------------
            var crlfBody = "CX-CRLF-HEAD reply with exactly OK\r\nsecond line\r\nCX-CRLF-TAIL";
            var beforeD = probe.PromptCount();
            await runner.WriteAsync(crlfBody);
            await Task.Delay(1200);
            var (dRow, dEnters) = await SubmitWithRetriesAsync(runner, probe, "CX-CRLF-HEAD", log, "D");
            await Task.Delay(2000);
            var dRows = probe.PromptCount() - beforeD;
            log($"PHASE D produced {dRows} prompt row(s) for ONE body, after {dEnters} Enter(s)");
            if (dRow is null)
            {
                drift.Add("D: a raw CRLF body never reached the rollout");
            }
            else
            {
                var doubled = dRow.Text!.Contains("\n\n");
                log($"PHASE D sent {crlfBody.Length} chars, recorded {dRow.Text.Length}; "
                    + $"tail present: {dRow.Text.Contains("CX-CRLF-TAIL")}; CRLF doubled to LF LF: {doubled}");
                log("PHASE D recorded verbatim: " + Visible(dRow.Text));
                if (dRows > 1)
                    drift.Add($"D: a raw CRLF body FRAGMENTED into {dRows} prompts — a mid-body CR now acts "
                        + "as Enter (Claude's shape). Any write path that skips NormalizeBody now SPLITS "
                        + "messages, not merely mangles them, which is a strictly worse failure");
                if (!doubled)
                    drift.Add("D: a raw CRLF body no longer doubles its newlines — CR handling changed; "
                        + "re-measure before relying on the rationale recorded here");
            }

            // ---- Phase E: a bracketed paste ------------------------------------------------------
            var pasteBody = BuildMarkedBody("CX-PASTE", 40);
            await runner.WriteAsync(PtyInputEncoding.EncodeBody(pasteBody));
            await runner.WaitForQuietAsync(TimeSpan.FromMilliseconds(1500), TimeSpan.FromSeconds(60));
            var pasteScreen = runner.SnapshotScreen();
            // RECORDED, never asserted: whether the composer collapses a paste to a placeholder is
            // NOT deterministic for a given body size. The same 3 466-char paste rendered as two
            // chips (1 649 + 1 815 chars) on one run and as plain text on the next, because ConPTY
            // splits one write into several reads and the composer decides per read. Claude's
            // placeholder is stable enough for ComposerDeliveryEvidence to match by index
            // (CARD-0037); Codex's is not — and the wording differs too: "[Pasted Content N chars]",
            // a CHARACTER count with no #index, against Claude's "[Pasted text #N +M lines]". Any
            // future screen-evidence path for Codex has to survive both renderings of one body.
            var chips = System.Text.RegularExpressions.Regex.Matches(
                pasteScreen, @"\[Pasted Content (\d+) chars\]");
            log($"PHASE E placeholder chips on screen: {chips.Count} "
                + $"({string.Join(" + ", chips.Select(m => m.Groups[1].Value))} chars); "
                + $"body head visible as text: {pasteScreen.Contains("CX-PASTE-HEAD")}");
            var (eRow, eEnters) = await SubmitWithRetriesAsync(runner, probe, "CX-PASTE-HEAD", log, "E");
            if (eRow is null)
            {
                log("PHASE E: no prompt row. Screen:\n" + CxSession.Tail(runner.SnapshotScreen(), 1500));
                drift.Add($"E: a bracketed paste of {pasteBody.Length} chars never reached the rollout "
                    + "after every Enter retry — a body this size cannot be delivered to Codex inline");
            }
            else
            {
                log($"PHASE E sent {pasteBody.Length} chars, recorded {eRow.Text!.Length} after "
                    + $"{eEnters} Enter(s); head={eRow.Text.Contains("CX-PASTE-HEAD")} "
                    + $"tail={eRow.Text.Contains("CX-PASTE-TAIL")} newlines={eRow.Text.Contains('\n')}");
                // THE fact CARD-0055 rides on: the row must carry the body, not the placeholder.
                if (eRow.Text.Contains("[Pasted Content"))
                    drift.Add("E: the rollout row carries the PLACEHOLDER, not the body — CARD-0055's "
                        + "confirm-by-text-match has nothing to match against for Codex");
                if (!eRow.Text.Contains("CX-PASTE-TAIL"))
                    drift.Add($"E: the pasted body did not land whole — sent {pasteBody.Length}, "
                        + $"recorded {eRow.Text.Length}");
            }
            await WaitForTurnsAsync(probe, 4, log);

            // ---- Phase F: a large TYPED body (the CARD-0027 clip question) -----------------------
            var typedBody = PtyInputEncoding.NormalizeBody(BuildMarkedBody("CX-TYPED", 60));
            await runner.WriteAsync(typedBody);
            await runner.WaitForQuietAsync(TimeSpan.FromMilliseconds(1500), TimeSpan.FromSeconds(60));
            var (fRow, fEnters) = await SubmitWithRetriesAsync(runner, probe, "CX-TYPED-HEAD", log, "F");
            if (fRow is null)
            {
                drift.Add($"F: a {typedBody.Length}-char typed body never reached the rollout after every "
                    + "Enter retry — it is stranded in the composer, the CARD-0055 shape");
            }
            else
            {
                log($"PHASE F sent {typedBody.Length} chars "
                    + $"({System.Text.Encoding.UTF8.GetByteCount(typedBody)} UTF-8 bytes), "
                    + $"recorded {fRow.Text!.Length} after {fEnters} Enter(s); "
                    + $"head={fRow.Text.Contains("CX-TYPED-HEAD")} tail={fRow.Text.Contains("CX-TYPED-TAIL")}");
                if (!fRow.Text.Contains("CX-TYPED-TAIL"))
                    log("PHASE F CLIPPED — Codex's composer now loses part of a large TYPED body, the "
                        + "CARD-0027 shape. Recorded, not failed: the inline ceiling for Codex is 0 "
                        + "either way.");
            }

            log("ROLLOUT payload census: " + string.Join(", ", CxSession.ReadRollout(probe.Rollout)
                .GroupBy(r => $"{r.Type}/{r.EventType ?? "-"}{(r.ItemType is null ? "" : ":" + r.ItemType)}")
                .Select(g => $"{g.Key}={g.Count()}")));

            await runner.KillAsync(TimeSpan.FromSeconds(5));

            drift.ShouldBeEmpty(
                "Codex's measured composer contract drifted from what this canary pins. Every phase "
                + "ran; the full measurement is in TestOutput/CodexCanary/"
                + nameof(Composer_submit_contract_on_the_modern_backend) + ".log. Findings:\n  - "
                + string.Join("\n  - ", drift));
        }
        catch (Exception ex) when (ex is not SkipTestException)
        {
            log("TEST EXCEPTION: " + ex.Message);
            throw;
        }
        finally
        {
            CxSession.BestEffortDelete(cwd);
        }
    }

    /// <summary>
    /// Presses Enter, waits, and re-presses — <b>Enter only, never a re-type</b>, which is exactly
    /// CARD-0055's retry contract and is safe here for the reason phase A pins. Returns how many
    /// Enters it took, because a body that needed more than one is the near-miss this whole slice is
    /// about: in production that same body spends a delivery attempt each time and parks at three.
    /// </summary>
    private static async Task<(CodexRolloutRow? Row, int Enters)> SubmitWithRetriesAsync(
        PtyAgentRunner runner, PromptProbe probe, string marker, Action<string> log, string phase,
        int maxEnters = 3)
    {
        for (var i = 1; i <= maxEnters; i++)
        {
            await runner.WriteAsync("\r");
            var row = await probe.WaitForPromptAsync(marker, TimeSpan.FromSeconds(i == 1 ? 30 : 15));
            if (row is not null)
            {
                if (i > 1)
                    log($"PHASE {phase}: took {i} Enters to submit — in production this body would have "
                        + "consumed delivery attempts");
                return (row, i);
            }
            log($"PHASE {phase}: Enter {i}/{maxEnters} did not submit; composer tail: "
                + Truncate(CxSession.Tail(runner.SnapshotScreen(), 200), 200));
        }
        return (null, maxEnters);
    }

    private static async Task WaitForTurnsAsync(PromptProbe probe, int expected, Action<string> log)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(90);
        while (DateTime.UtcNow < deadline && probe.CompletedTurns() < expected)
            await Task.Delay(250);
        log($"   turn {expected} complete: {probe.CompletedTurns() >= expected} "
            + $"(task_complete rows={probe.CompletedTurns()})");
        await Task.Delay(1000);
    }

    /// <summary>
    /// Resolves the session's rollout BY CWD and re-resolves it on every poll while it is still
    /// null. Both halves are measured requirements, not defensive coding.
    ///
    /// <para><b>Lazily created:</b> the file does not exist until the session's first TURN, and the
    /// first turn does not happen until something submits — so a canary that resolves the path once
    /// up front resolves it to null and then reads every later phase as a failure of the composer
    /// rather than as its own timing bug.</para>
    ///
    /// <para><b>By cwd, not "the newest new file":</b> other agents on this machine run their own
    /// Codex sessions into the same CODEX_HOME, and one of them landing between the snapshot and the
    /// first turn would hand this test a stranger's transcript to make its verdicts from — the
    /// CARD-0006 failure, in a test. <c>session_meta.cwd</c> against a unique temp directory is
    /// exact, and it is the same C2 evidence production binding requires.</para>
    /// </summary>
    private sealed class PromptProbe(HashSet<string> before, string cwd)
    {
        public string? Rollout { get; private set; }

        public async Task<CodexRolloutRow?> WaitForPromptAsync(string marker, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (Resolve() is { } path)
                {
                    var row = CxSession.ReadRollout(path)
                        .FirstOrDefault(r => IsPrompt(r) && r.Text!.Contains(marker, StringComparison.Ordinal));
                    if (row is not null) return row;
                }
                await Task.Delay(250);
            }
            return null;
        }

        public int PromptCount() =>
            Resolve() is { } path ? CxSession.ReadRollout(path).Count(IsPrompt) : 0;

        public int CompletedTurns() =>
            Resolve() is { } path
                ? CxSession.ReadRollout(path).Count(r => r.EventType == "task_complete")
                : 0;

        private string? Resolve()
        {
            if (Rollout is not null) return Rollout;
            var wanted = Path.GetFullPath(cwd).TrimEnd('\\');
            foreach (var path in CxSession.Rollouts().Where(f => !before.Contains(f)))
            {
                var declared = CxSession.ReadRollout(path)
                    .FirstOrDefault(r => r.Type == "session_meta")?.Cwd;
                if (declared is not null
                    && string.Equals(declared.TrimEnd('\\'), wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return Rollout = path;
                }
            }
            return null;
        }
    }

    /// <summary>
    /// The prompt as the model received it, in either launch mode. TUI mode (what Antiphon runs)
    /// records it as an <c>item_completed</c> whose <c>item.type</c> is <c>UserMessage</c>;
    /// <c>codex exec</c> uses a flat <c>event_msg/user_message</c>. This is the row S1's
    /// <c>CodexTranscriptNormalizer</c> maps to a UserPrompt and the row CARD-0055 would confirm a
    /// delivery against, which is why every verdict here is made from it.
    /// </summary>
    private static bool IsPrompt(CodexRolloutRow r) =>
        r.Text is { Length: > 0 }
        && (r.EventType == "user_message"
            || (r.EventType == "item_completed"
                && string.Equals(r.ItemType, "UserMessage", StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// A body with unambiguous head and tail markers, so a partial arrival is identifiable as
    /// partial rather than merely short — the same shape the Grok canary uses, deliberately: the two
    /// measurements are meant to be read side by side.
    /// </summary>
    private static string BuildMarkedBody(string marker, int lines)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(marker).Append("-HEAD reply with exactly OK and nothing else\n");
        for (var i = 0; i < lines; i++)
            sb.Append(marker).Append('-').Append(i.ToString("D3"))
              .Append(" filler line to give the body real size, paths like C:/src/Antiphon/x.cs\n");
        sb.Append(marker).Append("-TAIL");
        return sb.ToString();
    }

    /// <summary>Renders control characters visibly so a log line shows what actually landed.</summary>
    private static string Visible(string? s) =>
        s is null ? "<null>" : s.Replace("\r", "<CR>").Replace("\n", "<LF>");

    private static string Truncate(string? s, int n) =>
        s is null ? "<null>" : s.Length <= n ? s : s[..n] + "...";
}
