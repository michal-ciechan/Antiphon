using Antiphon.E2E.Fixtures;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.E2E;

/// <summary>
/// Classification canary: given only prose, does a real Claude find the antiphon-delegate skill and
/// pick a sensible SHAPE (worker vs sub-orchestrator) and ROLE for the work?
///
/// That decision is the entire "auto-decide how complex this is" mechanism, and it is the one thing
/// no amount of server-side testing can establish — the tier ladder is only worth having if the
/// model puts work on the right rung.
///
/// Deliberately narrow: everything either side of this decision — dispatch, delegate execution,
/// marker correlation, report delivery, sequencing — is covered end to end and reliably by
/// <see cref="DelegationSequencingE2ETests"/>, so this test stops as soon as the task row exists.
/// (It used to re-test all of that through its own copy of the pty harness, which is how it
/// accumulated every input-handling bug that harness has since fixed centrally.)
///
/// Assertions are accepted-SETS, never single values, and every pick is logged — a model is not a
/// deterministic fixture, and a canary that hard-fails on a defensible alternative choice is one
/// people learn to ignore.
///
/// Opt-in headed: ANTIPHON_HEADED_TESTS=1 + claude on PATH; self-skips otherwise.
/// </summary>
[Category("Headed")]
[Category("HeadedCanary")]
[NotInParallel("Headed")]
public class DelegationPipelineE2ETests
{
    [Test]
    public async Task A_small_well_defined_job_is_classified_as_a_worker()
    {
        await AssertClassificationAsync(
            instruction:
                "Use your antiphon-delegate skill to hand this off to another agent: update README.md "
                + "so the install section says pwsh 7 instead of cmd. Delegate it, then stop and end "
                + "your turn. Do not edit any files yourself.",
            expectedKind: AgentTaskKind.Worker,
            acceptableRoles: [AgentTaskRole.Docs, AgentTaskRole.Code, AgentTaskRole.Custom]);
    }

    [Test]
    public async Task A_multi_step_job_is_classified_as_a_sub_orchestrator()
    {
        // The other half of the decision. Getting the role right but the shape wrong still wastes a
        // tier — a worker handed a whole migration either does it badly or gives up.
        await AssertClassificationAsync(
            instruction:
                "Use your antiphon-delegate skill to hand off this whole piece of work: migrate this "
                + "project from Postgres 17 to 18 — schema changes, the compose file, connection "
                + "strings, docs, and a full test pass. Delegate it as ONE handoff, then stop and end "
                + "your turn.",
            expectedKind: AgentTaskKind.Orchestrator,
            acceptableRoles: [AgentTaskRole.Plan, AgentTaskRole.Code, AgentTaskRole.Custom]);
    }

    private static async Task AssertClassificationAsync(
        string instruction,
        AgentTaskKind expectedKind,
        AgentTaskRole[] acceptableRoles)
    {
        ClaudeHarness.SkipIfNotEligible();

        var fixture = new AntiphonAppFixture();
        await fixture.InitializeAsync();
        using var repo = new DelegationScratchRepo();
        try
        {
            var settings = fixture.Services
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<DelegationSettings>>().Value;
            settings.AllowedRoots.Add(repo.Path);
            settings.ApiBaseUrl = fixture.BaseAddress;

            var sessionId = await SeedSessionAsync(fixture, repo.Path);
            var rootTaskId = await SeedOrchestratorTaskAsync(fixture, repo.Path, sessionId);

            await using var orchestrator = await ClaudeHarness.StartAsync(
                repo.Path,
                DelegationScratchRepo.EnvFor(
                    fixture.BaseAddress, sessionId, rootTaskId, AgentTaskService.RawTokens[rootTaskId]),
                model: "sonnet");

            await orchestrator.SubmitAsync(instruction);

            var task = await WaitForChildAsync(fixture, rootTaskId, TimeSpan.FromMinutes(5));
            if (task is null)
            {
                // Distinguish the two ways this can produce nothing. If PowerShell printed its usage
                // banner, the model garbled its own quoting and the command never reached
                // delegate.ps1 — nothing about our contract was exercised, so failing would be
                // noise. A canary people learn to ignore is worse than no canary.
                if (ShellRejectedTheCommand(orchestrator.Screen()))
                {
                    throw new TUnit.Core.Exceptions.SkipTestException(
                        "the model garbled its shell invocation, so delegate.ps1 was never reached — "
                        + "model-side flake, not a delegation failure");
                }

                task.ShouldNotBeNull(
                    "a real Claude must find the skill and reach the API from prose alone. "
                    + Describe(orchestrator));
            }

            Console.WriteLine($"Model chose: kind={task!.Kind} role={task.Role} tier={task.ModelLevel}");

            task.Kind.ShouldBe(expectedKind);
            acceptableRoles.ShouldContain(task.Role, $"'{task.Role}' is not a defensible role here");

            // The TIER is ours to guarantee, whatever role the model picked.
            await using var scope = fixture.Services.CreateAsyncScope();
            var expectedLevel = scope.ServiceProvider.GetRequiredService<AgentTaskService>()
                .ResolveLevel(task.Kind, task.Role, explicitLevel: null);
            task.ModelLevel.ShouldBe(expectedLevel, "the dispatched tier must match the role policy");
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    /// <summary>
    /// True when the shell rejected the command outright and printed its own usage banner — the
    /// model's quoting was wrong and delegate.ps1 was never invoked.
    /// </summary>
    private static bool ShellRejectedTheCommand(string screen) =>
        screen.Contains("[-ExecutionPolicy", StringComparison.OrdinalIgnoreCase)
        || screen.Contains("[-InputFormat", StringComparison.OrdinalIgnoreCase)
        || screen.Contains("PowerShell[.exe]", StringComparison.OrdinalIgnoreCase);

    /// <summary>Say WHY nothing happened — "task was null" after five minutes is not a bug report.</summary>
    private static string Describe(ClaudeHarness session)
    {
        var blocked = session.BlockedOn();
        var reason = blocked is null
            ? "The TUI is not blocked on a dialog — it either never ran the command, or the command failed."
            : $"The TUI is BLOCKED on a [{blocked.Kind}] dialog: {blocked.Title}";
        return $"{reason}\nScreen:\n{session.Screen()}";
    }

    private static async Task<AgentTask?> WaitForChildAsync(
        AntiphonAppFixture fixture, Guid parentTaskId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var task = await db.AgentTasks.AsNoTracking()
                .FirstOrDefaultAsync(t => t.ParentTaskId == parentTaskId);
            if (task is not null)
                return task;
            await Task.Delay(2_000);
        }
        return null;
    }

    private static async Task<Guid> SeedSessionAsync(AntiphonAppFixture fixture, string cwd)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var session = new AgentSession
        {
            Id = Guid.NewGuid(),
            DefinitionName = "fake",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = cwd,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        };
        db.AgentSessions.Add(session);
        await db.SaveChangesAsync();
        return session.Id;
    }

    private static async Task<Guid> SeedOrchestratorTaskAsync(
        AntiphonAppFixture fixture, string cwd, Guid sessionId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var created = await scope.ServiceProvider.GetRequiredService<AgentTaskService>().CreateAsync(
            new CreateAgentTaskRequest(
                Goal: "Coordinate this run.", Kind: AgentTaskKind.Orchestrator, Role: AgentTaskRole.Plan),
            new AgentTaskService.Caller(null, null, cwd),
            CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.AgentTasks.SingleAsync(t => t.Id == created.Id);
        row.AgentSessionId = sessionId;
        row.Status = AgentTaskStatus.Working;
        await db.SaveChangesAsync();
        return created.Id;
    }
}
