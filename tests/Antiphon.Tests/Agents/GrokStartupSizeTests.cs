using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

internal static class GrokStartupSizeFixture
{
    public static string Screen(int cols, int rows, string mode = "authenticated")
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Agents", "Fixtures", "card0861", "startup-sizes.json")));
        return document.RootElement.GetProperty("captures").EnumerateArray().Single(c =>
            c.GetProperty("cols").GetInt32() == cols && c.GetProperty("rows").GetInt32() == rows
            && c.GetProperty("mode").GetString() == mode).GetProperty("screen").GetString()!;
    }
}

[Category("Unit")]
public class GrokStartupSizeTests
{
    [Test]
    [Arguments(80, 24, GrokStartupReason.Ready, "9880df906bfb5a68b503efa004787aeced48c42adadae2a822544f3634c8106a")]
    [Arguments(100, 30, GrokStartupReason.Ready, "5f3383c28a383a8a8082cbe32cc07103a5493b9f6c4304f704d15007eaff038f")]
    [Arguments(120, 30, GrokStartupReason.Ready, "d7b6fa93c8506ed054f6dc285a891749a94572ba31f81c34665e6f386a32882d")]
    [Arguments(160, 50, GrokStartupReason.Ready, "9d7e0dcdbd5de66b652424a12054324d741d172f8f1dd8b4bd5717e012eadd5a")]
    [Arguments(200, 60, GrokStartupReason.Ready, "91e86f128ea4c56f61169f4b953c9683f924658f99dbf93c9f50bffd11e5dca7")]
    [Arguments(40, 20, GrokStartupReason.Unknown, "63511d86d3a4b6c9551112eaeef518d4dd6b23658fce8c96b5923014af1c2d3d")]
    public void Captured_ready_size_is_qualified(int cols, int rows, GrokStartupReason expected, string hash)
    {
        var screen = GrokStartupSizeFixture.Screen(cols, rows);
        Digest(screen).ShouldBe(hash);
        var lines = screen.Split('\n');
        lines.Length.ShouldBe(rows);
        lines.All(line => line.Length <= cols).ShouldBeTrue();
        var observed = GrokStartupScreen.Classify(screen);
        observed.IsReady.ShouldBe(expected == GrokStartupReason.Ready, $"native-ready:{cols}x{rows}");
        observed.Reason.ShouldBe(expected);

        // One cell flip: a different input marker is not an empty Grok composer.
        var inputRow = Array.FindIndex(lines, line => line.Contains('❯'));
        inputRow.ShouldBeGreaterThanOrEqualTo(0);
        lines[inputRow] = lines[inputRow].Replace('❯', 'x');
        var changed = GrokStartupScreen.Classify(string.Join('\n', lines));
        changed.IsReady.ShouldBeFalse($"changed-marker:{cols}x{rows}");
        changed.Reason.ShouldBe(expected == GrokStartupReason.Ready
            ? GrokStartupReason.ComposerUnavailable : GrokStartupReason.Unknown);
    }

    [Test]
    [Arguments(80, 24, "735f143523adb815fc1a7ab9bd5a6940b1538754820e4f347bcd24db6f248e9a")]
    [Arguments(100, 30, "df79d5e9e13f0f3377c21205efe94ec310b36a1ad79091cab144c56047ab5b93")]
    [Arguments(120, 30, "ecfb4f42a57b95492efac2aad5a7afb928bc0e554dcb46e7a959c0b4e7c8ca1d")]
    [Arguments(160, 50, "9657961cf60807418db6d505638541c0d277cebf7124b410402bb9dce13deabc")]
    [Arguments(200, 60, "a15e03781cad5b5833512786eff0c06e5de35c1dbdd3e1f0f5810dac9517a3dc")]
    [Arguments(40, 20, "66ac61b52a1ad65fcd7c4283f272b441be14b019105cdb93dfac781f5b8e26cf")]
    public void Captured_sign_in_stays_blocked(int cols, int rows, string hash)
    {
        var screen = GrokStartupSizeFixture.Screen(cols, rows, "empty-home");
        Digest(screen).ShouldBe(hash);
        AssertBlocked(screen, GrokStartupReason.SignIn);
        // One row flip: device-code visibility is not the sign-in predicate.
        var lines = screen.Split('\n');
        var code = Array.FindIndex(lines, line => Regex.IsMatch(line.Trim(), @"\AX+(-X+)?\z"));
        code.ShouldBeGreaterThanOrEqualTo(0);
        lines[code] = "";
        AssertBlocked(string.Join('\n', lines), GrokStartupReason.SignIn);
    }

    [Test]
    [Arguments(80, 24)]
    [Arguments(100, 30)]
    [Arguments(120, 30)]
    [Arguments(160, 50)]
    [Arguments(200, 60)]
    [Arguments(40, 20)]
    public void Historical_trust_prompt_replayed_at_size_stays_blocked(int cols, int rows)
    {
        // Synthetic replay of the real 1.0.13 prompt in GrokTrustPromptDetectorTests,
        // not a native 1.0.41 trust capture. TerminalScreen performs the line wrapping.
        const string prompt = "Do you trust the contents of this directory?\r\n"
            + "Grok Build may run or modify contents in this directory,\r\n"
            + "posing security risks.\r\nYes, proceed y\r\nNo, quit n";
        var terminal = new TerminalScreen(cols, rows);
        terminal.Feed(prompt);
        AssertBlocked(terminal.GetScreenText(), GrokStartupReason.Trust);
        // One phrase flip: Codex's affirmative choice does not qualify as Grok trust.
        var changed = new TerminalScreen(cols, rows);
        changed.Feed(prompt.Replace("Yes, proceed", "Yes, continue", StringComparison.Ordinal));
        AssertBlocked(changed.GetScreenText(), GrokStartupReason.Unknown);
    }

    private static void AssertBlocked(string screen, GrokStartupReason reason)
    {
        var observed = GrokStartupScreen.Classify(screen, GrokStartupFixture.ReadyScreen());
        observed.IsReady.ShouldBeFalse();
        observed.Reason.ShouldBe(reason);
    }

    private static string Digest(string screen) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(screen)));
}
