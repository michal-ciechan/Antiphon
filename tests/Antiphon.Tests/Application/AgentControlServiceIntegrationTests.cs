using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.Tui;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.WorkspaceHooks;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("AgentControl")]
public class AgentControlServiceIntegrationTests
{
    [Test]
    public async Task Legacy_only_provider_starts_unprofiled_agent_through_configured_registry()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var workspace = Path.Combine(tempRoot, "legacy-agent-workspace");
            Directory.CreateDirectory(workspace);
            var adapter = new FakeAgentProtocolAdapter();
            await using var harness = BuildHarness(tempRoot, [adapter]);

            var now = DateTime.UtcNow;
            var agent = new Agent
            {
                Id = Guid.NewGuid(),
                Name = "Legacy Registry Agent",
                Slug = $"legacy-registry-agent-{Guid.NewGuid():N}",
                WorkingDirectory = workspace,
                Details = string.Empty,
                Status = AgentStatus.Idle,
                CreatedAt = now,
                UpdatedAt = now,
                TuiProfileId = null,
                ModelId = null
            };
            db.Agents.Add(agent);
            await db.SaveChangesAsync();

            var detail = await harness.Control.StartAsync(
                agent.Id,
                new StartAgentRequest(Fresh: true),
                CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            detail.PersistentSessionId.ShouldNotBeNull();
            adapter.Started.ShouldBeTrue();

            await using var verify = CreateContext();
            var session = await verify.AgentSessions.SingleAsync(s => s.Id.ToString() == detail.PersistentSessionId);
            session.DefinitionName.ShouldBe("fake");
            session.AgentKind.ShouldBe(AgentKind.Raw);
            session.TuiProfileRevisionId.ShouldBeNull();
            session.EffectiveModelId.ShouldBeNull();
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Registered_profile_resolver_without_default_preserves_legacy_claude_launch_arguments()
    {
        await using var isolatedSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(isolatedSchema.ConnectionString);
        var tempRoot = NewTempRoot();
        try
        {
            (await db.AgentTuiProfiles.AnyAsync()).ShouldBeFalse();
            var workspace = Path.Combine(tempRoot, "legacy-claude-workspace");
            Directory.CreateDirectory(workspace);
            var adapter = new FakeAgentProtocolAdapter();
            await using var harness = BuildHarness(
                tempRoot,
                [adapter],
                defaultKind: "ClaudeCode",
                includeLaunchResolver: true,
                connectionString: isolatedSchema.ConnectionString);

            var now = DateTime.UtcNow;
            var agent = new Agent
            {
                Id = Guid.NewGuid(),
                Name = "Legacy Claude",
                Slug = $"legacy-claude-{Guid.NewGuid():N}",
                WorkingDirectory = workspace,
                Details = string.Empty,
                Status = AgentStatus.Idle,
                ModelLevel = AgentModelLevel.Low,
                SystemPromptAppend = "Keep responses concise.",
                CreatedAt = now,
                UpdatedAt = now,
                TuiProfileId = null,
                ModelId = null
            };
            db.Agents.Add(agent);
            await db.SaveChangesAsync();

            await harness.Control.StartAsync(
                agent.Id,
                new StartAgentRequest(Fresh: true),
                CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            adapter.Started.ShouldBeTrue();
            adapter.StartedArgs.ShouldContain("--name");
            adapter.StartedArgs.ShouldContain("Legacy Claude");
            adapter.StartedArgs.ShouldContain("--model");
            adapter.StartedArgs.ShouldContain("haiku");
            var preambleIndex = Array.IndexOf(adapter.StartedArgs.ToArray(), "--append-system-prompt");
            preambleIndex.ShouldNotBe(-1);
            var preambleArgument = adapter.StartedArgs[preambleIndex + 1];
            preambleArgument.ShouldContain("Keep responses concise.");
        }
        finally
        {
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task T6_blank_field_grok_profile_starts_without_a_model_argument()
    {
        await using var isolatedSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(isolatedSchema.ConnectionString);
        var tempRoot = NewTempRoot();
        try
        {
            var workspace = Path.Combine(tempRoot, "gkp-workspace");
            Directory.CreateDirectory(workspace);
            var adapter = new FakeAgentProtocolAdapter();
            await using var harness = BuildHarness(
                tempRoot,
                [adapter],
                defaultKind: "Grok",
                includeLaunchResolver: true,
                connectionString: isolatedSchema.ConnectionString);

            var profile = await SeedBlankModelArgumentProfileAsync(db, AgentKind.Grok);
            var now = DateTime.UtcNow;
            var agent = new Agent
            {
                Id = Guid.NewGuid(),
                Name = "GKP Grok",
                Slug = $"gkp-grok-{Guid.NewGuid():N}",
                WorkingDirectory = workspace,
                Details = string.Empty,
                Status = AgentStatus.Idle,
                Kind = AgentKind.Grok,
                ModelLevel = AgentModelLevel.High,
                ModelId = null,
                TuiProfileId = profile.Id,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.Agents.Add(agent);
            await db.SaveChangesAsync();

            await harness.Control.StartAsync(
                agent.Id,
                new StartAgentRequest(Fresh: true),
                CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            adapter.Started.ShouldBeTrue();
            adapter.StartedArgs.ShouldNotContain("--model");
            adapter.StartedArgs.ShouldNotContain("grok-4.6");
        }
        finally
        {
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Start_with_remote_control_boots_queue_head_and_sends_rename_then_remote_control_before_work()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            var template = NewWorkflowTemplate(tempRoot);
            db.Projects.Add(project);
            db.WorkflowTemplates.Add(template);
            await db.SaveChangesAsync();
            // The fake echoes this after every prompt, so the /remote-control command "arms" the
            // bridge the way the real TUI reports it — the boot sequence waits for this marker
            // before renaming.
            var adapter = new FakeAgentProtocolAdapter { PromptOutput = "/remote-control is active · BOOTED" };
            await using var harness = BuildHarness(tempRoot, [adapter], defaultKind: "ClaudeCode");

            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Remote Control Board"), CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Wire the thing"), CancellationToken.None);
            var agent = await harness.AgentService.CreateAsync(
                new CreateAgentRequest("Remote Claude", Path.Combine(tempRoot, "agent-workspace"), DefaultWorkflowTemplateId: template.Id),
                CancellationToken.None);
            await harness.AgentService.AssignCardAsync(
                agent.Id, new AssignAgentCardRequest(card.Id), CancellationToken.None);

            var detail = await harness.Control.StartAsync(
                agent.Id, new StartAgentRequest(RemoteControl: true), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            detail.Status.ShouldBe(AgentStatus.Running);
            detail.CurrentCardId.ShouldBe(card.Id);
            detail.PersistentSessionId.ShouldNotBeNull();

            // The remote-control + rename commands must arrive before the work prompt — and in
            // that order: claude.ai only syncs titles from /rename events fired while armed.
            adapter.Prompts.Count.ShouldBe(3);
            adapter.Prompts[0].ShouldBe("/remote-control");
            adapter.Prompts[1].ShouldBe("/rename Remote Claude");
            adapter.Prompts[2].ShouldNotBeNullOrWhiteSpace();
            adapter.Prompts[2].ShouldNotStartWith("/remote-control");

            await using var verify = CreateContext();
            var session = await verify.AgentSessions.SingleAsync(s => s.Id.ToString() == detail.PersistentSessionId);
            session.Status.ShouldBe(SessionStatus.Running);
            session.CardId.ShouldBe(card.Id);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Start_with_Prompt_on_a_queued_card_is_422()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            var template = NewWorkflowTemplate(tempRoot);
            db.Projects.Add(project);
            db.WorkflowTemplates.Add(template);
            await db.SaveChangesAsync();
            var adapter = new FakeAgentProtocolAdapter();
            await using var harness = BuildHarness(tempRoot, [adapter], defaultKind: "ClaudeCode");

            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Prompt Card Board"), CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "The card is the work"), CancellationToken.None);
            var agent = await harness.AgentService.CreateAsync(
                new CreateAgentRequest(
                    "Prompt Card Claude",
                    Path.Combine(tempRoot, "agent-workspace"),
                    DefaultWorkflowTemplateId: template.Id),
                CancellationToken.None);
            await harness.AgentService.AssignCardAsync(
                agent.Id, new AssignAgentCardRequest(card.Id), CancellationToken.None);

            var ex = await Should.ThrowAsync<ValidationException>(
                () => harness.Control.StartAsync(
                    agent.Id,
                    new StartAgentRequest(Prompt: "this must not override the card"),
                    CancellationToken.None));
            ex.StatusCode.ShouldBe(422);
            ex.Errors[nameof(StartAgentRequest.Prompt)].ShouldNotBeEmpty();
            adapter.Started.ShouldBeFalse();
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Start_with_remote_control_on_a_grok_agent_is_refused_and_launches_nothing()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var workspace = Path.Combine(tempRoot, "grok-rc-workspace");
            Directory.CreateDirectory(workspace);
            var adapter = new FakeAgentProtocolAdapter();
            await using var harness = BuildHarness(tempRoot, [adapter], includeLaunchResolver: true);
            var grok = await SeedBlankModelArgumentProfileAsync(db, AgentKind.Grok);
            var agent = await harness.AgentService.CreateAsync(
                new CreateAgentRequest("Grok RC Refuse", workspace, TuiProfileId: grok.Id),
                CancellationToken.None);

            var ex = await Should.ThrowAsync<ConflictException>(() =>
                harness.Control.StartAsync(
                    agent.Id,
                    new StartAgentRequest(RemoteControl: true),
                    CancellationToken.None));
            ex.Code.ShouldBe("remote_control_refused");

            adapter.Started.ShouldBeFalse();
            adapter.Prompts.ShouldBeEmpty();

            await using var verify = CreateContext();
            (await verify.AgentSessions.CountAsync(s => s.Cwd == workspace)).ShouldBe(0);
            (await verify.Agents.SingleAsync(a => a.Id == agent.Id)).Status.ShouldBe(AgentStatus.Idle);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Start_inheriting_a_stale_grok_remote_control_flag_launches_without_typing_it()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            var template = NewWorkflowTemplate(tempRoot);
            db.Projects.Add(project);
            db.WorkflowTemplates.Add(template);
            await db.SaveChangesAsync();
            var adapter = new FakeAgentProtocolAdapter { PromptOutput = "BOOTED" };
            await using var harness = BuildHarness(tempRoot, [adapter], includeLaunchResolver: true);
            var grok = await SeedBlankModelArgumentProfileAsync(db, AgentKind.Grok);

            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Grok Inherit Board"), CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Grok inherit work"), CancellationToken.None);
            var agent = await harness.AgentService.CreateAsync(
                new CreateAgentRequest(
                    "Grok Inherit RC",
                    Path.Combine(tempRoot, "grok-inherit-workspace"),
                    DefaultWorkflowTemplateId: template.Id,
                    TuiProfileId: grok.Id),
                CancellationToken.None);
            await db.Agents.Where(a => a.Id == agent.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.RemoteControlEnabled, true));
            db.ChangeTracker.Clear();
            harness.Scope.ServiceProvider.GetRequiredService<AppDbContext>().ChangeTracker.Clear();
            await harness.AgentService.AssignCardAsync(
                agent.Id, new AssignAgentCardRequest(card.Id), CancellationToken.None);

            var detail = await harness.Control.StartAsync(
                agent.Id, new StartAgentRequest(), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            detail.Status.ShouldBe(AgentStatus.Running);
            adapter.Prompts.ShouldNotBeEmpty();
            adapter.Prompts.ShouldNotContain("/remote-control");
            adapter.Prompts.ShouldNotContain(p => p.StartsWith("/rename", StringComparison.Ordinal));
            adapter.Prompts[0].ShouldNotBeNullOrWhiteSpace();
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Start_without_remote_control_sends_only_the_work_prompt()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            var template = NewWorkflowTemplate(tempRoot);
            db.Projects.Add(project);
            db.WorkflowTemplates.Add(template);
            await db.SaveChangesAsync();
            var adapter = new FakeAgentProtocolAdapter { PromptOutput = "BOOTED" };
            await using var harness = BuildHarness(tempRoot, [adapter]);

            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Plain Board"), CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Plain work"), CancellationToken.None);
            var agent = await harness.AgentService.CreateAsync(
                new CreateAgentRequest("Plain Claude", Path.Combine(tempRoot, "agent-workspace"), DefaultWorkflowTemplateId: template.Id),
                CancellationToken.None);
            await harness.AgentService.AssignCardAsync(
                agent.Id, new AssignAgentCardRequest(card.Id), CancellationToken.None);

            await harness.Control.StartAsync(agent.Id, new StartAgentRequest(), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            adapter.Prompts.Count.ShouldBe(1);
            adapter.Prompts[0].ShouldNotStartWith("/rename");
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Start_is_idempotent_when_a_live_session_already_exists()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            var template = NewWorkflowTemplate(tempRoot);
            db.Projects.Add(project);
            db.WorkflowTemplates.Add(template);
            await db.SaveChangesAsync();
            // Only one adapter is queued: a second spawn would throw "no fake adapter queued",
            // so a no-op second start is proven by the absence of that throw.
            var adapter = new FakeAgentProtocolAdapter { PromptOutput = "BOOTED" };
            await using var harness = BuildHarness(tempRoot, [adapter]);

            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Idempotent Board"), CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Only-once work"), CancellationToken.None);
            var agent = await harness.AgentService.CreateAsync(
                new CreateAgentRequest("Once Claude", Path.Combine(tempRoot, "agent-workspace"), DefaultWorkflowTemplateId: template.Id),
                CancellationToken.None);
            await harness.AgentService.AssignCardAsync(
                agent.Id, new AssignAgentCardRequest(card.Id), CancellationToken.None);

            var first = await harness.Control.StartAsync(agent.Id, new StartAgentRequest(), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            var second = await harness.Control.StartAsync(agent.Id, new StartAgentRequest(), CancellationToken.None);

            second.PersistentSessionId.ShouldBe(first.PersistentSessionId);
            await using var verify = CreateContext();
            var sessionCount = await verify.AgentSessions.CountAsync(s => s.CardId == card.Id);
            sessionCount.ShouldBe(1);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Start_interactive_resumes_previous_claude_session_by_default()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var workspace = Path.Combine(tempRoot, "agent-workspace");
            Directory.CreateDirectory(workspace);
            var firstAdapter = new FakeAgentProtocolAdapter();
            var resumeAdapter = new FakeAgentProtocolAdapter();
            await using var harness = BuildHarness(tempRoot, [firstAdapter, resumeAdapter], defaultKind: "ClaudeCode");

            var agent = await harness.AgentService.CreateAsync(
                new CreateAgentRequest("Resume Claude", workspace), CancellationToken.None);

            var first = await harness.Control.StartAsync(agent.Id, new StartAgentRequest(), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            first.PersistentSessionId.ShouldNotBeNull();
            firstAdapter.StartedArgs.ShouldContain("--session-id");
            firstAdapter.StartedArgs.ShouldContain(first.PersistentSessionId);

            await MarkSessionEndedAsync(first.PersistentSessionId!, SessionStatus.Stopped);

            // A fresh scope mirrors a new HTTP request — no stale tracked entities.
            using var scope = harness.Provider.CreateScope();
            var control = scope.ServiceProvider.GetRequiredService<AgentControlService>();
            var second = await control.StartAsync(agent.Id, new StartAgentRequest(), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            // Same session id, relaunched with --resume: the terminal picks up where it left off.
            second.PersistentSessionId.ShouldBe(first.PersistentSessionId);
            resumeAdapter.StartedArgs.ShouldContain("--resume");
            resumeAdapter.StartedArgs.ShouldContain(first.PersistentSessionId);
            resumeAdapter.StartedArgs.ShouldNotContain("--session-id");

            await using var verify = CreateContext();
            var sessions = await verify.AgentSessions.Where(s => s.Cwd == workspace).ToListAsync();
            sessions.Count.ShouldBe(1);
            sessions[0].Status.ShouldBe(SessionStatus.Running);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Fresh_herdr_arm_sets_ReusePaneOfSessionId_to_the_previous_session()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var workspace = Path.Combine(tempRoot, "agent-workspace");
            Directory.CreateDirectory(workspace);
            var firstAdapter = new FakeAgentProtocolAdapter();
            var freshAdapter = new FakeAgentProtocolAdapter();
            await using var harness = BuildHarness(tempRoot, [firstAdapter, freshAdapter], defaultKind: "ClaudeCode");

            var agent = await harness.AgentService.CreateAsync(
                new CreateAgentRequest(
                    "Reuse Pane Grok",
                    workspace,
                    SessionBackend: SessionBackend.Herdr),
                CancellationToken.None);

            var first = await harness.Control.StartAsync(
                agent.Id, new StartAgentRequest(Fresh: true, RemoteControl: false), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            first.PersistentSessionId.ShouldNotBeNull();
            var previousId = Guid.Parse(first.PersistentSessionId);

            await MarkSessionEndedAsync(first.PersistentSessionId!, SessionStatus.Failed);

            using var scope = harness.Provider.CreateScope();
            var control = scope.ServiceProvider.GetRequiredService<AgentControlService>();
            var second = await control.StartAsync(
                agent.Id, new StartAgentRequest(Fresh: true, RemoteControl: false), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            second.PersistentSessionId.ShouldNotBe(first.PersistentSessionId);
            freshAdapter.StartedHerdr.ShouldNotBeNull();
            freshAdapter.StartedHerdr!.ReusePaneOfSessionId.ShouldBe(previousId);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Resume_herdr_arm_leaves_ReusePaneOfSessionId_null()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var workspace = Path.Combine(tempRoot, "agent-workspace");
            Directory.CreateDirectory(workspace);
            var firstAdapter = new FakeAgentProtocolAdapter();
            var resumeAdapter = new FakeAgentProtocolAdapter();
            await using var harness = BuildHarness(tempRoot, [firstAdapter, resumeAdapter], defaultKind: "ClaudeCode");

            var agent = await harness.AgentService.CreateAsync(
                new CreateAgentRequest(
                    "Resume Pane Claude",
                    workspace,
                    SessionBackend: SessionBackend.Herdr),
                CancellationToken.None);

            var first = await harness.Control.StartAsync(
                agent.Id, new StartAgentRequest(RemoteControl: false), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            await MarkSessionEndedAsync(first.PersistentSessionId!, SessionStatus.Failed);

            using var scope = harness.Provider.CreateScope();
            var control = scope.ServiceProvider.GetRequiredService<AgentControlService>();
            var second = await control.StartAsync(
                agent.Id, new StartAgentRequest(RemoteControl: false), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            second.PersistentSessionId.ShouldBe(first.PersistentSessionId);
            resumeAdapter.StartedHerdr.ShouldNotBeNull();
            resumeAdapter.StartedHerdr!.ReusePaneOfSessionId.ShouldBeNull();
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Card_spawn_leaves_ReusePaneOfSessionId_null()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            var template = NewWorkflowTemplate(tempRoot);
            db.Projects.Add(project);
            db.WorkflowTemplates.Add(template);
            await db.SaveChangesAsync();
            var adapter = new FakeAgentProtocolAdapter { PromptOutput = "BOOTED" };
            await using var harness = BuildHarness(tempRoot, [adapter], defaultKind: "ClaudeCode");

            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Reuse Board"), CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Card spawn work"), CancellationToken.None);
            var agent = await harness.AgentService.CreateAsync(
                new CreateAgentRequest(
                    "Card Spawn Herdr",
                    Path.Combine(tempRoot, "agent-workspace"),
                    DefaultWorkflowTemplateId: template.Id,
                    SessionBackend: SessionBackend.Herdr),
                CancellationToken.None);
            await harness.AgentService.AssignCardAsync(
                agent.Id, new AssignAgentCardRequest(card.Id), CancellationToken.None);

            await harness.Control.StartAsync(
                agent.Id, new StartAgentRequest(RemoteControl: false), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            adapter.Started.ShouldBeTrue();
            (adapter.StartedHerdr?.ReusePaneOfSessionId).ShouldBeNull();
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    // The runner's CPU spin watchdog kills an IDLE session whose process was busy-looping a core
    // (incident 2026-08-08). The kill's exit code is non-zero, but it must land as a CLEAN stop —
    // session and agent Stopped, not Failed — and the next start must RESUME the same session id,
    // so a message sent to the agent afterwards picks the conversation back up.
    [Test]
    public async Task Cpu_spin_watchdog_kill_lands_as_clean_stop_and_next_start_resumes_the_same_session()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var workspace = Path.Combine(tempRoot, "agent-workspace");
            Directory.CreateDirectory(workspace);
            var firstAdapter = new FakeAgentProtocolAdapter();
            var resumeAdapter = new FakeAgentProtocolAdapter();
            await using var harness = BuildHarness(tempRoot, [firstAdapter, resumeAdapter], defaultKind: "ClaudeCode");

            var agent = await harness.AgentService.CreateAsync(
                new CreateAgentRequest("Spinning Claude", workspace), CancellationToken.None);

            var first = await harness.Control.StartAsync(agent.Id, new StartAgentRequest(), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            var sessionId = Guid.Parse(first.PersistentSessionId!);

            // The watchdog's exit event arrives via the event pump: non-zero code, CpuSpinKilled.
            var runtime = harness.Provider.GetRequiredService<AgentSessionRuntime>();
            await runtime.ObserveExitAsync(sessionId, -1, AgentExitReason.CpuSpinKilled, CancellationToken.None);

            await using (var verify = CreateContext())
            {
                var session = await verify.AgentSessions.SingleAsync(s => s.Id == sessionId);
                session.Status.ShouldBe(SessionStatus.Stopped, "a spin kill is a clean stop, not a failure");
                session.ExitCode.ShouldBe(-1);
                session.EndedAt.ShouldNotBeNull();

                var dbAgent = await verify.Agents.SingleAsync(a => a.Id == agent.Id);
                dbAgent.Status.ShouldBe(AgentStatus.Stopped);
            }

            // Sending the agent back to work resumes the SAME conversation by id.
            using var scope = harness.Provider.CreateScope();
            var control = scope.ServiceProvider.GetRequiredService<AgentControlService>();
            var second = await control.StartAsync(agent.Id, new StartAgentRequest(), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            second.PersistentSessionId.ShouldBe(first.PersistentSessionId);
            resumeAdapter.StartedArgs.ShouldContain("--resume");
            resumeAdapter.StartedArgs.ShouldContain(first.PersistentSessionId);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    // A resumed session whose previous process died MID-TURN (reboot/crash/kill — no TurnEnd, no
    // interrupt marker, and none ever coming) must not come back reading "Working" forever: the
    // relaunch writes a SessionRestartBoundary so the transcript reads idle again, and queues the
    // auto-continue prompt so the interrupted work resumes itself (live miss 2026-08-08).
    [Test]
    public async Task Resume_of_a_mid_turn_session_writes_a_restart_boundary_and_queues_the_auto_continue()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var workspace = Path.Combine(tempRoot, "agent-workspace");
            Directory.CreateDirectory(workspace);
            var firstAdapter = new FakeAgentProtocolAdapter();
            var resumeAdapter = new FakeAgentProtocolAdapter();
            await using var harness = BuildHarness(tempRoot, [firstAdapter, resumeAdapter], defaultKind: "ClaudeCode");

            var agent = await harness.AgentService.CreateAsync(
                new CreateAgentRequest("Interrupted Claude", workspace), CancellationToken.None);

            var first = await harness.Control.StartAsync(agent.Id, new StartAgentRequest(), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            var sessionId = Guid.Parse(first.PersistentSessionId!);

            // The turn that never ended: activity with nothing ending it.
            await SeedTranscriptEntryAsync(sessionId, 1, TranscriptKinds.AssistantText, "half-finished work");

            await MarkSessionEndedAsync(first.PersistentSessionId!, SessionStatus.Stopped);

            using var scope = harness.Provider.CreateScope();
            var control = scope.ServiceProvider.GetRequiredService<AgentControlService>();
            var second = await control.StartAsync(agent.Id, new StartAgentRequest(), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            second.PersistentSessionId.ShouldBe(first.PersistentSessionId);

            await using var verify = CreateContext();
            var boundary = await verify.TranscriptEntries
                .SingleAsync(t => t.AgentSessionId == sessionId
                    && t.Kind == TranscriptKinds.SessionRestartBoundary);
            boundary.Sequence.ShouldBe(2, "the boundary ends the interrupted turn, right after its last record");
            boundary.Timestamp.ShouldNotBeNull("the working rule's timestamp override must rank it newest");

            // The auto-continue rode the queue (WhenIdle; possibly already delivered) — what
            // matters is that it exists and targets this session.
            var queued = await verify.SessionQueuedMessages
                .Where(m => m.AgentSessionId == sessionId)
                .ToListAsync();
            queued.ShouldContain(
                m => m.Body.Contains("interrupted by a restart"),
                "the interrupted work must be told to continue");
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    // The negative twin: a resumed session whose transcript ended cleanly gets NO boundary and NO
    // auto-continue — resuming an idle agent must not invent work for it.
    [Test]
    public async Task Resume_of_a_cleanly_ended_session_writes_no_boundary_and_queues_nothing()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var workspace = Path.Combine(tempRoot, "agent-workspace");
            Directory.CreateDirectory(workspace);
            var firstAdapter = new FakeAgentProtocolAdapter();
            var resumeAdapter = new FakeAgentProtocolAdapter();
            await using var harness = BuildHarness(tempRoot, [firstAdapter, resumeAdapter], defaultKind: "ClaudeCode");

            var agent = await harness.AgentService.CreateAsync(
                new CreateAgentRequest("Settled Claude", workspace), CancellationToken.None);

            var first = await harness.Control.StartAsync(agent.Id, new StartAgentRequest(), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            var sessionId = Guid.Parse(first.PersistentSessionId!);

            await SeedTranscriptEntryAsync(sessionId, 1, TranscriptKinds.AssistantText, "finished work");
            await SeedTranscriptEntryAsync(sessionId, 2, TranscriptKinds.TurnEnd, null);

            await MarkSessionEndedAsync(first.PersistentSessionId!, SessionStatus.Stopped);

            using var scope = harness.Provider.CreateScope();
            var control = scope.ServiceProvider.GetRequiredService<AgentControlService>();
            await control.StartAsync(agent.Id, new StartAgentRequest(), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            await using var verify = CreateContext();
            (await verify.TranscriptEntries.AnyAsync(
                    t => t.AgentSessionId == sessionId && t.Kind == TranscriptKinds.SessionRestartBoundary))
                .ShouldBeFalse("a cleanly ended transcript needs no restart boundary");
            (await verify.SessionQueuedMessages.AnyAsync(m => m.AgentSessionId == sessionId))
                .ShouldBeFalse("no auto-continue for an agent that was not mid-turn");
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    private static async Task SeedTranscriptEntryAsync(Guid sessionId, long sequence, string kind, string? text)
    {
        await using var db = CreateContext();
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Sequence = sequence,
            Kind = kind,
            Text = text,
            Timestamp = DateTime.UtcNow.AddMinutes(-5).AddSeconds(sequence),
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    [Test]
    public async Task Start_interactive_falls_back_to_fresh_session_when_claude_conversation_is_missing()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var workspace = Path.Combine(tempRoot, "agent-workspace");
            Directory.CreateDirectory(workspace);
            var firstAdapter = new FakeAgentProtocolAdapter();
            var failingResumeAdapter = new FakeAgentProtocolAdapter { ReadyResult = false };
            var freshAdapter = new FakeAgentProtocolAdapter();
            await using var harness = BuildHarness(
                tempRoot, [firstAdapter, failingResumeAdapter, freshAdapter], defaultKind: "ClaudeCode");

            var agent = await harness.AgentService.CreateAsync(
                new CreateAgentRequest("Fallback Claude", workspace), CancellationToken.None);

            var first = await harness.Control.StartAsync(agent.Id, new StartAgentRequest(), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            failingResumeAdapter.StartupOutput = $"No conversation found with session ID: {first.PersistentSessionId}";

            await MarkSessionEndedAsync(first.PersistentSessionId!, SessionStatus.Stopped);

            using var scope = harness.Provider.CreateScope();
            var control = scope.ServiceProvider.GetRequiredService<AgentControlService>();
            var second = await control.StartAsync(agent.Id, new StartAgentRequest(), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            // The --resume attempt reported a missing conversation, so the launch fell back to a
            // fresh conversation under the same session id.
            failingResumeAdapter.StartedArgs.ShouldContain("--resume");
            failingResumeAdapter.Disposed.ShouldBeTrue();
            freshAdapter.StartedArgs.ShouldContain("--session-id");
            freshAdapter.StartedArgs.ShouldContain(first.PersistentSessionId);
            second.PersistentSessionId.ShouldBe(first.PersistentSessionId);

            await using var verify = CreateContext();
            var session = await verify.AgentSessions.SingleAsync(s => s.Id.ToString() == first.PersistentSessionId);
            session.Status.ShouldBe(SessionStatus.Running);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Start_interactive_with_fresh_request_starts_a_new_session()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var workspace = Path.Combine(tempRoot, "agent-workspace");
            Directory.CreateDirectory(workspace);
            var firstAdapter = new FakeAgentProtocolAdapter();
            var freshAdapter = new FakeAgentProtocolAdapter();
            await using var harness = BuildHarness(tempRoot, [firstAdapter, freshAdapter], defaultKind: "ClaudeCode");

            var agent = await harness.AgentService.CreateAsync(
                new CreateAgentRequest("Fresh Claude", workspace), CancellationToken.None);

            var first = await harness.Control.StartAsync(agent.Id, new StartAgentRequest(), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            await MarkSessionEndedAsync(first.PersistentSessionId!, SessionStatus.Stopped);

            using var scope = harness.Provider.CreateScope();
            var control = scope.ServiceProvider.GetRequiredService<AgentControlService>();
            var second = await control.StartAsync(
                agent.Id, new StartAgentRequest(Fresh: true), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            second.PersistentSessionId.ShouldNotBeNull();
            second.PersistentSessionId.ShouldNotBe(first.PersistentSessionId);
            freshAdapter.StartedArgs.ShouldContain("--session-id");
            freshAdapter.StartedArgs.ShouldContain(second.PersistentSessionId);
            freshAdapter.StartedArgs.ShouldNotContain("--resume");

            await using var verify = CreateContext();
            var sessionCount = await verify.AgentSessions.CountAsync(s => s.Cwd == workspace);
            sessionCount.ShouldBe(2);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Fresh_start_moves_pending_messages_and_repoints_in_flight_tasks()
    {
        // CARD-0079: the pair that 4A already wrote for messages, applied to tasks. A new session
        // id used to move Pending queue rows and leave Dispatched/Working tasks on the previous
        // id, so settlement looked in the wrong session and occupancy locked the specialist.
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        Guid? taskId = null;
        try
        {
            var workspace = Path.Combine(tempRoot, "agent-workspace");
            Directory.CreateDirectory(workspace);
            var firstAdapter = new FakeAgentProtocolAdapter();
            var freshAdapter = new FakeAgentProtocolAdapter();
            await using var harness = BuildHarness(tempRoot, [firstAdapter, freshAdapter], defaultKind: "ClaudeCode");

            var agent = await harness.AgentService.CreateAsync(
                new CreateAgentRequest("AlwaysOn Specialist", workspace, AlwaysOn: true),
                CancellationToken.None);

            var first = await harness.Control.StartAsync(agent.Id, new StartAgentRequest(), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            first.PersistentSessionId.ShouldNotBeNull();
            var previousSessionId = Guid.Parse(first.PersistentSessionId);

            taskId = Guid.NewGuid();
            var messageId = Guid.NewGuid();
            await using (var seed = CreateContext())
            {
                seed.SessionQueuedMessages.Add(new SessionQueuedMessage
                {
                    Id = messageId,
                    AgentSessionId = previousSessionId,
                    Body = "pending brief that must follow the new session",
                    Status = QueuedMessageStatus.Pending,
                    Sequence = 1,
                    Origin = QueuedMessageOrigin.Delegation,
                    CreatedAt = DateTime.UtcNow,
                });
                seed.AgentTasks.Add(new AgentTask
                {
                    Id = taskId.Value,
                    RootTaskId = taskId.Value,
                    Title = "in-flight interpretation",
                    Goal = "in-flight interpretation",
                    Kind = AgentTaskKind.Worker,
                    Role = AgentTaskRole.Check,
                    ReplyTo = AgentTaskReplyTo.None,
                    ModelLevel = AgentModelLevel.Low,
                    Workspace = WorkspaceMode.Shared,
                    WorkingDirectory = workspace,
                    AgentId = agent.Id,
                    AgentSessionId = previousSessionId,
                    Ephemeral = false,
                    Status = AgentTaskStatus.Dispatched,
                    CreatedAt = DateTime.UtcNow.AddMinutes(-5),
                    DispatchedAt = DateTime.UtcNow.AddMinutes(-5),
                });
                await seed.SaveChangesAsync();
            }

            await MarkSessionEndedAsync(first.PersistentSessionId, SessionStatus.Stopped);

            using var scope = harness.Provider.CreateScope();
            var control = scope.ServiceProvider.GetRequiredService<AgentControlService>();
            var second = await control.StartAsync(
                agent.Id, new StartAgentRequest(Fresh: true), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            second.PersistentSessionId.ShouldNotBeNull();
            second.PersistentSessionId.ShouldNotBe(first.PersistentSessionId);
            var newSessionId = Guid.Parse(second.PersistentSessionId);

            await using var verify = CreateContext();
            var moved = await verify.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == messageId);
            moved.AgentSessionId.ShouldBe(newSessionId, "Pending messages follow the new session");

            var remapped = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId.Value);
            remapped.AgentSessionId.ShouldBe(newSessionId, "in-flight tasks follow the new session");
            remapped.Status.ShouldBe(AgentTaskStatus.Dispatched, "re-pointing is not settlement");
        }
        finally
        {
            if (taskId is { } id)
            {
                await using var cleanup = CreateContext();
                await cleanup.AgentTasks.Where(t => t.Id == id).ExecuteDeleteAsync();
            }
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    // The agent queue reflects work REMAINING: reaching Review must remove the card from its
    // agent's queue and compact the positions behind it, exactly like the explicit queue-remove
    // endpoint. Left enqueued, the card re-spawns a session on every agent start (CARD-0001).
    [Test]
    public async Task Card_moved_to_review_is_dequeued_and_the_agent_queue_compacts()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            var template = NewWorkflowTemplate(tempRoot);
            db.Projects.Add(project);
            db.WorkflowTemplates.Add(template);
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(tempRoot, []);

            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Dequeue Board"), CancellationToken.None);
            var inProgressColumn = board.Columns.Single(c => c.StateKey == "in-progress");
            var reviewColumn = board.Columns.Single(c => c.StateKey == "review");
            var reviewedCard = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Finished work"), CancellationToken.None);
            var remainingCard = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Still queued"), CancellationToken.None);
            var agent = await harness.AgentService.CreateAsync(
                new CreateAgentRequest("Dequeue Claude", Path.Combine(tempRoot, "agent-workspace"), DefaultWorkflowTemplateId: template.Id),
                CancellationToken.None);
            await harness.AgentService.AssignCardAsync(
                agent.Id, new AssignAgentCardRequest(reviewedCard.Id), CancellationToken.None);
            await harness.AgentService.AssignCardAsync(
                agent.Id, new AssignAgentCardRequest(remainingCard.Id), CancellationToken.None);

            // Seed the card into In Progress directly (a service-level move into an active column
            // would spawn a session); the transition under test is InProgress -> Review.
            Guid concurrencyToken;
            await using (var seed = CreateContext())
            {
                var card = await seed.Cards.SingleAsync(c => c.Id == reviewedCard.Id);
                card.BoardColumnId = inProgressColumn.Id;
                card.Status = CardStatus.InProgress;
                await seed.SaveChangesAsync();
                concurrencyToken = card.ConcurrencyToken;
            }

            // Fresh scope, as a real request would be — the harness scope has stale tracked entities.
            using var scope = harness.Provider.CreateScope();
            var cardService = scope.ServiceProvider.GetRequiredService<CardService>();
            await cardService.MoveAsync(
                reviewedCard.Id, new MoveCardRequest(reviewColumn.Id, concurrencyToken), CancellationToken.None);

            await using var verify = CreateContext();
            var finished = await verify.Cards.SingleAsync(c => c.Id == reviewedCard.Id);
            finished.Status.ShouldBe(CardStatus.Review);
            finished.AssignedAgentId.ShouldBeNull("a finished card must leave its agent's queue");
            finished.AgentQueuePosition.ShouldBeNull();
            finished.ActiveWorkflowRunId.ShouldBeNull();

            var queued = await verify.Cards.SingleAsync(c => c.Id == remainingCard.Id);
            queued.AssignedAgentId.ShouldBe(agent.Id);
            queued.AgentQueuePosition.ShouldBe(1, "the queue compacts when the head leaves");
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    // Defense against rows written before the dequeue-on-transition policy existed (or racing past
    // it): a Review card still sitting at queue head — the exact CARD-0001 shape — must not be
    // spawned on. The start must fall through to a cardless interactive session and leave the
    // stale row untouched.
    [Test]
    public async Task Start_with_only_a_review_card_queued_starts_a_cardless_session()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            var template = NewWorkflowTemplate(tempRoot);
            db.Projects.Add(project);
            db.WorkflowTemplates.Add(template);
            await db.SaveChangesAsync();
            var workspace = Path.Combine(tempRoot, "agent-workspace");
            Directory.CreateDirectory(workspace);
            var adapter = new FakeAgentProtocolAdapter { PromptOutput = "BOOTED" };
            await using var harness = BuildHarness(tempRoot, [adapter], defaultKind: "ClaudeCode");

            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Stale Queue Board"), CancellationToken.None);
            var reviewColumn = board.Columns.Single(c => c.StateKey == "review");
            var card = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Already reviewed"), CancellationToken.None);
            var agent = await harness.AgentService.CreateAsync(
                new CreateAgentRequest("Stale Queue Claude", workspace, DefaultWorkflowTemplateId: template.Id),
                CancellationToken.None);
            await harness.AgentService.AssignCardAsync(
                agent.Id, new AssignAgentCardRequest(card.Id), CancellationToken.None);

            // Recreate the legacy state: status Review with the queue row (and CurrentCardId)
            // still in place, as pre-policy data has it.
            await using (var seed = CreateContext())
            {
                var staleCard = await seed.Cards.SingleAsync(c => c.Id == card.Id);
                staleCard.BoardColumnId = reviewColumn.Id;
                staleCard.Status = CardStatus.Review;
                var staleAgent = await seed.Agents.SingleAsync(a => a.Id == agent.Id);
                staleAgent.CurrentCardId = card.Id;
                await seed.SaveChangesAsync();
            }

            using var scope = harness.Provider.CreateScope();
            var control = scope.ServiceProvider.GetRequiredService<AgentControlService>();
            var detail = await control.StartAsync(agent.Id, new StartAgentRequest(), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            detail.Status.ShouldBe(AgentStatus.Running);
            detail.PersistentSessionId.ShouldNotBeNull();

            await using var verify = CreateContext();
            var cardSessions = await verify.AgentSessions.CountAsync(s => s.CardId == card.Id);
            cardSessions.ShouldBe(0, "a Review card is finished work — starting the agent must not respawn on it");
            var session = await verify.AgentSessions.SingleAsync(s => s.Id.ToString() == detail.PersistentSessionId);
            session.CardId.ShouldBeNull("the start falls through to a cardless interactive session");

            var startedAgent = await verify.Agents.SingleAsync(a => a.Id == agent.Id);
            startedAgent.CurrentCardId.ShouldBeNull();

            // The defensive skip only refuses to spawn — cleaning the stale row stays the
            // transition policy's job.
            var staleRow = await verify.Cards.SingleAsync(c => c.Id == card.Id);
            staleRow.AssignedAgentId.ShouldBe(agent.Id);
            staleRow.AgentQueuePosition.ShouldBe(1);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    // Two cards of the same agent can finish inside one unit of work (tracker sync sweep,
    // orchestrator reconcile). The compaction query's SQL filter sees pre-save values, so the
    // first dequeued card comes back as a candidate while dequeuing the second — it must not be
    // handed a queue position back.
    [Test]
    public async Task Dequeuing_two_finished_cards_in_one_unit_of_work_leaves_neither_positioned()
    {
        var tempRoot = NewTempRoot();
        try
        {
            await using var db = CreateContext();
            var now = DateTime.UtcNow;
            var project = NewProject(tempRoot);
            var board = new Board
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Name = $"Batch Dequeue Board {Guid.NewGuid():N}",
                CreatedAt = now,
                UpdatedAt = now,
                Project = project
            };
            var columns = BoardService.CreateDefaultColumns(board, now);
            var reviewColumn = columns.Single(c => c.StateKey == "review");
            var inProgressColumn = columns.Single(c => c.StateKey == "in-progress");
            var agent = new Agent
            {
                Id = Guid.NewGuid(),
                Name = $"Batch Dequeue Claude {Guid.NewGuid():N}",
                Slug = $"batch-dequeue-{Guid.NewGuid():N}",
                WorkingDirectory = Path.Combine(tempRoot, "agent-workspace"),
                CreatedAt = now,
                UpdatedAt = now
            };

            Card NewCard(string title, BoardColumn column, CardStatus status, int position) => new()
            {
                Id = Guid.NewGuid(),
                BoardId = board.Id,
                BoardColumnId = column.Id,
                Identifier = $"BATCH-{Guid.NewGuid():N}"[..12],
                Title = title,
                Status = status,
                AssignedAgentId = agent.Id,
                AgentQueuePosition = position,
                CreatedAt = now,
                UpdatedAt = now
            };

            var firstFinished = NewCard("First finished", reviewColumn, CardStatus.Review, 1);
            var secondFinished = NewCard("Second finished", reviewColumn, CardStatus.Review, 2);
            var stillQueued = NewCard("Still queued", inProgressColumn, CardStatus.InProgress, 3);
            db.Projects.Add(project);
            db.Boards.Add(board);
            db.BoardColumns.AddRange(columns);
            db.Agents.Add(agent);
            db.Cards.AddRange(firstFinished, secondFinished, stillQueued);
            await db.SaveChangesAsync();

            var firstRemoval = await CardLifecycleTransitions.DequeueFinishedCardAsync(
                db, firstFinished, now, CancellationToken.None);
            var secondRemoval = await CardLifecycleTransitions.DequeueFinishedCardAsync(
                db, secondFinished, now, CancellationToken.None);
            await db.SaveChangesAsync();

            firstRemoval.ShouldNotBeNull();
            secondRemoval.ShouldNotBeNull();
            secondRemoval.ShiftedCards.ShouldAllBe(
                c => c.Id == stillQueued.Id,
                "an already-dequeued card must not reappear as a compaction candidate");

            await using var verify = CreateContext();
            var first = await verify.Cards.SingleAsync(c => c.Id == firstFinished.Id);
            first.AssignedAgentId.ShouldBeNull();
            first.AgentQueuePosition.ShouldBeNull("the second dequeue's compaction must not re-position the first");
            var second = await verify.Cards.SingleAsync(c => c.Id == secondFinished.Id);
            second.AssignedAgentId.ShouldBeNull();
            second.AgentQueuePosition.ShouldBeNull();
            var queued = await verify.Cards.SingleAsync(c => c.Id == stillQueued.Id);
            queued.AssignedAgentId.ShouldBe(agent.Id);
            queued.AgentQueuePosition.ShouldBe(1);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Start_returns_409_subscription_quota_low_on_a_fresh_low_Codex_reading()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var workspace = Path.Combine(tempRoot, "quota-codex-workspace");
            Directory.CreateDirectory(workspace);
            var adapter = new FakeAgentProtocolAdapter();
            var provisioner = new AgentWorkspaceProvisioner(NullLogger<AgentWorkspaceProvisioner>.Instance);
            await using var harness = BuildHarness(
                tempRoot, [adapter], defaultKind: "Codex", includeQuotaGate: true, workspace: provisioner);

            var agent = await SeedAgentAsync(db, "Quota Codex", workspace, AgentKind.Codex, profileId: null);
            await SeedLowCodexSampleAsync(SubscriptionUsageKey.For(agent, AgentKind.Codex), remaining: 3, hoursToReset: 36);

            var ex = await Should.ThrowAsync<SubscriptionQuotaLowException>(
                () => harness.Control.StartAsync(agent.Id, new StartAgentRequest(), CancellationToken.None));

            ex.Code.ShouldBe("subscription_quota_low");
            ex.StatusCode.ShouldBe(409);
            var quota = ex.Extensions.ShouldNotBeNull()["quota"].ShouldBeOfType<SubscriptionQuotaProblemDto>();
            quota.RemainingPercent.ShouldBe(3);
            quota.Provider.ShouldBe("Codex");
            quota.Rule.ShouldBe("low-with-a-day-left");

            adapter.Started.ShouldBeFalse();
            File.Exists(Path.Combine(workspace, AgentWorkspaceProvisioner.FileName))
                .ShouldBeFalse("a refused launch must not Provision");
            await using var verify = CreateContext();
            (await verify.AgentSessions.CountAsync(s => s.Cwd == workspace)).ShouldBe(0);
            (await verify.Agents.SingleAsync(a => a.Id == agent.Id)).Status.ShouldBe(AgentStatus.Idle);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Start_with_IgnoreSubscriptionQuota_launches_and_writes_SubscriptionQuotaOverridden()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var workspace = Path.Combine(tempRoot, "quota-override-workspace");
            Directory.CreateDirectory(workspace);
            var adapter = new FakeAgentProtocolAdapter();
            await using var harness = BuildHarness(
                tempRoot, [adapter], defaultKind: "Codex", includeQuotaGate: true);

            var agent = await SeedAgentAsync(db, "Quota Override Codex", workspace, AgentKind.Codex, profileId: null);
            await SeedLowCodexSampleAsync(SubscriptionUsageKey.For(agent, AgentKind.Codex), remaining: 3, hoursToReset: 36);

            var detail = await harness.Control.StartAsync(
                agent.Id,
                new StartAgentRequest(IgnoreSubscriptionQuota: true),
                CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            detail.PersistentSessionId.ShouldNotBeNull();
            adapter.Started.ShouldBeTrue();

            await using var verify = CreateContext();
            var incident = await verify.AgentIncidents.SingleAsync(
                i => i.AgentId == agent.Id && i.Kind == AgentIncidentKind.SubscriptionQuotaOverridden);
            incident.Severity.ShouldBe(AlertSeverity.Warning);
            incident.Message.ShouldContain("3%");
            incident.Message.ShouldContain("low-with-a-day-left");
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Start_returns_409_model_disabled_when_the_agent_alias_is_held()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        var holdId = Guid.NewGuid();
        try
        {
            var workspace = Path.Combine(tempRoot, "model-hold-workspace");
            Directory.CreateDirectory(workspace);
            var adapter = new FakeAgentProtocolAdapter();
            await using var harness = BuildHarness(
                tempRoot, [adapter], defaultKind: "ClaudeCode", includeModelAvailability: true);

            var agent = await SeedAgentAsync(db, "Held Fable", workspace, AgentKind.ClaudeCode, profileId: null);
            agent.ModelLevel = AgentModelLevel.Frontier;
            await db.SaveChangesAsync();
            await db.ModelAvailabilityHolds
                .Where(h => h.Kind == AgentKind.ClaudeCode && h.ModelAlias == "fable" && h.ClearedAt == null)
                .ExecuteDeleteAsync();
            db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
            {
                Id = holdId,
                Kind = AgentKind.ClaudeCode,
                ModelAlias = "fable",
                Source = ModelAvailabilitySource.AutoDetected,
                DisabledUntil = DateTime.UtcNow.AddHours(1),
                HitAt = DateTime.UtcNow,
                Reason = "session-limit resets 18:10 Europe/London",
            });
            await db.SaveChangesAsync();

            var ex = await Should.ThrowAsync<ModelDisabledException>(
                () => harness.Control.StartAsync(agent.Id, new StartAgentRequest(), CancellationToken.None));
            ex.Code.ShouldBe("model_disabled");
            adapter.Started.ShouldBeFalse();

            var still = await Should.ThrowAsync<ModelDisabledException>(
                () => harness.Control.StartAsync(
                    agent.Id, new StartAgentRequest(IgnoreModelDisabled: true), CancellationToken.None));
            still.Code.ShouldBe("model_disabled");
            adapter.Started.ShouldBeFalse();
        }
        finally
        {
            await db.ModelAvailabilityHolds.Where(h => h.Id == holdId).ExecuteDeleteAsync();
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Start_of_a_haiku_agent_succeeds_while_fable_is_held()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        var holdId = Guid.NewGuid();
        try
        {
            var workspace = Path.Combine(tempRoot, "haiku-hold-workspace");
            Directory.CreateDirectory(workspace);
            var adapter = new FakeAgentProtocolAdapter();
            await using var harness = BuildHarness(
                tempRoot, [adapter], defaultKind: "ClaudeCode", includeModelAvailability: true);

            var agent = await SeedAgentAsync(db, "Haiku Interpreter", workspace, AgentKind.ClaudeCode, profileId: null);
            agent.ModelLevel = AgentModelLevel.Low;
            await db.SaveChangesAsync();
            await db.ModelAvailabilityHolds
                .Where(h => h.Kind == AgentKind.ClaudeCode && h.ModelAlias == "fable" && h.ClearedAt == null)
                .ExecuteDeleteAsync();
            db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
            {
                Id = holdId,
                Kind = AgentKind.ClaudeCode,
                ModelAlias = "fable",
                Source = ModelAvailabilitySource.AutoDetected,
                DisabledUntil = null,
                HitAt = DateTime.UtcNow,
                Reason = "Fable 5 per-model cap (no reset stated)",
            });
            await db.SaveChangesAsync();

            var detail = await harness.Control.StartAsync(
                agent.Id, new StartAgentRequest(Fresh: true), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            detail.PersistentSessionId.ShouldNotBeNull();
            adapter.Started.ShouldBeTrue();
        }
        finally
        {
            await db.ModelAvailabilityHolds.Where(h => h.Id == holdId).ExecuteDeleteAsync();
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Start_of_a_Claude_agent_passes_with_no_sample()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var workspace = Path.Combine(tempRoot, "quota-claude-workspace");
            Directory.CreateDirectory(workspace);
            var adapter = new FakeAgentProtocolAdapter();
            await using var harness = BuildHarness(
                tempRoot, [adapter], defaultKind: "ClaudeCode", includeQuotaGate: true);

            var agent = await SeedAgentAsync(db, "Quota Claude", workspace, AgentKind.ClaudeCode, profileId: null);

            var detail = await harness.Control.StartAsync(
                agent.Id, new StartAgentRequest(Fresh: true), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            detail.PersistentSessionId.ShouldNotBeNull();
            adapter.Started.ShouldBeTrue();
            await using var verify = CreateContext();
            (await verify.AgentIncidents.CountAsync(
                i => i.AgentId == agent.Id && i.Kind == AgentIncidentKind.SubscriptionQuotaOverridden))
                .ShouldBe(0);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Start_with_an_ANTIPHON_override_name_is_refused_422()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var workspace = Path.Combine(tempRoot, "override-workspace");
            Directory.CreateDirectory(workspace);
            var adapter = new FakeAgentProtocolAdapter();
            await using var harness = BuildHarness(tempRoot, [adapter]);
            var agent = await SeedAgentAsync(db, "Override Agent", workspace, AgentKind.Raw, profileId: null);

            var ex = await Should.ThrowAsync<ValidationException>(() => harness.Control.StartAsync(
                agent.Id,
                new StartAgentRequest(
                    Fresh: true,
                    LaunchEnvOverride: new Dictionary<string, string>
                    {
                        ["ANTIPHON_SESSION_ID"] = "hijacked",
                    }),
                CancellationToken.None));

            ex.StatusCode.ShouldBe(422);
            ex.Errors.Values.SelectMany(e => e).ShouldContain(e => e.Contains("ANTIPHON_SESSION_ID"));
            adapter.Started.ShouldBeFalse();
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    internal static async Task MarkSessionEndedAsync(string sessionId, SessionStatus status)
    {
        await using var db = CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id.ToString() == sessionId);
        session.Status = status;
        session.EndedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private static AppDbContext CreateContext(string? connectionString = null) =>
        new(TestDbFixture.CreateDbContextOptions(connectionString));

    internal static Harness BuildHarness(
        string tempRoot,
        IReadOnlyList<IAgentProtocolAdapter> adapters,
        string defaultKind = "Raw",
        bool includeLaunchResolver = false,
        string? connectionString = null,
        bool includeQuotaGate = false,
        bool includeModelAvailability = false,
        AgentWorkspaceProvisioner? workspace = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString ?? TestDbFixture.ConnectionString, npgsql =>
            {
                npgsql.MigrationsAssembly("Antiphon.Server");
                npgsql.SetPostgresVersion(16, 0);
            }));
        var eventBus = new MockEventBus();
        services.AddSingleton(eventBus);
        services.AddSingleton<IEventBus>(eventBus);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IOptions<AgentSessionSettings>>(Options.Create(new AgentSessionSettings
        {
            FirstDeltaTimeoutMs = 1_000,
            KillGraceMs = 100,
            SignalRMaxChunkChars = 16 * 1024,
            ReplayBufferMaxChars = 128 * 1024,
            SessionLogPath = Path.Combine(tempRoot, "session-logs"),
            RemoteControlArmTimeoutMs = 500, // fakes without the armed marker must not stall boots
            RemoteControlSetupTimeoutMs = 1_000,
        }));
        services.AddSingleton<IOptions<OrchestratorSettings>>(Options.Create(new OrchestratorSettings
        {
            InternalTrackerRepositoryPathPrefix = tempRoot
        }));
        services.AddSingleton<IOptions<DelegationSettings>>(Options.Create(new DelegationSettings()));
        services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(new OptionsMonitorStub<AgentRegistrySettings>(new AgentRegistrySettings
        {
            DefaultDefinition = "fake",
            Definitions = { ["fake"] = new AgentDefinition { Kind = defaultKind, Exe = Path.Combine(Environment.SystemDirectory, "cmd.exe") } }
        }));
        services.AddSingleton<AgentRegistry>();
        if (includeLaunchResolver)
        {
            services.AddSingleton<IAgentTuiSecretProtector, NoOpAgentTuiSecretProtector>();
            services.AddSingleton<AgentTuiMetrics>();
            services.AddSingleton<AgentTuiRunnerCatalog>();
            services.AddScoped<AgentTuiLaunchResolver>();
        }
        services.AddSingleton<IWorktreeManager>(new FakeWorktreeManager(Path.Combine(tempRoot, "worktrees")));
        services.AddSingleton<IAgentProtocolAdapterFactory>(new QueueAdapterFactory(adapters));
        services.AddSingleton<IWorkspaceHookRunner>(new WorkspaceHookRunner(NullLogger<WorkspaceHookRunner>.Instance));
        services.AddScoped<WorkspaceHookService>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddScoped<AgentSessionService>();
        services.AddScoped<RetryScheduler>();
        services.AddScoped<ExternalTrackerSyncService>();
        services.AddSingleton<OrchestratorControlState>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddScoped<AgentSessionLaunchComposer>();
        services.AddScoped<OrchestratorService>();
        services.AddScoped<CardWorkflowRunFactory>();
        services.AddScoped<AgentService>();
        services.AddScoped<AgentControlService>();
        if (workspace is not null)
            services.AddSingleton(workspace);
        if (includeQuotaGate)
        {
            services.AddScoped<SubscriptionUsageReader>();
            services.AddSingleton(Options.Create(new SubscriptionQuotaGateSettings()));
            services.AddScoped<SubscriptionQuotaGate>();
        }
        if (includeModelAvailability)
            services.AddScoped<ModelAvailability>();
        services.AddSingleton<Antiphon.Server.Application.Interfaces.IDirectoryWriter>(
            new Antiphon.Server.Infrastructure.FileSystem.FileSystemDirectoryWriter(new System.IO.Abstractions.FileSystem()));
        services.AddScoped<BoardService>();
        // CardService now depends on AgentReviewCheckpointService (files-review checkpoints);
        // register it and its GitWorkspaceService dep alongside, as ReviewLoopTests does.
        services.AddGitWorkspaceService();
        services.AddScoped<AgentReviewCheckpointService>();
        services.AddScoped<CardService>();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        return new Harness(
            provider,
            scope,
            scope.ServiceProvider.GetRequiredService<BoardService>(),
            scope.ServiceProvider.GetRequiredService<CardService>(),
            scope.ServiceProvider.GetRequiredService<AgentService>(),
            scope.ServiceProvider.GetRequiredService<AgentControlService>(),
            provider.GetRequiredService<AgentSessionLaunchQueue>(),
            eventBus);
    }

    private static async Task<AgentTuiProfile> SeedBlankModelArgumentProfileAsync(
        AppDbContext db, AgentKind kind)
    {
        var now = DateTime.UtcNow;
        var profile = new AgentTuiProfile
        {
            Id = Guid.NewGuid(),
            DisplayName = $"blank-arg-{kind}-{Guid.NewGuid():N}"[..40],
            Kind = kind,
            IsEnabled = true,
            IsDefault = false,
            Source = AgentTuiProfileSource.Operator,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.AgentTuiProfiles.Add(profile);
        await db.SaveChangesAsync();

        var revision = new AgentTuiProfileRevision
        {
            Id = Guid.NewGuid(),
            ProfileId = profile.Id,
            RevisionNumber = 1,
            Executable = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            ArgumentsJson = JsonSerializer.Serialize(new[] { "--always-approve" }),
            DiscoveryArgumentsJson = "[]",
            VersionArgumentsJson = "[]",
            AuthenticationMode = AgentTuiAuthenticationMode.WrapperManaged,
            NonSecretEnvironmentJson = "{}",
            SecretEnvironmentNamesJson = "[]",
            ModelArgumentName = null,
            Guidance = "CARD-0182 T6",
            CreatedAt = now
        };
        db.AgentTuiProfileRevisions.Add(revision);
        await db.SaveChangesAsync();
        profile.ActiveRevisionId = revision.Id;
        await db.SaveChangesAsync();
        return profile;
    }

    private static async Task<Agent> SeedAgentAsync(
        AppDbContext db, string name, string workspace, AgentKind kind, Guid? profileId)
    {
        var now = DateTime.UtcNow;
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = name,
            Slug = $"quota-{Guid.NewGuid():N}"[..16],
            WorkingDirectory = workspace,
            Details = string.Empty,
            Status = AgentStatus.Idle,
            Kind = kind,
            TuiProfileId = profileId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Agents.Add(agent);
        await db.SaveChangesAsync();
        return agent;
    }

    private static async Task SeedLowCodexSampleAsync(string key, double remaining, int hoursToReset)
    {
        var now = DateTime.UtcNow;
        await using var db = CreateContext();
        db.SubscriptionUsageSamples.Add(new SubscriptionUsageSample
        {
            Id = Guid.NewGuid(),
            Provider = AgentKind.Codex,
            SubscriptionKey = key,
            PlanLabel = "SuperPlan",
            RemainingPercent = remaining,
            ResetsAt = now.AddHours(hoursToReset),
            ObservedAt = now,
            AgentSessionId = Guid.NewGuid(),
            SourceCommand = "/status",
            ParseStatus = SubscriptionUsageParseStatus.Parsed,
            RawExcerpt = "seeded",
        });
        await db.SaveChangesAsync();
    }

    private static Project NewProject(string tempRoot)
    {
        var repoPath = Path.Combine(tempRoot, "repo");
        Directory.CreateDirectory(repoPath);
        var now = DateTime.UtcNow;
        return new Project
        {
            Id = Guid.NewGuid(),
            Name = $"Project {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/repo.git",
            LocalRepositoryPath = repoPath,
            BaseBranch = "main",
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static WorkflowTemplate NewWorkflowTemplate(string tempRoot)
    {
        var now = DateTime.UtcNow;
        return new WorkflowTemplate
        {
            Id = Guid.NewGuid(),
            Name = $"Agent Template {Guid.NewGuid():N}",
            Description = tempRoot,
            YamlDefinition = """
                name: One Shot
                description: Implement then review
                stages:
                  - name: Implement
                    executorType: agent
                    gateRequired: false
                  - name: Human Review
                    executorType: human
                    gateRequired: true
                """,
            IsBuiltIn = false,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    internal static string NewTempRoot() =>
        Path.Combine(Path.GetTempPath(), $"antiphon-agent-control-tests-{Guid.NewGuid():N}");

    internal static async Task CleanupProjectsByTempRootAsync(string tempRoot)
    {
        await using var db = CreateContext();
        var workflowTemplateIds = await db.WorkflowTemplates
            .Where(t => t.Description == tempRoot)
            .Select(t => t.Id)
            .ToListAsync();
        var projectIds = await db.Projects
            .Where(p => p.LocalRepositoryPath != null && p.LocalRepositoryPath.StartsWith(tempRoot))
            .Select(p => p.Id)
            .ToListAsync();
        if (projectIds.Count == 0)
        {
            // Interactive-only tests have no project: clean their agents and cardless sessions directly.
            await db.Agents
                .Where(a => a.WorkingDirectory.StartsWith(tempRoot))
                .ExecuteUpdateAsync(updates => updates.SetProperty(a => a.PersistentSessionId, (string?)null));
            await db.AgentSessions.Where(s => s.CardId == null && s.Cwd.StartsWith(tempRoot)).ExecuteDeleteAsync();
            await db.Agents.Where(a => a.WorkingDirectory.StartsWith(tempRoot)).ExecuteDeleteAsync();
            await db.WorkflowTemplates.Where(t => workflowTemplateIds.Contains(t.Id)).ExecuteDeleteAsync();
            return;
        }

        var boardIds = await db.Boards
            .Where(b => projectIds.Contains(b.ProjectId))
            .Select(b => b.Id)
            .ToListAsync();
        var cardIds = await db.Cards
            .Where(c => boardIds.Contains(c.BoardId))
            .Select(c => c.Id)
            .ToListAsync();
        var sessionIds = await db.AgentSessions
            .Where(s => s.CardId != null && cardIds.Contains(s.CardId.Value))
            .Select(s => s.Id)
            .ToListAsync();
        var workflowRunIds = await db.CardWorkflowRuns
            .Where(r => cardIds.Contains(r.CardId))
            .Select(r => r.Id)
            .ToListAsync();
        var agentIds = await db.Agents
            .Where(a => a.WorkingDirectory.StartsWith(tempRoot)
                || (a.CurrentCardId != null && cardIds.Contains(a.CurrentCardId.Value))
                || db.Cards.Any(c => cardIds.Contains(c.Id) && c.AssignedAgentId == a.Id)
                || db.CardWorkflowRuns.Any(r => workflowRunIds.Contains(r.Id) && r.AgentId == a.Id))
            .Select(a => a.Id)
            .ToListAsync();
        var attemptIds = await db.RunAttempts
            .Where(a => cardIds.Contains(a.CardId))
            .Select(a => a.Id)
            .ToListAsync();
        var worktreeIds = await db.Worktrees
            .Where(w => cardIds.Contains(w.CardId))
            .Select(w => w.Id)
            .ToListAsync();

        // Agents may reference a board (default board) and cards reference agents/sessions/runs —
        // null the cross-links before deleting so FK constraints don't block the teardown.
        await db.Agents
            .Where(a => agentIds.Contains(a.Id))
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(a => a.CurrentCardId, (Guid?)null)
                .SetProperty(a => a.BoardId, (Guid?)null)
                .SetProperty(a => a.PersistentSessionId, (string?)null));
        await db.Cards
            .Where(c => cardIds.Contains(c.Id))
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(c => c.OwnerSessionId, (Guid?)null)
                .SetProperty(c => c.CurrentWorktreeId, (Guid?)null)
                .SetProperty(c => c.AssignedAgentId, (Guid?)null)
                .SetProperty(c => c.ActiveWorkflowRunId, (Guid?)null));
        await db.CardWorkflowRuns
            .Where(r => workflowRunIds.Contains(r.Id))
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(r => r.CurrentStageId, (Guid?)null));
        await db.CardWorkflowStages.Where(s => workflowRunIds.Contains(s.CardWorkflowRunId)).ExecuteDeleteAsync();
        await db.CardWorkflowRuns.Where(r => workflowRunIds.Contains(r.Id)).ExecuteDeleteAsync();
        await db.TokenUsages.Where(t => attemptIds.Contains(t.RunAttemptId)).ExecuteDeleteAsync();
        await db.RetrySchedules.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
        await db.ExternalIssueRefs.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
        await db.RunAttempts.Where(a => attemptIds.Contains(a.Id)).ExecuteDeleteAsync();
        await db.AgentSessions.Where(s => sessionIds.Contains(s.Id)).ExecuteDeleteAsync();
        // Cardless interactive sessions are keyed only by their Cwd inside the temp root.
        await db.AgentSessions.Where(s => s.CardId == null && s.Cwd.StartsWith(tempRoot)).ExecuteDeleteAsync();
        await db.Worktrees.Where(w => worktreeIds.Contains(w.Id)).ExecuteDeleteAsync();
        await db.Cards.Where(c => cardIds.Contains(c.Id)).ExecuteDeleteAsync();
        await db.BoardWorkflowDefinitions.Where(d => boardIds.Contains(d.BoardId)).ExecuteDeleteAsync();
        await db.BoardColumns.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync();
        await db.Boards.Where(b => boardIds.Contains(b.Id)).ExecuteDeleteAsync();
        await db.Agents.Where(a => agentIds.Contains(a.Id)).ExecuteDeleteAsync();
        await db.Projects.Where(p => projectIds.Contains(p.Id)).ExecuteDeleteAsync();
        await db.WorkflowTemplates.Where(t => workflowTemplateIds.Contains(t.Id)).ExecuteDeleteAsync();
    }

    internal static void DeleteDirectoryBestEffort(string path)
    {
        try
        {
            if (!Directory.Exists(path))
                return;

            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);

            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for temp worktree/session directories.
        }
    }

    internal sealed record Harness(
        ServiceProvider Provider,
        IServiceScope Scope,
        BoardService BoardService,
        CardService CardService,
        AgentService AgentService,
        AgentControlService Control,
        AgentSessionLaunchQueue LaunchQueue,
        MockEventBus EventBus) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Scope.Dispose();
            await Provider.DisposeAsync();
        }
    }

    private sealed class QueueAdapterFactory : IAgentProtocolAdapterFactory
    {
        private readonly Queue<IAgentProtocolAdapter> _adapters;

        public QueueAdapterFactory(IEnumerable<IAgentProtocolAdapter> adapters)
        {
            _adapters = new Queue<IAgentProtocolAdapter>(adapters);
        }

        public IAgentProtocolAdapter Create(AgentKind kind)
        {
            if (_adapters.TryDequeue(out var adapter))
                return adapter;

            throw new InvalidOperationException("No fake adapter was queued for dispatch.");
        }
    }

    private sealed class NoOpAgentTuiSecretProtector : IAgentTuiSecretProtector
    {
        public string Protect(Guid profileId, string environmentName, string plaintext) => plaintext;

        public string Unprotect(Guid profileId, string environmentName, string protectedValue) => protectedValue;
    }

    private sealed class FakeWorktreeManager : IWorktreeManager
    {
        private readonly string _worktreeRoot;

        public FakeWorktreeManager(string worktreeRoot)
        {
            _worktreeRoot = worktreeRoot;
        }

        public Task<WorktreeInfo> CreateAsync(string repoPath, string cardId, string baseRef, CancellationToken ct)
        {
            Directory.CreateDirectory(_worktreeRoot);
            var worktreePath = Path.Combine(_worktreeRoot, $"card-{cardId}");
            Directory.CreateDirectory(worktreePath);
            var now = DateTimeOffset.UtcNow;
            var info = new WorktreeInfo(cardId, repoPath, worktreePath, $"feat/card-{cardId}", baseRef, now, now);
            return Task.FromResult(info);
        }

        public Task<IReadOnlyList<WorktreeInfo>> ListAsync(string repoPath, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WorktreeInfo>>([]);

        public Task RemoveAsync(string repoPath, string worktreePath, CancellationToken ct) => Task.CompletedTask;

        public Task TouchAsync(string worktreePath, CancellationToken ct) => Task.CompletedTask;

        public Task<int> PruneStaleAsync(CancellationToken ct) => Task.FromResult(0);
    }

    private sealed class OptionsMonitorStub<T> : IOptionsMonitor<T>
    {
        public OptionsMonitorStub(T currentValue)
        {
            CurrentValue = currentValue;
        }

        public T CurrentValue { get; }

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
