using Antiphon.TestSupport.LiveClaude;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Antiphon.Agents.Pty.Tests;

internal static class LiveClaudeStartupAudit
{
    internal const string StartupPath="tests/Antiphon.Agents.Pty.Tests/ClaudeRemoteControlAtStartupCanaryTests.cs";
    internal const string MenuPath="tests/Antiphon.Agents.Pty.Tests/ClaudeRemoteControlMenuCanaryTests.cs";
    internal static MethodDeclarationSyntax Method(string source,string name)
    {
        var tree=CSharpSyntaxTree.ParseText(source);
        var methods=tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Where(m=>m.Identifier.ValueText==name).ToArray();
        if(tree.GetDiagnostics().Any(d=>d.Severity==DiagnosticSeverity.Error) || methods.Length!=1)
            throw new InvalidOperationException("C1545 startup-audit/"+name);
        return methods[0];
    }
    private static void Require(BlockSyntax body,string statement,string name)
    {
        var target=SyntaxFactory.ParseStatement(statement);
        var found=body.Statements.Where(s=>SyntaxFactory.AreEquivalent(s,target)).ToArray();
        if(found.Length!=1) throw new InvalidOperationException("C1545 startup-audit/"+name);
        foreach(var before in body.Statements.TakeWhile(s=>s!=found[0]))
            if(before is not (LocalDeclarationStatementSyntax or ExpressionStatementSyntax) && before is not IfStatementSyntax { Statement: ThrowStatementSyntax, Else: null })
                throw new InvalidOperationException("C1545 startup-audit/"+name+"-reachability");
    }
    internal static void Caller(string source,string method,string role)
    {
        var body=Method(source,method).Body!;
        Require(body,"var startup = SharedStartup();",role+"-shared-fixture");
        var ordered=body.Statements.Select((s,i)=>(s,i)).Single(p=>p.s.ToString().Contains("var control = await startup.",StringComparison.Ordinal)).i;
        if(body.Statements.Take(ordered).Any(s=>s.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(c=>c.Expression.ToString() is "LaunchUntilReadyAsync" or "runner.StartAsync" or "CanarySandbox.Create")))
            throw new InvalidOperationException("C1545 startup-audit/"+role+"-early-launch");
        var factory="() => RunStartupControlAsync(executable, profile, experiment)";
        Require(body,role=="control" ? $"var control = await startup.ControlAsync(experiment, {factory});"
            : $"var control = await startup.BeforeDependentAsync(experiment, {factory}, () => {{ }});",role+"-ordered-control");
        if(role=="control") Require(body,"LiveClaudeStartupEvidence.RequireControl(experiment, control);",role+"-verdict");
        else
        {
            Require(body,$"using var sandbox = CanarySandbox.Create(profile, \"{role}\");",role+"-launch");
            var tries=body.Statements.OfType<TryStatementSyntax>().ToArray();
            if(tries.Length!=1 || tries[0].Catches.Count!=0 || tries[0].Finally is null)
                throw new InvalidOperationException("C1545 startup-audit/"+role+"-catch");
            Require(tries[0].Block,role=="off" ? "LiveClaudeStartupEvidence.RequireOff(experiment, control, observation);"
                : "LiveClaudeStartupEvidence.RequireSlash(experiment, control, before, after);",role+"-verdict");
        }
    }
    internal static void Menu(string source)
    {
        var body=Method(source,"C514_Idle_menu_clearance_preserves_bridge_and_converts_prompt").Body!;
        Require(body,"await ClaudeRemoteControlAtStartupCanaryTests.EnsureMenuBridgeAsync(runner, profile, sessionId);","menu-setup");
        var calls=body.Statements.OfType<ExpressionStatementSyntax>().ToArray();
        var setup=Array.FindIndex(calls,c=>c.ToString().Contains("EnsureMenuBridgeAsync",StringComparison.Ordinal));
        var submit=Array.FindIndex(calls,c=>c.ToString().Contains("EchoGatedSubmit.SendAsync",StringComparison.Ordinal));
        if(setup<0 || setup>=submit) throw new InvalidOperationException("C1545 startup-audit/menu-order");
    }
    public static void Run()
    {
        var source=LiveClaudeExecutableAudit.Read(StartupPath);
        var shared=Method(source,"SharedStartup");
        if(!SyntaxFactory.AreEquivalent(shared.ExpressionBody?.Expression,SyntaxFactory.ParseExpression("(LiveClaudeStartupFixture)TestSessionContext.Current.AllTests.OrderBy(test => test.Id, StringComparer.Ordinal).First().StateBag.Items.GetOrAdd(\"C1545.StartupControl\", _ => new LiveClaudeStartupFixture())")))
            throw new InvalidOperationException("C1545 startup-audit/session-owned-fixture");
        foreach(var (method,role) in new[]{
            ("Control_auto_connects_a_bridge_session_before_the_first_user","control"),
            ("Settings_file_prevents_auto_connect_and_keeps_startup_records","off"),
            ("Remote_control_slash_command_still_arms_after_settings_off","slash")}) Caller(source,method,role);
        var body=Method(source,"RunStartupControlAsync").Body!.Statements.OfType<TryStatementSyntax>().Single();
        if(body.Catches.Count!=0) throw new InvalidOperationException("C1545 startup-audit/control-swallowed");
        Require(body.Block,"LiveClaudeStartupEvidence.RequireControl(experiment, control);","shared-control");
        Require(body.Block,"LiveClaudeStartupEvidence.RequireSettings(control, owner, control.Settings);","shared-settings");
        Menu(LiveClaudeExecutableAudit.Read(MenuPath));
        // Re-exercise inherited launch/path contracts while the same committed source is loaded.
        LiveClaudeProfileAudit.PtyCallers();
        var ensure=Method(source,"EnsureMenuBridgeAsync").Body!;
        Require(ensure,"LiveClaudeStartupEvidence.RequireBridge(Owner(runner, profile, session), bridge);","menu-owned-bridge");
        var config=Method(source,"RequireHookConfiguration");
        if(!config.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(c=>c.Expression.ToString()=="LiveClaudeProfile.HashFile"))
            throw new InvalidOperationException("C1545 startup-audit/hook-input");
    }
    internal static void Reject(Action<string> check,string source,string from,string to,string pc)
    {
        var index=source.IndexOf(from,StringComparison.Ordinal);
        if(index<0 || source.IndexOf(from,index+from.Length,StringComparison.Ordinal)>=0)
            throw new InvalidOperationException("C1545 "+pc+" mutation site ambiguous");
        check(source);
        try {check(source.Replace(from,to,StringComparison.Ordinal));}
        catch(InvalidOperationException e) when(e.Message.StartsWith("C1545 startup-audit/",StringComparison.Ordinal))
        {
            check(source);
            Console.WriteLine($"C1545 {pc} assertion-red; restored-green source={LiveClaudeProfile.Hash(source)} patch={LiveClaudeProfile.Hash(from+"\n"+to)} (owned syntax copy)");
            return;
        }
        throw new InvalidOperationException("C1545 "+pc+" mutant survived");
    }
}
