using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;

namespace Antiphon.Server.Infrastructure.Data;

internal static class SessionRuntimeActionClaimQuery
{
    internal static IQueryable<SessionRuntimeAction> Query(AppDbContext db, Guid sessionId, DateTime now)
    {
        const string ChannelReplyDebt = SessionRuntimeActionCoordinator.ChannelReplyDebt;
        const int QueuePhase = SessionRuntimeActionCoordinator.QueuePhase;
        return db.SessionRuntimeActions.Where(a => a.AgentSessionId == sessionId
                    && (a.TriggerKind != ChannelReplyDebt || (a.DueAt <= now
                        && !db.SessionRuntimeActions.Any(b => b.AgentSessionId == sessionId
                            && b.TriggerKind == ChannelReplyDebt && b.Sequence < a.Sequence
                            && (b.ConversationKey == null || a.ConversationKey == null
                                || b.ConversationKey == a.ConversationKey
                                || db.SessionQueuedMessages.Any(m => m.AgentSessionId == sessionId
                                    && m.ConversationKey != null
                                    && ((a.LateConfirmedChannelMessageIds.Contains(m.Id)
                                            && m.ConversationKey == b.ConversationKey)
                                        || (b.LateConfirmedChannelMessageIds.Contains(m.Id)
                                            && m.ConversationKey == a.ConversationKey)
                                        || (a.LateConfirmedChannelMessageIds.Contains(m.Id)
                                            && db.SessionQueuedMessages.Any(n => n.AgentSessionId == sessionId
                                                && b.LateConfirmedChannelMessageIds.Contains(n.Id)
                                                && n.ConversationKey == m.ConversationKey)))))))))
                .OrderBy(a => a.Phase >= QueuePhase).ThenBy(a => a.Sequence).ThenBy(a => a.TriggerKind)
                .Take(1);
    }
}
