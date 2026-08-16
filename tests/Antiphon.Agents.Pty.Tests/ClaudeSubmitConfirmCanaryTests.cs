using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// The two assumptions CARD-0055's shipped design bears its full weight on, measured against REAL
/// Claude. Neither had a pin before this file; if either is false, deployed code has a live defect.
///
/// <list type="number">
/// <item><b>Enter on an already-empty composer is a no-op.</b> The confirm loop re-presses Enter up
/// to <c>SubmitAttempts</c> times and NEVER re-types. If the first Enter really did submit, every
/// re-press lands on an empty composer — so if a stray Enter did anything at all, a retry could
/// double-submit, and for a channel reply that is a duplicate message to a human.</item>
/// <item><b>A COLLAPSED PASTE's JSONL user record carries the full body</b>, not the
/// <c>[Pasted text #N +M lines]</c> placeholder the composer shows (CARD-0037). The matcher compares
/// the body's head-200 normalized chars against the record text
/// (<see cref="PromptSubmissionMatch"/>), so if the record held only the placeholder every large
/// delivery would fail to confirm, retry three times and park.</item>
/// </list>
///
/// <para><b>Ground truth is the JSONL, not the screen.</b> Both canaries read the same field the
/// production path reads — <c>message.content</c> as a string or its <c>text</c> blocks, which is
/// exactly what <c>TranscriptNormalizer.FromUser</c> turns into the <c>UserPrompt</c> row the queue
/// polls. A body that reached Claude but landed anywhere else in the record (an attachment map, a
/// <c>pastedContents</c> side-table) is NOT confirmable by the shipped code, so it must fail here.</para>
///
/// <para>Headed, opt-in (<c>ANTIPHON_HEADED_TESTS=1</c>) and <c>[Explicit]</c>: they spend real
/// Claude turns and cannot run in CI. Their CI mirrors are
/// <c>FakeClaudeContractTests.Enter_on_an_empty_composer_submits_nothing</c> and
/// <c>…A_collapsed_paste_still_produces_composer_evidence</c> — the fake can model these behaviours
/// but only these canaries can measure them.</para>
/// </summary>
[NotInParallel("Headed")]
[Category("Headed")]
[Category("HeadedCanary")]
[Category("Card0055")]
[Explicit]
public class ClaudeSubmitConfirmCanaryTests
{
    private static readonly Regex DonePattern = new(@" for \d+s", RegexOptions.Compiled);
    private static readonly Regex PastePlaceholder = new(@"\[Pasted text #\d+ \+\d+ lines\]", RegexOptions.Compiled);

    /// <summary>
    /// Assumption (a). After a completed turn the composer is empty — the exact state a re-pressed
    /// Enter lands in — and the Enters below must produce NOTHING: no new user record, no new
    /// assistant record, no turn.
    /// </summary>
    [Test]
    public async Task Enter_on_an_empty_composer_is_a_no_op()
    {
        ClSession.SkipIfNotEligible();
        var sessionId = Guid.NewGuid().ToString("D");

        // The deployment runs modern (CARD-0037 step 4), so that is the pty this measures.
        await using var runner = new PtyAgentRunner("modern");
        var (app, args) = ClSession.BuildLaunch(
            ClSession.ResolveOrThrow(), "--dangerously-skip-permissions", "--session-id", sessionId);
        await runner.StartAsync(app, args, cols: 120, rows: 30, env: ClSession.HeadedSafeEnv());
        Console.WriteLine($"backend={runner.Backend?.Backend} reason={runner.Backend?.Reason}");
        var ready = await new ClaudeReadyDetector().WaitAsync(runner);
        if (!ready) throw new SkipTestException("real Claude TUI did not reach a ready state");
        runner.ClearLiveBuffer();

        // One real turn, so the JSONL exists and the composer is empty the way it is after a
        // SUCCESSFUL delivery.
        await runner.SendLineAsync("Reply with the single word PONG and nothing else.");
        (await runner.WaitForOutputAsync(s => DonePattern.IsMatch(s), TimeSpan.FromMinutes(3)))
            .ShouldBeTrue("the first turn must complete. Screen:\n" + runner.SnapshotScreen());

        var jsonlPath = await WaitForSessionJsonlAsync(sessionId);
        jsonlPath.ShouldNotBeNull($"session JSONL for {sessionId} must exist under ~/.claude/projects");
        Console.WriteLine($"Session JSONL: {jsonlPath}");

        // Let the turn's records finish landing before the baseline is taken.
        await Task.Delay(TimeSpan.FromSeconds(5));
        var before = CountRecords(jsonlPath!);
        Console.WriteLine($"BASELINE: user={before.User} assistant={before.Assistant}");

        // The re-presses a delivery would send: SubmitAttempts is 3, so two more after the first,
        // spaced like ReEnterIntervalSeconds. Deliberately unhurried — a fast double-tap could be
        // eaten as one keypress and would prove less than the real cadence does.
        for (var i = 0; i < 3; i++)
        {
            await runner.WriteAsync("\r");
            await Task.Delay(TimeSpan.FromSeconds(3));
        }

        // Anything a stray Enter started would show here.
        await Task.Delay(TimeSpan.FromSeconds(15));
        var after = CountRecords(jsonlPath!);
        Console.WriteLine($"AFTER 3 EMPTY ENTERS: user={after.User} assistant={after.Assistant}");
        Console.WriteLine("SCREEN (tail):\n" + Tail(runner.SnapshotScreen(), 800));

        await runner.SendLineAsync("/exit");
        await Task.WhenAny(runner.Exited, Task.Delay(TimeSpan.FromSeconds(10)));
        await runner.KillAsync(TimeSpan.FromSeconds(2));

        after.User.ShouldBe(before.User,
            "an Enter on an empty composer must record NO user prompt — the delivery retry re-presses "
            + "Enter up to 3 times and never re-types, so anything a stray Enter submits is a duplicate "
            + $"to whoever is on the other end. New records: {string.Join(" | ", NewUserTexts(jsonlPath!, before.User))}");
        after.Assistant.ShouldBe(before.Assistant,
            "an empty Enter must not start a turn either — a turn costs tokens and, mid-delivery, "
            + "would make the session read working while the queue waits on it");
    }

    /// <summary>
    /// Assumption (b). A body large enough for the composer to COLLAPSE it — the screen shows the
    /// placeholder and none of the body, which is why delivery verification had to learn the
    /// placeholder (CARD-0037) — must still be recorded in the JSONL in full, or transcript
    /// confirmation cannot see any large delivery land.
    ///
    /// <para>The collapse itself is asserted first: without it this test measures an ordinary paste
    /// and settles nothing, so it fails rather than passes vacuously.</para>
    /// </summary>
    [Test]
    public async Task A_collapsed_pastes_jsonl_record_carries_the_full_body_not_the_placeholder()
    {
        ClSession.SkipIfNotEligible();
        var sessionId = Guid.NewGuid().ToString("D");

        // MUST be modern: on the inbox conhost the bracketed-paste markers are stripped before the
        // TUI sees them, the body arrives as typing, and no paste — hence no collapse — happens at
        // all (CARD-0030). The deployment runs modern, which is why this assumption is load-bearing.
        await using var runner = new PtyAgentRunner("modern");
        var (app, args) = ClSession.BuildLaunch(
            ClSession.ResolveOrThrow(), "--dangerously-skip-permissions", "--session-id", sessionId);
        await runner.StartAsync(app, args, cols: 120, rows: 30, env: ClSession.HeadedSafeEnv());
        runner.Backend!.Backend.ShouldBe(PtyBackend.ModernConPty,
            "a collapsed paste needs the markers delivered; the inbox conhost strips them and there "
            + "is nothing to measure. Reason: " + runner.Backend!.Reason);
        var ready = await new ClaudeReadyDetector().WaitAsync(runner);
        if (!ready) throw new SkipTestException("real Claude TUI did not reach a ready state");
        runner.ClearLiveBuffer();

        var body = BuildCollapsingBody();
        Console.WriteLine($"body: {body.Length} chars, {body.Split('\n').Length} lines");

        // The production encoding, and the production shape: ONE write, no CR (CARD-0037 — the
        // measured ceilings are single-write ceilings).
        await runner.WriteAsync(PtyInputEncoding.EncodeBody(body));

        var collapsed = await runner.WaitForScreenAsync(
            s => PastePlaceholder.IsMatch(s), TimeSpan.FromSeconds(20));
        Console.WriteLine("COMPOSER AFTER PASTE (tail):\n" + Tail(runner.SnapshotScreen(), 600));
        collapsed.ShouldBeTrue(
            "the composer must COLLAPSE this paste to [Pasted text #N +M lines] — without a collapse "
            + "this canary measures an ordinary paste and settles nothing. Screen:\n"
            + runner.SnapshotScreen());

        await Task.Delay(20);
        await runner.WriteAsync("\r");
        (await runner.WaitForOutputAsync(s => DonePattern.IsMatch(s), TimeSpan.FromMinutes(4)))
            .ShouldBeTrue("the pasted body must submit and complete a turn. Screen:\n" + runner.SnapshotScreen());

        var jsonlPath = await WaitForSessionJsonlAsync(sessionId);
        jsonlPath.ShouldNotBeNull($"session JSONL for {sessionId} must exist under ~/.claude/projects");
        Console.WriteLine($"Session JSONL: {jsonlPath}");
        await Task.Delay(TimeSpan.FromSeconds(3));

        await runner.SendLineAsync("/exit");
        await Task.WhenAny(runner.Exited, Task.Delay(TimeSpan.FromSeconds(10)));
        await runner.KillAsync(TimeSpan.FromSeconds(2));

        // Exactly the field the production path reads. A body recorded anywhere else is invisible to
        // the shipped matcher, and reporting that as "the body is in the transcript" would be a lie.
        var prompts = UserPromptTexts(jsonlPath!);
        Console.WriteLine($"USER RECORDS: {prompts.Count}");
        foreach (var p in prompts)
            Console.WriteLine($"  [{p.Length} chars] {Truncate(p, 160)}");

        var confirming = prompts.FirstOrDefault(p => PromptSubmissionMatch.IsConfirmedBy(body, p));
        confirming.ShouldNotBeNull(
            "CARD-0055 D1: the collapsed paste's JSONL user record must carry the BODY, not the "
            + "placeholder — the delivery matcher compares the body's head-200 normalized chars "
            + "against this text, so a placeholder-only record means every large delivery fails to "
            + "confirm, retries 3 times and parks. Records seen: "
            + string.Join(" | ", prompts.Select(p => Truncate(p, 120))));

        PastePlaceholder.IsMatch(confirming!).ShouldBeFalse(
            "the confirming record must not be the placeholder text itself");
        // The head window is what is compared, so it is what must be intact.
        PromptSubmissionMatch.Normalize(confirming!)
            .ShouldContain(PromptSubmissionMatch.Normalize(body)[..PromptSubmissionMatch.MatchWindowChars]);
    }

    /// <summary>
    /// Multi-line and comfortably past any plausible collapse threshold, with the identifying marker
    /// in the FIRST line: the matcher compares the head window, so a body whose distinctive text
    /// lived in the tail would pass for the wrong reason.
    /// </summary>
    private static string BuildCollapsingBody()
    {
        var sb = new StringBuilder();
        sb.Append("CARD0055-HEAD-MARKER this is a transcript-confirmation canary; ");
        sb.Append("read nothing below and reply with exactly OK and nothing else.\n");
        for (var i = 0; i < 60; i++)
            sb.Append($"CARD0055-L{i:D3} ignorable filler line {new string('x', 40)}\n");
        sb.Append("CARD0055-TAIL-MARKER end of filler. Reply with exactly OK.");
        return sb.ToString();
    }

    private static async Task<string?> WaitForSessionJsonlAsync(string sessionId)
    {
        for (var i = 0; i < 20; i++)
        {
            var path = FindSessionJsonl(sessionId);
            if (path is not null) return path;
            await Task.Delay(1000);
        }
        return null;
    }

    private static string? FindSessionJsonl(string sessionId)
    {
        var projects = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
        if (!Directory.Exists(projects)) return null;
        return Directory
            .EnumerateFiles(projects, $"{sessionId}.jsonl", SearchOption.AllDirectories)
            .FirstOrDefault();
    }

    private static (int User, int Assistant) CountRecords(string jsonlPath)
    {
        var user = 0;
        var assistant = 0;
        foreach (var line in ReadLinesSafely(jsonlPath))
        {
            var type = TypeOf(line);
            if (type == "user") user++;
            else if (type == "assistant") assistant++;
        }
        return (user, assistant);
    }

    private static List<string> NewUserTexts(string jsonlPath, int skipFirstUserRecords)
    {
        var texts = new List<string>();
        var seen = 0;
        foreach (var line in ReadLinesSafely(jsonlPath))
        {
            if (TypeOf(line) != "user") continue;
            if (seen++ < skipFirstUserRecords) continue;
            texts.Add(Truncate(string.Join(" ", ContentTexts(line)), 200));
        }
        return texts;
    }

    /// <summary>
    /// Every user record's text, read the way <c>TranscriptNormalizer.FromUser</c> reads it:
    /// <c>message.content</c> as a string, or the <c>text</c> of each content block. isMeta records
    /// are dropped, exactly as the normalizer drops them.
    /// </summary>
    private static List<string> UserPromptTexts(string jsonlPath)
    {
        var texts = new List<string>();
        foreach (var line in ReadLinesSafely(jsonlPath))
        {
            if (TypeOf(line) != "user") continue;
            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.TryGetProperty("isMeta", out var meta) && meta.ValueKind == JsonValueKind.True)
                continue;
            texts.AddRange(ContentTexts(line));
        }
        return texts;
    }

    private static List<string> ContentTexts(string jsonLine)
    {
        var texts = new List<string>();
        using var doc = JsonDocument.Parse(jsonLine);
        if (!doc.RootElement.TryGetProperty("message", out var message)
            || !message.TryGetProperty("content", out var content))
            return texts;

        if (content.ValueKind == JsonValueKind.String)
        {
            if (content.GetString() is { } s) texts.Add(s);
        }
        else if (content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
                if (block.ValueKind == JsonValueKind.Object
                    && block.TryGetProperty("text", out var text)
                    && text.GetString() is { } t)
                {
                    texts.Add(t);
                }
        }
        return texts;
    }

    private static string? TypeOf(string jsonLine)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonLine);
            return doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IEnumerable<string> ReadLinesSafely(string jsonlPath)
    {
        // Claude is appending while we read; skip blanks and anything half-written.
        foreach (var line in File.ReadAllLines(jsonlPath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var ok = true;
            try { using var _ = JsonDocument.Parse(line); }
            catch (JsonException) { ok = false; }
            if (ok) yield return line;
        }
    }

    private static string Truncate(string? s, int n) =>
        s is null ? "<null>" : s.Length <= n ? s : s[..n] + "…";

    private static string Tail(string s, int n) => s.Length <= n ? s : s[^n..];
}
