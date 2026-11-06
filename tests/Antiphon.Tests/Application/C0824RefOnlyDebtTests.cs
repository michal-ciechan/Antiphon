using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(80_000)] // CARD-0824 provisional restart/claim/save pack; first required samples retained.
public sealed class C0824RefOnlyDebtTests
{
    [Test]
    public async Task Absent_identity_directory_does_not_supersede_debt()
    {
        foreach (var cut in new[] { "none", "claimed", "synced", "saved" })
        {
            await using var w = await CreateAsync();
            var source = await w.Git.RunnerPushAsync("owed.txt", "immutable source " + cut);
            await w.MintRefOnlyDebtAsync(RunnerSettlementWorld.Report("Implemented. " + w.Claim(source)));
            if (cut == "none")
            {
                Directory.CreateDirectory(w.Task.WorktreePath!);
                await File.WriteAllTextAsync(Path.Combine(w.Task.WorktreePath!, "foreign.txt"), "foreign identity bytes");
            }
            else Directory.Exists(w.Task.WorktreePath!).ShouldBeFalse("absent identity is expected");
            var history = await HistoryAsync(w);
            var fired = false;
            await using (var scope = w.Services.CreateAsyncScope())
            {
                var recovery = scope.ServiceProvider.GetRequiredService<SettlementSyncRecoveryService>();
                recovery.BoundaryAsync = (site, _) =>
                {
                    if (site != cut) return Task.CompletedTask;
                    fired = true;
                    throw new IOException("owned crash at " + cut);
                };
                await recovery.SweepAsync(default);
            }
            if (cut != "none") fired.ShouldBeTrue("reached:D1:" + cut);
            var debt = await DebtAsync(w);
            debt.State.ShouldNotBe(AgentTaskSyncDebtState.Superseded, "absence-never-supersedes:" + cut);
            if (cut is "claimed" or "synced")
            {
                debt.State.ShouldBe(AgentTaskSyncDebtState.Pending, "crash-remains-owed:" + cut);
                await w.RestartServicesAsync();
                w.DebtClock.Advance(TimeSpan.FromMinutes(2));
                (await w.SweepSettlementSyncAsync()).ShouldBe(1);
                debt = await DebtAsync(w);
            }
            debt.State.ShouldBe(AgentTaskSyncDebtState.Ready, "debt-ready=S:" + cut);
            debt.ConfirmedSha.ShouldBe(source, "debt-ready=S:" + cut);
            (await w.Git.RevParseAsync(w.Git.FullRef)).ShouldBe(source);
            (await w.Git.RunAsync(w.Git.Desktop, "show", source + ":owed.txt")).ShouldBe("immutable source " + cut);
            (await HistoryAsync(w)).ShouldBe(history, "settlement-history-immutable:" + cut);
            if (cut == "none")
                (await File.ReadAllTextAsync(Path.Combine(w.Task.WorktreePath!, "foreign.txt"))).ShouldBe("foreign identity bytes");
            else Directory.Exists(w.Task.WorktreePath!).ShouldBeFalse("no-identity-materialization");
            (await w.SweepSettlementSyncAsync()).ShouldBe(0, "recovery-is-idempotent");
            await using var detailScope = w.Services.CreateAsyncScope();
            var detail = await detailScope.ServiceProvider.GetRequiredService<AgentTaskService>().GetAsync(w.TaskId, default);
            detail.SyncDebt.ShouldNotBeNull().State.ShouldBe(nameof(AgentTaskSyncDebtState.Ready));
            AssertNoCheckout(w);
        }
    }

    [Test]
    public async Task Recovery_confirms_only_recorded_sha_and_attempt()
    {
        await using var w = await CreateAsync();
        var source = await w.Git.RunnerPushAsync("owed.txt", "recorded S");
        await w.MintRefOnlyDebtAsync(RunnerSettlementWorld.Report("Implemented. " + w.Claim(source)));
        var original = w.Task;
        var baseline = TaskProgressJson.TryReadBaseline(original.ProgressBaselineJson)!;
        var initialDebt = await DebtAsync(w);
        var history = await HistoryAsync(w);
        foreach (var phase in new[] { "claim", "save" })
        foreach (var dimension in new[] { "attempt", "mode", "binding", "revision", "baseline", "path", "runner", "remote-path", "repository", "binding-revision", "debt-revision" })
        {
            await ResetAsync(w, initialDebt);
            async Task CorruptAsync()
            {
                await using var db = w.CreateContext();
                var row = await db.AgentTasks.SingleAsync(t => t.Id == w.TaskId);
                switch (dimension)
                {
                    case "attempt": row.Attempt++; break;
                    case "mode": row.DesktopWorkspaceKind = DesktopWorkspaceKind.Checkout; break;
                    case "binding": row.TaskRefWorkspaceBindingId = Guid.NewGuid(); break;
                    case "revision": row.ConcurrencyToken = Guid.NewGuid(); break;
                    case "baseline": row.ProgressBaselineJson = TaskProgressJson.SerializeBaseline(baseline with { CapturedAt = baseline.CapturedAt.AddSeconds(1) }); break;
                    case "path": row.WorktreePath += "-other"; break;
                    case "runner": row.RunnerId = "different-runner"; break;
                    case "remote-path": row.RemoteWorktreePath += "-other"; break;
                    case "repository": row.RepoPath = w.Git.Runner; break;
                    case "binding-revision":
                        var binding = await db.TaskRefWorkspaceBindings.SingleAsync(b => b.Id == original.TaskRefWorkspaceBindingId);
                        binding.Revision++; break;
                    case "debt-revision":
                        var debt = await db.AgentTaskSyncDebts.SingleAsync(d => d.Id == initialDebt.Id);
                        debt.Revision++; debt.State = AgentTaskSyncDebtState.Held; break;
                }
                await db.SaveChangesAsync();
            }
            if (phase == "claim") await CorruptAsync();
            w.Git.Git.Clear();
            var reached = false;
            await using (var scope = w.Services.CreateAsyncScope())
            {
                var recovery = scope.ServiceProvider.GetRequiredService<SettlementSyncRecoveryService>();
                recovery.BoundaryAsync = async (site, _) =>
                {
                    if (site != "synced") return;
                    reached = true;
                    if (phase == "save") await CorruptAsync();
                };
                await recovery.SweepAsync(default);
            }
            var result = await DebtAsync(w);
            if (phase == "claim")
            {
                reached.ShouldBeFalse("no-claim-Git-crossing:" + dimension);
                w.Git.Git.Commands.ShouldBeEmpty("claim-refused-before-Git:" + dimension);
            }
            else reached.ShouldBeTrue("reached:D2:" + dimension + ":save");
            result.State.ShouldNotBe(AgentTaskSyncDebtState.Ready, "debt-not-ready:" + dimension + ":" + phase);
            result.ConfirmedSha.ShouldBeNull("no-false-confirmed-source:" + dimension);
            (await HistoryAsync(w)).ShouldBe(history, "settlement-history-immutable");
            await using (var db = w.CreateContext())
            {
                var row = await db.AgentTasks.SingleAsync(t => t.Id == w.TaskId);
                row.Attempt = original.Attempt; row.DesktopWorkspaceKind = original.DesktopWorkspaceKind;
                row.TaskRefWorkspaceBindingId = original.TaskRefWorkspaceBindingId; row.ConcurrencyToken = original.ConcurrencyToken;
                row.ProgressBaselineJson = original.ProgressBaselineJson; row.WorktreePath = original.WorktreePath;
                row.RunnerId = original.RunnerId; row.RemoteWorktreePath = original.RemoteWorktreePath; row.RepoPath = original.RepoPath;
                var binding = await db.TaskRefWorkspaceBindings.SingleAsync(b => b.Id == original.TaskRefWorkspaceBindingId);
                binding.Revision = 1;
                await db.SaveChangesAsync();
            }
        }
        await ResetAsync(w, initialDebt);
        var advanced = await w.Git.RunnerPushAsync("newer.txt", "not the recorded source");
        (await w.SweepSettlementSyncAsync()).ShouldBe(1);
        (await DebtAsync(w)).State.ShouldBe(AgentTaskSyncDebtState.Held, "debt-not-ready:newer-origin");
        (await w.Git.RevParseAsync(w.Git.FullRef)).ShouldBe(w.Git.Baseline);
        await w.Git.RunAsync(w.Git.Runner, "push", "--force", "origin", source + ":" + w.Git.FullRef);
        await ResetAsync(w, initialDebt);
        (await w.SweepSettlementSyncAsync()).ShouldBe(1);
        (await DebtAsync(w)).ConfirmedSha.ShouldBe(source, "valid-exact-S-recovers");
        advanced.ShouldNotBe(source);
        AssertNoCheckout(w);
    }

    [Test]
    public async Task Disabling_gate_keeps_existing_recovery_alive()
    {
        await using var w = await CreateAsync();
        var source = await w.Git.RunnerPushAsync("owed.txt", "flag-off debt");
        await w.MintRefOnlyDebtAsync(RunnerSettlementWorld.Report("Implemented. " + w.Claim(source)));
        var debt = await DebtAsync(w);
        await w.RestartServicesAsync();
        var settings = w.Services.GetRequiredService<IOptions<DelegationSettings>>().Value;
        var gate = w.Services.GetRequiredService<IOptions<RefOnlyDesktopSettings>>().Value;
        gate.Enabled.ShouldBeFalse();
        gate.RunnerIds.ShouldBeEmpty();
        gate.Roles.ShouldBe("none");
        settings.RunnerSyncDebtOnSettlement = false;
        await using (var db = w.CreateContext())
        {
            await db.AgentTaskSyncDebts.Where(d => d.Id == debt.Id).ExecuteUpdateAsync(u => u
                .SetProperty(d => d.NextAttemptAt, w.DebtClock.GetUtcNow().UtcDateTime.AddMinutes(5)));
        }
        w.Git.Git.Clear();
        (await w.SweepSettlementSyncAsync()).ShouldBe(0, "not-due-no-Git");
        w.Git.Git.Commands.ShouldBeEmpty();
        await ResetAsync(w, debt, AgentTaskSyncDebtState.Held);
        (await w.SweepSettlementSyncAsync()).ShouldBe(0, "Held-no-Git");
        w.Git.Git.Commands.ShouldBeEmpty();
        (await DebtAsync(w)).State.ShouldBe(AgentTaskSyncDebtState.Held);
        await ResetAsync(w, debt);
        (await w.SweepSettlementSyncAsync()).ShouldBe(1);
        (await DebtAsync(w)).State.ShouldBe(AgentTaskSyncDebtState.Ready, "existing-mode-retained");
        (await w.Git.RevParseAsync(w.Git.FullRef)).ShouldBe(source);
        var baseline = TaskProgressJson.TryReadBaseline(w.Task.ProgressBaselineJson)!;
        await using (var db = w.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == w.TaskId);
            task.ProgressBaselineJson = TaskProgressJson.SerializeBaseline(baseline with { SchemaVersion = 1,
                Primary = baseline.Primary with { DesktopWorkspaceKind = DesktopWorkspaceKind.Checkout, TaskRefWorkspaceBindingId = null } });
            await db.SaveChangesAsync();
        }
        await ResetAsync(w, debt);
        (await w.SweepSettlementSyncAsync()).ShouldBe(0, "invalid-RefOnly-baseline-refused-before-claim");
        (await DebtAsync(w)).State.ShouldBe(AgentTaskSyncDebtState.Held);
        await using var checkout = await RunnerSettlementWorld.CreateAsync();
        var v1 = TaskProgressJson.TryReadBaseline(checkout.Task.ProgressBaselineJson)!;
        v1.SchemaVersion.ShouldBe(1);
        var roundTrip = TaskProgressJson.SerializeBaseline(v1);
        roundTrip.ShouldNotContain("desktopWorkspaceKind");
        TaskProgressJson.TryReadBaseline(roundTrip).ShouldBe(v1, "v1-checkout-roundtrip");
        var result = await checkout.Git.Service().SyncAsync(checkout.Task, default);
        result.Confirmed.ShouldBeTrue("v1-checkout-outcomes-retained");
        result.DesktopWorkspaceKind.ShouldBe(DesktopWorkspaceKind.Checkout);
        AssertNoCheckout(w);
    }

    private static async Task<RunnerSettlementWorld> CreateAsync()
    {
        var w = await RunnerSettlementWorld.CreateAsync(controlledSyncClock: true);
        try { await w.RecordRefOnlyAsync(); return w; }
        catch { await w.DisposeAsync(); throw; }
    }

    private static async Task<AgentTaskSyncDebt> DebtAsync(RunnerSettlementWorld w)
    {
        await using var db = w.CreateContext();
        return await db.AgentTaskSyncDebts.AsNoTracking().SingleAsync(d => d.TaskId == w.TaskId);
    }

    private static async Task<string> HistoryAsync(RunnerSettlementWorld w)
    {
        await using var db = w.CreateContext();
        var row = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == w.TaskId);
        var events = await db.AgentTaskEvents.AsNoTracking().Where(e => e.AgentTaskId == w.TaskId).OrderBy(e => e.Id).ToArrayAsync();
        return JsonSerializer.Serialize(new { row.Status, row.Result, row.CompletionProgressEvidenceJson,
            Events = events.Select(e => new { e.Id, e.Type, e.Detail, e.At }) });
    }

    private static async Task ResetAsync(RunnerSettlementWorld w, AgentTaskSyncDebt original,
        AgentTaskSyncDebtState state = AgentTaskSyncDebtState.Pending)
    {
        await using var db = w.CreateContext();
        await db.AgentTaskSyncDebts.Where(d => d.Id == original.Id).ExecuteUpdateAsync(u => u
            .SetProperty(d => d.State, state).SetProperty(d => d.Revision, original.Revision)
            .SetProperty(d => d.Attempts, 0).SetProperty(d => d.ConfirmedSha, (string?)null)
            .SetProperty(d => d.SourceReadyAt, (DateTime?)null).SetProperty(d => d.NextAttemptAt, w.DebtClock.GetUtcNow().UtcDateTime));
        await w.Git.RunAsync(w.Git.Desktop, "update-ref", w.Git.FullRef, w.Git.Baseline);
    }

    private static void AssertNoCheckout(RunnerSettlementWorld w)
    {
        w.Git.Git.WorkingDirectories.ShouldNotContain(w.Task.WorktreePath!);
        w.Git.Git.Commands.ShouldNotContain(c => c.StartsWith("worktree add ", StringComparison.Ordinal)
            || c.StartsWith("worktree prune", StringComparison.Ordinal) || c.StartsWith("reset ", StringComparison.Ordinal)
            || c.StartsWith("checkout ", StringComparison.Ordinal) || c.Contains("--ff-only", StringComparison.Ordinal));
    }
}
