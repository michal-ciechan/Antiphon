using System.Diagnostics;
using System.Globalization;
using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-1224 S1. Linux <see cref="PtyBackend.UnixPty"/> ladder through the staged fakes.
/// Set <c>ANTIPHON_C1224_RECORD</c> to an absolute path to write the receipt from
/// <see cref="Delivery_latency_is_recorded"/>. Checkpoint runs leave that variable unset
/// and only read the committed receipt.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class UnixPtyDeliveryEnvelopeTests
{
    private const string RecordVariable = "ANTIPHON_C1224_RECORD";
    private const string ReceiptName = "2026-11-09-card-1224-linux-delivery-envelope-receipt.md";
    private const int ChunkBytes = 1024;
    private static readonly int[] SingleSizes = [64, 900, 1024, 1350, 4096, 16384, 43200, 86400];
    private static readonly int[] PacedSizes = [1350, 43200, 86400];
    private const string Line1 = "LINE1 alpha";
    private const string Line2 = "LINE2 beta";
    private const string Line3 = "LINE3 gamma";
    private const string Line4 = "LINE4 delta";

    [Test]
    [Timeout(180_000)]
    public async Task Control_body_arrives_whole()
    {
        RequireLinux();
        var receipt = LoadReceipt();
        foreach (var rep in new[] { 1, 2 })
        {
            var live = await MeasureClaudeAsync(64, rep, paced: false, debug: false);
            live.Whole.ShouldBeTrue($"64-byte control rep {rep} must arrive whole (submits={live.SubmitCount})");
            receipt.Require("single", 64, rep).Whole.ShouldBeTrue($"receipt control rep {rep}");
            live.Whole.ShouldBe(receipt.Require("single", 64, rep).Whole);
        }
    }

    [Test]
    [Timeout(480_000)]
    public async Task Single_write_ladder_matches_the_receipt()
    {
        RequireLinux();
        var receipt = LoadReceipt();
        foreach (var size in SingleSizes)
        {
            for (var rep = 1; rep <= 2; rep++)
            {
                var live = await MeasureClaudeAsync(size, rep, paced: false, debug: false);
                var expected = receipt.Require("single", size, rep);
                live.Whole.ShouldBe(expected.Whole, $"single {size} rep {rep} submits={live.SubmitCount}");
            }
        }
    }

    [Test]
    [Timeout(360_000)]
    public async Task Paced_ladder_matches_the_receipt()
    {
        RequireLinux();
        var receipt = LoadReceipt();
        foreach (var size in PacedSizes)
        {
            for (var rep = 1; rep <= 2; rep++)
            {
                var live = await MeasureClaudeAsync(size, rep, paced: true, debug: false);
                var expected = receipt.Require("paced", size, rep);
                live.Whole.ShouldBe(expected.Whole, $"paced {size} rep {rep} submits={live.SubmitCount}");
            }
        }
    }

    [Test]
    [Timeout(120_000)]
    public async Task Bracketed_paste_markers_match_the_receipt()
    {
        RequireLinux();
        var receipt = LoadReceipt();
        var live = await MeasureMarkersAsync();
        live.MarkersSurvived.ShouldBe(receipt.MarkersSurvived,
            $"paste start seen={live.MarkerSeen} submits={live.SubmitCount}");
    }

    [Test]
    [Timeout(120_000)]
    public async Task Multiline_lf_body_matches_the_receipt()
    {
        RequireLinux();
        var receipt = LoadReceipt();
        var live = await MeasureMarkersAsync();
        live.SubmitCount.ShouldBe(receipt.MultilineSubmitCount);
    }

    [Test]
    [Timeout(120_000)]
    public async Task Grok_newline_join_matches_the_receipt()
    {
        RequireLinux();
        var receipt = LoadReceipt();
        var kept = await MeasureGrokNewlinesAsync();
        kept.ShouldBe(receipt.GrokNewlinesKept);
    }

    [Test]
    [Timeout(720_000)]
    public async Task Delivery_latency_is_recorded()
    {
        RequireLinux();
        var recordPath = Environment.GetEnvironmentVariable(RecordVariable);
        if (!string.IsNullOrWhiteSpace(recordPath))
            await RecordAsync(recordPath);

        var path = ReceiptPath();
        var text = File.ReadAllText(path);
        text.ShouldContain("latencyControlMs:");
        text.ShouldContain("latencyBriefMs:");
        var receipt = LoadReceipt();
        receipt.LatencyControlMs.ShouldBeGreaterThanOrEqualTo(0);
        receipt.LatencyBriefMs.ShouldBeGreaterThanOrEqualTo(0);
    }

    private static async Task RecordAsync(string recordPath)
    {
        if (!Path.IsPathRooted(recordPath))
            throw new InvalidOperationException($"{RecordVariable} must be an absolute path.");
        RefuseClip();
        var trials = new List<Trial>();
        var controlWrites = new List<long>();
        foreach (var size in SingleSizes)
        {
            for (var rep = 1; rep <= 2; rep++)
            {
                var live = await MeasureClaudeAsync(size, rep, paced: false, debug: false);
                trials.Add(new Trial("single", size, rep, live.Whole));
                if (size == 64) controlWrites.Add(live.WriteMilliseconds);
                Console.WriteLine($"trial: single {size} {rep} {(live.Whole ? "WHOLE" : "LOST")}");
            }
        }

        if (controlWrites.Count != 2 || trials.Any(row => row.Mode == "single" && row.Size == 64 && !row.Whole))
            throw new InvalidOperationException("64-byte control was not whole; receipt not written.");

        foreach (var size in PacedSizes)
        {
            for (var rep = 1; rep <= 2; rep++)
            {
                var live = await MeasureClaudeAsync(size, rep, paced: true, debug: false);
                trials.Add(new Trial("paced", size, rep, live.Whole));
                Console.WriteLine($"trial: paced {size} {rep} {(live.Whole ? "WHOLE" : "LOST")}");
            }
        }

        var markers = await MeasureMarkersAsync();
        Console.WriteLine($"markersSurvived: {(markers.MarkersSurvived ? "true" : "false")} submits={markers.SubmitCount}");
        var grokKept = await MeasureGrokNewlinesAsync();
        Console.WriteLine($"grokNewlinesKept: {(grokKept ? "true" : "false")}");
        var ceilings = Derive(trials, markers.MarkersSurvived);
        var briefWrites = new List<long>();
        for (var rep = 1; rep <= 2; rep++)
        {
            var live = await MeasureClaudeAsync(ceilings.Brief, rep, paced: false, debug: false);
            briefWrites.Add(live.WriteMilliseconds);
        }

        var text = FormatReceipt(ceilings, markers.MarkersSurvived, grokKept, markers.SubmitCount,
            MaxMilliseconds(controlWrites), MaxMilliseconds(briefWrites), trials);
        var destination = Path.GetFullPath(recordPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".writing";
        File.WriteAllText(temporary, text);
        File.Move(temporary, destination, overwrite: true);
        Console.WriteLine(
            $"recorded single={ceilings.Single} brief={ceilings.Brief} reply={ceilings.Reply} -> {destination}");
    }

    private static async Task<Arrival> MeasureClaudeAsync(int size, int rep, bool paced, bool debug)
    {
        var exe = RequireFake("fakeclaude");
        var body = BuildBody(size, rep, paced);
        var lines = body.Split('\n');
        var env = ChildEnvironment(debug ? [("ANTIPHON_FAKE_DEBUG_INPUT", "1")] : []);
        await using var runner = await LaunchAsync(exe, "Fake Claude ready", env);
        try
        {
            var encoded = PtyInputEncoding.EncodeBody(body);
            if (paced)
            {
                encoded.StartsWith(PtyInputEncoding.PasteStart, StringComparison.Ordinal).ShouldBeTrue();
                encoded.EndsWith(PtyInputEncoding.PasteEnd, StringComparison.Ordinal).ShouldBeTrue();
            }

            var clock = Stopwatch.StartNew();
            if (!paced)
            {
                await runner.WriteAsync(encoded);
            }
            else
            {
                for (var offset = 0; offset < encoded.Length;)
                {
                    var count = Math.Min(ChunkBytes, encoded.Length - offset);
                    await runner.WriteAsync(encoded.Substring(offset, count));
                    offset += count;
                    if (offset < encoded.Length)
                        await Task.Delay(25);
                }
            }

            clock.Stop();
            await Task.Delay(200);
            await runner.WriteAsync("\r");
            var sawSubmit = await runner.WaitForOutputAsync(
                static text => text.Contains("SUBMITTED:", StringComparison.Ordinal),
                TimeSpan.FromSeconds(60));
            var output = runner.SnapshotText();
            var submits = CountSubmitted(output);
            var whole = sawSubmit && submits == 1 && lines.All(line => output.Contains(line, StringComparison.Ordinal));
            return new Arrival(whole, submits, PasteStartSeen(output), clock.ElapsedMilliseconds);
        }
        finally
        {
            await runner.KillAsync(TimeSpan.FromSeconds(2));
        }
    }

    private static async Task<Arrival> MeasureMarkersAsync()
    {
        var exe = RequireFake("fakeclaude");
        var body = Line1 + "\r" + Line2 + "\r" + Line3 + "\r" + Line4;
        var env = ChildEnvironment([("ANTIPHON_FAKE_DEBUG_INPUT", "1")]);
        await using var runner = await LaunchAsync(exe, "Fake Claude ready", env);
        try
        {
            await runner.WriteAsync(PtyInputEncoding.EncodeBody(body));
            await Task.Delay(200);
            await runner.WriteAsync("\r");
            await runner.WaitForOutputAsync(
                static text => text.Contains("SUBMITTED:", StringComparison.Ordinal),
                TimeSpan.FromSeconds(30));
            var output = runner.SnapshotText();
            var submits = CountSubmitted(output);
            var payloads = SubmittedPayloads(output).ToArray();
            var oneSubmitHasEveryLine = payloads.Length == 1
                && payloads[0].Contains(Line1, StringComparison.Ordinal)
                && payloads[0].Contains(Line2, StringComparison.Ordinal)
                && payloads[0].Contains(Line3, StringComparison.Ordinal)
                && payloads[0].Contains(Line4, StringComparison.Ordinal);
            var seen = PasteStartSeen(output);
            return new Arrival(oneSubmitHasEveryLine && seen, submits, seen, 0);
        }
        finally
        {
            await runner.KillAsync(TimeSpan.FromSeconds(2));
        }
    }

    private static async Task<bool> MeasureGrokNewlinesAsync()
    {
        var exe = RequireFake("fakegrok");
        var home = Path.Combine(Path.GetTempPath(), "c1224-grok-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(home);
        var body = Line1 + "\n" + Line2 + "\n" + Line3 + "\n" + Line4;
        var env = ChildEnvironment(
        [
            ("ANTIPHON_FAKE_LF_ENTER", "1"),
            ("GROK_HOME", home),
        ]);
        await using var runner = await LaunchAsync(exe, "Fake Grok ready", env);
        try
        {
            await runner.WriteAsync(PtyInputEncoding.EncodeBody(body));
            await Task.Delay(200);
            await runner.WriteAsync("\r");
            await runner.WaitForOutputAsync(
                static text => text.Contains("SUBMITTED:", StringComparison.Ordinal),
                TimeSpan.FromSeconds(30));
            return SubmittedPayloads(runner.SnapshotText()).Any(payload => payload.Contains("\\n", StringComparison.Ordinal));
        }
        finally
        {
            await runner.KillAsync(TimeSpan.FromSeconds(2));
            try { Directory.Delete(home, recursive: true); } catch (IOException) { }
        }
    }

    private static async Task<PtyAgentRunner> LaunchAsync(
        string exe, string banner, Dictionary<string, string> env)
    {
        RefuseClip();
        var runner = new PtyAgentRunner();
        await runner.StartAsync(exe, [], cols: 120, rows: 30, env: env);
        runner.Backend.ShouldNotBeNull();
        runner.Backend!.Backend.ShouldBe(PtyBackend.UnixPty, runner.Backend.ToString());
        var ready = await runner.WaitForOutputAsync(
            text => text.Contains(banner, StringComparison.Ordinal),
            TimeSpan.FromSeconds(45));
        ready.ShouldBeTrue($"banner missing from {Tail(runner.SnapshotText())}");
        runner.ClearLiveBuffer();
        return runner;
    }

    private static string BuildBody(int size, int rep, bool paced)
    {
        if (!paced)
        {
            var tag = string.Create(CultureInfo.InvariantCulture, $"S{size:D5}R{rep} ");
            if (tag.Length >= size)
                throw new InvalidOperationException($"marker does not fit in {size} bytes");
            return tag + new string('x', size - tag.Length);
        }

        var chars = new char[size];
        Array.Fill(chars, 'x');
        for (var index = 63; index < size - 1; index += 64)
            chars[index] = '\n';
        var line = 0;
        var position = 0;
        while (position < size)
        {
            var end = position;
            while (end < size && chars[end] != '\n') end++;
            var tag = string.Create(CultureInfo.InvariantCulture, $"L{line:D4}R{rep} ");
            if (end - position >= tag.Length)
                tag.AsSpan().CopyTo(chars.AsSpan(position));
            line++;
            if (end >= size) break;
            position = end + 1;
        }

        return new string(chars);
    }

    private static Ceilings Derive(IReadOnlyList<Trial> trials, bool markersSurvived)
    {
        var wholeSingle = LargestWhole(trials, "single", SingleSizes);
        var wholePaced = LargestWhole(trials, "paced", PacedSizes);
        if (!markersSurvived || wholeSingle < 1024 || wholePaced < 1024)
            return new Ceilings(1024, 900, 3000);
        var single = Math.Min(wholeSingle, 86400);
        var brief = Math.Min(wholePaced, single / 2);
        if (brief < 1024)
            return new Ceilings(1024, 900, 3000);
        return new Ceilings(single, brief, brief / 3);
    }

    private static int LargestWhole(IReadOnlyList<Trial> trials, string mode, int[] sizes)
    {
        var whole = 0;
        foreach (var size in sizes)
        {
            var reps = trials.Where(row => row.Mode == mode && row.Size == size).ToArray();
            if (reps.Length == 2 && reps.All(row => row.Whole) && size > whole)
                whole = size;
        }

        return whole;
    }

    private static string FormatReceipt(
        Ceilings ceilings, bool markersSurvived, bool grokNewlinesKept, int submitCount,
        long latencyControlMs, long latencyBriefMs, IReadOnlyList<Trial> trials)
    {
        var lines = new List<string>
        {
            "measurementId: card-1224-unixpty",
            "oracle: fake-agent",
            "markersSurvived: " + (markersSurvived ? "true" : "false"),
            "singleWriteBytes: " + ceilings.Single.ToString(CultureInfo.InvariantCulture),
            "briefInlineMaxBytes: " + ceilings.Brief.ToString(CultureInfo.InvariantCulture),
            "replyInlineMaxChars: " + ceilings.Reply.ToString(CultureInfo.InvariantCulture),
            "grokNewlinesKept: " + (grokNewlinesKept ? "true" : "false"),
            "codex: unmeasured",
            "latencyControlMs: " + latencyControlMs.ToString(CultureInfo.InvariantCulture),
            "latencyBriefMs: " + latencyBriefMs.ToString(CultureInfo.InvariantCulture),
            "multilineSubmitCount: " + submitCount.ToString(CultureInfo.InvariantCulture),
        };
        foreach (var trial in trials)
            lines.Add($"trial: {trial.Mode} {trial.Size.ToString(CultureInfo.InvariantCulture)} {trial.Rep.ToString(CultureInfo.InvariantCulture)} {(trial.Whole ? "WHOLE" : "LOST")}");
        return string.Join('\n', lines) + "\n";
    }

    private static Receipt LoadReceipt()
    {
        var path = ReceiptPath();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var trials = new List<Trial>();
        foreach (var raw in File.ReadAllLines(path))
        {
            if (raw.Length == 0) continue;
            if (raw.StartsWith("trial: ", StringComparison.Ordinal))
            {
                var parts = raw.Split(' ');
                if (parts.Length != 5 || (parts[1] != "single" && parts[1] != "paced")
                    || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var size)
                    || !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var rep)
                    || (parts[4] != "WHOLE" && parts[4] != "LOST"))
                    throw new InvalidOperationException("malformed trial line: " + raw);
                trials.Add(new Trial(parts[1], size, rep, parts[4] == "WHOLE"));
                continue;
            }

            var split = raw.IndexOf(':');
            if (split <= 0) throw new InvalidOperationException("malformed receipt line: " + raw);
            values[raw[..split]] = raw[(split + 1)..].Trim();
        }

        values["measurementId"].ShouldBe("card-1224-unixpty");
        values["oracle"].ShouldBe("fake-agent");
        values["codex"].ShouldBe("unmeasured");
        return new Receipt(
            ParseBool(values, "markersSurvived"),
            ParseInt(values, "singleWriteBytes"),
            ParseInt(values, "briefInlineMaxBytes"),
            ParseInt(values, "replyInlineMaxChars"),
            ParseBool(values, "grokNewlinesKept"),
            ParseInt(values, "latencyControlMs"),
            ParseInt(values, "latencyBriefMs"),
            ParseInt(values, "multilineSubmitCount"),
            trials);
    }

    private static string ReceiptPath()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "docs", "superpowers", "plans", ReceiptName);
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
        }

        throw new FileNotFoundException(
            "card-1224 receipt is not committed at docs/superpowers/plans/" + ReceiptName);
    }

    private static void RequireLinux()
    {
        if (!OperatingSystem.IsLinux())
            throw new SkipTestException("UnixPty delivery envelope runs on Linux");
        OperatingSystem.IsLinux().ShouldBeTrue();
    }

    private static string RequireFake(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, name, name);
        if (!File.Exists(path))
            throw new FileNotFoundException($"staged {name} is absent on Linux", path);
        return path;
    }

    private static void RefuseClip()
    {
        var clip = Environment.GetEnvironmentVariable("ANTIPHON_FAKE_STDIN_CLIP");
        if (!string.IsNullOrEmpty(clip))
            throw new InvalidOperationException("ANTIPHON_FAKE_STDIN_CLIP must stay unset.");
    }

    private static Dictionary<string, string> ChildEnvironment((string Key, string Value)[] extra)
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
                env[key] = value;
        }

        env.Remove("ANTIPHON_FAKE_STDIN_CLIP");
        env.Remove(RecordVariable);
        foreach (var (key, value) in extra)
            env[key] = value;
        return env;
    }

    private static bool PasteStartSeen(string output) =>
        output.Contains("<27>[200~", StringComparison.Ordinal)
        || output.Contains("\u001b[200~", StringComparison.Ordinal);

    private static int CountSubmitted(string output)
    {
        var count = 0;
        var index = 0;
        const string token = "SUBMITTED:";
        while ((index = output.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }

    private static IEnumerable<string> SubmittedPayloads(string output)
    {
        const string token = "SUBMITTED:";
        var index = 0;
        while ((index = output.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            var start = index + token.Length;
            var end = output.IndexOfAny(['\r', '\n'], start);
            if (end < 0) end = output.Length;
            yield return output[start..end];
            index = end;
        }
    }

    private static int ParseInt(Dictionary<string, string> values, string key)
    {
        values.TryGetValue(key, out var text).ShouldBeTrue($"receipt missing {key}");
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number).ShouldBeTrue(key);
        return number;
    }

    private static bool ParseBool(Dictionary<string, string> values, string key)
    {
        values.TryGetValue(key, out var text).ShouldBeTrue($"receipt missing {key}");
        if (text == "true") return true;
        if (text == "false") return false;
        throw new InvalidOperationException($"receipt {key} is not true or false");
    }

    private static long MaxMilliseconds(IReadOnlyList<long> samples) => samples.Max();

    private static string Tail(string text)
    {
        if (text.Length <= 400) return text;
        return text[^400..];
    }

    private readonly record struct Arrival(bool Whole, int SubmitCount, bool MarkerSeen, long WriteMilliseconds)
    {
        public bool MarkersSurvived => MarkerSeen && SubmitCount == 1 && Whole;
    }

    private readonly record struct Trial(string Mode, int Size, int Rep, bool Whole);

    private readonly record struct Ceilings(int Single, int Brief, int Reply);

    private sealed record Receipt(
        bool MarkersSurvived,
        int SingleWriteBytes,
        int BriefInlineMaxBytes,
        int ReplyInlineMaxChars,
        bool GrokNewlinesKept,
        int LatencyControlMs,
        int LatencyBriefMs,
        int MultilineSubmitCount,
        IReadOnlyList<Trial> Trials)
    {
        public Trial Require(string mode, int size, int rep) =>
            Trials.Single(row => row.Mode == mode && row.Size == size && row.Rep == rep);
    }
}
