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
/// CARD-0291 — <see cref="CreateAgentTaskRequest.Agent"/> resolves a standing agent by guid, slug,
/// or case-insensitive name and feeds the CARD-0140 pin path unchanged. Ambiguity, unknowns, pool
/// delegates, a disagreeing <c>AgentId</c>, and combining with <c>FollowUpOnTask</c> are all 422
/// refusals: an explicit reference that silently binds nothing (or the wrong thing) is how work
/// ends up reporting to nobody.
/// </summary>
[Category("Integration")]
[NotInParallel("AgentQueue")]
public class AgentNamePinTests
{
    [Test]
    public async Task T1_a_guid_reference_pins_the_task_and_inherits_the_agents_kind()
    {
        using var workspace = new TempWorkspace();
        var agentId = await SeedStandingAgentAsync(
            workspace.Path, AgentKind.Codex, name: Unique("Codex standing"), slug: UniqueSlug());

        await using var db = CreateContext();
        var created = await CreateService(db).CreateAsync(
            new CreateAgentTaskRequest(Goal: "pin by guid text") { Agent = agentId.ToString() },
            ManualCaller(workspace.Path),
            CancellationToken.None);

        created.AgentKind.ShouldBe(AgentKind.Codex);

        await using var verify = CreateContext();
        var row = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id);
        row.AgentId.ShouldBe(agentId);
        row.Ephemeral.ShouldBeFalse("a pinned task must never delete the standing agent on retry");
        row.AgentKind.ShouldBe(AgentKind.Codex);
    }

    [Test]
    public async Task T2_an_exact_slug_reference_pins_the_task()
    {
        using var workspace = new TempWorkspace();
        var slug = UniqueSlug();
        var agentId = await SeedStandingAgentAsync(
            workspace.Path, AgentKind.ClaudeCode, name: Unique("Slugged standing"), slug: slug);

        await using var db = CreateContext();
        var created = await CreateService(db).CreateAsync(
            new CreateAgentTaskRequest(Goal: "pin by slug") { Agent = slug },
            ManualCaller(workspace.Path),
            CancellationToken.None);

        await using var verify = CreateContext();
        var row = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id);
        row.AgentId.ShouldBe(agentId);
        row.Ephemeral.ShouldBeFalse();
    }

    [Test]
    public async Task T3_a_name_reference_is_case_insensitive()
    {
        using var workspace = new TempWorkspace();
        var name = Unique("Gym Stat Child");
        var agentId = await SeedStandingAgentAsync(
            workspace.Path, AgentKind.Grok, name: name, slug: UniqueSlug());

        await using var db = CreateContext();
        var created = await CreateService(db).CreateAsync(
            new CreateAgentTaskRequest(Goal: "pin by shouted name") { Agent = name.ToUpperInvariant() },
            ManualCaller(workspace.Path),
            CancellationToken.None);

        created.AgentKind.ShouldBe(AgentKind.Grok, "the pin inherits the agent's kind");
        (await CreateContext().AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id))
            .AgentId.ShouldBe(agentId);
    }

    [Test]
    public async Task T4_an_ambiguous_name_is_refused_naming_the_candidates()
    {
        using var workspace = new TempWorkspace();
        var name = Unique("Twin standing");
        var first = await SeedStandingAgentAsync(workspace.Path, AgentKind.ClaudeCode, name, UniqueSlug());
        var second = await SeedStandingAgentAsync(workspace.Path, AgentKind.Codex, name, UniqueSlug());

        await using var db = CreateContext();
        var ex = await Should.ThrowAsync<ValidationException>(
            () => CreateService(db).CreateAsync(
                new CreateAgentTaskRequest(Goal: "which twin?") { Agent = name },
                ManualCaller(workspace.Path),
                CancellationToken.None));

        var message = string.Join(" ", ex.Errors.Values.SelectMany(v => v));
        message.ShouldContain("ambiguous");
        message.ShouldContain(first.ToString());
        message.ShouldContain(second.ToString());
    }

    [Test]
    public async Task T5_an_unknown_reference_is_refused_not_silently_unpinned()
    {
        using var workspace = new TempWorkspace();
        var reference = Unique("no such agent");
        var goal = $"unknown pin {Guid.NewGuid():N}";

        await using var db = CreateContext();
        var ex = await Should.ThrowAsync<ValidationException>(
            () => CreateService(db).CreateAsync(
                new CreateAgentTaskRequest(Goal: goal) { Agent = reference },
                ManualCaller(workspace.Path),
                CancellationToken.None));

        string.Join(" ", ex.Errors.Values.SelectMany(v => v)).ShouldContain(reference);
        (await CreateContext().AgentTasks.CountAsync(t => t.Goal == goal))
            .ShouldBe(0, "a refused pin must not leave an unpinned task behind");
    }

    [Test]
    public async Task T6_a_pool_delegate_is_refused_pointing_at_OnAgent()
    {
        using var workspace = new TempWorkspace();
        var poolId = await SeedPoolAgentAsync(workspace.Path, AgentKind.ClaudeCode);

        await using var db = CreateContext();
        var ex = await Should.ThrowAsync<ValidationException>(
            () => CreateService(db).CreateAsync(
                new CreateAgentTaskRequest(Goal: "pin the ephemeral population") { Agent = poolId.ToString() },
                ManualCaller(workspace.Path),
                CancellationToken.None));

        var message = string.Join(" ", ex.Errors.Values.SelectMany(v => v));
        message.ShouldContain("pool delegate");
        message.ShouldContain("-OnAgent");
    }

    [Test]
    public async Task T7_a_disagreeing_AgentId_is_refused_and_an_agreeing_one_is_accepted()
    {
        using var workspace = new TempWorkspace();
        var name = Unique("Agreeable standing");
        var agentId = await SeedStandingAgentAsync(workspace.Path, AgentKind.ClaudeCode, name, UniqueSlug());
        var otherId = await SeedStandingAgentAsync(
            workspace.Path, AgentKind.ClaudeCode, Unique("Other standing"), UniqueSlug());

        await using var db = CreateContext();
        var service = CreateService(db);

        var ex = await Should.ThrowAsync<ValidationException>(
            () => service.CreateAsync(
                new CreateAgentTaskRequest(Goal: "two different pins")
                {
                    Agent = name,
                    AgentId = otherId,
                },
                ManualCaller(workspace.Path),
                CancellationToken.None));
        string.Join(" ", ex.Errors.Values.SelectMany(v => v)).ShouldContain(otherId.ToString());

        var created = await service.CreateAsync(
            new CreateAgentTaskRequest(Goal: "the same pin twice")
            {
                Agent = name,
                AgentId = agentId,
            },
            ManualCaller(workspace.Path),
            CancellationToken.None);
        (await CreateContext().AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id))
            .AgentId.ShouldBe(agentId);
    }

    [Test]
    public async Task T8_Agent_combined_with_FollowUpOnTask_is_refused()
    {
        using var workspace = new TempWorkspace();
        var name = Unique("Standing not follow-up");
        await SeedStandingAgentAsync(workspace.Path, AgentKind.ClaudeCode, name, UniqueSlug());

        await using var db = CreateContext();
        var ex = await Should.ThrowAsync<ValidationException>(
            () => CreateService(db).CreateAsync(
                new CreateAgentTaskRequest(Goal: "two idioms at once")
                {
                    Agent = name,
                    FollowUpOnTask = "deadbeef",
                },
                ManualCaller(workspace.Path),
                CancellationToken.None));

        string.Join(" ", ex.Errors.Values.SelectMany(v => v)).ShouldContain("follow-up");
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static string Unique(string prefix) => $"{prefix} {Guid.NewGuid():N}"[..(prefix.Length + 9)];

    private static string UniqueSlug() => $"pin-{Guid.NewGuid():N}"[..16];

    private static AgentTaskService.Caller ManualCaller(string directory) => new(null, null, directory);

    private static async Task<Guid> SeedStandingAgentAsync(
        string directory, AgentKind kind, string name, string slug)
    {
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = name,
            Slug = slug,
            WorkingDirectory = directory,
            Details = "A standing agent for CARD-0291.",
            Status = AgentStatus.Idle,
            ModelLevel = AgentModelLevel.High,
            Kind = kind,
            AlwaysOn = false,
            IsPoolDelegate = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        await using var db = CreateContext();
        db.Agents.Add(agent);
        await db.SaveChangesAsync();
        return agent.Id;
    }

    private static async Task<Guid> SeedPoolAgentAsync(string directory, AgentKind kind)
    {
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = $"task-{Guid.NewGuid():N}"[..13],
            Slug = $"pool-{Guid.NewGuid():N}"[..13],
            WorkingDirectory = directory,
            Details = "Warm pool delegate.",
            Status = AgentStatus.Idle,
            ModelLevel = AgentModelLevel.High,
            Kind = kind,
            IsPoolDelegate = true,
            PoolIdleSince = DateTime.UtcNow.AddMinutes(-5),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        await using var db = CreateContext();
        db.Agents.Add(agent);
        await db.SaveChangesAsync();
        return agent.Id;
    }

    private static AgentTaskService CreateService(AppDbContext db)
    {
        var settings = new DelegationSettings
        {
            MaxDepth = 5,
            MaxTasksPerRoot = 40,
            MaxCostUsdPerRoot = 5.00m,
            AllowedRoots = [],
        };
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

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-name-pin").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
