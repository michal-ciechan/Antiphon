using Antiphon.TestSupport.LiveClaude;
using Antiphon.Tests.Agents;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Unit"), Timeout(30_000)]
public class LiveLaneExecutableTests
{
    private static LiveClaudeExecutableCases Fixture()
        => new(LiveClaudeCore.C680, (_, r, c, l) => ClaudeDialogWatchdogCanaryTests.PrepareExecutable(r, c, l), "claude.exe");
    private const string Caller = "tests/Antiphon.Tests/Agents/ClaudeDialogWatchdogCanaryTests.cs";

    [Test]
    public void C680UsesAttestedExecutable()
    {
        using var f = Fixture();
        f.Selection("V-4", legacy: false);
        var executable = ClaudeDialogWatchdogCanaryTests.PrepareExecutable(f.Request, f.Context,
            () => throw new InvalidOperationException("PATH consulted"));
        var probe = ClaudeDialogWatchdogCanaryTests.CreateVersionProbe(executable);
        var spec = ClaudeDialogWatchdogCanaryTests.CreateLaunchSpec(executable, ["--version"], new(), "owned-cwd", Guid.NewGuid(), DateTime.UtcNow);
        if (probe.FileName != f.Context.Image.Path || spec.Exe != f.Context.Image.Path)
            throw new InvalidOperationException("C1545 V-4/owned actual C680 probe/start spec uses attested image");
        var source = LiveClaudeExecutableAudit.Read(Caller);
        LiveClaudeExecutableAudit.C680(source);
        LiveClaudeExecutableAudit.Reject(s => LiveClaudeExecutableAudit.Builder(s, "CreateVersionProbe", 0), source,
            "new(executable.RequirePath(), \"--version\")", "new(Resolve(\"claude.exe\"), \"--version\")", "PC-19");
        LiveClaudeExecutableAudit.Reject(s => LiveClaudeExecutableAudit.Builder(s, "CreateLaunchSpec", 2), source,
            "AgentKind.ClaudeCode, executable.RequirePath(), args", "AgentKind.ClaudeCode, Resolve(\"claude.exe\"), args", "PC-20");
    }

    [Test]
    public void C680RefusesMissingContext()
    {
        using var f = Fixture();
        f.Admission("V-5");
        var source = LiveClaudeExecutableAudit.Read(Caller);
        LiveClaudeExecutableAudit.Reject(LiveClaudeExecutableAudit.C680, source,
            "var executable = RequireExecutable();", "var executable = LiveClaudeExecutable.Resolve(\"non-core-manual\", null, null, () => Resolve(\"claude.exe\"));", "PC-21");
        LiveClaudeExecutableMutants.Run("V-5");
    }

    [Test]
    public void C680ImageMismatchRefuses()
    {
        using var f = Fixture();
        f.Images("V-6");
        var source = LiveClaudeExecutableAudit.Read(Caller);
        LiveClaudeExecutableAudit.Reject(LiveClaudeExecutableAudit.C680, source,
            "executable.RequireNativeObservation();", ";", "PC-30");
    }
}
