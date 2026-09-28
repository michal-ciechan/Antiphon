using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Agents;

/// <summary>Read-only, current observations used by the expectation sweep.</summary>
public sealed class ExpectationObservationAdapter(
    AppDbContext db,
    PhoneHomeRunnerDirectory directory,
    SubscriptionUsageReader usage,
    IModelAvailability models,
    AgentTuiRunnerCatalog tui,
    IOptions<SubscriptionQuotaGateSettings> quota,
    TimeProvider time,
    AgentFilesService? files = null,
    RepositoryChildJournalInspector? journalInspector = null)
{
    public Task<ExpectationReferenceCatalog> ReferencesAsync(CancellationToken ct) => ReferencesAsync(null, ct);

    public async Task<ExpectationReferenceCatalog> ReferencesAsync(ExpectationDirectiveSettings? directive,
        CancellationToken ct)
    {
        var cardQuery = db.Cards.AsNoTracking();
        var agentQuery = db.Agents.AsNoTracking();
        var channelQuery = db.ChatChannels.AsNoTracking();
        if (directive is not null)
        {
            cardQuery = cardQuery.Where(card => card.Id == directive.AuditCardId);
            agentQuery = agentQuery.Where(agent => agent.Id == directive.AgentId);
            channelQuery = channelQuery.Where(channel => channel.Id == directive.OperatorChannelId);
        }
        var cards = await cardQuery.Select(c => new { c.Id, c.BoardId }).ToListAsync(ct);
        var agents = await agentQuery
            .Select(a => new { a.Id, a.BoardId, a.IsPoolDelegate }).ToListAsync(ct);
        var channels = await channelQuery
            .Select(c => new { c.Id, c.Enabled }).ToListAsync(ct);
        return new ExpectationReferenceCatalog(
            cards.ToDictionary(c => c.Id, c => c.BoardId),
            agents.ToDictionary(a => a.Id, a => new ExpectationAgentReference(a.BoardId, a.IsPoolDelegate)),
            channels.ToDictionary(c => c.Id, c => c.Enabled),
            directory.KnownRunnerIds.ToHashSet(StringComparer.Ordinal));
    }

    public async Task<ExpectationProbeInput> ObserveAsync(ExpectationDirectiveSettings directive, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var runners = new Dictionary<string, ExpectationRunnerProbe>(StringComparer.Ordinal);
        foreach (var target in directive.Targets)
        {
            var key = ExpectationSubjects.RunnerKey(target.RunnerId);
            try
            {
                var descriptor = await directory.DescribeAsync(target.RunnerId, ct);
                runners[key] = descriptor is null
                    ? new ExpectationRunnerProbe(null, now, "runner not configured")
                    : new ExpectationRunnerProbe(descriptor.Available, now,
                        descriptor.DispatchEligible ? "dispatch eligible" : "dispatch unavailable");
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                runners[key] = new ExpectationRunnerProbe(null, now, "probe timeout");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                runners[key] = new ExpectationRunnerProbe(null, now, ex.GetType().Name);
            }
        }

        var candidates = new List<ExpectationCandidateProbe>();
        foreach (var target in directive.Targets)
        {
            foreach (var candidate in target.Candidates)
            {
                try
                {
                    var sample = await usage.GetLatestAsync(candidate.AgentKind, candidate.SubscriptionKey, ct);
                    var alias = tui.MapLegacyModel(candidate.AgentKind, candidate.ModelLevel);
                    bool? held = alias is null ? null : await models.IsHeldAsync(candidate.AgentKind, alias, ct);
                    candidates.Add(new ExpectationCandidateProbe(target.RunnerId, candidate.AgentKind,
                        candidate.ModelLevel, candidate.SubscriptionKey, sample, sample is not null, held));
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch
                {
                    candidates.Add(new ExpectationCandidateProbe(target.RunnerId, candidate.AgentKind,
                        candidate.ModelLevel, candidate.SubscriptionKey, null, false, null));
                }
            }
        }

        var projectId = await db.Boards.AsNoTracking().Where(board => board.Id == directive.BoardId)
            .Select(board => (Guid?)board.ProjectId).SingleOrDefaultAsync(ct);
        var cardIds = await db.Cards.AsNoTracking().Where(card => card.BoardId == directive.BoardId)
            .Select(card => card.Id).ToListAsync(ct);
        var ownedSessions = await db.AgentSessions.AsNoTracking()
            .Where(session => session.StandingAgentId == directive.AgentId)
            .Select(session => session.Id).ToListAsync(ct);
        var active = await db.AgentTasks.AsNoTracking()
            .Where(task => (task.Status == AgentTaskStatus.Dispatched || task.Status == AgentTaskStatus.Working)
                && task.AgentSessionId != null
                && (task.CardId != null && cardIds.Contains(task.CardId.Value)
                    || task.CardId == null && projectId != null && task.ProjectId == projectId
                        && task.ParentSessionId != null && ownedSessions.Contains(task.ParentSessionId.Value)))
            .Select(task => new { task.AgentSessionId, task.RunnerId })
            .Distinct().ToListAsync(ct);
        var activeIds = active.Select(task => task.AgentSessionId!.Value).Distinct().ToList();
        var generations = await db.AgentSessions.AsNoTracking()
            .Where(session => activeIds.Contains(session.Id))
            .Select(session => new { session.Id, session.StartedAt })
            .ToDictionaryAsync(session => session.Id, session => session.StartedAt, ct);
        var sessions = new Dictionary<Guid, ExpectationSessionProbe>();
        foreach (var task in active)
        {
            var sessionId = task.AgentSessionId!.Value;
            var key = ExpectationSubjects.RunnerKey(task.RunnerId);
            if (runners.TryGetValue(key, out var availability) && availability.Available != true)
            {
                sessions[sessionId] = new ExpectationSessionProbe(null, now, "runner unobserved");
                continue;
            }
            try
            {
                var live = await directory.Resolve(task.RunnerId).GetAsync(sessionId, ct);
                if (!generations.TryGetValue(sessionId, out var persisted)
                    || live.AcceptedStartedAt is not { } accepted
                    || !SessionGeneration.Equal(SessionGeneration.Normalize(persisted), accepted))
                {
                    sessions[sessionId] = new ExpectationSessionProbe(null, now,
                        "session generation unobserved or different");
                    continue;
                }
                sessions[sessionId] = new ExpectationSessionProbe(
                    live.Status.Equals("running", StringComparison.OrdinalIgnoreCase)
                        || live.Status.Equals("starting", StringComparison.OrdinalIgnoreCase)
                        || live.Status.Equals("stopping", StringComparison.OrdinalIgnoreCase),
                    now, live.Status);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                sessions[sessionId] = new ExpectationSessionProbe(null, now, "session probe timeout");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                sessions[sessionId] = new ExpectationSessionProbe(null, now, ex.GetType().Name);
            }
        }

        var workspace = new Dictionary<Guid, Antiphon.Server.Application.Dtos.WorkspaceProgressArm>();
        if (files is not null)
        {
            var local = await db.AgentTasks.AsNoTracking()
                .Where(task => (task.Status == AgentTaskStatus.Dispatched || task.Status == AgentTaskStatus.Working)
                    && task.DispatchedAt != null && (task.RunnerId == null || task.RunnerId == "")
                    && (task.CardId != null && cardIds.Contains(task.CardId.Value)
                        || task.CardId == null && projectId != null && task.ProjectId == projectId
                            && task.ParentSessionId != null && ownedSessions.Contains(task.ParentSessionId.Value)))
                .Select(task => new { task.Id, task.WorkingDirectory, task.DispatchedAt, task.Workspace })
                .ToListAsync(ct);
            foreach (var task in local)
            {
                if (task.DispatchedAt is not { } dispatched || now - dispatched < TimeSpan.FromMinutes(10))
                    continue;
                try
                {
                    workspace[task.Id] = await files.ProbeProgressAsync(task.WorkingDirectory, dispatched,
                        task.Workspace == WorkspaceMode.Shared, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch { /* A failed workspace read is unknown; transcript and other subjects remain usable. */ }
            }
        }

        var journals = new Dictionary<string, ExpectationJournalProbe>(StringComparer.Ordinal);
        if (journalInspector is not null)
        {
            var repositoryPaths = await db.AgentTasks.AsNoTracking()
                .Where(task => task.Status == AgentTaskStatus.Queued && task.RepoPath != null
                    && (task.CardId != null && cardIds.Contains(task.CardId.Value)
                        || task.CardId == null && projectId != null && task.ProjectId == projectId
                            && task.ParentSessionId != null && ownedSessions.Contains(task.ParentSessionId.Value)))
                .Select(task => task.RepoPath!).Distinct().ToListAsync(ct);
            var persisted = await db.AgentTaskLandRequests.AsNoTracking()
                .Where(request => request.IsPending && request.RepositoryPathSnapshot != null
                    && request.SourceCommonDirectory != null
                    && repositoryPaths.Contains(request.RepositoryPathSnapshot))
                .Select(request => new { request.RepositoryPathSnapshot, request.SourceCommonDirectory })
                .Distinct().ToListAsync(ct);
            foreach (var row in persisted)
            {
                var scope = ExpectationRepository.For(row.RepositoryPathSnapshot, directive.BoardId);
                if (journals.ContainsKey(scope)) continue;
                try
                {
                    var inspection = await journalInspector.InspectCommonAsync(row.SourceCommonDirectory!,
                        TimeSpan.FromMinutes(5), time.GetUtcNow(), ct);
                    journals[scope] = new ExpectationJournalProbe(
                        inspection.Findings.Count(f => f.State == JournalRecordState.Alive),
                        inspection.Findings.Count(f => f.State == JournalRecordState.Dead),
                        inspection.Findings.Count(f => f.State is JournalRecordState.Unknown or JournalRecordState.Malformed),
                        now, "read-only persisted common directory");
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    journals[scope] = new ExpectationJournalProbe(0, 0, 1, now,
                        "journal inspection unavailable: " + ex.GetType().Name);
                }
            }
        }

        return new ExpectationProbeInput(quota.Value, candidates, runners, false, null, sessions, workspace, journals);
    }
}
