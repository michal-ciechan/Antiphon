using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class CheckpointAdmissionContractTests : CheckpointTestBase
{
    [Test]
    public async Task C1531_production_parser_reads_legacy_and_current_receipts()
    {
        var observation = CheckpointFixtures.CleanSource();
        var source = new SourceEvidence { Start = observation, End = observation, State = "clean", BuildSource = "verified" };
        var run = TempDir();
        var model = new ReportModel
        {
            RunId = "fixture", Commit = observation.Commit!, Source = source,
            Builds = [new ReportBuild { Id = "bin-fixture", Source = source, State = "built" }],
            Rows = [new ReportRow { Id = "CP-1", State = "green", Source = source, Executed = 1, Passed = 1, Failed = 0, Skipped = 0,
                Line = $"CHECKPOINT CP-1 commit={observation.Commit} source={observation.Commit} dirty=0 sourceState=clean buildSource=verified executed=1 passed=1 failed=0 skipped=0" }],
        };
        ReportWriter.WriteFiles(run, model);
        var report = JsonNode.Parse(File.ReadAllText(Path.Combine(run, "report.json")))!.AsObject();
        var state = JsonSerializer.SerializeToNode(new RunState
        {
            RunId = "fixture", ToolOwner = SafeWrite.Marker, Source = source,
            Builds = [new BuildProgress { Id = "bin-fixture" }], Rows = [new RowProgress { Id = "CP-1" }],
        }, RunStateStore.Json)!.AsObject();
        var failures = new List<string>();
        foreach (var shape in new[] { "null", "absent", "value", "empty" })
        {
            var input = report.DeepClone().AsObject();
            var stateInput = state.DeepClone().AsObject();
            if (shape == "absent")
            {
                RemoveNewFields(input);
                RemoveNewFields(stateInput);
            }
            if (shape is "value" or "empty")
            {
                input["executionSeconds"] = 12.5;
                foreach (var collection in new[] { "builds", "rows" })
                {
                    input[collection]![0]!["executedSeconds"] = 0.5;
                    input[collection]![0]!["brokerReason"] = shape == "empty" ? "" : "enabled";
                    input[collection]![0]!["terminalReason"] = shape == "empty" ? "" : "completed";
                    stateInput[collection]![0]!["phase"] = "executing";
                }
            }
            // Unknown fields have always been ignored; they confer no qualification authority.
            input["futureDiagnostic"] = "ignored";
            try
            {
                var parsed = ReceiptJson.Parse<ReportModel>(input.ToJsonString(), ReportWriter.Json);
                parsed.ExecutionSeconds.ShouldBe(shape is "value" or "empty" ? 12.5 : null);
                parsed.Rows.Single().ExecutedSeconds.ShouldBe(shape is "value" or "empty" ? 0.5 : null);
                parsed.Rows.Single().Executed.ShouldBe(1);
                parsed.Attempts.ShouldBeEmpty();
                ReportValidator.Validate(parsed, observation.Commit!).ShouldBeNull();
                ReceiptJson.Parse<RunState>(stateInput.ToJsonString(), RunStateStore.Json).Rows.Single().Phase
                    .ShouldBe(shape is "value" or "empty" ? "executing" : null);
                var path = Path.Combine(run, "acceptance.json");
                File.WriteAllText(path, input.ToJsonString());
                var output = new StringWriter();
                (await Antiphon.Checkpoints.Program.RunAsync(["validate", "--evidence", path, "--expected-source-sha", observation.Commit!],
                    new CheckpointApp.Runtime { RepoRoot = run, Output = output })).ShouldBe(0, output.ToString());
            }
            catch (Exception ex) { failures.Add($"shape={shape}: {ex.Message}"); }
        }
        foreach (var required in new[] { "source", "rows", "builds", "cleanupRefusals", "markdownSha256" })
        {
            var invalid = report.DeepClone().AsObject();
            invalid.Remove(required);
            Should.Throw<JsonException>(() => ReceiptJson.Parse<ReportModel>(invalid.ToJsonString(), ReportWriter.Json));
        }
        foreach (var invalidValue in new JsonNode?[] { null, JsonValue.Create(""), JsonValue.Create(42) })
        {
            var invalid = report.DeepClone().AsObject();
            invalid["attempts"] = invalidValue?.DeepClone();
            Should.Throw<JsonException>(() => ReceiptJson.Parse<ReportModel>(invalid.ToJsonString(), ReportWriter.Json));
        }
        foreach (var field in new[] { "brokerReason", "terminalReason" })
        {
            var invalid = report.DeepClone().AsObject();
            invalid["rows"]![0]![field] = 42;
            Should.Throw<JsonException>(() => ReceiptJson.Parse<ReportModel>(invalid.ToJsonString(), ReportWriter.Json));
        }
        var invalidState = state.DeepClone().AsObject();
        invalidState.Remove("phase");
        Should.Throw<JsonException>(() => ReceiptJson.Parse<RunState>(invalidState.ToJsonString(), RunStateStore.Json));
        foreach (var schema in new[] { 2, 3 })
        {
            var input = report.DeepClone().AsObject();
            RemoveNewFields(input);
            input["schemaVersion"] = schema;
            input["exitCode"] = 5;
            input["verdict"] = "RED";
            input["rows"]![0]!["exitCode"] = 5;
            input["rows"]![0]!["state"] = "timeout";
            var parsed = ReceiptJson.Parse<ReportModel>(input.ToJsonString(), ReportWriter.Json);
            parsed.SchemaVersion.ShouldBe(schema);
            parsed.Rows.Single().ExecutedSeconds.ShouldBeNull();
            parsed.Rows.Single().ExitCode.ShouldBe(5);
            ReportValidator.Validate(parsed, observation.Commit!).ShouldBe("report_failed");
        }
        foreach (var historical in new[] { "failed", "cleanup" })
        {
            var input = report.DeepClone().AsObject();
            var attempt = report.DeepClone().AsObject();
            RemoveNewFields(attempt);
            if (historical == "failed") { attempt["exitCode"] = 5; attempt["verdict"] = "RED"; }
            else attempt["cleanupRefusals"] = new JsonArray("still-present");
            input["attempts"] = new JsonArray(attempt);
            var parsed = ReceiptJson.Parse<ReportModel>(input.ToJsonString(), ReportWriter.Json);
            ReportValidator.Validate(parsed, observation.Commit!).ShouldBe(historical == "failed" ? "historical_attempt_failed" : "cleanup_refused");
        }
        failures.ShouldBeEmpty(string.Join('\n', failures));
    }

    private static void RemoveNewFields(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in new[] { "attempts", "brokerReason", "terminalReason", "executedSeconds", "budgetWallSeconds",
                "admissionOnlySeconds", "executionSeconds", "executionLimitSeconds", "hardWallLimitSeconds", "remainingExecutionSeconds", "hardWallTimeoutAt", "teardownSeconds" })
                obj.Remove(key);
            // The run's phase is old and required; only build/row phase is new.
            foreach (var key in new[] { "builds", "rows" })
                if (obj[key] is JsonArray children)
                    foreach (var child in children.OfType<JsonObject>()) { child.Remove("phase"); child.Remove("wallSeconds"); }
            foreach (var child in obj.Select(pair => pair.Value).OfType<JsonNode>()) RemoveNewFields(child);
        }
        else if (node is JsonArray array)
            foreach (var child in array.OfType<JsonNode>()) RemoveNewFields(child);
    }

    [Test]
    public async Task C1531_inputs_resolve_without_disabling_bounds()
    {
        var root=TempDir();
        foreach(var value in new[]{"0","-1","null","\"\"","2147483647"})
            Should.Throw<ManifestValidationException>(()=>ManifestLoader.LoadYaml("timeouts:\n  hardWallMinutes: "+value,root,false));
        var manifest=ManifestLoader.LoadYaml("build:\n  slotWaitMinutes: 2\ntimeouts:\n  hardWallMinutes: 17",root,false);
        manifest.Build.SlotWaitMinutes.ShouldBe(2);
        ManifestLoader.ToYaml(manifest).ShouldContain("hardWallMinutes: 17");
        var cliManifest = new CheckpointManifest { Checkpoints = [new CheckpointSpec { Id="CP-1",After=["S1"],Command="fake",EstimatedMinutes=1 }] };
        var manifestFile=Path.Combine(root,"input.yaml"); File.WriteAllText(manifestFile,ManifestLoader.ToYaml(cliManifest));
        var tool=TempDir(); File.WriteAllText(Path.Combine(tool,"Antiphon.Checkpoints.dll"),"fixture");
        var runtime=new CheckpointApp.Runtime { RepoRoot=root,EnvironmentLookup=_=>null,SourceCapture=_=>CheckpointFixtures.CleanSource(),
            ToolDirectory=tool,Launch=_=>Environment.ProcessId,Output=TextWriter.Null };
        foreach(var name in new[]{"total-timeout","hard-wall-timeout"})
            foreach(var input in new[]{"", " ", "0", "-1m", "junk", "999999999999999d", "NaNm"})
                await Should.ThrowAsync<ManifestValidationException>(()=>Antiphon.Checkpoints.Program.RunAsync(["start",manifestFile,"--"+name,input],runtime));
        (await Antiphon.Checkpoints.Program.RunAsync(["start",manifestFile,"--total-timeout","10m","--hard-wall-timeout","25m"],runtime)).ShouldBe(0);
        var requestFile=Directory.GetFiles(Path.Combine(root,".antiphon","checkpoints"),"request.json",SearchOption.AllDirectories).Single();
        var request=JsonSerializer.Deserialize<RunRequest>(File.ReadAllText(requestFile),CheckpointApp.Json)!;
        request.TotalTimeoutMinutes.ShouldBe(10); request.HardWallTimeoutMinutes.ShouldBe(25);
        foreach(var wallOverride in new int?[] {null,12})
        {
        var bound = new CheckpointAdmissionBudgetTests.Harness(TempDir(), commands:true);
        bound.Manifest.Checkpoints.RemoveAt(1);
        bound.Manifest.Checkpoints.Single().After=["S1"];
        bound.Manifest.Build.SlotWaitMinutes=2;
        bound.Manifest.Timeouts.HardWallMinutes=8;
        var boundRun=CheckpointApp.CreateRun(bound.Manifest,new RunRequest { TotalTimeoutMinutes=10,HardWallTimeoutMinutes=wallOverride,KeepOutputs=true },bound.Root,
            new CheckpointApp.Runtime { SourceCapture=_=>CheckpointFixtures.CleanSource() });
        var entered=CheckpointAdmissionBudgetTests.Signal();
        var holders=new CheckpointAdmissionBudgetTests.Holders();
        var handler=new CheckpointAdmissionBudgetTests.AdmissionHttp(bound.Clock,entered,false);
        var execute=CheckpointApp.ExecuteAsync(boundRun,bound.Watchdog.Token,new CheckpointApp.Runtime
        {
            Driver=bound.Driver,SlotHandler=handler,SlotHolders=holders,TimeProvider=bound.Clock,SlotClock=bound.Clock.GetUtcNow,
            SourceCapture=_=>CheckpointFixtures.CleanSource(),EnvironmentLookup=_=>null,Platform=new FakePlatform(),
        });
        try
        {
            await entered.Task.WaitAsync(bound.Watchdog.Token);
            bound.Clock.Advance(TimeSpan.FromMinutes(2));
            (await execute).ShouldBe(4);
            var state=new RunStateStore().TryRead(Path.Combine(boundRun,"state.json"))!;
            state.Rows.Single().WaitedSeconds.ShouldBe(120);
            state.HardWallLimitSeconds.ShouldBe(wallOverride is null ? 480 : 720);
            state.ExecutionLimitSeconds.ShouldBe(600);
            holders.Disposed.ShouldBe(1);bound.Driver.Calls.ShouldBeEmpty();
        }
        finally { bound.Watchdog.Cancel();await execute; }
        foreach(var yaml in new[]{"schemaVersion: 1\ntimeouts: null","schemaVersion: 1\ntimeouts: {}","schemaVersion: 1\nbuild: null","schemaVersion: 1\nbuild: {}"})
            ManifestLoader.LoadYaml(yaml,root,false).Build.SlotWaitMinutes.ShouldBe(45);
        foreach(var json in new[]{"{\"executionSeconds\":-1}","{\"rows\":[{\"executedSeconds\":-1}]}"})
            ReportValidator.Validate(JsonSerializer.Deserialize<ReportModel>(json,ReportWriter.Json)!,new string('a',40)).ShouldBe("invalid_timing");
        Should.Throw<JsonException>(()=>JsonSerializer.Deserialize<ReportModel>("{\"executionSeconds\":\"0\"}",ReportWriter.Json));

        }
        var absent=ManifestLoader.LoadYaml("schemaVersion: 1",root,false);
        absent.Build.SlotWaitMinutes.ShouldBe(45);
    }

    [Test]
    public async Task C1531_receipts_keep_wait_execution_and_counts()
    {
        var h=new CheckpointAdmissionBudgetTests.Harness(TempDir());
        h.Slots.OnAcquire=(_,ct)=>{h.Clock.Advance(TimeSpan.FromMinutes(2));ct.ThrowIfCancellationRequested();return Task.CompletedTask;};
        h.Driver.When(_=>true,(r,ct)=>h.Finish(r,ct,1));
        var run=CheckpointApp.CreateRun(h.Manifest,new RunRequest { TotalTimeoutMinutes=10,KeepOutputs=true },h.Root,
            new CheckpointApp.Runtime { SourceCapture=_=>CheckpointFixtures.CleanSource() });
        var exit=await CheckpointApp.ExecuteAsync(run,h.Watchdog.Token,new CheckpointApp.Runtime
        {
            Driver=h.Driver,Slots=h.Slots,TimeProvider=h.Clock,SchedulerSuspended=h.Slots.Pump,Platform=new FakePlatform(),EnvironmentLookup=_=>null,
            SourceCapture=_=>CheckpointFixtures.CleanSource(),
        });
        exit.ShouldBe(0);
        var report=JsonSerializer.Deserialize<ReportModel>(File.ReadAllText(Path.Combine(run,"report.json")),ReportWriter.Json)!;
        report.ExecutionSeconds.ShouldBe(120);
        report.AdmissionOnlySeconds.ShouldBe(240);
        report.Rows.Single().Executed.ShouldBe(1);
        report.Rows.Single().ExecutedSeconds.ShouldBe(60);
        report.Rows.Single().WaitedSeconds.ShouldBe(120);
        var markdown=File.ReadAllText(Path.Combine(run,"report.md"));
        markdown.ShouldContain("executed=1 passed=1");
        markdown.ShouldContain("CHECKPOINT-TIMING CP-1 waited=120s executed=60s wall=180s");
    }

    [Test]
    public void C1531_legacy_receipts_remain_unknown_not_zero()
    {
        foreach(var schema in new[]{2,3})
        {
            var raw="{\"schemaVersion\":"+schema+",\"rows\":[{\"id\":\"CP-1\",\"exitCode\":5,\"executed\":0,\"state\":\"timeout\"}],\"cleanupRefusals\":[\"still-present\"]}";
            var report=JsonSerializer.Deserialize<ReportModel>(raw,ReportWriter.Json)!;
            report.ExecutionSeconds.ShouldBeNull(); report.AdmissionOnlySeconds.ShouldBeNull();
            report.Rows.Single().ExecutedSeconds.ShouldBeNull();
            var roundtrip=JsonSerializer.Deserialize<ReportModel>(ReportWriter.JsonText(report),ReportWriter.Json)!;
            roundtrip.Rows.Single().ExitCode.ShouldBe(5);
            roundtrip.Rows.Single().ExecutedSeconds.ShouldBeNull();
            roundtrip.CleanupRefusals.ShouldContain("still-present");
            var observation=CheckpointFixtures.CleanSource();
            var source=new SourceEvidence { Start=observation,End=observation,State="clean",BuildSource="verified" };
            roundtrip.Source=source; roundtrip.Commit=observation.Commit!; roundtrip.ExitCode=5; roundtrip.Verdict="RED";
            roundtrip.Rows.Single().Source=source;
            roundtrip.Rows.Single().Build="bin-a";
            var next=JsonSerializer.Deserialize<ReportModel>(ReportWriter.JsonText(roundtrip),ReportWriter.Json)!;
            next.EndedAt=DateTimeOffset.UtcNow.AddSeconds(1); next.ExitCode=0;next.Verdict="GREEN";next.CleanupRefusals=[];
            next.Rows.Single().ExitCode=0;next.Rows.Single().State="green";next.Rows.Single().ExecutedSeconds=12;
            var merged=ReportMerger.Merge([roundtrip,next]);
            merged.Attempts.Count.ShouldBe(2);
            merged.Attempts[0].ExitCode.ShouldBe(5);
            merged.Attempts[0].Rows.Single().ExecutedSeconds.ShouldBeNull();
            merged.Rows.Single().ExecutedSeconds.ShouldBe(12);
            ReportValidator.Validate(merged,observation.Commit!).ShouldBe("cleanup_refused");
            roundtrip.CleanupRefusals=[];
            ReportValidator.Validate(ReportMerger.Merge([roundtrip,next]),observation.Commit!).ShouldBe("historical_attempt_failed");
        }
    }
}
