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
        $Sha='aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa';
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
            using var f=new C1008WrapperFixture();f.State["cleanupElapsedMs"]=elapsed-80;
            var retired=f.State["statuses"]!["server2-temp"]!.DeepClone();var pending=retired.DeepClone();pending["retiredAt"]=null;pending["available"]=true;pending["dispatchEligible"]=true;pending["runnerSessions"]=0;
            f.State["statusSequence"]=new JsonArray(new JsonObject{["clockMs"]=0,["status"]=pending},new JsonObject{["clockMs"]=80,["status"]=retired});
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
        if(!main) {
            var zero=valid.DeepClone().AsObject();zero["runnerSessions"]=0;Add("zero",true,zero);
            var offset=valid.DeepClone().AsObject();offset["retiredAt"]="2026-10-03T11:30:00+02:00";Add("offset",true,offset);
            foreach(var stamp in new[]{"","2026-02-30T00:00:00Z"}){var invalid=valid.DeepClone().AsObject();invalid["retiredAt"]=stamp;Add("invalid-stamp:"+stamp,false,invalid);}
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
        using(var batch=new C1008HostFixture()) {
            var vectors=C994TaskVectors.Build(batch.Vectors["emptyTasks"]!.AsObject());File.WriteAllText(batch.Root+"/task-vectors.json",vectors.ToJsonString());
            var proof=await C994ScriptProcess.Run("pwsh","-NoProfile","-Command",LoadFunctions+$$"""
                $vectors=Get-Content -Raw '{{batch.Root}}/task-vectors.json'|ConvertFrom-Json
                foreach($v in $vectors){
                    $script:vector=$v.input
                    function Invoke-RecycleRead {param([string]$Path)
                        if($Path -match 'projectId=([^&]+)'){$scope=$Matches[1];$kind=if($Path.Contains('&landPending=true')){'land'}elseif($Path.Contains('&status=Queued,Dispatched,Working,Blocked')){'open'}else{throw 'unfiltered query'};$value=$script:vector.scopes.$scope.$kind}else{$value=$script:vector.details.($Path.Split('/')[-1])}
                        if($null -eq $value){throw 'api failed'};return $value
                    }
                    $script:called=0;function Invoke-HostCase {$script:called++}
                    $accepted=$true;try{[void](Assert-RecycleTaskCensus 'server2-temp' '{{C994TaskVectors.Project}}');Invoke-HostCase}catch{$accepted=$false}
                    if($accepted -ne $v.accepted -or $script:called -ne [int]$v.accepted){throw ('c994-wrapper-work: '+$v.key)}
                }
                """);
            proof.Exit.ShouldBe(0,"c994-wrapper-work: independent matrix; "+proof.Output);
        }
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
        var strict=await host.Run("retire-temp-runner","""
            LANE=host;C1008_PROJECT="$TEMP_PROJECT";C1008_RUNNER=server2-temp
            original_census=$(declare -f c1008_container_census);eval "${original_census/c1008_container_census/c994_original_census}"
            original_status=$(declare -f c849_status_body);eval "${original_status/c849_status_body/c994_original_status}"
            for variant in present-null absent-null shared-null shared-zero;do
                (
                    c1008_refuse(){ printf '%s\n' "$1";exit 2; }
                    if [ "$variant" = absent-null ];then c1008_container_census(){ printf '[]'; };fi
                    if [ "$variant" = shared-zero ];then c849_status_body(){ c994_original_status "$1" | jq '.runnerSessions=0'; };fi
                    code=0
                    case "$variant" in
                      present-null|absent-null) (c1008_status_proof) > "$C1008_FIXTURE_ROOT/strict-$variant.log" 2>&1 || code=$? ;;
                      *) c849_status_zero server2-temp || code=$? ;;
                    esac
                    case "$variant" in
                      present-null) [ "$code" = 2 ] && grep -q RunnerCounterUnknown "$C1008_FIXTURE_ROOT/strict-$variant.log" || exit 3 ;;
                      shared-null) [ "$code" = 1 ] || exit 3 ;;
                      *) [ "$code" = 0 ] || exit 3 ;;
                    esac
                    printf 'STRICT_CASE %s\n' "$variant"
                ) || exit 2
            done
            write_result true '' 0
            """);
        strict.Exit.ShouldBe(0,"c994-host-strict-null c994-shared-null: direct predicates; "+strict.Output);
        strict.Output.Split('\n').Where(x=>x.StartsWith("STRICT_CASE ")).Select(x=>x[12..]).ShouldBe(new[]{"present-null","absent-null","shared-null","shared-zero"},"c994-host-strict-null: exact controls");
        host.Trace.Any(a=>a[0] is "rm" or "stop" or "create"||a.Take(2).SequenceEqual(new[]{"volume","rm"})).ShouldBeFalse("c994-shared-null: read-only proof");
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
        using var host=new C1008HostFixture();
        var proof=await host.Run(extra:"""
            LANE=host;C1008_PROJECT="$HOST_PROJECT"
            # Live containers are the running generation. Resume reconciles that roster.
            model="$(c1008_previous_model "$(sed -n 's/^SOURCE_REVISION=//p' "$SERVER2_ENV")")" || exit 2
            volumes="$(jq -c .volumes "$C1008_FIXTURE_ROOT/docker.json")"
            fresh="$(c1008_container_census | jq -c '[.[]|select(.Config.Labels["com.docker.compose.service"]!="build-slots")]')"
            c1008_owned_mounts "$fresh" "$model" "$volumes";saved="$C1008_OWNED"
            for variant in valid version bind tmpfs; do
                (
                    C994_FIXTURE_RESUME_CENSUS="$fresh";original="$saved"
                    case "$variant" in
                      version) original="$(printf '%s' "$saved"|jq -c '.[0].Topology|=del(.version)')" ;;
                      bind) C994_FIXTURE_RESUME_CENSUS="$(printf '%s' "$fresh"|jq -c '(.[0].Mounts[]|select(.Type=="bind")).Source="/foreign"')" ;;
                      tmpfs) C994_FIXTURE_RESUME_CENSUS="$(printf '%s' "$fresh"|jq -c '.[0].HostConfig.Tmpfs["/run/antiphon"]="ro"')" ;;
                    esac
                    C1008_RECORD="$(jq -cn --argjson owned "$original" --argjson volumes "$volumes" '{owned:$owned,volumes:$volumes,preserved:{},stopIntents:[],stopReceipts:[],removeIntents:[],removeReceipts:[],phase:"preflight"}')"
                    c1008_container_census(){ printf '%s' "$C994_FIXTURE_RESUME_CENSUS"; }
                    c1008_refuse(){ printf '%s\n' "$1";exit 2; }
                    c1008_save(){ printf '%s' "$C1008_RECORD"|jq -e '.removeIntents==[]' >/dev/null; }
                    c1008_reconcile_owned "$model"
                ) > "$C1008_FIXTURE_ROOT/resume-$variant.log" 2>&1 && code=0 || code=$?
                if [ "$variant" = valid ];then [ "$code" = 0 ] || exit 2;else [ "$code" = 2 ] && grep -q RecycleResumeMismatch "$C1008_FIXTURE_ROOT/resume-$variant.log" || exit 2;fi
                printf 'RESUME_CASE %s %s\n' "$variant" "$code"
            done
            write_result true '' 0
            """);
        proof.Exit.ShouldBe(0,"c994-resume-isolation: direct shared reconcile; "+proof.Output);
        proof.Output.Split('\n').Count(x=>x.StartsWith("RESUME_CASE ")).ShouldBe(4,"c994-resume-isolation: complete tuple vectors");
        host.Trace.Any(a=>a[0] is "rm" or "stop" or "create").ShouldBeFalse("c994-resume-isolation: no new intent effects");
    }
    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C994_Cleanup_receipt_is_required_before_recycle(){
        foreach(var fault in new[]{"receiptMissing","receiptMismatch","receiptCopyFail","cleanupFail","driftAfterCleanup"}){using var f=new C1008WrapperFixture();f.State[fault]=true;
            var run=await f.Run("retire-temp");run.Exit.ShouldBe(2,"c994-receipt-before-recycle c994-fresh-volume-proof: "+fault+"; "+run.Output);NoRecycle(run.Trace,"c994-receipt-before-recycle");}
    }
    private static string Context=>"{\"version\":1,\"operationId\":\"c99400000000000000000000000000000001\",\"project\":\"antiphon-runner-temp\",\"projectId\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1\",\"dryRun\":false,\"retiredAt\":\"2026-10-03T09:30:00Z\"}";
    private static List<(string Key,string Json,bool Accepted)> CleanupContextVectors() {
        var vectors=new List<(string,string,bool)>{("valid",Context,true)};
        void Add(string key,string field,JsonNode? value,bool accepted=false) {
            var c=JsonNode.Parse(Context)!.AsObject();c[field]=value?.DeepClone();vectors.Add((key,c.ToJsonString(),accepted));
        }
        Add("preview","dryRun",JsonValue.Create(true),true);
        Add("offset","retiredAt",JsonValue.Create("2026-10-03T11:30:00+02:00"),true);
        Add("fraction:utc","retiredAt",JsonValue.Create("2026-10-03T09:30:00.1234567Z"),true);
        Add("fraction:offset","retiredAt",JsonValue.Create("2026-10-03T11:30:00.1234567+02:00"),true);
        foreach(var field in new[]{"version","operationId","project","projectId","dryRun","retiredAt"}) {
            var missing=JsonNode.Parse(Context)!.AsObject();missing.Remove(field);vectors.Add((field+":missing",missing.ToJsonString(),false));
            Add(field+":null",field,null);
            Add(field+":array",field,new JsonArray());
            Add(field+":object",field,new JsonObject());
            Add(field+":type",field,field=="version"?JsonValue.Create("1"):field=="dryRun"?JsonValue.Create("false"):JsonValue.Create(1));
        }
        Add("version:unsupported","version",JsonValue.Create(2));Add("version:fraction","version",JsonValue.Create(1.5));
        Add("operation:path","operationId",JsonValue.Create("../receipt"));
        Add("operation:case","operationId",JsonValue.Create("C994"+new string('a',32)));
        Add("project:foreign","project",JsonValue.Create("antiphon-runner"));
        Add("projectId:case","projectId",JsonValue.Create("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA"));
        Add("dryRun:number","dryRun",JsonValue.Create(0));
        var stampIndex=0;
        foreach(var value in new[]{"","2026-02-30T00:00:00Z","2026-10-03T09:30:00","';touch /tmp/c994-command-sentinel;#","2026-10-03T09:30:00Z\nexport C994_FORCE=1"})Add("stamp:"+stampIndex++,"retiredAt",JsonValue.Create(value));
        Add("stamp:fraction-without-zone","retiredAt",JsonValue.Create("2026-10-03T09:30:00.1234567"));
        foreach(var field in new[]{"force","volumes","containerId","path","image"})Add("unknown:"+field,field,JsonValue.Create("foreign"));
        vectors.Add(("array","[]",false));vectors.Add(("scalar","1",false));vectors.Add(("null","null",false));
        vectors.Add(("duplicate",Context.Replace("\"dryRun\":false","\"dryRun\":false,\"dryRun\":false",StringComparison.Ordinal),false));
        vectors.Add(("duplicate:stamp",Context.Replace("\"retiredAt\":", "\"retiredAt\":\"2026-10-03T09:30:00\",\"retiredAt\":",StringComparison.Ordinal),false));
        return vectors;
    }
    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C994_Verifier_manifest_is_strict(){
        using var f=new C1008WrapperFixture();
        foreach(var name in new[]{"c590-command.ps1","c590-real.ps1"})File.Copy(Path.Combine(DelegateScriptRunner.RepoRoot,"scripts",name),Path.Combine(f.Root,name));
        var verifier=File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot,"scripts/verify-docker-stack.ps1"));
        // Intercept only the outbound bridge; keep each actual verifier admission check.
        var boundary=". (Join-Path $PSScriptRoot 'c590-real.ps1')";
        verifier=verifier.Replace(boundary,boundary+$$"""

            function Invoke-C590LiveCase {param($Case,$Manifest,$RawManifest) Set-Content -LiteralPath '{{f.Root}}/dispatched' -Value $Case;exit 0}
            """,StringComparison.Ordinal);
        File.WriteAllText(f.Root+"/verifier.ps1",verifier);
        var checkedKeys=new List<string>();
        foreach(var (key,json,accepted) in CleanupContextVectors()) {
            File.Delete(f.Root+"/dispatched");
            File.WriteAllText(f.Root+"/manifest.json","{\"sourceSha\":\""+new string('a',40)+"\",\"runId\":\"fixture\",\"evidenceRoot\":"+System.Text.Json.JsonSerializer.Serialize(f.Root)+",\"tempContainerCleanup\":"+json+"}");
            var run=await C994ScriptProcess.Run("pwsh","-NoProfile","-File",f.Root+"/verifier.ps1","-Case","retire-temp-containers","-Manifest",f.Root+"/manifest.json");
            (run.Exit==0).ShouldBe(accepted,"c994-verifier-context: "+key+"; "+run.Output);
            File.Exists(f.Root+"/dispatched").ShouldBe(accepted,"c994-verifier-context: predispatch "+key);
            if(!accepted)run.Output.ShouldContain("TempContainerContextInvalid",customMessage:"c994-verifier-context: specific refusal "+key);
            checkedKeys.Add(key);
        }
        checkedKeys.ShouldBe(CleanupContextVectors().Select(x=>x.Key),"c994-verifier-context: complete raw roster");
        var duplicate=Context.Replace("\"dryRun\":false","\"dryRun\":false,\"dryRun\":false",StringComparison.Ordinal);
        var raw=await C994ScriptProcess.Run("pwsh","-NoProfile","-Command",LoadFunctions+"Assert-C994RawManifest '"+duplicate+"'");raw.Exit.ShouldNotBe(0,"c994-verifier-context: duplicate");
        var addition=await C994ScriptProcess.Run("pwsh","-NoProfile","-Command",LoadFunctions+"""
            $c=[pscustomobject]@{version=1;project='antiphon-runner-temp';operationId=('c1008'+('a'*32));projectId='aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1';dryRun=$false;resume=$false;cleanupOperationId=('c994'+('a'*32))}
            Assert-C1008BridgeContext $c 'retire-temp-runner'
            Assert-RecycleContext $c
            foreach($variant in @('main','resume','unknown','operation')) {
              $bad=$c.PSObject.Copy()
              switch($variant){main{$bad.project='antiphon-runner'} resume{$bad.resume=$true} unknown{$bad|Add-Member -NotePropertyName foreign -NotePropertyValue $true} operation{$bad.cleanupOperationId='../receipt'}}
              $ok=$true;try{Assert-RecycleContext $bad}catch{$ok=$false};if($ok){throw ('c994-recycle-addition: wrapper '+$variant)}
            }
            foreach($case in @('deploy-parent','retire-temp-runner')) {
              $c.resume=$case -eq 'retire-temp-runner';$ok=$true;try{Assert-C1008BridgeContext $c $case}catch{$ok=$false};if($ok){throw 'c994-recycle-addition: wrong lane or resume'}
            }
            $c.resume=$false
            $c|Add-Member -NotePropertyName foreign -NotePropertyValue $true
            $ok=$true;try{Assert-C1008BridgeContext $c 'retire-temp-runner'}catch{$ok=$false};if($ok){throw 'c994-recycle-addition'}
            """);addition.Exit.ShouldBe(0,"c994-recycle-addition: "+addition.Output);
    }
    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C994_Bridge_transport_preserves_literal_context(){
        using(var vectorsRoot=new C1008WrapperFixture()) {
            var vectors=new JsonArray();foreach(var (key,json,accepted) in CleanupContextVectors())vectors.Add(new JsonObject{["key"]=key,["raw"]="{\"tempContainerCleanup\":"+json+"}",["accepted"]=accepted});
            File.WriteAllText(vectorsRoot.Root+"/transport-vectors.json",vectors.ToJsonString());
            var proof=await C994ScriptProcess.Run("pwsh","-NoProfile","-Command",LoadFunctions+$$"""
                $vectors=Get-Content -Raw '{{vectorsRoot.Root}}/transport-vectors.json'|ConvertFrom-Json
                foreach($v in $vectors){
                  $ok=$true;try{Assert-C994RawManifest $v.raw;$m=$v.raw|ConvertFrom-Json;Assert-C994BridgeContext $m.tempContainerCleanup 'retire-temp-containers'}catch{$ok=$false}
                  if($ok -ne $v.accepted){throw ('c994-bridge-context: '+$v.key)}
                  Write-Output ('BRIDGE_CASE '+$v.key)
                }
                $c='{{Context}}'|ConvertFrom-Json
                $ok=$true;try{Assert-C994BridgeContext $c 'retire-temp-runner'}catch{$ok=$false};if($ok){throw 'c994-bridge-context: wrong case'}
                """);
            proof.Exit.ShouldBe(0,"c994-bridge-context: independent raw matrix; "+proof.Output);
            proof.Output.Split('\n').Where(x=>x.StartsWith("BRIDGE_CASE ")).Select(x=>x[12..].TrimEnd('\r')).ShouldBe(CleanupContextVectors().Select(x=>x.Key),"c994-bridge-context: exact roster");
        }
        var run=await C994ScriptProcess.Run("pwsh","-NoProfile","-Command",LoadFunctions+"$c='"+Context+"'|ConvertFrom-Json\n"+"""
            Assert-C994BridgeContext $c 'retire-temp-containers'
            $a=ConvertTo-C994RetiredInstant $c.retiredAt
            $c.retiredAt='2026-10-03T11:30:00+02:00';$b=ConvertTo-C994RetiredInstant $c.retiredAt
            if($a -cne $b){throw 'c994-literal-transport'}
            foreach($value in @('false',1,$null,"';touch /tmp/c994-command-sentinel;#")){
              $c.dryRun=$value;$ok=$true;try{Assert-C994BridgeContext $c 'retire-temp-containers'}catch{$ok=$false};if($ok){throw 'c994-bridge-context'}
            }
            """);run.Exit.ShouldBe(0,"c994-bridge-context c994-literal-transport: "+run.Output);
        using var host=new C1008HostFixture(main:false);
        host.Docker["containers"]!.AsArray().Add(host.Container('7',"antiphon-runner-temp","session-runner",false));
        var cleaned=await host.Run("retire-temp-containers");cleaned.Exit.ShouldBe(0,"c994-literal-transport: host evidence; "+cleaned.Output);
        await CheckLiveBridge(host,false);
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
    internal static async Task CheckLiveBridge(C1008HostFixture host, bool copyFailure)
    {
        var hostEvidence = Path.Combine(host.Root, "evidence/retire-temp-containers");
        var receipt = JsonNode.Parse(File.ReadAllText(hostEvidence + "/temp-containers.json"))!;
        var root = host.Root + "/live-bridge";
        Directory.CreateDirectory(root + "/bin");
        var shim = """
            #!/usr/bin/env node
            const fs=require('fs'),path=require('path'),cp=require('child_process');
            const root=process.env.C994_TEST_BRIDGE_ROOT,kind=path.basename(process.argv[1]),args=process.argv.slice(2);
            fs.appendFileSync(root+'/arguments.jsonl',JSON.stringify({kind,args})+'\n');
            if(kind==='ssh'&&args.at(-1).startsWith('export ')) {
              const remote=args.at(-1),end='bash /home/mc/antiphon-c590/c590-remote.sh';
              if(!remote.endsWith(end))throw Error('unexpected host executable');
              const script=remote.slice(0,-end.length)+"printf '%s\\n' \"$C994_VERSION\" \"$C994_OPERATION\" \"$C994_PROJECT\" \"$C994_PROJECT_ID\" \"$C994_DRY_RUN\" \"$C994_RETIRED_AT\"";
              const child=cp.spawnSync('bash',['-c',script],{encoding:'utf8',timeout:5000});
              if(child.status!==0)process.exit(2);
              fs.writeFileSync(root+'/host-values.json',JSON.stringify(child.stdout.trimEnd().split('\n')));
            }
            if(kind==='scp'&&args.includes('-r')) {
              if(process.env.C994_TEST_COPY_FAILURE==='1')process.exit(37);
              fs.cpSync(process.env.C994_TEST_HOST_EVIDENCE,args.at(-1)+'/retire-temp-containers',{recursive:true});
            }
            """;
        foreach (var name in new[] { "ssh", "scp" }) {
            var file = root + "/bin/" + name;
            File.WriteAllText(file, shim);
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        foreach (var offset in new[] { false, true }) {
            var destination = root + (offset ? "/offset" : "/utc");
            Directory.CreateDirectory(destination + "/retire-temp-containers");
            // A failed copy must remain red even when an earlier valid local reply exists.
            if (copyFailure) File.Copy(hostEvidence + "/c590-result.json", destination + "/retire-temp-containers/c590-result.json", true);
            var context = new JsonObject {
                ["version"] = 1, ["operationId"] = receipt["operationId"]!.DeepClone(),
                ["project"] = receipt["project"]!.DeepClone(), ["projectId"] = receipt["projectId"]!.DeepClone(),
                ["dryRun"] = false, ["retiredAt"] = offset ? "2026-10-03T11:30:00+02:00" : "2026-10-03T09:30:00Z"
            };
            var manifest = new JsonObject { ["evidenceRoot"] = destination,
                ["sourceSha"] = receipt["sourceSha"]!.DeepClone(), ["runId"] = receipt["runId"]!.DeepClone(),
                ["tempContainerCleanup"] = context };
            File.WriteAllText(root + "/manifest.json", manifest.ToJsonString());
            var run = await C994ScriptProcess.Run("pwsh", "-NoProfile", "-Command", $$"""
                $ErrorActionPreference='Stop'
                $env:PATH='{{root}}/bin'+[IO.Path]::PathSeparator+$env:PATH
                $env:C994_TEST_BRIDGE_ROOT='{{root}}'
                $env:C994_TEST_HOST_EVIDENCE='{{hostEvidence}}'
                $env:C994_TEST_COPY_FAILURE='{{(copyFailure ? "1" : "0")}}'
                . '{{DelegateScriptRunner.RepoRoot}}/scripts/c590-command.ps1'
                . '{{DelegateScriptRunner.RepoRoot}}/scripts/c590-real.ps1'
                $raw=Get-Content -Raw '{{root}}/manifest.json'
                $m=$raw|ConvertFrom-Json
                Invoke-C590LiveCase -Case 'retire-temp-containers' -Manifest $m -RawManifest $raw
                """);
            run.Exit.ShouldBe(copyFailure ? 2 : 0, "c994-receipt-copy c994-literal-transport: " + run.Output);
            if (copyFailure) run.Output.ShouldContain("RecycleReceiptUnavailable", customMessage: "c994-receipt-copy");
            var values = JsonNode.Parse(File.ReadAllText(root + "/host-values.json"))!.AsArray().Select(x => x!.GetValue<string>()).ToArray();
            values.ShouldBe(new[] { "1", context["operationId"]!.GetValue<string>(), "antiphon-runner-temp",
                context["projectId"]!.GetValue<string>(), "0", "2026-10-03T09:30:00.0000000+00:00" }, "c994-literal-transport");
            if (!copyFailure) File.ReadAllText(destination + "/retire-temp-containers/temp-containers.json")
                .ShouldBe(File.ReadAllText(hostEvidence + "/temp-containers.json"), "c994-receipt-copy: identical content");
        }
        var calls = File.ReadAllLines(root + "/arguments.jsonl").Select(x => JsonNode.Parse(x)!).ToArray();
        var ssh = calls.Where(x => x["kind"]!.GetValue<string>() == "ssh").ToArray();
        ssh.Length.ShouldBe(4, "c994-literal-transport: exact transport calls");
        foreach (var call in ssh) {
            var args = call["args"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray();
            args.Length.ShouldBe(10, "c994-literal-transport: remote command is one literal argument");
            args[^2].ShouldBe("mc@server2");
        }
    }
}
