using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
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
public sealed class CardSpawnModelArgumentTests
{
    [Test]
    public async Task Card_spawn_installs_the_same_composition_before_its_boot_prompt()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var root = AgentControlServiceIntegrationTests.NewTempRoot();
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readyGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapter = new FakeAgentProtocolAdapter { StartGate = startGate, ReadyHold = readyGate, PromptOutput = "card work answered" };
        await using var harness = AgentControlServiceIntegrationTests.BuildHarness(root, [adapter],
            defaultKind: "Raw", includeLaunchResolver: true, connectionString: schema.ConnectionString,
            configureServices: services => {
                services.AddSingleton(Options.Create(new GrokRulesSettings()));
                services.AddSingleton<GrokRulesRefreshService>();
            });
        adapter.RegisterOnStart = harness.Provider.GetRequiredService<AgentSessionRuntime>();
        harness.Runner.AdvertiseGrokRules = true;
        try
        {
            await using var db = NewDb(schema.ConnectionString);
            var profile = await SeedProfileAsync(db, AgentKind.Grok, "--model");
            var card = await SeedAssignedCardAsync(db, harness, root, profile.Id, AgentKind.Grok, AgentModelLevel.High, null);
            var agent = await db.Agents.SingleAsync(a => a.Id == card.AssignedAgentId);
            agent.SystemPromptAppend = "CARD-RULE-FIRST\r\nkeep multiline canaries ðŸ˜€\r\nCARD-RULE-LAST";
            agent.ReplyStyle = AgentReplyStyle.Terse;
            await AgentBundleAttachments.SetAsync(db, agent, [InstructionBundles.Orchestrator], DateTime.UtcNow, CancellationToken.None);
            await db.SaveChangesAsync();
            var composed = InstructionBundleComposer.Compose([InstructionBundles.Orchestrator], AgentReplyStyles.ComposedKey(agent.ReplyStyle), agent.SystemPromptAppend);
            ClearHarnessTracking(harness);
            var spawn = await harness.CardService.SpawnAsync(card.Id, new SpawnCardRequest(), CancellationToken.None);
            var id = spawn.SessionId;
            var until = DateTime.UtcNow.AddSeconds(15);
            while (!await db.AgentSessions.AnyAsync(s => s.Id == id && s.GrokRulesGeneration != null)
                && DateTime.UtcNow < until) await Task.Delay(20);
            var prepared = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == id);
            prepared.GrokRulesGeneration.ShouldNotBeNull();
            var receipt = await new Antiphon.SessionRunner.GrokRulesFileStore(Path.Combine(root, "runner"), new())
                .WriteAsync(id, new(composed.Text, 1, prepared.GrokRulesGeneration.Value), CancellationToken.None);
            harness.Runner.RulesReceipt = queried => queried == id ? receipt : null;
            startGate.SetResult();
            until = DateTime.UtcNow.AddSeconds(15);
            while (!await db.SessionQueuedMessages.AnyAsync(m => m.AgentSessionId == id && m.RulesRefreshKey != null)
                && DateTime.UtcNow < until) await Task.Delay(20);
            var refresh = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == id && m.RulesRefreshKey != null);
            refresh.RulesDeadlineAt.ShouldBeNull();
            adapter.Prompts.ShouldBeEmpty();
            adapter.SubmittedBodies.ShouldBeEmpty("card boot prompt must not cross readiness or rules initialization");
            adapter.StartedArgs.ShouldContain(GrokLaunchArgs.ReasoningEffortFlag);
            adapter.StartedArgs.ShouldNotContain(a => a.Contains("CARD-RULE-FIRST"));
            (await File.ReadAllBytesAsync(receipt.Path)).ShouldBe(System.Text.Encoding.UTF8.GetBytes(composed.Text));
            adapter.OnSubmitted = async body => {
                await using var transcript = NewDb(schema.ConnectionString);
                var seq = await transcript.TranscriptEntries.Where(e => e.AgentSessionId == id).MaxAsync(e => (long?)e.Sequence) ?? 0;
                var rules = body.StartsWith(GrokRulesRefreshService.Header(refresh.Id), StringComparison.Ordinal);
                var reply = rules ? $"ANTIPHON_RULES_ACK id={refresh.Id:N} generation={receipt.Generation:N} sha256={receipt.Sha256}" : "card work answered";
                foreach (var pair in new[] { (TranscriptKinds.UserPrompt, (string?)body), (TranscriptKinds.AssistantText, reply), (TranscriptKinds.TurnEnd, (string?)null) })
                    transcript.TranscriptEntries.Add(new() { Id = Guid.NewGuid(), AgentSessionId = id, Sequence = ++seq,
                        Kind = pair.Item1, Text = pair.Item2, Timestamp = DateTime.UtcNow, CreatedAt = DateTime.UtcNow,
                        StopReason = pair.Item1 == TranscriptKinds.TurnEnd ? "end_turn" : null });
                await transcript.SaveChangesAsync();
            };
            adapter.PromptFailure = prompt => {
                adapter.SubmittedBodies.Count.ShouldBe(1, "rules refresh must precede direct boot delivery");
                using var committed = NewDb(schema.ConnectionString);
                committed.AgentSessions.AsNoTracking().Single(s => s.Id == id).GrokRulesState.ShouldBe(GrokRulesState.Ready);
                return null;
            };
            readyGate.SetResult(true);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(40), CancellationToken.None);
            var session = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == id);
            session.Status.ShouldBe(SessionStatus.Running, session.FailureReason);
            session.GrokRulesState.ShouldBe(GrokRulesState.Ready);
            session.ComposedBundleStamp.ShouldBe(composed.StampLine);
            adapter.SubmittedBodies.Count.ShouldBe(1);
            adapter.SubmittedBodies[0].ShouldStartWith(GrokRulesRefreshService.Header(refresh.Id));
            adapter.Prompts.Count.ShouldBe(1);
            adapter.Prompts[0].ShouldContain(card.Title);
        }
        finally
        {
            readyGate.TrySetResult(false); startGate.TrySetResult();
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(40), CancellationToken.None);
            AgentControlServiceIntegrationTests.DeleteDirectoryBestEffort(root);
        }
    }


    [Test]
    public async Task Assigned_card_spawn_composes_instructions_and_authenticates_as_its_own_session()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var tempRoot = AgentControlServiceIntegrationTests.NewTempRoot();
        try
        {
            await using var db = NewDb(schema.ConnectionString);
            var adapter = new FakeAgentProtocolAdapter();
            await using var harness = AgentControlServiceIntegrationTests.BuildHarness(
                tempRoot, [adapter], defaultKind: "Raw", includeLaunchResolver: true,
                connectionString: schema.ConnectionString);
            var profile = await SeedProfileAsync(db, AgentKind.ClaudeCode, modelArgumentName: "--model");
            var card = await SeedAssignedCardAsync(db, harness, tempRoot, profile.Id, AgentKind.ClaudeCode,
                AgentModelLevel.High, modelId: null);
            var agent = await db.Agents.SingleAsync(a => a.Id == card.AssignedAgentId);
            agent.SystemPromptAppend = "Agent {agentName}; channels: {channels}.";
            db.ChatChannels.Add(new ChatChannel
            {
                Id = Guid.NewGuid(), Provider = "telegram", ExternalId = "ops", Title = "Ops",
                AgentId = agent.Id, Enabled = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            await SpawnOnlyCardAsync(db, harness);

            adapter.StartedArgs.ShouldContain("--name");
            adapter.StartedArgs.ShouldContain("Card spawn agent");
            adapter.StartedArgs.ShouldContain("--append-system-prompt");
            adapter.StartedArgs.ShouldContain(a => a.Contains("Agent Card spawn agent; channels: telegram \"Ops\"."));
            adapter.StartedEnv.ShouldContainKey("ANTIPHON_TASK_TOKEN");
            adapter.StartedEnv.ShouldContainKey("ANTIPHON_AGENT_ID");

            var session = await db.AgentSessions.SingleAsync(s => s.CardId == card.Id);
            session.DelegationTokenHash.ShouldBe(AgentTaskService.HashToken(adapter.StartedEnv["ANTIPHON_TASK_TOKEN"]));
            session.ComposedBundleStamp.ShouldNotBeNull();
            var caller = await new AgentTaskService(
                db, null!, Options.Create(new DelegationSettings()), new MockEventBus(), null!,
                TimeProvider.System, NullLogger<AgentTaskService>.Instance)
                .AuthenticateAsync(adapter.StartedEnv["ANTIPHON_TASK_TOKEN"], CancellationToken.None);
            caller.Task.ShouldBeNull();
            caller.SessionId.ShouldBe(session.Id);
        }
        finally
        {
            AgentControlServiceIntegrationTests.DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Codex_assigned_card_spawn_carries_the_reasoning_effort_override()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var tempRoot = AgentControlServiceIntegrationTests.NewTempRoot();
        try
        {
            await using var db = NewDb(schema.ConnectionString);
            var adapter = new FakeAgentProtocolAdapter();
            await using var harness = AgentControlServiceIntegrationTests.BuildHarness(
                tempRoot, [adapter], defaultKind: "Raw", includeLaunchResolver: true,
                connectionString: schema.ConnectionString);
            var profile = await SeedProfileAsync(db, AgentKind.Codex, modelArgumentName: "--model");
            await SeedAssignedCardAsync(db, harness, tempRoot, profile.Id, AgentKind.Codex,
                AgentModelLevel.High, modelId: null);

            await SpawnOnlyCardAsync(db, harness);

            adapter.StartedArgs.ShouldContain(CodexLaunchArgs.ConfigFlag);
            adapter.StartedArgs.ShouldContain(CodexLaunchArgs.ReasoningEffortOverride(AgentModelLevel.High));
            adapter.StartedArgs.ShouldContain(CodexLaunchArgs.DisablePasteBurst);
        }
        finally
        {
            AgentControlServiceIntegrationTests.DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Grok_assigned_card_spawn_carries_the_reasoning_effort_flag()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var tempRoot = AgentControlServiceIntegrationTests.NewTempRoot();
        try
        {
            await using var db = NewDb(schema.ConnectionString);
            var adapter = new FakeAgentProtocolAdapter();
            await using var harness = AgentControlServiceIntegrationTests.BuildHarness(
                tempRoot, [adapter], defaultKind: "Raw", includeLaunchResolver: true,
                connectionString: schema.ConnectionString);
            var profile = await SeedProfileAsync(db, AgentKind.Grok, modelArgumentName: "--model");
            await SeedAssignedCardAsync(db, harness, tempRoot, profile.Id, AgentKind.Grok,
                AgentModelLevel.High, modelId: null);

            await SpawnOnlyCardAsync(db, harness);

            adapter.StartedArgs.ShouldContain(GrokLaunchArgs.ReasoningEffortFlag);
            var flag = adapter.StartedArgs.ToList().IndexOf(GrokLaunchArgs.ReasoningEffortFlag);
            adapter.StartedArgs[flag + 1].ShouldBe(GrokLaunchArgs.ReasoningEffort(AgentModelLevel.High));
            adapter.StartedArgs.ShouldNotContain(ClaudeLaunchArgs.EffortFlag);
        }
        finally
        {
            AgentControlServiceIntegrationTests.DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Grok_assigned_card_spawn_with_unsafe_inline_profile_rules_is_refused_and_creates_no_session()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var tempRoot = AgentControlServiceIntegrationTests.NewTempRoot();
        try
        {
            await using var db = NewDb(schema.ConnectionString);
            var adapter = new FakeAgentProtocolAdapter();
            await using var harness = AgentControlServiceIntegrationTests.BuildHarness(
                tempRoot, [adapter], defaultKind: "Raw", includeLaunchResolver: true,
                connectionString: schema.ConnectionString);
            var profile = await SeedProfileAsync(db, AgentKind.Grok, modelArgumentName: "--model");
            var revision = await db.AgentTuiProfileRevisions.SingleAsync(r => r.Id == profile.ActiveRevisionId);
            revision.ArgumentsJson = JsonSerializer.Serialize(new[] { "--rules", "Unsafe inline line one\nline two" });
            await db.SaveChangesAsync();
            var card = await SeedAssignedCardAsync(db, harness, tempRoot, profile.Id, AgentKind.Grok,
                AgentModelLevel.High, modelId: null);
            var agent = await db.Agents.SingleAsync(a => a.Id == card.AssignedAgentId);
            var sentinel = "card0382-sentinel-" + Guid.NewGuid().ToString("N");
            agent.SystemPromptAppend = "Safe server-owned multiline rules\n" + sentinel;
            await db.SaveChangesAsync();

            ClearHarnessTracking(harness);
            var ex = await Should.ThrowAsync<ConflictException>(() =>
                harness.CardService.SpawnAsync(card.Id, new SpawnCardRequest(), CancellationToken.None));
            ex.Code.ShouldBe(GrokRulesArgvPolicy.ProblemCode);
            adapter.Started.ShouldBeFalse();
            (await db.AgentSessions.CountAsync(s => s.CardId == card.Id)).ShouldBe(0);
        }
        finally
        {
            AgentControlServiceIntegrationTests.DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Grok_4_5_frontier_card_spawn_clamps_xhigh_to_high()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var tempRoot = AgentControlServiceIntegrationTests.NewTempRoot();
        try
        {
            await using var db = NewDb(schema.ConnectionString);
            var adapter = new FakeAgentProtocolAdapter();
            await using var harness = AgentControlServiceIntegrationTests.BuildHarness(
                tempRoot, [adapter], defaultKind: "Raw", includeLaunchResolver: true,
                connectionString: schema.ConnectionString);
            var profile = await SeedProfileAsync(db, AgentKind.Grok, modelArgumentName: "--model",
                models: ["grok-4.5"]);
            await SeedAssignedCardAsync(db, harness, tempRoot, profile.Id, AgentKind.Grok,
                AgentModelLevel.Frontier, modelId: "grok-4.5");

            await SpawnOnlyCardAsync(db, harness);

            adapter.StartedArgs.ShouldContain(GrokLaunchArgs.ReasoningEffortFlag);
            var flag = adapter.StartedArgs.ToList().IndexOf(GrokLaunchArgs.ReasoningEffortFlag);
            adapter.StartedArgs[flag + 1].ShouldBe("high");
        }
        finally
        {
            AgentControlServiceIntegrationTests.DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Claude_assigned_card_spawn_carries_the_effort_flag()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var tempRoot = AgentControlServiceIntegrationTests.NewTempRoot();
        try
        {
            await using var db = NewDb(schema.ConnectionString);
            var adapter = new FakeAgentProtocolAdapter();
            await using var harness = AgentControlServiceIntegrationTests.BuildHarness(
                tempRoot, [adapter], defaultKind: "Raw", includeLaunchResolver: true,
                connectionString: schema.ConnectionString);
            var profile = await SeedProfileAsync(db, AgentKind.ClaudeCode, modelArgumentName: "--model");
            await SeedAssignedCardAsync(db, harness, tempRoot, profile.Id, AgentKind.ClaudeCode,
                AgentModelLevel.High, modelId: null);

            await SpawnOnlyCardAsync(db, harness);

            adapter.StartedArgs.ShouldContain(ClaudeLaunchArgs.EffortFlag);
            var flag = adapter.StartedArgs.ToList().IndexOf(ClaudeLaunchArgs.EffortFlag);
            adapter.StartedArgs[flag + 1].ShouldBe(ClaudeLaunchArgs.Effort(AgentModelLevel.High));
            adapter.StartedArgs.ShouldNotContain(GrokLaunchArgs.ReasoningEffortFlag);
        }
        finally
        {
            AgentControlServiceIntegrationTests.DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Agentless_card_spawn_has_a_delegation_token_without_agent_composition()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var tempRoot = AgentControlServiceIntegrationTests.NewTempRoot();
        try
        {
            await using var db = NewDb(schema.ConnectionString);
            var adapter = new FakeAgentProtocolAdapter();
            await using var harness = AgentControlServiceIntegrationTests.BuildHarness(
                tempRoot, [adapter], defaultKind: "Raw", includeLaunchResolver: true,
                connectionString: schema.ConnectionString);
            var card = await SeedUnassignedCardAsync(db, harness, tempRoot);

            ClearHarnessTracking(harness);
            await harness.CardService.SpawnAsync(
                card.Id, new SpawnCardRequest(DefinitionName: "fake"), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            adapter.StartedEnv.ShouldContainKey("ANTIPHON_TASK_TOKEN");
            adapter.StartedEnv.ShouldNotContainKey("ANTIPHON_AGENT_ID");
            adapter.StartedArgs.ShouldNotContain("--name");
            adapter.StartedArgs.ShouldNotContain("--append-system-prompt");
            var session = await db.AgentSessions.SingleAsync(s => s.CardId == card.Id);
            session.DelegationTokenHash.ShouldBe(AgentTaskService.HashToken(adapter.StartedEnv["ANTIPHON_TASK_TOKEN"]));
            session.ComposedBundleStamp.ShouldBeNull();
        }
        finally
        {
            AgentControlServiceIntegrationTests.DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Assigned_card_spawn_with_a_blank_model_uses_the_agents_claude_tier()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var tempRoot = AgentControlServiceIntegrationTests.NewTempRoot();
        try
        {
            await using var db = NewDb(schema.ConnectionString);
            var adapter = new FakeAgentProtocolAdapter();
            await using var harness = AgentControlServiceIntegrationTests.BuildHarness(
                tempRoot, [adapter], defaultKind: "Raw", includeLaunchResolver: true,
                connectionString: schema.ConnectionString);
            var profile = await SeedProfileAsync(db, AgentKind.ClaudeCode, modelArgumentName: "--model");
            var card = await SeedAssignedCardAsync(db, harness, tempRoot, profile.Id, AgentKind.ClaudeCode,
                AgentModelLevel.High, modelId: null);

            var directAgent = await db.Agents.SingleAsync(a => a.Id == card.AssignedAgentId);
            var direct = await AgentLaunchResolution.ResolveForAgentAsync(
                directAgent,
                harness.Scope.ServiceProvider.GetRequiredService<AgentRegistry>(),
                harness.Scope.ServiceProvider.GetRequiredService<AgentTuiLaunchResolver>(),
                new AgentLaunchOptions(),
                CancellationToken.None);
            ModelPair(direct.Spec.Args).ShouldBe(["--model", "opus"]);
            await SpawnOnlyCardAsync(db, harness);

            adapter.Started.ShouldBeTrue();
            ModelPair(adapter.StartedArgs).ShouldBe(["--model", "opus"]);
        }
        finally
        {
            AgentControlServiceIntegrationTests.DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Card_spawn_matches_a_cardless_start_for_the_same_tier()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var tempRoot = AgentControlServiceIntegrationTests.NewTempRoot();
        try
        {
            await using var db = NewDb(schema.ConnectionString);
            var cardlessAdapter = new FakeAgentProtocolAdapter();
            var cardAdapter = new FakeAgentProtocolAdapter();
            await using var harness = AgentControlServiceIntegrationTests.BuildHarness(
                tempRoot, [cardlessAdapter, cardAdapter], defaultKind: "Raw", includeLaunchResolver: true,
                connectionString: schema.ConnectionString);
            var profile = await SeedProfileAsync(db, AgentKind.ClaudeCode, modelArgumentName: "--model");
            var cardless = await SeedAgentAsync(db, tempRoot, profile.Id, AgentKind.ClaudeCode,
                AgentModelLevel.Medium, modelId: null);

            await harness.Control.StartAsync(cardless.Id, new StartAgentRequest(Fresh: true), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            await SeedAssignedCardAsync(db, harness, tempRoot, profile.Id, AgentKind.ClaudeCode,
                AgentModelLevel.Medium, modelId: null);
            await SpawnOnlyCardAsync(db, harness);

            SessionIndependentArgs(cardlessAdapter.StartedArgs).ShouldBe(SessionIndependentArgs(cardAdapter.StartedArgs));
            ModelPair(cardlessAdapter.StartedArgs).ShouldBe(ModelPair(cardAdapter.StartedArgs));
            ModelPair(cardAdapter.StartedArgs).ShouldBe(["--model", "sonnet"]);
        }
        finally
        {
            AgentControlServiceIntegrationTests.DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Blank_model_argument_profile_suppresses_a_derived_tier_with_profile_owned_provenance()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var tempRoot = AgentControlServiceIntegrationTests.NewTempRoot();
        try
        {
            await using var db = NewDb(schema.ConnectionString);
            var adapter = new FakeAgentProtocolAdapter();
            await using var harness = AgentControlServiceIntegrationTests.BuildHarness(
                tempRoot, [adapter], defaultKind: "Raw", includeLaunchResolver: true,
                connectionString: schema.ConnectionString);
            var profile = await SeedProfileAsync(db, AgentKind.Raw, modelArgumentName: null);
            var card = await SeedAssignedCardAsync(db, harness, tempRoot, profile.Id, AgentKind.Raw,
                AgentModelLevel.High, modelId: null);

            ClearHarnessTracking(harness);
            await harness.CardService.SpawnAsync(card.Id, new SpawnCardRequest(), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            adapter.StartedArgs.ShouldNotContain("--model");
            adapter.StartedArgs.ShouldNotContain("opus");
            var agent = await db.Agents.SingleAsync(a => a.TuiProfileId == profile.Id);
            var resolved = await AgentLaunchResolution.ResolveForAgentAsync(
                agent,
                harness.Scope.ServiceProvider.GetRequiredService<AgentRegistry>(),
                harness.Scope.ServiceProvider.GetRequiredService<AgentTuiLaunchResolver>(),
                new AgentLaunchOptions(),
                CancellationToken.None);
            resolved.ModelArgument.ShouldBe(LaunchModelArgument.ProfileOwned);
        }
        finally
        {
            AgentControlServiceIntegrationTests.DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Exact_model_on_a_card_spawn_wins_once_over_the_derived_tier()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var tempRoot = AgentControlServiceIntegrationTests.NewTempRoot();
        try
        {
            await using var db = NewDb(schema.ConnectionString);
            var adapter = new FakeAgentProtocolAdapter();
            await using var harness = AgentControlServiceIntegrationTests.BuildHarness(
                tempRoot, [adapter], defaultKind: "Raw", includeLaunchResolver: true,
                connectionString: schema.ConnectionString);
            var profile = await SeedProfileAsync(db, AgentKind.ClaudeCode, modelArgumentName: "--model", models: ["exact-model"]);
            await SeedAssignedCardAsync(db, harness, tempRoot, profile.Id, AgentKind.ClaudeCode,
                AgentModelLevel.High, modelId: "exact-model");

            await SpawnOnlyCardAsync(db, harness);

            adapter.StartedArgs.Count(a => a == "--model").ShouldBe(1);
            ModelPair(adapter.StartedArgs).ShouldBe(["--model", "exact-model"]);
        }
        finally
        {
            AgentControlServiceIntegrationTests.DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Spawn_with_remote_control_name_on_a_grok_card_is_refused_and_creates_no_session()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var tempRoot = AgentControlServiceIntegrationTests.NewTempRoot();
        try
        {
            await using var db = NewDb(schema.ConnectionString);
            var adapter = new FakeAgentProtocolAdapter();
            await using var harness = AgentControlServiceIntegrationTests.BuildHarness(
                tempRoot, [adapter], defaultKind: "Grok", includeLaunchResolver: true,
                connectionString: schema.ConnectionString);
            var profile = await SeedProfileAsync(db, AgentKind.Grok, modelArgumentName: "--model");
            var card = await SeedAssignedCardAsync(db, harness, tempRoot, profile.Id, AgentKind.Grok,
                AgentModelLevel.High, modelId: null);

            ClearHarnessTracking(harness);
            var ex = await Should.ThrowAsync<ConflictException>(() =>
                harness.CardService.SpawnAsync(
                    card.Id,
                    new SpawnCardRequest(RemoteControlName: "Grok Card Agent"),
                    CancellationToken.None));
            ex.Code.ShouldBe("remote_control_refused");
            ex.Message.ShouldContain("Grok");

            adapter.Started.ShouldBeFalse();
            (await db.AgentSessions.CountAsync(s => s.CardId == card.Id)).ShouldBe(0);
        }
        finally
        {
            AgentControlServiceIntegrationTests.DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Unassigned_card_spawn_offers_no_synthetic_default_tier()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var tempRoot = AgentControlServiceIntegrationTests.NewTempRoot();
        try
        {
            await using var db = NewDb(schema.ConnectionString);
            var adapter = new FakeAgentProtocolAdapter();
            await using var harness = AgentControlServiceIntegrationTests.BuildHarness(
                tempRoot, [adapter], defaultKind: "Raw", includeLaunchResolver: true,
                connectionString: schema.ConnectionString);
            await SeedProfileAsync(db, AgentKind.Raw, modelArgumentName: "--model", isDefault: true);
            var card = await SeedUnassignedCardAsync(db, harness, tempRoot);

            ClearHarnessTracking(harness);
            await harness.CardService.SpawnAsync(card.Id, new SpawnCardRequest(), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            adapter.StartedArgs.ShouldNotContain("--model");
        }
        finally
        {
            AgentControlServiceIntegrationTests.DeleteDirectoryBestEffort(tempRoot);
        }
    }

    private static async Task<Card> SeedAssignedCardAsync(AppDbContext db,
        AgentControlServiceIntegrationTests.Harness harness, string tempRoot, Guid profileId,
        AgentKind kind, AgentModelLevel level, string? modelId)
    {
        var card = await SeedUnassignedCardAsync(db, harness, tempRoot);
        var agent = await SeedAgentAsync(db, tempRoot, profileId, kind, level, modelId);
        card.AssignedAgentId = agent.Id;
        await db.SaveChangesAsync();
        return card;
    }

    private static async Task<Agent> SeedAgentAsync(AppDbContext db, string tempRoot, Guid profileId,
        AgentKind kind, AgentModelLevel level, string? modelId)
    {
        var now = DateTime.UtcNow;
        var workspace = Path.Combine(tempRoot, $"agent-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        var agent = new Agent
        {
            Id = Guid.NewGuid(), Name = "Card spawn agent", Slug = $"card-spawn-{Guid.NewGuid():N}"[..30],
            WorkingDirectory = workspace, Details = string.Empty, Status = AgentStatus.Idle, Kind = kind,
            ModelLevel = level, ModelId = modelId, TuiProfileId = profileId, CreatedAt = now, UpdatedAt = now
        };
        db.Agents.Add(agent);
        await db.SaveChangesAsync();
        return agent;
    }

    private static async Task<Card> SeedUnassignedCardAsync(AppDbContext db,
        AgentControlServiceIntegrationTests.Harness harness, string tempRoot)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(), Name = $"Card spawn {Guid.NewGuid():N}", GitRepositoryUrl = "https://example.test/repo.git",
            LocalRepositoryPath = Path.Combine(tempRoot, $"repo-{Guid.NewGuid():N}"), BaseBranch = "main",
            CreatedAt = now, UpdatedAt = now
        };
        Directory.CreateDirectory(project.LocalRepositoryPath);
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var board = await harness.BoardService.CreateAsync(new CreateBoardRequest(project.Id, "Card spawn board"), CancellationToken.None);
        var card = await harness.CardService.CreateAsync(board.Id, new CreateCardRequest(null, "Spawn it"), CancellationToken.None);
        return await db.Cards.SingleAsync(c => c.Id == card.Id);
    }

    private static async Task SpawnOnlyCardAsync(AppDbContext db, AgentControlServiceIntegrationTests.Harness harness)
    {
        var card = await db.Cards.Where(c => c.AssignedAgentId != null).OrderByDescending(c => c.CreatedAt).FirstAsync();
        ClearHarnessTracking(harness);
        await harness.CardService.SpawnAsync(card.Id, new SpawnCardRequest(), CancellationToken.None);
        await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
    }

    private static void ClearHarnessTracking(AgentControlServiceIntegrationTests.Harness harness) =>
        harness.Scope.ServiceProvider.GetRequiredService<AppDbContext>().ChangeTracker.Clear();

    private static string[] ModelPair(IReadOnlyList<string> args)
    {
        var index = args.ToList().IndexOf("--model");
        index.ShouldBeGreaterThanOrEqualTo(0);
        return [args[index], args[index + 1]];
    }

    private static IReadOnlyList<string> SessionIndependentArgs(IReadOnlyList<string> args)
    {
        var sessionId = args.ToList().IndexOf("--session-id");
        return sessionId < 0
            ? args
            : args.Take(sessionId).Concat(args.Skip(sessionId + 2)).ToList();
    }

    private static AppDbContext NewDb(string connectionString) => new(TestDbFixture.CreateDbContextOptions(connectionString));

    private static async Task<AgentTuiProfile> SeedProfileAsync(AppDbContext db, AgentKind kind,
        string? modelArgumentName, bool isDefault = false, IReadOnlyList<string>? models = null)
    {
        var now = DateTime.UtcNow;
        var profile = new AgentTuiProfile
        {
            Id = Guid.NewGuid(), DisplayName = $"Profile {Guid.NewGuid():N}", Kind = kind, IsEnabled = true,
            IsDefault = isDefault, Source = AgentTuiProfileSource.Operator, CreatedAt = now, UpdatedAt = now
        };
        db.AgentTuiProfiles.Add(profile);
        await db.SaveChangesAsync();
        var revision = new AgentTuiProfileRevision
        {
            Id = Guid.NewGuid(), ProfileId = profile.Id, RevisionNumber = 1,
            Executable = Path.Combine(Environment.SystemDirectory, "cmd.exe"), ArgumentsJson = "[]",
            DiscoveryArgumentsJson = "[]", VersionArgumentsJson = "[]", AuthenticationMode = AgentTuiAuthenticationMode.WrapperManaged,
            NonSecretEnvironmentJson = "{}", SecretEnvironmentNamesJson = "[]", ModelArgumentName = modelArgumentName,
            Guidance = "CARD-0193", CreatedAt = now
        };
        db.AgentTuiProfileRevisions.Add(revision);
        profile.ActiveRevisionId = revision.Id;
        foreach (var model in models ?? [])
            db.AgentTuiModels.Add(new AgentTuiModel { Id = Guid.NewGuid(), ProfileId = profile.Id, Identifier = model,
                DisplayName = model, Source = AgentTuiModelSource.Operator, Availability = AgentTuiModelAvailability.Verified,
                CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();
        return profile;
    }
}
