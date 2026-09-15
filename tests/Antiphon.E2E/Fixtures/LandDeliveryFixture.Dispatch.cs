using System.Text.Json;
using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Antiphon.E2E.Fixtures;

public sealed partial class LandDeliveryFixture
{
    public sealed record DispatchSibling(Guid Id, string Branch, string Tip, int Covered);
    private readonly List<DispatchSibling> _dispatchSiblings = [];
    public IReadOnlyList<DispatchSibling> ExpectedDispatchSiblings { get; private set; } = [];

    public async Task StartDispatchAsync()
    {
        File.Exists(Path.Combine(Root, "dispatch-task.txt")).ShouldBeTrue("opt-in dispatch setup required");
        Console.WriteLine($"C540 native evidence: {Root}");
        await using var db = CreateContext();
        var now = DateTime.UtcNow;
        var project = new Project { Id = Guid.NewGuid(), Name = "C540 owned", GitRepositoryUrl = Remote,
            BaseBranch = "master", CreatedAt = now, UpdatedAt = now };
        var board = new Board { Id = Guid.NewGuid(), ProjectId = project.Id, Name = "C540 owned", CreatedAt = now, UpdatedAt = now };
        var column = new BoardColumn { Id = Guid.NewGuid(), BoardId = board.Id, StateKey = "backlog", Name = "Backlog",
            CardStatus = CardStatus.Backlog, CreatedAt = now, UpdatedAt = now };
        var card = new Card { Id = Guid.NewGuid(), BoardId = board.Id, BoardColumnId = column.Id,
            Identifier = "CARD-0540", Title = "Owned sibling dispatch", CreatedAt = now, UpdatedAt = now };
        db.AddRange(project, board, column, card);
        foreach (var (name, start, alias) in new[] { ("a", "master", false), ("alias", "c540-a", true),
                     ("containing", "c540-a", false), ("divergent", "master", false) })
        {
            var branch = "c540-" + name;
            await GitAsync(Repository, "checkout", "-b", branch, start);
            if (!alias)
            {
                await File.WriteAllTextAsync(Path.Combine(Repository, name + ".txt"), name);
                await GitAsync(Repository, "add", name + ".txt");
                await GitAsync(Repository, "commit", "-m", "owned sibling " + name);
            }
            var tip = (await GitAsync(Repository, "rev-parse", "HEAD")).Trim();
            var id = Guid.NewGuid();
            var sibling = new DispatchSibling(id, branch, tip, name == "containing" ? 2 : 0);
            _dispatchSiblings.Add(sibling);
            db.AgentTasks.Add(new AgentTask { Id = id, RootTaskId = id, CardId = card.Id, ProjectId = project.Id,
                Title = "Owned sibling " + name, Goal = "fixture", Role = AgentTaskRole.Plan,
                Workspace = WorkspaceMode.Worktree, Status = AgentTaskStatus.Succeeded,
                RepoPath = Repository, WorkingDirectory = Repository, WorktreeBranch = branch,
                ReplyTo = AgentTaskReplyTo.None, CreatedAt = now.AddMinutes(-10), CompletedAt = now.AddMinutes(-5) });
        }
        await GitAsync(Repository, "checkout", "master");
        ExpectedDispatchSiblings = _dispatchSiblings.Where(s => s.Branch is "c540-containing" or "c540-divergent").ToArray();
        await File.WriteAllTextAsync(Path.Combine(Root, "dispatch-graph.json"), JsonSerializer.Serialize(_dispatchSiblings));
        db.AgentTasks.Add(new AgentTask { Id = TaskId, RootTaskId = TaskId, CardId = card.Id, ProjectId = project.Id,
            Title = "C540 native queued dispatch", Goal = "Complete this owned fixture turn.", Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Code, AgentKind = AgentKind.Grok, Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = Repository, RepoPath = Repository, Status = AgentTaskStatus.Queued,
            ReplyTo = AgentTaskReplyTo.Session, ParentSessionId = CallerId, CreatedAt = now });
        await db.SaveChangesAsync();
    }

    public async Task<List<AgentTaskDispatchWarningIntent>> DispatchIntentsAsync()
    {
        await using var db = CreateContext();
        return await db.AgentTaskDispatchWarningIntents.AsNoTracking().Where(i => i.TaskId == TaskId).OrderBy(i => i.WarningKey).ToListAsync();
    }

    public async Task<List<AgentTaskDispatchWarningIntent>> WaitForDispatchIntentsAsync()
    {
        await UntilAsync(async () => (await DispatchIntentsAsync()).Count == 2, "two committed reduced dispatch intents");
        var intents = await DispatchIntentsAsync();
        foreach (var expected in ExpectedDispatchSiblings)
        {
            var intent = intents.Single(i => i.WarningKey == DispatchBaseNotificationPayload.SiblingKey(expected.Id));
            intent.Detail.ShouldContain(expected.Branch); intent.Detail.ShouldContain(expected.Tip);
            if (expected.Covered > 0) intent.Detail.ShouldContain($"also covers {expected.Covered} other kept sibling branches");
            else intent.Detail.ShouldNotContain("also covers");
            intent.ParentSessionId.ShouldBe(CallerId);
        }
        return intents;
    }

    public Task WaitForBoundaryAsync(string boundary) => UntilAsync(() =>
        Task.FromResult(File.Exists(Path.Combine(Root, boundary + ".barrier.json"))), boundary);

    public Task TwoNotificationScansAsync()
    {
        var scans = Directory.GetFiles(Root, "notification-scan-*.observation.json").Length;
        return UntilAsync(() => Task.FromResult(Directory.GetFiles(Root, "notification-scan-*.observation.json").Length >= scans + 2),
            "two completed notification scans");
    }

    public async Task AssertDispatchReceiptsAsync(IReadOnlyList<AgentTaskDispatchWarningIntent> original)
    {
        await TwoNotificationScansAsync();
        await using (var db = CreateContext())
        {
            var notes = await db.AgentTaskLandNotifications.Where(n => n.TaskId == TaskId && n.Kind == LandNotificationKind.DispatchBase).ToListAsync();
            notes.Select(n => n.Id).Order().ShouldBe(original.Select(i => i.NotificationId).Order());
            foreach (var intent in original)
            {
                var note = notes.Single(n => n.Id == intent.NotificationId);
                note.Body.ShouldBe(intent.Body); note.SourceEventId.ShouldBe(intent.Id); note.ParentSessionId.ShouldBe(CallerId);
            }
        }
        foreach (var intent in original)
            await ReceiptAsync(LandNotificationKind.DispatchBase, notificationId: intent.NotificationId);
        await TwoNotificationScansAsync();
        await using var check = CreateContext();
        var current = await DispatchIntentsAsync();
        current.Select(i => (i.Id, i.NotificationId, i.WarningKey, i.Body)).ShouldBe(
            original.Select(i => (i.Id, i.NotificationId, i.WarningKey, i.Body)));
        var prompts = await check.TranscriptEntries.Where(p => p.AgentSessionId == CallerId && p.Kind == TranscriptKinds.UserPrompt).ToListAsync();
        var native = Directory.GetFiles(Path.Combine(Root, "native"), "updates.jsonl", SearchOption.AllDirectories)
            .SelectMany(File.ReadAllLines).Select(line => JsonDocument.Parse(line)).ToList();
        try
        {
            var nativePrompts = native.Where(d => d.RootElement.TryGetProperty("params", out var p)
                    && p.TryGetProperty("update", out var u) && u.GetProperty("sessionUpdate").GetString() == "user_message_chunk")
                .Select(d => d.RootElement.GetProperty("params").GetProperty("update").GetProperty("content").GetProperty("text").GetString()!).ToList();
            bool IsWarning(string? text) => text is not null && (text.Contains("[dispatch-base ", StringComparison.Ordinal)
                || text.Contains("branched from", StringComparison.Ordinal) && text.Contains("kept branch", StringComparison.Ordinal));
            prompts.Count(p => IsWarning(p.Text)).ShouldBe(2);
            nativePrompts.Count(IsWarning).ShouldBe(2, "all native keyed and unkeyed sibling warnings");
            foreach (var intent in original)
            {
                prompts.Count(p => PromptSubmissionMatch.IsCompleteIn(intent.Body, p.Text ?? "")).ShouldBe(1);
                nativePrompts.Count(p => PromptSubmissionMatch.IsCompleteIn(intent.Body, p)).ShouldBe(1);
                (await check.SessionQueuedMessages.CountAsync(m => m.SourceLandNotificationId == intent.NotificationId)).ShouldBe(1);
            }
            (await check.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == CallerId && m.Body.Contains("kept branch"))).ShouldBe(2);
        }
        finally { foreach (var document in native) document.Dispose(); }
        await SnapshotAsync();
    }

    public async Task MoveDispatchObservationsAsync()
    {
        foreach (var sibling in _dispatchSiblings)
            await GitAsync(Repository, "update-ref", "refs/heads/" + sibling.Branch, "master");
        await using var db = CreateContext();
        await db.AgentTasks.Where(t => t.Id == TaskId).ExecuteUpdateAsync(s => s
            .SetProperty(t => t.Status, AgentTaskStatus.Canceled).SetProperty(t => t.ReplyTo, AgentTaskReplyTo.None)
            .SetProperty(t => t.ParentSessionId, (Guid?)null));
        foreach (var sibling in _dispatchSiblings)
            await db.AgentTasks.Where(t => t.Id == sibling.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, AgentTaskStatus.Canceled));
    }
}
