using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

/// <summary>The continuation decision is stored on the synthetic channel restart boundary.</summary>
internal static class ChannelRestartContinuation
{
    internal const string BoundaryUuidPrefix = "channel-generation/";
    internal const string Due = "channel-resume-continue-due";
    internal const string Queued = "channel-resume-continue-queued";

    internal static IQueryable<TranscriptEntry> Pending(AppDbContext db) =>
        db.TranscriptEntries.Where(t => t.Kind == TranscriptKinds.SessionRestartBoundary
            && t.StopReason == Due && t.Uuid != null && t.Uuid.StartsWith(BoundaryUuidPrefix));
}
