using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

internal static class GrokStartupFixture
{
    private static readonly string PathName = Path.Combine(AppContext.BaseDirectory,
        "Agents", "Fixtures", "card0778", "startup-frames.json");

    public static JsonDocument Read() => JsonDocument.Parse(File.ReadAllText(PathName));

    public static JsonElement Capture(JsonDocument document, string prefix) => document.RootElement
        .GetProperty("captures").EnumerateObject().Single(x => x.Name.StartsWith(prefix, StringComparison.Ordinal)).Value;

    public static string Screen(JsonElement capture, int afterChunk) => capture.GetProperty("checkpoints")
        .EnumerateArray().Single(x => x.GetProperty("afterChunk").GetInt32() == afterChunk)
        .GetProperty("screen").GetString()!;

    public static string ReadyScreen()
    {
        using var document = Read();
        return Screen(Capture(document, "idle-"), 40);
    }
}

[Category("Unit")]
public class GrokStartupReadinessTests
{
    [Test]
    public void Captured_idle_frames_stay_ready_while_spinner_redraws()
    {
        using var document = GrokStartupFixture.Read();
        var capture = GrokStartupFixture.Capture(document, "idle-");
        var chunks = capture.GetProperty("chunks").EnumerateArray().ToArray();
        var checkpoints = capture.GetProperty("checkpoints").EnumerateArray()
            .ToDictionary(x => x.GetProperty("afterChunk").GetInt32());
        capture.GetProperty("cols").GetInt32().ShouldBe(120);
        capture.GetProperty("rows").GetInt32().ShouldBe(30);
        chunks.Length.ShouldBe(97);
        checkpoints.Count.ShouldBe(68);
        var chunkText = string.Concat(chunks.Select(x => x.GetProperty("text").GetString()));
        Digest(chunkText).ShouldBe("bedd9d072a6ed4f07e864e4ad868fb322a1ffb82a0ca8b1b274b086be14e2ac1");
        Digest(string.Join('\0', capture.GetProperty("checkpoints").EnumerateArray()
            .Select(x => x.GetProperty("screen").GetString()))).ShouldBe(
            "cf1b3ede1d73cb24adacad5d50c7a9227957f14d648fad8305868177fea2256a");
        var terminal = new TerminalScreen(120, 30);
        string? identity = null;
        double firstMs = 0, lastMs = 0;
        for (var i = 0; i < chunks.Length; i++)
        {
            chunks[i].GetProperty("index").GetInt32().ShouldBe(i);
            terminal.Feed(chunks[i].GetProperty("text").GetString()!);
            if (!checkpoints.TryGetValue(i, out var checkpoint)) continue;
            var rendered = terminal.GetScreenText();
            rendered.ShouldBe(checkpoint.GetProperty("screen").GetString());
            if (i is < 22 or > 48) continue;
            var observation = GrokStartupScreen.Classify(rendered);
            observation.IsReady.ShouldBeTrue($"captured idle chunk {i}");
            if (identity is null) { identity = observation.Region; firstMs = checkpoint.GetProperty("elapsedMs").GetDouble(); }
            else observation.Region.ShouldBe(identity);
            lastMs = checkpoint.GetProperty("elapsedMs").GetDouble();
        }
        (lastMs - firstMs).ShouldBeGreaterThan(1000);
    }

    [Test]
    public void Unknown_or_blocked_current_frames_never_become_ready()
    {
        using var document = GrokStartupFixture.Read();
        foreach (var (prefix, expected) in new[]
        {
            ("startup-", "StartingSession"), ("idle-", "ComposerUnavailable")
        })
        {
            var capture = GrokStartupFixture.Capture(document, prefix);
            var found = capture.GetProperty("checkpoints").EnumerateArray()
                .First(x => x.GetProperty("expectedReason").GetString() == expected);
            var actual = GrokStartupScreen.Classify(found.GetProperty("screen").GetString());
            actual.IsReady.ShouldBeFalse();
            actual.Reason.ToString().ShouldBe(expected);
        }
        foreach (var synthetic in document.RootElement.GetProperty("synthetic").EnumerateArray())
        {
            if (synthetic.GetProperty("id").GetString() == "syn-nonempty-composer") continue;
            var expected = synthetic.GetProperty("expectedReason").GetString();
            if (expected == "Ready") continue;
            var actual = GrokStartupScreen.Classify(synthetic.GetProperty("screen").GetString());
            actual.IsReady.ShouldBeFalse();
            actual.Reason.ToString().ShouldBe(expected);
        }
        var otherSize = document.RootElement.GetProperty("synthetic").EnumerateArray()
            .Single(x => x.GetProperty("id").GetString() == "syn-other-size-120x29");
        otherSize.GetProperty("label").GetString().ShouldBe("synthetic");
        otherSize.GetProperty("rows").GetInt32().ShouldBe(29);
    }

    [Test]
    public void Current_frame_overrides_raw_history()
    {
        using var document = GrokStartupFixture.Read();
        var synthetic = document.RootElement.GetProperty("synthetic").EnumerateArray().ToArray();
        var signin = synthetic.Single(x => x.GetProperty("id").GetString() == "syn-stale-raw-ready-current-signin");
        GrokStartupScreen.Classify(signin.GetProperty("screen").GetString(), GrokStartupFixture.ReadyScreen())
            .Reason.ShouldBe(GrokStartupReason.SignIn);
        var trust = synthetic.Single(x => x.GetProperty("id").GetString() == "syn-stale-raw-trust-current-ready");
        GrokStartupScreen.Classify(trust.GetProperty("screen").GetString(),
            trust.GetProperty("rawHistoryText").GetString()).Reason.ShouldBe(GrokStartupReason.Ready);
    }

    [Test]
    public void Composer_change_or_blocker_restarts_settle()
    {
        var tracker = new GrokReadyTracker(TimeSpan.FromMilliseconds(1000));
        var ready = GrokStartupScreen.Classify(GrokStartupFixture.ReadyScreen());
        tracker.Observe(ready, TimeSpan.Zero).ShouldBeFalse();
        tracker.Observe(ready, TimeSpan.FromMilliseconds(999)).ShouldBeFalse();
        tracker.Observe(ready with { Region = ready.Region + "change" },
            TimeSpan.FromMilliseconds(1000)).ShouldBeFalse();
        tracker.Observe(ready with { Region = ready.Region + "change" },
            TimeSpan.FromMilliseconds(1999)).ShouldBeFalse();
        tracker.Observe(ready with { Region = ready.Region + "change" },
            TimeSpan.FromMilliseconds(2000)).ShouldBeTrue();
        tracker.Observe(new(false, GrokStartupReason.StartingSession, "", false),
            TimeSpan.FromMilliseconds(2100)).ShouldBeFalse();
        tracker.Observe(ready, TimeSpan.FromMilliseconds(2200)).ShouldBeFalse();
        tracker.Observe(ready, TimeSpan.FromMilliseconds(3199)).ShouldBeFalse();
        tracker.Observe(ready, TimeSpan.FromMilliseconds(3200)).ShouldBeTrue();
    }

    [Test]
    public void Settle_requires_two_observations_and_elapsed_time()
    {
        var ready = GrokStartupScreen.Classify(GrokStartupFixture.ReadyScreen());
        ready.IsReady.ShouldBeTrue();
        var tracker = new GrokReadyTracker(TimeSpan.FromMilliseconds(1000));
        tracker.Observe(ready, TimeSpan.Zero).ShouldBeFalse();
        tracker.Observe(ready, TimeSpan.Zero).ShouldBeFalse();
        tracker.Observe(ready, TimeSpan.FromMilliseconds(1000)).ShouldBeTrue();
        var zero = new GrokReadyTracker(TimeSpan.Zero);
        zero.Observe(ready, TimeSpan.Zero).ShouldBeFalse();
        zero.Observe(ready, TimeSpan.Zero).ShouldBeTrue();
    }

    private static string Digest(string content) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(content)));
}
