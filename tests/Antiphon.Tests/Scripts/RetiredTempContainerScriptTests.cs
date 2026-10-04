using System.Text.Json.Nodes;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
namespace Antiphon.Tests.Scripts;

[Category("Unit")]
public sealed class RetiredTempContainerScriptTests
{
    private static string LoadFunctions => $$"""
        $ErrorActionPreference='Stop'
        foreach($file in @('scripts/c590-real.ps1','scripts/deploy-server2.ps1')) {
            $ast=[Management.Automation.Language.Parser]::ParseFile('{{DelegateScriptRunner.RepoRoot}}/'+$file,[ref]$null,[ref]$null)
            foreach($function in $ast.EndBlock.Statements | Where-Object {$_ -is [Management.Automation.Language.FunctionDefinitionAst]}) {Invoke-Expression $function.Extent.Text}
        }
        $Sha='aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
        """;
    private static IEnumerable<JsonObject> Cases(JsonObject[] trace) => trace.Where(x=>x["kind"]?.GetValue<string>()=="case");
    private static void NoRecycle(JsonObject[] trace,string label)=>Cases(trace).Any(x=>x["name"]?.GetValue<string>()=="retire-temp-runner").ShouldBeFalse(label);

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C994_Drain_waits_for_exit_and_removes_containers() {
        foreach(var present in new[]{false,true}) {
            using var f=new C1008WrapperFixture();f.State["tempContainer"]=present;f.State["tempExited"]=true;
            f.State["cleanupSequence"]=new JsonArray(true,false);
            var run=await f.Run("drain-temp");run.Exit.ShouldBe(0,"c994-drain-exit: "+run.Output);
            Cases(run.Trace).Count(x=>x["name"]?.GetValue<string>()=="retire-temp-containers").ShouldBe(2,"c994-drain-exit: live proof then exited proof");
            JsonNode.Parse(File.ReadAllText(f.StatePath))!["tempContainer"]!.GetValue<bool>().ShouldBeFalse();NoRecycle(run.Trace,"c994-drain-exit: gate9 separate");
            run.Trace.Any(x=>x["method"]?.GetValue<string>()=="POST").ShouldBeFalse();
        }
    }
    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C994_Drain_uses_one_deadline() {
        foreach(var elapsed in new[]{95,105}) {
            using var f=new C1008WrapperFixture();f.State["cleanupElapsedMs"]=elapsed;
            var run=await f.Run("drain-temp");run.Exit.ShouldBe(elapsed==95?0:2,"c994-one-deadline: "+elapsed+"; "+run.Output);
            if(elapsed==105)run.Output.ShouldContain("TempContainerExitTimeout");
        }
        using var connected=new C1008WrapperFixture();connected.State["statuses"]!["server2-temp"]!["available"]=true;
        connected.State["clockStepMs"]=25;
        var timed=await connected.Run("drain-temp");timed.Exit.ShouldBe(2,"c994-one-deadline: connected");timed.Output.ShouldContain("TempContainerExitTimeout");Cases(timed.Trace).ShouldBeEmpty();
    }
    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C994_Retire_recovers_exited_and_absent_temp() {
        foreach(var present in new[]{false,true})foreach(var zero in new[]{false,true}){
            using var f=new C1008WrapperFixture();f.State["tempContainer"]=present;f.State["tempExited"]=true;if(zero)f.State["statuses"]!["server2-temp"]!["runnerSessions"]=0;
            var run=await f.Run("retire-temp");run.Exit.ShouldBe(0,"c994-retire-order: "+run.Output);
            Cases(run.Trace).Select(x=>x["name"]!.GetValue<string>()).ShouldBe(new[]{"retire-temp-containers","retire-temp-runner"},"c994-retire-order");
            Cases(run.Trace).Last()["recycle"]!["cleanupOperationId"]!.GetValue<string>().ShouldBe(Cases(run.Trace).First()["cleanup"]!["operationId"]!.GetValue<string>());
        }
        using var live=new C1008WrapperFixture();live.State["tempContainer"]=true;
        var refused=await live.Run("retire-temp");refused.Exit.ShouldBe(2,"c994-live-retire");refused.Output.ShouldContain("TempContainerStillRunning");NoRecycle(refused.Trace,"c994-live-retire");
    }
    private static async Task TypedStatus(bool main) {
        using var f=new C1008WrapperFixture();var valid=f.State["statuses"]![main?"server2":"server2-temp"]!.AsObject();var vectors=new JsonArray();
        void Add(string key,bool good,JsonObject status)=>vectors.Add(new JsonObject{["key"]=key,["good"]=good,["status"]=status.DeepClone()});
        Add("valid",true,valid);
        foreach(var field in valid.Select(x=>x.Key).Where(k=>main?new[]{"available","dispatchEligible","acceptingNewWork","draining","retiredAt"}.Contains(k):new[]{"available","dispatchEligible","acceptingNewWork","draining","retireWhenIdle","redirectTo","retiredAt","sessions","queuedTasks","runnerSessions"}.Contains(k))) {
            var missing=valid.DeepClone().AsObject();missing.Remove(field);Add(field+":missing",false,missing);
            foreach(var value in new JsonNode?[]{null,JsonValue.Create("0"),JsonValue.Create(-1),JsonValue.Create(.5),JsonValue.Create(1),JsonValue.Create(false),JsonValue.Create(true)}) {
                var copy=valid.DeepClone().AsObject();copy[field]=value?.DeepClone();
                var expected=JsonNode.DeepEquals(copy[field],valid[field])||!main&&field=="runnerSessions"&&value==null;
                Add(field+":"+(value?.ToJsonString()??"null"),expected,copy);
            }
        }
        File.WriteAllText(f.Root+"/vectors.json",vectors.ToJsonString());
        var run=await C994ScriptProcess.Run("pwsh","-NoProfile","-Command",LoadFunctions+$$"""
            $vectors=Get-Content -Raw '{{f.Root}}/vectors.json'|ConvertFrom-Json
            foreach($v in $vectors) {
              $accepted=$true
              try { {{(main?"Assert-TempCleanupMain -Status $v.status":"[void](Assert-TempCleanupStatus -Status $v.status)")}} } catch {$accepted=$false}
              if($accepted -ne $v.good){throw ('{{(main?"c994-wrapper-main":"c994-wrapper-retirement")}}: '+$v.key)}
            }
            """ );
        run.Exit.ShouldBe(0,(main?"c994-wrapper-main":"c994-wrapper-retirement")+": "+run.Output);
    }
    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public Task C994_Wrapper_retirement_proof_is_typed()=>TypedStatus(false);
    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C994_Wrapper_main_admission_is_required(){await TypedStatus(true);using var f=new C1008WrapperFixture();f.State["statuses"]!["server2"]!["buildVersion"]=new string('b',40);var run=await f.Run("drain-temp");run.Exit.ShouldBe(2,"c994-drain-sha");Cases(run.Trace).ShouldBeEmpty();}
    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C994_Wrapper_task_and_land_census_is_complete(){
        foreach(var fault in new[]{"api","incomplete","bound","land"}) {
            using var f=new C1008WrapperFixture();
            if(fault=="api")f.State["taskError"]="failure";
            else if(fault=="incomplete")f.State["tasks"]!["excluded"]!["total"]=1;
            else f.State["tasks"]!["items"]!.AsArray().Add(new JsonObject{["id"]="22222222-2222-2222-2222-222222222222",["status"]=fault=="bound"?"Working":"Succeeded",["runnerId"]="server2-temp",["projectId"]="aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1",["scopeSource"]="Task",["landRequestedAt"]=fault=="land"?"2026-10-04T00:00:00Z":null,["landStartedAt"]=null});
            var run=await f.Run("retire-temp");run.Exit.ShouldBe(2,"c994-wrapper-work: "+fault);Cases(run.Trace).ShouldBeEmpty("c994-wrapper-work");
        }
    }
    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C994_Strict_volume_admission_remains_independent(){
        using var f=new C1008WrapperFixture();File.WriteAllText(f.Root+"/status.json",f.State["statuses"]!["server2-temp"]!.ToJsonString());
        var run=await C994ScriptProcess.Run("pwsh","-NoProfile","-Command",LoadFunctions+$$"""
            $s=Get-Content -Raw '{{f.Root}}/status.json'|ConvertFrom-Json
            function Assert-TempProjectAbsent {throw 'present'}
            $accepted=$true;try{[void](Assert-RetiredTempCounters $s)}catch{$accepted=$false};if($accepted){throw 'c994-wrapper-strict-null'}
            function Assert-TempProjectAbsent {}
            [void](Assert-RetiredTempCounters $s)
            if($null -ne $s.runnerSessions){throw 'c994-shared-null: input altered'}
            $accepted=$true;try{Assert-ZeroCounters $s 'server2-temp'}catch{$accepted=$false};if($accepted){throw 'c994-shared-null'}
            $s.runnerSessions=0;Assert-ZeroCounters $s 'server2-temp'
            """);run.Exit.ShouldBe(0,"c994-wrapper-strict-null c994-shared-null: "+run.Output);
        using var host=new C1008HostFixture(main:false);host.Docker["containers"]!.AsArray().Add(host.Container('7',"antiphon-runner-temp","session-runner",false));
        var present=await host.Run("retire-temp-runner");present.Exit.ShouldBe(2,"c994-host-strict-null");present.Output.ShouldContain("RunnerCounterUnknown");
    }
    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C994_Preview_reports_pending_volume_proof(){
        foreach(var present in new[]{false,true})foreach(var zero in new[]{false,true}){using var f=new C1008WrapperFixture();f.State["tempContainer"]=present;f.State["tempExited"]=true;if(zero)f.State["statuses"]!["server2-temp"]!["runnerSessions"]=0;
            var run=await f.Run("retire-temp","-DryRun");run.Exit.ShouldBe(0,"c994-preview-pending: "+run.Output);
            if(present){NoRecycle(run.Trace,"c994-preview-pending");run.Output.ShouldContain("volumeProof=pending-confirmed-absence auditPending=true");}
            JsonNode.Parse(File.ReadAllText(f.StatePath))!["tempContainer"]!.GetValue<bool>().ShouldBe(present);run.Trace.Any(x=>x["method"]?.GetValue<string>()=="POST").ShouldBeFalse();}
    }
    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C994_Resume_never_runs_container_cleanup(){
        foreach(var present in new[]{false,true}){using var f=new C1008WrapperFixture();f.State["tempContainer"]=present;
            var run=await f.Run("retire-temp","-ResumeRecycle","c100800000000000000000000000000000001");run.Exit.ShouldBe(present?2:0,"c994-resume-isolation: "+run.Output);Cases(run.Trace).Any(x=>x["name"]?.GetValue<string>()=="retire-temp-containers").ShouldBeFalse("c994-resume-isolation");}
    }
    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C994_Cleanup_receipt_is_required_before_recycle(){
        foreach(var fault in new[]{"receiptMissing","receiptMismatch","receiptCopyFail","cleanupFail","driftAfterCleanup"}){using var f=new C1008WrapperFixture();f.State[fault]=true;
            var run=await f.Run("retire-temp");run.Exit.ShouldBe(2,"c994-receipt-before-recycle c994-fresh-volume-proof: "+fault+"; "+run.Output);NoRecycle(run.Trace,"c994-receipt-before-recycle");}
    }
    private static string Context=>"{\"version\":1,\"operationId\":\"c99400000000000000000000000000000001\",\"project\":\"antiphon-runner-temp\",\"projectId\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1\",\"dryRun\":false,\"retiredAt\":\"2026-10-03T09:30:00Z\"}";
    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C994_Verifier_manifest_is_strict(){
        using var f=new C1008WrapperFixture();var input=new JsonObject{["tempContainerCleanup"]=JsonNode.Parse(Context),["evidenceRoot"]=f.Root};
        foreach(var field in new[]{"version","operationId","project","projectId","dryRun","retiredAt"}) {
            var missing=input.DeepClone().AsObject();missing["tempContainerCleanup"]!.AsObject().Remove(field);File.WriteAllText(f.Root+"/manifest.json",missing.ToJsonString());
            var run=await C994ScriptProcess.Run("pwsh","-NoProfile","-File","scripts/verify-docker-stack.ps1","-Case","retire-temp-containers","-Manifest",f.Root+"/manifest.json");run.Exit.ShouldNotBe(0,"c994-verifier-context: "+field);
        }
        var duplicate=Context.Replace("\"dryRun\":false","\"dryRun\":false,\"dryRun\":false",StringComparison.Ordinal);
        var raw=await C994ScriptProcess.Run("pwsh","-NoProfile","-Command",LoadFunctions+"Assert-C994RawManifest '"+duplicate+"'");raw.Exit.ShouldNotBe(0,"c994-verifier-context: duplicate");
        var addition=await C994ScriptProcess.Run("pwsh","-NoProfile","-Command",LoadFunctions+"""
            $c=[pscustomobject]@{version=1;project='antiphon-runner-temp';operationId=('c1008'+('a'*32));projectId='aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1';dryRun=$false;resume=$false;cleanupOperationId=('c994'+('a'*32))}
            Assert-C1008BridgeContext $c 'retire-temp-runner'
            $c|Add-Member -NotePropertyName foreign -NotePropertyValue $true
            $ok=$true;try{Assert-C1008BridgeContext $c 'retire-temp-runner'}catch{$ok=$false};if($ok){throw 'c994-recycle-addition'}
            """);addition.Exit.ShouldBe(0,"c994-recycle-addition: "+addition.Output);
    }
    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C994_Bridge_transport_preserves_literal_context(){
        var run=await C994ScriptProcess.Run("pwsh","-NoProfile","-Command",LoadFunctions+"$c='"+Context+"'|ConvertFrom-Json\n"+"""
            Assert-C994BridgeContext $c 'retire-temp-containers'
            $a=ConvertTo-C994RetiredInstant $c.retiredAt
            $c.retiredAt='2026-10-03T11:30:00+02:00';$b=ConvertTo-C994RetiredInstant $c.retiredAt
            if($a -cne $b){throw 'c994-literal-transport'}
            foreach($value in @('false',1,$null,"';touch /tmp/c994-command-sentinel;#")){
              $c.dryRun=$value;$ok=$true;try{Assert-C994BridgeContext $c 'retire-temp-containers'}catch{$ok=$false};if($ok){throw 'c994-bridge-context'}
            }
            """);run.Exit.ShouldBe(0,"c994-bridge-context c994-literal-transport: "+run.Output);
        foreach(var file in new[]{"scripts/deploy-server2.ps1","scripts/c590-real.ps1","scripts/verify-docker-stack.ps1"})File.ReadAllBytes(Path.Combine(DelegateScriptRunner.RepoRoot,file)).All(b=>b<128).ShouldBeTrue("c994-literal-transport: ASCII");
    }
    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C994_Separate_invocations_preserve_audit_identity(){
        using var f=new C1008WrapperFixture();f.State["tempContainer"]=true;f.State["tempExited"]=true;
        var drain=await f.Run("drain-temp");drain.Exit.ShouldBe(0,"c994-cross-run-image: "+drain.Output);
        var saved=JsonNode.Parse(File.ReadAllText(f.StatePath))!.AsObject();f.State.Clear();foreach(var (key,value) in saved)f.State[key]=value?.DeepClone();
        var retire=await f.Run("retire-temp");retire.Exit.ShouldBe(0,"c994-cross-run-image: "+retire.Output);
        var ops=Cases(retire.Trace).Where(x=>x["name"]?.GetValue<string>()=="retire-temp-containers").Select(x=>x["cleanup"]!["operationId"]!.GetValue<string>()).ToArray();ops.Distinct().Count().ShouldBe(2);
        using var present=new C1008WrapperFixture();present.State["tempContainer"]=true;
        var refused=await present.Run("deploy-temp");refused.Exit.ShouldBe(2,"c994-cross-run-image: leftover");refused.Trace.Any(x=>x["method"]?.GetValue<string>()=="POST").ShouldBeFalse();
    }
}
