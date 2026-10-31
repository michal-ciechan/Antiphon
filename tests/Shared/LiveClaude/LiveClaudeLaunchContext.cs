namespace Antiphon.TestSupport.LiveClaude;

// Test-side diagnostic inputs only. No issuer, credential or native admission is supplied here.
internal sealed record LiveClaudeBinding(string Uid, string Run, int Attempt, string Source, string Profile);
internal sealed record LiveClaudeImage(string Path, string Sha256);
internal sealed record LiveClaudeLock(bool Held, string Owner);
internal sealed record LiveClaudeLaunchContext(LiveClaudeBinding Binding, LiveClaudeImage Image,
    string LockOwner, Func<LiveClaudeLock> ReadLock, IReadOnlyList<LiveClaudeImage> Chain);
internal sealed record LiveClaudeLaunchRequest(LiveClaudeBinding Binding, string? Executable);
internal sealed record LiveClaudeProcess(int Pid, string Started, string Session);
internal sealed record LiveClaudeObservation(LiveClaudeProcess Process, LiveClaudeImage Image,
    IReadOnlyList<LiveClaudeImage> Chain);
internal sealed record LiveClaudeExecutableReceipt(LiveClaudeObservation ExecutedImage);

internal sealed class LiveClaudeRefusal(string reason) : InvalidOperationException("20 " + reason)
{
    public string Reason { get; } = reason;
    public int ExitCode => 20;
    public bool Credit => false;
}

internal static class LiveClaudeCore
{
    public const string Submit = "Antiphon.Agents.Pty.Tests.ClaudeSubmitContractLiveTests.Submitting_via_two_writes_completes_a_turn(claude)";
    public const string Control = "Antiphon.Agents.Pty.Tests.ClaudeRemoteControlAtStartupCanaryTests.Control_auto_connects_a_bridge_session_before_the_first_user";
    public const string Off = "Antiphon.Agents.Pty.Tests.ClaudeRemoteControlAtStartupCanaryTests.Settings_file_prevents_auto_connect_and_keeps_startup_records";
    public const string Slash = "Antiphon.Agents.Pty.Tests.ClaudeRemoteControlAtStartupCanaryTests.Remote_control_slash_command_still_arms_after_settings_off";
    public const string Menu = "Antiphon.Agents.Pty.Tests.ClaudeRemoteControlMenuCanaryTests.C514_Idle_menu_clearance_preserves_bridge_and_converts_prompt";
    public const string C680 = "Antiphon.Tests.Agents.ClaudeDialogWatchdogCanaryTests.C680_LiveNudgeRefusesPermissionAndQuestionDialogs";
    public static readonly string[] All = [Submit, Control, Off, Slash, Menu, C680];
    public static bool Contains(string uid) => All.Contains(uid, StringComparer.Ordinal);
}
