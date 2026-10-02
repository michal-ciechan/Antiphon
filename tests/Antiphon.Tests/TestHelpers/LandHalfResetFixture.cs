using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Update;

namespace Antiphon.Tests.TestHelpers;

internal sealed class LandHalfResetFixture : IAsyncDisposable
{
    public LandingSafetyHarness Harness { get; } = new();
    public SaveCut Interceptor { get; } = new();
    public RefMovedBoundary Boundary { get; }

    public LandHalfResetFixture()
    {
        Boundary = new RefMovedBoundary(Interceptor);
        Harness.Boundary = Boundary;
        Harness.LandCutInterceptor = Interceptor;
    }

    public async Task<(string Local, string Reviewed, Guid Evidence)> SeedReviewedDescendantAsync()
    {
        var h = Harness;
        await h.InitializeAsync();
        var local = await h.AddSourceAsync();
        await h.Fixture.RequiredAsync(h.Fixture.Source, "push", "origin", h.Fixture.SourceRef);
        var reviewedTree = Path.Combine(h.Fixture.Root, "trees", "reviewed");
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "worktree", "add", "--detach", reviewedTree, local);
        await File.WriteAllTextAsync(Path.Combine(reviewedTree, "feature.txt"), "reviewed feature\n");
        await h.Fixture.RequiredAsync(reviewedTree, "add", ".");
        await h.Fixture.RequiredAsync(reviewedTree, "commit", "-m", "reviewed descendant");
        var reviewed = (await h.Fixture.RequiredAsync(reviewedTree, "rev-parse", "HEAD")).Trim();
        await h.Fixture.RequiredAsync(reviewedTree, "push", "origin", $"HEAD:{h.Fixture.SourceRef}");
        Guid evidence;
        await using (var db = h.CreateContext())
        {
            var owner = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
            owner.Status = Antiphon.Server.Domain.Enums.AgentTaskStatus.Failed;
            var row = new StageOutcome
            {
                Id = Guid.NewGuid(), Stage = Antiphon.Server.Domain.Enums.OrchestrationStage.Review,
                Outcome = Antiphon.Server.Domain.Enums.StageOutcomeKind.Clean,
                Source = Antiphon.Server.Domain.Enums.StageOutcomeSource.Delegate,
                SubjectTaskId = owner.Id, StageTaskId = Guid.NewGuid(),
                ReviewedSourceSha = reviewed, ReviewedSourceClean = true,
                ReviewedSourceRef = h.Fixture.SourceRef, ReviewedRepositoryPath = owner.RepoPath,
                CommissionedRound = Antiphon.Server.Domain.Enums.VerificationRound.Final,
                OrdinaryScopeCompleted = Antiphon.Server.Domain.Enums.VerificationScope.Full,
                RecordedAt = DateTime.UtcNow,
            };
            db.StageOutcomes.Add(row);
            await db.SaveChangesAsync();
            evidence = row.Id;
        }
        return (local, reviewed, evidence);
    }

    public ValueTask DisposeAsync() => Harness.DisposeAsync();

    internal sealed class SaveCut : SaveChangesInterceptor
    {
        public Guid RequestId { get; set; }
        public bool Armed { get; set; }
        public int Fired { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Armed && data.Context is { } db)
            {
                var entry = db.ChangeTracker.Entries<AgentTaskLandRequest>()
                    .FirstOrDefault(e => e.Entity.Id == RequestId && e.State == EntityState.Modified);
                if (entry is not null)
                {
                    Armed = false;
                    Fired++;
                    throw new DbUpdateConcurrencyException("fixture-request-save-conflict",
                        (IReadOnlyList<IUpdateEntry>)[(IUpdateEntry)entry.GetInfrastructure()]);
                }
            }
            return ValueTask.FromResult(result);
        }
    }

    internal sealed class RefMovedBoundary(SaveCut cut) : LandDeliveryBoundary
    {
        public int Reached { get; private set; }
        public override Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
        {
            if (boundary == "source-adopt-ref-moved-before-reset" && identity == cut.RequestId)
            {
                Reached++;
                cut.Armed = true;
            }
            return Task.CompletedTask;
        }
    }
}
