using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-0299 S3: ready waits until the MCP boot line has been absent 500 ms, bound
/// <c>CodexBootStatusMaxWaitMs</c>. Both adapters call <see cref="CodexMcpBoot.WaitUntilAbsentAsync"/>
/// (lockstep comments on <c>CodexReadyDetector.WaitAsync</c> and
/// <c>RunnerCodexAdapter.WaitForReadyAsync</c>).
/// </summary>
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

    [Test]
    public async Task WaitUntilAbsent_returns_immediately_when_the_line_is_not_on_screen()
    {
        var snapshots = 0;
        var started = DateTime.UtcNow;
        await CodexMcpBoot.WaitUntilAbsentAsync(
            _ =>
            {
                snapshots++;
                return Task.FromResult("OpenAI Codex\n> ");
            },
            settle: TimeSpan.FromMilliseconds(500),
            maxWait: TimeSpan.FromSeconds(10));

        snapshots.ShouldBe(1);
        (DateTime.UtcNow - started).ShouldBeLessThan(TimeSpan.FromMilliseconds(200));
    }

    [Test]
    public async Task WaitUntilAbsent_zero_max_wait_disables()
    {
        var snapshots = 0;
        await CodexMcpBoot.WaitUntilAbsentAsync(
            _ =>
            {
                snapshots++;
                return Task.FromResult(BootLine);
            },
            settle: TimeSpan.FromMilliseconds(500),
            maxWait: TimeSpan.Zero);

        snapshots.ShouldBe(0);
    }

    [Test]
    public async Task WaitUntilAbsent_waits_until_the_line_has_been_gone_for_settle()
    {
        var n = 0;
        var goneAt = (DateTime?)null;
        var returnedAt = DateTime.MinValue;
        await CodexMcpBoot.WaitUntilAbsentAsync(
            _ =>
            {
                n++;
                // Three boot frames, then clear.
                if (n <= 3)
                    return Task.FromResult(BootLine);
                goneAt ??= DateTime.UtcNow;
                return Task.FromResult("OpenAI Codex\n> ");
            },
            settle: TimeSpan.FromMilliseconds(80),
            maxWait: TimeSpan.FromSeconds(2));
        returnedAt = DateTime.UtcNow;

        n.ShouldBeGreaterThan(3);
        goneAt.ShouldNotBeNull();
        (returnedAt - goneAt.Value).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(60));
    }

    [Test]
    public async Task WaitUntilAbsent_bound_expiry_proceeds_and_invokes_callback()
    {
        var warnedMs = 0;
        var started = DateTime.UtcNow;
        await CodexMcpBoot.WaitUntilAbsentAsync(
            _ => Task.FromResult(BootLine),
            settle: TimeSpan.FromMilliseconds(500),
            maxWait: TimeSpan.FromMilliseconds(120),
            onBoundExpired: ms => warnedMs = ms);

        warnedMs.ShouldBe(120);
        (DateTime.UtcNow - started).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(80));
        (DateTime.UtcNow - started).ShouldBeLessThan(TimeSpan.FromSeconds(2));
    }
}
