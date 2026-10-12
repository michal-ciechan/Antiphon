using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0992 boundary comparison. Each row is one scenario with one hand-written item count.
/// The count is the base outcome, not a value copied from the production builder. The reference
/// is the f6d468e71 loader, frozen here, and the row passes only when production, that reference,
/// and the written count all agree.
/// </summary>
[Category("Integration")]
[Category("Slow")]
[NotInParallel("AgentQueue")]
public sealed class TaskInputWeakArmBoundaryTests
{
    [Test]
    [Arguments("complaint-row-7", 1)]
    [Arguments("complaint-row-8", 1)]
    [Arguments("complaint-row-9", 1)]
    [Arguments("complaint-row-17", 1)]
    [Arguments("no-assistant", 0)]
    [Arguments("receipt-at-end", 0)]
    [Arguments("receipt-at-start", 1)]
    [Arguments("no-nonnull-user-prompt", 0)]
    [Arguments("complaint-before-second-prompt", 0)]
    [Arguments("complaint-after-second-prompt", 0)]
    [Arguments("null-and-system-prompts", 1)]
    [Arguments("saturated-phrase-page", 1)]
    [Arguments("two-inputs-window-shortcut", 2)]
    [Arguments("two-inputs-phrase-shortcut", 2)]
    [Arguments("mixed-weak-strong-implicit", 0)]
    [Arguments("weak-strong-shared-receipt", 2)]
    [Arguments("two-tasks-shared-session", 2)]
    public async Task Boundary_matches_the_base_outcome(string scenario, int expectedItems)
    {
        await using var f = await TaskInputSpillFixture.CreateAsync();
        await SeedAsync(f, scenario);
        var actual = await ReadProductionAsync(f);
        await using var db = f.Db();
        var tasks = await db.AgentTasks.AsNoTracking().OrderBy(t => t.CreatedAt).ThenBy(t => t.Id).ToListAsync();
        var reference = await BaseSemanticsBuilder.BuildAsync(db, tasks, CancellationToken.None);
        var equal = JsonSerializer.Serialize(actual) == JsonSerializer.Serialize(reference);
        var verdict = $"production={actual.Count};reference={reference.Count};equal={equal}";
        verdict.ShouldBe($"production={expectedItems};reference={expectedItems};equal=True",
            $"{scenario} productionIds=[{Ids(actual)}] referenceIds=[{Ids(reference)}]");
    }

    private static string Ids(IReadOnlyList<AttentionItemDto> items) =>
        string.Join(",", items.Select(item => item.MessageId is Guid id ? id.ToString("D") : ""));

    private static async Task SeedAsync(TaskInputSpillFixture f, string scenario)
    {
        switch (scenario)
        {
            case "complaint-row-7":
            case "complaint-row-8":
            case "complaint-row-9":
            case "complaint-row-17":
            {
                var offset = int.Parse(scenario["complaint-row-".Length..], System.Globalization.CultureInfo.InvariantCulture);
                var row = await SeedOwnedAsync(f, 10, weak: true);
                for (var i = 1; i <= offset; i++)
                    await AddEntryAsync(f, 10 + i, TranscriptKinds.AssistantText,
                        i == offset ? $"I cannot read {row.RemoteSpillRelativePath}." : "working");
                return;
            }
            case "no-assistant":
                await SeedOwnedAsync(f, 10, weak: true);
                return;
            case "receipt-at-end":
                await AddEntryAsync(f, 1, TranscriptKinds.AssistantText, "I cannot read the refinement file.");
                await SeedOwnedAsync(f, 10, weak: true);
                return;
            case "receipt-at-start":
            {
                var row = await SeedOwnedAsync(f, 0, weak: true);
                await AddEntryAsync(f, 1, TranscriptKinds.AssistantText, $"I cannot read {row.RemoteSpillRelativePath}.");
                return;
            }
            case "no-nonnull-user-prompt":
            {
                var row = await SeedOwnedAsync(f, 10, weak: true);
                await using var db = f.Db();
                await db.TranscriptEntries.ExecuteUpdateAsync(s => s.SetProperty(t => t.Text, (string?)null));
                await AddEntryAsync(f, 11, TranscriptKinds.AssistantText, $"I cannot read {row.RemoteSpillRelativePath}.");
                return;
            }
            case "complaint-before-second-prompt":
            case "complaint-after-second-prompt":
            {
                var after = scenario == "complaint-after-second-prompt";
                var row = await SeedOwnedAsync(f, 10, weak: true);
                await AddEntryAsync(f, 20, TranscriptKinds.UserPrompt, "unrelated next prompt");
                await AddEntryAsync(f, after ? 21 : 11, TranscriptKinds.AssistantText,
                    $"I cannot read {row.RemoteSpillRelativePath}.");
                return;
            }
            case "null-and-system-prompts":
            {
                var row = await SeedOwnedAsync(f, 10, weak: true);
                await AddEntryAsync(f, 11, TranscriptKinds.UserPrompt, null);
                await AddEntryAsync(f, 12, "System", "not user");
                await AddEntryAsync(f, 13, TranscriptKinds.AssistantText, $"I cannot read {row.RemoteSpillRelativePath}.");
                return;
            }
            case "saturated-phrase-page":
            {
                var row = await SeedOwnedAsync(f, 10, weak: true);
                for (var i = 1; i <= 8; i++)
                    await AddEntryAsync(f, 10 + i, TranscriptKinds.AssistantText, "working");
                for (var i = 9; i <= 16; i++)
                    await AddEntryAsync(f, 10 + i, TranscriptKinds.AssistantText, "I cannot read /work/unrelated.md.");
                await AddEntryAsync(f, 27, TranscriptKinds.AssistantText, $"I cannot read {row.RemoteSpillRelativePath}.");
                return;
            }
            case "two-inputs-window-shortcut":
            case "two-inputs-phrase-shortcut":
            {
                var phrase = scenario == "two-inputs-phrase-shortcut";
                var one = await SeedOwnedAsync(f, 10, weak: true);
                var two = await SeedOwnedAsync(f, 100, weak: true);
                await using (var db = f.Db())
                    await db.TranscriptEntries.Where(t => t.Sequence == 100).ExecuteDeleteAsync();
                for (var i = 1; i <= 8; i++)
                    await AddEntryAsync(f, 10 + i, TranscriptKinds.AssistantText,
                        !phrase && i == 1 ? $"I cannot read {one.RemoteSpillRelativePath}." : "working");
                if (phrase)
                    await AddEntryAsync(f, 19, TranscriptKinds.AssistantText, $"I cannot read {one.RemoteSpillRelativePath}.");
                await AddEntryAsync(f, 20, TranscriptKinds.AssistantText, $"I cannot read {two.RemoteSpillRelativePath}.");
                return;
            }
            case "mixed-weak-strong-implicit":
                await SeedOwnedAsync(f, 10, weak: true);
                await SeedOwnedAsync(f, 20, weak: false);
                await AddEntryAsync(f, 21, TranscriptKinds.AssistantText, "I cannot read the refinement file.");
                return;
            case "weak-strong-shared-receipt":
            {
                var weak = await SeedOwnedAsync(f, 10, weak: true);
                var strong = await SeedOwnedAsync(f, 100, weak: false);
                await using (var db = f.Db())
                    await db.TranscriptEntries.Where(t => t.Sequence == 10).ExecuteDeleteAsync();
                await AddEntryAsync(f, 101, TranscriptKinds.AssistantText, $"I cannot read {weak.RemoteSpillRelativePath}.");
                await AddEntryAsync(f, 102, TranscriptKinds.AssistantText, $"I cannot read {strong.RemoteSpillRelativePath}.");
                return;
            }
            case "two-tasks-shared-session":
            {
                var first = await SeedOwnedAsync(f, 10, weak: true);
                var secondTaskId = Guid.NewGuid();
                var eventId = Guid.NewGuid();
                var route = AgentTaskInputService.Route(secondTaskId, eventId);
                var now = DateTime.UtcNow.AddMinutes(1);
                await using (var db = f.Db())
                {
                    db.AgentTasks.Add(new AgentTask
                    {
                        Id = secondTaskId, RootTaskId = secondTaskId, AgentSessionId = f.SessionId,
                        Title = "second owner", Goal = "Check", Kind = AgentTaskKind.Worker,
                        Role = AgentTaskRole.Code, AgentKind = AgentKind.Codex, ModelLevel = AgentModelLevel.High,
                        Workspace = WorkspaceMode.Worktree, WorkingDirectory = f.ServerRoot,
                        Status = AgentTaskStatus.Blocked, CreatedAt = now,
                    });
                    db.AgentTaskEvents.Add(new AgentTaskEvent
                    {
                        Id = eventId, AgentTaskId = secondTaskId, AgentSessionId = f.SessionId,
                        Type = AgentTaskEventType.Refined, InputBody = "go", At = now,
                    });
                    db.SessionQueuedMessages.Add(new SessionQueuedMessage
                    {
                        Id = Guid.NewGuid(), AgentSessionId = f.SessionId, Sequence = 2,
                        Body = "go", Origin = QueuedMessageOrigin.Delegation, CreatedAt = now,
                        ConversationKey = AgentTaskInputService.ConversationKey(secondTaskId, eventId),
                    });
                    await db.SaveChangesAsync();
                }
                for (var i = 1; i <= 8; i++)
                    await AddEntryAsync(f, 10 + i, TranscriptKinds.AssistantText,
                        i == 1 ? $"I cannot read {first.RemoteSpillRelativePath}." : "working");
                await AddEntryAsync(f, 20, TranscriptKinds.AssistantText, $"I cannot read {route}.");
                return;
            }
            default:
                throw new InvalidOperationException(scenario);
        }
    }

    private static async Task<SessionQueuedMessage> SeedOwnedAsync(TaskInputSpillFixture f, long sequence, bool weak)
    {
        await f.Replies.RefineAsync(f.TaskId, new string('q', 1800), CancellationToken.None);
        await using var db = f.Db();
        var row = await db.SessionQueuedMessages.AsNoTracking().OrderByDescending(r => r.Sequence).FirstAsync();
        await AddEntryAsync(f, sequence, TranscriptKinds.UserPrompt, weak ? "" : row.Body);
        if (!weak)
            return row;
        await using var update = f.Db();
        await update.SessionQueuedMessages.Where(r => r.Id == row.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Body, "go"));
        return row;
    }

    private static async Task AddEntryAsync(TaskInputSpillFixture f, long sequence, string kind, string? text)
    {
        await using var db = f.Db();
        var now = new DateTime(2026, 10, 2, 10, 0, 0, DateTimeKind.Utc).AddSeconds(sequence);
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(), AgentSessionId = f.SessionId, Sequence = sequence,
            Kind = kind, Text = text, Timestamp = now, CreatedAt = now,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<List<AttentionItemDto>> ReadProductionAsync(TaskInputSpillFixture f)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(f.ConnectionString));
        var tasks = await db.AgentTasks.AsNoTracking().OrderBy(t => t.CreatedAt).ThenBy(t => t.Id).ToListAsync();
        var service = new AttentionService(db, new BridgeQueueHarness.EmptyRunnerClient(),
            Options.Create(new SupervisionSettings()), Options.Create(new DelegationSettings()),
            TimeProvider.System, NullLogger<AttentionService>.Instance);
        var method = typeof(AttentionService).GetMethod("BuildTaskInputUnreadableItemsAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        return await (Task<List<AttentionItemDto>>)method.Invoke(service, [tasks, CancellationToken.None])!;
    }

    /// <summary>
    /// f6d468e71 BuildTaskInputUnreadableItemsAsync. The DbContext argument is the only adaptation.
    /// Short bodies still contribute "%" and closed weak inputs stay in the soleInput count.
    /// </summary>
    private static class BaseSemanticsBuilder
    {
        public static async Task<List<AttentionItemDto>> BuildAsync(
            AppDbContext db, IReadOnlyList<AgentTask> tasks, CancellationToken ct)
        {
            var bound = tasks.Where(t => t.AgentSessionId is not null).ToList();
            if (bound.Count == 0)
                return [];
            var sessionIds = bound.Select(t => t.AgentSessionId!.Value).Distinct().ToArray();
            var taskIds = bound.Select(t => t.Id).ToArray();
            var inputEvents = await db.AgentTaskEvents.AsNoTracking()
                .Where(e => taskIds.Contains(e.AgentTaskId) && e.InputBody != null
                    && (e.Type == AgentTaskEventType.Refined || e.Type == AgentTaskEventType.Replied))
                .Select(e => new { e.Id, e.AgentTaskId, e.AgentSessionId, e.Type })
                .ToDictionaryAsync(e => e.Id, ct);
            var inputKeys = inputEvents.Values
                .Select(e => AgentTaskInputService.ConversationKey(e.AgentTaskId, e.Id)).ToArray();
            var rows = await db.SessionQueuedMessages.AsNoTracking()
                .Where(m => sessionIds.Contains(m.AgentSessionId)
                    && m.Origin == QueuedMessageOrigin.Delegation
                    && ((m.ConversationKey != null && inputKeys.Contains(m.ConversationKey))
                        || (m.ConversationKey == null && m.Body.Contains("REFINEMENT")
                            && m.Body.Contains(".md"))))
                .OrderBy(m => m.Sequence)
                .Select(m => new QueueRow(m.Id, m.AgentSessionId, m.Sequence, m.Body,
                    m.ConversationKey, m.RemoteSpillRelativePath)).ToListAsync(ct);
            var inputs = new Dictionary<Guid, List<InputRow>>();
            foreach (var task in bound)
            {
                var sessionId = task.AgentSessionId!.Value;
                var marker = DelegationReportFormatter.TaskMarker(task.Id);
                var taskRows = rows.Where(r => r.AgentSessionId == sessionId).Select(r =>
                {
                    var owned = AgentTaskInputService.TryParseConversationKey(
                        r.ConversationKey, out var keyedTaskId, out var eventId)
                        && keyedTaskId == task.Id
                        && inputEvents.TryGetValue(eventId, out var e)
                        && e.AgentTaskId == task.Id && e.AgentSessionId == sessionId;
                    var legacy = !owned && r.ConversationKey is null
                        && r.Body.Contains(marker, StringComparison.Ordinal)
                        && r.Body.Contains("REFINEMENT", StringComparison.Ordinal)
                        && TaskInputReadFailure.LegacyLocation(r.Body) is not null;
                    var location = owned
                        ? r.RemoteSpillRelativePath ?? AgentTaskInputService.Route(task.Id, eventId)
                        : legacy ? TaskInputReadFailure.LegacyLocation(r.Body) : null;
                    return new InputRow(r, location,
                        owned && inputEvents[eventId].Type == AgentTaskEventType.Replied);
                }).Where(x => x.Location is not null).ToList();
                if (taskRows.Count > 0)
                    inputs[task.Id] = taskRows;
            }
            if (inputs.Count == 0)
                return [];

            var transcripts = new Dictionary<Guid, List<TranscriptRow>>();
            foreach (var group in inputs.Values.SelectMany(x => x).GroupBy(x => x.Row.AgentSessionId))
            {
                var sessionId = group.Key;
                var sessionInputs = group.ToList();
                var patterns = sessionInputs.Select(x => PromptPattern(x.Row.Body)).Distinct().ToArray();
                long? firstPromptSequence = null;
                var candidates = db.TranscriptEntries.AsNoTracking()
                    .Where(t => t.AgentSessionId == sessionId && t.Kind == TranscriptKinds.UserPrompt
                        && t.Text != null && patterns.Any(pattern => EF.Functions.Like(t.Text, pattern, "!")))
                    .OrderBy(t => t.Sequence)
                    .Select(t => new TranscriptRow(t.Sequence, t.Kind, t.Text, t.Timestamp ?? t.CreatedAt));
                await foreach (var prompt in candidates.AsAsyncEnumerable().WithCancellation(ct))
                {
                    if (!sessionInputs.Any(input => PromptSubmissionMatch.IsCompleteIn(input.Row.Body, prompt.Text)))
                        continue;
                    firstPromptSequence = prompt.Sequence;
                    break;
                }
                if (firstPromptSequence is not long floor)
                    continue;
                transcripts[sessionId] = await db.TranscriptEntries.AsNoTracking()
                    .Where(t => t.AgentSessionId == sessionId && t.Sequence >= floor
                        && (t.Kind == TranscriptKinds.UserPrompt || t.Kind == TranscriptKinds.AssistantText))
                    .OrderBy(t => t.Sequence)
                    .Select(t => new TranscriptRow(t.Sequence, t.Kind, t.Text, t.Timestamp ?? t.CreatedAt))
                    .ToListAsync(ct);
            }
            if (transcripts.Count == 0)
                return [];

            var relevantSessionIds = transcripts.Keys.ToArray();
            var delegationBodies = await db.SessionQueuedMessages.AsNoTracking()
                .Where(m => relevantSessionIds.Contains(m.AgentSessionId)
                    && m.Origin == QueuedMessageOrigin.Delegation)
                .Select(m => new { m.AgentSessionId, m.Body }).ToListAsync(ct);
            var items = new List<AttentionItemDto>();
            foreach (var task in bound)
            {
                var sessionId = task.AgentSessionId!.Value;
                if (!inputs.TryGetValue(task.Id, out var taskRows)
                    || !transcripts.TryGetValue(sessionId, out var sessionEntries))
                    continue;
                var prompts = sessionEntries.Where(t => t.Kind == TranscriptKinds.UserPrompt
                    && t.Text is not null).ToList();
                foreach (var input in taskRows)
                {
                    var prompt = prompts.FirstOrDefault(p =>
                        PromptSubmissionMatch.IsCompleteIn(input.Row.Body, p.Text));
                    if (prompt is null)
                        continue;
                    var nextPrompt = prompts.FirstOrDefault(p => p.Sequence > prompt.Sequence);
                    var end = nextPrompt?.Sequence ?? long.MaxValue;
                    var soleInput = taskRows.Count(x => x.Row.Sequence <= input.Row.Sequence) == 1;
                    var complaint = sessionEntries.FirstOrDefault(t => t.Kind == TranscriptKinds.AssistantText
                        && t.Sequence > prompt.Sequence && t.Sequence < end
                        && TaskInputReadFailure.Matches(t.Text, input.Location!, input.IsReply, soleInput));
                    if (complaint is null)
                        continue;
                    if (prompts.Any(p => p.Sequence > complaint.Sequence && delegationBodies.Any(r =>
                            r.AgentSessionId == sessionId
                            && PromptSubmissionMatch.IsCompleteIn(r.Body, p.Text))))
                        continue;
                    items.Add(new AttentionItemDto(
                        AttentionKind.TaskInputUnreadable, AlertSeverity.Warning,
                        task.Id, sessionId, task.AgentId, input.Row.Id,
                        $"Input unreadable: {DelegationReportFormatter.Short(task.Id)}",
                        "Delegate says it cannot read a caller input",
                        $"Assistant transcript #{complaint.Sequence}: {(complaint.Text ?? string.Empty)[..Math.Min(240, complaint.Text?.Length ?? 0)]}",
                        complaint.At, null,
                        [AttentionAction.OpenDrawer], task.CardId,
                        ConditionKey: $"task-input-unreadable:{task.Id:D}:{input.Row.Id:D}"));
                }
            }
            return items;
        }

        private static string PromptPattern(string body)
        {
            if (!PromptSubmissionMatch.RequiresTextMatch(body))
                return "%";
            var characters = PromptSubmissionMatch.Normalize(body).Replace(" ", "", StringComparison.Ordinal)
                .EnumerateRunes().Take(32).Select(rune => rune.ToString()
                    .Replace("!", "!!", StringComparison.Ordinal)
                    .Replace("%", "!%", StringComparison.Ordinal)
                    .Replace("_", "!_", StringComparison.Ordinal));
            return "%" + string.Join("%", characters) + "%";
        }

        private sealed record QueueRow(Guid Id, Guid AgentSessionId, long Sequence,
            string Body, string? ConversationKey, string? RemoteSpillRelativePath);
        private sealed record InputRow(QueueRow Row, string? Location, bool IsReply);
        private sealed record TranscriptRow(long Sequence, string Kind, string? Text, DateTime At);
    }
}
