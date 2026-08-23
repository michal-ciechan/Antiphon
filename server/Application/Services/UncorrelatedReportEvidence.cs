using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// One predicate for "this <see cref="AgentIncidentKind.DelegateReportUncorrelated"/> row is
/// evidence about THIS task" (CARD-0117). Shared by the recorder
/// (<c>AgentTaskReplyService.RecordUncorrelatedReportAsync</c>) and both readers
/// (<c>AgentTaskDispatcher.FailNeverStartedAsync</c> arm 2, <c>AttentionService</c> row 5) so the
/// three cannot disagree on scope.
///
/// <para>The 2026-08-21 miss: a ten-minute-old incident from an already-settled task answered a
/// question about a later task, so <c>FailNeverStartedAsync</c> took the mangled-brief arm while
/// that task's own brief sat <c>Pending</c>. The recorder's once-per-session dedup was the other
/// half of the same defect — once one incident existed, a genuinely uncorrelated report on a later
/// task was never written, and both readers went blind together.</para>
///
/// <para>The granularity this preserves: a stranded delegate ending turn after turn still produces
/// exactly one row for <em>that</em> task (every later turn-end finds the incident raised after
/// its <c>DispatchedAt</c>). A different, later task gets its own row — which is the finding, not
/// the noise. The alert's <c>DedupKey</c> (<c>delegation:uncorrelated:{task.Id}</c>) was already
/// per-task; this brings the incident row into line with the alert it fires.</para>
/// </summary>
internal static class UncorrelatedReportEvidence
{
    internal static bool IsEvidenceFor(AgentTask task, Guid? incidentSessionId, DateTime incidentCreatedAt)
        => incidentSessionId == task.AgentSessionId
           && (task.DispatchedAt is not DateTime d || incidentCreatedAt >= d);
}
