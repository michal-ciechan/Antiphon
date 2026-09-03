using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Antiphon.E2E.Fixtures;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.E2E;

/// <summary>
/// The backend↔frontend CONTRACT snapshots. Each test drives the REAL app (shared
/// WebApplicationFactory/Kestrel + Postgres — see <see cref="SharedApp"/>) through a deterministic
/// scenario and snapshots the scrubbed response JSON into
/// <c>client/src/test/fixtures/contract/</c> — the ONLY data Storybook stories may seed their
/// mocks from. A drifted backend response fails here (delete the fixture file to re-capture after
/// verifying the frontend against the new shape), so stories/screenshots can never silently mock
/// a shape the backend no longer produces.
/// </summary>
[NotInParallel]
public class ContractSnapshotTests
{
    [Test]
    public async Task Agent_files_review_threads_and_content_contracts()
    {
        var app = await SharedApp.GetAsync();

        // ---- deterministic scenario: a git workspace with known files + agent activity ----
        var workspace = Path.Combine(Path.GetTempPath(), $"antiphon-contract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        try
        {
            (await GitAsync(workspace, "init")).ShouldBeTrue("git CLI required for the contract scenario");
            await GitAsync(workspace, "config", "user.email", "contract@antiphon.local");
            await GitAsync(workspace, "config", "user.name", "Contract Tests");
            await File.WriteAllTextAsync(
                Path.Combine(workspace, "README.md"),
                "# Contract Workspace\n\nOriginal committed content.\n");
            await GitAsync(workspace, "add", ".");
            await GitAsync(workspace, "commit", "-m", "seed");
            await File.WriteAllTextAsync(
                Path.Combine(workspace, "README.md"),
                "# Contract Workspace\n\nEdited by the agent for the contract scenario.\n");
            Directory.CreateDirectory(Path.Combine(workspace, "notes"));
            await File.WriteAllTextAsync(
                Path.Combine(workspace, "notes", "report.md"),
                "# Report\n\n- finding one\n- finding two\n");

            var create = await app.HttpClient.PostAsJsonAsync("/api/agents", new
            {
                name = $"Contract Agent {Guid.NewGuid():N}"[..24],
                workingDirectory = workspace,
            });
            create.EnsureSuccessStatusCode();
            var agent = JsonNode.Parse(await create.Content.ReadAsStringAsync())!;
            var agentId = agent["id"]!.GetValue<string>();

            // Agent activity + an ANSWERED review thread, seeded at the DB tier (transcript entries
            // and agent comments are produced by live sessions in production).
            var sessionId = Guid.NewGuid();
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var dbAgent = await db.Agents.FirstAsync(a => a.Id == Guid.Parse(agentId));
                dbAgent.PersistentSessionId = sessionId.ToString("D");
                db.AgentSessions.Add(new AgentSession
                {
                    Id = sessionId, CardId = null, DefinitionName = "fake",
                    AgentKind = Server.Domain.Enums.AgentKind.ClaudeCode,
                    Status = Server.Domain.Enums.SessionStatus.Running,
                    Cwd = workspace, Cols = 120, Rows = 30,
                    CreatedAt = DateTime.UtcNow, StartedAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow,
                });
                // A deterministic two-turn conversation with API-call usage + spaced timestamps —
                // the transcript contract carries the token/wall-clock metrics the UI computes
                // (fixed instants; this snapshot is captured with timestamp scrubbing OFF because
                // the offsets ARE the data). Uuids/apiCallIds are non-GUID strings on purpose so
                // the GUID scrubber leaves them alone.
                var t0 = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
                var readmePath = Path.Combine(workspace, "README.md");
                db.TranscriptEntries.AddRange(
                    Entry(sessionId, 1, TranscriptKinds.UserPrompt, t0,
                        e => { e.Role = "user"; e.Text = "Update the README for the new invoice flow."; }),
                    Entry(sessionId, 2, TranscriptKinds.Thinking, t0.AddSeconds(2), e =>
                    {
                        e.Role = "assistant"; e.Text = "The wording needs to mention the invoice flow.";
                        Usage(e, "msg_contract_1", 4, 210, 15000, 2000);
                    }),
                    Entry(sessionId, 3, TranscriptKinds.ToolCall, t0.AddSeconds(3), e =>
                    {
                        e.Role = "assistant"; e.ToolName = "Edit"; e.ToolUseId = "toolu_contract_1";
                        e.ToolInput = JsonSerializer.Serialize(new { file_path = readmePath });
                        Usage(e, "msg_contract_1", 4, 210, 15000, 2000);
                    }),
                    Entry(sessionId, 4, TranscriptKinds.ToolResult, t0.AddSeconds(6),
                        e => { e.Role = "user"; e.ToolUseId = "toolu_contract_1"; e.Text = "OK"; e.ToolIsError = false; }),
                    Entry(sessionId, 5, TranscriptKinds.AssistantText, t0.AddSeconds(8), e =>
                    {
                        e.Role = "assistant"; e.Text = "README updated for the invoice flow.";
                        Usage(e, "msg_contract_2", 6, 180, 17000, 0);
                    }),
                    Entry(sessionId, 6, TranscriptKinds.TurnEnd, t0.AddSeconds(8), e =>
                    {
                        e.Role = "assistant"; e.StopReason = "end_turn";
                        Usage(e, "msg_contract_2", 6, 180, 17000, 0);
                    }),
                    Entry(sessionId, 7, TranscriptKinds.UserPrompt, t0.AddSeconds(90),
                        e => { e.Role = "user"; e.Text = "Great — anything else changed?"; }),
                    Entry(sessionId, 8, TranscriptKinds.AssistantText, t0.AddSeconds(94), e =>
                    {
                        e.Role = "assistant"; e.Text = "No — only the README changed.";
                        Usage(e, "msg_contract_3", 3, 45, 17200, 0);
                    }),
                    Entry(sessionId, 9, TranscriptKinds.TurnEnd, t0.AddSeconds(95), e =>
                    {
                        e.Role = "assistant"; e.StopReason = "end_turn";
                        Usage(e, "msg_contract_3", 3, 45, 17200, 0);
                    }));
                await db.SaveChangesAsync();
            }

            var threadResponse = await app.HttpClient.PostAsJsonAsync($"/api/agents/{agentId}/review/threads", new
            {
                path = "README.md",
                line = 3,
                snippet = "Edited by the agent for the contract scenario.",
                body = "Is this edit intentional?",
                dispatch = false,
            });
            threadResponse.EnsureSuccessStatusCode();
            var threadId = JsonNode.Parse(await threadResponse.Content.ReadAsStringAsync())!["id"]!.GetValue<string>();
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.ReviewComments.Add(new ReviewComment
                {
                    Id = Guid.NewGuid(), ThreadId = Guid.Parse(threadId),
                    Author = ReviewCommentAuthor.Agent,
                    Body = "Yes — intentional; the wording matches the new invoice flow.",
                    CreatedAt = DateTime.UtcNow,
                });
                var thread = await db.ReviewThreads.FirstAsync(t => t.Id == Guid.Parse(threadId));
                thread.Status = ReviewThreadStatus.AwaitingHuman;
                await db.SaveChangesAsync();
            }

            var mark = await app.HttpClient.PostAsJsonAsync($"/api/agents/{agentId}/files/review", new
            {
                paths = new[] { "notes/report.md" },
                prefix = (string?)null,
                level = "viewed",
            });
            mark.EnsureSuccessStatusCode();

            // ---- snapshot the contracts ----
            await SnapshotAsync(app, $"/api/agents/{agentId}/files", "agent-files.json", workspace);
            await SnapshotAsync(app, $"/api/agents/{agentId}/review/threads", "review-threads.json", workspace);
            await SnapshotAsync(
                app, $"/api/agents/{agentId}/files/content?path=README.md&rev=work", "file-content-work.json", workspace);
            await SnapshotAsync(
                app, $"/api/agents/{agentId}/files/content?path=README.md&rev=head", "file-content-head.json", workspace);
            // Timestamps NOT scrubbed here: the seeded instants are already fixed, and the offsets
            // between them are what the transcript UI turns into duration/idle metrics.
            await SnapshotAsync(
                app, $"/api/sessions/{sessionId:D}/transcript", "session-transcript.json", workspace,
                scrubTimestamps: false);
        }
        finally
        {
            try { Directory.Delete(workspace, recursive: true); } catch { /* best effort */ }
        }
    }

    [Test]
    public async Task Delegated_task_board_and_drawer_contracts()
    {
        var app = await SharedApp.GetAsync();

        // A run with the shape the design is FOR — orchestrator → sub-orchestrator → worker —
        // carrying every state the board has to render: all four tiers, all four lanes, an
        // escalation, and each workspace mode. Seeded at the DB tier (like the transcript above)
        // because the interesting part is the projection, not the creation path, which
        // AgentTaskServiceIntegrationTests already covers.
        const string cwd = @"C:\src\antiphon";
        var t0 = new DateTime(2026, 2, 3, 9, 0, 0, DateTimeKind.Utc);
        var root = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        var schema = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
        var suite = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");
        var install = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000004");
        var hang = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000005");

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (await db.AgentTasks.AnyAsync(t => t.RootTaskId == root))
            {
                db.AgentTaskEvents.RemoveRange(db.AgentTaskEvents.Where(e => db.AgentTasks
                    .Where(t => t.RootTaskId == root).Select(t => t.Id).Contains(e.AgentTaskId)));
                db.AgentTasks.RemoveRange(db.AgentTasks.Where(t => t.RootTaskId == root));
                await db.SaveChangesAsync();
            }

            // Fixed agent ids as well as fixed task ids: the snapshot carries agentId, so a random
            // one would make this test fail on its own second run instead of on a real drift.
            var agents = new Dictionary<string, Guid>
            {
                ["task-upgrade"] = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"),
                ["task-schema"] = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002"),
                ["task-suite"] = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000003"),
                ["task-install"] = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000004"),
                ["task-hang"] = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000005"),
            };
            var levels = new Dictionary<string, Server.Domain.Enums.AgentModelLevel>
            {
                ["task-upgrade"] = Server.Domain.Enums.AgentModelLevel.Frontier,
                ["task-schema"] = Server.Domain.Enums.AgentModelLevel.Frontier,
                ["task-suite"] = Server.Domain.Enums.AgentModelLevel.Low,
                ["task-install"] = Server.Domain.Enums.AgentModelLevel.Medium,
                ["task-hang"] = Server.Domain.Enums.AgentModelLevel.Frontier,
            };
            var existing = await db.Agents
                .Where(a => agents.Values.Contains(a.Id)).Select(a => a.Id).ToListAsync();
            foreach (var (name, id) in agents.Where(a => !existing.Contains(a.Value)))
                db.Agents.Add(DelegateAgent(id, name, cwd, levels[name], t0));
            await db.SaveChangesAsync();

            db.AgentTasks.AddRange(
                Task(root, root, null, 0, "Ship the Postgres 18 upgrade", cwd, t0, agents["task-upgrade"], "task-upgrade", t =>
                {
                    t.Kind = Server.Domain.Enums.AgentTaskKind.Orchestrator;
                    t.Role = Server.Domain.Enums.AgentTaskRole.Plan;
                    t.ModelLevel = Server.Domain.Enums.AgentModelLevel.Frontier;
                    t.Status = Server.Domain.Enums.AgentTaskStatus.Working;
                    t.DispatchedAt = t0;
                    t.TokensIn = 84_000; t.CacheReadTokens = 2_400_000; t.CacheCreationTokens = 41_000;
                    t.TokensOut = 3_100; t.CostUsd = 0.412m; t.CostPricingVersion = Server.Application.Services.DelegationCost.PricingVersion;
                }),
                Task(schema, root, root, 1, "Migrate the schema and connection strings", cwd, t0.AddMinutes(2),
                    agents["task-schema"], "task-schema", t =>
                {
                    t.Kind = Server.Domain.Enums.AgentTaskKind.Orchestrator;
                    t.Role = Server.Domain.Enums.AgentTaskRole.Code;
                    t.ModelLevel = Server.Domain.Enums.AgentModelLevel.Frontier;
                    t.Status = Server.Domain.Enums.AgentTaskStatus.Working;
                    t.Workspace = Server.Domain.Enums.WorkspaceMode.Worktree;
                    t.MergeTargetRef = "feat/pg18";
                    t.DispatchedAt = t0.AddMinutes(2);
                    t.TokensIn = 61_500; t.CacheReadTokens = 1_850_000; t.CacheCreationTokens = 32_000;
                    t.TokensOut = 4_800; t.CostUsd = 0.318m; t.CostPricingVersion = Server.Application.Services.DelegationCost.PricingVersion;
                }),
                Task(suite, root, schema, 2, "Run the integration suite and report failures", cwd, t0.AddMinutes(9),
                    agents["task-suite"], "task-suite", t =>
                {
                    t.Role = Server.Domain.Enums.AgentTaskRole.Test;
                    t.ModelLevel = Server.Domain.Enums.AgentModelLevel.Low;
                    t.Status = Server.Domain.Enums.AgentTaskStatus.Succeeded;
                    t.Scope = "tests/**";
                    t.DispatchedAt = t0.AddMinutes(9);
                    t.CompletedAt = t0.AddMinutes(13).AddSeconds(24);
                    t.Result = "3 failures, all in Antiphon.Tests.Application.CardServiceTests — "
                        + "each is a missing checkpoint dependency, not a schema problem. Rerun with "
                        + "dotnet run --project tests/Antiphon.Tests.";
                    t.TokensIn = 22_000; t.CacheReadTokens = 430_000; t.CacheCreationTokens = 9_000;
                    t.TokensOut = 900; t.CostUsd = 0.019m; t.CostPricingVersion = Server.Application.Services.DelegationCost.PricingVersion;
                }),
                Task(install, root, root, 1, "Rewrite the Windows install section", cwd, t0.AddMinutes(3),
                    agents["task-install"], "task-install", t =>
                {
                    t.Role = Server.Domain.Enums.AgentTaskRole.Docs;
                    t.ModelLevel = Server.Domain.Enums.AgentModelLevel.Medium;
                    t.Status = Server.Domain.Enums.AgentTaskStatus.Blocked;
                    t.Workspace = Server.Domain.Enums.WorkspaceMode.ReadOnly;
                    t.Scope = "docs/setup.md";
                    t.DispatchedAt = t0.AddMinutes(3);
                    t.Result = "Rewrote \"## Windows install\" in docs/setup.md — 34 lines changed, "
                        + "every command now pwsh 7.\n\nOne decision is yours: should the old cmd "
                        + "examples be deleted, or kept alongside?";
                    t.TokensIn = 18_400; t.CacheReadTokens = 520_000; t.CacheCreationTokens = 11_000;
                    t.TokensOut = 1_250; t.CostUsd = 0.031m; t.CostPricingVersion = Server.Application.Services.DelegationCost.PricingVersion;
                }),
                Task(hang, root, root, 1, "Find out why the suite hangs on CI", cwd, t0.AddMinutes(4),
                    agents["task-hang"], "task-hang", t =>
                {
                    t.Role = Server.Domain.Enums.AgentTaskRole.Debug;
                    // The escalation ladder, visible on the chip: started at opus, now on fable.
                    t.ModelLevel = Server.Domain.Enums.AgentModelLevel.Frontier;
                    t.EscalatedFrom = Server.Domain.Enums.AgentModelLevel.High;
                    t.Attempt = 2;
                    t.Status = Server.Domain.Enums.AgentTaskStatus.Queued;
                    t.Result = "Could not reproduce in 25 minutes; the hang only appears under load.";
                    t.TokensIn = 40_100; t.CacheReadTokens = 1_150_000; t.CacheCreationTokens = 21_000;
                    t.TokensOut = 2_600; t.CostUsd = 0.204m; t.CostPricingVersion = Server.Application.Services.DelegationCost.PricingVersion;
                }));

            db.AgentTaskEvents.AddRange(
                Event(install, Server.Domain.Enums.AgentTaskEventType.Created,
                    "Worker/Docs at Medium (role policy) in " + cwd, t0.AddMinutes(3),
                    Server.Domain.Enums.AgentModelLevel.Medium),
                Event(install, Server.Domain.Enums.AgentTaskEventType.Dispatched,
                    "Dispatched to agent 'task-install' (sonnet) in " + cwd, t0.AddMinutes(3).AddSeconds(4),
                    Server.Domain.Enums.AgentModelLevel.Medium),
                Event(install, Server.Domain.Enums.AgentTaskEventType.Blocked,
                    "Delegate asked a question.", t0.AddMinutes(7).AddSeconds(11), null));
            await db.SaveChangesAsync();
        }

        // Timestamps NOT scrubbed: the chips show elapsed and the drawer shows a timeline, so the
        // OFFSETS between these instants are the data. Ids are fixed, so there is nothing to scrub.
        await SnapshotAsync(app, $"/api/agent-tasks?rootId={root:D}", "agent-tasks.json",
            workspace: null, scrubTimestamps: false, scrubGuids: false);
        await SnapshotAsync(app, $"/api/agent-tasks/{install:D}", "agent-task-detail.json",
            workspace: null, scrubTimestamps: false, scrubGuids: false);
    }

    /// <summary>
    /// CARD-0002 S2. Own test so a pre-existing <c>agent-tasks.json</c> drift (extra summary
    /// fields the fixture was never recaptured against) cannot block capturing the home-rail
    /// contract. Seeds the same unbound-task run plus a Review card, a NeedsDecision card,
    /// and a bound Working task.
    /// </summary>
    [Test]
    public async Task Home_tasks_rail_contract()
    {
        var app = await SharedApp.GetAsync();
        const string cwd = @"C:\src\antiphon";
        var t0 = new DateTime(2026, 2, 3, 9, 0, 0, DateTimeKind.Utc);
        var root = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        var schema = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
        var suite = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");
        var install = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000004");
        var hang = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000005");

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (await db.AgentTasks.AnyAsync(t => t.RootTaskId == root))
            {
                db.AgentTaskEvents.RemoveRange(db.AgentTaskEvents.Where(e => db.AgentTasks
                    .Where(t => t.RootTaskId == root).Select(t => t.Id).Contains(e.AgentTaskId)));
                db.AgentTasks.RemoveRange(db.AgentTasks.Where(t => t.RootTaskId == root));
                await db.SaveChangesAsync();
            }

            var agents = new Dictionary<string, Guid>
            {
                ["task-upgrade"] = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"),
                ["task-schema"] = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002"),
                ["task-suite"] = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000003"),
                ["task-install"] = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000004"),
                ["task-hang"] = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000005"),
            };
            var levels = new Dictionary<string, Server.Domain.Enums.AgentModelLevel>
            {
                ["task-upgrade"] = Server.Domain.Enums.AgentModelLevel.Frontier,
                ["task-schema"] = Server.Domain.Enums.AgentModelLevel.Frontier,
                ["task-suite"] = Server.Domain.Enums.AgentModelLevel.Low,
                ["task-install"] = Server.Domain.Enums.AgentModelLevel.Medium,
                ["task-hang"] = Server.Domain.Enums.AgentModelLevel.Frontier,
            };
            var existing = await db.Agents
                .Where(a => agents.Values.Contains(a.Id)).Select(a => a.Id).ToListAsync();
            foreach (var (name, id) in agents.Where(a => !existing.Contains(a.Value)))
                db.Agents.Add(DelegateAgent(id, name, cwd, levels[name], t0));
            await db.SaveChangesAsync();

            db.AgentTasks.AddRange(
                Task(root, root, null, 0, "Ship the Postgres 18 upgrade", cwd, t0, agents["task-upgrade"], "task-upgrade", t =>
                {
                    t.Kind = Server.Domain.Enums.AgentTaskKind.Orchestrator;
                    t.Role = Server.Domain.Enums.AgentTaskRole.Plan;
                    t.ModelLevel = Server.Domain.Enums.AgentModelLevel.Frontier;
                    t.Status = Server.Domain.Enums.AgentTaskStatus.Working;
                    t.DispatchedAt = t0;
                    t.TokensIn = 84_000; t.CacheReadTokens = 2_400_000; t.CacheCreationTokens = 41_000;
                    t.TokensOut = 3_100; t.CostUsd = 0.412m; t.CostPricingVersion = Server.Application.Services.DelegationCost.PricingVersion;
                }),
                Task(schema, root, root, 1, "Migrate the schema and connection strings", cwd, t0.AddMinutes(2),
                    agents["task-schema"], "task-schema", t =>
                {
                    t.Kind = Server.Domain.Enums.AgentTaskKind.Orchestrator;
                    t.Role = Server.Domain.Enums.AgentTaskRole.Code;
                    t.ModelLevel = Server.Domain.Enums.AgentModelLevel.Frontier;
                    t.Status = Server.Domain.Enums.AgentTaskStatus.Working;
                    t.Workspace = Server.Domain.Enums.WorkspaceMode.Worktree;
                    t.MergeTargetRef = "feat/pg18";
                    t.DispatchedAt = t0.AddMinutes(2);
                    t.TokensIn = 61_500; t.CacheReadTokens = 1_850_000; t.CacheCreationTokens = 32_000;
                    t.TokensOut = 4_800; t.CostUsd = 0.318m; t.CostPricingVersion = Server.Application.Services.DelegationCost.PricingVersion;
                }),
                Task(suite, root, schema, 2, "Run the integration suite and report failures", cwd, t0.AddMinutes(9),
                    agents["task-suite"], "task-suite", t =>
                {
                    t.Role = Server.Domain.Enums.AgentTaskRole.Test;
                    t.ModelLevel = Server.Domain.Enums.AgentModelLevel.Low;
                    t.Status = Server.Domain.Enums.AgentTaskStatus.Succeeded;
                    t.Scope = "tests/**";
                    t.DispatchedAt = t0.AddMinutes(9);
                    t.CompletedAt = t0.AddMinutes(13).AddSeconds(24);
                    t.TokensIn = 22_000; t.CacheReadTokens = 430_000; t.CacheCreationTokens = 9_000;
                    t.TokensOut = 900; t.CostUsd = 0.019m; t.CostPricingVersion = Server.Application.Services.DelegationCost.PricingVersion;
                }),
                Task(install, root, root, 1, "Rewrite the Windows install section", cwd, t0.AddMinutes(3),
                    agents["task-install"], "task-install", t =>
                {
                    t.Role = Server.Domain.Enums.AgentTaskRole.Docs;
                    t.ModelLevel = Server.Domain.Enums.AgentModelLevel.Medium;
                    t.Status = Server.Domain.Enums.AgentTaskStatus.Blocked;
                    t.Workspace = Server.Domain.Enums.WorkspaceMode.ReadOnly;
                    t.Scope = "docs/setup.md";
                    t.DispatchedAt = t0.AddMinutes(3);
                    t.TokensIn = 18_400; t.CacheReadTokens = 520_000; t.CacheCreationTokens = 11_000;
                    t.TokensOut = 1_250; t.CostUsd = 0.031m; t.CostPricingVersion = Server.Application.Services.DelegationCost.PricingVersion;
                }),
                Task(hang, root, root, 1, "Find out why the suite hangs on CI", cwd, t0.AddMinutes(4),
                    agents["task-hang"], "task-hang", t =>
                {
                    t.Role = Server.Domain.Enums.AgentTaskRole.Debug;
                    t.ModelLevel = Server.Domain.Enums.AgentModelLevel.Frontier;
                    t.EscalatedFrom = Server.Domain.Enums.AgentModelLevel.High;
                    t.Attempt = 2;
                    t.Status = Server.Domain.Enums.AgentTaskStatus.Queued;
                    t.TokensIn = 40_100; t.CacheReadTokens = 1_150_000; t.CacheCreationTokens = 21_000;
                    t.TokensOut = 2_600; t.CostUsd = 0.204m; t.CostPricingVersion = Server.Application.Services.DelegationCost.PricingVersion;
                }));
            await db.SaveChangesAsync();
            await SeedHomeTasksScenarioAsync(db, cwd, t0);
        }

        var homeIds = new HashSet<Guid>
        {
            root, schema, suite, install, hang,
            HomeReviewCardId, HomeDecisionCardId, HomeRunningCardId,
        };
        await SnapshotHomeTasksAsync(app, homeIds);
    }

    /// <summary>
    /// CARD-0031 S3. Own scenario so a home-rail recapture cannot pick up a bound Plan worker.
    /// Seeds one in-flight Shared writer, one queued task behind that lease, and one ready card.
    /// </summary>
    [Test]
    public async Task Pipeline_status_contract()
    {
        var app = await SharedApp.GetAsync();
        const string cwd = @"C:\src\antiphon-pipeline";
        var t0 = new DateTime(2026, 2, 3, 9, 0, 0, DateTimeKind.Utc);

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SeedPipelineScenarioAsync(db, cwd, t0);
        }

        await SnapshotPipelineAsync(
            app,
            new HashSet<Guid> { PipelineHolderTaskId, PipelineQueuedTaskId, PipelinePlanTaskId, PipelineBlockedTaskId },
            new HashSet<Guid> { PipelineReadyCardId });
    }

    // ---- scenario helpers ----

    private static readonly Guid HomeProjectId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid HomeBoardId = Guid.Parse("cccccccc-0000-0000-0000-000000000002");
    private static readonly Guid HomeReviewCardId = Guid.Parse("cccccccc-0000-0000-0000-000000000003");
    private static readonly Guid HomeDecisionCardId = Guid.Parse("cccccccc-0000-0000-0000-000000000004");
    private static readonly Guid HomeRunningCardId = Guid.Parse("cccccccc-0000-0000-0000-000000000005");
    private static readonly Guid HomeBoundTaskId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000006");
    private static readonly Guid HomeBoundAgentId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000006");
    private static readonly Guid PipelineHolderTaskId = Guid.Parse("dddddddd-0000-0000-0000-000000000001");
    private static readonly Guid PipelineQueuedTaskId = Guid.Parse("dddddddd-0000-0000-0000-000000000002");
    private static readonly Guid PipelinePlanTaskId = Guid.Parse("dddddddd-0000-0000-0000-000000000003");
    private static readonly Guid PipelineBlockedTaskId = Guid.Parse("dddddddd-0000-0000-0000-000000000004");
    private static readonly Guid PipelineHolderAgentId = Guid.Parse("dddddddd-0000-0000-0000-000000000011");
    private static readonly Guid PipelineQueuedAgentId = Guid.Parse("dddddddd-0000-0000-0000-000000000012");
    private static readonly Guid PipelineProjectId = Guid.Parse("dddddddd-0000-0000-0000-000000000021");
    private static readonly Guid PipelineBoardId = Guid.Parse("dddddddd-0000-0000-0000-000000000022");
    private static readonly Guid PipelineReadyCardId = Guid.Parse("dddddddd-0000-0000-0000-000000000023");

    private static async Task SeedHomeTasksScenarioAsync(AppDbContext db, string cwd, DateTime t0)
    {
        db.AgentTasks.RemoveRange(db.AgentTasks.Where(t => t.Id == HomeBoundTaskId));
        db.Cards.RemoveRange(db.Cards.Where(c =>
            c.Id == HomeReviewCardId || c.Id == HomeDecisionCardId || c.Id == HomeRunningCardId));
        await db.SaveChangesAsync();

        if (!await db.Agents.AnyAsync(a => a.Id == HomeBoundAgentId))
            db.Agents.Add(DelegateAgent(HomeBoundAgentId, "task-bound", cwd, Server.Domain.Enums.AgentModelLevel.High, t0));

        if (!await db.Projects.AnyAsync(p => p.Id == HomeProjectId))
        {
            db.Projects.Add(new Project
            {
                Id = HomeProjectId,
                Name = "Home tasks contract",
                GitRepositoryUrl = "https://example.test/antiphon.git",
                LocalRepositoryPath = cwd,
                BaseBranch = "master",
                CreatedAt = t0,
                UpdatedAt = t0,
            });
        }

        if (!await db.Boards.AnyAsync(b => b.Id == HomeBoardId))
        {
            db.Boards.Add(new Board
            {
                Id = HomeBoardId,
                ProjectId = HomeProjectId,
                Name = "Home",
                CreatedAt = t0,
                UpdatedAt = t0,
            });
            var columns = new (Guid Id, string Key, string Name, Server.Domain.Enums.CardStatus Status, bool Active, bool Terminal)[]
            {
                (Guid.Parse("cccccccc-0000-0000-0000-000000000011"), "backlog", "Backlog", Server.Domain.Enums.CardStatus.Backlog, false, false),
                (Guid.Parse("cccccccc-0000-0000-0000-000000000012"), "in-progress", "In Progress", Server.Domain.Enums.CardStatus.InProgress, true, false),
                (Guid.Parse("cccccccc-0000-0000-0000-000000000013"), "review", "Review", Server.Domain.Enums.CardStatus.Review, false, false),
                (Guid.Parse("cccccccc-0000-0000-0000-000000000014"), "needs-decision", "Needs decision", Server.Domain.Enums.CardStatus.NeedsDecision, false, false),
                (Guid.Parse("cccccccc-0000-0000-0000-000000000015"), "done", "Done", Server.Domain.Enums.CardStatus.Done, false, true),
                (Guid.Parse("cccccccc-0000-0000-0000-000000000016"), "canceled", "Canceled", Server.Domain.Enums.CardStatus.Canceled, false, true),
            };
            for (var i = 0; i < columns.Length; i++)
            {
                var col = columns[i];
                db.BoardColumns.Add(new BoardColumn
                {
                    Id = col.Id,
                    BoardId = HomeBoardId,
                    StateKey = col.Key,
                    Name = col.Name,
                    ColumnOrder = i,
                    CardStatus = col.Status,
                    IsActive = col.Active,
                    IsTerminal = col.Terminal,
                    CreatedAt = t0,
                    UpdatedAt = t0,
                });
            }

            await db.SaveChangesAsync();
        }

        var columnByStatus = await db.BoardColumns
            .Where(c => c.BoardId == HomeBoardId)
            .ToDictionaryAsync(c => c.CardStatus);

        db.Cards.AddRange(
            HomeCard(HomeReviewCardId, "CARD-0002", "Tasks section on the home rail",
                Server.Domain.Enums.CardStatus.Review, columnByStatus, t0.AddMinutes(-40)),
            HomeCard(HomeDecisionCardId, "CARD-0003", "Should validation errors block save?",
                Server.Domain.Enums.CardStatus.NeedsDecision, columnByStatus, t0.AddMinutes(-20)),
            HomeCard(HomeRunningCardId, "CARD-0004", "Ship the Postgres 18 upgrade",
                Server.Domain.Enums.CardStatus.InProgress, columnByStatus, t0, startedAt: t0));
        await db.SaveChangesAsync();

        db.AgentTasks.Add(Task(
            HomeBoundTaskId, HomeBoundTaskId, null, 0,
            "CARD-0004 bound code pass", cwd, t0.AddMinutes(1),
            HomeBoundAgentId, "task-bound", t =>
            {
                t.CardId = HomeRunningCardId;
                t.Role = Server.Domain.Enums.AgentTaskRole.Code;
                t.ModelLevel = Server.Domain.Enums.AgentModelLevel.High;
                t.Status = Server.Domain.Enums.AgentTaskStatus.Working;
                t.DispatchedAt = t0.AddMinutes(1);
                t.CostUsd = 0.11m;
                t.CostPricingVersion = Server.Application.Services.DelegationCost.PricingVersion;
            }));
        await db.SaveChangesAsync();
    }

    private static async Task SeedPipelineScenarioAsync(AppDbContext db, string cwd, DateTime t0)
    {
        db.AgentTaskEvents.RemoveRange(db.AgentTaskEvents.Where(e =>
            e.AgentTaskId == PipelineHolderTaskId
            || e.AgentTaskId == PipelineQueuedTaskId
            || e.AgentTaskId == PipelinePlanTaskId
            || e.AgentTaskId == PipelineBlockedTaskId));
        db.AgentTasks.RemoveRange(db.AgentTasks.Where(t =>
            t.Id == PipelineHolderTaskId
            || t.Id == PipelineQueuedTaskId
            || t.Id == PipelinePlanTaskId
            || t.Id == PipelineBlockedTaskId));
        db.Cards.RemoveRange(db.Cards.Where(c => c.Id == PipelineReadyCardId));
        await db.SaveChangesAsync();

        if (!await db.Agents.AnyAsync(a => a.Id == PipelineHolderAgentId))
            db.Agents.Add(DelegateAgent(PipelineHolderAgentId, "pipe-holder", cwd, Server.Domain.Enums.AgentModelLevel.Medium, t0));
        if (!await db.Agents.AnyAsync(a => a.Id == PipelineQueuedAgentId))
            db.Agents.Add(DelegateAgent(PipelineQueuedAgentId, "pipe-queued", cwd, Server.Domain.Enums.AgentModelLevel.Medium, t0));

        if (!await db.Projects.AnyAsync(p => p.Id == PipelineProjectId))
        {
            db.Projects.Add(new Project
            {
                Id = PipelineProjectId,
                Name = "Pipeline contract",
                GitRepositoryUrl = "https://example.test/pipeline.git",
                LocalRepositoryPath = cwd,
                BaseBranch = "master",
                CreatedAt = t0,
                UpdatedAt = t0,
            });
        }

        if (!await db.Boards.AnyAsync(b => b.Id == PipelineBoardId))
        {
            db.Boards.Add(new Board
            {
                Id = PipelineBoardId,
                ProjectId = PipelineProjectId,
                Name = "Pipeline",
                CreatedAt = t0,
                UpdatedAt = t0,
            });
            db.BoardColumns.Add(new BoardColumn
            {
                Id = Guid.Parse("dddddddd-0000-0000-0000-000000000031"),
                BoardId = PipelineBoardId,
                StateKey = "review",
                Name = "Review",
                ColumnOrder = 0,
                CardStatus = Server.Domain.Enums.CardStatus.Review,
                CreatedAt = t0,
                UpdatedAt = t0,
            });
            await db.SaveChangesAsync();
        }

        var reviewColumnId = await db.BoardColumns
            .Where(c => c.BoardId == PipelineBoardId && c.CardStatus == Server.Domain.Enums.CardStatus.Review)
            .Select(c => c.Id)
            .SingleAsync();

        db.Cards.Add(new Card
        {
            Id = PipelineReadyCardId,
            BoardId = PipelineBoardId,
            BoardColumnId = reviewColumnId,
            Identifier = "CARD-0031",
            Title = "Project status view",
            Status = Server.Domain.Enums.CardStatus.Review,
            Importance = CardImportance.High,
            CreatedAt = t0.AddDays(-3),
            UpdatedAt = t0.AddDays(-3),
        });
        await db.SaveChangesAsync();

        db.AgentTasks.AddRange(
            Task(PipelineHolderTaskId, PipelineHolderTaskId, null, 0,
                "in-flight docs pass", cwd, t0.AddMinutes(-20),
                PipelineHolderAgentId, "pipe-holder", t =>
                {
                    t.Role = Server.Domain.Enums.AgentTaskRole.Docs;
                    t.ModelLevel = Server.Domain.Enums.AgentModelLevel.Medium;
                    t.Status = Server.Domain.Enums.AgentTaskStatus.Working;
                    t.Workspace = Server.Domain.Enums.WorkspaceMode.Shared;
                    t.DispatchedAt = t0.AddMinutes(-20);
                    t.CostPricingVersion = Server.Application.Services.DelegationCost.PricingVersion;
                }),
            Task(PipelineQueuedTaskId, PipelineQueuedTaskId, null, 0,
                "queued behind the checkout", cwd, t0.AddMinutes(-10),
                PipelineQueuedAgentId, "pipe-queued", t =>
                {
                    t.Role = Server.Domain.Enums.AgentTaskRole.Docs;
                    t.ModelLevel = Server.Domain.Enums.AgentModelLevel.Medium;
                    t.Status = Server.Domain.Enums.AgentTaskStatus.Queued;
                    t.Workspace = Server.Domain.Enums.WorkspaceMode.Shared;
                    t.CostPricingVersion = Server.Application.Services.DelegationCost.PricingVersion;
                }),
            Task(PipelinePlanTaskId, PipelinePlanTaskId, null, 0,
                "CARD-0031 plan", cwd, t0.AddDays(-3),
                PipelineHolderAgentId, "pipe-holder", t =>
                {
                    t.CardId = PipelineReadyCardId;
                    t.Role = Server.Domain.Enums.AgentTaskRole.Plan;
                    t.ModelLevel = Server.Domain.Enums.AgentModelLevel.Frontier;
                    t.Status = Server.Domain.Enums.AgentTaskStatus.Succeeded;
                    t.DispatchedAt = t0.AddDays(-3);
                    t.CompletedAt = t0.AddDays(-3).AddHours(2);
                    t.DeliverablePath = "docs/superpowers/plans/2026-09-02-card-0031-project-status-view-plan.md";
                    t.CostPricingVersion = Server.Application.Services.DelegationCost.PricingVersion;
                }),
            Task(PipelineBlockedTaskId, PipelineBlockedTaskId, null, 0,
                "blocked deploy", cwd, t0.AddMinutes(-5),
                PipelineQueuedAgentId, "pipe-queued", t =>
                {
                    t.Role = Server.Domain.Enums.AgentTaskRole.Deploy;
                    t.ModelLevel = Server.Domain.Enums.AgentModelLevel.Medium;
                    t.Status = Server.Domain.Enums.AgentTaskStatus.Blocked;
                    t.Workspace = Server.Domain.Enums.WorkspaceMode.Shared;
                    t.CostPricingVersion = Server.Application.Services.DelegationCost.PricingVersion;
                }));
        await db.SaveChangesAsync();
    }

    private static Card HomeCard(
        Guid id, string identifier, string title, Server.Domain.Enums.CardStatus status,
        IReadOnlyDictionary<Server.Domain.Enums.CardStatus, BoardColumn> columns, DateTime at,
        DateTime? startedAt = null) =>
        new()
        {
            Id = id,
            BoardId = HomeBoardId,
            BoardColumnId = columns[status].Id,
            Identifier = identifier,
            Title = title,
            Status = status,
            Importance = CardImportance.High,
            CreatedAt = at,
            UpdatedAt = at,
            StartedAt = startedAt,
        };

    private static Agent DelegateAgent(
        Guid id, string name, string cwd, Server.Domain.Enums.AgentModelLevel level, DateTime at) =>
        new()
        {
            Id = id,
            Name = name,
            Slug = name,
            WorkingDirectory = cwd,
            Details = "Ephemeral delegate.",
            Status = Server.Domain.Enums.AgentStatus.Running,
            ModelLevel = level,
            AlwaysOn = false,
            RemoteControlEnabled = false,
            CreatedAt = at,
            UpdatedAt = at,
        };

    private static AgentTask Task(
        Guid id, Guid rootId, Guid? parentId, int depth, string title, string cwd, DateTime createdAt,
        Guid agentId, string agentName, Action<AgentTask> configure)
    {
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = rootId,
            ParentTaskId = parentId,
            Depth = depth,
            Title = title,
            Goal = title + ".",
            WorkingDirectory = cwd,
            RepoPath = cwd,
            AgentId = agentId,
            // Snapshotted at dispatch in production; the projection reads THIS, not a join — the
            // ephemeral agent row is deleted when a task settles.
            AgentName = agentName,
            Ephemeral = true,
            CreatedAt = createdAt,
            MaxAttempts = 2,
        };
        configure(task);
        return task;
    }

    private static AgentTaskEvent Event(
        Guid taskId, Server.Domain.Enums.AgentTaskEventType type, string detail, DateTime at,
        Server.Domain.Enums.AgentModelLevel? level) =>
        new() { Id = Guid.NewGuid(), AgentTaskId = taskId, Type = type, Detail = detail, At = at, ModelLevel = level };

    private static TranscriptEntry Entry(
        Guid sessionId, long sequence, string kind, DateTime timestamp, Action<TranscriptEntry> configure)
    {
        var entry = new TranscriptEntry
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Sequence = sequence,
            Kind = kind,
            Uuid = $"line-{sequence:D2}",
            Timestamp = timestamp,
            CreatedAt = timestamp,
        };
        configure(entry);
        return entry;
    }

    private static void Usage(TranscriptEntry e, string apiCallId, int input, int output, int cacheRead, int cacheCreate)
    {
        e.ApiCallId = apiCallId;
        e.InputTokens = input;
        e.OutputTokens = output;
        e.CacheReadTokens = cacheRead;
        e.CacheCreationTokens = cacheCreate;
    }

    // ---- snapshot machinery ----

    /// <summary>
    /// Fleet-global <c>GET /api/home/tasks</c> is otherwise polluted by whatever else SharedApp
    /// has written. Keep the scenario's own ids so the fixture is what Storybook seeds, and
    /// stamp a fixed generatedAt — that clock is not part of the contract.
    /// </summary>
    private static async Task SnapshotHomeTasksAsync(AntiphonAppFixture app, IReadOnlySet<Guid> keepIds)
    {
        var response = await app.HttpClient.GetAsync("/api/home/tasks");
        response.EnsureSuccessStatusCode();
        var node = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        node["generatedAt"] = "2026-02-03T09:00:00Z";
        var items = node["items"]!.AsArray();
        var kept = items
            .Where(item => keepIds.Contains(Guid.Parse(item!["id"]!.GetValue<string>())))
            .ToList();
        items.Clear();
        foreach (var item in kept)
            items.Add(item);

        var fixtureName = "home-tasks.json";
        var pretty = PrettyPrint(node.ToJsonString());
        var fixturePath = Path.Combine(FixturesDir(), fixtureName);
        if (!File.Exists(fixturePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fixturePath)!);
            await File.WriteAllTextAsync(fixturePath, pretty);
            Console.WriteLine($"CAPTURED contract fixture {fixtureName}");
            return;
        }

        var existing = await File.ReadAllTextAsync(fixturePath);
        Normalize(existing).ShouldBe(
            Normalize(pretty),
            $"Backend contract for /api/home/tasks drifted from {fixtureName}. If the change is intentional, "
            + "verify the frontend stories against the new shape, delete the fixture, and re-run to re-capture.");
    }

    /// <summary>
    /// CARD-0031 S3. Fleet-global <c>GET /api/agent-tasks/pipeline</c> is otherwise polluted by
    /// whatever else SharedApp has written. Keep the scenario's own task/card ids, stamp a fixed
    /// asOf, and rewrite the cap counters from the kept in-flight rows so the fixture is the
    /// contract Storybook seeds rather than the live fleet.
    /// </summary>
    private static async Task SnapshotPipelineAsync(
        AntiphonAppFixture app, IReadOnlySet<Guid> keepTaskIds, IReadOnlySet<Guid> keepCardIds)
    {
        var response = await app.HttpClient.GetAsync("/api/agent-tasks/pipeline");
        response.EnsureSuccessStatusCode();
        var node = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        node["asOf"] = "2026-02-03T09:00:00Z";

        var keptInFlight = 0;
        foreach (var stage in node["stages"]!.AsArray())
        {
            keptInFlight += FilterPipelineRows(stage!["inFlight"]!.AsArray(), keepTaskIds, idKey: "taskId");
            FilterPipelineRows(stage["queued"]!.AsArray(), keepTaskIds, idKey: "taskId");
            FilterPipelineRows(stage["blocked"]!.AsArray(), keepTaskIds, idKey: "taskId");
            FilterReadyRows(stage["ready"]!.AsArray(), keepCardIds, keepTaskIds);
            var remaining = stage["inFlight"]!.AsArray().Count;
            stage["inFlightCount"] = remaining;
            var recommended = stage["recommendedInFlight"];
            stage["atOrAboveRecommendation"] =
                recommended is JsonValue rec
                && rec.TryGetValue<int>(out var limit)
                && remaining >= limit;
        }

        node["inFlightAgainstCap"] = keptInFlight;

        var fixtureName = "pipeline.json";
        var pretty = PrettyPrint(node.ToJsonString());
        var fixturePath = Path.Combine(FixturesDir(), fixtureName);
        if (!File.Exists(fixturePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fixturePath)!);
            await File.WriteAllTextAsync(fixturePath, pretty);
            Console.WriteLine($"CAPTURED contract fixture {fixtureName}");
            return;
        }

        var existing = await File.ReadAllTextAsync(fixturePath);
        Normalize(existing).ShouldBe(
            Normalize(pretty),
            $"Backend contract for /api/agent-tasks/pipeline drifted from {fixtureName}. If the change is intentional, "
            + "verify the frontend stories against the new shape, delete the fixture, and re-run to re-capture.");
    }

    private static int FilterPipelineRows(JsonArray rows, IReadOnlySet<Guid> keepIds, string idKey)
    {
        var kept = rows
            .Where(row => keepIds.Contains(Guid.Parse(row![idKey]!.GetValue<string>())))
            .ToList();
        rows.Clear();
        foreach (var row in kept)
            rows.Add(row);
        return kept.Count;
    }

    private static void FilterReadyRows(
        JsonArray rows, IReadOnlySet<Guid> keepCardIds, IReadOnlySet<Guid> keepTaskIds)
    {
        var kept = rows
            .Where(row =>
            {
                var cardId = Guid.Parse(row!["card"]!["id"]!.GetValue<string>());
                var planId = Guid.Parse(row["sourcePlanTaskId"]!.GetValue<string>());
                return keepCardIds.Contains(cardId) || keepTaskIds.Contains(planId);
            })
            .ToList();
        rows.Clear();
        foreach (var row in kept)
            rows.Add(row);
    }

    private static async Task SnapshotAsync(
        AntiphonAppFixture app, string url, string fixtureName, string? workspace,
        bool scrubTimestamps = true, bool scrubGuids = true)
    {
        var response = await app.HttpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var raw = await response.Content.ReadAsStringAsync();
        var scrubbed = Scrub(raw, workspace, scrubTimestamps, scrubGuids);

        var fixturePath = Path.Combine(FixturesDir(), fixtureName);
        if (!File.Exists(fixturePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fixturePath)!);
            await File.WriteAllTextAsync(fixturePath, scrubbed);
            Console.WriteLine($"CAPTURED contract fixture {fixtureName}");
            return;
        }

        var existing = await File.ReadAllTextAsync(fixturePath);
        Normalize(existing).ShouldBe(
            Normalize(scrubbed),
            $"Backend contract for {url} drifted from {fixtureName}. If the change is intentional, "
            + "verify the frontend stories against the new shape, delete the fixture, and re-run to re-capture.");
    }

    /// <summary>
    /// Strip run-specific values so snapshots are stable: GUIDs → sequential placeholders (order of
    /// first appearance), timestamps → a fixed instant, the temp workspace root → a token. File
    /// hashes/sizes stay REAL — the scenario content is deterministic, so they are too. A scenario
    /// that fixes its own ids/paths up front opts out of the corresponding pass.
    /// </summary>
    private static string Scrub(
        string json, string? workspace, bool scrubTimestamps = true, bool scrubGuids = true)
    {
        var guidMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var scrubbed = scrubGuids
            ? Regex.Replace(
                json,
                "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
                m =>
                {
                    if (!guidMap.TryGetValue(m.Value, out var placeholder))
                    {
                        placeholder = $"00000000-0000-0000-0000-{guidMap.Count + 1:D12}";
                        guidMap[m.Value] = placeholder;
                    }
                    return placeholder;
                })
            : json;
        if (scrubTimestamps)
            scrubbed = Regex.Replace(
                scrubbed,
                @"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})?",
                "2026-01-01T00:00:00Z");
        if (workspace is not null)
        {
            scrubbed = scrubbed
                // Double-encoded first: toolInput is a JSON string CONTAINING JSON, so paths inside
                // it carry twice-escaped backslashes.
                .Replace(JsonEncode(JsonEncode(workspace)), "<workspace>")
                .Replace(JsonEncode(workspace), "<workspace>")
                .Replace(workspace.Replace('\\', '/'), "<workspace>");
        }
        // The agent name embeds a GUID fragment for uniqueness in the shared DB.
        scrubbed = Regex.Replace(scrubbed, "Contract Agent [0-9a-fA-F]+", "Contract Agent");
        return PrettyPrint(scrubbed);
    }

    private static string JsonEncode(string s) =>
        JsonSerializer.Serialize(s)[1..^1]; // escaped form without surrounding quotes

    private static string PrettyPrint(string json) =>
        JsonSerializer.Serialize(
            JsonSerializer.Deserialize<JsonElement>(json),
            new JsonSerializerOptions { WriteIndented = true });

    private static string Normalize(string json) => PrettyPrint(json).ReplaceLineEndings("\n").Trim();

    private static string FixturesDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Antiphon.sln")) || Directory.Exists(Path.Combine(dir, ".git")))
                return Path.Combine(dir, "client", "src", "test", "fixtures", "contract");
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new DirectoryNotFoundException("Repo root not found from " + AppContext.BaseDirectory);
    }

    private static async Task<bool> GitAsync(string dir, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git", WorkingDirectory = dir,
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p is null) return false;
            await p.WaitForExitAsync();
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
