using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Unit")]
public sealed class RetiredTempContainerHostTests
{
    private const string Project = "antiphon-runner-temp";
    private static C1008HostFixture Fixture(params string[] services) {
        var f = new C1008HostFixture(main:false); var id='7';
        foreach (var service in services) f.Docker["containers"]!.AsArray().Add(f.Container(id++, Project, service, false));
        // Same-generation containers carry the token bind. Their stack SHA must render that roster.
        if (services.Any(service => service is "session-runner" or "state-init")) AlignSameGeneration(f);
        return f;
    }
    private static void AlignSameGeneration(C1008HostFixture f) {
        var sha = new string('a', 40);
        f.Statuses["server2-temp"]!["buildVersion"] = sha;
        File.WriteAllText(Path.Combine(f.Root, "temp.env"),
            "RUNNER_GROK_STORE_DIR=" + f.Root + "/grok\nSOURCE_REVISION=" + sha + "\n");
    }
    private static void PinReceiptImage(C1008HostFixture f, string image) {
        var path = Path.Combine(f.Root, "server/temp-container-retirement/c99400000000000000000000000000000001.json");
        var receipt = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        receipt["image"] = image;
        File.WriteAllText(path, receipt.ToJsonString());
    }
    private static bool Mutating(string[] a) => a[0] is "rm" or "stop" or "kill" or "create" or "start" or "prune" || a.Contains("down") || a.Take(2).SequenceEqual(new[] { "volume", "rm" });
    private static void NoMutation(C1008HostFixture f, string label) => f.Trace.Any(Mutating).ShouldBeFalse(label);
    private static JsonNode Receipt(C1008HostFixture f) => JsonNode.Parse(File.ReadAllText(Path.Combine(f.Root,"evidence/retire-temp-containers/temp-containers.json")))!;
    private static async Task<(int Exit,string Output)> Cleanup(C1008HostFixture f, string extra="", bool preview=false) => await f.Run("retire-temp-containers", extra, preview);
    private static string[] TempIds(C1008HostFixture f) => JsonNode.Parse(File.ReadAllText(f.StatePath))!["containers"]!.AsArray()
        .Where(c=>c!["Config"]!["Labels"]!["com.docker.compose.project"]?.GetValue<string>()==Project).Select(c=>c!["Id"]!.GetValue<string>()).ToArray();

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C994_Exited_owned_containers_are_removed_without_volumes() {
        foreach (var services in new[] { new[]{"session-runner"}, new[]{"state-init"}, new[]{"session-runner","state-init","state-init"} })
        foreach (var zero in new[]{false,true}) {
            using var f=Fixture(services); if(zero)f.Statuses["server2-temp"]!["runnerSessions"]=0;
            var ids=f.Docker["containers"]!.AsArray().Where(c=>c!["Config"]!["Labels"]!["com.docker.compose.project"]!.GetValue<string>()==Project).Select(c=>c!["Id"]!.GetValue<string>()).ToArray();
            var before=f.Docker["volumes"]!.ToJsonString(); var run=await Cleanup(f);
            run.Exit.ShouldBe(0,"c994-container-only: "+run.Output);
            f.Trace.Where(a=>a[0]=="rm").Select(a=>a.Last()).ShouldBe(ids,"c994-container-only: exact IDs");
            f.Trace.Where(Mutating).All(a=>a.Length==3&&a[0]=="rm"&&a[1]=="--").ShouldBeTrue("c994-container-only: forbidden argv");
            f.ReloadDocker(); f.Docker["volumes"]!.ToJsonString().ShouldBe(before,"c994-container-only: volume generations");
            foreach(var v in f.Docker["volumes"]!.AsObject())File.ReadAllText(v.Value!["Mountpoint"]!.GetValue<string>()+"/sentinel").ShouldContain(":old");
            TempIds(f).ShouldBeEmpty();
        }
    }

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C994_Absent_cleanup_is_idempotent() {
        foreach(var zero in new[]{false,true})foreach(var volumes in new[]{false,true}) {
            using var f=Fixture();if(zero)f.Statuses["server2-temp"]!["runnerSessions"]=0;
            if(!volumes)foreach(var role in new[]{"work","runner-state","runner-tmp","dind-data"})f.Docker["volumes"]!.AsObject().Remove(Project+"_"+role);
            (await Cleanup(f)).Exit.ShouldBe(0,"c994-absence-idempotent");NoMutation(f,"c994-absence-idempotent");
            Receipt(f)["finalCensus"]!.AsArray().ShouldBeEmpty();f.ReloadDocker();
            var retry=await Cleanup(f,"C994_OPERATION=c99400000000000000000000000000000002");
            retry.Exit.ShouldBe(0,"c994-absence-idempotent: "+retry.Output);NoMutation(f,"c994-absence-idempotent retry");
        }
    }

    // Typed predicates are batched so later guards cannot mask a missing conjunct.
    private static async Task StatusVectors(string label,bool main) {
        using var f=Fixture();var valid=f.Statuses[main?"server2":"server2-temp"]!.AsObject();var cases=new JsonArray();var expected=new Dictionary<string,bool>();
        void Add(string key,bool accepted,JsonObject body){expected.Add(key,accepted);cases.Add(new JsonObject{["key"]=key,["body"]=body.DeepClone()});}
        Add("valid",true,valid);
        foreach(var field in main?new[]{"available","dispatchEligible","acceptingNewWork","draining","retiredAt"}:new[]{"available","dispatchEligible","acceptingNewWork","draining","retireWhenIdle","redirectTo","retiredAt","sessions","queuedTasks","runnerSessions"}) {
            var omit=valid.DeepClone().AsObject();omit.Remove(field);Add(field+":omit",false,omit);
            foreach(var (key,value) in new (string,JsonNode?)[]{("null",null),("string",JsonValue.Create("0")),("boolean",JsonValue.Create(true)),("negative",JsonValue.Create(-1)),("fraction",JsonValue.Create(.5)),("positive",JsonValue.Create(1))}) {
                var body=valid.DeepClone().AsObject();body[field]=value?.DeepClone();
                var accepted=field=="runnerSessions"&&key=="null"||field=="retiredAt"&&main&&key=="null"||
                    key=="boolean"&&(main&&field is "available" or "dispatchEligible" or "acceptingNewWork"||!main&&field is "draining" or "retireWhenIdle");
                Add(field+":"+key,accepted,body);
            }
            if(valid[field] is JsonValue v&&v.TryGetValue<bool>(out var boolValue)){var opposite=valid.DeepClone().AsObject();opposite[field]=!boolValue;Add(field+":opposite",false,opposite);}
        }
        if(!main){var zero=valid.DeepClone().AsObject();zero["runnerSessions"]=0;Add("zero",true,zero);
            var offset=valid.DeepClone().AsObject();offset["retiredAt"]="2026-10-03T11:30:00+02:00";Add("offset",true,offset);
            var changed=valid.DeepClone().AsObject();changed["retiredAt"]="2026-10-03T10:30:00Z";Add("changed-stamp",false,changed);
            foreach(var stamp in new[]{"","2026-02-30T00:00:00Z"}){var invalid=valid.DeepClone().AsObject();invalid["retiredAt"]=stamp;Add("invalid-stamp-"+stamp,false,invalid);}
            var hold=valid.DeepClone().AsObject();hold["retireWhenIdle"]=false;hold["retiredAt"]=null;Add("failed-deploy-hold",false,hold);}
        File.WriteAllText(f.Root+"/status-vectors.json",cases.ToJsonString());
        var extra=$$"""
            LANE=host
            original_status() { node -e 'process.stdout.write(JSON.stringify(JSON.parse(require("fs").readFileSync(process.argv[1]))[process.argv[2]]))' '{{f.Root}}/statuses.json' "$1"; }
            while IFS= read -r vector; do
                key="$(printf '%s' "$vector" | jq -r .key)"
                (
                    c849_status_body() { if [ "$1" = '{{(main?"server2":"server2-temp")}}' ]; then printf '%s' "$vector" | jq -c .body; else original_status "$1"; fi; }
                    c994_refuse() { exit 2; }
                    c994_status_proof
                ) >/dev/null 2>&1 && code=0 || code=$?
                printf 'STATUS_CASE %s %s\n' "$key" "$code"
            done < <(jq -c '.[]' '{{f.Root}}/status-vectors.json')
            write_result true '' 0
            """;
        var run=await Cleanup(f,extra);run.Exit.ShouldBe(0,label+": child; "+run.Output);
        var actual=run.Output.Split('\n').Where(x=>x.StartsWith("STATUS_CASE ")).Select(x=>x.Split(' ')).ToDictionary(x=>x[1],x=>x[2]=="0");
        actual.Keys.Order().ShouldBe(expected.Keys.Order(),label+": roster");foreach(var (key,value) in expected)actual[key].ShouldBe(value,label+": "+key);
        NoMutation(f,label);
    }
    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public Task C994_Host_retirement_proof_is_typed()=>StatusVectors("c994-host-retirement",false);
    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public Task C994_Host_main_admission_is_required()=>StatusVectors("c994-host-main",true);

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C994_Host_task_and_land_census_is_complete() {
        using(var batch=Fixture()) {
            var vectors=C994TaskVectors.Build(batch.Vectors["emptyTasks"]!.AsObject());File.WriteAllText(batch.Root+"/task-vectors.json",vectors.ToJsonString());
            var proof=await Cleanup(batch,"""
                LANE=host;C1008_RUNNER=server2-temp
                while IFS= read -r vector; do
                    input="$(printf '%s' "$vector"|jq -c .input)"
                    (
                        c1008_http() {
                            local key="$1" kind
                            if [[ "$key" == *projectId=* ]]; then
                                if [[ "$key" == *'&landPending=true' ]]; then kind=land
                                elif [[ "$key" == *'&status=Queued,Dispatched,Working,Blocked' ]]; then kind=open
                                else return 2; fi
                                key="${key#*projectId=}";key="${key%%&*}"
                                C1008_HTTP_BODY="$(printf '%s' "$input"|jq -ec --arg key "$key" --arg kind "$kind" '.scopes[$key][$kind]')" || return 2
                            else C1008_HTTP_BODY="$(printf '%s' "$input"|jq -ec --arg key "${key##*/}" '.details[$key]')" || return 2; fi
                        }
                        c1008_tasks >/dev/null
                    ) && code=0 || code=$?
                    printf 'TASK_CASE %s %s\n' "$(printf '%s' "$vector"|jq -r .key)" "$code"
                done < <(jq -c '.[]' "$C1008_FIXTURE_ROOT/task-vectors.json")
                write_result true '' 0
                """);
            proof.Exit.ShouldBe(0,"c994-host-work: batched real census; "+proof.Output);
            var actual=proof.Output.Split('\n').Where(x=>x.StartsWith("TASK_CASE ")).Select(x=>x.Split(' ')).ToDictionary(x=>x[1],x=>x[2]=="0");
            actual.Count.ShouldBe(vectors.Count,"c994-host-work: complete matrix");foreach(var v in vectors)actual[v!["key"]!.GetValue<string>()].ShouldBe(v["accepted"]!.GetValue<bool>(),"c994-host-work: "+v["key"]);
            NoMutation(batch,"c994-host-work");
        }
        foreach(var status in new[]{"Queued","Dispatched","Working","Blocked","Failed","Canceled","Succeeded"}) {
            using var f=Fixture("session-runner");
            // CARD-1087: a terminal Failed or Canceled row is not bound work. Succeeded stays bound here because landRequestedAt is set.
            var terminal=status is "Failed" or "Canceled";
            f.TaskScopes.Select(x=>x.Value).Single()!["items"]!.AsArray().Add(new JsonObject { ["id"]="22222222-2222-2222-2222-222222222222",["status"]=status,
                ["runnerId"]="server2-temp",["projectId"]="aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1",["scopeSource"]="Task",
                ["landRequestedAt"]=status=="Succeeded"?"2026-10-04T00:00:00Z":null,["landStartedAt"]=null });
            var run=await Cleanup(f);
            if(terminal) run.Exit.ShouldBe(0,"c994-host-work: "+status+" is not bound; "+run.Output);
            else { run.Exit.ShouldBe(2,"c994-host-work: "+status); NoMutation(f,"c994-host-work"); }
        }
        using var unknown=Fixture("session-runner"); unknown.TaskScopes.Clear();
        (await Cleanup(unknown)).Exit.ShouldBe(2,"c994-host-work: unknown");NoMutation(unknown,"c994-host-work");
    }

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C994_Census_and_inspect_fail_closed() {
        foreach(var fault in new[]{"ps-error","inspect-error","inspect-empty","inspect-malformed","c994-ps-partial","c994-duplicate-id","c994-wrong-id","c994-missing-state",
            "c994-ps-empty-error","c994-truncated-id","c994-nonhex-id","c994-inspect-multirow","c994-missing-image","c994-missing-mounts","c994-missing-labels"}){
            using var f=Fixture("session-runner");f.Docker["fault"]=fault;var run=await Cleanup(f);
            run.Exit.ShouldBe(2,"c994-census-known: "+fault+"; "+run.Output);NoMutation(f,"c994-census-known: "+fault);
        }
    }
    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C994_Container_service_and_state_are_restricted() {
        foreach(var service in new[]{"session-runner","state-init","build-slots","foreign"})foreach(var state in new[]{"running","paused","restarting","created","dead","exited","running-false","exited-true"}) {
            using var f=Fixture(service);var c=f.Docker["containers"]!.AsArray().Last()!;c["State"]!["Status"]=state=="running-false"?"running":state=="exited-true"?"exited":state;c["State"]!["Running"]=state is "running" or "exited-true";
            var good=service is "session-runner" or "state-init"&&state=="exited";var run=await Cleanup(f);
            run.Exit.ShouldBe(good?0:2,"c994-service c994-state: "+service+":"+state+"; "+run.Output);if(!good)NoMutation(f,"c994-state");
        }
        using var duplicate=Fixture("session-runner","session-runner");(await Cleanup(duplicate)).Exit.ShouldBe(2,"c994-cardinality");NoMutation(duplicate,"c994-cardinality");
        using var neighbour=Fixture("session-runner");neighbour.Docker["containers"]!.AsArray().Last()!["Config"]!["Labels"]!["com.docker.compose.project"]=Project+"-foreign";
        (await Cleanup(neighbour)).Exit.ShouldBe(0,"c994-project");NoMutation(neighbour,"c994-project");
    }
    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C994_Production_mount_topology_is_proven() {
        foreach(var fault in new[]{"valid","bind","tmpfs","private","compose","token-missing","token-source","token-writable"}) {
            using var f=Fixture("session-runner","state-init");var runner=f.Docker["containers"]!.AsArray()[1]!;
            var tokenMount=runner["Mounts"]!.AsArray().Single(m=>m!["Destination"]!.GetValue<string>()=="/run/antiphon/github-token")!;
            if(fault=="token-missing")runner["Mounts"]!.AsArray().Remove(tokenMount);
            if(fault=="token-source")tokenMount["Source"]="/foreign";
            if(fault=="token-writable")tokenMount["RW"]=true;
            if(fault=="bind")runner["Mounts"]!.AsArray().Single(m=>m!["Destination"]!.GetValue<string>()=="/state/codex")!["Source"]="/foreign";
            if(fault=="tmpfs")runner["HostConfig"]=new JsonObject();
            if(fault=="private")f.Docker["volumes"]![Project+"_work"]!["Labels"]!["com.docker.compose.project"]="foreign";
            if(fault=="compose")f.Docker["models"]![Project]!["services"]!["state-init"]!["volumes"]![1]!["target"]="/state";
            var run=await Cleanup(f);run.Exit.ShouldBe(fault=="valid"?0:2,"c994-mounts c994-private-identity c994-compose: "+fault+"; "+run.Output);
            if(fault!="valid")NoMutation(f,"c994-mounts");
            else {
                var candidates=Receipt(f)["candidates"]!.AsArray();
                candidates.Select(c=>c!["Topology"]!["mounts"]!.AsArray().Count).ShouldBe(new[]{15,3},"c994-mounts: logical roster including the token bind");
                var token=candidates[0]!["Topology"]!["mounts"]!.AsArray().Single(m=>m!["target"]!.GetValue<string>()=="/run/antiphon/github-token")!;
                token["kind"]!.GetValue<string>().ShouldBe("bind","c994-mounts: token directory bind");
                token["source"]!.GetValue<string>().ShouldBe(f.Root+"/github-token","c994-mounts: owned token directory");
                token["rw"]!.GetValue<bool>().ShouldBeFalse("c994-mounts: token bind is read-only");
                token["file"]!.GetValue<bool>().ShouldBeFalse("c994-mounts: token directory supports atomic rotation");
            }
        }
    }
    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C994_Whole_candidate_set_precedes_removal() {
        foreach(var reverse in new[]{false,true}) {using var f=Fixture("session-runner","state-init");
            f.Docker["containers"]!.AsArray().Last()!["Mounts"]![0]!["Source"]="/foreign";
            if(reverse){var array=f.Docker["containers"]!.AsArray();var copy=array.Reverse().Select(c=>c!.DeepClone()).ToArray();array.Clear();foreach(var c in copy)array.Add(c);}
            (await Cleanup(f)).Exit.ShouldBe(2,"c994-whole-set");NoMutation(f,"c994-whole-set");}
    }
    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C994_Rollout_lock_covers_final_census() {
        using var first=Fixture("session-runner");using var second=Fixture("session-runner");
        var finalBarrier=first.Root+"/final-barrier";var release=first.Root+"/final-release";
        var firstExtra=$$"""
            original_save=$(declare -f c994_save); eval "${original_save/c994_save/c994_save_original}"
            c849_lock() { [ "$C1008_LOCK_HELD" = 1 ] || exit 2; printf 'C994_LOCK_ORDER rollout-cache\n'; }
            c994_save() {
                if printf '%s' "$C994_RECORD" | jq -e '.outcome=="completed"' >/dev/null; then
                    touch '{{finalBarrier}}'
                    while [ ! -f '{{release}}' ]; do sleep 0.02; done
                fi
                c994_save_original
            }
            """;
        var firstRun=Cleanup(first,firstExtra);
        Task<(int Exit,string Output)>? secondRun=null;
        try {
            var deadline=DateTime.UtcNow.AddSeconds(15);
            while(!File.Exists(finalBarrier)&&!firstRun.IsCompleted&&DateTime.UtcNow<deadline)await Task.Delay(20);
            File.Exists(finalBarrier).ShouldBeTrue("c994-lock: first final-census persistence barrier");
            var requested=second.Root+"/lock-request";
            secondRun=Cleanup(second,$$"""
                SERVER2_ROOT='{{first.Root}}/server'
                C994_OPERATION=c99400000000000000000000000000000002
                flock() { touch '{{requested}}'; command flock "$@"; }
                """);
            deadline=DateTime.UtcNow.AddSeconds(10);
            while(!File.Exists(requested)&&!secondRun.IsCompleted&&DateTime.UtcNow<deadline)await Task.Delay(20);
            File.Exists(requested).ShouldBeTrue("c994-lock: second entrant requested the real flock");
            second.Trace.ShouldBeEmpty("c994-lock: second cannot census before first receipt completes");
        } finally {
            File.WriteAllText(release,"release");
            var firstResult=await firstRun;firstResult.Exit.ShouldBe(0,"c994-lock c994-lock-order: "+firstResult.Output);
            firstResult.Output.ShouldContain("C994_LOCK_ORDER rollout-cache");
            if(secondRun is not null){var secondResult=await secondRun;secondResult.Exit.ShouldBe(0,"c994-lock: second after release; "+secondResult.Output);}
        }
    }
    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C994_Each_removal_rechecks_live_proofs() {
        foreach(var fault in new[]{"c994-late-main","c994-late-stamp","c994-late-land","c994-late-image","c994-late-mount","c994-late-state","c994-late-id","c994-late-task"}){
            using var f=Fixture("session-runner","state-init");f.Docker["fault"]=fault;var run=await Cleanup(f);
            run.Exit.ShouldBe(2,"c994-recheck-status c994-recheck-work c994-recheck-container: "+fault+"; "+run.Output);
            f.Trace.Count(a=>a[0]=="rm").ShouldBe(1,"c994-recheck-container: prior progress only");
        }
        foreach(var fault in new[]{"main","stamp","land","task","id","state","image","mount"}) {
            using var f=Fixture("session-runner","state-init");
            var extra=$$"""
                original_save=$(declare -f c994_save); eval "${original_save/c994_save/c994_save_original}"
                c994_save() {
                    c994_save_original || return $?
                    if [ ! -f "$C1008_FIXTURE_ROOT/drift-injected" ] && printf '%s' "$C994_RECORD" | jq -e '.outcome=="pending" and (.candidates|length)>0 and (.removals|length)==0' >/dev/null; then
                        touch "$C1008_FIXTURE_ROOT/drift-injected"
                        node - "$C1008_FIXTURE_ROOT" '{{fault}}' <<'DRIFT'
                const fs=require('fs'),root=process.argv[2],fault=process.argv[3];
                const read=name=>JSON.parse(fs.readFileSync(root+'/'+name+'.json'));
                const save=(name,body)=>fs.writeFileSync(root+'/'+name+'.json',JSON.stringify(body));
                if(fault==='main'||fault==='stamp') {
                  const s=read('statuses');if(fault==='main')s.server2.acceptingNewWork=false;else s['server2-temp'].retiredAt='2026-10-03T10:30:00Z';save('statuses',s);
                } else if(fault==='land'||fault==='task') {
                  const t=read('tasks'),row=t.scopes[Object.keys(t.scopes)[0]];
                  row.items.push({id:'22222222-2222-2222-2222-222222222222',status:fault==='task'?'Working':'Succeeded',runnerId:'server2-temp',projectId:'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1',scopeSource:'Task',landRequestedAt:fault==='land'?'2026-10-04T00:00:00Z':null,landStartedAt:null});save('tasks',t);
                } else {
                  const d=read('docker'),c=d.containers.find(c=>c.Config.Labels['com.docker.compose.project']==='antiphon-runner-temp');
                  if(fault==='id')c.Id='e'.repeat(64);if(fault==='state')c.State={Running:true,Status:'running'};
                  if(fault==='image')c.Image='sha256:'+'b'.repeat(64);if(fault==='mount')c.Mounts[0].Source='/foreign';save('docker',d);
                }
                DRIFT
                    fi
                }
                """;
            var run=await Cleanup(f,extra);
            run.Exit.ShouldBe(2,"c994-recheck-status c994-recheck-work c994-recheck-container: before-first "+fault+"; "+run.Output);
            File.Exists(f.Root+"/drift-injected").ShouldBeTrue("c994-recheck-container: reached admitted-candidate barrier");
            NoMutation(f,"c994-recheck-container: before-first "+fault);
        }
    }
    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C994_Final_absence_is_observed() {
        foreach(var fault in new[]{"c994-rm-retains","c994-id-absence-error","c994-final-replacement","final-ps-error","c994-final-ps-partial"}) {using var f=Fixture("session-runner");f.Docker["fault"]=fault;
            var run=await Cleanup(f);run.Exit.ShouldBe(2,"c994-id-absent c994-project-absent: "+fault+"; "+run.Output);Receipt(f)["outcome"]!.GetValue<string>().ShouldNotBe("completed");}
    }
    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C994_Removal_failure_and_crash_retry_preserve_progress() {
        foreach(var nth in new[]{1,2}) {using var f=Fixture("session-runner","state-init");f.Docker["fault"]="c994-rm-"+nth;
            var run=await Cleanup(f);run.Exit.ShouldBe(2,"c994-rm-failure: "+run.Output);TempIds(f).Length.ShouldBe(3-nth,"c994-rm-failure: progress");
            f.ReloadDocker();f.Docker["fault"]="";var remaining=TempIds(f);
            var retry=await Cleanup(f,"C994_OPERATION=c99400000000000000000000000000000002");retry.Exit.ShouldBe(0,"c994-retry: "+retry.Output);TempIds(f).ShouldBeEmpty();
            Receipt(f)["removals"]!.AsArray().Select(x=>x!["id"]!.GetValue<string>()).ShouldBe(remaining,"c994-retry: remaining only");f.Removed.ShouldBeEmpty();}
        foreach(var boundary in new[]{"before-intent","after-intent","after-rm","before-final-copy"}) {
            using var f=Fixture("session-runner","state-init");var before=f.Docker["volumes"]!.ToJsonString();
            var crash=$$"""
                original_save=$(declare -f c994_save); eval "${original_save/c994_save/c994_save_original}"
                c994_save() {
                    if [ '{{boundary}}' = before-intent ] && printf '%s' "$C994_RECORD" | jq -e '.removals|length==1' >/dev/null; then exit 77; fi
                    if [ '{{boundary}}' = after-rm ] && printf '%s' "$C994_RECORD" | jq -e 'any(.removals[]; .outcome=="removed")' >/dev/null; then exit 77; fi
                    if [ '{{boundary}}' = before-final-copy ] && printf '%s' "$C994_RECORD" | jq -e '.outcome=="completed"' >/dev/null; then exit 77; fi
                    c994_save_original || return $?
                    if [ '{{boundary}}' = after-intent ] && printf '%s' "$C994_RECORD" | jq -e '.removals|length==1' >/dev/null; then exit 77; fi
                }
                """;
            var interrupted=await Cleanup(f,crash);interrupted.Exit.ShouldBe(77,"c994-retry crash: "+boundary+"; "+interrupted.Output);
            var durable=Receipt(f);durable["outcome"]!.GetValue<string>().ShouldBe("pending","c994-retry: no premature success");
            durable["removals"]!.AsArray().Count.ShouldBe(boundary=="before-intent"?0:boundary=="before-final-copy"?2:1,"c994-retry: atomic persistence");
            f.ReloadDocker();var remaining=TempIds(f);
            var statuses=JsonNode.Parse(File.ReadAllText(f.Root+"/statuses.json"))!.AsObject();f.Statuses.Clear();foreach(var (key,value) in statuses)f.Statuses[key]=value?.DeepClone();
            f.Statuses["server2"]!["acceptingNewWork"]=false;
            var drift=await Cleanup(f,"C994_OPERATION=c99400000000000000000000000000000002");drift.Exit.ShouldBe(2,"c994-retry: fresh status required");TempIds(f).ShouldBe(remaining);
            f.ReloadDocker();f.Statuses["server2"]!["acceptingNewWork"]=true;
            var retry=await Cleanup(f,"C994_OPERATION=c99400000000000000000000000000000003");retry.Exit.ShouldBe(0,"c994-retry: "+boundary+"; "+retry.Output);
            Receipt(f)["removals"]!.AsArray().Select(x=>x!["id"]!.GetValue<string>()).ShouldBe(remaining,"c994-retry: freshly remaining only");
            TempIds(f).ShouldBeEmpty();f.ReloadDocker();f.Docker["volumes"]!.ToJsonString().ShouldBe(before,"c994-retry: volumes retained");
            retry.Output.Split("C994_TEMP_CONTAINERS").Length.ShouldBe(2,"c994-retry: one summary");
        }
    }
    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C994_Receipt_write_and_copy_fail_closed() {
        foreach(var fault in new[]{"intent","outcome","copy"}) {using var f=Fixture("session-runner","state-init");
            var extra=fault=="intent"?"c994_save() { return 2; }":fault=="copy"?"cp() { return 2; }":"original_save=$(declare -f c994_save); eval \"${original_save/c994_save/c994_save_original}\"; c994_save() { if printf '%s' \"$C994_RECORD\" | jq -e 'any(.removals[]; .outcome==\"removed\")' >/dev/null; then return 2; fi; c994_save_original; }";
            var run=await Cleanup(f,extra);run.Exit.ShouldBe(2,"c994-intent-first c994-receipt-copy: "+fault+"; "+run.Output);
            if(fault!="outcome")NoMutation(f,"c994-intent-first");else TempIds(f).Length.ShouldBe(1,"c994-receipt-copy: exact progress");}
        using var bridge=Fixture("session-runner");(await Cleanup(bridge)).Exit.ShouldBe(0,"c994-receipt-copy: real host evidence");
        await RetiredTempContainerScriptTests.CheckLiveBridge(bridge,true);
    }
    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C994_Host_context_is_strict() {
        foreach(var extra in new[]{"C994_OPERATION=''","C994_VERSION=2","C994_PROJECT=foreign","C994_PROJECT_ID=bad","C994_DRY_RUN=true","C994_RETIRED_AT=garbage","detect_lane() { LANE=nested; }"}) {
            using var f=Fixture("session-runner");var run=await Cleanup(f,extra);run.Exit.ShouldBe(2,"c994-host-context c994-host-lane: "+extra+"; "+run.Output);NoMutation(f,"c994-host-context");}
    }
    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C994_Host_preview_never_mutates() {
        foreach(var present in new[]{false,true})foreach(var zero in new[]{false,true}) {using var f=present?Fixture("session-runner","state-init"):Fixture();if(zero)f.Statuses["server2-temp"]!["runnerSessions"]=0;
            var before=f.Docker.ToJsonString();var run=await Cleanup(f,preview:true);run.Exit.ShouldBe(0,"c994-host-preview: "+run.Output);NoMutation(f,"c994-host-preview");f.ReloadDocker();f.Docker.ToJsonString().ShouldBe(before);
            Receipt(f)["outcome"]!.GetValue<string>().ShouldBe("preview");run.Output.ShouldContain("C994_PREVIEW volume="+Project+"_work");}
        using var drift=Fixture("session-runner");(await Cleanup(drift,preview:true)).Exit.ShouldBe(0);
        drift.ReloadDocker();drift.Statuses["server2"]!["acceptingNewWork"]=false;
        (await Cleanup(drift,"C994_OPERATION=c99400000000000000000000000000000002")).Exit.ShouldBe(2,"c994-host-preview: apply rechecks changed facts");
        NoMutation(drift,"c994-host-preview: no inherited preview authority");
    }
    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C994_Image_receipt_lookup_is_bound() {
        using var f=Fixture("session-runner");(await Cleanup(f)).Exit.ShouldBe(0,"c994-image-binding");var image=Receipt(f)["image"]!.GetValue<string>();
        f.ReloadDocker();var retry=await Cleanup(f,"C994_OPERATION=c99400000000000000000000000000000002");retry.Exit.ShouldBe(0,"c994-image-retention: "+retry.Output);
        Receipt(f)["image"]!.GetValue<string>().ShouldBe(image,"c994-image-retention");Receipt(f)["originalCleanupOperationId"]!.GetValue<string>().ShouldBe("c99400000000000000000000000000000001");
        File.WriteAllText(f.Root+"/lookup-seed.json",Receipt(f).ToJsonString());
        var lookup=await Cleanup(f,"""
            LANE=host
            C994_STAMP="$(date -u -d "$C994_RETIRED_AT" +%Y-%m-%dT%H:%M:%S.%NZ)"
            for variant in matching retained fallback corrupt symlink traversal foreign-operation foreign-source foreign-project foreign-projectid foreign-stamp conflicting unrelated-newest; do
                (
                    SERVER2_ROOT="$C1008_FIXTURE_ROOT/lookup-$variant"
                    root="$SERVER2_ROOT/temp-container-retirement";mkdir -p "$root"
                    op=c99400000000000000000000000000000002
                    file="$root/$op.json";cp "$C1008_FIXTURE_ROOT/lookup-seed.json" "$file"
                    case "$variant" in
                      matching|retained) ;;
                      fallback) rm -- "$file";op='' ;;
                      corrupt) printf '{' > "$file" ;;
                      symlink) mv "$file" "$SERVER2_ROOT/saved.json";ln -s "$SERVER2_ROOT/saved.json" "$file" ;;
                      traversal) op='../lookup-seed' ;;
                      foreign-operation) jq '.operationId="c99400000000000000000000000000000009"' "$file" > "$file.new";mv "$file.new" "$file" ;;
                      foreign-source) jq '.sourceSha="bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"' "$file" > "$file.new";mv "$file.new" "$file" ;;
                      foreign-project) jq '.project="antiphon-runner"' "$file" > "$file.new";mv "$file.new" "$file" ;;
                      foreign-projectid) jq '.projectId="bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"' "$file" > "$file.new";mv "$file.new" "$file" ;;
                      foreign-stamp) jq '.retiredAt="2026-10-04T09:30:00.000000000Z"' "$file" > "$file.new";mv "$file.new" "$file" ;;
                      conflicting|unrelated-newest)
                        jq --arg variant "$variant" '.operationId="c99400000000000000000000000000000009" | .image="sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" | del(.originalCleanupOperationId) | if $variant=="unrelated-newest" then .retiredAt="2026-10-04T09:30:00.000000000Z" else . end' "$file" > "$root/c99400000000000000000000000000000009.json"
                        op='' ;;
                    esac
                    code=0;c994_lookup_image "$op" || code=$?
                    case "$variant" in
                      matching|retained|unrelated-newest)
                        [ "$code" = 0 ] && [ "$C994_IMAGE" = sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa ] && [ "$C994_ORIGINAL" = c99400000000000000000000000000000001 ] || exit 3 ;;
                      fallback) [ "$code" = 0 ] && [ -z "$C994_IMAGE" ] || exit 3 ;;
                      *) [ "$code" = 2 ] || exit 3 ;;
                    esac
                    printf 'LOOKUP_CASE %s\n' "$variant"
                ) || exit 2
            done
            write_result true '' 0
            """);
        lookup.Exit.ShouldBe(0,"c994-image-binding c994-image-retention: "+lookup.Output);
        lookup.Output.Split('\n').Where(x=>x.StartsWith("LOOKUP_CASE ")).Select(x=>x[12..]).ShouldBe(new[]{"matching","retained","fallback","corrupt","symlink","traversal","foreign-operation","foreign-source","foreign-project","foreign-projectid","foreign-stamp","conflicting","unrelated-newest"},"c994-image-binding: exact receipt roster");
        f.ReloadDocker();var file=f.Root+"/server/temp-container-retirement/c99400000000000000000000000000000001.json";var record=JsonNode.Parse(File.ReadAllText(file))!;record["sourceSha"]=new string('b',40);File.WriteAllText(file,record.ToJsonString());
        var foreign=await f.Run("retire-temp-runner","C1008_CLEANUP_OPERATION=c99400000000000000000000000000000001");foreign.Exit.ShouldBe(2,"c994-image-binding: foreign source");f.Removed.ShouldBeEmpty();
    }
    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C994_Audit_image_is_pinned_and_required() {
        foreach(var fault in new[]{"image-inspect-error","image-inspect-wrong","original-image-missing"}) {using var f=Fixture("session-runner");(await Cleanup(f)).Exit.ShouldBe(0);f.ReloadDocker();if(fault=="original-image-missing")PinReceiptImage(f,"sha256:"+new string('b',64));f.Docker["fault"]=fault;
            var run=await f.Run("retire-temp-runner","C1008_CLEANUP_OPERATION=c99400000000000000000000000000000001");run.Exit.ShouldBe(2,"c994-image-required: "+run.Output);run.Output.ShouldContain("RecycleGitAuditUnknown");f.Removed.ShouldBeEmpty("c994-audit-required");}
        foreach(var work in new[]{"published","dirty","unpublished"}) {
            using var f=Fixture("session-runner");
            var setup=await C994ScriptProcess.Run("bash","-c",$$"""
                set -euo pipefail
                git init -q --bare '{{f.Root}}/origin'
                git init -q -b main '{{f.Root}}/work/repo'
                git -C '{{f.Root}}/work/repo' config user.name Fixture
                git -C '{{f.Root}}/work/repo' config user.email fixture@example.invalid
                printf published > '{{f.Root}}/work/repo/sentinel'
                git -C '{{f.Root}}/work/repo' add sentinel
                git -C '{{f.Root}}/work/repo' commit -qm published
                git -C '{{f.Root}}/work/repo' remote add origin '{{f.Root}}/origin'
                git -C '{{f.Root}}/work/repo' push -qu origin main
                """);setup.Exit.ShouldBe(0,"c994-audit-required: real Git setup; "+setup.Output);
            (await Cleanup(f)).Exit.ShouldBe(0,"c994-image-required: preserve original digest");PinReceiptImage(f,"sha256:"+new string('b',64));f.ReloadDocker();
            if(work=="dirty")File.WriteAllText(f.Root+"/work/repo/dirty","unpublished-sentinel");
            if(work=="unpublished") {
                File.WriteAllText(f.Root+"/work/repo/local","unpublished-sentinel");
                var commit=await C994ScriptProcess.Run("git","-C",f.Root+"/work/repo","add","local");commit.Exit.ShouldBe(0);
                commit=await C994ScriptProcess.Run("git","-C",f.Root+"/work/repo","commit","-qm","unpublished");commit.Exit.ShouldBe(0);
            }
            var before=f.Docker["volumes"]!.ToJsonString();
            var run=await f.Run("retire-temp-runner","C1008_CLEANUP_OPERATION=c99400000000000000000000000000000001");
            run.Exit.ShouldBe(work=="published"?0:2,"c994-image-required c994-audit-required: "+work+"; "+run.Output);
            f.Trace.Where(a=>a[0]=="create").ShouldNotBeEmpty("c994-image-required: offline audit reached");
            f.Trace.Where(a=>a[0]=="create").All(a=>a.Contains("sha256:"+new string('b',64))&&a.Contains("1654:1654")&&a.Any(x=>x.EndsWith(",readonly"))).ShouldBeTrue("c994-image-required: exact pinned readonly uid-1654 helper");
            if(work!="published") {
                run.Output.ShouldContain(work=="dirty"?"RecycleWorktreeDirty":"RecycleUnpublishedWork",customMessage:"c994-audit-required");
                f.Removed.ShouldBeEmpty("c994-audit-required");f.ReloadDocker();f.Docker["volumes"]!.ToJsonString().ShouldBe(before,"c994-audit-required: generations retained");
                File.ReadAllText(f.Root+"/work/repo/"+(work=="dirty"?"dirty":"local")).ShouldBe("unpublished-sentinel","c994-audit-required: work retained");
            }
        }
    }
    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C994_Cleanup_receipts_redact_unapproved_fields() {
        foreach(var refuse in new[]{false,true}) {
            using var f=Fixture("session-runner");var c=f.Docker["containers"]!.AsArray().Last()!;
            c["Config"]!["Env"]=new JsonArray("C994_SECRET_SENTINEL");c["Config"]!["Labels"]!["unapproved"]="C994_SECRET_SENTINEL";
            File.WriteAllText(f.Root+"/codex/auth.json","C994_SECRET_SENTINEL");File.WriteAllText(f.Root+"/grok/auth.json","C994_SECRET_SENTINEL");
            f.Docker["dockerStderr"]="C994_SECRET_SENTINEL";if(refuse)c["State"]!["Status"]="created";
            var run=await Cleanup(f);run.Exit.ShouldBe(refuse?2:0,"c994-redaction: "+run.Output);
            foreach(var text in new[]{run.Output,Receipt(f).ToJsonString(),File.ReadAllText(f.Root+"/server/temp-container-retirement/c99400000000000000000000000000000001.json")})text.ShouldNotContain("C994_SECRET_SENTINEL",customMessage:"c994-redaction: success/refusal/stdout/host/copy");
            File.ReadAllText(f.Root+"/codex/auth.json").ShouldBe("C994_SECRET_SENTINEL","c994-redaction: bind untouched");
            if(!refuse)Receipt(f)["candidates"]![0]!["Id"]!.GetValue<string>().ShouldBe(new string('7',64),"c994-redaction: approved identity retained");
        }
    }
    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C994_Fixture_custody_is_bounded() {
        var script = """
            import fs from 'node:fs';import os from 'node:os';
            import {ownedRoot,ownedObject,ownedDaemon,boundedChild} from './scripts/fixtures/c994-real-custody.mjs';
            const root=fs.mkdtempSync('/tmp/c994-real-');const ledger={schema:1,prefix:'c994rd'+'a'.repeat(16)};
            fs.writeFileSync(root+'/ownership.json',JSON.stringify(ledger));
            const cases=[];const refuses=(key,call)=>{let refused=false;try{call();}catch{refused=true;}if(!refused)throw Error(key);cases.push(key);};
            try {
              ownedRoot(root);ownedObject(ledger,{Name:ledger.prefix+'-runner',Config:{Labels:{'io.antiphon.c994-real':ledger.prefix}}});
              refuses('production',()=>ownedObject(ledger,{Name:'antiphon-runner',Config:{Labels:{'io.antiphon.c994-real':ledger.prefix}}}));
              refuses('foreign-label',()=>ownedObject(ledger,{Name:ledger.prefix+'-runner',Config:{Labels:{'io.antiphon.c994-real':'foreign'}}}));
              const link=root+'-link';fs.symlinkSync(root,link);try{refuses('linked-root',()=>ownedRoot(link));}finally{fs.unlinkSync(link);}
              refuses('sibling-daemon',()=>ownedDaemon('foreign',os.hostname(),true));
              let error;try{await boundedChild('bash',['-c','sleep 100 & wait'],{},100);}catch(e){error=e;}
              let returnedReaped=error?.reaped===true,returnedDead=false;
              try{process.kill(error.pid,0);}catch(e){if(e.code==='ESRCH')returnedDead=true;else throw e;}
              // Custody-independent teardown prevents a deliberate helper defect leaking its owned child.
              if(!returnedDead&&Number.isInteger(error?.pid)){try{process.kill(-error.pid,'SIGKILL');}catch(e){if(e.code!=='ESRCH')throw e;}await new Promise(r=>setTimeout(r,100));}
              if(!returnedReaped||!returnedDead)throw Error('child-not-reaped-at-return');
              cases.push('child-reaped');
              if(cases.sort().join(',')!=='child-reaped,foreign-label,linked-root,production,sibling-daemon')throw Error('roster');
              console.log('C994_CUSTODY cases=5 reaped=true');
            } finally {fs.rmSync(root,{recursive:true});}
            """;
        var run=await C994ScriptProcess.Run("node","--input-type=module","-e",script);
        run.Exit.ShouldBe(0,"c994-fixture-ownership c994-child-reaped: "+run.Output);
        run.Output.ShouldContain("C994_CUSTODY cases=5 reaped=true");
    }

    // CARD-1105 V-6. Temp cleanup proves the containers' own generation, not the target roster.
    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1105_Temp_cleanup_uses_previous_generation()
    {
        C1008HostFixture.RequireNativeLinux();
        using (var accepted = new C1008HostFixture(main: false))
        {
            accepted.Docker["containers"]!.AsArray().Add(accepted.Container('7', Project, "session-runner", false, previousGeneration: true));
            accepted.Docker["containers"]!.AsArray().Add(accepted.Container('8', Project, "state-init", false, previousGeneration: true));
            var run = await Cleanup(accepted);
            run.Exit.ShouldBe(0, "c1105-temp-generation: " + run.Output);
            var mounts = Receipt(accepted)["candidates"]!.AsArray().Single(candidate =>
                candidate!["Config"]!["Labels"]!["com.docker.compose.service"]!.GetValue<string>() == "session-runner")!
                ["Topology"]!["mounts"]!.AsArray();
            mounts.Count.ShouldBe(14, "c1105-temp-generation: previous temp roster has no token bind");
            mounts.Any(mount => mount!["target"]!.GetValue<string>() == "/run/antiphon/github-token").ShouldBeFalse();
        }
        using var refused = new C1008HostFixture(main: false);
        refused.Docker["containers"]!.AsArray().Add(refused.Container('7', Project, "session-runner", false, previousGeneration: true));
        refused.Docker["containers"]!.AsArray().Add(refused.Container('8', Project, "state-init", false, previousGeneration: true));
        File.WriteAllText(Path.Combine(refused.Root, "temp.env"),
            "RUNNER_GROK_STORE_DIR=" + refused.Root + "/grok\nSOURCE_REVISION=" + new string('d', 40) + "\n");
        var mismatch = await Cleanup(refused);
        mismatch.Exit.ShouldBe(2, "c1105-temp-generation: a different stack SHA refuses; " + mismatch.Output);
        mismatch.Output.ShouldContain("RecycleGenerationMismatch");
        NoMutation(refused, "c1105-temp-generation");
    }
}
