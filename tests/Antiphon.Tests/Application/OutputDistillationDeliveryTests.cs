using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Files;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
[ParallelLimiter<ProcessSpawnLimit>]
public class OutputDistillationDeliveryTests
{
    [Test]
    public async Task Public_goal_limit_is_preserved_when_internal_prompt_storage_is_widened()
    {
        await using var provider = AgentTaskSettlementRaceTests.BuildHarness();
        await using var scope = provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<AgentTaskService>();
        await Should.ThrowAsync<Antiphon.Server.Application.Exceptions.ValidationException>(() =>
            service.CreateAsync(new(new string('x', 20_001)), new(null, null, null), CancellationToken.None));
    }

    [Test]
    [Arguments("Check")] [Arguments("Distill")] [Arguments("Diagnose")]
    [Arguments("Blocked")] [Arguments("None")]
    public async Task Ineligible_reports_do_not_enter_canonical_or_model_work(string scenario)
    {
        await using var f = await DeliveryFixture.CreateAsync();
        var parent = await AgentTaskSettlementRaceTests.SeedSessionAsync(f.Workspace.Main);
        var (task, session) = await AgentTaskSettlementRaceTests.SeedSharedTaskAsync(f.Workspace.Main, parent);
        await using var db = OutputDistillationHarness.CreateContext();
        var row = await db.AgentTasks.SingleAsync(t => t.Id == task.Id);
        row.Role = scenario is "Blocked" or "None" ? AgentTaskRole.Review : Enum.Parse<AgentTaskRole>(scenario);
        if (scenario == "None") row.ReplyTo = AgentTaskReplyTo.None;
        await db.SaveChangesAsync();
        await AgentTaskSettlementRaceTests.SeedMarkedTurnAsync(session, task.Id, ReportOfLength(5500), scenario == "Blocked" ? "blocked" : "done");
        await f.Provider.GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(session, CancellationToken.None);
        await db.Entry(row).ReloadAsync();
        row.ResultFilePath.ShouldBeNull();
        f.Provider.GetRequiredService<OutputDistillationQueue>().PendingCount.ShouldBe(0);
        (await db.OutputDistillations.CountAsync(d => d.TaskId == task.Id)).ShouldBe(0);
        await db.SessionQueuedMessages.Where(m => m.SourceTaskId == task.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, QueuedMessageStatus.Canceled));
    }

    internal const string Middle = "The violet lantern marks the distinctive middle of this report.";
    internal const string Summary = "- Completed the requested report with its conclusion and caveat preserved. The review found the documented behavior consistent and ready for further review; optional work remains bounded by the original deadline.";
    internal static string ReportOfLength(int length)
    {
        var head = "Completed the report.\n";
        var tail = "\nCaveat: further review remains necessary.";
        var padding = string.Concat(Enumerable.Repeat("ordinary explanatory words ", length / 20 + 1));
        var half = (length - head.Length - Middle.Length - tail.Length - 2) / 2;
        var text = head + padding[..half] + "\n" + Middle + "\n";
        return text + padding[..(length - text.Length - tail.Length)] + tail;
    }

    [Test]
    [Arguments(3500, false)] [Arguments(5500, true)] [Arguments(4000, true)]
    [Arguments(20000, true)] [Arguments(20001, true)]
    public async Task Report_boundaries(int length, bool modern)
    {
        await using var f = await DeliveryFixture.CreateAsync(modern: modern);
        var seed = await f.SettleAsync(ReportOfLength(length));
        seed.Task.Result!.Length.ShouldBe(length);
        seed.Task.ResultFilePath.ShouldNotBeNull();
        (await File.ReadAllTextAsync(seed.Task.ResultFilePath!)).ShouldBe(seed.Task.Result);
        if (length >= 4000) seed.Task.ResultFilePath.ShouldBe(f.Workspace.Expected(seed.Task));
        var eligible = length is >= 4000 and <= 20000;
        var ledger = await f.RunDistillerAsync(seed, eligible ? Summary : null);
        ledger.Outcome.ShouldBe(eligible ? DistillationOutcome.Applied
            : length < 4000 ? DistillationOutcome.SkippedShort : DistillationOutcome.SkippedLong);
        var note = await f.DeliverAsync(seed);
        if (eligible) note.Body.ShouldContain(Summary);
        else note.Body.ShouldBe(seed.RawBody);
    }

    [Test]
    [Arguments(OutputDistillerMode.Shadow, true, false)] [Arguments(OutputDistillerMode.Apply, true, false)]
    [Arguments(OutputDistillerMode.Shadow, false, false)] [Arguments(OutputDistillerMode.Apply, false, false)]
    [Arguments(OutputDistillerMode.Shadow, true, true)] [Arguments(OutputDistillerMode.Apply, true, true)]
    [Arguments(OutputDistillerMode.Shadow, false, true)] [Arguments(OutputDistillerMode.Apply, false, true)]
    public async Task Mode_and_terminal_status_preserve_report(OutputDistillerMode mode, bool enabled, bool failed)
    {
        await using var f = await DeliveryFixture.CreateAsync(mode: mode, enabled: enabled);
        var seed = await f.SettleAsync(ReportOfLength(5500), failed: failed);
        seed.Task.ResultFilePath.ShouldBe(f.Workspace.Expected(seed.Task));
        seed.Task.Status.ShouldBe(failed ? AgentTaskStatus.Failed : AgentTaskStatus.Succeeded);
        if (enabled) (await f.RunDistillerAsync(seed, Summary)).Outcome.ShouldBe(
            mode == OutputDistillerMode.Apply ? DistillationOutcome.Applied : DistillationOutcome.Shadowed);
        else
        {
            seed.HoldUntil.ShouldBeNull();
            await using var db = OutputDistillationHarness.CreateContext();
            (await db.OutputDistillations.CountAsync(d => d.TaskId == seed.Task.Id)).ShouldBe(0);
            (await db.AgentTasks.CountAsync(t => t.AgentName == f.Settings.OutputDistillerAgentSlug)).ShouldBe(0);
        }
        var note = await f.DeliverAsync(seed);
        note.NoteHeader.ShouldBe(seed.Header);
        if (enabled && mode == OutputDistillerMode.Apply) note.Body.ShouldContain(Summary);
        else note.Body.ShouldBe(seed.RawBody);
    }

    [Test]
    public async Task Modern_report_below_transport_limit_applies_with_canonical_file()
    {
        await using var f = await DeliveryFixture.CreateAsync();
        var seed = await f.SettleAsync(ReportOfLength(5500));
        seed.RawBody.ShouldContain(Middle);
        (await f.RunDistillerAsync(seed, Summary)).Outcome.ShouldBe(DistillationOutcome.Applied);
        var note = await f.DeliverAsync(seed);
        note.Body.ShouldContain(Summary); note.Body.ShouldContain("Full report: " + seed.Task.ResultFilePath);
        note.Body.ShouldNotContain(Middle); note.Body.ShouldNotContain("GET /api/agent-tasks/");
        note.NoteHeader.ShouldBe(seed.Header); note.ContentDigest.ShouldBe(seed.Digest); note.HoldUntil.ShouldBeNull();
        await using var db = OutputDistillationHarness.CreateContext();
        (await db.AgentTasks.SingleAsync(t => t.Id == seed.Task.Id)).Result.ShouldBe(seed.Task.Result);
        (await db.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == seed.Task.Id)).ShouldBe(1);
    }

    [Test]
    [Arguments(false)] [Arguments(true)]
    public async Task Target_canonical_unavailable_retains_legacy_backstop_without_relabeling_author_file(bool authorFile)
    {
        await using var f = await DeliveryFixture.CreateAsync(modern: false);
        using var temp = new ReportTempWorkspace();
        var seed = await f.SettleAsync(ReportOfLength(5500), cwd: temp.Path, authorFile: authorFile);
        var legacy = Path.Combine(temp.Path, ".antiphon", $"task-{DelegationReportFormatter.Short(seed.Task.Id)}.md");
        seed.Task.ResultFilePath.ShouldBe(legacy);
        (await File.ReadAllTextAsync(legacy)).ShouldBe(authorFile ? "delegate authored different detail" : seed.Task.Result);
        (await f.RunDistillerAsync(seed, Summary)).Outcome.ShouldBe(DistillationOutcome.Applied);
        var note = await f.ReadNoteAsync(seed.NoteId);
        if (authorFile) { note.Body.ShouldContain("GET /api/agent-tasks/"); note.Body.ShouldNotContain(legacy); }
        else note.Body.ShouldContain("Full report: " + legacy);
    }

    [Test]
    public async Task Canonical_selection_preserves_but_never_advertises_different_legacy_author_file()
    {
        await using var f = await DeliveryFixture.CreateAsync(modern: false);
        var seed = await f.SettleAsync(ReportOfLength(5500), authorFile: true);
        var legacy = Path.Combine(f.Workspace.Main, ".antiphon", $"task-{DelegationReportFormatter.Short(seed.Task.Id)}.md");
        seed.Task.ResultFilePath.ShouldBe(f.Workspace.Expected(seed.Task));
        (await File.ReadAllTextAsync(legacy)).ShouldBe("delegate authored different detail");
        await f.RunDistillerAsync(seed, Summary);
        var note = await f.ReadNoteAsync(seed.NoteId);
        note.Body.ShouldContain(seed.Task.ResultFilePath!); note.Body.ShouldNotContain(legacy);
    }

    [Test]
    [Arguments("null")] [Arguments("deleted")] [Arguments("corrupt")] [Arguments("unreadable")]
    public async Task Unusable_file_uses_api_recovery(string scenario)
    {
        await using var f = await DeliveryFixture.CreateAsync();
        var seed = await f.SettleAsync(ReportOfLength(5500));
        FileStream? locked = null;
        try
        {
            if (scenario == "null")
            {
                await using var db = OutputDistillationHarness.CreateContext();
                await db.AgentTasks.Where(t => t.Id == seed.Task.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.ResultFilePath, (string?)null));
            }
            if (scenario == "deleted") File.Delete(seed.Task.ResultFilePath!);
            if (scenario == "corrupt") await File.WriteAllTextAsync(seed.Task.ResultFilePath!, "different content");
            if (scenario == "unreadable") locked = new FileStream(seed.Task.ResultFilePath!, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            await f.RunDistillerAsync(seed, Summary);
            var note = await f.DeliverAsync(seed);
            note.Body.ShouldContain("GET /api/agent-tasks/"); note.Body.ShouldNotContain(seed.Task.ResultFilePath!);
        }
        finally { locked?.Dispose(); }
    }

    [Test]
    public async Task Canonical_file_survives_worktree_removal_and_provider_recreation()
    {
        await using var f = await DeliveryFixture.CreateAsync(enabled: false, worktree: true);
        var seed = await f.SettleAsync(ReportOfLength(5500), cwd: f.Workspace.Worktree);
        seed.Task.ResultFilePath.ShouldNotBeNull("settlement must retain a canonical file before worktree removal");
        await f.Provider.DisposeAsync();
        await f.Workspace.RemoveWorktreeAsync();
        await using var recreated = AgentTaskSettlementRaceTests.BuildHarness();
        await using var scope = recreated.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Antiphon.Server.Infrastructure.Data.AppDbContext>();
        var stored = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == seed.Task.Id);
        stored.ResultFilePath.ShouldBe(seed.Task.ResultFilePath);
        File.Exists(stored.ResultFilePath).ShouldBeTrue("the retained report must outlive its delegate worktree");
        (await File.ReadAllTextAsync(stored.ResultFilePath!)).ShouldBe(seed.Task.Result);
    }

    [Test]
    [Arguments(false)] [Arguments(true)]
    public async Task Apply_preserves_handoff_warnings_and_generated_deliverables(bool failed)
    {
        await using var f = await DeliveryFixture.CreateAsync();
        var anchors = "\n--- next stage ---\nnext: review\nhandoff: Verify the implementation.";
        var seed = await f.SettleAsync(ReportOfLength(5500 - anchors.Length) + anchors, failed: failed, deliverables: true);
        var annotatedHeader = seed.Header + "\nCaller warning: inspect generated files. Git: fixture revision; scope: server/.";
        await using (var db = OutputDistillationHarness.CreateContext())
            await db.SessionQueuedMessages.Where(m => m.Id == seed.NoteId)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.NoteHeader, annotatedHeader));
        seed = seed with { Header = annotatedHeader };
        seed.Task.NextHandoff.ShouldBe("Verify the implementation.");
        var attachPaths = DeliverableBundleService.ListAttachableFiles(seed.Task);
        attachPaths.Count.ShouldBe(2);
        foreach (var path in attachPaths) seed.Task.Result!.ShouldNotContain(path);
        await f.RunDistillerAsync(seed, Summary + anchors);
        var note = await f.DeliverAsync(seed);
        note.NoteHeader.ShouldBe(seed.Header); note.Body.ShouldStartWith(seed.Header);
        note.Body.ShouldContain(anchors); note.Body.ShouldContain(seed.Task.ResultFilePath!);
        foreach (var path in attachPaths)
            note.Body.Split("[[attach: " + path + "]]", StringSplitOptions.None).Length.ShouldBe(2);
        note.Body.ShouldNotContain("[[attach: " + seed.Task.ResultFilePath);
        note.ContentDigest.ShouldBe(seed.Digest);
    }

    [Test]
    [Arguments(1199)] [Arguments(1200)] [Arguments(1201)]
    [Arguments(3999)] [Arguments(4000)]
    public async Task Legacy_minimum_and_unicode_boundaries(int length)
    {
        await using var f = await DeliveryFixture.CreateAsync();
        if (length < 2000) f.Settings.DistillMinChars = 1200;
        var report = length < 2000 ? ReportOfLength(length) : new string('é', length - 2) + "🙂";
        var seed = await f.SettleAsync(report);
        seed.Task.Result!.Length.ShouldBe(length);
        (seed.Task.ResultFilePath is not null).ShouldBe(length >= f.Settings.DistillMinChars);
        var eligible = length >= f.Settings.DistillMinChars;
        (await f.RunDistillerAsync(seed, eligible ? Summary : null)).Outcome.ShouldBe(
            eligible ? DistillationOutcome.Applied : DistillationOutcome.SkippedShort);
        var note = await f.DeliverAsync(seed);
        if (eligible) note.Body.ShouldContain(Summary); else note.Body.ShouldBe(seed.RawBody);
    }

    [Test]
    [Arguments("root")] [Arguments("write")] [Arguments("publish")] [Arguments("readback")]
    [Arguments("denied")] [Arguments("budget")] [Arguments("path-too-long")]
    public async Task Storage_failures_leave_a_recoverable_settled_report(string scenario)
    {
        await using var f = await DeliveryFixture.CreateAsync();
        using var nonGit = new ReportTempWorkspace();
        if (scenario == "path-too-long") f.Settings.ReportStorageRoot = Path.Combine(f.Workspace.Main, ".antiphon",
            string.Join(Path.DirectorySeparatorChar, Enumerable.Repeat(new string('x', 80), 12)));
        var store = (AgentReportStore)f.Provider.GetRequiredService<IAgentReportStore>();
        store.BeforeIoAsync = async (phase, ct) => {
            if (scenario == phase) throw new IOException("fixture");
            if (scenario == "denied" && phase == "write") throw new UnauthorizedAccessException();
            if (scenario == "budget" && phase == "write") await Task.Delay(Timeout.Infinite, ct);
        };
        var seed = await f.SettleAsync(ReportOfLength(5500), cwd: scenario == "path-too-long" ? nonGit.Path : null);
        seed.Task.Status.ShouldBe(AgentTaskStatus.Succeeded); seed.Task.ResultFilePath.ShouldBeNull();
        await f.RunDistillerAsync(seed, Summary);
        var note = await f.DeliverAsync(seed);
        note.Body.ShouldContain("GET /api/agent-tasks/"); note.Body.ShouldContain(Summary);
    }

    [Test]
    public async Task Host_cancellation_during_storage_can_retry_settlement_once()
    {
        await using var f = await DeliveryFixture.CreateAsync(enabled: false);
        var parent = await AgentTaskSettlementRaceTests.SeedSessionAsync(f.Workspace.Main);
        var (task, session) = await AgentTaskSettlementRaceTests.SeedSharedTaskAsync(f.Workspace.Main, parent);
        var raw = ReportOfLength(5500);
        await AgentTaskSettlementRaceTests.SeedMarkedTurnAsync(session, task.Id, raw, "done");
        using var host = new CancellationTokenSource();
        var store = (AgentReportStore)f.Provider.GetRequiredService<IAgentReportStore>();
        store.BeforeIoAsync = (phase, ct) => {
            if (phase == "publish") { host.Cancel(); ct.ThrowIfCancellationRequested(); }
            return Task.CompletedTask;
        };
        await using (var canceled = f.Provider.CreateAsyncScope())
            await Should.ThrowAsync<OperationCanceledException>(() => canceled.ServiceProvider
                .GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(session, host.Token));
        store.BeforeIoAsync = null;
        await using (var retry = f.Provider.CreateAsyncScope())
            await retry.ServiceProvider.GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(session, CancellationToken.None);
        await using var db = OutputDistillationHarness.CreateContext();
        var settled = await db.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded); settled.Result.ShouldBe(raw);
        settled.ResultFilePath.ShouldNotBeNull();
        (await File.ReadAllTextAsync(settled.ResultFilePath!)).ShouldBe(raw);
        (await db.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == task.Id)).ShouldBe(1);
        Directory.EnumerateFiles(f.Workspace.Main, "*.tmp", SearchOption.AllDirectories).ShouldBeEmpty();
        await db.SessionQueuedMessages.Where(m => m.SourceTaskId == task.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, QueuedMessageStatus.Canceled));
    }

    [Test]
    public async Task File_read_does_not_claim_a_parent_api_poll()
    {
        await using var f = await DeliveryFixture.CreateAsync();
        var seed = await f.SettleAsync(ReportOfLength(5500));
        await f.RunDistillerAsync(seed, Summary); await f.DeliverAsync(seed);
        (await File.ReadAllTextAsync(seed.Task.ResultFilePath!)).ShouldBe(seed.Task.Result);
        await using var db = OutputDistillationHarness.CreateContext();
        (await db.AgentTasks.SingleAsync(t => t.Id == seed.Task.Id)).LastPolledResultHash.ShouldBeNull();
        (await db.OutputDistillations.SingleAsync(t => t.TaskId == seed.Task.Id)).FullReadAt.ShouldBeNull();
        await using var scope = f.Provider.CreateAsyncScope();
        var dto = await scope.ServiceProvider.GetRequiredService<AgentTaskService>()
            .GetAsync(seed.Task.Id, CancellationToken.None, pollingSessionId: seed.Task.ParentSessionId);
        dto.Result.ShouldBe(seed.Task.Result); dto.ResultFilePath.ShouldBe(seed.Task.ResultFilePath);
        await db.Entry(await db.AgentTasks.SingleAsync(t => t.Id == seed.Task.Id)).ReloadAsync();
        (await db.AgentTasks.SingleAsync(t => t.Id == seed.Task.Id)).LastPolledResultHash.ShouldBe(seed.Digest);
    }

    [Test]
    [Arguments("missing-path", true)] [Arguments("missing-next", true)] [Arguments("missing-handoff", true)]
    [Arguments("oversized", true)] [Arguments("empty", true)] [Arguments("failed", true)]
    [Arguments("missing-path", false)] [Arguments("missing-next", false)] [Arguments("missing-handoff", false)]
    [Arguments("oversized", false)] [Arguments("empty", false)] [Arguments("failed", false)]
    public async Task Fallback_delivers_original_raw_or_marked_excerpt(string scenario, bool modern)
    {
        await using var f = await DeliveryFixture.CreateAsync(modern: modern);
        var anchors = "\nserver/Program.cs\n--- next stage ---\nnext: review\nhandoff: Check the report carefully.";
        var report = ReportOfLength(5500 - anchors.Length) + anchors;
        var passing = Summary + anchors;
        OutputDistillationGate.Evaluate(report, passing, 1500, 0.6).Passed.ShouldBeTrue();
        var candidate = scenario switch {
            "missing-path" => passing.Replace("server/Program.cs", ""),
            "missing-next" => passing.Replace("next: review", ""),
            "missing-handoff" => passing.Replace("handoff: Check the report carefully.", ""),
            "oversized" => passing + new string('z', 1501 - passing.Length),
            "empty" => "", _ => passing };
        var seed = await f.SettleAsync(report);
        var ledger = await f.RunDistillerAsync(seed, candidate, scenario == "failed");
        ledger.Outcome.ShouldBe(scenario switch {
            "empty" => DistillationOutcome.DegradedEmpty, "failed" => DistillationOutcome.DegradedFailed,
            "oversized" => DistillationOutcome.RejectedUnderCompressed, _ => DistillationOutcome.RejectedOverCompressed });
        (await f.ReadNoteAsync(seed.NoteId)).HoldUntil.ShouldBeNull("rejection cleanup must release the original hold before delivery");
        var note = await f.DeliverAsync(seed);
        note.Body.ShouldBe(seed.RawBody); note.HoldUntil.ShouldBeNull();
        (await File.ReadAllTextAsync(seed.Task.ResultFilePath!)).ShouldBe(report);
    }

    internal sealed record Settled(AgentTask Task, Guid NoteId, string RawBody, string Header, string Digest, DateTime? HoldUntil);

    [Test]
    [Arguments("unavailable", true)] [Arguments("held", true)] [Arguments("backlog", true)]
    [Arguments("full", true)] [Arguments("closed", true)] [Arguments("missing", true)]
    [Arguments("expired", true)] [Arguments("timeout", true)]
    [Arguments("unavailable", false)] [Arguments("held", false)] [Arguments("backlog", false)]
    [Arguments("full", false)] [Arguments("closed", false)] [Arguments("missing", false)]
    [Arguments("expired", false)] [Arguments("timeout", false)]
    public async Task Admission_and_deadline_fallbacks_reach_parent(string scenario, bool modern)
    {
        await using var f = await DeliveryFixture.CreateAsync(modern: modern, configure: s => {
            if (scenario == "missing") s.RemoveAll<OutputDistillationQueue>();
            if (scenario == "unavailable") s.AddScoped<IModelAvailability>(_ => new UnavailableFixture());
        });
        var requests = f.Provider.GetService<OutputDistillationQueue>();
        if (scenario == "closed") requests!.Complete();
        if (scenario == "full")
            for (var n = 0; n < 3; n++) requests!.TryEnqueue(new(Guid.NewGuid(), null,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddSeconds(45), OutputDistillerMode.Apply)).ShouldBeTrue();
        if (scenario is "expired" or "timeout") f.Settings.OutputDistillerWaitSeconds = 2;
        await using var db = OutputDistillationHarness.CreateContext();
        var holdId = Guid.NewGuid();
        if (scenario == "held")
        {
            db.ModelAvailabilityHolds.Add(new() { Id = holdId, Kind = AgentKind.ClaudeCode,
                ModelAlias = "haiku", Source = ModelAvailabilitySource.Manual, HitAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        if (scenario == "backlog")
        {
            await using var scope = f.Provider.CreateAsyncScope();
            var seat = await scope.ServiceProvider.GetRequiredService<OutputDistillerProvisioner>().EnsureAsync(CancellationToken.None);
            seat.ShouldNotBeNull(); f.Settings.OutputDistillerMaxBacklog = 1;
            var id = Guid.NewGuid();
            db.AgentTasks.Add(new() { Id = id, RootTaskId = id, AgentId = seat.Id, AgentName = seat.Slug,
                Title = "existing queued specialist", Goal = "fixture", WorkingDirectory = f.Workspace.Main,
                Role = AgentTaskRole.Distill, Status = AgentTaskStatus.Queued, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        try
        {
            var seed = await f.SettleAsync(ReportOfLength(5500));
            if (scenario == "expired") await Task.Delay(2100);
            var record = scenario is "full" or "closed" or "missing"
                ? await db.OutputDistillations.AsNoTracking().SingleAsync(r => r.TaskId == seed.Task.Id)
                : await f.RunDistillerAsync(seed, scenario == "timeout" ? "" : null, leaveWorking: scenario == "timeout");
            record.Outcome.ShouldBe(scenario switch {
                "unavailable" => DistillationOutcome.DegradedUnavailable, "held" => DistillationOutcome.DegradedHeld,
                "expired" => DistillationOutcome.DegradedExpired, "timeout" => DistillationOutcome.DegradedTimeout,
                _ => DistillationOutcome.DegradedBusy });
            if (scenario != "timeout") record.DistillTaskId.ShouldBeNull();
            (await f.ReadNoteAsync(seed.NoteId)).HoldUntil.ShouldBeNull();
            (await f.DeliverAsync(seed)).Body.ShouldBe(seed.RawBody);
            (await File.ReadAllTextAsync(seed.Task.ResultFilePath!)).ShouldBe(seed.Task.Result);
        }
        finally { await db.ModelAvailabilityHolds.Where(h => h.Id == holdId).ExecuteDeleteAsync(); }
    }

    private sealed class UnavailableFixture : IModelAvailability
    {
        public Task<bool> IsHeldAsync(AgentKind kind, string alias, CancellationToken ct) => throw new IOException("fixture availability refusal");
    }

    [Test]
    public async Task Provider_recreation_releases_expired_raw_note_once()
    {
        await using var f = await DeliveryFixture.CreateAsync();
        f.Settings.OutputDistillerWaitSeconds = 1; // only this test request's original deadline
        var seed = await f.SettleAsync(ReportOfLength(5500));
        seed.HoldUntil.ShouldNotBeNull();
        await BridgeQueueHarness.InsertEntryAsync(seed.Task.ParentSessionId!.Value, TranscriptKinds.TurnEnd, null);
        await f.RecreateForRawFallbackAsync();
        await Task.Delay(1100);
        var note = await f.DeliverAsync(seed, seedBaseline: false);
        note.Body.ShouldBe(seed.RawBody);
        (await File.ReadAllTextAsync(seed.Task.ResultFilePath!)).ShouldBe(seed.Task.Result);
        // Normal worker recovery delivered without a new turn-end wakeup or SendNow.
        await using var db = OutputDistillationHarness.CreateContext();
        (await db.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == seed.Task.Id)).ShouldBe(1);
    }

    [Test]
    public async Task Expired_hold_is_deliverable_at_two_normal_flush_opportunities()
    {
        await using var f = await DeliveryFixture.CreateAsync();
        f.Settings.OutputDistillerWaitSeconds = 1;
        var seed = await f.SettleAsync(ReportOfLength(5500));
        var parent = seed.Task.ParentSessionId!.Value;
        await BridgeQueueHarness.InsertEntryAsync(parent, TranscriptKinds.TurnEnd, null);
        await f.RecreateForRawFallbackAsync();
        var adapter = new FakeAgentProtocolAdapter();
        adapter.OnSubmitted = body => BridgeQueueHarness.InsertEntryAsync(parent, TranscriptKinds.UserPrompt, body);
        f.Provider.GetRequiredService<AgentSessionRuntime>().Register(parent, adapter);
        await Task.Delay(1100);
        seed.HoldUntil!.Value.ShouldBeLessThan(DateTime.UtcNow);
        var queue = f.Provider.GetRequiredService<SessionMessageQueueService>();
        await queue.FlushIfIdleAsync(parent, CancellationToken.None);
        await queue.FlushIfIdleAsync(parent, CancellationToken.None);
        var note = await f.ReadNoteAsync(seed.NoteId);
        note.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered, "two completed normal flush opportunities must not strand an expired hold");
        note.DeliveryAttempts.ShouldBe(1);
        await using var db = OutputDistillationHarness.CreateContext();
        (await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == parent && t.Kind == TranscriptKinds.UserPrompt)).ShouldBe(1);
    }

    [Test]
    [Arguments(-1)] [Arguments(0)] [Arguments(1)]
    public async Task Composed_summary_and_metadata_obey_utf8_spill_guard(int delta)
    {
        await using var f = await DeliveryFixture.CreateAsync();
        var seed = await f.SettleAsync(ReportOfLength(5500), deliverables: true);
        var ceiling = f.Provider.GetRequiredService<PtyDeliveryProfile>().Ceilings.SingleWriteMaxBytes;
        var baseBody = DelegationReportFormatter.BuildDistilledNoteBody(seed.Header, Summary, seed.Task,
            seed.Task.ResultFilePath, DeliverableBundleService.ListAttachableFiles(seed.Task));
        var extra = ceiling + delta - System.Text.Encoding.UTF8.GetByteCount(baseBody) - 1;
        var header = seed.Header + "\n" + new string('é', extra / 2) + (extra % 2 == 1 ? "x" : "");
        await using (var db = OutputDistillationHarness.CreateContext())
            await db.SessionQueuedMessages.Where(m => m.Id == seed.NoteId).ExecuteUpdateAsync(s => s.SetProperty(m => m.NoteHeader, header));
        await f.RunDistillerAsync(seed, Summary);
        var applied = await f.ReadNoteAsync(seed.NoteId);
        System.Text.Encoding.UTF8.GetByteCount(applied.Body).ShouldBe(ceiling + delta);
        var delivered = await f.DeliverAsync(seed);
        delivered.Body.ShouldBe(applied.Body);
        var wire = await f.ReadNoteAsync(seed.NoteId);
        wire.Body.Contains(TypedBodySpill.PointerHeadline, StringComparison.Ordinal).ShouldBe(delta == 1);
        delivered.Body.ShouldContain("--- deliverable ---"); delivered.Body.ShouldContain(seed.Task.ResultFilePath!);
    }

    [Test]
    public async Task Same_root_applied_notes_batch_into_one_intact_spill_file()
    {
        await using var f = await DeliveryFixture.CreateAsync(modern: false, configure: s =>
            s.AddSingleton(Options.Create(new ChannelBridgeSettings { BatchingEnabled = true, DebounceWindowMs = 0 })));
        var first = await f.SettleAsync(ReportOfLength(5500));
        await f.RunDistillerAsync(first, Summary, keepWorker: true);
        var second = await f.SettleAsync(ReportOfLength(5501), parentId: first.Task.ParentSessionId, rootId: first.Task.RootTaskId);
        await f.RunDistillerAsync(second, Summary);
        var a = await f.ReadNoteAsync(first.NoteId); var b = await f.ReadNoteAsync(second.NoteId);
        a.ConversationKey.ShouldBe(b.ConversationKey);
        var ceiling = f.Provider.GetRequiredService<PtyDeliveryProfile>().Ceilings.SingleWriteMaxBytes;
        System.Text.Encoding.UTF8.GetByteCount(a.Body).ShouldBeLessThanOrEqualTo(ceiling);
        System.Text.Encoding.UTF8.GetByteCount(b.Body).ShouldBeLessThanOrEqualTo(ceiling);
        System.Text.Encoding.UTF8.GetByteCount(a.Body + b.Body).ShouldBeGreaterThan(ceiling);
        var delivered = await f.DeliverAsync(first);
        delivered.Body.ShouldContain(a.Body); delivered.Body.ShouldContain(b.Body);
        (await f.ReadNoteAsync(first.NoteId)).Body.ShouldBe((await f.ReadNoteAsync(second.NoteId)).Body);
        (await f.ReadNoteAsync(second.NoteId)).DeliveryAttempts.ShouldBe(1);
        (await f.ReadNoteAsync(second.NoteId)).DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
        foreach (var task in new[] { first.Task, second.Task })
            (await File.ReadAllTextAsync(task.ResultFilePath!)).ShouldBe(task.Result);
    }
    internal sealed class DeliveryFixture : IAsyncDisposable
    {
        public ReportWorkspace Workspace { get; } = new();
        public ServiceProvider Provider { get; private set; } = null!;
        public DelegationSettings Settings { get; private set; } = null!;
        private OutputDistillationHostedService? _worker;
        private CompletionNoteWorkHostedService? _flush;
        private readonly List<Guid> _parents = [];
        public static async Task<DeliveryFixture> CreateAsync(bool modern = true, OutputDistillerMode mode = OutputDistillerMode.Apply,
            bool enabled = true, bool worktree = false, Action<IServiceCollection>? configure = null)
        {
            var f = new DeliveryFixture(); await f.Workspace.InitializeAsync(worktree);
            f.Settings = new() { OutputDistillerEnabled = enabled, OutputDistillerMode = mode,
                OutputDistillerAgentSlug = "c419-" + Guid.NewGuid().ToString("N"),
                OutputDistillerWorkingDirectory = Path.Combine(f.Workspace.Main, ".antiphon", "specialist"), MaxConcurrentTasks = 512 };
            f.Provider = AgentTaskSettlementRaceTests.BuildHarness(s => {
                s.AddSingleton(Options.Create(f.Settings)); s.AddSingleton<ISessionRunnerClient, FakeSessionRunnerClient>();
                s.AddScoped<IModelAvailability, ModelAvailability>();
                s.AddSingleton(p => new PtyDeliveryProfile(p.GetRequiredService<IServiceScopeFactory>(),
                    NullLogger<PtyDeliveryProfile>.Instance, Options.Create(f.Settings), TimeProvider.System, modern ? "modern" : "inbox"));
                s.AddScoped(p => new OutputDistillerProvisioner(p.GetRequiredService<Antiphon.Server.Infrastructure.Data.AppDbContext>(),
                    Options.Create(f.Settings), TimeProvider.System, NullLogger<OutputDistillerProvisioner>.Instance,
                    claudeConfigJsonPath: Path.Combine(f.Workspace.Root, "test-claude.json")));
                s.AddScoped<OutputDistillationService>(); s.AddSingleton<OutputDistillationQueue>();
                s.AddSingleton<CompletionNoteFlushQueue>(); s.AddSingleton<SpecialistFailureQueue>();
                configure?.Invoke(s);
            });
            (await f.Provider.GetRequiredService<PtyDeliveryProfile>().RefreshAsync(CancellationToken.None))
                .ReplyInlineMaxChars.ShouldBe(modern ? 14400 : 3000);
            return f;
        }
        public async Task<Settled> SettleAsync(string report, bool failed = false, string? cwd = null, bool authorFile = false,
            bool deliverables = false, Guid? parentId = null, Guid? rootId = null)
        {
            cwd ??= Workspace.Main;
            var parent = parentId ?? await AgentTaskSettlementRaceTests.SeedSessionAsync(Workspace.Main); _parents.Add(parent);
            var (task, session) = await AgentTaskSettlementRaceTests.SeedSharedTaskAsync(cwd, parent);
            await using (var db = OutputDistillationHarness.CreateContext())
            {
                var row = await db.AgentTasks.SingleAsync(t => t.Id == task.Id);
                row.Role = AgentTaskRole.Review;
                if (rootId is Guid root) row.RootTaskId = root;
                row.RepoPath = cwd == Workspace.Worktree ? Workspace.Main : cwd;
                row.WorktreePath = cwd == Workspace.Worktree ? Workspace.Worktree : null;
                if (deliverables)
                {
                    row.DeliverableBundleDir = Path.Combine(Workspace.Main, ".antiphon", "bundle");
                    Directory.CreateDirectory(row.DeliverableBundleDir);
                    await File.WriteAllTextAsync(Path.Combine(row.DeliverableBundleDir, "first.md"), "first deliverable");
                    await File.WriteAllTextAsync(Path.Combine(row.DeliverableBundleDir, "second.md"), "second deliverable");
                    row.DeliverableFileCount = 2;
                }
                await db.SaveChangesAsync();
            }
            if (authorFile)
            {
                var file = Path.Combine(cwd, ".antiphon", $"task-{DelegationReportFormatter.Short(task.Id)}.md");
                Directory.CreateDirectory(Path.GetDirectoryName(file)!); await File.WriteAllTextAsync(file, "delegate authored different detail");
            }
            await AgentTaskSettlementRaceTests.SeedMarkedTurnAsync(session, task.Id, report, failed ? "failed" : "done");
            await Provider.GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(session, CancellationToken.None);
            await using var read = OutputDistillationHarness.CreateContext();
            var stored = await read.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
            stored.Result.ShouldBe(report);
            var note = (await read.SessionQueuedMessages.Where(m => m.SourceTaskId == task.Id).ToListAsync()).ShouldHaveSingleItem();
            return new(stored, note.Id, note.Body, note.NoteHeader!, note.ContentDigest!, note.HoldUntil);
        }
        public async Task<OutputDistillationRecord> RunDistillerAsync(Settled seed, string? summary, bool failed = false,
            bool leaveWorking = false, bool keepWorker = false)
        {
            if (_worker is null)
            {
                _worker = new(Provider.GetRequiredService<OutputDistillationQueue>(), Provider.GetRequiredService<IServiceScopeFactory>(),
                    Options.Create(Settings), NullLogger<OutputDistillationHostedService>.Instance);
                await _worker.StartAsync(CancellationToken.None);
            }
            if (summary is not null)
            {
                await UntilAsync(async () => {
                    await using var db = OutputDistillationHarness.CreateContext();
                    var task = await db.AgentTasks.SingleOrDefaultAsync(t => t.AgentName == Settings.OutputDistillerAgentSlug
                        && t.Role == AgentTaskRole.Distill && t.Status == AgentTaskStatus.Queued);
                    if (task is null)
                    {
                        var early = await db.OutputDistillations.SingleOrDefaultAsync(d => d.TaskId == seed.Task.Id);
                        early.ShouldBeNull("an eligible source must create a specialist task; an early skip/refusal is not an attempt");
                        return false;
                    }
                    if (leaveWorking)
                    {
                        task.Status = AgentTaskStatus.Working; task.DispatchedAt = DateTime.UtcNow;
                        await db.SaveChangesAsync(); return true;
                    }
                    task.Status = failed ? AgentTaskStatus.Failed : AgentTaskStatus.Succeeded; task.Result = summary;
                    task.CompletedAt = DateTime.UtcNow; await db.SaveChangesAsync(); return true;
                });
            }
            OutputDistillationRecord? record = null;
            await UntilAsync(async () => {
                await using var db = OutputDistillationHarness.CreateContext();
                record = await db.OutputDistillations.AsNoTracking().SingleOrDefaultAsync(r => r.TaskId == seed.Task.Id);
                return record is not null;
            });
            if (!keepWorker) { await _worker.StopAsync(CancellationToken.None); _worker.Dispose(); _worker = null; }
            return record!;
        }
        public async Task<SessionQueuedMessage> ReadNoteAsync(Guid id)
        {
            await using var db = OutputDistillationHarness.CreateContext();
            return await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == id);
        }
        public async Task RecreateForRawFallbackAsync()
        {
            await Provider.DisposeAsync();
            Provider = AgentTaskSettlementRaceTests.BuildHarness(s => {
                s.AddSingleton(Options.Create(Settings));
                s.AddSingleton<CompletionNoteFlushQueue>(); s.AddSingleton<SpecialistFailureQueue>();
            });
        }
        public async Task<SessionQueuedMessage> DeliverAsync(Settled seed, bool seedBaseline = true)
        {
            var parent = seed.Task.ParentSessionId!.Value;
            if (seedBaseline) await BridgeQueueHarness.InsertEntryAsync(parent, TranscriptKinds.TurnEnd, null);
            var adapter = new FakeAgentProtocolAdapter();
            adapter.OnSubmitted = body => BridgeQueueHarness.InsertEntryAsync(parent, TranscriptKinds.UserPrompt, body);
            Provider.GetRequiredService<AgentSessionRuntime>().Register(parent, adapter);
            _flush = new(Provider.GetRequiredService<IServiceScopeFactory>(), Provider.GetRequiredService<CompletionNoteFlushQueue>(),
                Provider.GetRequiredService<SpecialistFailureQueue>(), TimeProvider.System, NullLogger<CompletionNoteWorkHostedService>.Instance);
            await _flush.StartAsync(CancellationToken.None);
            try { await UntilAsync(async () => (await ReadNoteAsync(seed.NoteId)).DeliveryVerdict == DeliveryVerdict.Delivered); }
            catch (OperationCanceledException)
            {
                var stuck = await ReadNoteAsync(seed.NoteId);
                throw new InvalidOperationException($"Delivery fixture did not confirm: status={stuck.Status} attempts={stuck.DeliveryAttempts} verdict={stuck.DeliveryVerdict} hold={stuck.HoldUntil:O}");
            }
            await _flush.StopAsync(CancellationToken.None); _flush.Dispose(); _flush = null;
            var note = await ReadNoteAsync(seed.NoteId);
            await using var db = OutputDistillationHarness.CreateContext();
            var prompts = await db.TranscriptEntries.Where(t => t.AgentSessionId == parent && t.Kind == TranscriptKinds.UserPrompt).ToListAsync();
            prompts.ShouldHaveSingleItem().Text.ShouldBe(note.Body);
            note.DeliveryAttempts.ShouldBe(1); adapter.KillCount.ShouldBe(0);
            // Outer terminal spill is separate from the canonical raw report and its inner note.
            if (note.Body.Contains(TypedBodySpill.PointerHeadline, StringComparison.Ordinal))
                note.Body = await File.ReadAllTextAsync(TypedBodySpill.InboxAbsolutePath(Workspace.Main, note.Id.ToString("D")));
            return note;
        }
        public async ValueTask DisposeAsync()
        {
            if (_worker is not null) { await _worker.StopAsync(CancellationToken.None); _worker.Dispose(); }
            if (_flush is not null) { await _flush.StopAsync(CancellationToken.None); _flush.Dispose(); }
            await using (var db = OutputDistillationHarness.CreateContext())
                await db.SessionQueuedMessages.Where(m => _parents.Contains(m.AgentSessionId) && m.Status == QueuedMessageStatus.Pending)
                    .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, QueuedMessageStatus.Canceled));
            await Provider.DisposeAsync(); await Workspace.DisposeAsync();
        }
        internal static async Task UntilAsync(Func<Task<bool>> predicate)
        {
            using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (!await predicate()) await Task.Delay(50, watchdog.Token);
        }
    }
}
