using Antiphon.TestSupport.LiveClaude;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

[Category("Unit")]
public class LiveClaudeExecutableWiringTests
{
    private static LiveClaudeExecutableCases Fixture(string uid = LiveClaudeCore.Submit)
        => new(uid, (u, r, c, l) => ClSession.PrepareCore(u, r, c,
            () => ClSession.ResolveCl(Path.GetDirectoryName(l()))!));

    [Test]
    public void ClSessionUsesAttestedExecutable()
    {
        using var f = Fixture();
        f.Selection("V-1");
        var executable = ClSession.PrepareCore(LiveClaudeCore.Submit, f.Request, f.Context, () => throw new InvalidOperationException("PATH consulted"));
        var (app, args) = ClSession.BuildCoreLaunch(executable, "--version");
        if (app != f.Context.Image.Path || !args.SequenceEqual(new[] { "--version" }))
            throw new InvalidOperationException("C1545 V-1/owned actual ClSession launch builder uses attested image");
        AuditPtyCallers();
        LiveClaudeExecutableMutants.Run("V-1");
    }

    [Test]
    public void CoreCallersRefuseMissingContext()
    {
        foreach (var uid in LiveClaudeCore.All.Where(u => u != LiveClaudeCore.C680))
        {
            using var f = Fixture(uid);
            f.Admission("V-2");
            try { ClSession.RequireCore(uid); }
            catch (TUnit.Core.Exceptions.SkipTestException e) when (e.Message == "20 missing-context") { continue; }
            throw new InvalidOperationException("C1545 V-2/missing-context native boundary must remain a required Full skip");
        }
        LiveClaudeExecutableMutants.Run("V-2");
    }

    [Test]
    public void ExecutedImageMismatchRefuses()
    {
        using var f = Fixture();
        f.Images("V-3");
        LiveClaudeExecutableMutants.Run("V-3");
    }

    private static void AuditPtyCallers()
    {
        var prefix = "tests/Antiphon.Agents.Pty.Tests/";
        var cases = new[]
        {
            ("ClaudeSubmitContractLiveTests.cs", "Submitting_via_two_writes_completes_a_turn", "Submit", "ClaudeSubmitContractHarness.LaunchReadyAsync", "PC-2", 1),
            ("ClaudeRemoteControlAtStartupCanaryTests.cs", "Control_auto_connects_a_bridge_session_before_the_first_user", "Control", "LaunchUntilReadyAsync", "PC-4", 0),
            ("ClaudeRemoteControlAtStartupCanaryTests.cs", "Settings_file_prevents_auto_connect_and_keeps_startup_records", "Off", "LaunchUntilReadyAsync", "PC-5", 0),
            ("ClaudeRemoteControlAtStartupCanaryTests.cs", "Remote_control_slash_command_still_arms_after_settings_off", "Slash", "LaunchUntilReadyAsync", "PC-6", 0),
            ("ClaudeRemoteControlMenuCanaryTests.cs", "C514_Idle_menu_clearance_preserves_bridge_and_converts_prompt", "Menu", "ClSession.BuildCoreLaunch", "PC-7", 0)
        };
        foreach (var (file, method, id, consume, pc, argumentIndex) in cases)
        {
            var source = LiveClaudeExecutableAudit.Read(prefix + file);
            void Audit(string s) => LiveClaudeExecutableAudit.Core(s, method, id, consume, argumentIndex: argumentIndex);
            var call = $"var executable = ClSession.RequireCore(LiveClaudeCore.{id});";
            LiveClaudeExecutableAudit.Reject(Audit, source, call, "var executable = ClSession.PrepareCore(\"non-core-manual\");", pc);
            foreach (var replacement in new[] { "/* " + call + " */", "if (false) { " + call + " }", call.Replace("var executable = ", "") })
                LiveClaudeExecutableAudit.Reject(Audit, source, call, replacement, pc + "-syntax");
        }
        LiveClaudeExecutableAudit.Launch(LiveClaudeExecutableAudit.Read(prefix + "ClaudeRemoteControlAtStartupCanaryTests.cs"), "LaunchUntilReadyAsync");
        LiveClaudeExecutableAudit.Launch(LiveClaudeExecutableAudit.Read(prefix + "ClaudeRemoteControlMenuCanaryTests.cs"), "C514_Idle_menu_clearance_preserves_bridge_and_converts_prompt");
        var harness = LiveClaudeExecutableAudit.Read(prefix + "ClaudeSubmitContractTests.cs");
        LiveClaudeExecutableAudit.Reject(LiveClaudeExecutableAudit.Harness, harness,
            "var executable = coreExecutable ?? ClSession.RequireCore(LiveClaudeCore.Submit);",
            "var executable = ClSession.PrepareCore(\"non-core-manual\");", "PC-3");
    }
}
