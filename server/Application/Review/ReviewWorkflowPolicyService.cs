using Antiphon.Server.Application.Review.Workflows;
using Antiphon.Server.Application.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Review;

public sealed class ReviewWorkflowPolicyService(
    ReviewReadScope reads,
    ReviewWorkflowService workflows,
    IOptions<ReviewPolicySettings> policy)
{
    public async Task<WorkflowPolicyDto> GetAsync(Guid runId, Guid? revisionId, CancellationToken ct)
    {
        var run = await reads.Runs
            .Where(run => run.Id == runId)
            .Select(run => new { run.Subject.Repo, run.WorkflowRevisionId })
            .SingleOrDefaultAsync(ct)
            ?? throw reads.RunNotFound(runId);

        var selected = revisionId ?? run.WorkflowRevisionId;
        var definition = workflows.Default;
        var name = definition.Name;
        var hash = workflows.DefaultHash;
        if (selected is { } id)
        {
            var revision = await reads.GetRevisionAsync(id, ct);
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
