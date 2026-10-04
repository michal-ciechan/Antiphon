using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0710 V-4. Placement reads the runtime snapshot, filters a hard platform before a
/// preference can win, and does not insert a task when the named runner disagrees.
/// </summary>
[Category("Integration")]
public sealed class TaskPlatformPlacementTests
{
    [Test]
    public async Task C796_Explicit_desktop_codex_is_admitted()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: "server2", allowedRunnerId: "server2");
        kit.RealDirectory = Matrix();
        foreach (var runner in new[] { "local", "desktop", " Local ", "DESKTOP" })
        foreach (var required in new[] { RequiredPlatform.Any, RequiredPlatform.Windows })
        foreach (var workspace in new[] { WorkspaceMode.Worktree, WorkspaceMode.Shared, WorkspaceMode.ReadOnly })
        {
            var before = await kit.TaskCountAsync();
            await using var db = kit.Context();
            var created = await kit.Service(db).CreateAsync(
                new CreateAgentTaskRequest("c772 desktop codex", Role: AgentTaskRole.Code,
                    AgentKind: AgentKind.Codex, Workspace: workspace, RunnerId: runner,
                    RequiredPlatform: required),
                kit.Caller, CancellationToken.None);
            (await kit.TaskCountAsync()).ShouldBe(before + 1);
            var saved = await kit.ReadAsync(created.Id);
            saved.Task.Status.ShouldBe(AgentTaskStatus.Queued);
            saved.Task.AgentKind.ShouldBe(AgentKind.Codex);
            saved.Task.RunnerId.ShouldBeNull();
            saved.Task.RequiredPlatform.ShouldBe(required);
            saved.Task.Workspace.ShouldBe(workspace);
            saved.Task.RunnerSelectionSource.ShouldBe(RunnerSelectionSource.Explicit);
            saved.Task.PlacementReason.ShouldBe(DefaultRunnerRoutingPolicy.ReasonLocalRequested);
            saved.Created.ShouldContain("runner source=explicit-local");
            DefaultRunnerKit.Occurrences(saved.Created, "runner source=").ShouldBe(1);
        }
    }

    [Test]
    public async Task C796_Defaulted_desktop_codex_is_admitted()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        foreach (var configured in new string?[] { null, "desktop", "local" })
        {
            var kit = DefaultRunnerKit.Create(schema.ConnectionString, configured, allowedRunnerId: "server2");
            kit.RealDirectory = Matrix();
            var before = await kit.TaskCountAsync();
            await using var db = kit.Context();
            var created = await kit.Service(db).CreateAsync(
                new CreateAgentTaskRequest("c772 default desktop", Role: AgentTaskRole.Code,
                    AgentKind: AgentKind.Codex, Workspace: WorkspaceMode.Worktree),
                kit.Caller, CancellationToken.None);
            (await kit.TaskCountAsync()).ShouldBe(before + 1);
            var saved = await kit.ReadAsync(created.Id);
            saved.Task.Status.ShouldBe(AgentTaskStatus.Queued);
            saved.Task.AgentKind.ShouldBe(AgentKind.Codex);
            saved.Task.RunnerId.ShouldBeNull();
            saved.Task.RunnerSelectionSource.ShouldBeNull();
            saved.Task.RunnerDefaultsRevision.ShouldBeNull();
        }

        var runtimeKit = DefaultRunnerKit.Create(schema.ConnectionString, "server2", allowedRunnerId: "server2");
        var directory = Matrix();
        runtimeKit.RealDirectory = directory;
        await using var defaultsDb = runtimeKit.Context();
        var defaults = new RunnerDefaultSettingsService(defaultsDb, Options.Create(runtimeKit.Settings),
            TimeProvider.System, new MockEventBus());
        var snapshot = await defaults.GetAsync(CancellationToken.None);
        await defaults.PutAsync(new PutRunnerDefaultsRequest(snapshot.Revision, "server2",
            [new PutRunnerKindDefault(AgentKind.Codex, "desktop")], "Codex desktop preference", "Human"),
            null, CancellationToken.None);
        await AssertDefaultAdmissionAsync(runtimeKit, defaults, RunnerSelectionSource.KindDefault);

        snapshot = await defaults.GetAsync(CancellationToken.None);
        await defaults.PutAsync(new PutRunnerDefaultsRequest(snapshot.Revision, "desktop", [],
            "Desktop global preference", "Human"), null, CancellationToken.None);
        await AssertDefaultAdmissionAsync(runtimeKit, defaults, RunnerSelectionSource.GlobalDefault);

        snapshot = await defaults.GetAsync(CancellationToken.None);
        await defaults.PutAsync(new PutRunnerDefaultsRequest(snapshot.Revision, null, [],
            "No preference", "Human"), null, CancellationToken.None);
        await AssertDefaultAdmissionAsync(runtimeKit, defaults, null);

        directory.Rows["server2"] = Describe("server2", "linux", eligible: false);
        snapshot = await defaults.GetAsync(CancellationToken.None);
        await defaults.PutAsync(new PutRunnerDefaultsRequest(snapshot.Revision, "server2", [],
            "Unavailable remote falls back locally", "Human"), null, CancellationToken.None);
        await AssertDefaultAdmissionAsync(runtimeKit, defaults, RunnerSelectionSource.GlobalDefault);
    }

    private static async Task AssertDefaultAdmissionAsync(DefaultRunnerKit kit, RunnerDefaultSettingsService defaults,
        RunnerSelectionSource? expectedSource)
    {
        var before = await kit.TaskCountAsync();
        await using var db = kit.Context();
        var created = await kit.Service(db, defaults).CreateAsync(
            new CreateAgentTaskRequest("c772 runtime default", Role: AgentTaskRole.Code,
                AgentKind: AgentKind.Codex, Workspace: WorkspaceMode.Worktree),
            kit.Caller, CancellationToken.None);
        (await kit.TaskCountAsync()).ShouldBe(before + 1);
        var saved = await kit.ReadAsync(created.Id);
        saved.Task.Status.ShouldBe(AgentTaskStatus.Queued);
        saved.Task.AgentKind.ShouldBe(AgentKind.Codex);
        saved.Task.RunnerId.ShouldBeNull();
        saved.Task.RunnerSelectionSource.ShouldBe(expectedSource);
        saved.Task.RunnerDefaultsRevision.ShouldNotBeNull();
        DefaultRunnerKit.Occurrences(saved.Created, "runner source=").ShouldBe(expectedSource is null ? 0 : 1);
    }

    [Test]
    public async Task C796_Windows_fallback_selects_desktop_without_linux_reroute()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, "server2", allowedRunnerId: "server2");
        kit.RealDirectory = Matrix();
        var before = await kit.TaskCountAsync();
        await using var db = kit.Context();
        var created = await kit.Service(db).CreateAsync(
            new CreateAgentTaskRequest("c772 Windows needs Windows", Role: AgentTaskRole.Code,
                AgentKind: AgentKind.Codex, Workspace: WorkspaceMode.Worktree,
                RequiredPlatform: RequiredPlatform.Windows), kit.Caller, CancellationToken.None);
        (await kit.TaskCountAsync()).ShouldBe(before + 1);
        var saved = await kit.ReadAsync(created.Id);
        saved.Task.Status.ShouldBe(AgentTaskStatus.Queued);
        saved.Task.AgentKind.ShouldBe(AgentKind.Codex);
        saved.Task.RunnerId.ShouldBeNull();
        saved.Task.RequiredPlatform.ShouldBe(RequiredPlatform.Windows);
        saved.Task.RunnerSelectionSource.ShouldBe(RunnerSelectionSource.Fallback);
        saved.Task.PlacementReason.ShouldBe("platform_match");
        saved.Created.ShouldContain("runner source=fallback");
    }

    [Test]
    public async Task C796_Resolved_codex_kind_is_admitted()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, null, allowedRunnerId: "server2");
        kit.RealDirectory = Matrix();
        await using (var seed = kit.Context())
        {
            seed.RoutingPins.Add(new RoutingPin
            {
                Id = Guid.NewGuid(), Role = AgentTaskRole.Code,
                Provenance = RoutingPinProvenance.Human, Strength = RoutingPinStrength.Required,
                CandidatesJson = RoutingCandidate.Serialize(
                    [new RoutingCandidate(AgentKind.Codex, AgentModelLevel.Frontier)]),
                Reason = "c772 resolved kind", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            await seed.SaveChangesAsync();
        }
        await using var db = kit.Context();
        var before = await kit.TaskCountAsync();
        var created = await kit.Service(db, withRouting: true).CreateAsync(
            new CreateAgentTaskRequest("c772 resolved kind", Role: AgentTaskRole.Code,
                Workspace: WorkspaceMode.Worktree),
            kit.Caller, CancellationToken.None);
        (await kit.TaskCountAsync()).ShouldBe(before + 1);
        var saved = await kit.ReadAsync(created.Id);
        saved.Task.Status.ShouldBe(AgentTaskStatus.Queued);
        saved.Task.AgentKind.ShouldBe(AgentKind.Codex);
        saved.Task.RunnerId.ShouldBeNull();

        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using (var seed = kit.Context())
        {
            seed.AgentSessions.Add(new AgentSession
            {
                Id = sessionId, DefinitionName = "codex", AgentKind = AgentKind.Codex,
                Status = SessionStatus.Running, Cwd = kit.RepoRoot, Cols = 80, Rows = 24,
                CreatedAt = now, StartedAt = now, LastSeenAt = now,
            });
            seed.Agents.Add(new Agent
            {
                Id = agentId, Name = "c772-existing-codex", Slug = "c772-existing-codex",
                Details = "retained desktop process", WorkingDirectory = kit.RepoRoot,
                Status = AgentStatus.Idle, Kind = AgentKind.Codex,
                ModelLevel = AgentModelLevel.Frontier, PersistentSessionId = sessionId.ToString("D"),
                CreatedAt = now, UpdatedAt = now,
            });
            await seed.SaveChangesAsync();
        }
        await using var retainedDb = kit.Context();
        var retained = await kit.Service(retainedDb, withRouting: true).CreateAsync(
                new CreateAgentTaskRequest("c772 retained codex", Role: AgentTaskRole.Code,
                    AgentId: agentId, Workspace: WorkspaceMode.Shared), kit.Caller, CancellationToken.None);
        (await kit.TaskCountAsync()).ShouldBe(before + 2);
        var retainedSaved = await kit.ReadAsync(retained.Id);
        retainedSaved.Task.Status.ShouldBe(AgentTaskStatus.Queued);
        retainedSaved.Task.AgentKind.ShouldBe(AgentKind.Codex);
        retainedSaved.Task.RunnerId.ShouldBeNull();
        retainedSaved.Task.AgentId.ShouldBe(agentId);
        await using var verify = kit.Context();
        (await verify.AgentSessions.SingleAsync(s => s.Id == sessionId)).Status.ShouldBe(SessionStatus.Running);
        (await verify.Agents.SingleAsync(a => a.Id == agentId)).Status.ShouldBe(AgentStatus.Idle);
    }

    [Test]
    public async Task C772_Desktop_claude_and_grok_still_create()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, null, allowedRunnerId: "server2");
        kit.RealDirectory = Matrix();
        foreach (var kind in new[] { AgentKind.ClaudeCode, AgentKind.Grok })
        foreach (var runner in new[] { "local", "desktop" })
        {
            await using var db = kit.Context();
            var created = await kit.Service(db).CreateAsync(
                new CreateAgentTaskRequest("c772 allowed desktop " + Guid.NewGuid().ToString("N"),
                    Role: AgentTaskRole.Code, AgentKind: kind, Workspace: WorkspaceMode.Worktree,
                    RunnerId: runner, RequiredPlatform: RequiredPlatform.Windows),
                kit.Caller, CancellationToken.None);
            var saved = await kit.ReadAsync(created.Id);
            saved.Task.Status.ShouldBe(AgentTaskStatus.Queued);
            saved.Task.AgentKind.ShouldBe(kind);
            saved.Task.RunnerId.ShouldBeNull();
            saved.Task.RequiredPlatform.ShouldBe(RequiredPlatform.Windows);
        }
    }

    [Test]
    public async Task C772_Server2_codex_still_creates()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, "server2", allowedRunnerId: "server2");
        kit.RealDirectory = Matrix();
        foreach (var runner in new string?[] { "server2", null })
        {
            await using var db = kit.Context();
            var created = await kit.Service(db).CreateAsync(
                new CreateAgentTaskRequest("c772 remote codex " + Guid.NewGuid().ToString("N"),
                    Role: AgentTaskRole.Code, AgentKind: AgentKind.Codex,
                    Workspace: WorkspaceMode.Worktree, RunnerId: runner, RequiredPlatform: RequiredPlatform.Linux),
                kit.Caller, CancellationToken.None);
            var saved = await kit.ReadAsync(created.Id);
            saved.Task.Status.ShouldBe(AgentTaskStatus.Queued);
            saved.Task.AgentKind.ShouldBe(AgentKind.Codex);
            saved.Task.RunnerId.ShouldBe("server2");
        }
    }

    [Test]
    public async Task C772_Platform_refusals_keep_precedence()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, null, allowedRunnerId: "server2");
        kit.RealDirectory = Matrix();
        foreach (var (runner, required) in new[]
                 { ("desktop", RequiredPlatform.Linux), ("server2", RequiredPlatform.Windows) })
        {
            await using var db = kit.Context();
            var before = await kit.TaskCountAsync();
            var refused = await Should.ThrowAsync<ConflictException>(() => kit.Service(db).CreateAsync(
                new CreateAgentTaskRequest("c772 mismatch", Role: AgentTaskRole.Code,
                    AgentKind: AgentKind.Codex, Workspace: WorkspaceMode.Worktree,
                    RunnerId: runner, RequiredPlatform: required), kit.Caller, CancellationToken.None));
            refused.Code.ShouldBe(RunnerPlatformProblems.Mismatch);
            (await kit.TaskCountAsync()).ShouldBe(before);
        }

        var unknown = Matrix();
        unknown.Rows["server2"] = Describe("server2", null, eligible: true);
        kit.RealDirectory = unknown;
        await using var unknownDb = kit.Context();
        var beforeUnknown = await kit.TaskCountAsync();
        var unknownRefusal = await Should.ThrowAsync<ConflictException>(() => kit.Service(unknownDb).CreateAsync(
            new CreateAgentTaskRequest("c772 unknown platform", Role: AgentTaskRole.Code,
                AgentKind: AgentKind.Codex, Workspace: WorkspaceMode.Worktree,
                RunnerId: "server2", RequiredPlatform: RequiredPlatform.Windows),
            kit.Caller, CancellationToken.None));
        unknownRefusal.Code.ShouldBe(RunnerPlatformProblems.Unknown);
        (await kit.TaskCountAsync()).ShouldBe(beforeUnknown);
    }

    [Test]
    public async Task Specific_platform_filters_candidates_in_order()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: null, allowedRunnerId: "server2");
        kit.RealDirectory = Matrix();
        await using var db = kit.Context();
        var defaults = new RunnerDefaultSettingsService(db, Options.Create(kit.Settings), TimeProvider.System, new MockEventBus());
        var current = await defaults.GetAsync(CancellationToken.None);
        await defaults.PutAsync(new PutRunnerDefaultsRequest(
            current.Revision, "server2",
            [new PutRunnerKindDefault(AgentKind.Codex, "desktop")],
            "Codex prefers the desktop; Linux must not follow it.",
            "Human"), null, CancellationToken.None);
        await using var createDb = kit.Context();

        var created = await kit.Service(createDb, defaults).CreateAsync(
            new CreateAgentTaskRequest(
                "c710 linux past desktop kind default",
                Role: AgentTaskRole.Code,
                AgentKind: AgentKind.Codex,
                Workspace: WorkspaceMode.Worktree,
                RequiredPlatform: RequiredPlatform.Linux),
            kit.Caller, CancellationToken.None);

        var saved = await kit.ReadAsync(created.Id);
        saved.Task.RunnerId.ShouldBe("server2");
        saved.Task.RequiredPlatform.ShouldBe(RequiredPlatform.Linux);
        saved.Task.RunnerSelectionSource.ShouldBe(RunnerSelectionSource.GlobalDefault);
        saved.Task.Status.ShouldBe(AgentTaskStatus.Queued);
    }

    [Test]
    public async Task Explicit_mismatch_has_no_side_effects()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: null, allowedRunnerId: "server2");
        kit.RealDirectory = Matrix();
        await using var db = kit.Context();
        var before = await kit.TaskCountAsync();

        var refused = await Should.ThrowAsync<ConflictException>(() => kit.Service(db).CreateAsync(
            new CreateAgentTaskRequest(
                "c710 windows on server2",
                Role: AgentTaskRole.Code,
                AgentKind: AgentKind.Grok,
                Workspace: WorkspaceMode.Worktree,
                RunnerId: "server2",
                RequiredPlatform: RequiredPlatform.Windows),
            kit.Caller, CancellationToken.None));

        refused.Code.ShouldBe(RunnerPlatformProblems.Mismatch);
        (await kit.TaskCountAsync()).ShouldBe(before);
    }

    [Test]
    public async Task Codex_worker_inherits_global_server2()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: "server2", allowedRunnerId: "server2");
        kit.RealDirectory = Matrix();
        await using var db = kit.Context();
        var defaults = new RunnerDefaultSettingsService(db, Options.Create(kit.Settings), TimeProvider.System, new MockEventBus());
        await defaults.EnsureInitializedAsync(CancellationToken.None);
        await using var createDb = kit.Context();

        var created = await kit.Service(createDb, defaults).CreateAsync(
            new CreateAgentTaskRequest(
                "c710 any codex inherits server2",
                Role: AgentTaskRole.Code,
                AgentKind: AgentKind.Codex,
                Workspace: WorkspaceMode.Worktree),
            kit.Caller, CancellationToken.None);

        var saved = await kit.ReadAsync(created.Id);
        saved.Task.RunnerId.ShouldBe("server2");
        saved.Task.AgentKind.ShouldBe(AgentKind.Codex);
        saved.Task.RunnerSelectionSource.ShouldBe(RunnerSelectionSource.GlobalDefault);
        saved.Task.Status.ShouldBe(AgentTaskStatus.Queued);
    }

    [Test]
    public async Task Any_keeps_default_and_local_fallback()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: null, allowedRunnerId: "server2");
        kit.RealDirectory = Matrix();
        await using var db = kit.Context();
        var defaults = new RunnerDefaultSettingsService(db, Options.Create(kit.Settings), TimeProvider.System, new MockEventBus());
        var current = await defaults.GetAsync(CancellationToken.None);
        await defaults.PutAsync(new PutRunnerDefaultsRequest(
            current.Revision, "server2",
            [new PutRunnerKindDefault(AgentKind.Codex, "desktop")],
            "Codex stays on the desktop; other kinds inherit server2.",
            "Human"), null, CancellationToken.None);

        await using var grokDb = kit.Context();
        var grok = await kit.Service(grokDb, defaults).CreateAsync(
            new CreateAgentTaskRequest("c710 any grok", Role: AgentTaskRole.Code, AgentKind: AgentKind.Grok, Workspace: WorkspaceMode.Worktree),
            kit.Caller, CancellationToken.None);
        var grokSaved = await kit.ReadAsync(grok.Id);
        grokSaved.Task.RunnerId.ShouldBe("server2");
        grokSaved.Task.RequiredPlatform.ShouldBe(RequiredPlatform.Any);
        grokSaved.Task.RunnerSelectionSource.ShouldBe(RunnerSelectionSource.GlobalDefault);

        await using var codexDb = kit.Context();
        var beforeCodex = await kit.TaskCountAsync();
        var codex = await kit.Service(codexDb, defaults).CreateAsync(
            new CreateAgentTaskRequest("c710 any codex desktop override", Role: AgentTaskRole.Code, AgentKind: AgentKind.Codex, Workspace: WorkspaceMode.Worktree),
            kit.Caller, CancellationToken.None);
        (await kit.TaskCountAsync()).ShouldBe(beforeCodex + 1);
        var codexSaved = await kit.ReadAsync(codex.Id);
        codexSaved.Task.Status.ShouldBe(AgentTaskStatus.Queued);
        codexSaved.Task.RunnerId.ShouldBeNull();
        codexSaved.Task.RunnerSelectionSource.ShouldBe(RunnerSelectionSource.KindDefault);

        await using var clearDb = kit.Context();
        var cleared = await defaults.GetAsync(CancellationToken.None);
        await defaults.PutAsync(new PutRunnerDefaultsRequest(
            cleared.Revision, null, [], "Clear every preference.", "Human"), null, CancellationToken.None);
        var local = await kit.Service(clearDb, defaults).CreateAsync(
            new CreateAgentTaskRequest("c710 any with no default", Role: AgentTaskRole.Code, AgentKind: AgentKind.Grok, Workspace: WorkspaceMode.Worktree),
            kit.Caller, CancellationToken.None);
        var localSaved = await kit.ReadAsync(local.Id);
        localSaved.Task.RunnerId.ShouldBeNull("a null global uses the built-in desktop fallback and does not scan other runners");
    }

    [Test]
    public async Task Unknown_platform_refuses_specific_only()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: null, allowedRunnerId: "server2");
        var directory = Matrix();
        directory.Rows["server2"] = Describe("server2", null, eligible: true);
        kit.RealDirectory = directory;
        await using var db = kit.Context();
        var defaults = new RunnerDefaultSettingsService(db, Options.Create(kit.Settings), TimeProvider.System, new MockEventBus());
        var current = await defaults.GetAsync(CancellationToken.None);
        await defaults.PutAsync(new PutRunnerDefaultsRequest(
            current.Revision, "server2", [], "Unknown platform is still the named default.", "Human"), null, CancellationToken.None);
        var before = await kit.TaskCountAsync();

        var refused = await Should.ThrowAsync<ConflictException>(() => kit.Service(db, defaults).CreateAsync(
            new CreateAgentTaskRequest(
                "c710 windows on unknown",
                Role: AgentTaskRole.Code,
                AgentKind: AgentKind.Grok,
                Workspace: WorkspaceMode.Worktree,
                RunnerId: "server2",
                RequiredPlatform: RequiredPlatform.Windows),
            kit.Caller, CancellationToken.None));
        refused.Code.ShouldBe(RunnerPlatformProblems.Unknown);
        (await kit.TaskCountAsync()).ShouldBe(before);

        await using var anyDb = kit.Context();
        var created = await kit.Service(anyDb, defaults).CreateAsync(
            new CreateAgentTaskRequest("c710 any may keep an unknown host", Role: AgentTaskRole.Code, AgentKind: AgentKind.Grok, Workspace: WorkspaceMode.Worktree),
            kit.Caller, CancellationToken.None);
        var saved = await kit.ReadAsync(created.Id);
        saved.Task.RunnerId.ShouldBe("server2");
        saved.Task.ObservedPlatform.ShouldBeNull();
        saved.Task.RequiredPlatform.ShouldBe(RequiredPlatform.Any);
    }

    [Test]
    public async Task Shapes_and_codex_worker_admission_are_preserved()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: null, allowedRunnerId: "server2");
        kit.RealDirectory = Matrix();
        await using var db = kit.Context();
        var defaults = new RunnerDefaultSettingsService(db, Options.Create(kit.Settings), TimeProvider.System, new MockEventBus());
        var current = await defaults.GetAsync(CancellationToken.None);
        await defaults.PutAsync(new PutRunnerDefaultsRequest(
            current.Revision, "server2", [], "Supported workers inherit server2.", "Human"), null, CancellationToken.None);

        await using var workerDb = kit.Context();
        var worker = await kit.Service(workerDb, defaults).CreateAsync(
            new CreateAgentTaskRequest(
                "c710 codex linux worker",
                Role: AgentTaskRole.Code,
                AgentKind: AgentKind.Codex,
                Workspace: WorkspaceMode.Worktree,
                RequiredPlatform: RequiredPlatform.Linux),
            kit.Caller, CancellationToken.None);
        (await kit.ReadAsync(worker.Id)).Task.RunnerId.ShouldBe("server2");

        var orchestrator = await Should.ThrowAsync<ValidationException>(() => kit.Service(db, defaults).CreateAsync(
            new CreateAgentTaskRequest(
                "c710 codex orchestrator",
                Kind: AgentTaskKind.Orchestrator,
                Role: AgentTaskRole.Plan,
                AgentKind: AgentKind.Codex,
                Workspace: WorkspaceMode.Worktree,
                RequiredPlatform: RequiredPlatform.Linux),
            kit.Caller, CancellationToken.None));
        orchestrator.Errors.Keys.ShouldContain(nameof(CreateAgentTaskRequest.AgentKind));

        var landing = await Should.ThrowAsync<ConflictException>(() => kit.Service(db, defaults).CreateAsync(
            new CreateAgentTaskRequest(
                "c710 codex sourcelanding",
                Role: AgentTaskRole.Mutation,
                AgentKind: AgentKind.Codex,
                Workspace: WorkspaceMode.Worktree,
                RequiredPlatform: RequiredPlatform.Linux,
                SourceLandingOperationId: Guid.NewGuid()),
            kit.Caller, CancellationToken.None));
        landing.Code.ShouldBe(RunnerPlatformProblems.Unavailable);
    }

    [Test]
    public async Task Full_runner_is_selected_and_queued()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: null, allowedRunnerId: "server2");
        var directory = Matrix();
        directory.Rows["server2"] = Describe("server2", "linux", eligible: true, capacity: 0);
        directory.Rows["runner-b"] = Describe("runner-b", "linux", eligible: true, capacity: 4);
        kit.RealDirectory = directory;
        await using var db = kit.Context();
        var defaults = new RunnerDefaultSettingsService(db, Options.Create(kit.Settings), TimeProvider.System, new MockEventBus());
        var current = await defaults.GetAsync(CancellationToken.None);
        await defaults.PutAsync(new PutRunnerDefaultsRequest(
            current.Revision, "server2", [], "A full default still wins.", "Human"), null, CancellationToken.None);

        var created = await kit.Service(db, defaults).CreateAsync(
            new CreateAgentTaskRequest(
                "c710 full runner queues",
                Role: AgentTaskRole.Code,
                AgentKind: AgentKind.Grok,
                Workspace: WorkspaceMode.Worktree,
                RequiredPlatform: RequiredPlatform.Linux),
            kit.Caller, CancellationToken.None);
        var saved = await kit.ReadAsync(created.Id);
        saved.Task.RunnerId.ShouldBe("server2");
        saved.Task.Status.ShouldBe(AgentTaskStatus.Queued);
    }

    [Test]
    public async Task Existing_process_is_never_relocated()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: null, allowedRunnerId: "server2");
        kit.RealDirectory = Matrix();
        var agentId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var priorId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using (var seed = kit.Context())
        {
            seed.AgentSessions.Add(new AgentSession
            {
                Id = sessionId,
                DefinitionName = "grok",
                AgentKind = AgentKind.Grok,
                Status = SessionStatus.Running,
                Cwd = kit.RepoRoot,
                Cols = 80,
                Rows = 24,
                CreatedAt = now,
                StartedAt = now,
                LastSeenAt = now,
                RunnerId = "server2",
                RunnerStoreId = Guid.NewGuid(),
                RunnerCwd = kit.RepoRoot,
            });
            seed.Agents.Add(new Agent
            {
                Id = agentId,
                Name = "c710-seat",
                Slug = "c710-seat",
                WorkingDirectory = kit.RepoRoot,
                Details = "existing remote process",
                Status = AgentStatus.Idle,
                Kind = AgentKind.Grok,
                ModelLevel = AgentModelLevel.Medium,
                PersistentSessionId = sessionId.ToString("D"),
                RunnerId = "server2",
                CreatedAt = now,
                UpdatedAt = now,
            });
            seed.AgentTasks.Add(new AgentTask
            {
                Id = priorId,
                RootTaskId = priorId,
                Title = "prior",
                Goal = "prior goal",
                Kind = AgentTaskKind.Worker,
                Role = AgentTaskRole.Code,
                AgentKind = AgentKind.Grok,
                ModelLevel = AgentModelLevel.Medium,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = kit.RepoRoot,
                RunnerId = "server2",
                RequiredPlatform = RequiredPlatform.Linux,
                AgentId = agentId,
                AgentSessionId = sessionId,
                Status = AgentTaskStatus.Succeeded,
                ReplyTo = AgentTaskReplyTo.None,
                CreatedAt = now,
                CompletedAt = now,
                ConcurrencyToken = Guid.NewGuid(),
            });
            await seed.SaveChangesAsync();
        }

        await using var db = kit.Context();
        var defaults = new RunnerDefaultSettingsService(db, Options.Create(kit.Settings), TimeProvider.System, new MockEventBus());
        var current = await defaults.GetAsync(CancellationToken.None);
        await defaults.PutAsync(new PutRunnerDefaultsRequest(
            current.Revision, "desktop", [], "A later desktop preference must not move the live process.", "Human"), null, CancellationToken.None);

        await using var followDb = kit.Context();
        AgentTaskCreatedDto? created = null;
        await Should.NotThrowAsync(async () => created = await kit.Service(followDb, defaults).CreateAsync(
            new CreateAgentTaskRequest("c710 follow the live process", FollowUpOnTask: priorId.ToString("D")),
            kit.Caller, CancellationToken.None), "remote-standing-follow-up-admitted");
        created.ShouldNotBeNull();
        var saved = await kit.ReadAsync(created.Id);
        saved.Task.RunnerId.ShouldBe("server2");
        saved.Task.RequiredPlatform.ShouldBe(RequiredPlatform.Linux);
        saved.Task.RequirementSource.ShouldBe(RequirementSource.FollowUp);
        saved.Task.RunnerSelectionSource.ShouldBe(RunnerSelectionSource.ExistingProcess);
        saved.Task.AgentId.ShouldBe(agentId);
    }

    private static MatrixDirectory Matrix()
    {
        var directory = new MatrixDirectory();
        directory.Rows["desktop"] = Describe("desktop", "windows", eligible: true);
        directory.Rows["server2"] = Describe("server2", "linux", eligible: true);
        return directory;
    }

    private static RunnerDescriptor Describe(string id, string? platform, bool eligible, int? capacity = 4) => new(
        id, id, platform, platform is null ? null : DateTimeOffset.UtcNow, eligible, eligible, !eligible, capacity,
        platform is null ? null : new RunnerCapabilitiesDto("InboxConhost", "inbox", "test", false,
            Features: [RunnerPlatformWire.Feature], Platform: platform));

    private sealed class MatrixDirectory : ISessionRunnerDirectory
    {
        public Dictionary<string, RunnerDescriptor> Rows { get; } = new(StringComparer.Ordinal);
        public ISessionRunnerClient Local { get; } = new StubClient();
        public IReadOnlyList<string> KnownRunnerIds => Rows.Keys.ToList();
        public Guid? GetLiveStoreId(string? runnerId) =>
            string.IsNullOrWhiteSpace(runnerId) ? null : Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        public Task<RunnerDescriptor?> DescribeAsync(string? runnerId, CancellationToken ct)
        {
            var key = string.IsNullOrWhiteSpace(runnerId) || RunnerRequestIntent.IsDesktopAlias(runnerId)
                ? "desktop" : runnerId.Trim();
            return Task.FromResult(Rows.TryGetValue(key, out var row) ? row : null);
        }

        public ISessionRunnerClient Resolve(string? runnerId)
        {
            if (runnerId is null || !Rows.TryGetValue(runnerId, out var row) || !row.DispatchEligible)
                throw new ServiceUnavailableException("Phone-home runner is unavailable.", PhoneHomeProblemTypes.Unavailable);
            return Local;
        }

        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<SessionRunnerOwner?>(null);
        public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<SessionRunnerBinding>(SessionRunnerBinding.Missing.Instance);
        public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct) =>
            throw new InvalidOperationException("placement must not list inventory");

        private sealed class StubClient : ISessionRunnerClient
        {
            public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) =>
                throw new NotSupportedException();
            public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
                throw new NotSupportedException();
            public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
                throw new NotSupportedException();
            public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
                throw new NotSupportedException();
            public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
                throw new NotSupportedException();
            public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
                throw new NotSupportedException();
            public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) => Task.CompletedTask;
            public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => Task.CompletedTask;
            public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => Task.CompletedTask;
            public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) =>
                throw new NotSupportedException();
            public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct) => Empty();
            private static async IAsyncEnumerable<SessionRunnerEvent> Empty()
            {
                await Task.CompletedTask;
                yield break;
            }
        }
    }
}
