using System.Collections.Concurrent;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0604 G-21. Carries a REMOTE session's spilled body from the point it is spilled to the
/// Input frame that types its pointer, so the body travels in the Input payload and the RUNNER
/// writes the file inside the session's own cwd.
///
/// The desktop must never write it: a remote session's <c>Cwd</c> is a Windows path it cannot see,
/// so a desktop write leaves a file no one reads behind a prompt pointing at nothing — the agent
/// is told "read it in full before you do anything else" about a path that does not exist.
///
/// Transient staging is keyed by session and path. Queued bodies are persisted on their message
/// rows; this service reads the exact row Id from the pointer when an Input is sent.
/// </summary>
public sealed class RemoteSpillCourier
{
    private readonly ConcurrentDictionary<(Guid SessionId, string Path), StagedSpill> _staged = new();
    private IServiceScopeFactory? _scopeFactory;

    public RemoteSpillCourier(IServiceScopeFactory? scopeFactory = null) => _scopeFactory = scopeFactory;

    internal void UseScopeFactory(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public void Stage(Guid sessionId, string runnerCwd, PhoneHomeInputSpill spill) =>
        _staged[(sessionId, spill.RelativePath)] = new StagedSpill(runnerCwd, spill);

    public bool TryTake(Guid sessionId, out StagedSpill staged) =>
        TryPeek(sessionId, out staged) && Ack(sessionId, staged);

    /// <summary>
    /// CARD-0604 D-3. Look at the staged body WITHOUT clearing it. Taking it before the Input
    /// frame is acknowledged loses it outright when the phone-home socket drops mid-request:
    /// the body is gone from memory, the runner never wrote the file, and the retry types a
    /// pointer at a path that does not exist. Pair this with <see cref="Ack"/>.
    /// </summary>
    public bool TryPeek(Guid sessionId, out StagedSpill staged) =>
        TryPeek(sessionId, null, out staged);

    public bool TryPeek(Guid sessionId, string? input, out StagedSpill staged)
    {
        foreach (var entry in _staged)
        {
            if (entry.Key.SessionId != sessionId || (input is not null
                && !input.Contains(entry.Key.Path, StringComparison.Ordinal)))
                continue;
            staged = entry.Value;
            return true;
        }
        staged = null!;
        return false;
    }

    /// <summary>Find the exact queued spill after a server restart or a failed Input.</summary>
    public async Task<StagedSpill?> FindDurableAsync(Guid sessionId, string input, CancellationToken ct)
    {
        if (_scopeFactory is null)
            return null;
        const string prefix = ".antiphon/inbox/";
        var start = input.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0)
            return null;
        start += prefix.Length;
        var end = input.IndexOf(".md", start, StringComparison.Ordinal);
        if (end < 0 || !Guid.TryParse(input[start..end], out var messageId))
            return null;
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.SessionQueuedMessages
            .Where(m => m.Id == messageId && m.AgentSessionId == sessionId)
            .SingleOrDefaultAsync(ct);
        if (row is null)
            return null;
        var relative = row.RemoteSpillRelativePath
            ?? TypedBodySpill.InboxRelativePath(row.Id.ToString("D"));
        if (!input.Contains(relative, StringComparison.Ordinal))
            return null;

        if (!string.IsNullOrEmpty(row.RemoteSpillBody))
        {
            var storedCwd = await db.AgentSessions.AsNoTracking().Where(s => s.Id == sessionId)
                .Select(s => s.RunnerCwd).SingleOrDefaultAsync(ct);
            if (string.IsNullOrWhiteSpace(storedCwd))
                throw new InvalidOperationException("Queued remote spill has no runner cwd.");
            return new StagedSpill(storedCwd, new PhoneHomeInputSpill(relative, row.RemoteSpillBody, row.Id));
        }

        // A screen-only Delivered verdict is not a receipt. Only a complete UserPrompt of
        // this pointer releases a null body; anything else is re-spilled or canceled.
        if (await HasCompleteMatchingUserPromptAsync(db, sessionId, input, ct))
            return null;

        var frozen = await TryComposeFrozenSpillAsync(db, row, ct);
        if (frozen.Kind == FrozenSpillCompose.Incomplete)
        {
            await CancelUndeliverableAsync(db, row, frozen.MemberIds, ct);
            throw new RemoteSpillUndeliverableException();
        }

        string? stagedBody = null;
        if (TryPeek(sessionId, input, out var staged) && !string.IsNullOrEmpty(staged.Spill.Body))
            stagedBody = staged.Spill.Body;
        var repair = Inspect(row, stagedBody);
        string? source = frozen.Kind == FrozenSpillCompose.Composed
            ? frozen.Body
            : repair.Kind switch
            {
                SpillBodyRepairKind.Ready => row.RemoteSpillBody,
                SpillBodyRepairKind.Recovered => repair.Body,
                _ => null,
            };
        if (string.IsNullOrEmpty(source))
        {
            await CancelUndeliverableAsync(db, row, [], ct);
            throw new RemoteSpillUndeliverableException();
        }

        if (row.RemoteSpillBody is null)
        {
            row.RemoteSpillBody = source;
            row.RemoteSpillRelativePath = relative;
            await db.SaveChangesAsync(ct);
        }

        var cwd = await db.AgentSessions.AsNoTracking().Where(s => s.Id == sessionId)
            .Select(s => s.RunnerCwd).SingleOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(cwd))
            throw new InvalidOperationException("Queued remote spill has no runner cwd.");
        return new StagedSpill(cwd, new PhoneHomeInputSpill(relative, source, row.Id));
    }

    /// <summary>
    /// CARD-0604 D-3. Clear the staged body now that the runner has acknowledged writing it.
    /// Only the body that was actually delivered is removed: a newer spill staged while the
    /// frame was in flight stays, so it is not silently dropped by a late acknowledgement.
    /// </summary>
    public bool Ack(Guid sessionId, StagedSpill delivered) =>
        _staged.TryRemove(new KeyValuePair<(Guid, string), StagedSpill>(
            (sessionId, delivered.Spill.RelativePath), delivered));

    public bool IsStaged(Guid sessionId) => _staged.Keys.Any(k => k.SessionId == sessionId);

    public void Clear(Guid sessionId)
    {
        foreach (var key in _staged.Keys.Where(k => k.SessionId == sessionId))
            _staged.TryRemove(key, out _);
    }

    public sealed record StagedSpill(string RunnerCwd, PhoneHomeInputSpill Spill);

    /// <summary>
    /// A null <see cref="SessionQueuedMessage.RemoteSpillBody"/> on a spill pointer. The source
    /// text is the row body when that body is not itself the pointer, or a body still staged
    /// in memory. A screen-only <see cref="DeliveryVerdict.Delivered"/> is not a release:
    /// <paramref name="releasedByCompleteUserPrompt"/> is the only receipt that counts.
    /// </summary>
    internal static SpillBodyRepair Inspect(
        SessionQueuedMessage row, string? stagedBody, bool releasedByCompleteUserPrompt = false)
    {
        if (!string.IsNullOrEmpty(row.RemoteSpillBody))
            return new SpillBodyRepair(SpillBodyRepairKind.Ready, row.RemoteSpillBody);
        if (releasedByCompleteUserPrompt)
            return new SpillBodyRepair(SpillBodyRepairKind.Released, null);

        var relative = row.RemoteSpillRelativePath
            ?? TypedBodySpill.InboxRelativePath(row.Id.ToString("D"));
        var bodyIsPointer = !string.IsNullOrEmpty(row.Body)
            && (row.Body.Contains(relative, StringComparison.Ordinal)
                || row.Body.Contains(TypedBodySpill.PointerHeadline, StringComparison.Ordinal));
        if (!bodyIsPointer && !string.IsNullOrEmpty(row.Body))
            return new SpillBodyRepair(SpillBodyRepairKind.Recovered, row.Body);
        if (!string.IsNullOrEmpty(stagedBody))
            return new SpillBodyRepair(SpillBodyRepairKind.Recovered, stagedBody);
        return new SpillBodyRepair(SpillBodyRepairKind.Undeliverable, null);
    }

    /// <summary>
    /// True when a stored UserPrompt carries the whole typed pointer. A Delivered verdict
    /// without that record is the screen-only fallback and must not release the bytes.
    /// </summary>
    internal static async Task<bool> HasCompleteMatchingUserPromptAsync(
        AppDbContext db, Guid sessionId, string? typed, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(typed) || !PromptSubmissionMatch.RequiresTextMatch(typed))
            return false;
        var prompts = await db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId
                && t.Kind == TranscriptKinds.UserPrompt
                && t.Text != null)
            .Select(t => t.Text!)
            .ToListAsync(ct);
        return prompts.Any(text => PromptSubmissionMatch.IsCompleteIn(typed, text));
    }

    /// <summary>
    /// Rebuild the body a frozen Completion spill named. A batch is every member's logical
    /// note, in the frozen member order. NotFrozen means this row has no committed rendering.
    /// Incomplete means a rendering exists and a member note cannot be read.
    /// </summary>
    internal static async Task<FrozenSpillComposition> TryComposeFrozenSpillAsync(
        AppDbContext db, SessionQueuedMessage head, CancellationToken ct)
    {
        if (head.SourceLandNotificationId is null)
            return new FrozenSpillComposition(FrozenSpillCompose.NotFrozen, null, []);

        var headJson = await db.AgentTaskLandNotifications.AsNoTracking()
            .Where(n => n.Id == head.SourceLandNotificationId)
            .Select(n => n.CompletionDeliveryJson)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrEmpty(headJson))
            return new FrozenSpillComposition(FrozenSpillCompose.NotFrozen, null, []);

        var headDelivery = TaskCompletionNotification.TryReadDelivery(headJson);
        if (headDelivery is null || headDelivery.MemberQueueIds.Count == 0)
            return new FrozenSpillComposition(FrozenSpillCompose.Incomplete, null, headDelivery?.MemberQueueIds ?? []);

        var memberIds = headDelivery.MemberQueueIds;
        var rows = await db.SessionQueuedMessages.AsNoTracking()
            .Where(m => memberIds.Contains(m.Id))
            .Select(m => new { m.Id, m.SourceLandNotificationId })
            .ToListAsync(ct);
        if (rows.Count != memberIds.Count)
            return new FrozenSpillComposition(FrozenSpillCompose.Incomplete, null, memberIds);

        var noteIds = new List<Guid>(memberIds.Count);
        foreach (var id in memberIds)
        {
            var member = rows.SingleOrDefault(r => r.Id == id);
            if (member?.SourceLandNotificationId is not Guid noteId)
                return new FrozenSpillComposition(FrozenSpillCompose.Incomplete, null, memberIds);
            noteIds.Add(noteId);
        }

        var notes = await db.AgentTaskLandNotifications.AsNoTracking()
            .Where(n => noteIds.Contains(n.Id))
            .Select(n => new { n.Id, n.CompletionDeliveryJson })
            .ToListAsync(ct);
        var logicals = new List<string>(memberIds.Count);
        for (var i = 0; i < memberIds.Count; i++)
        {
            var json = notes.SingleOrDefault(n => n.Id == noteIds[i])?.CompletionDeliveryJson;
            if (TaskCompletionNotification.TryReadDelivery(json) is not { } delivery
                || delivery.MemberQueueIds.Count != memberIds.Count
                || !memberIds.All(delivery.MemberQueueIds.Contains))
                return new FrozenSpillComposition(FrozenSpillCompose.Incomplete, null, memberIds);
            logicals.Add(delivery.LogicalNote);
        }

        var composed = logicals.Count == 1
            ? logicals[0]
            : ChannelPromptFormat.FormatBatch(logicals.Take(logicals.Count - 1).ToList(), logicals[^1]);
        return new FrozenSpillComposition(FrozenSpillCompose.Composed, composed, memberIds);
    }

    internal static async Task CancelUndeliverableAsync(
        AppDbContext db, SessionQueuedMessage head, IReadOnlyList<Guid> memberIds, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var others = memberIds.Where(id => id != head.Id).Distinct().ToArray();
        if (others.Length > 0)
        {
            var rows = await db.SessionQueuedMessages.Where(m => others.Contains(m.Id)).ToListAsync(ct);
            foreach (var row in rows)
                MarkUndeliverable(row, now);
        }

        if (head.Status != QueuedMessageStatus.Canceled
            || head.DeliveryVerdict != DeliveryVerdict.SpillBodyMissing)
            MarkUndeliverable(head, now);
        await db.SaveChangesAsync(ct);
    }

    internal static void MarkUndeliverable(SessionQueuedMessage row, DateTime now)
    {
        row.Status = QueuedMessageStatus.Canceled;
        row.CanceledAt = now;
        row.DeliveryVerdict = DeliveryVerdict.SpillBodyMissing;
        row.DeliveryVerdictAt = now;
    }
}

/// <summary>A queued spill pointer whose file bytes are gone. Delivery must not type it.</summary>
public sealed class RemoteSpillUndeliverableException : InvalidOperationException
{
    public const string MissingBodyReason =
        "Queued spill pointer has no durable body and the source message is gone.";

    public RemoteSpillUndeliverableException()
        : base(MissingBodyReason)
    {
    }
}

internal enum SpillBodyRepairKind
{
    Ready,
    Recovered,
    Undeliverable,
    Released,
}

internal readonly record struct SpillBodyRepair(SpillBodyRepairKind Kind, string? Body);

internal enum FrozenSpillCompose
{
    NotFrozen,
    Composed,
    Incomplete,
}

internal readonly record struct FrozenSpillComposition(
    FrozenSpillCompose Kind, string? Body, IReadOnlyList<Guid> MemberIds);
