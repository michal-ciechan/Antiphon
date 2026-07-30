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

    // ---- scenario helpers ----

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
        AntiphonAppFixture app, string url, string fixtureName, string workspace, bool scrubTimestamps = true)
    {
        var response = await app.HttpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var raw = await response.Content.ReadAsStringAsync();
        var scrubbed = Scrub(raw, workspace, scrubTimestamps);

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
    /// hashes/sizes stay REAL — the scenario content is deterministic, so they are too.
    /// </summary>
    private static string Scrub(string json, string workspace, bool scrubTimestamps = true)
    {
        var guidMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var scrubbed = Regex.Replace(
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
            });
        if (scrubTimestamps)
            scrubbed = Regex.Replace(
                scrubbed,
                @"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})?",
                "2026-01-01T00:00:00Z");
        var workspaceForward = workspace.Replace('\\', '/');
        scrubbed = scrubbed
            // Double-encoded first: toolInput is a JSON string CONTAINING JSON, so paths inside it
            // carry twice-escaped backslashes.
            .Replace(JsonEncode(JsonEncode(workspace)), "<workspace>")
            .Replace(JsonEncode(workspace), "<workspace>")
            .Replace(workspaceForward, "<workspace>");
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
