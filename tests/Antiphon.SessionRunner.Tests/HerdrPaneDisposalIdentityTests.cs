using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

public sealed class HerdrPaneDisposalIdentityTests
{
    private static readonly Guid Session = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");
    private static readonly Guid Other = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444445");
    private static readonly DateTime Started = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    internal static HerdrDisposalObservation Observation(Guid session, string? kind = null)
    {
        var shell = new HerdrPaneDisposalProcess(10, "pwsh.exe", Started);
        var agent = new HerdrPaneDisposalProcess(11, (kind ?? "grok") + ".exe", Started.AddSeconds(1), 10, [session]);
        return new(new("w1:p1", "w1:t1", "w1", "terminal", Agent: kind), new("0.8.2", 20, "1:100"),
            shell, kind is null ? [] : [agent], kind is null ? [shell] : [shell, agent], true, false, false,
            [new(session, "token", null, false)], []);
    }
    private static string? Verdict(HerdrDisposalObservation o, Guid? native = null) =>
        new HerdrDisposalIdentity().Refusal(new("w1:p1", Session, native), o);
    private static HerdrDisposalObservation Agent(string kind = "grok", bool native = true)
    {
        var o = Observation(Session, kind);
        var p = o.Foreground![0] with { NativeSessionIds = native ? [Session] : [] };
        return o with { Foreground = [p], Affected = [o.Shell!, p] };
    }

    [Test] [Arguments("claude")] [Arguments("grok")] [Arguments("codex")]
    public void Accepts_only_current_positive_process_evidence(string kind)
    {
        Verdict(Observation(Session)).ShouldBeNull();
        var o = Agent(kind);
        Verdict(o, Session).ShouldBeNull();
        new HerdrDisposalIdentity().Refusal(new("w1:p1", null, Session), o with { Claims = [] }).ShouldBeNull();
        var p = o.Foreground![0] with { NativeSessionIds = [] };
        o = o with { Foreground = [p], Affected = [o.Shell!, p], Claims = [new(Session, "sidecar", "attached", false, kind, p.Pid, p.StartedAtUtc)] };
        Verdict(o).ShouldBeNull();
    }
    [Test] public void C461_G003_Both_expected_identities() => Verdict(Agent(), Other).ShouldBe(HerdrPaneDisposalCodes.IdentityUnproven);
    [Test] public void C461_G016_Positive_association() => Verdict(Observation(Session) with { Claims = [] }).ShouldBe(HerdrPaneDisposalCodes.IdentityUnproven);
    [Test] [Arguments(false)] [Arguments(true)]
    public void C461_G017_Contradictory_claims(bool reverse)
    {
        var claims = new HerdrPaneDisposalClaim[] { new(Session, "runtime", null, false), new(Other, "last-pane", null, false) };
        Verdict(Observation(Session) with { Claims = reverse ? claims.Reverse().ToArray() : claims }).ShouldBe(HerdrProblemTypes.PaneBound);
    }
    [Test] [Arguments("null")] [Arguments("partial")] [Arguments("claims")]
    public void C461_G018_Complete_inventory(string missing)
    {
        var o = Observation(Session);
        o = missing switch { "null" => o with { Foreground = null }, "partial" => o with { Complete = false }, _ => o with { ClaimsComplete = false } };
        Verdict(o).ShouldBe(HerdrPaneDisposalCodes.IdentityUnproven);
    }
    [Test] [Arguments(null)] [Arguments("unknown.exe")]
    public void C461_G019_Verified_shell(string? name)
    {
        var o = Observation(Session); Verdict(o with { Shell = name is null ? null : o.Shell! with { ExecutableName = name } }).ShouldBe(HerdrPaneDisposalCodes.IdentityUnproven);
    }
    [Test] [Arguments(0)] [Arguments(-1)]
    public void C461_G020_Valid_process_ids(int pid)
    {
        var o = Agent(); var p = o.Foreground![0] with { Pid = pid };
        Verdict(o with { Foreground = [p], Affected = [o.Shell!, p] }).ShouldBe(HerdrPaneDisposalCodes.IdentityUnproven);
    }
    [Test] [Arguments(false)] [Arguments(true)]
    public void C461_G021_Readable_creation_identity(bool shell)
    {
        var o = Agent(); var p = (shell ? o.Shell! : o.Foreground![0]) with { StartedAtUtc = null };
        Verdict(shell ? o with { Shell = p, Affected = [p, o.Foreground![0]] } : o with { Foreground = [p], Affected = [o.Shell!, p] }).ShouldBe(HerdrPaneDisposalCodes.IdentityUnproven);
    }
    [Test] [Arguments(1)] [Arguments(119)]
    public void C461_G022_Exact_recorded_child_identity(int seconds)
    {
        var o = Agent(native: false); var p = o.Foreground![0];
        Verdict(o with { Claims = [new(Session, "sidecar", "attached", false, "grok", p.Pid, p.StartedAtUtc!.Value.AddSeconds(seconds))] }).ShouldBe(HerdrPaneDisposalCodes.IdentityUnproven);
    }
    [Test] [Arguments("sidecar")] [Arguments("last-pane")]
    public void C461_G023_Legacy_sidecar_is_not_exact(string source)
    {
        var o = Agent(native: false);
        Verdict(o with { Claims = [new(Session, source, "launched", false, "grok", 11)] }).ShouldBe(HerdrPaneDisposalCodes.IdentityUnproven);
    }
    [Test] public void C461_G024_Native_source_antiphon()
    {
        var o = Agent(native: false);
        Verdict(o with { Pane = o.Pane with { AgentSession = new("antiphon", "grok", "uuid", Session.ToString()) } }).ShouldBe(HerdrPaneDisposalCodes.IdentityUnproven);
    }
    [Test] public void C461_G025_Native_incarnation_binding()
    {
        var o = Agent(native: false);
        Verdict(o with { Pane = o.Pane with { AgentSession = new("native", "grok", "uuid", Session.ToString()) } }).ShouldBe(HerdrPaneDisposalCodes.IdentityUnproven);
    }
    [Test] public void C461_G026_Conflicting_native_facts()
    {
        var o = Agent(); Verdict(o with { Pane = o.Pane with { AgentSession = new("native", "grok", "uuid", Other.ToString()) } }).ShouldBe(HerdrProblemTypes.PaneForeign);
    }
    [Test] [Arguments("raw")] [Arguments("opencode")] [Arguments("unknown")]
    public void C461_G027_Supported_kind(string kind) => Verdict(Agent(kind)).ShouldBe(HerdrProblemTypes.PaneForeign);
    [Test] public void C461_G028_Executable_family()
    {
        var o = Agent(); var p = o.Foreground![0] with { ExecutableName = "foreign.exe" };
        Verdict(o with { Foreground = [p], Affected = [o.Shell!, p] }).ShouldBe(HerdrProblemTypes.PaneForeign);
    }
    [Test] public void C461_G029_Exact_native_uuid() => Verdict(Agent(), Other).ShouldBe(HerdrPaneDisposalCodes.IdentityUnproven);
    [Test] public void C461_G030_Codex_positive_identity()
    {
        Verdict(Agent("codex", false)).ShouldBe(HerdrPaneDisposalCodes.IdentityUnproven);
        Accepts_only_current_positive_process_evidence("codex");
    }
    [Test] public void C461_G031_Single_agent_root()
    {
        var o = Agent(); var second = o.Foreground![0] with { Pid = 12 };
        Verdict(o with { Foreground = [o.Foreground[0], second], Affected = [.. o.Affected, second] }).ShouldBe(HerdrProblemTypes.PaneForeign);
    }
    [Test] [Arguments(false)] [Arguments(true)]
    public void C461_G032_Complete_affected_tree(bool occupied)
    {
        var o = occupied ? Agent() : Observation(Session);
        Verdict(o with { Affected = [.. o.Affected, new(12, "foreign.exe", Started.AddSeconds(2), 10)] }).ShouldBe(HerdrProblemTypes.PaneForeign);
    }
    [Test] public void C461_G033_No_pending_input() => Verdict(Observation(Session) with { PendingInput = true }).ShouldBe(HerdrProblemTypes.PaneBound);
    [Test] public void C461_G034_No_pending_backend_launch() => Verdict(Observation(Session) with { PendingLaunch = true }).ShouldBe(HerdrProblemTypes.PaneBound);
    [Test] public void C461_G035_Token_is_not_process_authority() => C461_G028_Executable_family();
}
