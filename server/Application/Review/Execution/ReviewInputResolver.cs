using System.Text.Json;
using Antiphon.Server.Application.Review.Workflows;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Review.Execution;

/// <summary>Explicit inputs use the latest preceding top-level row of each declared node in this iteration.</summary>
internal sealed class ReviewInputResolver(ReviewWalk walk, ReviewNodeExecution consumer)
{
    private IEnumerable<ReviewNodeExecution> Preceding => walk.Run.Nodes
        .Where(row => row.Iteration == consumer.Iteration && row.ItemKey == null && row.Sequence < consumer.Sequence);

    public ReviewNodeExecution? Latest(string? nodeId) => nodeId is null
        ? null
        : Preceding.Where(row => row.NodeId == nodeId).MaxBy(row => row.Sequence);

    public ReviewNodeExecution? LatestOf(string step)
    {
        var ids = walk.NodesOf(step).ToHashSet(StringComparer.Ordinal);
        return Preceding.Where(row => ids.Contains(row.NodeId)).MaxBy(row => row.Sequence);
    }

    public static T? Read<T>(ReviewNodeExecution? row) where T : class
    {
        if (row?.Status is not (ReviewNodeStatus.Succeeded or ReviewNodeStatus.Skipped or ReviewNodeStatus.FailedContinued))
            return null;

        try
        {
            return ReviewWalk.Output<T>(row);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

internal sealed record ReviewPublishInputs(CollateOutput Collate, ReviewNodeExecution VerifyRow, VerifyOutput Verify,
    CategorisationOutcome? Categorisation, SelectOutput? Selection)
{
    public static bool TryResolve(ReviewWalk walk, ReviewNodeExecution consumer, ReviewPublishConfiguration configuration,
        out ReviewPublishInputs? inputs, out string? failure)
    {
        inputs = null;
        var resolver = new ReviewInputResolver(walk, consumer);
        var collateRow = resolver.Latest(configuration.Collate);
        var verifyRow = resolver.Latest(configuration.Verify);
        var collate = ReviewInputResolver.Read<CollateOutput>(collateRow);
        var verify = ReviewInputResolver.Read<VerifyOutput>(verifyRow);

        if (collateRow is null || collate?.Candidates is null || collate.Candidates.Any(c =>
                c is null || string.IsNullOrEmpty(c.Cid) || c.Check is null || c.File is null
                    || c.Anchor is null || c.Title is null || c.Body is null || c.Flags is null || !Enum.IsDefined(c.Severity)))
        {
            failure = "Publish requires a usable declared collate result.";
            return false;
        }
        if (verifyRow is null || verify?.Kept is null || verify.Dropped is null)
        {
            failure = "Publish requires a usable declared verify result.";
            return false;
        }
        if (new ReviewInputResolver(walk, verifyRow).LatestOf(ReviewPublishConfiguration.CollateStep)?.Id != collateRow.Id)
        {
            failure = "The declared collate result was not the input to the declared verification.";
            return false;
        }

        var candidateIds = collate.Candidates.Select(c => c.Cid).ToHashSet(StringComparer.Ordinal);
        if (!candidateIds.IsSupersetOf(verify.Kept))
        {
            failure = "The declared verification kept IDs outside its collate result.";
            return false;
        }

        var categorisation = ReviewInputResolver.Read<CategorisationOutcome>(resolver.Latest(configuration.Categorise));
        var selection = ReviewInputResolver.Read<SelectOutput>(resolver.Latest(configuration.Select));
        inputs = new(collate, verifyRow, verify,
            categorisation is { ChangeKind: not null, Categories: not null } ? categorisation : null,
            selection?.Candidates is { } candidates && candidates.All(c => c is { Id: not null, Title: not null }) ? selection : null);
        failure = null;
        return true;
    }
}
