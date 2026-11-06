using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(60_000)] // CARD-0824 provisional read/report pack; real Git plus shared DB template.
public sealed class C0824RefOnlyReadTests
{
    [Test]
    public async Task Check_distinguishes_creation_baseline_from_remote_observation()
    {
        await using var f = await RefOnlySyncFixture.CreateAsync();
        var w = f.Git;
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(f.Schema.ConnectionString, new RefuseSave()));
        var git = new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance,
            settings: Options.Create(new GitSettings { TimeoutSeconds = 10 }));
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var probe = new DelegateCheckProbe(db, git, clock, Options.Create(new SupervisionSettings()));
        var baseline = TaskProgressJson.TryReadBaseline(f.Task.ProgressBaselineJson)!;
        await w.RunAsync(w.Desktop, "remote", "remove", "origin");
        async Task<string> RefsAsync() => await w.RunAsync(w.Desktop, "for-each-ref", "--format=%(refname) %(objectname)");
        var before = await RefsAsync();
        var facts = await probe.GatherAsync(f.Task, default);
        var observed = facts.Git.ShouldNotBeNull("creation-baseline-visible-without-checkout");
        observed.Directory.ShouldBe(w.Desktop);
        observed.Range.ShouldBe(w.Baseline + ".." + w.Baseline);
        ((object?)observed.ChangedFiles).ShouldBeNull("runner-dirt-unknown");
        ((object?)observed.UntrackedFiles).ShouldBeNull("runner-untracked-unknown");
        var digest = DelegateCheckProbe.RenderDigest(facts);
        digest.ShouldContain("creation baseline"); digest.ShouldContain("stale");
        digest.ShouldContain(baseline.CapturedAt.ToString("O"), Case.Sensitive, "check-sha-and-time:baseline");
        (await RefsAsync()).ShouldBe(before, "Check-never-moves-refs");
        await w.RunAsync(w.Desktop, "remote", "add", "origin", w.Origin);
        var source = await w.RunnerPushAsync("work.txt", "confirmed bytes");
        var synced = await f.Service().SyncAsync(f.Task, default);
        synced.Confirmed.ShouldBeTrue();
        var progress = await new TaskCompletionProgressService(w.Git, workspaceContexts: f.Contexts)
            .EvaluateAsync(f.Task, "", synced, default);
        f.Task.CompletionProgressEvidenceJson = TaskProgressJson.SerializeEvidence(progress.Evidence);
        await f.Db.SaveChangesAsync();
        var index = File.GetLastWriteTimeUtc(Path.Combine(w.Desktop, ".git", "index"));
        before = await RefsAsync();
        facts = await probe.GatherAsync(f.Task, default);
        facts.Git.ShouldNotBeNull().Range.ShouldBe(w.Baseline + ".." + source, "check-sha-and-time:confirmed");
        DelegateCheckProbe.RenderDigest(facts).ShouldContain("observed");
        File.GetLastWriteTimeUtc(Path.Combine(w.Desktop, ".git", "index")).ShouldBe(index, "read-only-index");
        var newer = await w.RunnerPushAsync("newer.txt", "not fetched by Check");
        (await w.HasObjectAsync(newer)).ShouldBeFalse("newer-object-not-local-before");
        facts = await probe.GatherAsync(f.Task, default);
        facts.Git.ShouldNotBeNull().Range.ShouldBe(w.Baseline + ".." + newer, "newer-exact-read-only-observation");
        facts.Git!.Unavailable.ShouldNotBeNull("unavailable-log-is-not-zero");
        ((object?)facts.Git.ChangedFiles).ShouldBeNull();
        ((object?)facts.Git.UntrackedFiles).ShouldBeNull();
        (await w.HasObjectAsync(newer)).ShouldBeFalse("Check-must-not-fetch");
        (await RefsAsync()).ShouldBe(before, "Check-must-not-pin-or-move-refs");
        Directory.Exists(f.Identity).ShouldBeFalse();
        await w.RunAsync(w.Desktop, "remote", "set-url", "--add", "--push", "origin", w.Runner);
        await w.RunAsync(w.Desktop, "remote", "set-url", "--add", "--push", "origin", w.Origin);
        var ambiguous = await probe.GatherAsync(f.Task, default);
        DelegateCheckProbe.RenderDigest(ambiguous).ShouldContain("stale");
        (await w.HasObjectAsync(newer)).ShouldBeFalse();
    }

    [Test]
    public async Task Reply_progress_counts_confirmed_range_never_main_head()
    {
        foreach (var state in new[] { "confirmed", "pending", "pending-unpinned", "pending-unknown", "refused", "failed" })
        {
            await using var w = await RunnerSettlementWorld.CreateAsync(controlledSyncClock: true,
                role: state == "pending-unknown" ? AgentTaskRole.Docs : AgentTaskRole.Code);
            await using (var stageDb = w.CreateContext())
            {
                var stageTask = await stageDb.AgentTasks.SingleAsync(t => t.Id == w.TaskId);
                stageTask.Stage = OrchestrationStage.FollowUp;
                await stageDb.SaveChangesAsync();
            }
            await w.RecordRefOnlyAsync();
            for (var i = 0; i < 3; i++)
            {
                await File.WriteAllTextAsync(Path.Combine(w.Git.Desktop, "main-" + i + ".txt"), "unrelated");
                await w.Git.RunAsync(w.Git.Desktop, "add", "main-" + i + ".txt");
                await w.Git.RunAsync(w.Git.Desktop, "commit", "-m", "unrelated main commit");
            }
            // A plausible foreign repository exists at the identity; existence grants no read authority.
            Directory.CreateDirectory(w.Task.WorktreePath!);
            await w.Git.RunAsync(w.Task.WorktreePath!, "init", "-b", "foreign");
            var source = await w.Git.RunnerPushAsync("only-task.txt", "one task commit");
            if (state.StartsWith("pending", StringComparison.Ordinal))
            {
                if (state == "pending")
                    await using (var scope = w.Services.CreateAsyncScope())
                        (await scope.ServiceProvider.GetRequiredService<RemoteWorkspaceService>().SyncAsync(w.Task, default)).Confirmed.ShouldBeTrue("admitted-source-pin");
                await w.MintRefOnlyDebtAsync(RunnerSettlementWorld.Report("Implemented. " + w.Claim(source)),
                    fetchSourceObjects: state != "pending-unknown");
                if (state != "pending")
                {
                    (await w.Git.HasObjectAsync(source)).ShouldBe(state == "pending-unpinned", "reached:R2:" + state + ":local-object");
                    var sync = w.Evidence()!.RemoteSync!;
                    sync.State.ShouldBe(RemoteSettlementSyncState.Pending);
                    sync.ConfirmedSha.ShouldBeNull("pending-not-confirmed:" + state);
                    var pin = sync.ObservationRef ?? RemoteWorkspaceService.SettlementPin(w.TaskId);
                    (await w.Services.GetRequiredService<GitWorkspaceService>().RevParseAsync(w.Git.Desktop, pin, default))
                        .ShouldBeNull("reached:R2:" + state + ":no-pin");
                }
            }
            else if (state == "refused")
                await w.RecoverAsync(new([new string('a', source.Length)], null,
                    HeldReport: RunnerSettlementWorld.Report("Held report. " + w.Claim(new string('a', source.Length)))
                        + DelegationReportFormatter.ReportToken(w.TaskId, "done")));
            else if (state == "failed") await w.SettleAsync(RunnerSettlementWorld.Report("Failed while working.", "code")
                + DelegationReportFormatter.ReportToken(w.TaskId, "failed"), closingVerdict: false);
            else await w.SettleAsync(RunnerSettlementWorld.Report("Implemented. " + w.Claim(source)));
            var note = (await w.NoteAsync()).ShouldNotBeNull().Body;
            if (state is "refused" or "failed" or "pending-unpinned" or "pending-unknown")
            {
                w.Task.Status.ShouldBe(state == "failed" ? AgentTaskStatus.Failed
                    : state.StartsWith("pending", StringComparison.Ordinal) ? AgentTaskStatus.Succeeded : AgentTaskStatus.Blocked);
                note.ShouldNotContain(DelegationGitFacts.FormatHeader(3, 3), Case.Sensitive, "no-main-HEAD-count");
                note.ShouldNotContain(DelegationGitFacts.FormatHeader(0, 0), Case.Sensitive, "unknown-is-not-zero");
                if (state.StartsWith("pending", StringComparison.Ordinal))
                {
                    note.ShouldContain("base unknown", Case.Sensitive, "pending-source-count-unavailable:" + state);
                    await using var db = w.CreateContext();
                    var outcome = await db.StageOutcomes.AsNoTracking().SingleAsync(o => o.StageTaskId == w.TaskId);
                    (outcome.Detail ?? "").ShouldNotContain("commits=", Case.Sensitive, "repair-hint-count-unavailable:" + state);
                }
            }
            else
            {
                w.Task.Status.ShouldBe(AgentTaskStatus.Succeeded, w.Task.CompletionProgressEvidenceJson);
                note.ShouldContain(DelegationGitFacts.FormatHeader(1, 1), Case.Sensitive, "count=B..S:" + state);
                if (state == "pending")
                {
                    w.Evidence()!.RemoteSync!.ConfirmedSha.ShouldBeNull("pending-not-confirmed");
                    note.ShouldContain("desktop-sync=pending");
                }
                await using var db = w.CreateContext();
                var outcome = await db.StageOutcomes.AsNoTracking().SingleAsync(o => o.StageTaskId == w.TaskId);
                outcome.Detail.ShouldContain("commits=1", Case.Sensitive, "repair-hint-count=B..S:" + state);
            }
        }
    }

    [Test]
    public async Task Long_report_retains_text_without_materializing_identity()
    {
        foreach (var storage in new[] { "canonical", "missing", "failure", "bind-refusal" })
        {
            using var root = new RefOnlyPersistentRoot();
            await using var w = await RunnerSettlementWorld.CreateAsync();
            await w.RecordRefOnlyAsync();
            w.ConfigureServices = services =>
            {
                if (storage == "missing") services.RemoveAll<IAgentReportStore>();
                if (storage == "failure") services.AddSingleton<IAgentReportStore>(new ThrowingReportStore());
            };
            await w.RestartServicesAsync();
            w.Services.GetRequiredService<IOptions<DelegationSettings>>().Value.ReportStorageRoot = root.Path;
            var source = await w.Git.RunnerPushAsync("work.txt", "source");
            var raw = "REPORT_HEAD\n" + new string('x', 18000) + "\nREPORT_MIDDLE\n" + new string('y', 18000) + "\nREPORT_TAIL";
            var mainSpill = Path.Combine(w.Git.Desktop, ".antiphon", "task-" + DelegationReportFormatter.Short(w.TaskId) + ".md");
            var identitySpill = Path.Combine(w.Task.WorktreePath!, ".antiphon", "task-" + DelegationReportFormatter.Short(w.TaskId) + ".md");
            Directory.CreateDirectory(Path.GetDirectoryName(identitySpill)!);
            await File.WriteAllTextAsync(identitySpill, "foreign report decoy");
            if (storage == "bind-refusal") await w.RecoverThroughWatchdogAsync(raw + "\n" + w.Claim(source));
            else await w.SettleAsync(RunnerSettlementWorld.Report(raw + "\n" + w.Claim(source)));
            w.Task.Status.ShouldBe(AgentTaskStatus.Succeeded, "full-report-settles:" + storage);
            await using var db = w.CreateContext();
            var row = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == w.TaskId);
            row.Result.ShouldNotBeNull().ShouldContain(raw, Case.Sensitive, "result-full:" + storage);
            if (storage is "canonical" or "bind-refusal")
            {
                row.ResultFilePath.ShouldNotBeNull();
                (await File.ReadAllTextAsync(row.ResultFilePath!)).ShouldBe(row.Result, "canonical-full-text");
                row.ResultFilePath!.ShouldStartWith(root.Path);
            }
            else
            {
                row.ResultFilePath.ShouldBeNull("canonical-pointer-unavailable:" + storage);
                (await w.NoteAsync()).ShouldNotBeNull().Body.ShouldContain("GET /api/agent-tasks/" + w.TaskId, Case.Sensitive, "unavailable-file-retains-API-pointer");
            }
            File.Exists(mainSpill).ShouldBeFalse("no-spill-write:" + storage);
            (await File.ReadAllTextAsync(identitySpill)).ShouldBe("foreign report decoy", "no-decoy-read-or-write");
        }
    }

    private sealed class RefuseSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Check may not save");
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) =>
            throw new InvalidOperationException("Check may not save");
    }
    private sealed class ThrowingReportStore : IAgentReportStore
    {
        public Task<AgentReportStorageResult> StoreAsync(AgentTask task, CancellationToken ct) => throw new IOException("owned report-store outage");
        public Task<bool> IsUsableAsync(string? path, string raw, CancellationToken ct) => Task.FromResult(false);
    }
}
