using System.Data.Common;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class MatrixDefaultComplexityCreateTests
{
    [Test]
    public async Task A_plain_create_walks_the_role_row_at_the_default_tier()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Row(AgentTaskRole.Plan, new(AgentKind.Codex, AgentModelLevel.Frontier), new(AgentKind.ClaudeCode, AgentModelLevel.High));
        var result = await f.Create();
        result.Status.ShouldBe(AgentTaskStatus.Queued);
        result.AgentKind.ShouldBe(AgentKind.Codex);
        result.ModelLevel.ShouldBe(AgentModelLevel.Frontier);
        result.Routing.ShouldNotBeNull();
        result.Routing.Complexity.ShouldBe(TaskComplexity.Medium);
        result.Routing.Source.ShouldBe("chain:Plan/Medium");
        result.Routing.Walked.ShouldBeTrue();
        var task = await f.Saved(result.Id);
        task.Complexity.ShouldBe(TaskComplexity.Medium);
        task.ExplicitAgentKind.ShouldBeNull();
        (await f.CreatedDetail(task.Id)).ShouldContain("(matrix default Medium)");
        (await f.CreatedDetail(task.Id)).ShouldNotContain("(role policy)");
    }

    [Test]
    public async Task An_unset_row_keeps_the_role_policy_path()
    {
        await using var f = await Fixture.CreateAsync();
        var result = await f.Create();
        result.Status.ShouldBe(AgentTaskStatus.Queued);
        result.AgentKind.ShouldBe(AgentKind.ClaudeCode);
        result.ModelLevel.ShouldBe(AgentModelLevel.Frontier);
        result.Routing.ShouldBeNull();
        (await f.Saved(result.Id)).Complexity.ShouldBeNull();
        (await f.CreatedDetail(result.Id)).ShouldContain("(role policy)");
        (await f.CreatedDetail(result.Id)).ShouldNotContain("(matrix default");
        await f.NoBlocked(result.Id);
    }

    [Test]
    public async Task An_exhausted_default_row_blocks_with_the_cell_name()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Row(AgentTaskRole.Code, new(AgentKind.ClaudeCode, AgentModelLevel.Frontier));
        await f.Hold();
        var parent = await f.Session();
        var result = await f.Create(new("code it", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Shared), parent);
        result.Status.ShouldBe(AgentTaskStatus.Blocked);
        result.Warning.ShouldStartWith("routing exhausted: ");
        result.Warning.ShouldContain("Code/Medium chain");
        result.Warning.ShouldContain("fable");
        await f.Blocked(await f.Saved(result.Id), AgentTaskRole.Code, parent);
    }

    [Test]
    public async Task An_explicit_kind_is_not_stamped_and_warns()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Row(AgentTaskRole.Plan, new(AgentKind.Codex, AgentModelLevel.Frontier));
        var result = await f.Create(new("plan", Role: AgentTaskRole.Plan, AgentKind: AgentKind.ClaudeCode, Workspace: WorkspaceMode.Shared));
        result.Status.ShouldBe(AgentTaskStatus.Queued);
        result.AgentKind.ShouldBe(AgentKind.ClaudeCode);
        result.ModelLevel.ShouldBe(AgentModelLevel.Frontier);
        result.Routing.ShouldBeNull();
        var task = await f.Saved(result.Id);
        task.Complexity.ShouldBeNull();
        task.ExplicitAgentKind.ShouldBe(AgentKind.ClaudeCode);
        result.Warning.ShouldContain("explicit kind/level: the Plan/Medium row was not consulted");
    }

    [Test]
    public async Task A_single_candidate_required_stage_pin_is_an_explicit_pair_and_is_not_stamped()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Row(AgentTaskRole.Plan, new(AgentKind.Codex, AgentModelLevel.Frontier));
        await f.Pin(AgentTaskRole.Plan, false, AgentKind.ClaudeCode, AgentModelLevel.High);
        var result = await f.Create();
        result.AgentKind.ShouldBe(AgentKind.ClaudeCode);
        result.ModelLevel.ShouldBe(AgentModelLevel.High);
        result.Routing.ShouldBeNull();
        var task = await f.Saved(result.Id);
        task.Complexity.ShouldBeNull();
        task.RoutingPinId.ShouldBeNull();
        task.ExplicitAgentKind.ShouldBeNull();
        var detail = await f.CreatedDetail(task.Id);
        detail.ShouldContain("(explicit override)");
        detail.ShouldContain("[pin=the human required stage-wide Plan routing pin");
        detail.ShouldNotContain("bypassed");
    }

    [Test]
    public async Task A_required_candidate_list_plus_a_row_is_stamped_and_walks_the_pin()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Row(AgentTaskRole.Plan, new(AgentKind.Codex, AgentModelLevel.Frontier));
        var pin = await f.Pins.UpsertAsync(new(AgentTaskRole.Plan, Provenance: RoutingPinProvenance.Human,
            Strength: RoutingPinStrength.Required, Reason: "pin list", Candidates:
            [new(AgentKind.ClaudeCode, AgentModelLevel.High), new(AgentKind.ClaudeCode, AgentModelLevel.Frontier)]), null, default);
        var result = await f.Create();
        result.AgentKind.ShouldBe(AgentKind.ClaudeCode);
        result.ModelLevel.ShouldBe(AgentModelLevel.High);
        result.Routing.ShouldNotBeNull();
        result.Routing.Source.ShouldBe("pin:stage Plan");
        result.Routing.Walked.ShouldBeTrue();
        result.Routing.Candidates.Count.ShouldBe(2);
        result.Routing.Candidates.ShouldAllBe(c => c.Origin == "pin");
        result.Warning.ShouldContain("complexity chain bypassed by");
        var task = await f.Saved(result.Id);
        task.Complexity.ShouldBe(TaskComplexity.Medium);
        task.RoutingPinId.ShouldBe(pin.Id);
    }

    [Test, Category("Unit")]
    [Arguments("plain", true)]
    [Arguments("Check", false)]
    [Arguments("Orchestrator", false)]
    [Arguments("agent", false)]
    [Arguments("kind", false)]
    [Arguments("level", false)]
    [Arguments("ignore", false)]
    [Arguments("follow-up", false)]
    public void The_stamp_predicate_excludes_specialists_orchestrators_agents_explicit_asks_and_ignore_model_disabled(string variant, bool expected)
    {
        var request = new CreateAgentTaskRequest("plan", Role: AgentTaskRole.Plan);
        request = variant switch
        {
            "Check" => request with { Role = AgentTaskRole.Check },
            "Orchestrator" => request with { Kind = AgentTaskKind.Orchestrator },
            "agent" => request with { AgentId = Guid.NewGuid() },
            "kind" => request with { AgentKind = AgentKind.Codex },
            "level" => request with { ModelLevel = AgentModelLevel.High },
            "ignore" => request with { IgnoreModelDisabled = true },
            "plain" or "follow-up" => request,
            _ => throw new ArgumentOutOfRangeException(nameof(variant)),
        };
        AgentTaskService.MatrixDefaultApplies(request, variant == "follow-up").ShouldBe(expected);
    }

    [Test]
    public async Task An_orchestrator_create_keeps_role_policy_and_the_level_floor()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Row(AgentTaskRole.Plan, new(AgentKind.ClaudeCode, AgentModelLevel.Low));
        var request = new CreateAgentTaskRequest("own it", Kind: AgentTaskKind.Orchestrator, Role: AgentTaskRole.Plan, Workspace: WorkspaceMode.Shared);
        var result = await f.Create(request);
        result.ModelLevel.ShouldBe(AgentModelLevel.Frontier);
        result.Routing.ShouldBeNull();
        (await f.Saved(result.Id)).Complexity.ShouldBeNull();
        f.Settings.RolePolicy["Plan"].Level = AgentModelLevel.Low;
        var clamped = await f.Create(request);
        clamped.ModelLevel.ShouldBe(AgentModelLevel.High);
        clamped.Routing.ShouldBeNull();
        (await f.Saved(clamped.Id)).Complexity.ShouldBeNull();
    }

    [Test]
    public async Task The_settlement_commit_child_takes_the_commit_row()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Row(AgentTaskRole.Commit, new(AgentKind.Codex, AgentModelLevel.Frontier));
        var child = await f.Child(AgentTaskRole.Commit);
        Pair(child, AgentKind.Codex, AgentModelLevel.Frontier, TaskComplexity.Medium);
        child.RoutingPinId.ShouldBeNull();
        await f.Db.ComplexityChains.ExecuteDeleteAsync();
        Pair(await f.Child(AgentTaskRole.Commit), AgentKind.ClaudeCode, AgentModelLevel.Medium, null);
    }

    [Test]
    [Arguments("no-row-list-pin", AgentKind.ClaudeCode, true)]
    [Arguments("no-row-single-pin", AgentKind.Codex, false)]
    [Arguments("row-and-list-pin", AgentKind.ClaudeCode, true)]
    [Arguments("row-and-single-pin", AgentKind.Codex, false)]
    public async Task A_required_pin_with_candidates_keeps_the_commit_child_on_todays_pin_path(string variant, AgentKind kind, bool list)
    {
        await using var f = await Fixture.CreateAsync();
        if (variant.StartsWith("row-", StringComparison.Ordinal))
            await f.Row(AgentTaskRole.Commit, new(list ? AgentKind.Grok : AgentKind.ClaudeCode, list ? AgentModelLevel.Frontier : AgentModelLevel.High));
        var pin = await f.Pin(AgentTaskRole.Commit, list, kind, AgentModelLevel.Frontier);
        var child = await f.Child(AgentTaskRole.Commit);
        Pair(child, kind, AgentModelLevel.Frontier, null);
        child.RoutingPinId.ShouldBe(list ? pin.Id : null);
        await f.NoBlocked(child.Id);
    }

    [Test]
    public Task A_stage_agent_only_pin_binds_the_standing_agent_and_is_not_stamped() => AgentPin(false);

    [Test]
    public Task A_card_agent_only_pin_binds_the_standing_agent_and_is_not_stamped() => AgentPin(true);

    private static async Task AgentPin(bool cardPin)
    {
        await using var f = await Fixture.CreateAsync();
        await f.Row(AgentTaskRole.Plan, new(AgentKind.Codex, AgentModelLevel.Frontier));
        var agent = new Agent { Id = Guid.NewGuid(), Name = "standing", Slug = "standing", WorkingDirectory = f.Path,
            Kind = AgentKind.ClaudeCode, ModelLevel = AgentModelLevel.Medium, Status = AgentStatus.Idle,
            IsPoolDelegate = false, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        f.Db.Agents.Add(agent);
        await f.Db.SaveChangesAsync();
        if (cardPin) await RoutingPinServiceTests.SeedCardAsync(f.Db, "CARD-9097A");
        await f.Pins.UpsertAsync(new(AgentTaskRole.Plan, Card: cardPin ? "CARD-9097A" : null,
            Provenance: RoutingPinProvenance.Human, Strength: RoutingPinStrength.Required, AgentId: agent.Id, Reason: "standing owner"), null, default);
        var result = await f.Create(new("plan", Role: AgentTaskRole.Plan, Workspace: WorkspaceMode.Shared, Card: cardPin ? "CARD-9097A" : null));
        result.AgentKind.ShouldBe(AgentKind.ClaudeCode);
        result.Routing.ShouldBeNull();
        var task = await f.Saved(result.Id);
        task.AgentId.ShouldBe(agent.Id);
        task.Complexity.ShouldBeNull();
        (await f.CreatedDetail(task.Id)).ShouldContain("[pin=");
        (await f.Db.Agents.AsNoTracking().SingleAsync(a => a.Id == agent.Id)).Kind.ShouldBe(AgentKind.ClaudeCode);
    }

    [Test]
    public async Task The_server_created_merge_child_takes_the_merge_row()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Row(AgentTaskRole.Merge, new(AgentKind.Codex, AgentModelLevel.Frontier));
        var child = await f.Child(AgentTaskRole.Merge);
        Pair(child, AgentKind.Codex, AgentModelLevel.Frontier, TaskComplexity.Medium);
        child.RoutingPinId.ShouldBeNull();
    }

    [Test]
    [Arguments("none")]
    [Arguments("stage-list-pin")]
    [Arguments("card-pin")]
    [Arguments("policy-kind-codex")]
    [Arguments("row-and-required-pin")]
    public async Task An_empty_matrix_or_a_required_pin_keeps_the_merge_child_on_todays_program_default(string variant)
    {
        await using var f = await Fixture.CreateAsync();
        Guid? cardId = null;
        switch (variant)
        {
            case "row-and-required-pin":
                await f.Row(AgentTaskRole.Merge, new(AgentKind.Codex, AgentModelLevel.Frontier));
                goto case "stage-list-pin";
            case "stage-list-pin": await f.Pin(AgentTaskRole.Merge, true, AgentKind.ClaudeCode, AgentModelLevel.Frontier); break;
            case "card-pin":
                cardId = (await RoutingPinServiceTests.SeedCardAsync(f.Db, "CARD-9097M")).Id;
                await f.Pin(AgentTaskRole.Merge, false, AgentKind.Codex, AgentModelLevel.Frontier, "CARD-9097M"); break;
            case "policy-kind-codex": f.Settings.RolePolicy["Merge"].Kind = AgentKind.Codex; break;
            case "none": break;
            default: throw new ArgumentOutOfRangeException(nameof(variant));
        }
        var child = await f.Child(AgentTaskRole.Merge, card: cardId);
        Pair(child, AgentKind.ClaudeCode, AgentModelLevel.High, null);
        child.RoutingPinId.ShouldBeNull();
        await f.NoBlocked(child.Id);
    }

    [Test]
    public Task An_exhausted_merge_row_blocks_the_merge_child() => ExhaustedChild(AgentTaskRole.Merge);

    [Test]
    public Task An_exhausted_commit_row_blocks_the_commit_child() => ExhaustedChild(AgentTaskRole.Commit);

    private static async Task ExhaustedChild(AgentTaskRole role)
    {
        await using var f = await Fixture.CreateAsync();
        await f.Row(role, new(AgentKind.ClaudeCode, AgentModelLevel.Frontier));
        await f.Hold();
        var parent = await f.Session();
        var child = await f.Child(role, parent);
        await f.Blocked(child, role, parent);
        (await f.Db.AgentTaskEvents.Where(e => e.AgentTaskId == child.Id && e.Type == AgentTaskEventType.Warning).ToListAsync())
            .ShouldNotContain(e => e.Detail != null && e.Detail.Contains("using role policy"));
    }

    [Test]
    public async Task A_held_commit_row_head_falls_to_the_next_candidate_with_the_skipped_warning()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Row(AgentTaskRole.Commit, new(AgentKind.ClaudeCode, AgentModelLevel.Frontier), new(AgentKind.Codex, AgentModelLevel.Frontier));
        await f.Hold();
        var child = await f.Child(AgentTaskRole.Commit);
        Pair(child, AgentKind.Codex, AgentModelLevel.Frontier, TaskComplexity.Medium);
        (await f.Db.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == child.Id && e.Type == AgentTaskEventType.Warning)).Detail.ShouldContain("skipped fable");
    }

    [Test]
    public async Task A_chain_read_failure_keeps_the_required_commit_pin()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Row(AgentTaskRole.Commit, new(AgentKind.ClaudeCode, AgentModelLevel.High));
        await f.Pin(AgentTaskRole.Commit, false, AgentKind.Codex, AgentModelLevel.Frontier);
        var broken = new AppDbContext(TestDbFixture.CreateDbContextOptions(f.Schema.ConnectionString));
        var service = f.Service(broken);
        await broken.DisposeAsync();
        var child = await f.Child(AgentTaskRole.Commit, service: service);
        Pair(child, AgentKind.Codex, AgentModelLevel.Frontier, null);
        child.RoutingPinId.ShouldBeNull();
        await f.NoBlocked(child.Id);
        (await f.Db.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == child.Id && e.Type == AgentTaskEventType.Warning)).Detail.ShouldContain("Routing read failed");
    }

    [Test]
    public async Task The_commit_child_reads_the_pin_once_and_keeps_a_resolved_required_pin()
    {
        var guard = new PinReadGuard();
        await using var f = await Fixture.CreateAsync(guard);
        await f.Row(AgentTaskRole.Commit, new(AgentKind.ClaudeCode, AgentModelLevel.High));
        await f.Pin(AgentTaskRole.Commit, false, AgentKind.Codex, AgentModelLevel.Frontier);
        guard.Armed = true;
        var child = await f.Child(AgentTaskRole.Commit);
        Pair(child, AgentKind.Codex, AgentModelLevel.Frontier, null);
        child.RoutingPinId.ShouldBeNull();
        await f.NoBlocked(child.Id);
        guard.PinReads.ShouldBe(1);
        (await f.Db.AgentTaskEvents.Where(e => e.AgentTaskId == child.Id).ToListAsync())
            .ShouldNotContain(e => e.Detail != null && e.Detail.Contains("Routing pin service failed"));
    }

    [Test]
    public async Task A_blocked_commit_child_without_a_parent_session_writes_no_note()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Row(AgentTaskRole.Commit, new(AgentKind.ClaudeCode, AgentModelLevel.Frontier));
        await f.Hold();
        foreach (var parent in new Guid?[] { null, Guid.NewGuid() })
        {
            var child = await f.Child(AgentTaskRole.Commit, parent);
            child.Status.ShouldBe(AgentTaskStatus.Blocked);
            child.FailureReason.ShouldStartWith("routing exhausted: ");
            child.Complexity.ShouldBe(TaskComplexity.Medium);
            (await f.Db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == child.Id && e.Type == AgentTaskEventType.Blocked)).ShouldBe(1);
            (await f.Db.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == child.Id)).ShouldBe(0);
        }
    }

    private static void Pair(AgentTask child, AgentKind kind, AgentModelLevel level, TaskComplexity? complexity)
    {
        child.Status.ShouldBe(AgentTaskStatus.Queued);
        child.AgentKind.ShouldBe(kind);
        child.ModelLevel.ShouldBe(level);
        child.Complexity.ShouldBe(complexity);
    }

    private sealed class PinReadGuard : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public int PinReads { get; private set; }
        private void Check(DbCommand command)
        {
            if (Armed && command.CommandText.Contains("\"RoutingPins\"", StringComparison.Ordinal) && ++PinReads > 1)
                throw new InvalidOperationException("second RoutingPins read");
        }
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        { Check(command); return result; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Check(command); return ValueTask.FromResult(result); }
    }

    private sealed class Fixture(IsolatedTestSchema schema, AppDbContext db) : IAsyncDisposable
    {
        public IsolatedTestSchema Schema { get; } = schema;
        public AppDbContext Db { get; } = db;
        public string Path { get; } = Directory.CreateTempSubdirectory("c097m-create").FullName;
        public DelegationSettings Settings { get; } = new();
        public RoutingPinService Pins => new(Db, TimeProvider.System, NullLogger<RoutingPinService>.Instance);
        public static async Task<Fixture> CreateAsync(PinReadGuard? guard = null)
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            if (guard is not null) options.AddInterceptors(guard);
            var f = new Fixture(schema, new AppDbContext(options.Options));
            f.Settings.AllowedRoots = [f.Path];
            return f;
        }
        public AgentTaskService Service(AppDbContext? routingDb = null)
        {
            var options = Options.Create(Settings);
            var availability = new ModelAvailability(Db, TimeProvider.System, NullLogger<ModelAvailability>.Instance);
            return new(Db, new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance), options,
                new MockEventBus(), new RecordingSessionStopper(), TimeProvider.System, NullLogger<AgentTaskService>.Instance,
                modelAvailability: availability, routingPins: Pins,
                complexityRouting: new ComplexityRoutingService(routingDb ?? Db, options, TimeProvider.System, availability));
        }
        public Task<AgentTaskCreatedDto> Create(CreateAgentTaskRequest? request = null, Guid? parent = null) => Service().CreateAsync(
            request ?? new("plan it", Role: AgentTaskRole.Plan, Workspace: WorkspaceMode.Shared), new(null, parent, Path), default);
        public Task<AgentTask> Saved(Guid id) => Db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == id);
        public async Task<string> CreatedDetail(Guid id) => (await Db.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == id && e.Type == AgentTaskEventType.Created)).Detail!;
        public async Task NoBlocked(Guid id) => (await Db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == id && e.Type == AgentTaskEventType.Blocked)).ShouldBe(0);
        public async Task Row(AgentTaskRole role, params ComplexityCandidatePair[] candidates)
        {
            Db.ComplexityChains.Add(new ComplexityChain { Id = Guid.NewGuid(), Role = role, Complexity = TaskComplexity.Medium,
                CandidatesJson = ComplexityChain.SerializeCandidates(candidates), Provenance = RoutingPinProvenance.Human,
                Reason = "matrix test", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            await Db.SaveChangesAsync();
        }
        public async Task Hold()
        {
            Db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold { Id = Guid.NewGuid(), Kind = AgentKind.ClaudeCode,
                ModelAlias = "fable", Source = ModelAvailabilitySource.Manual, HitAt = DateTime.UtcNow, Reason = "matrix test" });
            await Db.SaveChangesAsync();
        }
        public Task<RoutingPinDto> Pin(AgentTaskRole role, bool list, AgentKind kind, AgentModelLevel level, string? card = null) => Pins.UpsertAsync(
            new(role, Card: card, Provenance: RoutingPinProvenance.Human, Strength: RoutingPinStrength.Required,
                AgentKind: list ? null : kind, ModelLevel: list ? null : level, Reason: "matrix test pin",
                Candidates: list ? [new(kind, level), new(AgentKind.ClaudeCode, AgentModelLevel.High)] : null), null, default);
        public async Task<Guid> Session()
        {
            var id = Guid.NewGuid();
            Db.AgentSessions.Add(new AgentSession { Id = id, DefinitionName = "test", AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running, Cwd = Path, Cols = 120, Rows = 30, CreatedAt = DateTime.UtcNow,
                StartedAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow });
            await Db.SaveChangesAsync();
            return id;
        }
        public async Task<AgentTask> Child(AgentTaskRole role, Guid? parent = null, Guid? card = null, AgentTaskService? service = null)
        {
            var id = Guid.NewGuid();
            var settled = new AgentTask { Id = id, RootTaskId = id, Title = "parent", Goal = "work", Kind = AgentTaskKind.Worker,
                Role = AgentTaskRole.Custom, WorkingDirectory = Path, WorktreePath = Path, WorktreeBranch = "feat/card-task-c097m",
                MergeTargetRef = "master", Workspace = WorkspaceMode.Shared, Status = AgentTaskStatus.Blocked,
                ParentSessionId = parent, CardId = card, ReplyTo = AgentTaskReplyTo.Session, CreatedAt = DateTime.UtcNow };
            Db.AgentTasks.Add(settled);
            await Db.SaveChangesAsync();
            service ??= Service();
            var child = role == AgentTaskRole.Merge ? await service.CreateMergeTaskAsync(settled, ["a.cs"], default)
                : await service.CreateCommitTaskAsync(settled, "reason", ["a.txt"], null, null, default);
            child.ShouldNotBeNull();
            await Db.SaveChangesAsync();
            return await Saved(child.Id);
        }
        public async Task Blocked(AgentTask task, AgentTaskRole role, Guid parent)
        {
            task.Status.ShouldBe(AgentTaskStatus.Blocked);
            task.Complexity.ShouldBe(TaskComplexity.Medium);
            task.RoutingPinId.ShouldBeNull();
            task.FailureReason.ShouldStartWith("routing exhausted: ");
            task.FailureReason.ShouldContain($"{role}/Medium chain");
            task.FailureReason.ShouldContain("fable");
            (await Db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Blocked)).ShouldBe(1);
            var row = await Db.SessionQueuedMessages.SingleAsync(m => m.SourceTaskId == task.Id);
            row.AgentSessionId.ShouldBe(parent);
            row.Status.ShouldBe(QueuedMessageStatus.Pending);
            row.Origin.ShouldBe(QueuedMessageOrigin.Delegation);
            row.NoteHeader.ShouldNotBeNull();
            row.ContentDigest.ShouldBe(DelegationNoteDigest.Compute(task.FailureReason));
            row.Body.ShouldContain($"{role}/Medium");
        }
        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Schema.DisposeAsync();
            Directory.Delete(Path, true);
        }
    }
}
