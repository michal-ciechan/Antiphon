using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Review.Workflows;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Review;

public sealed class ReviewWorkflowPolicyService(
    AppDbContext db,
    ReviewWorkflowService workflows,
    IOptions<ReviewPolicySettings> policy)
{
    public async Task<WorkflowPolicyDto> GetAsync(Guid runId, Guid? revisionId, CancellationToken ct)
    {
        var run = await db.ReviewRuns.AsNoTracking()
            .Where(run => run.Id == runId)
            .Select(run => new { run.Subject.Repo, run.WorkflowRevisionId })
            .SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("Review run", runId);

        var selected = revisionId ?? run.WorkflowRevisionId;
        var definition = workflows.Default;
        var name = definition.Name;
        var hash = workflows.DefaultHash;
        if (selected is { } id)
        {
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
}
