using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

internal static class GrokLinuxStartupFixture
{
    public static JsonDocument Read() => JsonDocument.Parse(File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory, "Agents", "Fixtures", "card1004", "linux-startup-frames.json")));

    public static string Screen(string version)
    {
        using var document = Read();
        return document.RootElement.GetProperty("captures").EnumerateArray()
            .Single(x => x.GetProperty("cliVersion").GetString() == version)
            .GetProperty("screen").GetString()!;
    }
}

[Category("Unit")]
public class GrokLinuxStartupReadinessTests
{
    [Test]
    [Arguments("1.0.40")]
    [Arguments("1.0.41")]
    public void Captured_linux_dashboard_is_ready(string version)
    {
        using var document = GrokLinuxStartupFixture.Read();
        var capture = document.RootElement.GetProperty("captures").EnumerateArray()
            .Single(x => x.GetProperty("cliVersion").GetString() == version);
        var screen = capture.GetProperty("screen").GetString()!;
        var rows = screen.Split('\n');
        capture.GetProperty("cols").GetInt32().ShouldBe(120);
        capture.GetProperty("rows").GetInt32().ShouldBe(30);
        rows.Length.ShouldBe(30);
        rows.Max(x => x.Length).ShouldBe(118);
        rows[25][4].ShouldBe('\u276f');
        rows[25][2].ShouldBe('│');
        rows[25][117].ShouldBe('│');
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(screen)))
            .ShouldBe(capture.GetProperty("sha256").GetString());

        var observation = GrokStartupScreen.Classify(screen);
        observation.Reason.ShouldBe(GrokStartupReason.Ready);
        observation.IsReady.ShouldBeTrue();
    }

    [Test]
    [Arguments("1.0.40")]
    [Arguments("1.0.41")]
    public void Linux_dashboard_settles_after_two_observations_and_elapsed_time(string version)
    {
        var observation = GrokStartupScreen.Classify(GrokLinuxStartupFixture.Screen(version));
        var tracker = new GrokReadyTracker(TimeSpan.FromSeconds(1));
        tracker.Observe(observation, TimeSpan.Zero).ShouldBeFalse();
        tracker.PositiveObservations.ShouldBe(1);
        tracker.Observe(observation, TimeSpan.FromMilliseconds(999)).ShouldBeFalse();
        tracker.Observe(observation, TimeSpan.FromSeconds(1)).ShouldBeTrue();
        tracker.LastReason.ShouldBe(GrokStartupReason.Ready);
    }

    [Test]
    [Arguments("\u276f ghost suggestion")]
    [Arguments("\u276ftyped text")]
    [Arguments("> ghost suggestion")]
    public void Composer_text_after_either_marker_is_unavailable(string inner)
    {
        var rows = LinuxRows();
        rows[25] = "  │ " + inner.PadRight(113) + "│";
        AssertNotReady(rows, GrokStartupReason.ComposerUnavailable);
    }

    [Test]
    [Arguments('\u2192')]
    [Arguments('\u203a')]
    [Arguments('x')]
    public void Uncaptured_single_character_markers_are_unavailable(char marker)
    {
        var rows = LinuxRows();
        rows[25] = rows[25].Replace('\u276f', marker);
        AssertNotReady(rows, GrokStartupReason.ComposerUnavailable);
    }

    [Test]
    [Arguments(22, "  ⠋ Starting session…", GrokStartupReason.StartingSession)]
    [Arguments(22, "  Working…", GrokStartupReason.Working)]
    [Arguments(23, "  unexpected status", GrokStartupReason.Unknown)]
    [Arguments(28, "  unexpected status", GrokStartupReason.Unknown)]
    public void Populated_rows_around_linux_composer_block_readiness(int row, string text,
        GrokStartupReason reason)
    {
        var rows = LinuxRows();
        rows[row] = text;
        AssertNotReady(rows, reason);
    }

    [Test]
    [Arguments("  Shift+Tab:mode", GrokStartupReason.Unknown)]
    [Arguments("  Ctrl+c:cancel", GrokStartupReason.Working)]
    [Arguments("  Ctrl+;:queue", GrokStartupReason.Working)]
    public void Linux_composer_requires_the_exact_enabled_hint(string hint, GrokStartupReason reason)
    {
        var rows = LinuxRows();
        rows[28] = hint;
        AssertNotReady(rows, reason);
    }

    [Test]
    [Arguments(29, 118)]
    [Arguments(30, 121)]
    public void Unqualified_geometry_is_unknown(int rowCount, int headerWidth)
    {
        var rows = LinuxRows().Take(rowCount).ToArray();
        rows[1] = rows[1].PadRight(headerWidth);
        AssertNotReady(rows, GrokStartupReason.Unknown);
    }

    [Test]
    [Arguments("Approve in your browser to finish signing in.", GrokStartupReason.SignIn)]
    [Arguments("Do you trust the contents of this directory? Yes, proceed y No, quit n", GrokStartupReason.Trust)]
    public void Sign_in_and_trust_override_a_linux_composer(string blocker, GrokStartupReason reason)
    {
        var rows = LinuxRows();
        rows[5] = blocker;
        AssertNotReady(rows, reason);
    }

    private static string[] LinuxRows() => GrokLinuxStartupFixture.Screen("1.0.41").Split('\n');

    private static void AssertNotReady(string[] rows, GrokStartupReason reason)
    {
        var observation = GrokStartupScreen.Classify(string.Join('\n', rows));
        observation.Reason.ShouldBe(reason);
        observation.IsReady.ShouldBeFalse();
    }
}
