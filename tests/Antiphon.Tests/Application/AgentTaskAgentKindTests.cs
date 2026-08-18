using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0084 S2: a delegated task carries WHICH AGENT PROGRAM runs it, and the caller may choose
/// Grok explicitly. Everything here is about the two directions that matter: an omitted choice must
/// behave EXACTLY as it did before this column existed (ClaudeCode, no new failure mode), and an
/// explicit choice must be honoured or refused with a reason — never quietly substituted, because a
/// caller who asked for Grok and silently got Claude has no way to tell.
/// </summary>
[Category("Integration")]
[NotInParallel("AgentQueue")]
public class AgentTaskAgentKindTests
{
    // ---- the default: nothing changes for an existing caller --------------------------------

    [Test]
    public async Task a_task_created_without_a_kind_runs_on_ClaudeCode()
    {
        // The whole compatibility promise of the slice, asserted on all three surfaces a caller
        // can see it through: the created DTO, the stored row, and the summary the board reads.
        await using var db = CreateContext();
        using var workspace = new TempWorkspace();

        var created = await CreateService(db).CreateAsync(
            new CreateAgentTaskRequest(Goal: "run the suite", Role: AgentTaskRole.Test),
            ManualCaller(workspace.Path),
            CancellationToken.None);

        created.AgentKind.ShouldBe(AgentKind.ClaudeCode);

        await using var verify = CreateContext();
        var row = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id);
        row.AgentKind.ShouldBe(AgentKind.ClaudeCode);

        var summary = await CreateService(verify).GetSummaryAsync(row, [row]);
        summary.AgentKind.ShouldBe(AgentKind.ClaudeCode);
    }

    [Test]
    public async Task the_role_policy_kind_ships_unset_on_every_role()
    {
        // The promotion seam is deliberately INERT as shipped (plan §4): Code/Debug stay on Claude
        // until real Grok worker mileage says otherwise. If a default ever appears here, every
        // existing delegation silently changes program.
        var shipped = new DelegationSettings();

        shipped.RolePolicy.Values.ShouldAllBe(p => p.Kind == null);
    }

    [Test]
    public async Task a_row_written_without_the_column_reads_back_as_ClaudeCode()
    {
        // The migration's backfill, asserted against the DATABASE rather than the C# initialiser:
        // every task that existed before CARD-0084 really did run on Claude, so the column default
        // is a record of fact. Inserted with raw SQL precisely because EF would supply the value.
        var id = Guid.NewGuid();
        await using (var seed = CreateContext())
        {
            await seed.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "AgentTasks"
                    ("Id", "RootTaskId", "Depth", "Title", "Goal", "Kind", "Role", "ModelLevel",
                     "Workspace", "WorkingDirectory", "Status", "ReplyTo", "Attempt", "MaxAttempts",
                     "Ephemeral", "ConcurrencyToken", "CreatedAt", "TokensIn", "TokensOut",
                     "CacheReadTokens", "CacheCreationTokens", "CostUsd", "CostPricingVersion",
                     "ExpectedDurationMinutes", "CheckCount")
                VALUES
                    ({0}, {0}, 0, 'Pre-CARD-0084 row', 'Seeded before the column existed.', 0, 0, 1,
                     0, 'C:/pre-existing', 4, 0, 1, 2, false, {1}, {2}, 0, 0, 0, 0, 0, 0, 10, 0)
                """,
                id, Guid.NewGuid(), DateTime.UtcNow);
        }

        await using var verify = CreateContext();
        var row = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == id);
        row.AgentKind.ShouldBe(AgentKind.ClaudeCode);
    }

    // ---- the explicit opt-in ------------------------------------------------------------------

    [Test]
    public async Task an_explicit_Grok_worker_is_persisted_and_recorded()
    {
        await using var db = CreateContext();
        using var workspace = new TempWorkspace();

        var created = await CreateService(db).CreateAsync(
            new CreateAgentTaskRequest(Goal: "run the suite", Role: AgentTaskRole.Test)
            {
                AgentKind = AgentKind.Grok,
            },
            ManualCaller(workspace.Path),
            CancellationToken.None);

        created.AgentKind.ShouldBe(AgentKind.Grok);

        await using var verify = CreateContext();
        (await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id))
            .AgentKind.ShouldBe(AgentKind.Grok);

        // The timeline is where a human reconstructs "why did this run on Grok" weeks later.
        var events = await verify.AgentTaskEvents.AsNoTracking()
            .Where(e => e.AgentTaskId == created.Id && e.Type == AgentTaskEventType.Created)
            .ToListAsync();
        events.ShouldContain(e => e.Detail.Contains("Grok") && e.Detail.Contains("explicit"));
    }

    [Test]
    public async Task an_explicit_ClaudeCode_is_honoured_without_noise_in_the_timeline()
    {
        await using var db = CreateContext();
        using var workspace = new TempWorkspace();

        var created = await CreateService(db).CreateAsync(
            new CreateAgentTaskRequest(Goal: "run the suite") { AgentKind = AgentKind.ClaudeCode },
            ManualCaller(workspace.Path),
            CancellationToken.None);

        created.AgentKind.ShouldBe(AgentKind.ClaudeCode);

        await using var verify = CreateContext();
        var created_event = await verify.AgentTaskEvents.AsNoTracking()
            .SingleAsync(e => e.AgentTaskId == created.Id && e.Type == AgentTaskEventType.Created);
        created_event.Detail.ShouldNotContain(
            "ClaudeCode", customMessage: "the default kind on every line teaches nobody anything");
    }

    // ---- the allowlist -------------------------------------------------------------------------

    [Test]
    [Arguments(AgentKind.Codex)]
    [Arguments(AgentKind.OpenCode)]
    [Arguments(AgentKind.Raw)]
    public async Task a_kind_outside_the_allowlist_is_refused_with_its_reason(AgentKind kind)
    {
        // Rejected, not downgraded to Claude: none of these has been exercised as a DELEGATE (the
        // reporting contract, refinements, check-ins, settlement), and CARD-0083 is what will be
        // able to answer for them.
        await using var db = CreateContext();
        using var workspace = new TempWorkspace();

        // Scoped to a goal only this run can have written: the test database is shared, so an
        // unscoped "no such task exists" would also assert that no other suite is working.
        var goal = $"do the work {Guid.NewGuid():N}";
        var ex = await Should.ThrowAsync<ValidationException>(
            () => CreateService(db).CreateAsync(
                new CreateAgentTaskRequest(Goal: goal) { AgentKind = kind },
                ManualCaller(workspace.Path),
                CancellationToken.None));

        ex.Errors.Keys.ShouldContain(nameof(CreateAgentTaskRequest.AgentKind));
        var message = string.Join(" ", ex.Errors.Values.SelectMany(v => v));
        message.ShouldContain(kind.ToString());
        message.ShouldContain("ClaudeCode");
        message.ShouldContain("Grok");

        await using var verify = CreateContext();
        (await verify.AgentTasks.CountAsync(t => t.Goal == goal))
            .ShouldBe(0, "a refused kind must not leave a task behind");
    }

    [Test]
    public async Task the_allowlist_is_exactly_ClaudeCode_and_Grok()
    {
        // Pinned as a list rather than inferred from the enum: a kind joins it because it has been
        // measured as a delegate, never because someone added an enum member.
        AgentTaskService.DelegatableKinds.ShouldBe([AgentKind.ClaudeCode, AgentKind.Grok]);
    }

    // ---- orchestrators stay on Claude -----------------------------------------------------------

    [Test]
    public async Task an_orchestrator_cannot_be_asked_to_run_on_Grok()
    {
        // An orchestrator's contract — the PreToolUse deny hook, delegate.ps1, the check
        // interpreter — has only ever been exercised on Claude. Grok starts as a worker kind.
        await using var db = CreateContext();
        using var workspace = new TempWorkspace();

        var ex = await Should.ThrowAsync<ValidationException>(
            () => CreateService(db).CreateAsync(
                new CreateAgentTaskRequest(Goal: "own this chunk", Kind: AgentTaskKind.Orchestrator)
                {
                    AgentKind = AgentKind.Grok,
                },
                ManualCaller(workspace.Path),
                CancellationToken.None));

        var message = string.Join(" ", ex.Errors.Values.SelectMany(v => v));
        message.ShouldContain("orchestrator");
        message.ShouldContain(
            "WORKER", customMessage: "a refusal must say what Grok IS good for, not only what it isn't");
    }

    [Test]
    public async Task a_Grok_worker_under_a_Grok_ban_on_orchestrators_is_still_allowed()
    {
        // The ban is on the SHAPE of the task, not on the kind: the point of the slice is that
        // workers can be Grok.
        await using var db = CreateContext();
        var service = CreateService(db);

        service.ResolveAgentKind(AgentTaskKind.Worker, AgentTaskRole.Code, AgentKind.Grok)
            .ShouldBe(AgentKind.Grok);
    }

    // ---- the role-policy seam --------------------------------------------------------------------

    [Test]
    public async Task a_role_promoted_in_config_runs_its_workers_on_Grok_with_no_code_change()
    {
        // The promotion path from plan §4: after real mileage, flipping this one config value is
        // the whole change, and it is reversible the same way.
        await using var db = CreateContext();
        using var workspace = new TempWorkspace();
        var service = CreateService(db, configure: s => s.RolePolicy["Code"].Kind = AgentKind.Grok);

        var created = await service.CreateAsync(
            new CreateAgentTaskRequest(Goal: "write the code", Role: AgentTaskRole.Code),
            ManualCaller(workspace.Path),
            CancellationToken.None);

        created.AgentKind.ShouldBe(AgentKind.Grok);

        await using var verify = CreateContext();
        var created_event = await verify.AgentTaskEvents.AsNoTracking()
            .SingleAsync(e => e.AgentTaskId == created.Id && e.Type == AgentTaskEventType.Created);
        created_event.Detail.ShouldContain("role policy");
    }

    [Test]
    public async Task an_explicit_kind_outranks_the_role_policy()
    {
        await using var db = CreateContext();
        var service = CreateService(db, configure: s => s.RolePolicy["Code"].Kind = AgentKind.Grok);

        service.ResolveAgentKind(AgentTaskKind.Worker, AgentTaskRole.Code, AgentKind.ClaudeCode)
            .ShouldBe(AgentKind.ClaudeCode);
    }

    [Test]
    public async Task a_promoted_role_does_not_make_its_orchestrators_unrunnable()
    {
        // Unlike an explicit ask, a policy-derived kind CLAMPS on an orchestrator — the way the
        // tier floor does. Promoting Code to Grok must not start failing every Code orchestrator.
        await using var db = CreateContext();
        var service = CreateService(db, configure: s => s.RolePolicy["Code"].Kind = AgentKind.Grok);

        service.ResolveAgentKind(AgentTaskKind.Orchestrator, AgentTaskRole.Code, null)
            .ShouldBe(AgentKind.ClaudeCode);
    }

    [Test]
    public async Task a_role_configured_to_an_undelegatable_kind_fails_loudly_and_names_the_role()
    {
        // A typo in config must not run Claude while the operator believes it runs Codex.
        await using var db = CreateContext();
        var service = CreateService(db, configure: s => s.RolePolicy["Docs"].Kind = AgentKind.Codex);

        var ex = Should.Throw<ValidationException>(
            () => service.ResolveAgentKind(AgentTaskKind.Worker, AgentTaskRole.Docs, null));

        var message = string.Join(" ", ex.Errors.Values.SelectMany(v => v));
        message.ShouldContain("Docs");
        message.ShouldContain("Codex");
    }

    // ---- the wire shape the script depends on ------------------------------------------------------

    [Test]
    public async Task the_request_binds_agentKind_from_the_scripts_json()
    {
        // The script posts a STRING, and the API's own options are what turn it into the enum. Bound
        // with the server's configuration rather than a hand-rolled one, because a caller who sends
        // "Grok" and silently binds to ClaudeCode (the enum default) is the failure this prevents.
        var request = JsonSerializer.Deserialize<CreateAgentTaskRequest>(
            """{"goal":"run the suite","kind":"Worker","role":"Test","agentKind":"Grok"}""",
            WebJson);

        request.ShouldNotBeNull();
        request.AgentKind.ShouldBe(AgentKind.Grok);
    }

    [Test]
    public async Task an_omitted_agentKind_binds_to_null_rather_than_a_choice()
    {
        var request = JsonSerializer.Deserialize<CreateAgentTaskRequest>(
            """{"goal":"run the suite","kind":"Worker","role":"Test"}""", WebJson);

        request.ShouldNotBeNull();
        request.AgentKind.ShouldBeNull(
            "null is 'the caller said nothing', which is what lets the role policy answer");
    }

    [Test]
    public async Task the_created_response_carries_the_kind_as_a_string()
    {
        // delegate.ps1 compares $created.agentKind against 'ClaudeCode' — an int on the wire would
        // make that comparison silently true forever.
        var json = JsonSerializer.Serialize(
            new AgentTaskCreatedDto(
                Guid.NewGuid(), "abcd1234", AgentTaskStatus.Queued, AgentModelLevel.High,
                Warning: null, AgentKind: AgentKind.Grok),
            WebJson);

        json.ShouldContain("\"agentKind\":\"Grok\"");
    }

    /// <summary>The two lines Program.cs configures for the API: Web defaults plus string enums.</summary>
    private static readonly JsonSerializerOptions WebJson = BuildWebJson();

    private static JsonSerializerOptions BuildWebJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    // ---- follow-ups: the program is already running ------------------------------------------------

    [Test]
    public async Task a_follow_up_inherits_the_prior_tasks_kind()
    {
        using var workspace = new TempWorkspace();
        var agentId = await SeedPoolAgentAsync(workspace.Path);
        var prior = await SeedTaskAsync(workspace.Path, AgentKind.Grok);
        await PinTaskAgentAsync(prior.Id, agentId);

        await using var db = CreateContext();
        var created = await CreateService(db).CreateAsync(
            new CreateAgentTaskRequest(Goal: "now add the edge cases")
            {
                FollowUpOnTask = DelegationReportFormatter.Short(prior.Id),
            },
            ManualCaller(workspace.Path),
            CancellationToken.None);

        created.AgentKind.ShouldBe(AgentKind.Grok, "the program that holds the context is the program");
    }

    [Test]
    public async Task a_follow_up_cannot_switch_program_under_a_live_context()
    {
        using var workspace = new TempWorkspace();
        var agentId = await SeedPoolAgentAsync(workspace.Path);
        var prior = await SeedTaskAsync(workspace.Path, AgentKind.ClaudeCode);
        await PinTaskAgentAsync(prior.Id, agentId);

        await using var db = CreateContext();
        var ex = await Should.ThrowAsync<ConflictException>(
            () => CreateService(db).CreateAsync(
                new CreateAgentTaskRequest(Goal: "now on Grok please")
                {
                    FollowUpOnTask = DelegationReportFormatter.Short(prior.Id),
                    AgentKind = AgentKind.Grok,
                },
                ManualCaller(workspace.Path),
                CancellationToken.None));

        ex.Message.ShouldContain("ClaudeCode");
        ex.Message.ShouldContain(
            "Delegate normally", customMessage: "refusals must say what to do instead");
    }

    // ---- helpers ------------------------------------------------------------------------------------

    private static AgentTaskService.Caller ManualCaller(string directory) => new(null, null, directory);

    private static async Task<AgentTask> SeedTaskAsync(string workingDirectory, AgentKind agentKind)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Depth = 0,
            Title = $"Seeded {agentKind}",
            Goal = "Seeded goal.",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Custom,
            AgentKind = agentKind,
            ModelLevel = AgentModelLevel.High,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = workingDirectory,
            Status = AgentTaskStatus.Succeeded,
            CreatedAt = DateTime.UtcNow,
        };

        await using var db = CreateContext();
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static async Task<Guid> SeedPoolAgentAsync(string directory)
    {
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = $"task-{Guid.NewGuid():N}"[..13],
            Slug = $"kind-{Guid.NewGuid():N}"[..13],
            WorkingDirectory = directory,
            Details = "Warm pool delegate.",
            Status = AgentStatus.Idle,
            ModelLevel = AgentModelLevel.High,
            IsPoolDelegate = true,
            PoolIdleSince = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        await using var db = CreateContext();
        db.Agents.Add(agent);
        await db.SaveChangesAsync();
        return agent.Id;
    }

    private static async Task PinTaskAgentAsync(Guid taskId, Guid agentId)
    {
        await using var db = CreateContext();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
        task.AgentId = agentId;
        await db.SaveChangesAsync();
    }

    private static AgentTaskService CreateService(
        AppDbContext db, Action<DelegationSettings>? configure = null)
    {
        var settings = new DelegationSettings
        {
            MaxDepth = 5,
            MaxTasksPerRoot = 40,
            MaxCostUsdPerRoot = 5.00m,
            AllowedRoots = [],
        };
        configure?.Invoke(settings);
        return new AgentTaskService(
            db,
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            Options.Create(settings),
            new MockEventBus(),
            new RecordingSessionStopper(),
            TimeProvider.System,
            NullLogger<AgentTaskService>.Instance);
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    /// <summary>A real directory on disk — the resolver verifies existence, so a fake path won't do.</summary>
    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-kind-test").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { /* a delegate's stray file lock must not fail the test */ }
        }
    }
}
