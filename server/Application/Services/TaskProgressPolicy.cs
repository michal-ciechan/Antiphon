using System.Security.Cryptography;
using System.Text;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Whether an OPEN task that is mid-turn is actually making progress, or just emitting rows
/// (CARD-0153). One implementation, shared by the sweep that records it
/// (<c>AgentTaskDispatcher.DetectStalledProgressAsync</c>) and the projection that lists it
/// (<c>AttentionService</c>, <c>AttentionKind.ProgressStalled</c>)
/// — the same reason <see cref="TaskDeadlinePolicy"/> is shared by its pair.
///
/// <para><b>The partition with the phase deadline.</b> <see cref="TaskDeadlinePolicy"/> owns
/// "nothing landed" (a quiet mid-turn session). This owns "rows keep landing and none of them
/// is new". A session with fewer than <c>MinRowsInWindow</c> rows in the look-back is a slow
/// session, not a looping one, and returns null here so the phase clock can still fail it.</para>
///
/// <para><b>Working is asked ONLY through the shared verdict</b>
/// (<c>SessionMessageQueueService.IsWorkingAsync</c>). Idle is
/// <c>AttentionKind.PastExpectedIdle</c>'s business; a fourth
/// working-rule implementation would be a defect by this repo's own standing rule.</para>
///
/// <para><b>Read-only and side-effect free.</b> Nothing here fails, kills, settles, or writes.
/// Callers that are about to RAISE on a verdict pull the transcript first (CARD-0055).</para>
/// </summary>
internal static class TaskProgressPolicy
{
    internal sealed record ProgressRow(
        long Sequence,
        string Kind,
        string? ToolName,
        string? ToolInput,
        string? Text,
        DateTime At);

    /// <param name="StalledFor">now - max(lastProgressAt, DispatchedAt).</param>
    /// <param name="LastProgressAt">Latest novel row (or dispatch, when nothing in the window is novel).</param>
    /// <param name="Summary">The attention headline and the incident message are this sentence.</param>
    /// <param name="FailureReason">Machine-readable: <c>rows=14 distinct=2 lastNovel=ToolCall age=38m ...</c></param>
    internal sealed record Verdict(
        TimeSpan StalledFor,
        int RowCount,
        int DistinctFingerprints,
        string? LastNovelKind,
        DateTime LastProgressAt,
        string Summary,
        string FailureReason);

    /// <summary>
    /// File/commit activity since dispatch (CARD-0153 S2). Either timestamp newer than
    /// <c>lastProgressAt</c> replaces it — the arm can only ever withhold a stall, never create
    /// one. <see cref="Available"/> false means the directory is missing or not a git repo; the
    /// transcript arm stands alone.
    /// </summary>
    internal sealed record WorkspaceArm(
        bool Available,
        DateTime? LastFileChangeAt,
        DateTime? LastCommitAt,
        bool SharedCheckout);

    internal static async Task<Verdict?> EvaluateAsync(
        AppDbContext db,
        AgentTask task,
        DateTime now,
        DelegationSettings settings,
        CancellationToken ct,
        WorkspaceArm? workspace = null)
    {
        var stall = settings.StallDetection;
        if (!stall.Enabled || stall.StallMinutes <= 0)
            return null;
        if (task.AgentSessionId is not Guid sessionId || task.DispatchedAt is not DateTime dispatched)
            return null;

        var elapsed = now - dispatched;
        if (elapsed < TimeSpan.FromMinutes(stall.StallMinutes))
            return null;

        if (!await SessionMessageQueueService.IsWorkingAsync(db, sessionId, ct))
            return null;

        var lookBack = TimeSpan.FromMinutes(Math.Max(stall.LookBackMinutes, stall.StallMinutes));
        var windowStart = now - lookBack;
        if (windowStart < dispatched)
            windowStart = dispatched;

        var rows = await db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId)
            .Select(t => new ProgressRow(
                t.Sequence,
                t.Kind,
                t.ToolName,
                t.ToolInput,
                t.Text,
                t.Timestamp ?? t.CreatedAt))
            .ToListAsync(ct);

        var inWindow = rows
            .Where(r => r.At >= windowStart)
            .OrderBy(r => r.At)
            .ThenBy(r => r.Sequence)
            .ToList();

        if (inWindow.Count < stall.MinRowsInWindow)
            return null;

        // Fingerprint from DispatchedAt, not just the look-back: lastProgressAt would otherwise
        // be capped at LookBackMinutes, and EscalateToErrorAfterMinutes (90) could never fire.
        var sinceDispatch = rows
            .Where(r => r.At >= dispatched)
            .OrderBy(r => r.At)
            .ThenBy(r => r.Sequence)
            .ToList();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        DateTime? lastNovelAt = null;
        string? lastNovelKind = null;
        foreach (var row in sinceDispatch)
        {
            if (!TryTakeProgress(row, seen, out var kind))
                continue;
            lastNovelAt = row.At;
            lastNovelKind = kind;
        }

        var lastProgressAt = lastNovelAt ?? dispatched;
        var fileNote = "no workspace arm";
        var filesTag = "files=none";
        var commitsTag = "commits=none";
        if (workspace is { Available: true } arm)
        {
            if (arm.LastFileChangeAt is DateTime fileAt && fileAt > lastProgressAt)
                lastProgressAt = fileAt;
            if (arm.LastCommitAt is DateTime commitAt && commitAt > lastProgressAt)
                lastProgressAt = commitAt;

            fileNote = DescribeWorkspace(arm, now);
            filesTag = arm.LastFileChangeAt is DateTime f
                ? $"files={Duration(now - f)}"
                : "files=none";
            commitsTag = arm.LastCommitAt is DateTime c
                ? $"commits={Duration(now - c)}"
                : "commits=none";
            if (arm.SharedCheckout)
                fileNote += " (shared checkout)";
        }

        var stalledFor = now - (lastProgressAt > dispatched ? lastProgressAt : dispatched);
        if (stalledFor < TimeSpan.Zero)
            stalledFor = TimeSpan.Zero;
        if (stalledFor < TimeSpan.FromMinutes(stall.StallMinutes))
            return null;

        var pending = await db.SessionQueuedMessages.AsNoTracking()
            .CountAsync(m => m.AgentSessionId == sessionId
                && m.Origin == QueuedMessageOrigin.Delegation
                && m.Status == QueuedMessageStatus.Pending, ct);

        var age = Duration(stalledFor);
        var summary =
            $"Working with no novel progress for {age}. "
            + $"{inWindow.Count} rows in the last {(int)lookBack.TotalMinutes}m, "
            + $"{seen.Count} distinct; last novel {lastNovelKind ?? "none"} {age} ago. "
            + fileNote + ".";
        if (pending > 0)
            summary += pending == 1 ? " 1 refinement waiting." : $" {pending} refinements waiting.";

        var failureReason =
            $"rows={inWindow.Count} distinct={seen.Count} lastNovel={lastNovelKind ?? "none"} "
            + $"age={age} {filesTag} {commitsTag}";

        return new Verdict(
            stalledFor, inWindow.Count, seen.Count, lastNovelKind, lastProgressAt,
            summary, failureReason);
    }

    /// <summary>
    /// A row is progress-bearing when its fingerprint has not been seen earlier in the window.
    /// UserPrompt / QueuedUserPrompt always count (somebody steered). Thinking and housekeeping
    /// never do.
    /// </summary>
    internal static bool TryTakeProgress(ProgressRow row, HashSet<string> seen, out string kind)
    {
        kind = row.Kind;
        if (row.Kind is TranscriptKinds.UserPrompt or TranscriptKinds.QueuedUserPrompt)
        {
            // Always novel: a repeated "continue" is still a steer. Sequence makes the hash unique
            // so a second identical prompt still resets the clock.
            seen.Add($"steer:{row.Kind}:{row.Sequence}");
            return true;
        }

        var fingerprint = FingerprintOf(row);
        if (fingerprint is null)
            return false;
        if (!seen.Add(fingerprint))
            return false;
        return true;
    }

    internal static string? FingerprintOf(ProgressRow row)
    {
        switch (row.Kind)
        {
            case TranscriptKinds.ToolCall:
                return Hash(row.Kind, Collapse(row.ToolName, 200) + "\n" + Collapse(row.ToolInput, 2_000));
            case TranscriptKinds.ToolResult:
                return Hash(row.Kind, Collapse(row.Text, 2_000));
            case TranscriptKinds.AssistantText:
                return Hash(row.Kind, Collapse(row.Text, 500));
            default:
                return null;
        }
    }

    private static string DescribeWorkspace(WorkspaceArm arm, DateTime now)
    {
        var parts = new List<string>();
        if (arm.LastFileChangeAt is DateTime fileAt)
            parts.Add($"last file change {Duration(now - fileAt)} ago");
        else
            parts.Add("no file changed");
        if (arm.LastCommitAt is DateTime commitAt)
            parts.Add($"last commit {Duration(now - commitAt)} ago");
        else
            parts.Add("no commit");
        return string.Join("; ", parts);
    }

    private static string Collapse(string? value, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        var collapsed = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length <= maxChars ? collapsed : collapsed[..maxChars];
    }

    private static string Hash(string kind, string payload)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(kind + "\n" + payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    internal static string Duration(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
            span = TimeSpan.Zero;
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}h{span.Minutes:00}m"
            : $"{(int)span.TotalMinutes}m";
    }
}
