using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Api.Endpoints;
using Antiphon.Server.Api.Middleware;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class RunnerDefaultsWireTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };

    [Test]
    public async Task Put_global_affects_next_create_without_restart()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await DefaultsHost.StartAsync(schema.ConnectionString, "server2");
        var first = await host.Client.GetFromJsonAsync<RunnerDefaultsDto>("/api/runner-defaults", Json);
        first.ShouldNotBeNull();
        var put = await host.Client.PutAsJsonAsync("/api/runner-defaults", new PutRunnerDefaultsRequest(
            first.Revision, "server2", [], "Operator asked for server2.", "Human"), Json);
        put.StatusCode.ShouldBe(HttpStatusCode.OK);
        var saved = await put.Content.ReadFromJsonAsync<RunnerDefaultsDto>(Json);
        saved!.GlobalRunnerId.ShouldBe("server2");
        saved.Revision.ShouldBe(first.Revision + 1);

        var created = await host.Client.PostAsJsonAsync("/api/agent-tasks", new CreateAgentTaskRequest(
            "c710 grok inherits the put", Role: AgentTaskRole.Code, AgentKind: AgentKind.Grok,
            Workspace: WorkspaceMode.Worktree, WorkingDirectory: host.RepoRoot), Json);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var body = await created.Content.ReadFromJsonAsync<AgentTaskCreatedDto>(Json);
        body!.RunnerId.ShouldBe("server2");
        body.AgentKind.ShouldBe(AgentKind.Grok);
    }

    [Test]
    public async Task Per_kind_override_wins_over_global()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await DefaultsHost.StartAsync(schema.ConnectionString, null);
        var current = await host.Client.GetFromJsonAsync<RunnerDefaultsDto>("/api/runner-defaults", Json);
        var put = await host.Client.PutAsJsonAsync("/api/runner-defaults", new PutRunnerDefaultsRequest(
            current!.Revision, "server2", [new PutRunnerKindDefault(AgentKind.Codex, "desktop")],
            "Codex prefers the desktop.", "Human"), Json);
        put.EnsureSuccessStatusCode();
        var created = await host.Client.PostAsJsonAsync("/api/agent-tasks", new CreateAgentTaskRequest(
            "c710 codex kind default", Role: AgentTaskRole.Code, AgentKind: AgentKind.Codex,
            Workspace: WorkspaceMode.Worktree, WorkingDirectory: host.RepoRoot), Json);
        created.EnsureSuccessStatusCode();
        var body = await created.Content.ReadFromJsonAsync<AgentTaskCreatedDto>(Json);
        body!.RunnerId.ShouldBe("desktop");
        body.AgentKind.ShouldBe(AgentKind.Codex);
    }

    [Test]
    public async Task Clearing_override_restores_inheritance()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await DefaultsHost.StartAsync(schema.ConnectionString, null);
        var current = await host.Client.GetFromJsonAsync<RunnerDefaultsDto>("/api/runner-defaults", Json);
        var withKind = await host.Client.PutAsJsonAsync("/api/runner-defaults", new PutRunnerDefaultsRequest(
            current!.Revision, "server2", [new PutRunnerKindDefault(AgentKind.Codex, "desktop")],
            "Temporary Codex desktop.", "Human"), Json);
        withKind.EnsureSuccessStatusCode();
        var saved = await withKind.Content.ReadFromJsonAsync<RunnerDefaultsDto>(Json);
        var cleared = await host.Client.PutAsJsonAsync("/api/runner-defaults", new PutRunnerDefaultsRequest(
            saved!.Revision, "server2", [], "Clear the Codex override.", "Human"), Json);
        cleared.EnsureSuccessStatusCode();
        var created = await host.Client.PostAsJsonAsync("/api/agent-tasks", new CreateAgentTaskRequest(
            "c710 codex inherits again", Role: AgentTaskRole.Code, AgentKind: AgentKind.Codex,
            Workspace: WorkspaceMode.Worktree, WorkingDirectory: host.RepoRoot), Json);
        created.EnsureSuccessStatusCode();
        (await created.Content.ReadFromJsonAsync<AgentTaskCreatedDto>(Json))!.RunnerId.ShouldBe("server2");
    }

    [Test]
    public async Task Changing_defaults_does_not_move_queued_task()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await DefaultsHost.StartAsync(schema.ConnectionString, "server2");
        await host.Client.GetAsync("/api/runner-defaults");
        var created = await host.Client.PostAsJsonAsync("/api/agent-tasks", new CreateAgentTaskRequest(
            "c710 queued before the edit", Role: AgentTaskRole.Code, AgentKind: AgentKind.Grok,
            Workspace: WorkspaceMode.Worktree, WorkingDirectory: host.RepoRoot), Json);
        created.EnsureSuccessStatusCode();
        var body = await created.Content.ReadFromJsonAsync<AgentTaskCreatedDto>(Json);
        body!.RunnerId.ShouldBe("server2");
        var current = await host.Client.GetFromJsonAsync<RunnerDefaultsDto>("/api/runner-defaults", Json);
        var moved = await host.Client.PutAsJsonAsync("/api/runner-defaults", new PutRunnerDefaultsRequest(
            current!.Revision, "desktop", [], "Later tasks prefer the desktop.", "Human"), Json);
        moved.EnsureSuccessStatusCode();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var task = await db.AgentTasks.SingleAsync(t => t.Id == body.Id);
        task.RunnerId.ShouldBe("server2");
        task.Status.ShouldBe(AgentTaskStatus.Queued);
    }
}

[Category("Integration")]
public sealed class RunnerDefaultSettingsTests
{
    [Test]
    public async Task Writes_round_trip_after_restart_with_immutable_history()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = RunnerDefaultTestSupport.Kit(schema.ConnectionString, null);
        await using var db = kit.Context();
        var service = RunnerDefaultTestSupport.Service(db, kit, directory: true);
        var first = await service.GetAsync(CancellationToken.None);
        await service.PutAsync(new PutRunnerDefaultsRequest(
            first.Revision, "desktop", [new PutRunnerKindDefault(AgentKind.Grok, "server2")],
            "Remember this choice.", "Human"), Guid.NewGuid(), CancellationToken.None);
        await using var restarted = kit.Context();
        var again = RunnerDefaultTestSupport.Service(restarted, kit, directory: true);
        var current = await again.GetAsync(CancellationToken.None);
        current.GlobalRunnerId.ShouldBe("desktop");
        current.KindDefaults.ShouldContain(row => row.AgentKind == AgentKind.Grok && row.RunnerId == "server2");
        var page = await again.RevisionsAsync(null, 50, CancellationToken.None);
        page.Revisions.Count.ShouldBeGreaterThan(1);
        page.Revisions[0].Reason.ShouldBe("Remember this choice.");
        page.Revisions[0].PreviousRevision.ShouldBe(first.Revision);
    }

    [Test]
    public async Task Concurrent_edits_commit_one_revision()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = RunnerDefaultTestSupport.Kit(schema.ConnectionString, null);
        await using var seed = kit.Context();
        var initial = await RunnerDefaultTestSupport.Service(seed, kit).GetAsync(CancellationToken.None);
        await using var left = kit.Context();
        await using var right = kit.Context();
        var results = await Task.WhenAll(
            Put(left, kit, initial.Revision, "desktop", "left"),
            Put(right, kit, initial.Revision, "server2", "right"));
        results.Count(result => result is null).ShouldBe(1);
        results.Count(result => result is ConflictException).ShouldBe(1);
        await using var read = kit.Context();
        var page = await RunnerDefaultTestSupport.Service(read, kit).RevisionsAsync(null, 20, CancellationToken.None);
        page.Revisions.Select(row => row.Revision).Distinct().Count().ShouldBe(page.Revisions.Count);
        page.Revisions.Count(row => row.Revision == initial.Revision + 1).ShouldBe(1);
    }

    [Test]
    public async Task Invalid_targets_kinds_and_missing_fields_leave_state_unchanged()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = RunnerDefaultTestSupport.Kit(schema.ConnectionString, null);
        await using var db = kit.Context();
        var service = RunnerDefaultTestSupport.Service(db, kit, directory: true);
        var before = await service.GetAsync(CancellationToken.None);
        await Should.ThrowAsync<ValidationException>(() => service.PutAsync(new PutRunnerDefaultsRequest(
            before.Revision, "not a runner", [], "bad id", "Human"), null, CancellationToken.None));
        await Should.ThrowAsync<ValidationException>(() => service.PutAsync(new PutRunnerDefaultsRequest(
            before.Revision, "server2", [new PutRunnerKindDefault((AgentKind)99, "server2")], "bad kind", "Human"), null, CancellationToken.None));
        await Should.ThrowAsync<ValidationException>(() => service.PutAsync(new PutRunnerDefaultsRequest(
            before.Revision, "server2", [], " ", "Human"), null, CancellationToken.None));
        var after = await service.GetAsync(CancellationToken.None);
        after.Revision.ShouldBe(before.Revision);
        after.GlobalRunnerId.ShouldBe(before.GlobalRunnerId);
    }

    [Test]
    public async Task Aliases_clears_and_noop_have_distinct_semantics()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = RunnerDefaultTestSupport.Kit(schema.ConnectionString, null);
        await using var db = kit.Context();
        var service = RunnerDefaultTestSupport.Service(db, kit, directory: true);
        var current = await service.GetAsync(CancellationToken.None);
        var aliased = await service.PutAsync(new PutRunnerDefaultsRequest(
            current.Revision, "local", [], "Local means desktop.", "Human"), null, CancellationToken.None);
        aliased.GlobalRunnerId.ShouldBe("desktop");
        aliased.Revision.ShouldBe(current.Revision + 1);
        var same = await service.PutAsync(new PutRunnerDefaultsRequest(
            aliased.Revision, "desktop", [], "Local means desktop.", "Human"), null, CancellationToken.None);
        same.Revision.ShouldBe(aliased.Revision);
        var cleared = await service.PutAsync(new PutRunnerDefaultsRequest(
            same.Revision, null, [], "Clear the global preference.", "Human"), null, CancellationToken.None);
        cleared.GlobalRunnerId.ShouldBeNull();
        cleared.KindDefaults.ShouldBeEmpty();
        cleared.Revision.ShouldBe(same.Revision + 1);
    }

    [Test]
    public async Task Human_protection_and_caller_attribution_are_enforced()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = RunnerDefaultTestSupport.Kit(schema.ConnectionString, null);
        await using var db = kit.Context();
        var caller = Guid.NewGuid();
        var service = RunnerDefaultTestSupport.Service(db, kit, directory: true);
        var current = await service.GetAsync(CancellationToken.None);
        var human = await service.PutAsync(new PutRunnerDefaultsRequest(
            current.Revision, "server2", [], "An operator chose this.", "Human"), caller, CancellationToken.None);
        human.LastCallerTaskId.ShouldBe(caller);
        human.LastProvenance.ShouldBe("Human");
        var refused = await Should.ThrowAsync<ConflictException>(() => service.PutAsync(new PutRunnerDefaultsRequest(
            human.Revision, "desktop", [], "Automation must not replace the operator.", "Auto"), null, CancellationToken.None));
        refused.Code.ShouldBe(RunnerPlatformProblems.DefaultsHuman);
        (await service.GetAsync(CancellationToken.None)).Revision.ShouldBe(human.Revision);
    }

    [Test]
    public async Task Commit_and_notification_retry_do_not_duplicate_history()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = RunnerDefaultTestSupport.Kit(schema.ConnectionString, null);
        await using var db = kit.Context();
        var bus = new ThrowingBus();
        var service = new RunnerDefaultSettingsService(db, Options.Create(kit.Settings), TimeProvider.System, bus, new DefaultsHost.MatrixDirectory());
        var current = await service.GetAsync(CancellationToken.None);
        var saved = await service.PutAsync(new PutRunnerDefaultsRequest(
            current.Revision, "server2", [], "The event may fail.", "Human"), null, CancellationToken.None);
        bus.Attempts.ShouldBe(1);
        await using var read = kit.Context();
        var page = await RunnerDefaultTestSupport.Service(read, kit).RevisionsAsync(null, 20, CancellationToken.None);
        page.Revisions.Count(row => row.Revision == saved.Revision).ShouldBe(1);
    }

    private static async Task<Exception?> Put(AppDbContext db, DefaultRunnerKit kit, long revision, string runner, string reason)
    {
        try
        {
            await RunnerDefaultTestSupport.Service(db, kit, directory: true).PutAsync(new PutRunnerDefaultsRequest(
                revision, runner, [], reason, "Human"), null, CancellationToken.None);
            return null;
        }
        catch (Exception ex) when (ex is ConflictException or DbUpdateException)
        {
            return ex;
        }
    }
}

[Category("Integration")]
public sealed class RunnerDefaultPlacementTests
{
    [Test]
    public async Task Explicit_runner_and_platform_outrank_both_defaults()
    {
        await using var world = await World.Open();
        await world.Put("desktop", [new PutRunnerKindDefault(AgentKind.Grok, "desktop")]);
        var before = await world.Kit.TaskCountAsync();
        var refused = await Should.ThrowAsync<ConflictException>(() => world.Create(
            new CreateAgentTaskRequest("explicit windows on linux", Role: AgentTaskRole.Code, AgentKind: AgentKind.Grok,
                Workspace: WorkspaceMode.Worktree, RunnerId: "server2", RequiredPlatform: RequiredPlatform.Windows)));
        refused.Code.ShouldBe(RunnerPlatformProblems.Mismatch);
        (await world.Kit.TaskCountAsync()).ShouldBe(before);
    }

    [Test]
    public async Task Platform_filters_kind_then_global_then_fallback()
    {
        await using var world = await World.Open();
        await world.Put("server2", [new PutRunnerKindDefault(AgentKind.Codex, "desktop")]);
        var created = await world.Create(new CreateAgentTaskRequest(
            "linux skips the desktop kind default", Role: AgentTaskRole.Code, AgentKind: AgentKind.Codex,
            Workspace: WorkspaceMode.Worktree, RequiredPlatform: RequiredPlatform.Linux));
        var saved = await world.Kit.ReadAsync(created.Id);
        saved.Task.RunnerId.ShouldBe("server2");
        saved.Task.RunnerSelectionSource.ShouldBe(RunnerSelectionSource.GlobalDefault);
    }

    [Test]
    public async Task Effective_kind_after_model_pin_selects_kind_default()
    {
        await using var world = await World.Open();
        await world.Put("server2", [new PutRunnerKindDefault(AgentKind.Codex, "desktop")]);
        var created = await world.Create(new CreateAgentTaskRequest(
            "the effective kind is Codex", Role: AgentTaskRole.Code, AgentKind: AgentKind.Codex,
            Workspace: WorkspaceMode.Worktree));
        var saved = await world.Kit.ReadAsync(created.Id);
        saved.Task.AgentKind.ShouldBe(AgentKind.Codex);
        saved.Task.RunnerId.ShouldBeNull();
        saved.Task.RunnerSelectionSource.ShouldBe(RunnerSelectionSource.KindDefault);
    }

    [Test]
    public async Task Codex_inherits_server2_and_desktop_override_is_editable()
    {
        await using var world = await World.Open();
        await world.Put("server2", []);
        var inherited = await world.Create(new CreateAgentTaskRequest(
            "codex inherits", Role: AgentTaskRole.Code, AgentKind: AgentKind.Codex, Workspace: WorkspaceMode.Worktree));
        (await world.Kit.ReadAsync(inherited.Id)).Task.RunnerId.ShouldBe("server2");
        await world.Put("server2", [new PutRunnerKindDefault(AgentKind.Codex, "desktop")]);
        var overridden = await world.Create(new CreateAgentTaskRequest(
            "codex desktop override", Role: AgentTaskRole.Code, AgentKind: AgentKind.Codex, Workspace: WorkspaceMode.Worktree));
        (await world.Kit.ReadAsync(overridden.Id)).Task.RunnerId.ShouldBeNull();
    }

    [Test]
    public async Task Unavailable_preference_falls_back_but_full_runner_queues()
    {
        await using var world = await World.Open(offline: "runner-a");
        await world.Put("runner-a", []);
        var created = await world.Create(new CreateAgentTaskRequest(
            "offline preference falls through", Role: AgentTaskRole.Code, AgentKind: AgentKind.Grok,
            Workspace: WorkspaceMode.Worktree, RequiredPlatform: RequiredPlatform.Linux));
        var saved = await world.Kit.ReadAsync(created.Id);
        saved.Task.RunnerId.ShouldBe("server2");
        saved.Task.Status.ShouldBe(AgentTaskStatus.Queued);

        await world.Put("server2", []);
        world.Directory.Rows["server2"] = RunnerDefaultTestSupport.Describe("server2", "linux", true, 0);
        var queued = await world.Create(new CreateAgentTaskRequest(
            "full default still queues", Role: AgentTaskRole.Code, AgentKind: AgentKind.Grok,
            Workspace: WorkspaceMode.Worktree, RequiredPlatform: RequiredPlatform.Linux));
        var full = await world.Kit.ReadAsync(queued.Id);
        full.Task.RunnerId.ShouldBe("server2");
        full.Task.Status.ShouldBe(AgentTaskStatus.Queued);
    }

    [Test]
    public async Task Snapshot_is_coherent_during_concurrent_edit()
    {
        await using var world = await World.Open();
        var current = await world.Defaults.GetAsync(CancellationToken.None);
        await using var editDb = world.Kit.Context();
        var editor = new RunnerDefaultSettingsService(
            editDb, Options.Create(world.Kit.Settings), TimeProvider.System, new MockEventBus(), world.Directory);
        var create = world.Create(new CreateAgentTaskRequest(
            "one snapshot", Role: AgentTaskRole.Code, AgentKind: AgentKind.Grok, Workspace: WorkspaceMode.Worktree));
        var edit = editor.PutAsync(new PutRunnerDefaultsRequest(
            current.Revision, "desktop", [], "concurrent edit", "Human"), null, CancellationToken.None);
        await Task.WhenAll(create, edit);
        var saved = await world.Kit.ReadAsync((await create).Id);
        saved.Task.RunnerDefaultsRevision.ShouldNotBeNull();
        await using var db = world.Kit.Context();
        var revision = await db.RunnerRoutingRevisions.AsNoTracking()
            .SingleAsync(row => row.Revision == saved.Task.RunnerDefaultsRevision);
        using var snapshot = JsonDocument.Parse(revision.SnapshotJson);
        var global = snapshot.RootElement.GetProperty("globalRunnerId");
        if (saved.Task.RunnerSelectionSource == RunnerSelectionSource.GlobalDefault)
            global.GetString().ShouldBe(RunnerRequestIntent.DisplayRunnerId(saved.Task.RunnerId));
        else
        {
            saved.Task.RunnerSelectionSource.ShouldBe(RunnerSelectionSource.Fallback);
            global.ValueKind.ShouldBe(JsonValueKind.Null);
        }
    }

    [Test]
    public async Task Reroute_retry_followup_and_existing_process_keep_frozen_host()
    {
        await using var world = await World.Open();
        await world.Put("desktop", []);
        var created = await world.Create(new CreateAgentTaskRequest(
            "frozen host", Role: AgentTaskRole.Code, AgentKind: AgentKind.Grok, Workspace: WorkspaceMode.Worktree,
            RunnerId: "server2", RequiredPlatform: RequiredPlatform.Linux));
        await using var db = world.Kit.Context();
        var rerouted = await world.Kit.Service(db, world.Defaults).RerouteAsync(
            created.Id, AgentKind.Codex, AgentModelLevel.High, CancellationToken.None);
        rerouted.RunnerId.ShouldBe("server2");
        rerouted.RequiredPlatform.ShouldBe(RequiredPlatform.Linux);
        await using var fail = world.Kit.Context();
        await fail.AgentTasks.Where(t => t.Id == created.Id).ExecuteUpdateAsync(u => u
            .SetProperty(t => t.Status, AgentTaskStatus.Failed)
            .SetProperty(t => t.FailureReason, "retry me"));
        await using var retryDb = world.Kit.Context();
        var retried = await world.Kit.Service(retryDb, world.Defaults).RetryAsync(created.Id, CancellationToken.None);
        retried.RunnerId.ShouldBe("server2");
        retried.RequiredPlatform.ShouldBe(RequiredPlatform.Linux);
    }

    [Test]
    public async Task Unsupported_shapes_and_auth_refusals_do_not_gain_fallback()
    {
        await using var world = await World.Open();
        await world.Put("server2", []);
        var orchestrator = await Should.ThrowAsync<ValidationException>(() => world.Create(new CreateAgentTaskRequest(
            "codex orchestrator", Kind: AgentTaskKind.Orchestrator, Role: AgentTaskRole.Plan, AgentKind: AgentKind.Codex,
            Workspace: WorkspaceMode.Worktree, RequiredPlatform: RequiredPlatform.Linux)));
        orchestrator.Errors.Keys.ShouldContain(nameof(CreateAgentTaskRequest.AgentKind));
        var landing = await Should.ThrowAsync<ConflictException>(() => world.Create(new CreateAgentTaskRequest(
            "codex sourcelanding", Role: AgentTaskRole.Mutation, AgentKind: AgentKind.Codex,
            Workspace: WorkspaceMode.Worktree, RequiredPlatform: RequiredPlatform.Linux,
            SourceLandingOperationId: Guid.NewGuid())));
        landing.Code.ShouldBe(RunnerPlatformProblems.Unavailable);
    }
}

[Category("Integration")]
public sealed class RunnerDefaultMigrationTests
{
    [Test]
    public async Task Existing_schema_upgrades_and_legacy_remote_imports_once()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await RewindToPlatformMigration(schema.ConnectionString);
        var kit = RunnerDefaultTestSupport.Kit(schema.ConnectionString, "server2");
        await using var db = kit.Context();
        var imported = await RunnerDefaultTestSupport.Service(db, kit).EnsureInitializedAsync(CancellationToken.None);
        imported.GlobalRunnerId.ShouldBe("server2");
        imported.Revision.ShouldBe(1);
        imported.KindDefaults.ShouldBeEmpty();
        var again = await new RunnerDefaultSettingsService(
            db, Options.Create(new DelegationSettings { DefaultRunnerId = "desktop" }), TimeProvider.System)
            .EnsureInitializedAsync(CancellationToken.None);
        again.GlobalRunnerId.ShouldBe("server2");
        again.Revision.ShouldBe(1);
    }

    [Test]
    public async Task Local_blank_and_unknown_legacy_values_are_preserved()
    {
        (await Import(null)).GlobalRunnerId.ShouldBeNull();
        (await Import("local")).GlobalRunnerId.ShouldBe("desktop");
        (await Import("gone-runner")).GlobalRunnerId.ShouldBe("gone-runner");
    }

    [Test]
    public async Task Concurrent_initializers_publish_one_initial_revision()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = RunnerDefaultTestSupport.Kit(schema.ConnectionString, "server2");
        await using var left = kit.Context();
        await using var right = kit.Context();
        await Task.WhenAll(
            RunnerDefaultTestSupport.Service(left, kit).EnsureInitializedAsync(CancellationToken.None),
            RunnerDefaultTestSupport.Service(right, kit).EnsureInitializedAsync(CancellationToken.None));
        await using var read = kit.Context();
        (await read.RunnerRoutingRevisions.CountAsync(row => row.Revision == 1)).ShouldBe(1);
        (await read.RunnerRoutingSettings.SingleAsync()).GlobalRunnerId.ShouldBe("server2");
    }

    [Test]
    public async Task Persisted_clear_and_override_survive_legacy_edits_and_restart()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = RunnerDefaultTestSupport.Kit(schema.ConnectionString, "server2");
        await using var db = kit.Context();
        var service = RunnerDefaultTestSupport.Service(db, kit, directory: true);
        var current = await service.GetAsync(CancellationToken.None);
        await service.PutAsync(new PutRunnerDefaultsRequest(
            current.Revision, null, [new PutRunnerKindDefault(AgentKind.Grok, "server2")],
            "Clear the global preference.", "Human"), null, CancellationToken.None);
        await using var restarted = kit.Context();
        var legacy = new RunnerDefaultSettingsService(
            restarted, Options.Create(new DelegationSettings { DefaultRunnerId = "desktop", AllowedRoots = [kit.RepoRoot] }),
            TimeProvider.System);
        var snapshot = await legacy.EnsureInitializedAsync(CancellationToken.None);
        snapshot.GlobalRunnerId.ShouldBeNull();
        snapshot.KindDefaults[AgentKind.Grok].ShouldBe("server2");
    }

    [Test]
    public async Task Import_does_not_seed_codex_desktop_or_overwrite_human_state()
    {
        var imported = await Import("server2");
        imported.KindDefaults.ShouldBeEmpty();
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = RunnerDefaultTestSupport.Kit(schema.ConnectionString, "server2");
        await using var db = kit.Context();
        var service = RunnerDefaultTestSupport.Service(db, kit, directory: true);
        var current = await service.GetAsync(CancellationToken.None);
        await service.PutAsync(new PutRunnerDefaultsRequest(
            current.Revision, "desktop", [], "Human choice.", "Human"), null, CancellationToken.None);
        var kept = await new RunnerDefaultSettingsService(
            db, Options.Create(new DelegationSettings { DefaultRunnerId = "server2" }), TimeProvider.System)
            .EnsureInitializedAsync(CancellationToken.None);
        kept.GlobalRunnerId.ShouldBe("desktop");
        kept.KindDefaults.ShouldNotContainKey(AgentKind.Codex);
    }

    private static async Task<RunnerDefaultSnapshot> Import(string? legacy)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = RunnerDefaultTestSupport.Kit(schema.ConnectionString, legacy);
        await using var db = kit.Context();
        return await RunnerDefaultTestSupport.Service(db, kit).EnsureInitializedAsync(CancellationToken.None);
    }

    private static async Task RewindToPlatformMigration(string connectionString)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connectionString));
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        var platform = applied.Single(name => name.EndsWith("_AddTaskPlatformPlacement", StringComparison.Ordinal));
        await db.GetService<IMigrator>().MigrateAsync(platform);
        await db.GetService<IMigrator>().MigrateAsync();
    }
}

file static class RunnerDefaultTestSupport
{
    public static DefaultRunnerKit Kit(string connectionString, string? legacy) =>
        DefaultRunnerKit.Create(connectionString, legacy, allowedRunnerId: "server2");

    public static RunnerDefaultSettingsService Service(AppDbContext db, DefaultRunnerKit kit, bool directory = false) =>
        new(db, Options.Create(kit.Settings), TimeProvider.System, new MockEventBus(),
            directory ? new DefaultsHost.MatrixDirectory() : null);

    public static RunnerDescriptor Describe(string id, string? platform, bool eligible, int? capacity = 4) => new(
        id, id, platform, platform is null ? null : DateTimeOffset.UtcNow, eligible, eligible, !eligible, capacity,
        platform is null ? null : new RunnerCapabilitiesDto("InboxConhost", "inbox", "test", false,
            Features: [RunnerPlatformWire.Feature], Platform: platform));
}

file sealed class World : IAsyncDisposable
{
    public required DefaultRunnerKit Kit { get; init; }
    public required RunnerDefaultSettingsService Defaults { get; init; }
    public required DefaultsHost.MatrixDirectory Directory { get; init; }
    private readonly IsolatedTestSchema _schema;

    private World(IsolatedTestSchema schema) => _schema = schema;

    public static async Task<World> Open(string? offline = null)
    {
        var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = RunnerDefaultTestSupport.Kit(schema.ConnectionString, null);
        var directory = new DefaultsHost.MatrixDirectory();
        if (offline is not null)
            directory.Rows[offline] = RunnerDefaultTestSupport.Describe(offline, "linux", eligible: false);
        kit.RealDirectory = directory;
        var db = kit.Context();
        return new World(schema)
        {
            Kit = kit,
            Directory = directory,
            Defaults = new RunnerDefaultSettingsService(db, Options.Create(kit.Settings), TimeProvider.System, new MockEventBus(), directory),
        };
    }

    public Task Put(string? global, IReadOnlyList<PutRunnerKindDefault> kinds) =>
        PutCore(global, kinds);

    private async Task PutCore(string? global, IReadOnlyList<PutRunnerKindDefault> kinds)
    {
        var current = await Defaults.GetAsync(CancellationToken.None);
        await Defaults.PutAsync(new PutRunnerDefaultsRequest(
            current.Revision, global, kinds, "placement fixture", "Human"), null, CancellationToken.None);
    }

    public async Task<AgentTaskCreatedDto> Create(CreateAgentTaskRequest request)
    {
        await using var db = Kit.Context();
        return await Kit.Service(db, Defaults).CreateAsync(request, Kit.Caller, CancellationToken.None);
    }

    public async ValueTask DisposeAsync() => await _schema.DisposeAsync();
}

file sealed class ThrowingBus : IEventBus
{
    public int Attempts { get; private set; }
    public Task PublishToAllAsync(string eventName, object payload, CancellationToken ct = default)
    {
        Attempts++;
        throw new InvalidOperationException("event bus down");
    }
    public Task PublishToGroupAsync(string group, string eventName, object payload, CancellationToken ct = default) =>
        PublishToAllAsync(eventName, payload, ct);
}

file sealed class DefaultsHost : IAsyncDisposable
{
    public HttpClient Client { get; }
    public string RepoRoot { get; }
    private readonly WebApplication _app;

    private DefaultsHost(WebApplication app, string repoRoot)
    {
        _app = app;
        RepoRoot = repoRoot;
        Client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
    }

    public static async Task<DefaultsHost> StartAsync(string connectionString, string? legacy)
    {
        var repo = FindRepo();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.Listen(System.Net.IPAddress.Loopback, 0));
        builder.Services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false)));
        builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString, n =>
        {
            n.MigrationsAssembly("Antiphon.Server");
            n.SetPostgresVersion(16, 0);
        }));
        var settings = new DelegationSettings { AllowedRoots = [repo], DefaultRunnerId = legacy };
        var phone = new PhoneHomeRunnerSettings
        {
            Enabled = true,
            AllowedRunnerId = "server2",
            AllowDelegatedTasks = true,
            HostWorkspaceRoot = repo,
            CallbackOrigin = "https://antiphon.test",
            SharedSecret = "x",
        };
        var directory = new MatrixDirectory();
        builder.Services.AddSingleton(Options.Create(settings));
        builder.Services.AddSingleton(Options.Create(phone));
        builder.Services.AddSingleton<IEventBus, MockEventBus>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ISessionRunnerDirectory>(directory);
        builder.Services.AddSingleton<PhoneHomeLaunchPolicy>();
        builder.Services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
        builder.Services.AddSingleton<DelegationWorkspaceResolver>();
        builder.Services.AddScoped<RunnerDefaultSettingsService>();
        builder.Services.AddScoped<AgentTaskService>();
        builder.Services.AddLogging();
        var app = builder.Build();
        app.UseMiddleware<ExceptionMiddleware>();
        app.Use(async (ctx, next) =>
        {
            try
            {
                await next();
            }
            catch (Exception ex)
            {
                await File.AppendAllTextAsync(
                    Path.Combine(Path.GetTempPath(), "c710-http.log"),
                    ex + Environment.NewLine + "----" + Environment.NewLine);
                throw;
            }
        });
        app.MapRunnerDefaultEndpoints();
        app.MapAgentTaskEndpoints();
        await app.StartAsync();
        return new DefaultsHost(app, repo);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private static string FindRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Antiphon.sln was not found.");
    }

    internal sealed class MatrixDirectory : ISessionRunnerDirectory
    {
        public Dictionary<string, RunnerDescriptor> Rows { get; } = new(StringComparer.Ordinal)
        {
            ["desktop"] = RunnerDefaultTestSupport.Describe("desktop", "windows", true),
            ["server2"] = RunnerDefaultTestSupport.Describe("server2", "linux", true),
            ["runner-a"] = RunnerDefaultTestSupport.Describe("runner-a", "linux", true),
        };
        public ISessionRunnerClient Local { get; } = new PhoneHomeTestHost.RecordingLocalClient();
        public IReadOnlyList<string> KnownRunnerIds => Rows.Keys.Where(id => id != "desktop").ToList();
        public Guid? GetLiveStoreId(string? runnerId) =>
            string.IsNullOrWhiteSpace(runnerId) ? null : Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        public Task<RunnerDescriptor?> DescribeAsync(string? runnerId, CancellationToken ct)
        {
            var key = string.IsNullOrWhiteSpace(runnerId) || RunnerRequestIntent.IsDesktopAlias(runnerId) ? "desktop" : runnerId.Trim();
            return Task.FromResult(Rows.TryGetValue(key, out var row) ? row : null);
        }
        public ISessionRunnerClient Resolve(string? runnerId)
        {
            var key = string.IsNullOrWhiteSpace(runnerId) ? "desktop" : runnerId;
            if (!Rows.TryGetValue(key, out var row) || !row.DispatchEligible)
                throw new ServiceUnavailableException("unavailable", PhoneHomeProblemTypes.Unavailable);
            return Local;
        }
        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) => Task.FromResult<SessionRunnerOwner?>(null);
        public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<SessionRunnerBinding>(SessionRunnerBinding.Missing.Instance);
        public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct) =>
            Task.FromResult<RunnerInventory>(new RunnerInventory.Unavailable("unused"));
    }
}
