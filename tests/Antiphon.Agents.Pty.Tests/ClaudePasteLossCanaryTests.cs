using System.Text;
using System.Text.Json;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-0027's reproduction: type a marked body into a REAL Claude TUI through the production
/// encoding, then diff what Claude recorded in its own JSONL against what we sent. Ground truth —
/// no screen matching, no head-or-tail heuristic, byte offsets.
///
/// <para>Every line of the body carries its index (<c>L0000</c>…), so a shortfall names the exact
/// span that vanished instead of "lengths differ". <see cref="Diff"/> reports the surviving runs;
/// under the measured mechanism the survivors are whole ~1 KB read chunks, and their COUNT is the
/// number of event-loop turns the paste was delivered across.</para>
///
/// <para><b>This is a reproduction, and RED is its correct state while the defect exists.</b> It is
/// <c>[Explicit]</c> and headed-gated so it never runs in a normal suite — unlike the standing red
/// test the 2026-08-10 investigation found, which sat in the default run and read as noise. Run it
/// deliberately; green here means the receiving TUI stopped truncating.</para>
///
/// Headed and opt-in (<c>ANTIPHON_HEADED_TESTS=1</c>): it spends real Claude turns. Prefer
/// <see cref="ClaudeComposerCaptureProbeTests"/>, which measures the same loss for free.
/// </summary>
[NotInParallel("Headed")]
[Category("Card0027")]
[Explicit]
public class ClaudePasteLossCanaryTests
{
    private static readonly StringBuilder Report = new();

    private static void Line(string s)
    {
        Console.WriteLine(s);
        Report.AppendLine(s);
    }

    private static void Flush(string name)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "TestOutput", "card-0027");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name + ".txt"), Report.ToString());
        Console.WriteLine($"[card-0027] wrote {Path.Combine(dir, name + ".txt")}");
        Report.Clear();
    }

    /// <summary>
    /// Sweeps body sizes down ONE long-lived session, the way delegation actually delivers, and
    /// records for each one exactly which marked lines reached Claude's transcript.
    /// </summary>
    [Test]
    public async Task Marked_bodies_into_real_claude_are_diffed_against_its_transcript()
    {
        ClSession.SkipIfNotEligible();
        var sessionId = Guid.NewGuid().ToString("D");

        await using var runner = new PtyAgentRunner();
        var (app, args) = ClSession.BuildLaunch(
            ClSession.ResolveOrThrow(), "--dangerously-skip-permissions", "--session-id", sessionId);
        await runner.StartAsync(app, args, cols: 120, rows: 30, env: ClSession.HeadedSafeEnv());
        var ready = await new ClaudeReadyDetector().WaitAsync(runner);
        if (!ready) throw new SkipTestException("real Claude TUI did not reach a ready state");
        runner.ClearLiveBuffer();

        Line($"session={sessionId}");
        Line("trial\tsentBytes\tgotBytes\tlinesSent\tlinesGot\tsurvivingRuns\tfirstGap");

        var sizes = new[] { 1402, 2320, 1402, 5185, 4262, 2320 };
        var sent = new List<(int Trial, string Body)>();

        for (var trial = 0; trial < sizes.Length; trial++)
        {
            var body = BuildProbeBody(trial, sizes[trial]);
            sent.Add((trial, body));

            runner.ClearLiveBuffer();
            await runner.WriteAsync(PtyInputEncoding.EncodeBody(body));
            await Task.Delay(20);
            await runner.WriteAsync("\r");

            var done = await runner.WaitForOutputAsync(
                s => System.Text.RegularExpressions.Regex.IsMatch(s, @" for \d+s"),
                TimeSpan.FromMinutes(3));
            if (!done) Line($"trial {trial}: NO TURN END — screen:\n{Tail(runner.SnapshotScreen(), 400)}");
        }

        await runner.SendLineAsync("/exit");
        await Task.WhenAny(runner.Exited, Task.Delay(TimeSpan.FromSeconds(10)));
        await runner.KillAsync(TimeSpan.FromSeconds(2));

        string? jsonlPath = null;
        for (var i = 0; i < 20 && jsonlPath is null; i++)
        {
            jsonlPath = FindSessionJsonl(sessionId);
            if (jsonlPath is null) await Task.Delay(1000);
        }
        jsonlPath.ShouldNotBeNull($"session JSONL for {sessionId} must exist under ~/.claude/projects");
        Line($"jsonl={jsonlPath}");

        var userTexts = ReadUserTexts(jsonlPath!);
        Line($"userRecords={userTexts.Count}");

        var anyLoss = false;
        foreach (var (trial, body) in sent)
        {
            // Each body carries its own trial tag, so a delivery is matched by content, never by
            // position — a body that lost its head is still found by any surviving marked line.
            var arrived = userTexts.FirstOrDefault(t => t.Contains($"T{trial:D2}-L"));
            if (arrived is null)
            {
                Line($"{trial}\t{body.Length}\t0\t{CountLines(body, trial)}\t0\tNONE\ttotal loss");
                anyLoss = true;
                continue;
            }

            var (linesSent, linesGot, runs, firstGap) = Diff(body, arrived, trial);
            Line($"{trial}\t{body.Length}\t{arrived.Length}\t{linesSent}\t{linesGot}\t{runs}\t{firstGap}");
            if (linesGot != linesSent) anyLoss = true;
        }

        Flush("R1-real-claude");

        anyLoss.ShouldBeFalse(
            "a body typed into real Claude must reach its transcript whole — see the table above for "
            + "which marked spans vanished (CARD-0027)");
    }

    /// <summary>
    /// A body whose every line names its trial and index, ending with an instruction so the turn is
    /// cheap. Roughly <paramref name="targetBytes"/> long.
    /// </summary>
    private static string BuildProbeBody(int trial, int targetBytes)
    {
        const string tail = "\nIgnore the lines above. Reply with exactly OK and nothing else.";
        var sb = new StringBuilder();
        var i = 0;
        while (sb.Length + tail.Length < targetBytes)
        {
            sb.Append($"T{trial:D2}-L{i:D4} {new string('x', 16)}\n");
            i++;
        }
        sb.Append(tail);
        return sb.ToString();
    }

    private static int CountLines(string body, int trial) =>
        System.Text.RegularExpressions.Regex.Matches(body, $@"T{trial:D2}-L(\d{{4}}) ").Count;

    /// <summary>Which marked lines survived, and as how many contiguous runs.</summary>
    private static (int Sent, int Got, string Runs, string FirstGap) Diff(string body, string arrived, int trial)
    {
        var pattern = $@"T{trial:D2}-L(\d{{4}}) ";
        var sentIdx = System.Text.RegularExpressions.Regex.Matches(body, pattern)
            .Select(m => int.Parse(m.Groups[1].Value)).ToList();
        var gotIdx = System.Text.RegularExpressions.Regex.Matches(arrived, pattern)
            .Select(m => int.Parse(m.Groups[1].Value)).ToHashSet();

        var runs = new List<(int Lo, int Hi)>();
        foreach (var i in sentIdx.Where(gotIdx.Contains))
        {
            if (runs.Count > 0 && runs[^1].Hi == i - 1) runs[^1] = (runs[^1].Lo, i);
            else runs.Add((i, i));
        }

        var firstGap = "none";
        var missing = sentIdx.Where(i => !gotIdx.Contains(i)).ToList();
        if (missing.Count > 0)
        {
            // Byte offset of the first missing line in the body — the cut point.
            var m = System.Text.RegularExpressions.Regex.Match(body, $@"T{trial:D2}-L{missing[0]:D4} ");
            var offset = Encoding.UTF8.GetByteCount(body[..m.Index]);
            firstGap = $"line {missing[0]} @ byte {offset} (missing {missing.Count})";
        }

        return (sentIdx.Count, gotIdx.Count,
            string.Join(",", runs.Select(r => $"{r.Lo}-{r.Hi}")), firstGap);
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

    /// <summary>
    /// Every user text in the transcript, including pasted-content attachments — a large paste is
    /// recorded as an attachment rather than inline, and that is where the body actually lands.
    /// </summary>
    private static List<string> ReadUserTexts(string jsonlPath)
    {
        var texts = new List<string>();
        foreach (var line in File.ReadLines(jsonlPath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonDocument doc;
            try { doc = JsonDocument.Parse(line); }
            catch (JsonException) { continue; }
            using (doc)
            {
                Collect(doc.RootElement, texts, 0);
            }
        }
        return texts;
    }

    // The paste may be recorded inline, as a content block, or inside a pastedContents map — walk
    // the whole record rather than guessing the shape, and let the trial tag find it.
    private static void Collect(JsonElement el, List<string> into, int depth)
    {
        if (depth > 8) return;
        switch (el.ValueKind)
        {
            case JsonValueKind.String:
                if (el.GetString() is { Length: > 8 } s && s.Contains("-L0")) into.Add(s);
                break;
            case JsonValueKind.Object:
                foreach (var p in el.EnumerateObject()) Collect(p.Value, into, depth + 1);
                break;
            case JsonValueKind.Array:
                foreach (var i in el.EnumerateArray()) Collect(i, into, depth + 1);
                break;
        }
    }

    private static string Tail(string s, int n) => s.Length <= n ? s : s[^n..];
}
