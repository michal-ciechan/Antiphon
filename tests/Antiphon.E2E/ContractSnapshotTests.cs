using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Antiphon.E2E.Fixtures;
using Antiphon.Server.Domain.Entities;
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
                    t.TokensIn = 84_000; t.TokensOut = 3_100; t.CostUsd = 0.412m;
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
                    t.TokensIn = 61_500; t.TokensOut = 4_800; t.CostUsd = 0.318m;
                }),
                Task(suite, root, schema, 2, "Run the integration suite and report failures", cwd, t0.AddMinutes(9),
                    agents["task-suite"], "task-suite", t =>
                {
                    t.Role = Server.Domain.Enums.AgentTaskRole.Test;
                    t.ModelLevel = Server.Domain.Enums.AgentModelLevel.Low;
                    t.Status = Server.Domain.Enums.AgentTaskStatus.Succeeded;
                    t.ScopeGlob = "tests/**";
                    t.DispatchedAt = t0.AddMinutes(9);
                    t.CompletedAt = t0.AddMinutes(13).AddSeconds(24);
                    t.Result = "3 failures, all in Antiphon.Tests.Application.CardServiceTests — "
                        + "each is a missing checkpoint dependency, not a schema problem. Rerun with "
                        + "dotnet run --project tests/Antiphon.Tests.";
                    t.TokensIn = 22_000; t.TokensOut = 900; t.CostUsd = 0.019m;
                }),
                Task(install, root, root, 1, "Rewrite the Windows install section", cwd, t0.AddMinutes(3),
                    agents["task-install"], "task-install", t =>
                {
                    t.Role = Server.Domain.Enums.AgentTaskRole.Docs;
                    t.ModelLevel = Server.Domain.Enums.AgentModelLevel.Medium;
                    t.Status = Server.Domain.Enums.AgentTaskStatus.Blocked;
                    t.Workspace = Server.Domain.Enums.WorkspaceMode.ReadOnly;
                    t.ScopeGlob = "docs/setup.md";
                    t.DispatchedAt = t0.AddMinutes(3);
                    t.Result = "Rewrote \"## Windows install\" in docs/setup.md — 34 lines changed, "
                        + "every command now pwsh 7.\n\nOne decision is yours: should the old cmd "
                        + "examples be deleted, or kept alongside?";
                    t.TokensIn = 18_400; t.TokensOut = 1_250; t.CostUsd = 0.031m;
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
                    t.TokensIn = 40_100; t.TokensOut = 2_600; t.CostUsd = 0.204m;
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

    // ---- scenario helpers ----

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
