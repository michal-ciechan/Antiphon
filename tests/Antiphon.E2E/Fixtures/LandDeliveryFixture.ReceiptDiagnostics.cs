using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Application.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.E2E.Fixtures;

public sealed partial class LandDeliveryFixture
{
    internal async Task<AttentionItemDto> PollingAttentionAsync(AttentionDto attention, Guid requestId,
        Func<CancellationToken, Task<string>>? producerState = null)
    {
        // The feed collapses Outcome and Aged notices to the newest unconfirmed receipt per task.
        // Its headline therefore cannot identify the original Outcome; use the durable request.
        var matches = attention.Items.Where(i => i.TaskId == TaskId && i.LandRequestId == requestId
            && i.Kind == AttentionKind.LandOutcomeUnconfirmed).ToList();
        if (matches.Count == 1) return matches[0];
        var sequence = JsonSerializer.Serialize(attention.Items.Select(i => new {
            i.TaskId, i.LandRequestId, i.LandNotificationId, kind = i.Kind.ToString() }));
        var original = new InvalidOperationException(
            $"expected exactly one polling attention item; matches={matches.Count}; sequence={sequence}");
        var diagnostic = await ReceiptFailureDiagnosticAsync(original, LandNotificationKind.Outcome,
            requestId, null, producerState, "polling-attention");
        throw new InvalidOperationException(diagnostic, original);
    }

    // Diagnostic collection cannot extend the receipt deadline or replace its failure.
    internal async Task<TimeoutException> ReceiptTimeoutAsync(TimeoutException original, LandNotificationKind kind,
        Guid? requestId, Guid? notificationId, Func<CancellationToken, Task<string>>? producerState = null,
        string diagnosticPrefix = "receipt-timeout")
        => new(await ReceiptFailureDiagnosticAsync(original, kind, requestId, notificationId, producerState, diagnosticPrefix), original);

    private async Task<string> ReceiptFailureDiagnosticAsync(Exception original, LandNotificationKind kind,
        Guid? requestId, Guid? notificationId, Func<CancellationToken, Task<string>>? producerState,
        string diagnosticPrefix = "receipt-timeout")
    {
        var text = new StringBuilder(original.Message)
            .Append($" | kind={kind} taskId={TaskId} callerId={CallerId} requestId={requestId?.ToString() ?? "any-for-task"}")
            .Append($" notificationId={notificationId?.ToString() ?? "any-for-request"} evidenceRoot={Root}")
            .Append(" recipientBusyHeld=").Append(File.Exists(Path.Combine(Root, "caller-busy.held")) ? "true" : "false")
            .Append(" recipientBusyReleased=").Append(File.Exists(Path.Combine(Root, "caller-busy.release")) ? "true" : "false")
            .Append(" executionReleased=").Append(File.Exists(Path.Combine(Root, "execute.release")) ? "true" : "false");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            var capture = producerState is null
                ? CaptureReceiptProducerAsync(kind, requestId, deadline.Token)
                : producerState(deadline.Token);
            text.Append("\nproducerState=").Append(await capture.WaitAsync(deadline.Token));
        }
        catch (Exception ex) { text.Append("\nproducerState=unavailable:").Append(ex.GetType().Name); }
        try { text.Append('\n').Append(LastProtocolEvidence()); }
        catch (Exception ex) { text.Append("\nlastProtocolGit=unavailable:").Append(ex.GetType().Name); }
        foreach (var directory in new[] { Path.Combine(Root, "server-logs"), Path.Combine(Root, "child-logs"),
                     _app is null ? "" : _app.OwnedRunnerDirectory })
        {
            if (string.IsNullOrEmpty(directory)) continue;
            try
            {
                if (!Directory.Exists(directory)) { text.Append("\nlogDirectory=missing:").Append(directory); continue; }
                foreach (var log in Directory.GetFiles(directory, "*.log")
                             .OrderByDescending(File.GetLastWriteTimeUtc).Take(3))
                    text.Append("\nlogTail=").Append(log).Append('\n').Append(SafeLogTail(log));
            }
            catch (Exception ex) { text.Append("\nlogTail=unavailable:").Append(ex.GetType().Name).Append(" directory=").Append(directory); }
        }
        var path = Path.Combine(Root, diagnosticPrefix + "-" + (notificationId ?? requestId ?? TaskId).ToString("N") + ".txt");
        text.Append("\ndiagnosticPath=").Append(path);
        try { await File.WriteAllTextAsync(path, text.ToString()); }
        catch (Exception ex) { text.Append("\ndiagnosticWrite=unavailable:").Append(ex.GetType().Name); }
        return text.ToString();
    }

    private async Task<string> CaptureReceiptProducerAsync(LandNotificationKind kind, Guid? requestId, CancellationToken ct)
    {
        await using var db = CreateContext();
        // Explicit metadata projections: no body, prompt text, token, connection string or raw error.
        var requests = await db.AgentTaskLandRequests.AsNoTracking()
            .Where(r => r.TaskId == TaskId && (requestId == null || r.Id == requestId))
            .OrderByDescending(r => r.RequestedAt).Take(8)
            .Select(r => new { r.Id, r.State, r.IsPending, r.LandingOperationId, r.LastProgressAt,
                r.HighestProgress, r.Attempt, r.TerminalEventId, r.FailureDiagnosticId }).ToListAsync(ct);
        var operations = await db.AgentTaskLandings.AsNoTracking().Where(o => o.TaskId == TaskId)
            .OrderByDescending(o => o.CreatedAt).Take(8)
            .Select(o => new { o.Id, o.Phase, o.Publication, o.Cleanup, o.VerificationStartedAt,
                o.VerificationPassed, o.VerifiedAt, o.PushStartedAt, o.PushExitCode, o.ChildProcessId,
                o.ChildProcessStartTicks, o.UpdatedAt }).ToListAsync(ct);
        var notifications = await db.AgentTaskLandNotifications.AsNoTracking()
            .Where(n => n.TaskId == TaskId && n.Kind == kind && (requestId == null || n.RequestId == requestId))
            .OrderByDescending(n => n.CreatedAt).Take(8)
            .Select(n => new { n.Id, n.RequestId, n.Kind, n.State, n.QueueMessageId, n.EnqueueAttempts,
                n.EnqueuedAt, n.NextAttemptAt, n.ConfirmedAt, n.ConfirmingPromptSequence }).ToListAsync(ct);
        var queue = await db.SessionQueuedMessages.AsNoTracking().Where(m => m.SourceTaskId == TaskId)
            .OrderByDescending(m => m.CreatedAt).Take(8)
            .Select(m => new { m.Id, m.AgentSessionId, m.Status, m.DeliveryAttempts, m.SentAt,
                m.LastDeliveryStartedAt, m.LastDeliveryBaselineSequence }).ToListAsync(ct);
        var transcript = await db.TranscriptEntries.AsNoTracking().Where(p => p.AgentSessionId == CallerId)
            .OrderByDescending(p => p.Sequence).Take(8)
            .Select(p => new { p.Sequence, p.Kind, p.Timestamp }).ToListAsync(ct);
        var events = await db.AgentTaskEvents.AsNoTracking().Where(e => e.AgentTaskId == TaskId)
            .OrderByDescending(e => e.At).Take(8)
            .Select(e => new { e.Id, e.Type, e.At, e.LandRequestId, e.LandingOperationId }).ToListAsync(ct);
        return JsonSerializer.Serialize(new { requests, operations, notifications, queue, transcript, events },
            new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } });
    }

    private string LastProtocolEvidence()
    {
        var path = Directory.GetFiles(Root, "protocol-git-*.json")
            .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        if (path is null) return "lastProtocolGit=none";
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var data = json.RootElement;
        var command = data.GetProperty("arguments")[0].GetString();
        // Arguments can contain remote URLs. Emit only a recognized command name.
        if (command is not ("push" or "fetch" or "rev-parse" or "diff-tree" or "ls-tree" or "worktree"
            or "merge-base" or "merge" or "rebase" or "status" or "show-ref" or "update-ref" or "branch"
            or "remote" or "config" or "check-ref-format" or "log" or "cat-file" or "diff")) command = "other";
        return $"lastProtocolGit={path} gitCommand={command} exit={data.GetProperty("ExitCode").GetInt32()}"
            + $" at={data.GetProperty("at").GetDateTime():O} pid={data.GetProperty("pid").GetInt32()}";
    }

    private static string SafeLogTail(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        stream.Seek(Math.Max(0, stream.Length - 16_384), SeekOrigin.Begin);
        using var reader = new StreamReader(stream);
        return string.Join('\n', reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries).TakeLast(8).Select(line => {
            // Keep timestamp/level and a fixed category. Never echo arbitrary log messages or exceptions.
            var header = Regex.Match(line, @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2} \[(?:INF|WRN|ERR|DBG|VRB|FTL)\]").Value;
            var category = line.Contains("BUILD SLOT waiting", StringComparison.Ordinal) ? "build-slot-waiting"
                : line.Contains("BUILD SLOT granted", StringComparison.Ordinal) ? "build-slot-granted"
                : line.Contains("Landing verifier joined", StringComparison.Ordinal) ? "land-verifier-joined"
                : line.Contains("Landing verifier", StringComparison.Ordinal) ? "land-verifier"
                : "other-content-omitted";
            return header + " category=" + category;
        }));
    }
}
