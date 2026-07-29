using System.Diagnostics;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
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
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Application;

/// <summary>
/// The Files review surface, offline: git ∪ transcript file listing with hash-anchored
/// viewed/reviewed marks, and the inline-thread loop — create → dispatch into the agent session
/// (envelope + correlation) → the turn's reply lands back on the thread, exactly the channel
/// bridge pattern.
/// </summary>
[Category("Integration")]
[NotInParallel("MessageQueue")]
public class ReviewLoopTests
{
    // ---------- files listing (needs the git CLI; skips cleanly without it) ----------

    [Test]
    public async Task Files_listing_merges_git_changes_agent_activity_and_review_marks()
    {
        await using var h = await HarnessAsync(withGitRepo: true);

        // Repo state: one committed-then-modified file, one untracked file; the agent (transcript)
        // wrote a third file that is untracked too.
        await File.WriteAllTextAsync(Path.Combine(h.Workspace, "committed.md"), "changed content");
        await File.WriteAllTextAsync(Path.Combine(h.Workspace, "scratch.txt"), "untracked");
        var agentFile = Path.Combine(h.Workspace, "notes", "report.md");
        Directory.CreateDirectory(Path.GetDirectoryName(agentFile)!);
        await File.WriteAllTextAsync(agentFile, "# Report");
        await h.InsertToolCallAsync("Write", agentFile);

        var listing = await h.Files.GetFilesAsync(h.AgentId, CancellationToken.None);

        listing.ShouldNotBeNull();
        listing!.IsGitRepository.ShouldBeTrue();
        var byPath = listing.Files.ToDictionary(f => f.Path);
        byPath["committed.md"].GitStatus.ShouldBe("Modified");
        byPath["scratch.txt"].GitStatus.ShouldBe("Untracked");
        byPath["notes/report.md"].AgentEdits.ShouldBe(1);
        byPath["notes/report.md"].IsMarkdown.ShouldBeTrue();
        byPath["notes/report.md"].ContentHash.ShouldNotBeNull();
    }

    [Test]
    public async Task Viewed_marks_are_hash_anchored_and_go_stale_on_change()
    {
        await using var h = await HarnessAsync(withGitRepo: true);
        var file = Path.Combine(h.Workspace, "committed.md");
        await File.WriteAllTextAsync(file, "v1");

        (await h.Files.MarkAsync(h.AgentId, ["committed.md"], null, FileReviewLevel.Viewed, CancellationToken.None))
            .ShouldBe(1);
        var listing = await h.Files.GetFilesAsync(h.AgentId, CancellationToken.None);
        var dto = listing!.Files.Single(f => f.Path == "committed.md");
        dto.ReviewLevel.ShouldBe("Viewed");
        dto.ReviewStale.ShouldBeFalse();

        // The file changes → the mark survives but reads STALE (unviewed changes again).
        await File.WriteAllTextAsync(file, "v2 — changed after the mark");
        listing = await h.Files.GetFilesAsync(h.AgentId, CancellationToken.None);
        dto = listing!.Files.Single(f => f.Path == "committed.md");
        dto.ReviewLevel.ShouldBe("Viewed");
        dto.ReviewStale.ShouldBeTrue();
    }

    [Test]
    public async Task Folder_prefix_marking_covers_every_file_under_it()
    {
        await using var h = await HarnessAsync(withGitRepo: true);
        Directory.CreateDirectory(Path.Combine(h.Workspace, "docs"));
        await File.WriteAllTextAsync(Path.Combine(h.Workspace, "docs", "a.md"), "a");
        await File.WriteAllTextAsync(Path.Combine(h.Workspace, "docs", "b.md"), "b");
        await File.WriteAllTextAsync(Path.Combine(h.Workspace, "top.md"), "top");

        var marked = await h.Files.MarkAsync(h.AgentId, null, "docs", FileReviewLevel.Reviewed, CancellationToken.None);

        marked.ShouldBe(2, "only files under docs/ — not top.md");
        var listing = await h.Files.GetFilesAsync(h.AgentId, CancellationToken.None);
        listing!.Files.Single(f => f.Path == "docs/a.md").ReviewLevel.ShouldBe("Reviewed");
        listing.Files.Single(f => f.Path == "docs/b.md").ReviewLevel.ShouldBe("Reviewed");
        listing.Files.Single(f => f.Path == "top.md").ReviewLevel.ShouldBeNull();
    }

    // Live miss 2026-07-29 (Family agent, workspace agents/family INSIDE the ClaudeBot repo):
    // git reports paths relative to the REPO ROOT, so "sites/x.md" resolved against the workspace
    // to a nonexistent file and every viewer rendered empty. Workspace-subdir repos must
    // re-relativize paths and serve content correctly.
    [Test]
    public async Task A_workspace_that_is_a_subdirectory_of_the_repo_lists_and_serves_files_correctly()
    {
        await using var h = await HarnessAsync(withGitRepo: false);

        // Build a repo ABOVE the workspace: repoRoot/{shared.md, ws/inside.md}; workspace = repoRoot/ws.
        var repoRoot = Path.Combine(Path.GetTempPath(), $"antiphon-subdir-{Guid.NewGuid():N}");
        var wsDir = Path.Combine(repoRoot, "ws");
        Directory.CreateDirectory(wsDir);
        try
        {
            if (!await TryGitAsync(repoRoot, "init"))
                throw new SkipTestException("git CLI not available");
            await TryGitAsync(repoRoot, "config", "user.email", "t@antiphon.local");
            await TryGitAsync(repoRoot, "config", "user.name", "T");
            await File.WriteAllTextAsync(Path.Combine(wsDir, "inside.md"), "committed inside");
            await File.WriteAllTextAsync(Path.Combine(repoRoot, "shared.md"), "committed shared");
            await TryGitAsync(repoRoot, "add", ".");
            await TryGitAsync(repoRoot, "commit", "-m", "seed");
            await File.WriteAllTextAsync(Path.Combine(wsDir, "inside.md"), "changed inside");
            await File.WriteAllTextAsync(Path.Combine(repoRoot, "shared.md"), "changed shared");

            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions()))
            {
                var agent = await db.Agents.FirstAsync(a => a.Id == h.AgentId);
                agent.WorkingDirectory = wsDir;
                await db.SaveChangesAsync();
            }

            var listing = await h.Files.GetFilesAsync(h.AgentId, CancellationToken.None);
            listing.ShouldNotBeNull();

            // The workspace file: WORKSPACE-relative path, real content served, HEAD readable.
            var inside = listing!.Files.Single(f => f.Path == "inside.md");
            inside.GitStatus.ShouldBe("Modified");
            inside.External.ShouldBeFalse();
            var work = await h.Files.GetContentAsync(h.AgentId, "inside.md", "work", CancellationToken.None);
            work!.Text.ShouldBe("changed inside", "the viewer rendered empty before the fix");
            var head = await h.Files.GetContentAsync(h.AgentId, "inside.md", "head", CancellationToken.None);
            head!.Text.ShouldNotBeNull();
            head.Text!.TrimEnd().ShouldBe("committed inside");

            // The repo change OUTSIDE the workspace: listed as external with an absolute path,
            // and its content is servable.
            var shared = listing.Files.Single(f => f.Path.EndsWith("/shared.md", StringComparison.Ordinal));
            shared.External.ShouldBeTrue();
            var sharedContent = await h.Files.GetContentAsync(h.AgentId, shared.Path, "work", CancellationToken.None);
            sharedContent!.Text.ShouldBe("changed shared");
        }
        finally
        {
            try { Directory.Delete(repoRoot, recursive: true); } catch { /* best effort */ }
        }
    }

    // ---------- the thread loop ----------

    [Test]
    public async Task Dispatched_thread_delivers_an_enveloped_prompt_and_captures_the_agents_reply()
    {
        await using var h = await HarnessAsync(withGitRepo: false);

        var thread = await h.Threads.CreateAsync(h.AgentId, new CreateReviewThreadRequest(
            "notes/report.md", 12, "the questionable line", "Is this number right?", Dispatch: true),
            CancellationToken.None);
        thread.ShouldNotBeNull();
        thread!.Status.ShouldBe("AwaitingAgent");

        // Idle session → the queue delivered the prompt straight into the (fake) adapter.
        var prompt = h.Adapter.SubmittedBodies.ShouldHaveSingleItem();
        prompt.ShouldContain($"[Review #{thread.Id:N}"[..17]);
        prompt.ShouldContain("notes/report.md:12");
        prompt.ShouldContain("> the questionable line");
        prompt.ShouldContain("Is this number right?");

        // The agent's turn completes; its reply must land on THIS thread.
        await h.InsertTurnAsync(prompt, "Checked — the number is correct, source linked in the doc.");
        await h.Replies.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        var after = (await h.Threads.GetThreadsAsync(h.AgentId, null, CancellationToken.None)).Single();
        after.Status.ShouldBe("AwaitingHuman");
        after.Comments.Count.ShouldBe(2);
        after.Comments[^1].Author.ShouldBe("Agent");
        after.Comments[^1].Body.ShouldContain("the number is correct");
    }

    [Test]
    public async Task Two_dispatched_threads_route_their_replies_independently()
    {
        await using var h = await HarnessAsync(withGitRepo: false);

        var a = await h.Threads.CreateAsync(h.AgentId, new CreateReviewThreadRequest(
            "a.md", 1, null, "Question A", Dispatch: true), CancellationToken.None);
        // First dispatch is delivered immediately (idle); the second queues behind the now-working
        // session — deliver its turn first, then flush.
        var promptA = h.Adapter.SubmittedBodies.ShouldHaveSingleItem();

        await h.InsertTurnAsync(promptA, "Answer A");
        await h.Replies.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        var b = await h.Threads.CreateAsync(h.AgentId, new CreateReviewThreadRequest(
            "b.md", 2, null, "Question B", Dispatch: true), CancellationToken.None);
        var promptB = h.Adapter.SubmittedBodies.Skip(1).Single();
        promptB.ShouldContain("Question B");

        await h.InsertTurnAsync(promptB, "Answer B");
        await h.Replies.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        var threads = await h.Threads.GetThreadsAsync(h.AgentId, null, CancellationToken.None);
        var threadA = threads.Single(t => t.Id == a!.Id);
        var threadB = threads.Single(t => t.Id == b!.Id);
        threadA.Comments[^1].Body.ShouldBe("Answer A");
        threadB.Comments[^1].Body.ShouldBe("Answer B");
        threadB.Comments[^1].Body.ShouldNotContain("Answer A");
    }

    [Test]
    public async Task A_turn_the_review_loop_did_not_start_matches_no_thread()
    {
        await using var h = await HarnessAsync(withGitRepo: false);

        var thread = await h.Threads.CreateAsync(h.AgentId, new CreateReviewThreadRequest(
            "a.md", 1, null, "Pending question", Dispatch: true), CancellationToken.None);
        h.Adapter.SubmittedBodies.ShouldHaveSingleItem();

        // A human types an unrelated prompt into the terminal; its turn must not consume the thread.
        await h.InsertTurnAsync("run the tests please", "All green.");
        await h.Replies.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        var after = (await h.Threads.GetThreadsAsync(h.AgentId, null, CancellationToken.None)).Single();
        after.Status.ShouldBe("AwaitingAgent", "the unrelated turn must leave the correlation pending");
        after.Comments.Count.ShouldBe(1);
        h.Replies.PendingCount(h.SessionId).ShouldBe(1);
    }

    [Test]
    public async Task NO_REPLY_acknowledges_without_adding_a_comment()
    {
        await using var h = await HarnessAsync(withGitRepo: false);

        await h.Threads.CreateAsync(h.AgentId, new CreateReviewThreadRequest(
            "a.md", 1, null, "FYI only", Dispatch: true), CancellationToken.None);
        var prompt = h.Adapter.SubmittedBodies.ShouldHaveSingleItem();

        await h.InsertTurnAsync(prompt, "NO_REPLY");
        await h.Replies.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        var after = (await h.Threads.GetThreadsAsync(h.AgentId, null, CancellationToken.None)).Single();
        after.Status.ShouldBe("AwaitingHuman");
        after.Comments.Count.ShouldBe(1, "NO_REPLY consumes the correlation without commenting");
    }

    // ---------- harness ----------

    private sealed class Harness : IAsyncDisposable
    {
        public required ServiceProvider Provider { get; init; }
        public required AgentFilesService Files { get; init; }
        public required ReviewThreadService Threads { get; init; }
        public required ReviewReplyDispatcher Replies { get; init; }
        public required FakeAgentProtocolAdapter Adapter { get; init; }
        public required Guid AgentId { get; init; }
        public required Guid SessionId { get; init; }
        public required string Workspace { get; init; }

        public async Task InsertToolCallAsync(string toolName, string filePath)
        {
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            var baseSeq = (await db.TranscriptEntries
                .Where(t => t.AgentSessionId == SessionId)
                .MaxAsync(t => (long?)t.Sequence)) ?? 0;
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = SessionId, Sequence = baseSeq + 1,
                Kind = TranscriptKinds.ToolCall, ToolName = toolName,
                ToolInput = System.Text.Json.JsonSerializer.Serialize(new { file_path = filePath }),
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        public async Task InsertTurnAsync(string prompt, string response)
        {
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            var baseSeq = (await db.TranscriptEntries
                .Where(t => t.AgentSessionId == SessionId)
                .MaxAsync(t => (long?)t.Sequence)) ?? 0;
            var now = DateTime.UtcNow;
            db.TranscriptEntries.AddRange(
                new TranscriptEntry
                {
                    Id = Guid.NewGuid(), AgentSessionId = SessionId, Sequence = baseSeq + 1,
                    Kind = TranscriptKinds.UserPrompt, Text = prompt, CreatedAt = now,
                },
                new TranscriptEntry
                {
                    Id = Guid.NewGuid(), AgentSessionId = SessionId, Sequence = baseSeq + 2,
                    Kind = TranscriptKinds.AssistantText, Text = response, CreatedAt = now,
                },
                new TranscriptEntry
                {
                    Id = Guid.NewGuid(), AgentSessionId = SessionId, Sequence = baseSeq + 3,
                    Kind = TranscriptKinds.TurnEnd, StopReason = "end_turn", CreatedAt = now,
                });
            await db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions()))
            {
                await db.ReviewThreads.Where(t => t.AgentId == AgentId).ExecuteDeleteAsync();
                await db.FileReviewStates.Where(f => f.AgentId == AgentId).ExecuteDeleteAsync();
                await db.SessionQueuedMessages.Where(m => m.AgentSessionId == SessionId).ExecuteDeleteAsync();
                await db.TranscriptEntries.Where(t => t.AgentSessionId == SessionId).ExecuteDeleteAsync();
                await db.AgentSessions.Where(s => s.Id == SessionId).ExecuteDeleteAsync();
                await db.Agents.Where(a => a.Id == AgentId).ExecuteDeleteAsync();
            }
            await Provider.DisposeAsync();
            try { Directory.Delete(Workspace, recursive: true); } catch { /* best effort */ }
        }
    }

    private static async Task<Harness> HarnessAsync(bool withGitRepo)
    {
        var workspace = Path.Combine(Path.GetTempPath(), $"antiphon-review-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        if (withGitRepo)
        {
            if (!await TryGitAsync(workspace, "init"))
                throw new SkipTestException("git CLI not available");
            await TryGitAsync(workspace, "config", "user.email", "test@antiphon.local");
            await TryGitAsync(workspace, "config", "user.name", "Antiphon Tests");
            await File.WriteAllTextAsync(Path.Combine(workspace, "committed.md"), "original content");
            await TryGitAsync(workspace, "add", ".");
            await TryGitAsync(workspace, "commit", "-m", "seed");
        }

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(TestDbFixture.ConnectionString, npgsql =>
            {
                npgsql.MigrationsAssembly("Antiphon.Server");
                npgsql.SetPostgresVersion(16, 0);
            }));
        var eventBus = new MockEventBus();
        services.AddSingleton(eventBus);
        services.AddSingleton<IEventBus>(eventBus);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IOptions<AgentSessionSettings>>(Options.Create(new AgentSessionSettings()));
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<GitWorkspaceService>();
        services.AddScoped<AgentFilesService>();
        services.AddSingleton<ReviewReplyDispatcher>();
        services.AddScoped<ReviewThreadService>();
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var agentId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Agents.Add(new Agent
            {
                Id = agentId,
                Name = $"ReviewTestAgent-{agentId:N}"[..30],
                Slug = $"review-test-{agentId:N}"[..20],
                WorkingDirectory = workspace,
                Status = AgentStatus.Working,
                PersistentSessionId = sessionId.ToString("D"),
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.AgentSessions.Add(new AgentSession
            {
                Id = sessionId, CardId = null, DefinitionName = "fake", AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running, Cwd = workspace, Cols = 120, Rows = 30,
                CreatedAt = now, StartedAt = now, LastSeenAt = now,
            });
            await db.SaveChangesAsync();
        }

        var runtime = provider.GetRequiredService<AgentSessionRuntime>();
        var adapter = new FakeAgentProtocolAdapter();
        runtime.Register(sessionId, adapter);

        var scope2 = provider.CreateScope();
        return new Harness
        {
            Provider = provider,
            Files = scope2.ServiceProvider.GetRequiredService<AgentFilesService>(),
            Threads = scope2.ServiceProvider.GetRequiredService<ReviewThreadService>(),
            Replies = provider.GetRequiredService<ReviewReplyDispatcher>(),
            Adapter = adapter,
            AgentId = agentId,
            SessionId = sessionId,
            Workspace = workspace,
        };
    }

    private static async Task<bool> TryGitAsync(string dir, params string[] args)
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
