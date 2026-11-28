using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Review;

/// <summary>Repository grants for review data requested by run or revision ID.</summary>
public sealed class ReviewReadScope(AppDbContext db, ReviewViewer viewer, IOptions<ReviewSettings> settings)
{
    public IQueryable<ReviewRun> Runs
    {
        get
        {
            var runs = db.ReviewRuns.AsNoTracking();
            if (!settings.Value.Auth.IsGitHub || viewer.Repos is not { } grants)
                return runs;

            // GitHub owner and repository names use ASCII and compare case-insensitively.
            var repos = grants.Select(repo => repo.ToLowerInvariant()).ToArray();
            return runs.Where(run => run.Subject.Host == ReviewHost.GitHub && repos.Contains(EF.Functions.Collate(run.Subject.Repo, "C").ToLower()));
        }
    }

    public async Task RequireRunAsync(Guid id, CancellationToken ct)
    {
        if (!await Runs.AnyAsync(run => run.Id == id, ct))
            throw RunNotFound(id);
    }

    public Task RequireContextPackRunAsync(Guid id, CancellationToken ct) =>
        settings.Value.Auth.IsGitHub ? RequireRunAsync(id, ct) : Task.CompletedTask;

    public async Task<ReviewWorkflowRevision> GetRevisionAsync(Guid id, CancellationToken ct)
    {
        var revisions = db.ReviewWorkflowRevisions.AsNoTracking().Where(revision => revision.Id == id);
        if (settings.Value.Auth.IsGitHub && viewer.Repos is not null)
        {
            // An immutable revision can be referenced from several repositories.
            var runs = Runs;
            revisions = revisions.Where(revision => runs.Any(run => run.WorkflowRevisionId == revision.Id));
        }

        return await revisions.SingleOrDefaultAsync(ct) ?? throw NotFound("Review workflow revision", id);
    }

    public NotFoundException RunNotFound(Guid id) => NotFound("Review run", id);

    private NotFoundException NotFound(string entity, Guid id) => settings.Value.Auth.IsGitHub
        ? new MaskedNotFoundException(entity, id)
        : new NotFoundException(entity, id);
}
