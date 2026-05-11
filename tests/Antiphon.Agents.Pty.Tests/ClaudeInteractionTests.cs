using Antiphon.Agents.Pty;
using FluentAssertions;
using Xunit;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// Tests for navigating the Claude TUI's interactive choice menus (permission prompts,
/// arrow-key selection lists) using the <see cref="TerminalScreen"/> to read the
/// rendered state rather than the raw PTY byte stream.
///
/// These tests run WITHOUT --dangerously-skip-permissions so that permission prompts
/// actually appear.  The screen buffer lets us reliably detect the menu text despite
/// cursor-forward repaint optimisations.
/// </summary>
[Collection("Headed")]
[Trait("Category", "Headed")]
public class ClaudeInteractionTests
{
	// ── I01: screen buffer detects permission prompt ──────────────────────────

	/// <summary>
	/// Sends a Bash tool-use request without --dangerously-skip-permissions.
	/// The TUI must show a permission prompt.  SnapshotScreen() (which uses the
	/// TerminalScreen virtual screen buffer) must correctly reconstruct the menu
	/// text despite cursor-forward repaint optimisations.
	///
	/// Uses `systeminfo` rather than `echo`: simple echo commands are
	/// auto-approved by Claude Code without a prompt; `systeminfo` is a
	/// system-information command that reliably triggers the permission menu.
	/// </summary>
	[SkippableFact]
	public async Task Screen_detects_permission_prompt_for_bash_tool()
	{
		ClSession.SkipIfNotEligible();
		await using var runner = new PtyAgentRunner();
		// No --dangerously-skip-permissions — permission prompt should appear.
		var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow());
		await runner.StartAsync(app, args, cols: 200, rows: 60);

		await new ClaudeReadyDetector().WaitAsync(runner);

		runner.ClearLiveBuffer();
		await runner.SendLineAsync("Run `systeminfo` via Bash.");

		// Wait for the permission prompt to appear on the rendered screen.
		// The screen buffer correctly handles cursor-forward repaint,
		// unlike the raw buffer which loses characters via \x1b[1C gaps.
		var promptVisible = await runner.WaitForScreenAsync(
			screen => screen.Contains("Do you want to proceed?",
			                          StringComparison.OrdinalIgnoreCase),
			TimeSpan.FromSeconds(30));

		promptVisible.Should().BeTrue(
			$"permission prompt should appear on screen within 30s.\n" +
			$"Screen:\n{runner.SnapshotScreen()}");

		// Confirm the numbered options are also present.
		var screen = runner.SnapshotScreen();
		screen.Should().Contain("1.", "option 1 (Yes) must be present");
		screen.Should().Contain("2.", "option 2 (Yes, don't ask again) must be present");
		screen.Should().Contain("3.", "option 3 (No) must be present");

		await runner.KillAsync(TimeSpan.FromSeconds(2));
	}

	// ── I02: approve permission prompt via numbered choice ─────────────────────

	/// <summary>
	/// After detecting the permission prompt via SnapshotScreen(), approve it by
	/// sending "1" (Yes) and verify the tool executes and the response contains
	/// the expected output.
	/// </summary>
	[SkippableFact]
	public async Task Approve_permission_prompt_via_numbered_choice_executes_tool()
	{
		ClSession.SkipIfNotEligible();
		await using var runner = new PtyAgentRunner();
		var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow());
		await runner.StartAsync(app, args, cols: 200, rows: 60);

		await new ClaudeReadyDetector().WaitAsync(runner);

		runner.ClearLiveBuffer();
		await runner.SendLineAsync("Run `systeminfo` via Bash and tell me the OS Name in one short sentence.");

		// Wait for the permission prompt on the rendered screen.
		var promptVisible = await runner.WaitForScreenAsync(
			screen => screen.Contains("Do you want to proceed?",
			                          StringComparison.OrdinalIgnoreCase),
			TimeSpan.FromSeconds(30));
		promptVisible.Should().BeTrue("permission prompt should appear");

		// Approve: send "1" then Enter.  Small delay lets the prompt fully settle.
		await Task.Delay(300);
		runner.ClearLiveBuffer();
		await runner.SendLineAsync("1");

		// Wait for Claude to finish — systeminfo can take 15-20 s on a cold system.
		var done = await new ClaudeDoneDetector { MaxWait = TimeSpan.FromMinutes(3) }.WaitAsync(runner);
		done.Should().BeTrue("Claude should respond after permission granted");

		// systeminfo output always contains "Windows" in the OS Name field.
		var responseScreen = runner.SnapshotScreen();
		responseScreen.Should().Contain("Windows",
			"systeminfo must have executed and its OS Name reflected in Claude's response");

		await runner.SendLineAsync("/exit");
		await Task.WhenAny(runner.Exited, Task.Delay(TimeSpan.FromSeconds(5)));
		await runner.KillAsync(TimeSpan.FromSeconds(2));
	}

	// ── I03: deny permission prompt via numbered choice ────────────────────────

	/// <summary>
	/// Sends option "3" (No) to deny the permission prompt and verifies that
	/// the tool does NOT execute (no systeminfo-specific output in the response).
	/// </summary>
	[SkippableFact]
	public async Task Deny_permission_prompt_via_numbered_choice_skips_tool()
	{
		ClSession.SkipIfNotEligible();
		await using var runner = new PtyAgentRunner();
		var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow());
		await runner.StartAsync(app, args, cols: 200, rows: 60);

		await new ClaudeReadyDetector().WaitAsync(runner);

		runner.ClearLiveBuffer();
		await runner.SendLineAsync("Run `systeminfo` via Bash and tell me the OS Name.");

		var promptVisible = await runner.WaitForScreenAsync(
			screen => screen.Contains("Do you want to proceed?",
			                          StringComparison.OrdinalIgnoreCase),
			TimeSpan.FromSeconds(30));
		promptVisible.Should().BeTrue("permission prompt should appear");

		// Deny: send "3" (No).
		await Task.Delay(300);
		runner.ClearLiveBuffer();
		await runner.SendLineAsync("3");

		// Claude should still respond (explaining it couldn't run the tool).
		var done = await new ClaudeDoneDetector { MaxWait = TimeSpan.FromMinutes(2) }.WaitAsync(runner);
		done.Should().BeTrue("Claude should respond even when permission is denied");

		// systeminfo output contains "OS Name:" — must NOT appear if tool was denied.
		var responseScreen = runner.SnapshotScreen();
		responseScreen.Should().NotContain("OS Name:",
			"systeminfo was denied so its raw output must not appear on screen");

		await runner.SendLineAsync("/exit");
		await Task.WhenAny(runner.Exited, Task.Delay(TimeSpan.FromSeconds(5)));
		await runner.KillAsync(TimeSpan.FromSeconds(2));
	}
}
