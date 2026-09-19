using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-0574: keep the pure MCP marker detector. The absence-only waiter is gone;
/// readiness is the positive gate in <see cref="CodexReadyWait"/>.
/// </summary>
[Category("Unit")]
public class CodexMcpBootTests
{
    private const string BootLine = "Starting MCP servers (1/2): node_repl (1s  esc to interrupt)";

    [Test]
    public void IsVisible_matches_starting_and_booting_forms()
    {
        CodexMcpBoot.IsVisible(BootLine).ShouldBeTrue();
        CodexMcpBoot.IsVisible("Booting MCP server: codex_apps (0s • esc to interrupt)").ShouldBeTrue();
        CodexMcpBoot.IsVisible("OpenAI Codex (v0.151.0)\n> ").ShouldBeFalse();
        CodexMcpBoot.IsVisible("").ShouldBeFalse();
        CodexMcpBoot.IsVisible(null).ShouldBeFalse();
    }
}
