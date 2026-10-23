using System.Data.Common;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0822. Covered writes regenerate the instructions file and deliver one WhenIdle
/// System note. The harness does not register the hosted sweep.
/// </summary>
[Category("Integration")]
[NotInParallel("MessageQueue")]
public class OrchestratorInstructionsRefreshTests
{
    [Test]
    [Arguments("chain-default", false)]
    [Arguments("chain-default", true)]
    [Arguments("chain-other", false)]
    [Arguments("chain-other", true)]
    [Arguments("stage pin", false)]
    [Arguments("stage pin", true)]
    [Arguments("availability", false)]
    [Arguments("availability", true)]
    [Arguments("policy", false)]
    [Arguments("policy", true)]
    [Arguments("chain metadata", false)]
    [Arguments("chain metadata", true)]
    [Arguments("standing agent", false)]
    [Arguments("standing agent", true)]
    [Arguments("holds", false)]
    [Arguments("holds", true)]
    [Arguments("pins", false)]
    [Arguments("pins", true)]
    [Arguments("defaults", false)]
    [Arguments("defaults", true)]
    [Arguments("budget", false)]
    [Arguments("budget", true)]
    [Arguments("runners", false)]
    [Arguments("runners", true)]
    public async Task Failed_reads_are_unknown_in_file_and_notice_then_recover(string part, bool notice)
    {
        var fault = new SnapshotReadFault(part);
        await using var h = await CreateHarnessAsync(fault);
        h.Delegation.OrchestratorInstructions.Path = Path.Combine(h.TempRoot, "i.md");
        await AttachBundleAsync(h);
        h.Delegation.OrchestratorInstructions.Notify = OrchestratorInstructionsNotify.Off;
        await using (var scope = h.Provider.CreateAsyncScope())
        {
            var chains = scope.ServiceProvider.GetRequiredService<ComplexityChainService>();
            await chains.UpsertAsync(AgentTaskRole.Plan,
                part == "chain-other" ? TaskComplexity.Hard : TaskComplexity.Medium,
                new PutComplexityChainRequest([new(AgentKind.ClaudeCode, AgentModelLevel.Low)],
                    RoutingPinProvenance.Human, "D3 witness"), null, CancellationToken.None);
            if (part is "stage pin" or "standing agent")
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.RoutingPins.Add(new RoutingPin
                {
                    Id = Guid.NewGuid(), Role = AgentTaskRole.Plan,
                    AgentId = part == "standing agent" ? h.AgentId : null,
                    AgentKind = part == "stage pin" ? AgentKind.ClaudeCode : null,
                    ModelLevel = part == "stage pin" ? AgentModelLevel.High : null,
                    Strength = RoutingPinStrength.Required, Provenance = RoutingPinProvenance.Human,
                    Reason = "D3 stage pin", CreatedAt = h.Now, UpdatedAt = h.Now,
                });
                await db.SaveChangesAsync();
            }
        }
        await Instructions(h).WhenIdleAsync();
        await Instructions(h).ReconcileNowAsync("D3 baseline");
        var before = await SnapshotAsync(h);
        var beforeState = await ReadStateAsync(h);
        h.Delegation.OrchestratorInstructions.Notify = OrchestratorInstructionsNotify.All;
        fault.Enabled = true;
        await Instructions(h).ReconcileNowAsync("D3 failed read");
        fault.Failures.ShouldBeGreaterThan(0);
        var failed = await ReadStateAsync(h);
        failed.Revision.ShouldBe(beforeState.Revision + 1);
        var after = await SnapshotAsync(h);
        var delta = OrchestratorInstructionsDelta.Format(before, after);
        var reason = part.StartsWith("chain-", StringComparison.Ordinal) ? "chain" :
            part == "budget" ? "policy" : part;
        var unknown = $"unknown ({reason} read failed)";
        var file = await File.ReadAllTextAsync(failed.WrittenPath);
        var subject = (notice ? delta : file).Replace("\r\n", "\n", StringComparison.Ordinal);
        subject.ShouldContain(unknown);
        subject.ShouldNotContain("→ rolePolicy");
        subject.ShouldNotContain("→ unset");
        subject.ShouldNotContain("InvalidOperationException");
        delta.ShouldNotContain(" removed");
        file.ShouldContain("default Medium;");
        h.Adapter.SubmittedBodies.Count.ShouldBe(1);
        h.Adapter.SubmittedBodies[0].ShouldContain(unknown);
        h.Adapter.SubmittedBodies[0].ShouldNotContain("→ rolePolicy");
        h.Adapter.SubmittedBodies[0].ShouldNotContain("→ unset");
        if (part is "chain-default" or "stage pin")
        {
            var source = part == "stage pin" ? "stage pin" : "role";
            subject.ShouldContain(notice
                ? $"matrix: Plan source {source} → {unknown}"
                : $"  Plan: effective {unknown} via {unknown}");
        }
        if (part == "chain-other")
        {
            subject.ShouldContain(notice
                ? "matrix: Plan Hard ClaudeCode/Low (haiku) → unknown (chain read failed)"
                : "- Plan: Hard unknown (chain read failed); Medium (default) unset;");
            delta.ShouldNotContain("Plan source");
        }
        if (part == "policy")
            subject.ShouldContain(notice ? "caps: read observed → unknown (policy read failed)"
                : "## 1. Pipeline caps\nunknown (policy read failed)");

        // Exception type is diagnostic detail, not another settings transition.
        fault.AlternateException = true;
        await Instructions(h).ReconcileNowAsync("D3 same unavailable read, different exception");
        h.Adapter.SubmittedBodies.Count.ShouldBe(1);
        (await ReadStateAsync(h)).Revision.ShouldBe(failed.Revision);
        fault.Enabled = false;
        await Instructions(h).ReconcileNowAsync("D3 read recovered");
        var recovered = await ReadStateAsync(h);
        recovered.Revision.ShouldBe(failed.Revision + 1);
        h.Adapter.SubmittedBodies.Count.ShouldBe(2);
        var recovery = OrchestratorInstructionsDelta.Format(after, await SnapshotAsync(h));
        if (part is "chain-default" or "stage pin")
            recovery.ShouldContain($"matrix: Plan source {unknown} → {(part == "stage pin" ? "stage pin" : "role")}");
        recovery.ShouldContain(unknown + " → ");
        if (part == "policy")
            recovery.ShouldContain("caps: read unknown (policy read failed) → MaxOpenTasks 6; local budget 2 (config)");
        (await File.ReadAllTextAsync(recovered.WrittenPath)).ShouldNotContain("unknown (");
        await Instructions(h).ReconcileNowAsync("D3 stable recovery");
        h.Adapter.SubmittedBodies.Count.ShouldBe(2);
    }

    private static void AssertBoundedChainNotice(string notice, string delta, string path)
    {
        notice.Length.ShouldBeLessThanOrEqualTo(700);
        notice.ShouldContain("unknown (chain read failed)");
        notice.ShouldContain("ClaudeCode/Low (haiku)");
        notice.ShouldNotContain("→ unset");
        notice.ShouldContain(path);
        notice.ShouldContain("GET /api/orchestrator-instructions");
        const string separator = ". Re-read ANTIPHON_ORCHESTRATOR_INSTRUCTIONS (";
        var start = notice.IndexOf("): ", StringComparison.Ordinal);
        var end = notice.IndexOf(separator, StringComparison.Ordinal);
        start.ShouldBeGreaterThan(0);
        end.ShouldBeGreaterThan(start + 3);
        var visible = notice[(start + 3)..end];
        delta.StartsWith(visible, StringComparison.Ordinal).ShouldBeTrue("the notice retains a prefix of the complete delta");
        // Derive available delta space from this real wrapper, including its absolute path.
        var room = 700 - (notice.Length - visible.Length);
        if (delta.Length <= room)
            visible.ShouldBe(delta);
        else
        {
            visible.Length.ShouldBeLessThan(delta.Length);
            (room - visible.Length).ShouldBeLessThan(40, "the existing wrapper clips in 40-character steps");
        }
    }

    private static async Task<OrchestratorInstructionsSnapshot> SnapshotAsync(BridgeQueueHarness h)
    {
        await using var scope = h.Provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<OrchestratorInstructionsSnapshotBuilder>()
            .BuildAsync(CancellationToken.None);
    }

    private sealed class SnapshotReadFault(string part) : DbCommandInterceptor
    {
        public bool Enabled { get; set; }
        public bool AlternateException { get; set; }
        public int Failures { get; private set; }
        private int _chainReads;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            var sql = command.CommandText;
            var parameters = command.Parameters.Cast<DbParameter>().ToArray();
            bool Table(string name) => sql.Contains($"FROM \"{name}\"", StringComparison.Ordinal);
            bool Parameter(string name, int value) => parameters.Any(p => p.ParameterName.Contains(name, StringComparison.Ordinal)
                && p.Value is not DBNull && Convert.ToInt32(p.Value) == value);
            var planChain = Table("ComplexityChains") && Parameter("role", (int)AgentTaskRole.Plan)
                && Parameter("complexity", (int)(part == "chain-other" ? TaskComplexity.Hard : TaskComplexity.Medium));
            var fails = Enabled && part switch
            {
                "chain-default" or "chain-other" => planChain,
                // GetEffectiveAsync reads the cell's metadata and token before its candidates (CARD-1275).
                "chain metadata" => planChain && ++_chainReads % 2 == 1,
                "stage pin" => Table("RoutingPins") && Parameter("role", (int)AgentTaskRole.Plan),
                "standing agent" => Table("Agents") && sql.Contains("a.\"Kind\"", StringComparison.Ordinal)
                    && !sql.Contains("a.\"Status\"", StringComparison.Ordinal),
                "availability" => Table("ModelAvailabilityHolds") && parameters.Any(p => p.ParameterName.Contains("canonical", StringComparison.OrdinalIgnoreCase)),
                "policy" => Table("DispatchConcurrencySettings"),
                "holds" => Table("ModelAvailabilityHolds") && parameters.All(p => !p.ParameterName.Contains("canonical", StringComparison.OrdinalIgnoreCase)),
                "pins" => Table("RoutingPins") && parameters.All(p => !p.ParameterName.Contains("role", StringComparison.Ordinal)),
                "defaults" => Table("RunnerRoutingSettings"),
                "budget" => Table("HostBudgets"),
                "runners" => Table("AgentTasks") && sql.Contains("count(*)", StringComparison.Ordinal),
                _ => false,
            };
            if (fails)
            {
                Failures++;
                if (AlternateException) throw new ArgumentException("D3 read unavailable");
                throw new InvalidOperationException("D3 read unavailable");
            }
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    [Test]
    [Arguments(TaskComplexity.Hard)]
    [Arguments(TaskComplexity.Medium)]
    [Arguments(TaskComplexity.Easy)]
    [Arguments(null)]
    public async Task A_failed_chain_read_marks_the_affected_file_cells_unknown(TaskComplexity? tier)
    {
        var fault = new ChainReadFault(tier);
        await using var h = await CreateReadFailureHarnessAsync(fault);
        fault.Enabled = true;
        await Instructions(h).ReconcileNowAsync("chain read failure");

        var state = await ReadStateAsync(h);
        var file = await File.ReadAllTextAsync(state.WrittenPath);
        // Exercise Windows rendering on every host. The byte bound covers content, not CRLF.
        var line = MatrixLine(file.ReplaceLineEndings("\r\n"));
        fault.Failures.ShouldBe(fault.Tiers.Length * ChainReadFault.ReadsPerCell);
        foreach (var affected in fault.Tiers)
            line.ShouldContain($"{affected}{(affected == TaskComplexity.Medium ? " (default)" : "")} unknown (chain read failed)");
        Encoding.UTF8.GetByteCount(line).ShouldBeLessThanOrEqualTo(160);
        line.ShouldNotContain("\uFFFD");
        state.Body.ShouldContain(line);

        await using var scope = h.Provider.CreateAsyncScope();
        var snapshot = await scope.ServiceProvider.GetRequiredService<OrchestratorInstructionsSnapshotBuilder>()
            .BuildAsync(CancellationToken.None);
        var body = OrchestratorInstructionsRenderer.Render(snapshot);
        var cap = Encoding.UTF8.GetByteCount(body) - 1;
        var fitted = OrchestratorInstructionsRenderer.Render(snapshot, cap);
        Encoding.UTF8.GetByteCount(fitted).ShouldBeLessThanOrEqualTo(cap);
        fitted.ShouldContain("… 14 rows: GET /api/complexity-chains");
        fitted.ShouldNotContain("- Plan:");
    }

    [Test]
    [Arguments(TaskComplexity.Hard)]
    [Arguments(TaskComplexity.Medium)]
    [Arguments(TaskComplexity.Easy)]
    [Arguments(null)]
    public async Task A_failed_chain_read_notice_reports_unknown_and_recovery_reports_the_candidate(TaskComplexity? tier)
    {
        var fault = new ChainReadFault(tier);
        await using var h = await CreateReadFailureHarnessAsync(fault);
        var beforeSnapshot = await SnapshotAsync(h);
        fault.Enabled = true;
        await Instructions(h).ReconcileNowAsync("chain read failure");

        fault.Failures.ShouldBe(fault.Tiers.Length * ChainReadFault.ReadsPerCell);
        h.Adapter.SubmittedBodies.Count.ShouldBe(1);
        var failedSnapshot = await SnapshotAsync(h);
        var delta = OrchestratorInstructionsDelta.Format(beforeSnapshot, failedSnapshot);
        var failed = await ReadStateAsync(h);
        var failedFile = await File.ReadAllTextAsync(failed.WrittenPath);
        AssertBoundedChainNotice(h.Adapter.SubmittedBodies[0], delta, failed.WrittenPath);
        foreach (var affected in fault.Tiers)
        {
            delta.ShouldContain($"matrix: Plan {affected} ClaudeCode/Low (haiku) → unknown (chain read failed)");
            MatrixLine(failedFile).ShouldContain(
                $"{affected}{(affected == TaskComplexity.Medium ? " (default)" : "")} unknown (chain read failed)");
        }
        delta.ShouldNotContain("→ rolePolicy");
        if (tier is null or TaskComplexity.Medium)
        {
            delta.ShouldContain("matrix: Plan source role → unknown (chain read failed)");
            delta.ShouldContain("matrix: Plan effective ClaudeCode/Low (haiku) → unknown (chain read failed)");
            (await File.ReadAllTextAsync((await ReadStateAsync(h)).WrittenPath))
                .ShouldContain("  Plan: effective unknown (chain read failed) via unknown (chain read failed)");
        }
        await Instructions(h).ReconcileNowAsync("same chain read failure");
        h.Adapter.SubmittedBodies.Count.ShouldBe(1);
        (await ReadStateAsync(h)).Revision.ShouldBe(failed.Revision);

        fault.Enabled = false;
        await Instructions(h).ReconcileNowAsync("chain read recovered");
        h.Adapter.SubmittedBodies.Count.ShouldBe(2);
        var recovered = await ReadStateAsync(h);
        var recoveredFile = await File.ReadAllTextAsync(recovered.WrittenPath);
        var recovery = OrchestratorInstructionsDelta.Format(failedSnapshot, await SnapshotAsync(h));
        AssertBoundedChainNotice(h.Adapter.SubmittedBodies[1], recovery, recovered.WrittenPath);
        foreach (var affected in fault.Tiers)
        {
            recovery.ShouldContain($"matrix: Plan {affected} unknown (chain read failed) → ClaudeCode/Low (haiku)");
            MatrixLine(recoveredFile).ShouldContain(
                $"{affected}{(affected == TaskComplexity.Medium ? " (default)" : "")} ClaudeCode/Low (haiku)");
        }
        recovered.Revision.ShouldBe(failed.Revision + 1);
        recoveredFile.ShouldNotContain("unknown (chain read failed)");
    }

    private static string MatrixLine(string file) =>
        file.Split('\n').Select(value => value.TrimEnd('\r'))
            .Single(value => value.StartsWith("- Plan:", StringComparison.Ordinal));

    private static async Task<BridgeQueueHarness> CreateReadFailureHarnessAsync(ChainReadFault fault)
    {
        var h = await CreateHarnessAsync(fault);
        // A long absolute path reproduces the Windows wrapper pressure on every host.
        h.Delegation.OrchestratorInstructions.Path = Path.Combine(h.TempRoot, new string('w', 100), "i.md");
        await AttachBundleAsync(h);
        h.Delegation.OrchestratorInstructions.Notify = OrchestratorInstructionsNotify.Off;
        await using var scope = h.Provider.CreateAsyncScope();
        var chains = scope.ServiceProvider.GetRequiredService<ComplexityChainService>();
        foreach (var tier in fault.Tiers)
            await chains.UpsertAsync(AgentTaskRole.Plan, tier,
                new PutComplexityChainRequest([new(AgentKind.ClaudeCode, AgentModelLevel.Low)],
                    RoutingPinProvenance.Human, "read-failure witness"), null, CancellationToken.None);
        await Instructions(h).WhenIdleAsync();
        await Instructions(h).ReconcileNowAsync("startup");
        h.Adapter.SubmittedBodies.ShouldBeEmpty();
        h.Delegation.OrchestratorInstructions.Notify = OrchestratorInstructionsNotify.All;
        return h;
    }

    private sealed class ChainReadFault(TaskComplexity? tier) : DbCommandInterceptor
    {
        /// <summary>A role cell is read twice: its metadata and token, then its candidates (CARD-1275).</summary>
        public const int ReadsPerCell = 2;

        public bool Enabled { get; set; }
        public int Failures { get; private set; }
        public TaskComplexity[] Tiers { get; } = tier is { } selected
            ? [selected] : [TaskComplexity.Hard, TaskComplexity.Medium, TaskComplexity.Easy];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            var parameters = command.Parameters.Cast<DbParameter>().ToArray();
            if (Enabled && command.CommandText.Contains("FROM \"ComplexityChains\"", StringComparison.Ordinal)
                && parameters.Any(p => p.ParameterName.Contains("role", StringComparison.Ordinal)
                    && Convert.ToInt32(p.Value) == (int)AgentTaskRole.Plan)
                && parameters.Any(p => p.ParameterName.Contains("complexity", StringComparison.Ordinal)
                    && Tiers.Contains((TaskComplexity)Convert.ToInt32(p.Value))))
            {
                Failures++;
                throw new InvalidOperationException("C097 S-F chain read failure");
            }
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    [Test]
    public async Task Runner_defaults_put_rewrites_the_file_and_delivers_one_notice_to_an_idle_standing_orchestrator()
    {
        await using var h = await CreateHarnessAsync();
        await AttachBundleAsync(h);

        await using var scope = h.Provider.CreateAsyncScope();
        var before = await StateAsync(scope.ServiceProvider);
        var defaults = scope.ServiceProvider.GetRequiredService<RunnerDefaultSettingsService>();
        var current = await defaults.GetAsync(CancellationToken.None);
        await defaults.PutAsync(
            new PutRunnerDefaultsRequest(current.Revision, "desktop", [], "switch to desktop", "Human"),
            null,
            CancellationToken.None);
        await Instructions(h).WhenIdleAsync();

        var row = await StateAsync(scope.ServiceProvider);
        row.ShouldNotBeNull();
        row.Revision.ShouldBe((before?.Revision ?? 0) + 1);
        var file = File.ReadAllText(row.WrittenPath);
        file.ShouldStartWith($"[orchestrator-instructions v{row.Version} rev {row.Revision}]");
        file.ShouldContain("desktop");
        file.ShouldContain("switch to desktop");
        h.Adapter.SubmittedBodies.Count.ShouldBe(1);
        var body = h.Adapter.SubmittedBodies[0];
        body.ShouldStartWith("[System note from Antiphon:");
        body.ShouldContain(row.WrittenPath);
        body.ShouldContain("caps:");
        (await SessionVersionAsync(h)).ShouldBe(row.Version);
    }

    [Test]
    [Arguments("host-budget")]
    [Arguments("hold-upsert")]
    [Arguments("hold-clear")]
    [Arguments("routing-pin")]
    [Arguments("runner-eligibility")]
    [Arguments("dispatch-concurrency")]
    [Arguments("complexity-chain")]
    public async Task Covered_writes_each_regenerate_and_notify(string kind)
    {
        await using var h = await CreateHarnessAsync();
        await AttachBundleAsync(h);
        var section = await CoveredWriteAsync(h, kind);

        var row = await ReadStateAsync(h);
        row.Revision.ShouldBeGreaterThan(0);
        h.Adapter.SubmittedBodies.Count.ShouldBe(1);
        h.Adapter.SubmittedBodies[0].ShouldContain(section);
        h.Adapter.SubmittedBodies[0].ShouldContain(row.WrittenPath);
        if (kind == "complexity-chain")
        {
            var signals = h.Provider.GetRequiredService<RecordingSignals>();
            signals.Reasons.ToArray().ShouldBe(["complexity-chain Plan/Medium"]);
            await using var scope = h.Provider.CreateAsyncScope();
            var chains = scope.ServiceProvider.GetRequiredService<ComplexityChainService>();
            var bytes = await File.ReadAllBytesAsync(row.WrittenPath);
            await chains.UpsertAsync(AgentTaskRole.Plan, TaskComplexity.Medium,
                new PutComplexityChainRequest([new(AgentKind.Codex, AgentModelLevel.Frontier)],
                    RoutingPinProvenance.Human, "cell"), null, CancellationToken.None);
            await Instructions(h).WhenIdleAsync();
            signals.Reasons.Count.ShouldBe(2);
            (await ReadStateAsync(h)).Revision.ShouldBe(row.Revision);
            (await File.ReadAllBytesAsync(row.WrittenPath)).ShouldBe(bytes);
            h.Adapter.SubmittedBodies.Count.ShouldBe(1);
            await chains.ClearAsync(AgentTaskRole.Plan, TaskComplexity.Medium, CancellationToken.None);
            await Instructions(h).WhenIdleAsync();
            signals.Reasons.ToArray().ShouldBe([
                "complexity-chain Plan/Medium", "complexity-chain Plan/Medium", "complexity-chain Plan/Medium"]);
            (await ReadStateAsync(h)).Revision.ShouldBe(row.Revision + 1);
            h.Adapter.SubmittedBodies.Count.ShouldBe(2);
            await chains.ClearAsync(AgentTaskRole.Plan, TaskComplexity.Medium, CancellationToken.None);
            await Instructions(h).WhenIdleAsync();
            signals.Reasons.Count.ShouldBe(3);
            h.Adapter.SubmittedBodies.Count.ShouldBe(2);
        }
        if (kind == "routing-pin")
            row.Body.ShouldContain("- Code: Hard unset; Medium (default) unset; Easy unset; effective ClaudeCode/Frontier (fable) via stage pin");
    }

    [Test]
    public async Task An_orchestrator_task_session_receives_the_notice_when_idle()
    {
        await using var h = await CreateHarnessAsync();
        await MarkPoolDelegateAsync(h);
        await AddTaskAsync(h, AgentTaskKind.Orchestrator, AgentTaskStatus.Working);

        await using var scope = h.Provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<HostBudgetService>()
            .UpsertAsync("local", 3, "raise local", CancellationToken.None);
        await Instructions(h).WhenIdleAsync();

        h.Adapter.SubmittedBodies.Count.ShouldBe(1);
        h.Adapter.SubmittedBodies[0].ShouldContain("caps:");
    }

    [Test]
    public async Task Launch_records_the_current_version_on_the_session_and_the_env_carries_the_path_and_url()
    {
        await using var h = await CreateHarnessAsync();
        await AttachBundleAsync(h);
        await Instructions(h).ReconcileNowAsync("startup");
        var row = await ReadStateAsync(h);

        await using var scope = h.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var agent = await db.Agents.SingleAsync(a => a.Id == h.AgentId);
        var composition = await scope.ServiceProvider.GetRequiredService<AgentSessionLaunchComposer>()
            .ComposeForAgentAsync(agent, CancellationToken.None);
        composition.ExtraEnv.ShouldContainKey(OrchestratorInstructionsLaunch.PathVariable);
        composition.ExtraEnv.ShouldContainKey(OrchestratorInstructionsLaunch.UrlVariable);
        composition.ExtraEnv[OrchestratorInstructionsLaunch.PathVariable].ShouldBe(h.Delegation.OrchestratorInstructions.Path);
        composition.ExtraEnv[OrchestratorInstructionsLaunch.UrlVariable].ShouldEndWith("/api/orchestrator-instructions");
        composition.OrchestratorInstructionsVersion.ShouldBe(row.Version);

        var dispatcher = Dispatcher(h, scope.ServiceProvider);
        var orchestrator = dispatcher.BuildEnv(TaskOf(AgentTaskKind.Orchestrator), agent, new AgentSession { Id = h.SessionId });
        orchestrator.ShouldContainKey(OrchestratorInstructionsLaunch.PathVariable);
        orchestrator.ShouldContainKey(OrchestratorInstructionsLaunch.UrlVariable);
        var worker = dispatcher.BuildEnv(TaskOf(AgentTaskKind.Worker), agent, new AgentSession { Id = h.SessionId });
        worker.ShouldNotContainKey(OrchestratorInstructionsLaunch.PathVariable);
        worker.ShouldNotContainKey(OrchestratorInstructionsLaunch.UrlVariable);
    }

    [Test]
    public async Task Sweep_regenerates_after_a_direct_row_edit_without_a_trigger()
    {
        await using var h = await CreateHarnessAsync();
        await AttachBundleAsync(h);
        h.Delegation.OrchestratorInstructions.Notify = OrchestratorInstructionsNotify.Off;
        await using (var scope = h.Provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.HostBudgets.Add(new HostBudget
            {
                HostId = "local",
                MaxInFlight = 2,
                Reason = "seed",
                UpdatedAt = h.Now,
                Revision = 1,
            });
            await db.SaveChangesAsync();
        }

        await Instructions(h).ReconcileNowAsync("startup");
        h.Delegation.OrchestratorInstructions.Notify = OrchestratorInstructionsNotify.All;
        var before = await ReadStateAsync(h);
        await using (var scope = h.Provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().HostBudgets
                .Where(budget => budget.HostId == "local")
                .ExecuteUpdateAsync(u => u.SetProperty(budget => budget.MaxInFlight, 9));
        }

        await Instructions(h).ReconcileNowAsync("sweep");
        var after = await ReadStateAsync(h);
        after.Revision.ShouldBe(before.Revision + 1);
        after.Version.ShouldNotBe(before.Version);
        h.Adapter.SubmittedBodies.Count.ShouldBe(1);
        h.Adapter.SubmittedBodies[0].ShouldContain("caps:");
        (await SessionVersionAsync(h)).ShouldBe(after.Version);
    }

    [Test]
    public async Task Startup_regenerates_and_a_pre_feature_session_gets_one_notice()
    {
        await using var h = await CreateHarnessAsync();
        await AttachBundleAsync(h);
        (await SessionVersionAsync(h)).ShouldBeNull();

        await Instructions(h).ReconcileNowAsync("startup");

        var row = await ReadStateAsync(h);
        File.Exists(row.WrittenPath).ShouldBeTrue();
        h.Adapter.SubmittedBodies.Count.ShouldBe(1);
        (await SessionVersionAsync(h)).ShouldBe(row.Version);
    }

    [Test]
    public async Task An_unchanged_write_neither_rewrites_nor_notifies()
    {
        await using var h = await CreateHarnessAsync();
        await AttachBundleAsync(h);
        await using var scope = h.Provider.CreateAsyncScope();
        var defaults = scope.ServiceProvider.GetRequiredService<RunnerDefaultSettingsService>();
        var current = await defaults.GetAsync(CancellationToken.None);
        await defaults.PutAsync(
            new PutRunnerDefaultsRequest(current.Revision, "desktop", [], "switch to desktop", "Human"),
            null,
            CancellationToken.None);
        await Instructions(h).WhenIdleAsync();
        var row = await StateAsync(scope.ServiceProvider);
        var bytes = await File.ReadAllBytesAsync(row!.WrittenPath);
        var mtime = File.GetLastWriteTimeUtc(row.WrittenPath);

        var again = await defaults.GetAsync(CancellationToken.None);
        await defaults.PutAsync(
            new PutRunnerDefaultsRequest(again.Revision, "desktop", [], "switch to desktop", "Human"),
            null,
            CancellationToken.None);
        await Instructions(h).WhenIdleAsync();

        (await File.ReadAllBytesAsync(row.WrittenPath)).ShouldBe(bytes);
        File.GetLastWriteTimeUtc(row.WrittenPath).ShouldBe(mtime);
        (await StateAsync(scope.ServiceProvider))!.Revision.ShouldBe(row.Revision);
        h.Adapter.SubmittedBodies.Count.ShouldBe(1);
    }

    [Test]
    public async Task A_busy_session_gets_the_notice_after_its_turn_ends_and_a_second_change_replaces_the_pending_row()
    {
        await using var h = await CreateHarnessAsync();
        await AttachBundleAsync(h);
        await h.MarkWorkingAsync();
        await using var scope = h.Provider.CreateAsyncScope();
        var budgets = scope.ServiceProvider.GetRequiredService<HostBudgetService>();
        await budgets.UpsertAsync("local", 2, "first budget", CancellationToken.None);
        await Instructions(h).WhenIdleAsync();
        await budgets.UpsertAsync("local", 4, "second budget", CancellationToken.None);
        await Instructions(h).WhenIdleAsync();

        await using var db = NewDb(h);
        var pending = await db.SessionQueuedMessages.SingleAsync(message =>
            message.AgentSessionId == h.SessionId && message.Status == QueuedMessageStatus.Pending);
        pending.NoteHeader.ShouldBe(OrchestratorInstructionsService.NoteHeader);
        pending.Body.ShouldContain("changed more than once since v");
        pending.Body.ShouldContain("caps:");
        h.Adapter.SubmittedBodies.ShouldBeEmpty();

        await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        h.Adapter.SubmittedBodies.Count.ShouldBe(1);
        h.Adapter.SubmittedBodies[0].ShouldContain("changed more than once since v");
    }

    [Test]
    public async Task A_worker_delegate_session_receives_nothing()
    {
        await using var h = await CreateHarnessAsync();
        await MarkPoolDelegateAsync(h);
        await AddTaskAsync(h, AgentTaskKind.Worker, AgentTaskStatus.Working);
        await using var scope = h.Provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<HostBudgetService>()
            .UpsertAsync("local", 3, "raise local", CancellationToken.None);
        await Instructions(h).WhenIdleAsync();

        h.Adapter.SubmittedBodies.ShouldBeEmpty();
        await using var db = NewDb(h);
        (await db.SessionQueuedMessages.AnyAsync(message => message.AgentSessionId == h.SessionId)).ShouldBeFalse();
    }

    [Test]
    public async Task Secret_bearing_rows_never_reach_the_file_or_the_notice()
    {
        const string apiKey = "c822-api-key-sentinel";
        const string tuiSecret = "c822-tui-secret-sentinel";
        const string llmKey = "c822-llm-key-sentinel";
        const string capability = "c822c822c822c822c822c822c822c822c822c822c822c822c822c822c822c822";
        const string launchEnv = "c822-launch-env-sentinel";

        await using var h = await CreateHarnessAsync();
        await AttachBundleAsync(h);
        await using (var db = NewDb(h))
        {
            var now = h.Now;
            db.ApiKeys.Add(new ApiKey
            {
                Id = Guid.NewGuid(),
                Name = "c822-key",
                Ciphertext = apiKey,
                ProtectionVersion = "test",
                CreatedAt = now,
                UpdatedAt = now,
            });
            var profileId = Guid.NewGuid();
            db.AgentTuiProfiles.Add(new AgentTuiProfile
            {
                Id = profileId,
                DisplayName = "c822",
                Kind = AgentKind.ClaudeCode,
                IsEnabled = true,
                Source = AgentTuiProfileSource.Operator,
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.AgentTuiSecrets.Add(new AgentTuiSecret
            {
                Id = Guid.NewGuid(),
                ProfileId = profileId,
                Name = "C822_TUI",
                Ciphertext = tuiSecret,
                ProtectionVersion = "test",
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.LlmProviders.Add(new LlmProvider
            {
                Id = Guid.NewGuid(),
                Name = "c822",
                ProviderType = ProviderType.Anthropic,
                ApiKey = llmKey,
                BaseUrl = "http://127.0.0.1",
                DefaultModel = "unused",
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.DelegationCapabilities.Add(new DelegationCapability
            {
                Id = Guid.NewGuid(),
                Name = "c822",
                TokenHash = capability,
                RootsJson = "[]",
                CreatedAt = now,
            });
            await db.SaveChangesAsync();
            await db.Agents.Where(agent => agent.Id == h.AgentId)
                .ExecuteUpdateAsync(u => u.SetProperty(agent => agent.LaunchEnvJson, "{\"C822\":\"" + launchEnv + "\"}"));
        }

        await Instructions(h).ReconcileNowAsync("startup");
        var row = await ReadStateAsync(h);
        var file = await File.ReadAllTextAsync(row.WrittenPath);
        foreach (var sentinel in new[] { apiKey, tuiSecret, llmKey, capability, launchEnv })
        {
            file.ShouldNotContain(sentinel);
            row.Body.ShouldNotContain(sentinel);
            h.Adapter.SubmittedBodies.ShouldAllBe(body => !body.Contains(sentinel, StringComparison.Ordinal));
        }
    }

    [Test]
    public async Task A_refused_render_keeps_the_previous_file_and_records_a_warning_without_a_notice()
    {
        await using var h = await CreateHarnessAsync();
        await AttachBundleAsync(h);
        await Instructions(h).ReconcileNowAsync("startup");
        var row = await ReadStateAsync(h);
        var bytes = await File.ReadAllBytesAsync(row.WrittenPath);
        var notices = h.Adapter.SubmittedBodies.Count;

        h.Delegation.OrchestratorInstructions.StandingInstructions = ["{{key:x}}"];
        await Instructions(h).ReconcileNowAsync("operator");

        (await File.ReadAllBytesAsync(row.WrittenPath)).ShouldBe(bytes);
        h.Adapter.SubmittedBodies.Count.ShouldBe(notices);
        await using var db = NewDb(h);
        (await db.AgentIncidents.AnyAsync(incident =>
            incident.Kind == AgentIncidentKind.OrchestratorInstructionsWriteFailed
            && incident.Severity == AlertSeverity.Warning)).ShouldBeTrue();
    }

    [Test]
    public async Task A_cell_write_notice_is_received_as_one_complete_user_prompt_equal_to_its_queued_body()
    {
        await using var h = await CreateHarnessAsync();
        await AttachBundleAsync(h);
        h.Delegation.OrchestratorInstructions.Notify = OrchestratorInstructionsNotify.Off;
        await Instructions(h).ReconcileNowAsync("startup");
        var startup = await ReadStateAsync(h);
        (await SessionVersionAsync(h)).ShouldBeNull();
        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, "Dispatch work.");
        var floor = await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        h.Delegation.OrchestratorInstructions.Notify = OrchestratorInstructionsNotify.All;
        await using (var scope = h.Provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ComplexityChainService>().UpsertAsync(
                AgentTaskRole.Plan, TaskComplexity.Medium,
                new PutComplexityChainRequest([new(AgentKind.Codex, AgentModelLevel.Frontier)],
                    RoutingPinProvenance.Human, "cell"), null, CancellationToken.None);
        }
        await Instructions(h).WhenIdleAsync();
        var state = await ReadStateAsync(h);
        await using var db = NewDb(h);
        var rows = await db.SessionQueuedMessages.Where(row => row.AgentSessionId == h.SessionId
            && row.NoteHeader == OrchestratorInstructionsService.NoteHeader).ToListAsync();
        rows.Count.ShouldBe(1);
        var row = rows.Single();
        row.Status.ShouldBe(QueuedMessageStatus.Sent);
        row.LastDeliveryBaselineSequence.ShouldBe(floor);
        var prompts = await db.TranscriptEntries.Where(entry => entry.AgentSessionId == h.SessionId
            && entry.Kind == TranscriptKinds.UserPrompt).ToListAsync();
        prompts.Count.ShouldBe(2);
        prompts.Count(entry => entry.Sequence > floor).ShouldBe(1);
        prompts.Single(entry => entry.Sequence > floor).Text.ShouldBe(row.Body);
        row.Body.ShouldContain("matrix:");
        row.Body.ShouldContain(state.WrittenPath);
        h.Adapter.SubmittedBodies.ShouldBe([row.Body]);
        state.Revision.ShouldBe(startup.Revision + 1);
        state.Version.ShouldNotBe(startup.Version);
        (await SessionVersionAsync(h)).ShouldBe(state.Version);
        state.Body.ShouldContain("- Plan: Hard unset; Medium (default) Codex/Frontier (gpt-6-astra); Easy unset; effective Codex/Frontier (gpt-6-astra) via role");
        var snapshot = JsonSerializer.Deserialize<OrchestratorInstructionsSnapshot>(state.SnapshotJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        snapshot.Matrix.ShouldNotBeNull();
        snapshot.Matrix.DefaultComplexity.ShouldBe("Medium");
        snapshot.Matrix.Rows.Select(matrixRow => matrixRow.Role).ShouldBe([
            "Investigate", "Plan", "TestDesign", "Code", "Mutation", "Review", "Debug",
            "Coverage", "Docs", "Commit", "Test", "Deploy", "Merge", "Custom"]);
    }

    private static async Task<string> CoveredWriteAsync(BridgeQueueHarness h, string kind)
    {
        h.Delegation.OrchestratorInstructions.Notify = OrchestratorInstructionsNotify.Off;
        if (kind == "hold-clear")
        {
            await using var seed = h.Provider.CreateAsyncScope();
            await seed.ServiceProvider.GetRequiredService<ModelAvailability>().UpsertManualAsync(
                "ClaudeCode", "opus", DateTimeOffset.UtcNow.AddHours(4), "hold opus", CancellationToken.None);
            await Instructions(h).WhenIdleAsync();
        }

        await Instructions(h).ReconcileNowAsync("startup");
        h.Delegation.OrchestratorInstructions.Notify = OrchestratorInstructionsNotify.All;
        await using var scope = h.Provider.CreateAsyncScope();
        switch (kind)
        {
            case "complexity-chain":
                await scope.ServiceProvider.GetRequiredService<ComplexityChainService>().UpsertAsync(
                    AgentTaskRole.Plan, TaskComplexity.Medium,
                    new PutComplexityChainRequest([new(AgentKind.Codex, AgentModelLevel.Frontier)],
                        RoutingPinProvenance.Human, "cell"), null, CancellationToken.None);
                await Instructions(h).WhenIdleAsync();
                return "matrix:";
            case "host-budget":
                await scope.ServiceProvider.GetRequiredService<HostBudgetService>()
                    .UpsertAsync("local", 3, "raise local", CancellationToken.None);
                await Instructions(h).WhenIdleAsync();
                return "caps:";
            case "hold-upsert":
                await scope.ServiceProvider.GetRequiredService<ModelAvailability>().UpsertManualAsync(
                    "ClaudeCode", "opus", DateTimeOffset.UtcNow.AddHours(4), "hold opus", CancellationToken.None);
                await Instructions(h).WhenIdleAsync();
                return "holds:";
            case "hold-clear":
                await scope.ServiceProvider.GetRequiredService<ModelAvailability>()
                    .ClearAsync("ClaudeCode", "opus", CancellationToken.None);
                await Instructions(h).WhenIdleAsync();
                return "holds:";
            case "routing-pin":
                await scope.ServiceProvider.GetRequiredService<RoutingPinService>().UpsertAsync(
                    new PutRoutingPinRequest(
                        AgentTaskRole.Code,
                        Provenance: RoutingPinProvenance.Human,
                        AgentKind: AgentKind.ClaudeCode,
                        ModelLevel: AgentModelLevel.Frontier,
                        Reason: "stage pin"),
                    null,
                    CancellationToken.None);
                await Instructions(h).WhenIdleAsync();
                return "pins:";
            case "runner-eligibility":
                await using (var db = NewDb(h))
                {
                    db.HostBudgets.Add(new HostBudget
                    {
                        HostId = "local",
                        MaxInFlight = 1,
                        Reason = "direct",
                        UpdatedAt = h.Now,
                        Revision = 1,
                    });
                    await db.SaveChangesAsync();
                }

                new CompositeRunnerEligibilityObserver(
                    new AlarmWakeQueue(),
                    h.Provider.GetRequiredService<IOrchestratorInstructionsSignals>()).Changed("desktop");
                await Instructions(h).WhenIdleAsync();
                return "runners:";
            case "dispatch-concurrency":
                await scope.ServiceProvider.GetRequiredService<DispatchConcurrencySettingsService>().PutGlobalAsync(
                    new PutDispatchConcurrencyRequest(
                        1,
                        null,
                        JsonDocument.Parse("""{"roles":{"Code":{"maxParallel":4}}}""").RootElement.Clone(),
                        "code four",
                        "Human"),
                    null,
                    CancellationToken.None);
                await Instructions(h).WhenIdleAsync();
                return "caps:";
            default:
                throw new InvalidOperationException(kind);
        }
    }

    private static async Task<BridgeQueueHarness> CreateHarnessAsync(DbCommandInterceptor? interceptor = null)
    {
        var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var settings = new DelegationSettings();
        var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = true,
            ConnectionString = schema.ConnectionString,
            Delegation = settings,
            ConfigureDbContext = options =>
            {
                if (interceptor is not null) options.AddInterceptors(interceptor);
            },
            ConfigureServices = services =>
            {
                services.AddSingleton<OrchestratorInstructionsService>();
                services.AddSingleton<RecordingSignals>();
                services.AddSingleton<IOrchestratorInstructionsSignals>(sp =>
                    sp.GetRequiredService<RecordingSignals>());
                services.AddScoped<KeepSixShortfall>();
                services.AddScoped<KeepSixService>();
                services.AddScoped<OrchestratorInstructionsSnapshotBuilder>();
                services.AddScoped<RunnerDefaultSettingsService>();
                services.AddScoped<DispatchConcurrencySettingsService>();
                services.AddScoped<HostBudgetService>();
                services.AddScoped<RoutingPinService>();
                services.AddScoped<ModelAvailability>();
                services.AddSingleton<PhoneHomeRunnerDirectory>(sp => new PhoneHomeRunnerDirectory(
                    sp.GetRequiredService<ISessionRunnerClient>(),
                    Options.Create(new PhoneHomeRunnerSettings { Enabled = false }),
                    sp.GetRequiredService<IServiceScopeFactory>(),
                    sp.GetRequiredService<TimeProvider>()));
                services.AddSingleton<ISessionRunnerDirectory>(sp => sp.GetRequiredService<PhoneHomeRunnerDirectory>());
            },
        });
        settings.OrchestratorInstructions.Path = Path.Combine(harness.TempRoot, "ANTIPHON_ORCHESTRATOR_INSTRUCTIONS.md");
        harness.AfterDispose = schema;
        return harness;
    }

    private static async Task AttachBundleAsync(BridgeQueueHarness h)
    {
        await using var db = NewDb(h);
        db.AgentBundleAttachments.Add(new AgentBundleAttachment
        {
            AgentId = h.AgentId,
            BundleKey = InstructionBundles.Orchestrator,
            Position = 0,
            CreatedAt = h.Now,
        });
        await db.SaveChangesAsync();
    }

    private static async Task MarkPoolDelegateAsync(BridgeQueueHarness h)
    {
        await using var db = NewDb(h);
        await db.Agents.Where(agent => agent.Id == h.AgentId)
            .ExecuteUpdateAsync(u => u.SetProperty(agent => agent.IsPoolDelegate, true));
    }

    private static async Task AddTaskAsync(BridgeQueueHarness h, AgentTaskKind kind, AgentTaskStatus status)
    {
        await using var db = NewDb(h);
        var id = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "instructions",
            Goal = "notice",
            Kind = kind,
            Status = status,
            AgentSessionId = h.SessionId,
            WorkingDirectory = h.TempRoot,
            CreatedAt = h.Now,
        });
        await db.SaveChangesAsync();
    }

    private static OrchestratorInstructionsService Instructions(BridgeQueueHarness h) =>
        h.Provider.GetRequiredService<OrchestratorInstructionsService>();

    private sealed class RecordingSignals(OrchestratorInstructionsService instructions) : IOrchestratorInstructionsSignals
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> Reasons { get; } = new();

        public void Signal(string reason)
        {
            Reasons.Enqueue(reason);
            instructions.Signal(reason);
        }
    }

    private static AppDbContext NewDb(BridgeQueueHarness h) =>
        new(TestDbFixture.CreateDbContextOptions(h.ConnectionString));

    private static async Task<OrchestratorInstructionsState> ReadStateAsync(BridgeQueueHarness h)
    {
        await using var db = NewDb(h);
        return await db.OrchestratorInstructionsStates.AsNoTracking()
            .SingleAsync(state => state.Id == OrchestratorInstructionsState.FleetId);
    }

    private static Task<OrchestratorInstructionsState?> StateAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<AppDbContext>();
        return db.OrchestratorInstructionsStates
            .SingleOrDefaultAsync(state => state.Id == OrchestratorInstructionsState.FleetId);
    }

    private static async Task<string?> SessionVersionAsync(BridgeQueueHarness h)
    {
        await using var db = NewDb(h);
        return await db.AgentSessions.Where(session => session.Id == h.SessionId)
            .Select(session => session.OrchestratorInstructionsVersion)
            .SingleAsync();
    }

    private static AgentTaskDispatcher Dispatcher(BridgeQueueHarness h, IServiceProvider services) =>
        new(
            services.GetRequiredService<AppDbContext>(),
            h.Provider.GetRequiredService<AgentRegistry>(),
            h.Provider.GetRequiredService<AgentSessionLaunchQueue>(),
            h.Queue,
            null!,
            null!,
            null!,
            services.GetRequiredService<IOptions<DelegationSettings>>(),
            h.EventBus,
            h.Clock,
            NullLogger<AgentTaskDispatcher>.Instance);

    private static AgentTask TaskOf(AgentTaskKind kind)
    {
        var id = Guid.NewGuid();
        return new AgentTask { Id = id, RootTaskId = id, Kind = kind, Title = "env", Goal = "env" };
    }
}
