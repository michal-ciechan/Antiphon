using System.Diagnostics;
using Antiphon.TestSupport.LiveClaude;

namespace Antiphon.Agents.Pty.Tests;

internal static class LiveClaudeStartupCases
{
    internal static StartupExperiment Experiment => new("run-A", "host-epoch-A", "image-A", "profile-A", "settings-A");
    internal static StartupOwner Owner => new(101, "start-A", "session-A", "/owned/cwd");
    internal static StartupSettings Settings(string session) => new(
        new[] { "user", "project", "local" }.Select(s => new StartupMarker(s, session, "retained-" + s)).ToArray(),
        ["permission-mode", "atis-latch"]);
    internal static StartupControl Control => new(Experiment, Owner, new(Owner, "bridge-A"), TimeSpan.FromSeconds(2), 0, Settings(Owner.Session));
    internal static void Assert(bool value, string v, string c)
    { if (!value) throw new InvalidOperationException($"C1545 {v}/{c} required predicate"); }
    internal static void Refuses(Action action, string reason, string v, string c)
    {
        try { action(); }
        catch (LiveClaudeRefusal e) { Assert(e.Reason == reason && e.ExitCode == 20 && !e.Credit, v, c); return; }
        throw new InvalidOperationException($"C1545 {v}/{c} expected 20 {reason}");
    }
    internal static void Fails(Action action, string prefix, string v, string c)
    {
        try { action(); }
        catch (InvalidOperationException e) when (e is not LiveClaudeRefusal && e.Message.StartsWith(prefix, StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException($"C1545 {v}/{c} expected assertion-red");
    }

    public static async Task RunAsync(string v)
    {
        var started = Stopwatch.StartNew();
        switch (v)
        {
            case "V-18": await BridgeAsync(); break;
            case "V-19": await ControlAsync(); break;
            case "V-20": await WindowAsync(); break;
            case "V-21":
                Assert(ReferenceEquals(ClaudeRemoteControlAtStartupCanaryTests.SharedStartup(),ClaudeRemoteControlAtStartupCanaryTests.SharedStartup()),v,"session-shared-instance");
                LiveClaudeStartupAudit.Run(); break;
            case "V-22": await SettingsAsync(); break;
            default: throw new InvalidOperationException("Unknown startup witness");
        }
        await LiveClaudeStartupMutants.RunAsync(v);
        Console.WriteLine($"C1545 {v} bodyMs={started.Elapsed.TotalMilliseconds:F1} setup=owned-fake db=N/A http=N/A apphost=N/A native=N/A");
    }

    internal static async Task BridgeAsync()
    {
        const string v = "V-18";
        LiveClaudeStartupEvidence.RequireBridge(Owner, Control.Bridge);
        foreach (var (name, bridge) in new (string, StartupBridge?)[] {
            ("badge",null), ("jsonl",null), ("foreign-pid",new(Owner with { Pid=102 },"bridge-A")),
            ("foreign-session",new(Owner with {Session="foreign"},"bridge-A")),
            ("foreign-start",new(Owner with {Started="foreign"},"bridge-A")),
            ("foreign-cwd",new(Owner with {Cwd="/foreign"},"bridge-A")),
            ("unreadable",new(Owner,"bridge-A",false)) })
            Refuses(() => LiveClaudeStartupEvidence.RequireBridge(Owner,bridge),"bridge-unestablished",v,name);
        var off = new StartupObservation(Owner,null,TimeSpan.FromSeconds(15),Settings(Owner.Session));
        LiveClaudeStartupEvidence.RequireSlash(Experiment,Control,off,new(Owner,"bridge-new"));
        Refuses(() => LiveClaudeStartupEvidence.RequireSlash(Experiment,Control,off,null),"bridge-unestablished",v,"slash-setup");
        Refuses(() => LiveClaudeStartupEvidence.RequireControl(Experiment,Control with {ModelPrompts=1}),"premise-absent",v,"zero-prompts");
        var arms=0;
        var established=await LiveClaudeStartupEvidence.EstablishBridgeAsync(Owner,
            ()=>Task.FromResult<StartupBridge?>(null),()=> {arms++; return Task.FromResult<StartupBridge?>(new(Owner,"bridge-new"));});
        Assert(arms==1 && LiveClaudeStartupEvidence.OwnedBridge(Owner,established),v,"shared-setup");
        var already=await LiveClaudeStartupEvidence.EstablishBridgeAsync(Owner,
            ()=>Task.FromResult(Control.Bridge),()=> {arms++; return Task.FromResult<StartupBridge?>(null);});
        Assert(arms==1 && already==Control.Bridge,v,"already-ready");
        // The reader consumes a real owned synthetic sidecar, rather than a fake badge verdict.
        var root=Directory.CreateTempSubdirectory("c1545-bridge-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root,"sessions"));
            var file=Path.Combine(root,"sessions","101.json");
            File.WriteAllText(file,"{\"sessionId\":\"session-A\",\"cwd\":\"/owned/cwd\",\"bridgeSessionId\":\"bridge-A\"}");
            LiveClaudeStartupEvidence.RequireBridge(Owner,LiveClaudeStartupEvidence.ReadBridge(root,Owner));
            File.WriteAllText(file,"{\"sessionId\":\"foreign\",\"cwd\":\"/owned/cwd\",\"bridgeSessionId\":\"bridge-A\"}");
            Refuses(()=>LiveClaudeStartupEvidence.RequireBridge(Owner,LiveClaudeStartupEvidence.ReadBridge(root,Owner)),"bridge-unestablished",v,"reader-foreign");
            File.WriteAllText(file,"{");
            Refuses(()=>LiveClaudeStartupEvidence.RequireBridge(Owner,LiveClaudeStartupEvidence.ReadBridge(root,Owner)),"bridge-unestablished",v,"reader-partial");
        }
        finally { Directory.Delete(root,true); }

    }

    internal static async Task ControlAsync()
    {
        const string v="V-19";
        var fixture=new LiveClaudeStartupFixture();
        var barrier=new TaskCompletionSource<StartupControl>(TaskCreationOptions.RunContinuationsAsynchronously);
        var starts=0; var dependents=0;
        Task<StartupControl> Start() { Interlocked.Increment(ref starts); return barrier.Task; }
        var off=fixture.BeforeDependentAsync(Experiment,Start,()=>Interlocked.Increment(ref dependents));
        var slash=fixture.BeforeDependentAsync(Experiment,Start,()=>Interlocked.Increment(ref dependents));
        Assert(starts==1,v,"control-once"); Assert(dependents==0,v,"control-order");
        barrier.SetResult(Control); await Task.WhenAll(off,slash);
        Assert(dependents==2,v,"control-complete");
        var observation=new StartupObservation(Owner,null,TimeSpan.FromSeconds(15),Settings(Owner.Session));
        LiveClaudeStartupEvidence.RequireOff(Experiment,Control,observation);
        foreach(var (name,control) in new (string,StartupControl?)[] {
            ("control-absent",null), ("control-interrupted",Control with {Completed=false}),
            ("control-bridge",Control with {Bridge=null}),
            ("control-foreign",Control with {Experiment=Experiment with {Run="prior-run"}}),
            ("control-epoch",Control with {Experiment=Experiment with {Epoch="old"}}),
            ("control-binary",Control with {Experiment=Experiment with {Binary="other"}}),
            ("control-profile",Control with {Experiment=Experiment with {Profile="other"}}),
            ("control-settings",Control with {Experiment=Experiment with {Settings="other"}}) })
            Refuses(()=>LiveClaudeStartupEvidence.RequireOff(Experiment,control,observation),"premise-absent",v,name);
        Refuses(()=>fixture.ControlAsync(Experiment with {Run="fresh"},Start),"premise-absent",v,"fixture-foreign");
        var failed=new LiveClaudeStartupFixture();
        var error=new InvalidOperationException("interrupted");
        try { await failed.BeforeDependentAsync(Experiment,()=>Task.FromException<StartupControl>(error),()=>dependents++); }
        catch(InvalidOperationException e) when(ReferenceEquals(e,error)) { }
        Assert(dependents==2,v,"control-failed-dependent");
        var synchronous=new LiveClaudeStartupFixture();
        var synchronousStarts=0;
        for(var attempt=0;attempt<2;attempt++)
        {
            try { await synchronous.BeforeDependentAsync(Experiment,()=> {synchronousStarts++;throw error;},()=>dependents++); }
            catch(InvalidOperationException e) when(ReferenceEquals(e,error)) { }
        }
        Assert(synchronousStarts==1 && dependents==2,v,"control-sync-failure-once");
    }

    internal static async Task WindowAsync()
    {
        const string v="V-20";
        Fails(() => LiveClaudeStartupEvidence.RequireOff(Experiment, Control, new(Owner, null, TimeSpan.FromSeconds(1), Settings(Owner.Session))), "C1545 startup/window", v, "incomplete-window");
        foreach(var arrival in new[] {8.001,12,14.999,15})
        {
            var elapsed=TimeSpan.Zero;
            var observation=await LiveClaudeStartupEvidence.ObserveOffAsync(Owner,Control,()=>elapsed,
                ()=>Task.FromResult(elapsed>=TimeSpan.FromSeconds(arrival)?new StartupBridge(Owner,"late-bridge"):null),
                step=>
                {
                    var next=elapsed+step;
                    var eventTime=TimeSpan.FromSeconds(arrival);
                    elapsed=elapsed<eventTime && next>eventTime ? eventTime : next;
                    return Task.CompletedTask;
                },()=>Settings(Owner.Session));
            Assert(LiveClaudeStartupEvidence.OwnedBridge(Owner,observation.Bridge),v,arrival==15?"deadline-bridge":"late-bridge");
            Fails(()=>LiveClaudeStartupEvidence.RequireOff(Experiment,Control,observation),"C1545 startup/off",v,arrival==15?"deadline-bridge":"late-bridge");
        }
        foreach(var latency in new[] {2d,18d})
        {
            var control=Control with {Latency=TimeSpan.FromSeconds(latency)};
            var elapsed=TimeSpan.Zero;
            var observation=await LiveClaudeStartupEvidence.ObserveOffAsync(Owner,control,()=>elapsed,
                ()=>Task.FromResult<StartupBridge?>(null),step=> {elapsed+=step;return Task.CompletedTask;},()=>Settings(Owner.Session));
            Assert(observation.Elapsed>=TimeSpan.FromSeconds(Math.Max(15,latency)),v,latency==18?"latency-bound":"minimum-window");
            LiveClaudeStartupEvidence.RequireOff(Experiment,control,observation);
        }

    }

    internal static async Task SettingsAsync(string? writerPath = null)
    {
        const string v="V-22";
        var root=Directory.CreateTempSubdirectory("c1545-settings-").FullName;
        var file=Path.Combine(root,"markers.jsonl");
        try
        {
            // Only the actual hook child writes the healthy marker bytes.
            foreach(var source in new[]{"user","project","local"})
            {
                var info=new ProcessStartInfo("node") {RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false};
                info.ArgumentList.Add(writerPath ?? Path.Combine(LiveClaudeExecutableAudit.Root(),"tests/Antiphon.Agents.Pty.Tests/probes/settings-source-witness.mjs"));
                info.ArgumentList.Add(file); info.ArgumentList.Add(source); info.ArgumentList.Add("retained-"+source);
                using var child=Process.Start(info) ?? throw new InvalidOperationException("hook child absent");
                await child.StandardInput.WriteAsync("{\"session_id\":\"session-A\"}"); child.StandardInput.Close();
                using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try { await child.WaitForExitAsync(deadline.Token); }
                catch (OperationCanceledException) { child.Kill(entireProcessTree:true); await child.WaitForExitAsync(); throw; }
                Assert(child.ExitCode==0,v,"hook-writer");
            }
            Assert(File.Exists(file) && new FileInfo(file).Length>0,v,"marker-writer");
            var settings=LiveClaudeStartupEvidence.ReadSettings(file,["permission-mode","atis-latch"]);
            LiveClaudeStartupEvidence.RequireSettings(Control,Owner,settings);
            foreach(var source in new[]{"user","project","local"})
            {
                Refuses(()=>LiveClaudeStartupEvidence.RequireSettings(Control,Owner,settings with {Markers=settings.Markers.Where(m=>m.Source!=source).ToArray()}),"settings-unobserved",v,"marker-"+source);
                Refuses(()=>LiveClaudeStartupEvidence.RequireSettings(Control,Owner,settings with {Markers=settings.Markers.Select(m=>m.Source==source?m with {Session="foreign"}:m).ToArray()}),"settings-unobserved",v,"marker-foreign-"+source);
                Fails(()=>LiveClaudeStartupEvidence.RequireSettings(Control,Owner,settings with {Markers=settings.Markers.Select(m=>m.Source==source?m with {Retained="changed"}:m).ToArray()}),"C1545 startup/retained",v,"retained-setting-"+source);
            }
            Fails(()=>LiveClaudeStartupEvidence.RequireSettings(Control,Owner,settings with {Records=[]}),"C1545 startup/records",v,"startup-records");
            Refuses(()=>LiveClaudeStartupEvidence.RequireSettings(Control,Owner,new([],[])),"settings-unobserved",v,"no-evidence");
        }
        finally {Directory.Delete(root,true);}
    }
}
