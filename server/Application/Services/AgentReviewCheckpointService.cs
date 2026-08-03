using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Records "work completed up to here" checkpoints for an agent: the workspace HEAD commit plus a
/// timestamp, captured when a card is completed or the user sets a baseline explicitly. The Files
/// review surface diffs against the latest checkpoint by default ("what changed since I signed
/// off"), which matters because self-committing agents leave the working tree clean — a plain
/// diff-vs-HEAD is empty moments after every commit.
/// </summary>
public sealed class AgentReviewCheckpointService
{
    private readonly AppDbContext _db;
    private readonly GitWorkspaceService _git;
    private readonly ILogger<AgentReviewCheckpointService> _logger;

    public AgentReviewCheckpointService(
        AppDbContext db, GitWorkspaceService git, ILogger<AgentReviewCheckpointService> logger)
    {
        _db = db;
        _git = git;
        _logger = logger;
    }

    /// <summary>
    /// Capture a checkpoint for the agent (HEAD sha when the workspace is a repo, else timestamp
    /// only). Best-effort: failures are logged, never thrown — completing a card must not break
    /// on git problems.
    /// </summary>
    public async Task<AgentReviewCheckpoint?> CaptureAsync(Guid agentId, string reason, CancellationToken ct)
    {
        try
        {
            var agent = await _db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == agentId, ct);
            if (agent is null || string.IsNullOrWhiteSpace(agent.WorkingDirectory))
                return null;

            var sha = await _git.GetHeadShaAsync(Path.GetFullPath(agent.WorkingDirectory), ct);
            var checkpoint = new AgentReviewCheckpoint
            {
                Id = Guid.NewGuid(),
                AgentId = agentId,
                CommitSha = sha,
                Reason = reason.Length > 500 ? reason[..500] : reason,
                CreatedAt = DateTime.UtcNow,
            };
            _db.AgentReviewCheckpoints.Add(checkpoint);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "Review checkpoint for agent {AgentId}: {Reason} at commit {Sha}",
                agentId, reason, sha ?? "(none)");
            return checkpoint;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Capturing review checkpoint for agent {AgentId} failed", agentId);
            return null;
        }
    }

    public Task<AgentReviewCheckpoint?> GetLatestAsync(Guid agentId, CancellationToken ct) =>
        _db.AgentReviewCheckpoints.AsNoTracking()
            .Where(c => c.AgentId == agentId)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);
}
