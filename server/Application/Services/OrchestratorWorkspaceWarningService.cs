using System.Security.Cryptography;
using System.Text;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0251 S3: raise one Warning incident when a declared orchestrator launches
/// from a non-Dedicated workspace. Detection only — the launch already proceeded.
/// </summary>
public sealed class OrchestratorWorkspaceWarningService
{
    public const string FingerprintPrefix = "fp=";

    private readonly AppDbContext _db;
    private readonly OrchestratorWorkspaceFactGatherer _gatherer;
    private readonly TimeProvider _time;
    private readonly ILogger<OrchestratorWorkspaceWarningService> _logger;

    public OrchestratorWorkspaceWarningService(
        AppDbContext db,
        OrchestratorWorkspaceFactGatherer gatherer,
        TimeProvider time,
        ILogger<OrchestratorWorkspaceWarningService> logger)
    {
        _db = db;
        _gatherer = gatherer;
        _time = time;
        _logger = logger;
    }

    public Task MaybeRaiseForStandingAgentAsync(
        Agent agent, Guid? sessionId, CancellationToken ct) =>
        MaybeRaiseAsync(agent, sessionId, declaredByTaskKind: false, ct);

    public Task MaybeRaiseForOrchestratorTaskAsync(
        Agent agent, Guid? sessionId, CancellationToken ct) =>
        MaybeRaiseAsync(agent, sessionId, declaredByTaskKind: true, ct);

    internal static string Fingerprint(string cwd, OrchestratorWorkspaceState state, OrchestratorWorkspaceCli cli)
    {
        var payload = $"{cwd}|{state}|{cli}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash)[..8].ToLowerInvariant();
    }

    private async Task MaybeRaiseAsync(
        Agent agent, Guid? sessionId, bool declaredByTaskKind, CancellationToken ct)
    {
        try
        {
            if (!declaredByTaskKind && !await HasOrchestratorBundleAsync(agent.Id, ct))
                return;

            var project = await ResolveProjectAsync(agent, ct);
            if (project?.OrchestratorWorkspaceAcknowledgedAt is not null)
                return;

            var cwd = agent.WorkingDirectory;
            if (string.IsNullOrWhiteSpace(cwd))
                return;

            var cli = OrchestratorWorkspaceLayout.CliFromKind(agent.Kind);
            var facts = await _gatherer.GatherAsync(cwd, cli, ct);
            var home = _gatherer.ReadHomeFromProfile(cwd);
            var state = OrchestratorWorkspaceLayout.Classify(facts, cli, home);
            if (state == OrchestratorWorkspaceState.Dedicated)
                return;

            var fp = Fingerprint(Path.GetFullPath(cwd), state, cli);
            var key = FingerprintPrefix + fp;
            var exists = await _db.AgentIncidents.AsNoTracking().AnyAsync(
                i => i.AgentId == agent.Id
                    && i.Kind == AgentIncidentKind.OrchestratorWorkspaceUnconfigured
                    && i.FailureReason == key,
                ct);
            if (exists)
                return;

            var sibling = OrchestratorWorkspaceLayout.ProposedSiblingPath(cwd);
            var message =
                $"Orchestrator '{agent.Name}' is launching from {state} at {cwd.Replace('\\', '/')} "
                + $"(not a dedicated sibling workspace). Preview with scripts/orchestrator-workspace.ps1 plan. "
                + $"Proposed sibling: {sibling}. {key}";

            _db.AgentIncidents.Add(new AgentIncident
            {
                Id = Guid.NewGuid(),
                AgentId = agent.Id,
                SessionId = sessionId,
                Kind = AgentIncidentKind.OrchestratorWorkspaceUnconfigured,
                Severity = AlertSeverity.Warning,
                Message = Truncate(message, AgentIncident.MessageMaxLength),
                FailureReason = key,
                CreatedAt = _time.GetUtcNow().UtcDateTime,
            });
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Orchestrator workspace warning failed for agent {AgentId}; launch is unaffected",
                agent.Id);
        }
    }

    private async Task<bool> HasOrchestratorBundleAsync(Guid agentId, CancellationToken ct)
    {
        var keys = await AgentBundleAttachments.LoadAsync(_db, agentId, _logger, ct);
        return keys.Contains(InstructionBundles.Orchestrator, StringComparer.Ordinal);
    }

    private async Task<Project?> ResolveProjectAsync(Agent agent, CancellationToken ct)
    {
        if (agent.BoardId is Guid boardId)
        {
            var board = await _db.Boards.AsNoTracking()
                .Select(b => new { b.Id, b.ProjectId })
                .FirstOrDefaultAsync(b => b.Id == boardId, ct);
            if (board is not null)
            {
                return await _db.Projects.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == board.ProjectId, ct);
            }
        }

        if (string.IsNullOrWhiteSpace(agent.WorkingDirectory))
            return null;
        var normalized = DelegationWorkspaceResolver.NormalizeSeparators(agent.WorkingDirectory)
            .ToLowerInvariant();
        return await _db.Projects.AsNoTracking()
            .FirstOrDefaultAsync(
                p => p.LocalRepositoryPath != null
                    && p.LocalRepositoryPath.Replace("/", "\\").ToLower() == normalized,
                ct);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
