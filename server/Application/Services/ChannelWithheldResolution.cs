using System.Security.Cryptography;
using System.Text;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>The original incident stays in the audit trail. A second, idempotent incident points to its evidence.</summary>
internal static class ChannelWithheldResolution
{
    internal sealed class Candidate
    {
        public Guid IncidentId { get; init; }
        public Guid AgentId { get; init; }
        public Guid QueueId { get; init; }
    }

    internal static IQueryable<Candidate> Candidates(AppDbContext db)
    {
        return from a in db.AgentIncidents.AsNoTracking()
               from i in db.ChannelInbounds.Where(i => i.AgentId == a.AgentId && (i.Id == a.Id ||
                   (i.InputWithheldIncidentAt == a.CreatedAt && a.Message.StartsWith(
                       "Inbound message " + i.Provider + ":" + i.ConversationId + "/" + i.NativeMessageId + " is withheld:"))))
               join q in db.SessionQueuedMessages on i.QueueMessageId equals q.Id
               where a.Kind == AgentIncidentKind.ChannelReplyLost && a.FailureReason == ChannelBridgeService.WithheldReason
                   && a.AgentId != null && (a.SessionId == null || a.SessionId == q.AgentSessionId)
                   && q.Status == QueuedMessageStatus.Sent && q.Origin == QueuedMessageOrigin.Channel
                   && (q.DeliveryVerdict == DeliveryVerdict.Delivered || q.DeliveryVerdict == DeliveryVerdict.LateConfirmed)
                   && !db.AgentIncidents.Any(c => c.Kind == AgentIncidentKind.ChannelInputWithheldClosed
                       && c.FailureReason == a.Id.ToString())
               select new Candidate { IncidentId = a.Id, AgentId = a.AgentId.Value, QueueId = q.Id };
    }

    internal static Guid ClosureId(Guid incidentId) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes("channel-input-withheld-closed/" + incidentId.ToString("D"))).AsSpan(0, 16));

    internal static async Task<Guid?> CloseWithEvidenceAsync(AppDbContext db, Candidate candidate, long? requestedSequence,
        DateTime now, CancellationToken ct)
    {
        var row = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(q => q.Id == candidate.QueueId, ct);
        var floor = row.LastDeliveryBaselineSequence ?? 0;
        var query = db.TranscriptEntries.AsNoTracking().Where(p => p.AgentSessionId == row.AgentSessionId
            && p.Kind == TranscriptKinds.UserPrompt && p.Sequence > floor && p.Text != null);
        if (requestedSequence != null) query = query.Where(p => p.Sequence == requestedSequence);
        // The complete marker/body remains in the persisted prompt, including each member of a batch.
        var needle = ChannelPromptCorrelation.OpeningMarker(row.Body) ?? row.Body.ReplaceLineEndings("\n").Trim();
        if (needle.Length == 0) return null;
        var prompts = await query.Where(p => p.Text!.Contains(needle)).OrderBy(p => p.Sequence).ToListAsync(ct);
        var proof = prompts.FirstOrDefault(p => ChannelPromptCorrelation.Matches(row, p, TimeSpan.Zero, out _));
        if (proof == null) return null;
        var id = ClosureId(candidate.IncidentId);
        var message = $"Closed incident {candidate.IncidentId:D}; queueId={row.Id:D}, sessionId={row.AgentSessionId:D}, promptSequence={proof.Sequence}, transcriptEntryId={proof.Id:D}.";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AgentIncidents" ("Id","AgentId","SessionId","Kind","Severity","FailureReason","Message","CreatedAt")
            VALUES ({id},{candidate.AgentId},{row.AgentSessionId},{(int)AgentIncidentKind.ChannelInputWithheldClosed},
                    {(int)AlertSeverity.Info},{candidate.IncidentId.ToString()},{message},{now})
            ON CONFLICT ("Id") DO NOTHING
            """, ct);
        return id;
    }
}
