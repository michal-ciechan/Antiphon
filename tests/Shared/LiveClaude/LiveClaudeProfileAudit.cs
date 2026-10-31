using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Antiphon.TestSupport.LiveClaude;

internal static class LiveClaudeProfileAudit
{
    private const string Pty = "tests/Antiphon.Agents.Pty.Tests/";
    private const string Startup = Pty + "ClaudeRemoteControlAtStartupCanaryTests.cs";
    private static MethodDeclarationSyntax Method(string source,string name)
    {
        var tree=CSharpSyntaxTree.ParseText(source);
        var methods=tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Where(m=>m.Identifier.ValueText==name).ToArray();
        if(tree.GetDiagnostics().Any(d=>d.Severity==DiagnosticSeverity.Error) || methods.Length!=1) Fail(name);
        return methods[0];
    }
    private static void Fail(string name) => throw new InvalidOperationException("C1545 profile-audit/"+name);
    internal static bool IsSetupSkip(TryStatementSyntax statement)
    {
        if (statement.Finally is not null || statement.Catches.Count != 1) return false;
        var clause = statement.Catches[0];
        if (clause.Declaration?.Type.ToString() != "LiveClaudeRefusal") return false;
        if (!SyntaxFactory.AreEquivalent(clause.Filter?.FilterExpression, SyntaxFactory.ParseExpression("LiveClaudeOutcome.SetupStatus(e.Reason) is string setup"))) return false;
        return clause.Block.Statements.Count == 1
            && clause.Block.Statements[0].ToString().Contains("throw new SkipTestException(\"20 \" + setup)", StringComparison.Ordinal);
    }
    private static void Expression(string source,string method,string expression)
    {
        var m=Method(source,method);
        if(!SyntaxFactory.AreEquivalent(m.ExpressionBody?.Expression,SyntaxFactory.ParseExpression(expression))) Fail(method);
    }
    private static void Statement(string source,string method,string expected)
    {
        var m=Method(source,method);
        var body=m.Body ?? throw new InvalidOperationException("C1545 profile-audit/"+method);
        var statement=SyntaxFactory.ParseStatement(expected);
        var skips=body.Statements.OfType<TryStatementSyntax>().Where(IsSetupSkip).ToArray();
        if(skips.Length>1) Fail(method);
        var scopes=skips.Length==1 ? new[]{body,skips[0].Block} : new[]{body};
        var matches=scopes.SelectMany(scope=>scope.Statements.Where(s=>SyntaxFactory.AreEquivalent(s,statement))).ToArray();
        if(matches.Length!=1) Fail(method);
        var actual=matches[0];
        var parent=(BlockSyntax)actual.Parent!;
        Reachable(parent,actual,method);
        if(skips.Length==1 && parent==skips[0].Block) Reachable(body,skips[0],method);
    }
    private static void Reachable(BlockSyntax body,StatementSyntax target,string method)
    {
        foreach(var statement in body.Statements.TakeWhile(s=>s!=target))
            if(statement is not (LocalDeclarationStatementSyntax or ExpressionStatementSyntax or EmptyStatementSyntax)) Fail(method+"-unknown-preamble");
    }
    private static void Start(string source,string method)
    {
        var m=Method(source,method);
        var body=m.Body!;
        var start=body.Statements.SelectMany(s=>s.DescendantNodes().OfType<InvocationExpressionSyntax>())
            .SingleOrDefault(c=>c.Expression.ToString()=="runner.StartAsync");
        if(start is null || start.ArgumentList.Arguments.SingleOrDefault(a=>a.NameColon?.Name.Identifier.ValueText=="cwd")?.Expression.ToString()!="launch.Cwd"
            || start.ArgumentList.Arguments.SingleOrDefault(a=>a.NameColon?.Name.Identifier.ValueText=="env")?.Expression.ToString()!="launch.Env"
            || start.ArgumentList.Arguments.SingleOrDefault(a=>a.NameColon?.Name.Identifier.ValueText=="cols")?.Expression.ToString()!="launch.Cols"
            || start.ArgumentList.Arguments.SingleOrDefault(a=>a.NameColon?.Name.Identifier.ValueText=="rows")?.Expression.ToString()!="launch.Rows") Fail(method);
        var statement=start!.Ancestors().OfType<StatementSyntax>().First();
        if(statement.Parent!=body) Fail(method+"-conditional-start");
        Reachable(body,statement,method);
    }
    internal static void RequireCanarySetupFilters()
    {
        var startup = LiveClaudeExecutableAudit.Read(Startup);
        foreach (var name in new[]
        {
            "Control_auto_connects_a_bridge_session_before_the_first_user",
            "Settings_file_prevents_auto_connect_and_keeps_startup_records",
            "Remote_control_slash_command_still_arms_after_settings_off"
        }) RequireSetupFilter(startup, name);
        RequireSetupFilter(LiveClaudeExecutableAudit.Read(Pty + "ClaudeRemoteControlMenuCanaryTests.cs"),
            "C514_Idle_menu_clearance_preserves_bridge_and_converts_prompt");
    }
    private static void RequireSetupFilter(string source, string name)
    {
        var filters = Method(source, name).DescendantNodes().OfType<CatchFilterClauseSyntax>().ToArray();
        if (filters.Length != 1 || !SyntaxFactory.AreEquivalent(filters[0].FilterExpression,
                SyntaxFactory.ParseExpression("LiveClaudeOutcome.SetupStatus(e.Reason) is string setup"))) Fail(name + "-filter");
    }
    public static void Paths(string source)
    {
        Expression(source,"FindSessionJsonl","paths.FindSessionJsonl(sessionId)");
        Expression(source,"ReadSessionFile","paths.ReadSessionFile(sessionId, cwd)");
        var trust=Method(source,"Trust");
        var expected=SyntaxFactory.ParseStatement("if (!ClaudeProjectTrust.IsTrusted(profile.Paths.Cwd, profile.Paths.GlobalState)) throw new LiveClaudeRefusal(\"trust-unavailable\");");
        if(trust.Body?.Statements.Count!=1 || !SyntaxFactory.AreEquivalent(trust.Body.Statements[0],expected)) Fail("Trust");
        var snapshot=Method(source,"ReadOwnedBridge");
        if(!snapshot.Body!.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(c=>SyntaxFactory.AreEquivalent(c,SyntaxFactory.ParseExpression("ReadSessionFile(profile.Paths, session, profile.Paths.Cwd)")))
            || !snapshot.Body.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(c=>SyntaxFactory.AreEquivalent(c,SyntaxFactory.ParseExpression("FindSessionJsonl(profile.Paths, session)")))) Fail("SnapshotEvidence");
    }
    public static void StartupCaller(string source,string method,string role)
    {
        Statement(source,method,"using var profile = ClSession.RequireProfile(executable);");
        if (role == "control") method = "RunStartupControlAsync";
        Statement(source,method,$"using var sandbox = CanarySandbox.Create(profile, \"{role}\");");
        Statement(source,method,$"var launch = BuildProfileLaunch(profile, \"{role}\", sandbox.SessionId, sandbox.SettingsPath);");
        Statement(source,method,"await LaunchUntilReadyAsync(executable, runner, sandbox, launch);");
        Expression(source,"BuildProfileLaunch","ClSession.BuildProfileLaunch(profile, role, session, overlay)");
        Start(source,"LaunchUntilReadyAsync");
    }
    public static void PtyCallers()
    {
        var source=LiveClaudeExecutableAudit.Read(Startup);
        Paths(source);
        foreach(var (method,role,pc) in new[] {
            ("Control_auto_connects_a_bridge_session_before_the_first_user","control","PC-41"),
            ("Settings_file_prevents_auto_connect_and_keeps_startup_records","off","PC-42"),
            ("Remote_control_slash_command_still_arms_after_settings_off","slash","PC-43")})
        {
            void Check(string s)=>StartupCaller(s,method,role);
            Check(source);
            var call=$"var launch = BuildProfileLaunch(profile, \"{role}\", sandbox.SessionId, sandbox.SettingsPath);";
            Reject(Check,source,call,call.TrimEnd(';')+" with { Cwd = \"\" };",pc);
            if(role!="control") Reject(Check,source,call,$"var launch = new LiveClaudeProfileSpec(new[] {{ \"--settings\", sandbox.SettingsPath! }}, profile.Environment(), profile.Paths.Cwd);",role=="off"?"PC-50":"PC-51");
        }
        Reject(Paths,source,"profile.Paths.GlobalState","\"ambient-config\"","PC-31");
        Reject(Paths,source,"paths.FindSessionJsonl(sessionId)","paths.FindSessionJsonl(Guid.Empty.ToString())","PC-32");
        Reject(Paths,source,"paths.ReadSessionFile(sessionId, cwd)","paths.ReadSessionFile(Guid.Empty.ToString(), cwd)","PC-33");
        var harness=LiveClaudeExecutableAudit.Read(Pty+"ClaudeSubmitContractTests.cs");
        void Harness(string s)
        {
            var m=Method(s,"LaunchReadyAsync");
            var branch=m.Body!.Statements.OfType<IfStatementSyntax>().Single();
            if(branch.Condition.ToString()!="backend == \"fakeclaude\"" || branch.Else?.Statement is not BlockSyntax live) {Fail("harness");return;}
            var body="class Harness { "+m.WithBody(live).ToFullString()+" }";
            Statement(body,"LaunchReadyAsync","var launch = ClSession.BuildProfileLaunch(profile, \"submit\", coreSession ?? throw new LiveClaudeRefusal(\"missing-session\"));");
            Start(body,"LaunchReadyAsync");
            if(branch.Statement.DescendantNodes().OfType<IdentifierNameSyntax>().Any(n=>n.Identifier.ValueText is "profile" or "coreProfile")) Fail("fake-profile");
        }
        Harness(harness);
        Reject(Harness,harness,"cwd: launch.Cwd","cwd: null","PC-40");
        var submit=LiveClaudeExecutableAudit.Read(Pty+"ClaudeSubmitContractLiveTests.cs");
        Statement(submit,"Submitting_via_two_writes_completes_a_turn","using var profile = ClSession.RequireProfile(executable);");
        Statement(submit,"Submitting_via_two_writes_completes_a_turn","await using var runner = await ClaudeSubmitContractHarness.LaunchReadyAsync(backend, executable, profile, sessionId);");
        var menu=LiveClaudeExecutableAudit.Read(Pty+"ClaudeRemoteControlMenuCanaryTests.cs");
        void Menu(string s)
        {
            Statement(s,"C514_Idle_menu_clearance_preserves_bridge_and_converts_prompt","using var profile = ClSession.RequireProfile(executable);");
            Statement(s,"C514_Idle_menu_clearance_preserves_bridge_and_converts_prompt","var launch = ClSession.BuildProfileLaunch(profile, \"menu\", sessionId);");
            Start(s,"C514_Idle_menu_clearance_preserves_bridge_and_converts_prompt");
        }
        Menu(menu);
        // Replace only the core method; the non-core manual canary intentionally remains legacy.
        var m=Method(menu,"C514_Idle_menu_clearance_preserves_bridge_and_converts_prompt");
        Reject(s=>Menu(menu.Replace(m.ToFullString(),s,StringComparison.Ordinal)),m.ToFullString(),"cwd: launch.Cwd","cwd: null","PC-44");
    }
    public static void C680(string source)
    {
        const string name="C680_LiveNudgeRefusesPermissionAndQuestionDialogs";
        Statement(source,name,"var profileContext = executable.RequireProfileContext();");
        var m=Method(source,name);
        var loop=m.Body!.Statements.OfType<ForEachStatementSyntax>().Single();
        if(loop.Expression.ToString()!="new[] { false, true }") Fail("C680-probes");
        var body=loop.Statement.DescendantNodes().OfType<TryStatementSyntax>().First().Block;
        if(!body.Statements.Any(s=>SyntaxFactory.AreEquivalent(s,SyntaxFactory.ParseStatement("using var profile = new LiveClaudeProfile(profileContext with { Cwd = cwd });")))) Fail("C680-restoration");
        var start=body.DescendantNodes().OfType<InvocationExpressionSyntax>().Single(c=>c.Expression.ToString()=="runner.StartAsync");
        if(!SyntaxFactory.AreEquivalent(start.ArgumentList.Arguments[1].Expression,SyntaxFactory.ParseExpression("CreateProfileLaunch(executable, profile, settings, f.SessionId, generation)"))) Fail("C680-profile");
        Statement(source,"CreateProfileLaunch","var launch = BuildProfileLaunch(profile, \"control\", sessionId.ToString(\"D\"));");
        Statement(source,"CreateProfileLaunch","profile.Capture(args);");
        var ret=Method(source,"CreateProfileLaunch").Body!.Statements.OfType<ReturnStatementSyntax>().Single();
        if(!SyntaxFactory.AreEquivalent(ret.Expression,SyntaxFactory.ParseExpression("CreateLaunchSpec(executable, args, launch.Env, launch.Cwd, sessionId, generation)"))) Fail("C680-owned-spec");
        Statement(source,"BuildProfileLaunch","var spec = profile.Build(role, session, overlay);");
        Expression(source,"ProjectProfile","profile.Project(args)");
    }
    public static void C680Callers(LiveClaudeProfileCases.Builder? build = null, LiveClaudeProfileCases.Projector? project = null)
    {
        var source=LiveClaudeExecutableAudit.Read("tests/Antiphon.Tests/Agents/ClaudeDialogWatchdogCanaryTests.cs");
        C680(source);
        Reject(C680,source,"launch.Env, launch.Cwd","new Dictionary<string, string>(), launch.Cwd","PC-38");
        var selected=Environment.GetEnvironmentVariable("C1545_PROFILE_CONTROLS");
        if(selected=="V-13") foreach(var branch in new[]{"permission","question"})
        {
            using var f=new LiveClaudeProfileFixture();
            var cwd=Directory.CreateDirectory(Path.Combine(f.Context.Cwd,branch)).FullName;
            using(var profile=new LiveClaudeProfile(f.Context with {Cwd=cwd}))
                build!(profile,"control",f.Session,null);
            LiveClaudeProfileCases.Assert(!Antiphon.Agents.Pty.ClaudeProjectTrust.IsTrusted(cwd,Path.Combine(f.Context.ConfigDir!,".claude.json")),"V-13","restore-"+branch,"each probe cwd restores its owned trust");
            Reject(C680,source,"using var profile = new LiveClaudeProfile","var profile = new LiveClaudeProfile","PC-39",branch);
        }
        Reject(C680,source,"profileContext with { Cwd = cwd }","profileContext with { Cwd = Environment.CurrentDirectory }","PC-45");
        if(selected=="V-15")
        {
            foreach(var mode in new[]{"named-B","production-default"})
            {
                using var f=new LiveClaudeProfileFixture(mode:mode);
                using var profile=new LiveClaudeProfile(f.Context);
                var spec=build!(profile,"control",f.Session,null);
                var i=Array.IndexOf(spec.Args,"--model");
                LiveClaudeProfileCases.Assert(mode=="production-default" ? i<0 : i>=0 && spec.Args[i+1]==mode,"V-15",mode,"actual C680 builder consumes this model context");
                Reject(C680,source,"var spec = profile.Build(role, session, overlay);","var spec = profile.Build(role, session, overlay) with { Args = new[] { \"--model\", \"haiku\" } };","PC-56",mode);
            }
            foreach(var kind in new[]{"managed","global"})
            {
                using var f=new LiveClaudeProfileFixture();
                using var profile=new LiveClaudeProfile(f.Context);
                var spec=build!(profile,"control",f.Session,null);
                var before=project!(profile,spec.Args);
                var file=before.Sources.Single(s=>s.Kind==kind).Path;
                var original=File.ReadAllText(file);
                try
                {
                    var json=System.Text.Json.Nodes.JsonNode.Parse(original)!; json["model"]="changed-c680";
                    File.WriteAllText(file,json.ToJsonString());
                    LiveClaudeProfileCases.Assert(project(profile,spec.Args).Digest!=before.Digest,"V-15","drift-"+kind,"C680 caller measures this loaded source");
                }
                finally {File.WriteAllText(file,original);}
                Reject(C680,source,"profile.Capture(args);","profile.Capture(new[] { \"--setting-sources\", \"\" });","PC-73",kind);
            }
        }
        Reject(C680,source,"=> profile.Project(args);","=> profile.Project(args) with { ReuseEligible = true };","PC-74");
    }
    private static void Reject(Action<string> audit,string healthy,string from,string to,string pc,string? variant=null)
    {
        var selected=Environment.GetEnvironmentVariable("C1545_PROFILE_CONTROLS");
        var witness=pc switch { "PC-31" or "PC-32" or "PC-33" => "V-7", "PC-40" or "PC-41" or "PC-42" or "PC-43" or "PC-44"=>"V-8",
            "PC-50" or "PC-51"=>"V-11", "PC-38" or "PC-39"=>"V-13", "PC-45"=>"V-14", "PC-56" or "PC-73"=>"V-15", "PC-74"=>"V-16", _=>null };
        if(selected!=witness) return;
        if(!healthy.Contains(from,StringComparison.Ordinal)) Fail(pc+"-site");
        try {audit(healthy.Replace(from,to,StringComparison.Ordinal));}
        catch(InvalidOperationException e) when(e.Message.StartsWith("C1545 profile-audit/"))
        {
            audit(healthy);
            var path=pc switch {"PC-40"=>Pty+"ClaudeSubmitContractTests.cs","PC-44"=>Pty+"ClaudeRemoteControlMenuCanaryTests.cs",
                "PC-38" or "PC-39" or "PC-45" or "PC-56" or "PC-73" or "PC-74"=>"tests/Antiphon.Tests/Agents/ClaudeDialogWatchdogCanaryTests.cs",_=>Startup};
            var raw=LiveClaudeExecutableAudit.Read(path);
            var site=raw.IndexOf(from,StringComparison.Ordinal);
            if(site<0) Fail(pc+"-binding");
            var line=raw[..site].Count(c=>c=='\n')+1;
            Console.WriteLine($"C1545 {pc}/{variant??"caller"} assertion-red; restored-green file={path} line={line} source={LiveClaudeProfile.Hash(raw)} patch={LiveClaudeProfile.Hash(from+"\n"+to)} (owned syntax copy)");
            return;
        }
        Fail(pc+"-survived");
    }
}
