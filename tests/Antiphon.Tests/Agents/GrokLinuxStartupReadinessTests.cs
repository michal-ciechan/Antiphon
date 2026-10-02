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
}
