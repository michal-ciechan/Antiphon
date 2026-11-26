using Antiphon.TestSupport.LiveClaude;
using TUnit.Core;

namespace Antiphon.Tests.Invariants;

[Category("Unit")]
[Category("RepositoryInvariant")]
public sealed class LiveLaneExecutableInvariantTests
{
    [Test]
    public void Canary_source_uses_the_attested_executable()
    {
        var source = LiveClaudeExecutableAudit.Read(LiveClaudeExecutableAudit.C680Path);
        LiveClaudeExecutableAudit.C680(source);
        LiveClaudeExecutableAudit.Reject(s => LiveClaudeExecutableAudit.Builder(s, "CreateVersionProbe", 0), source,
            "new(executable.RequirePath(), \"--version\")", "new(Resolve(\"claude.exe\"), \"--version\")", "PC-19");
        LiveClaudeExecutableAudit.Reject(s => LiveClaudeExecutableAudit.Builder(s, "CreateLaunchSpec", 2), source,
            "AgentKind.ClaudeCode, executable.RequirePath(), args", "AgentKind.ClaudeCode, Resolve(\"claude.exe\"), args", "PC-20");
    }

    [Test]
    public void Canary_source_requires_an_executable_context()
    {
        var source = LiveClaudeExecutableAudit.Read(LiveClaudeExecutableAudit.C680Path);
        LiveClaudeExecutableAudit.Reject(LiveClaudeExecutableAudit.C680, source,
            "var executable = RequireExecutable();", "var executable = LiveClaudeExecutable.Resolve(\"non-core-manual\", null, null, () => Resolve(\"claude.exe\"));", "PC-21");
    }

    [Test]
    public void Canary_source_requires_native_image_observation()
    {
        var source = LiveClaudeExecutableAudit.Read(LiveClaudeExecutableAudit.C680Path);
        LiveClaudeExecutableAudit.Reject(LiveClaudeExecutableAudit.C680, source,
            "executable.RequireNativeObservation();", ";", "PC-30");
    }

    [Test]
    public void Canary_source_preserves_the_setup_catch_filters()
    {
        LiveClaudeProfileAudit.RequireCanarySetupFilters();
    }
}
