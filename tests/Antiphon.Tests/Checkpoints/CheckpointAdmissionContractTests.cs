using System.Text.Json;
using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class CheckpointAdmissionContractTests : CheckpointTestBase
{
    [Test]
    public void C1531_inputs_resolve_without_disabling_bounds()
    {
        var root=TempDir();
        foreach(var value in new[]{"0","-1","null","\"\"","2147483647"})
            Should.Throw<ManifestValidationException>(()=>ManifestLoader.LoadYaml("timeouts:\n  hardWallMinutes: "+value,root,false));
        var manifest=ManifestLoader.LoadYaml("build:\n  slotWaitMinutes: 2\ntimeouts:\n  hardWallMinutes: 17",root,false);
        manifest.Build.SlotWaitMinutes.ShouldBe(2);
        ManifestLoader.ToYaml(manifest).ShouldContain("hardWallMinutes: 17");
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
            Driver=h.Driver,Slots=h.Slots,TimeProvider=h.Clock,Platform=new FakePlatform(),EnvironmentLookup=_=>null,
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
        }
    }
}
