using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Update;
using Antiphon.Server.Infrastructure.Git;
using System.Security.Cryptography;
using Shouldly;

namespace Antiphon.Tests.TestHelpers;

internal sealed class LandHalfResetFixture : IAsyncDisposable
{
    public LandingSafetyHarness Harness { get; }
    public Guid? AdoptionSourceId { get; private set; }
    public string? AdoptionSourcePath { get; private set; }
    public SaveCut Interceptor { get; } = new();
    public RefMovedBoundary Boundary { get; }

    public LandHalfResetFixture(string? root = null)
    {
        Harness = new LandingSafetyHarness(root);
        Boundary = new RefMovedBoundary(Interceptor, Harness);
        Harness.Boundary = Boundary;
        Harness.LandCutInterceptor = Interceptor;
    }

    public async Task<(string Local, string Reviewed, Guid Evidence)> SeedReviewedDescendantAsync(bool bulk = false,
        bool executable = false, bool equivalentOldTip = false, bool adoption = false)
    {
        var h = Harness;
        await h.InitializeAsync();
        var local = await h.AddSourceAsync();
        if (executable)
        {
            await File.WriteAllTextAsync(Path.Combine(h.Fixture.Source, "run.sh"), "#!/bin/sh\nexit 0\n");
            await h.Fixture.RequiredAsync(h.Fixture.Source, "add", "run.sh");
            await h.Fixture.RequiredAsync(h.Fixture.Source, "update-index", "--chmod=+x", "run.sh");
            await h.Fixture.RequiredAsync(h.Fixture.Source, "commit", "-m", "executable old tip");
            local = (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim();
        }
        if (bulk)
        {
            for (var i = 0; i < 51; i++)
                await File.WriteAllTextAsync(Path.Combine(h.Fixture.Source, $"changed-{i:D2}.txt"), "old\n");
            await h.Fixture.RequiredAsync(h.Fixture.Source, "add", ".");
            await h.Fixture.RequiredAsync(h.Fixture.Source, "commit", "-m", "old bulk tip");
            local = (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim();
        }
        if (equivalentOldTip)
        {
            await h.Fixture.RequiredAsync(h.Fixture.Source, "commit", "--allow-empty", "-m", "equivalent old tree");
            local = (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim();
        }
        await h.Fixture.RequiredAsync(h.Fixture.Source, "push", "origin", h.Fixture.SourceRef);
        var reviewedTree = Path.Combine(h.Fixture.Root, "trees", "reviewed");
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "worktree", "add", "--detach", reviewedTree, local);
        if (bulk)
        {
            for (var i = 0; i < 51; i++)
                await File.WriteAllTextAsync(Path.Combine(reviewedTree, $"changed-{i:D2}.txt"), "reviewed\n");
            for (var i = 0; i < 13; i++)
                await File.WriteAllTextAsync(Path.Combine(reviewedTree, $"added-{i:D2}.txt"), "reviewed addition\n");
        }
        else
            await File.WriteAllTextAsync(Path.Combine(reviewedTree, "feature.txt"), "reviewed feature\n");
        await h.Fixture.RequiredAsync(reviewedTree, "add", ".");
        await h.Fixture.RequiredAsync(reviewedTree, "commit", "-m", "reviewed descendant");
        var reviewed = (await h.Fixture.RequiredAsync(reviewedTree, "rev-parse", "HEAD")).Trim();
        var sourceRef = h.Fixture.SourceRef;
        if (adoption)
        {
            AdoptionSourceId = Guid.NewGuid();
            AdoptionSourcePath = reviewedTree;
            sourceRef = $"refs/heads/feat/card-task-{AdoptionSourceId:N}";
            await h.Fixture.RequiredAsync(reviewedTree, "checkout", "-b", sourceRef[11..]);
        }
        await h.Fixture.RequiredAsync(reviewedTree, "push", "origin", $"HEAD:{sourceRef}");
        Guid evidence;
        await using (var db = h.CreateContext())
        {
            var owner = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
            owner.Status = Antiphon.Server.Domain.Enums.AgentTaskStatus.Failed;
            if (adoption)
            {
                var now = DateTime.UtcNow;
                var projectId = Guid.NewGuid();
                var boardId = Guid.NewGuid();
                var columnId = Guid.NewGuid();
                var cardId = Guid.NewGuid();
                db.Projects.Add(new Project { Id = projectId, Name = "C939 fixture", LocalRepositoryPath = h.Fixture.Repository, CreatedAt = now, UpdatedAt = now });
                db.Boards.Add(new Board { Id = boardId, ProjectId = projectId, Name = "C939", CreatedAt = now, UpdatedAt = now });
                db.BoardColumns.Add(new BoardColumn { Id = columnId, BoardId = boardId, Name = "Ready", StateKey = "ready", CreatedAt = now, UpdatedAt = now });
                db.Cards.Add(new Card { Id = cardId, BoardId = boardId, BoardColumnId = columnId, Identifier = "CARD-0939", Title = "fixture", CreatedAt = now, UpdatedAt = now });
                owner.ProjectId = projectId;
                owner.CardId = cardId;
                db.AgentTasks.Add(new AgentTask { Id = AdoptionSourceId!.Value, RootTaskId = AdoptionSourceId.Value,
                    Title = "reviewed source", Goal = "fixture", Kind = Antiphon.Server.Domain.Enums.AgentTaskKind.Worker,
                    Role = Antiphon.Server.Domain.Enums.AgentTaskRole.Code, Workspace = Antiphon.Server.Domain.Enums.WorkspaceMode.Worktree,
                    WorkingDirectory = h.Fixture.Repository, RepoPath = h.Fixture.Repository, WorktreePath = reviewedTree,
                    WorktreeBranch = sourceRef[11..], WorktreeBaseSha = local, Status = Antiphon.Server.Domain.Enums.AgentTaskStatus.Failed,
                    ProjectId = projectId, CardId = cardId, ReplyTo = Antiphon.Server.Domain.Enums.AgentTaskReplyTo.None, CreatedAt = now, CompletedAt = now });
            }
            var row = new StageOutcome
            {
                Id = Guid.NewGuid(), Stage = Antiphon.Server.Domain.Enums.OrchestrationStage.Review,
                Outcome = Antiphon.Server.Domain.Enums.StageOutcomeKind.Clean,
                Source = Antiphon.Server.Domain.Enums.StageOutcomeSource.Delegate,
                SubjectTaskId = AdoptionSourceId ?? owner.Id, StageTaskId = Guid.NewGuid(),
                ReviewedSourceSha = reviewed, ReviewedSourceClean = true,
                ReviewedSourceRef = sourceRef, ReviewedRepositoryPath = owner.RepoPath,
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

    /// <summary>Independent observations and a single owned admin-file write, restored only at disposal.</summary>
    internal sealed class BacklinkCorruption(
        LandingGitFixture fixture, LandingGitFixture.FixtureGit reader, string backlink, byte[] original,
        string worktrees, DateTime stamp, int entryCount, string sourceImage, string observerImage,
        string remoteRefs) : IAsyncDisposable
    {
        private bool redirected;

        public static async Task<BacklinkCorruption> CreateAsync(LandingGitFixture fixture)
        {
            var reader = new LandingGitFixture.FixtureGit(Path.Combine(fixture.Root, "home"), fixture.TaskId);
            var admin = (await RequiredAsync(reader, fixture.Source, "rev-parse", "--absolute-git-dir")).Trim();
            var common = Path.GetFullPath((await RequiredAsync(reader, fixture.Repository,
                "rev-parse", "--git-common-dir")).Trim(), fixture.Repository);
            var worktrees = Path.Combine(common, "worktrees");
            LandingGit.PathsEqual(Path.GetDirectoryName(admin)!, worktrees).ShouldBeTrue();
            var backlink = Path.Combine(admin, "gitdir");
            return new(fixture, reader, backlink, await File.ReadAllBytesAsync(backlink), worktrees,
                Directory.GetLastWriteTimeUtc(worktrees), Directory.EnumerateFileSystemEntries(worktrees).Count(),
                await CheckoutImageAsync(reader, fixture.Source), await ObserverImageAsync(reader, fixture.Observer),
                await RequiredAsync(reader, fixture.Remote, "show-ref", "--heads"));
        }

        public async Task RedirectAsync()
        {
            redirected.ShouldBeFalse("each scenario injects exactly once");
            redirected = true;
            // Git-for-Windows writes forward-slash admin backlinks. A native backslash
            // path makes worktree list name a different malformed path, not the observer.
            var observerGitDirectory = (await RequiredAsync(reader, fixture.Observer,
                "rev-parse", "--absolute-git-dir")).Trim();
            await File.WriteAllTextAsync(backlink, observerGitDirectory + "\n");
            AssertStamp();
            var rows = LandingGit.ParseRegistrations(await RequiredAsync(reader, fixture.Repository,
                "worktree", "list", "--porcelain", "-z"));
            rows.Any(row => LandingGit.PathsEqual(row.Path, fixture.Source)).ShouldBeFalse();
            rows.Any(row => LandingGit.PathsEqual(row.Path, fixture.Observer)).ShouldBeTrue();
        }

        public async Task AssertPreservedAsync(string ownerSha)
        {
            Directory.Exists(fixture.Source).ShouldBeTrue("the refused checkout stays in place");
            AssertStamp();
            (await RequiredAsync(reader, fixture.Source, "rev-parse", "HEAD")).Trim().ShouldBe(ownerSha);
            (await RequiredAsync(reader, fixture.Repository, "show-ref", "--verify", "--hash", fixture.SourceRef))
                .Trim().ShouldBe(ownerSha);
            (await RequiredAsync(reader, fixture.Source, "symbolic-ref", "HEAD")).Trim().ShouldBe(fixture.SourceRef);
            (await CheckoutImageAsync(reader, fixture.Source)).ShouldBe(sourceImage,
                "owner index, tracked bytes and .git file must survive the refusal");
            (await ObserverImageAsync(reader, fixture.Observer)).ShouldBe(observerImage,
                "observer HEAD, refs, index and bytes must stay unchanged");
            (await RequiredAsync(reader, fixture.Remote, "show-ref", "--heads")).ShouldBe(remoteRefs,
                "compare captured owner/adoption/target remotes, which need not share a SHA");
        }

        private void AssertStamp()
        {
            Directory.GetLastWriteTimeUtc(worktrees).ShouldBe(stamp,
                "nested corruption must leave the parent registration stamp unchanged");
            Directory.EnumerateFileSystemEntries(worktrees).Count().ShouldBe(entryCount);
        }

        private static async Task<string> CheckoutImageAsync(LandingGitFixture.FixtureGit reader, string checkout)
        {
            var admin = (await RequiredAsync(reader, checkout, "rev-parse", "--absolute-git-dir")).Trim();
            var facts = new List<string> { await DigestAsync(Path.Combine(admin, "index")) };
            var dotGit = Path.Combine(checkout, ".git");
            if (File.Exists(dotGit)) facts.Add(await DigestAsync(dotGit));
            foreach (var path in (await RequiredAsync(reader, checkout, "ls-files", "-z"))
                         .Split('\0', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal))
            {
                facts.Add(path);
                facts.Add(await DigestAsync(Path.Combine(checkout, path)));
            }
            return string.Join('\0', facts);
        }

        private static async Task<string> ObserverImageAsync(LandingGitFixture.FixtureGit reader, string observer) =>
            string.Join('\0', await RequiredAsync(reader, observer, "rev-parse", "HEAD"),
                await RequiredAsync(reader, observer, "show-ref"), await CheckoutImageAsync(reader, observer));

        private static async Task<string> DigestAsync(string path) =>
            Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path)));

        private static async Task<string> RequiredAsync(LandingGitFixture.FixtureGit reader, string path,
            params string[] arguments)
        {
            var result = await reader.RunAsync(path, arguments, CancellationToken.None);
            result.Succeeded.ShouldBeTrue(result.Diagnostic);
            return result.Output;
        }

        public async ValueTask DisposeAsync()
        {
            fixture.Git.AfterCommand = null;
            fixture.Git.BeforeCommand = null;
            if (redirected) await File.WriteAllBytesAsync(backlink, original);
        }
    }

    internal sealed class PairObservationGit(string home, LandingSafetyHarness h) : LandingGitFixture.FixtureGit(home, h.Fixture.TaskId)
    {
        public bool InAdoptionPair { get; private set; }
        public int DurableIntents { get; private set; }
        private bool AdoptionCommand(string directory, IReadOnlyList<string> args) =>
            args.Count == 5 && args[0] == "update-ref" && args[1] == "--no-deref" && args[2] == h.Fixture.SourceRef
            || args[0] == "reset" && directory == h.Fixture.Source;

        protected override System.Diagnostics.Process? StartProcess(System.Diagnostics.ProcessStartInfo start)
        {
            if (AdoptionCommand(start.WorkingDirectory, start.ArgumentList))
            {
                var children = Path.Combine(h.Fixture.Repository, ".git", "antiphon", "children");
                var intents = Directory.GetFiles(children, "*.json");
                if (intents.Length != 1) throw new InvalidOperationException("fixture_missing_durable_child_intent");
                using var record = System.Text.Json.JsonDocument.Parse(File.ReadAllText(intents[0]));
                if (record.RootElement.GetProperty("ProcessId").ValueKind != System.Text.Json.JsonValueKind.Null)
                    throw new InvalidOperationException("fixture_intent_not_observed_before_start");
                DurableIntents++;
            }
            return base.StartProcess(start);
        }

        public override async Task<Antiphon.Server.Application.Dtos.LandingGitResult> RunAsync(
            string repository, IReadOnlyList<string> args, CancellationToken ct)
        {
            if (args[0] == "update-ref" && AdoptionCommand(repository, args)) InAdoptionPair = true;
            try { return await base.RunAsync(repository, args, ct); }
            finally { if (args[0] == "reset" && repository == h.Fixture.Source) InAdoptionPair = false; }
        }
    }

    internal sealed class CustodyGit(string home, Guid taskId) : LandingGitFixture.FixtureGit(home, taskId), Antiphon.Server.Application.Interfaces.ILandingGit
    {
        public bool? ChildAlive { get; set; }
        public int LivenessReads { get; private set; }
        Task<bool?> Antiphon.Server.Application.Interfaces.ILandingGit.IsProcessAliveAsync(int pid, long ticks, CancellationToken ct)
        {
            if (pid != 883939 || ticks != 883939) throw new InvalidOperationException("unexpected fixture child identity");
            LivenessReads++;
            return Task.FromResult(ChildAlive);
        }
    }

    internal sealed class SaveCut : SaveChangesInterceptor
    {
        public Action<DbContext>? BeforeSave { get; set; }
        public Func<DbContext, Task>? AfterSave { get; set; }
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken ct = default)
        {
            if (AfterSave is not null && data.Context is not null) await AfterSave(data.Context);
            return result;
        }

        public Guid RequestId { get; set; }
        public bool Armed { get; set; }
        public string? OperationFilter { get; set; }
        public int Fired { get; private set; }
        public void FiredAtBoundary() => Fired++;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (data.Context is not null) BeforeSave?.Invoke(data.Context);
            if (Armed && data.Context is { } db)
            {
                var entry = db.ChangeTracker.Entries<AgentTaskLandRequest>()
                    .FirstOrDefault(e => e.Entity.Id == RequestId && e.State == EntityState.Modified);
                if (entry is not null && (OperationFilter is null
                    || entry.Entity.SourceAdvanceChildOperation == OperationFilter))
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

    internal sealed class RefMovedBoundary(SaveCut cut, LandingSafetyHarness harness) : LandDeliveryBoundary
    {
        public int Reached { get; private set; }
        public int BeforeRefReached { get; private set; }
        public bool ThrowConflict { get; set; } = true;
        public Func<Task>? AtCut { get; set; }
        public Func<Task>? BeforeRefMove { get; set; }
        public override async Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
        {
            if (boundary == "source-adopt-before-ref-move" && identity == cut.RequestId
                && BeforeRefMove is not null)
            {
                BeforeRefReached++;
                await BeforeRefMove();
            }
            if (boundary == "source-adopt-ref-moved-before-reset" && identity == cut.RequestId)
            {
                Reached++;
                if (AtCut is not null) await AtCut();
                if (!ThrowConflict) return;
                await using var db = harness.CreateContext();
                var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == identity, ct);
                db.Entry(request).State = EntityState.Modified;
                cut.FiredAtBoundary();
                throw new DbUpdateConcurrencyException("fixture-request-save-conflict",
                    (IReadOnlyList<IUpdateEntry>)[(IUpdateEntry)db.Entry(request).GetInfrastructure()]);
            }
        }
    }
}
