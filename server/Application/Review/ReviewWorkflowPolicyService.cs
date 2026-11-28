using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Review.Workflows;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Review;

public sealed class ReviewWorkflowPolicyService(
    AppDbContext db,
    ReviewWorkflowService workflows,
    ReviewViewer viewer,
    IOptions<ReviewPolicySettings> policy)
{
    public async Task<WorkflowPolicyDto> GetAsync(Guid runId, Guid? revisionId, CancellationToken ct)
    {
        var run = await db.ReviewRuns.AsNoTracking()
            .Where(run => run.Id == runId)
            .Select(run => new { run.Subject.Host, run.Subject.Repo, run.WorkflowRevisionId })
            .SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("Review run", runId);

        if (viewer.Repos is not null && (run.Host != ReviewHost.GitHub || !viewer.MayDecideOn(run.Repo)))
            throw RepositoryScopeDenied();

        var selected = revisionId ?? run.WorkflowRevisionId;
        var definition = workflows.Default;
        var name = definition.Name;
        var hash = workflows.DefaultHash;
        if (selected is { } id)
        {
            if (id != run.WorkflowRevisionId)
                await RequireRevisionScopeAsync(id, ct);

            var revision = await db.ReviewWorkflowRevisions.AsNoTracking().SingleOrDefaultAsync(revision => revision.Id == id, ct)
                ?? throw new NotFoundException("Review workflow revision", id);
            definition = ReviewWorkflowService.FromStoredGraph(revision.GraphJson);
            name = revision.Name;
            hash = revision.Hash;
        }

        var nodes = definition.Nodes
            .Select(node => ReviewWorkflowPolicy.ForNode(policy.Value, node, run.Repo))
            .ToArray();
        return new(runId, selected, name, hash, nodes);
    }

    private async Task RequireRevisionScopeAsync(Guid revisionId, CancellationToken ct)
    {
        if (viewer.Repos is null)
            return;

        // The same immutable revision can be pinned by runs in several repos.
        var repos = await db.ReviewRuns.AsNoTracking()
            .Where(run => run.WorkflowRevisionId == revisionId && run.Subject.Host == ReviewHost.GitHub)
            .Select(run => run.Subject.Repo)
            .Distinct()
            .ToListAsync(ct);
        if (!repos.Any(viewer.MayDecideOn))
            throw RepositoryScopeDenied();
    }

    private static ForbiddenException RepositoryScopeDenied() =>
        new("The workflow policy is outside your signed-in repository scope.", "review_policy_repo_scope");
}
