using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.Scripts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.Application.DelegationWorktreeTests;

namespace Antiphon.Tests.Application;

/// <summary>
/// FT-5. An index-only rebase conflict is regenerated from the merged tree.
/// A second conflicted path, a generator failure, and a clean rebase stay on today's path.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[Category("Slow")]
public class LandGeneratedArtifactTests
{
    private const string SampleTests =
        "namespace Fixture;\n\npublic class SampleTests\n{\n    public void Holds()\n    {\n    }\n}\n";

    [Test]
    public async Task Index_only_conflict_lands_regenerated_bytes()
    {
        var regenerator = new BuilderRegenerator();
        await using var world = await World.CreateAsync("index", regenerator);
        await File.WriteAllTextAsync(Path.Combine(world.Task.WorktreePath!, DerivedImpactIndexRegen.RelativePath), "candidate-index\n");
        await File.WriteAllTextAsync(Path.Combine(world.Task.WorktreePath!, "notes.md"), "kept\n");
        await RequiredGitAsync(world.Task.WorktreePath!, "add", DerivedImpactIndexRegen.RelativePath, "notes.md");
        await RequiredGitAsync(world.Task.WorktreePath!, "commit", "-m", "candidate edits");
        await world.Repo.CommitFileAsync(DerivedImpactIndexRegen.RelativePath, "target-index\n");
        await world.Repo.GitAsync("push", "origin", "master");
        var (request, sourceSha, _, _, sourceFullRef) = await PublishAndRequestAsync(world.Land, world.Task);

        (await world.Land.RunAsync(world.Task.Id, null, CancellationToken.None)).ShouldBe(LandRunResult.Complete);

        regenerator.Calls.ShouldBe(1);
        var operation = await AssertBoundOperationAsync(world.Db, world.Task.Id, request.RequestId, sourceSha, sourceFullRef);
        operation.Publication.ShouldBe(LandPublicationOutcome.Landed);
        var sha = operation.RebasedSourceSha;
        sha.ShouldNotBeNull();
        (await RequiredGitAsync(world.Remote.Path, "rev-parse", "master")).Trim().ShouldBe(sha);
        (await RequiredGitAsync(world.Remote.Path, "log", "-1", "--format=%s", sha)).Trim()
            .ShouldBe(DerivedImpactIndexRegen.CommitMessage);
        (await RequiredGitAsync(world.Remote.Path, "show", sha + "^:notes.md")).ShouldBe("kept\n");
        (await RequiredGitAsync(world.Remote.Path, "show", sha + "^:tests/derived-impact-index.json")).ShouldBe("target-index\n");
        (await RequiredGitAsync(world.Remote.Path, "diff-tree", "--no-commit-id", "--name-only", "-r", sha)).Trim()
            .ShouldBe(DerivedImpactIndexRegen.RelativePath);
        var actual = await RequiredGitAsync(world.Remote.Path, "cat-file", "-p", sha + ":" + DerivedImpactIndexRegen.RelativePath);
        actual.ShouldBe(Encoding.UTF8.GetString(DerivedImpactIndexBuilder.Build(world.Repo.Path).Utf8));
        if (operation.LandWorktreePath is { } land && Directory.Exists(land))
            (await RequiredGitAsync(land, "rev-parse", "HEAD")).Trim().ShouldBe(sha);
    }

    [Test]
    public async Task Index_and_another_file_still_refuses()
    {
        var regenerator = new BuilderRegenerator();
        await using var world = await World.CreateAsync("mixed", regenerator);
        await File.WriteAllTextAsync(Path.Combine(world.Task.WorktreePath!, DerivedImpactIndexRegen.RelativePath), "candidate-index\n");
        await File.WriteAllTextAsync(Path.Combine(world.Task.WorktreePath!, "notes.md"), "candidate\n");
        await RequiredGitAsync(world.Task.WorktreePath!, "add", DerivedImpactIndexRegen.RelativePath, "notes.md");
        await RequiredGitAsync(world.Task.WorktreePath!, "commit", "-m", "candidate edits");
        await world.Repo.CommitFileAsync(DerivedImpactIndexRegen.RelativePath, "target-index\n");
        await world.Repo.CommitFileAsync("notes.md", "target\n");
        await world.Repo.GitAsync("push", "origin", "master");
        var target = (await RequiredGitAsync(world.Remote.Path, "rev-parse", "master")).Trim();
        var (request, sourceSha, _, _, sourceFullRef) = await PublishAndRequestAsync(world.Land, world.Task);

        (await world.Land.RunAsync(world.Task.Id, null, CancellationToken.None)).ShouldBe(LandRunResult.Complete);

        regenerator.Calls.ShouldBe(0);
        var operation = await AssertBoundOperationAsync(world.Db, world.Task.Id, request.RequestId, sourceSha, sourceFullRef);
        operation.LastReason.ShouldBe("rebase_conflict");
        operation.Publication.ShouldNotBe(LandPublicationOutcome.Landed);
        (await AssertRequestAsync(world.Db, world.Task.Id, request.RequestId, sourceSha, sourceFullRef)).State
            .ShouldBe(LandRequestState.NeedsResolution);
        var conflict = await world.Db.AgentTaskEvents.AsNoTracking().SingleAsync(e => e.AgentTaskId == world.Task.Id
            && e.Type == AgentTaskEventType.Conflicted);
        conflict.Detail.ShouldNotContain(DerivedImpactIndexRegen.FailurePrefix);
        (await RequiredGitAsync(world.Remote.Path, "rev-parse", "master")).Trim().ShouldBe(target);
    }

    [Test]
    public async Task Generator_failure_refuses_index_regen_failed()
    {
        var regenerator = new FailingRegenerator();
        await using var world = await World.CreateAsync("fail", regenerator);
        await File.WriteAllTextAsync(Path.Combine(world.Task.WorktreePath!, DerivedImpactIndexRegen.RelativePath), "candidate-index\n");
        await RequiredGitAsync(world.Task.WorktreePath!, "add", DerivedImpactIndexRegen.RelativePath);
        await RequiredGitAsync(world.Task.WorktreePath!, "commit", "-m", "candidate index");
        await world.Repo.CommitFileAsync(DerivedImpactIndexRegen.RelativePath, "target-index\n");
        await world.Repo.GitAsync("push", "origin", "master");
        var target = (await RequiredGitAsync(world.Remote.Path, "rev-parse", "master")).Trim();
        var (request, sourceSha, _, _, sourceFullRef) = await PublishAndRequestAsync(world.Land, world.Task);

        (await world.Land.RunAsync(world.Task.Id, null, CancellationToken.None)).ShouldBe(LandRunResult.Complete);

        regenerator.Calls.ShouldBe(1);
        var operation = await AssertBoundOperationAsync(world.Db, world.Task.Id, request.RequestId, sourceSha, sourceFullRef);
        operation.LastReason.ShouldBe("rebase_conflict");
        operation.Publication.ShouldNotBe(LandPublicationOutcome.Landed);
        (await AssertRequestAsync(world.Db, world.Task.Id, request.RequestId, sourceSha, sourceFullRef)).State
            .ShouldBe(LandRequestState.NeedsResolution);
        var conflict = await world.Db.AgentTaskEvents.AsNoTracking().SingleAsync(e => e.AgentTaskId == world.Task.Id
            && e.Type == AgentTaskEventType.Conflicted);
        conflict.Detail.ShouldContain("index-regen-failed:nonzero");
        var outcome = await world.Db.StageOutcomes.AsNoTracking().SingleAsync(o => o.SubjectTaskId == world.Task.Id
            && o.Stage == OrchestrationStage.Rebase);
        outcome.Detail.ShouldContain("index-regen-failed:nonzero");
        (await RequiredGitAsync(world.Remote.Path, "rev-parse", "master")).Trim().ShouldBe(target);
    }

    [Test]
    public async Task Missing_selected_sdk_names_toolchain_in_conflict_and_status()
    {
        const string reason = "index-regen-unavailable:dotnet-sdk";
        await using var world = await World.CreateAsync("no-sdk", new DerivedImpactIndexRegenerator(null),
            seedUnavailableSdk: true);
        await File.WriteAllTextAsync(Path.Combine(world.Task.WorktreePath!, DerivedImpactIndexRegen.RelativePath), "candidate-index\n");
        await RequiredGitAsync(world.Task.WorktreePath!, "add", DerivedImpactIndexRegen.RelativePath);
        await RequiredGitAsync(world.Task.WorktreePath!, "commit", "-m", "candidate index");
        await world.Repo.CommitFileAsync(DerivedImpactIndexRegen.RelativePath, "target-index\n");
        await world.Repo.GitAsync("push", "origin", "master");
        var target = (await RequiredGitAsync(world.Remote.Path, "rev-parse", "master")).Trim();
        var (request, sourceSha, _, _, sourceFullRef) = await PublishAndRequestAsync(world.Land, world.Task);

        (await world.Land.RunAsync(world.Task.Id, null, CancellationToken.None)).ShouldBe(LandRunResult.Complete);

        var operation = await AssertBoundOperationAsync(world.Db, world.Task.Id, request.RequestId, sourceSha, sourceFullRef);
        operation.Phase.ShouldBe(LandPhase.Refused);
        operation.Publication.ShouldNotBe(LandPublicationOutcome.Landed);
        LandingEvidenceDto.From(operation).Reason.ShouldBe(reason);
        (await AssertRequestAsync(world.Db, world.Task.Id, request.RequestId, sourceSha, sourceFullRef)).State
            .ShouldBe(LandRequestState.NeedsResolution);
        var conflict = await world.Db.AgentTaskEvents.AsNoTracking().SingleAsync(e => e.AgentTaskId == world.Task.Id
            && e.Type == AgentTaskEventType.Conflicted);
        conflict.Detail.ShouldContain(reason);
        var outcome = await world.Db.StageOutcomes.AsNoTracking().SingleAsync(o => o.SubjectTaskId == world.Task.Id
            && o.Stage == OrchestrationStage.Rebase);
        outcome.Detail.ShouldContain(reason);
        (await RequiredGitAsync(world.Remote.Path, "rev-parse", "master")).Trim().ShouldBe(target);
    }

    [Test]
    public async Task Clean_rebase_does_not_regenerate()
    {
        var regenerator = new BuilderRegenerator();
        await using var world = await World.CreateAsync("clean", regenerator);
        await File.WriteAllTextAsync(Path.Combine(world.Task.WorktreePath!, "tests", "AddedSampleTests.cs"),
            "namespace Fixture;\n\npublic class AddedSampleTests\n{\n    public void Holds()\n    {\n    }\n}\n");
        await RequiredGitAsync(world.Task.WorktreePath!, "add", "tests/AddedSampleTests.cs");
        await RequiredGitAsync(world.Task.WorktreePath!, "commit", "-m", "add a test class");
        var (request, sourceSha, _, _, sourceFullRef) = await PublishAndRequestAsync(world.Land, world.Task);

        (await world.Land.RunAsync(world.Task.Id, null, CancellationToken.None)).ShouldBe(LandRunResult.Complete);

        regenerator.Calls.ShouldBe(0);
        var operation = await AssertBoundOperationAsync(world.Db, world.Task.Id, request.RequestId, sourceSha, sourceFullRef);
        operation.Publication.ShouldBe(LandPublicationOutcome.Landed);
        var sha = operation.RebasedSourceSha!;
        (await RequiredGitAsync(world.Remote.Path, "log", "-1", "--format=%s", sha)).Trim()
            .ShouldNotBe(DerivedImpactIndexRegen.CommitMessage);
        (await RequiredGitAsync(world.Remote.Path, "show", sha + ":tests/derived-impact-index.json")).ShouldBe("base-index\n");
    }

    // Unit 41 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the production regenerator probes only the
    // checkout's dotnet, builds the driver once at the slot's CPU grant, then runs the built driver itself until it prints
    // unchanged, every child in the land worktree. No pwsh child and no regenerate-derived-impact-index.ps1.
    [Test]
    public async Task Regenerator_runs_the_built_driver_until_it_prints_unchanged()
    {
        using var land = new TemporaryDirectory("antiphon-ft5-argv");
        SeedDriverProject(land.Path);
        var children = new RecordingChildren("derived-impact windowsOnly count=1 linuxOnly count=0\nderived-impact-index changed\n",
            "derived-impact windowsOnly count=1 linuxOnly count=0\nderived-impact-index unchanged\n");
        var slots = new GrantingSlots();

        var result = await new DerivedImpactIndexRegenerator(slots, children.RunAsync).RegenerateAsync(land.Path, CancellationToken.None);

        (result.Succeeded, result.Changed, result.Reason, result.ToolchainUnavailable).ShouldBe((true, true, (string?)null, false));
        const string project = "scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj";
        var driver = "dotnet run --project " + project + " --no-build -p:UseAppHost=false -- " + Path.GetFullPath(land.Path);
        children.Lines.ShouldBe([
            "dotnet --version",
            "dotnet build " + project + " -p:UseAppHost=false --nologo -maxcpucount:2",
            driver,
            driver,
        ]);
        children.Directories.ShouldAllBe(directory => directory == land.Path);
        (slots.Acquires, slots.Releases).ShouldBe((1, 1));
    }

    [Test]
    [Arguments("derived-impact-index changed\n", "still-changed", 2)]
    [Arguments("derived-impact windowsOnly count=1 linuxOnly count=0\n", "unexpected-output", 1)]
    public async Task Regenerator_refuses_a_driver_that_does_not_settle(string stdout, string reason, int runs)
    {
        using var land = new TemporaryDirectory("antiphon-ft5-settle");
        SeedDriverProject(land.Path);
        var children = new RecordingChildren(stdout);

        var result = await new DerivedImpactIndexRegenerator(new GrantingSlots(), children.RunAsync).RegenerateAsync(land.Path, CancellationToken.None);

        (result.Succeeded, result.Changed, result.Reason).ShouldBe((false, false, reason));
        children.Lines.Count(line => line.StartsWith("dotnet run ", StringComparison.Ordinal)).ShouldBe(runs);
    }

    [Test]
    public async Task Regenerator_without_the_driver_project_is_missing_generator()
    {
        using var land = new TemporaryDirectory("antiphon-ft5-nodriver");
        var children = new RecordingChildren("derived-impact-index unchanged\n");

        var result = await new DerivedImpactIndexRegenerator(new GrantingSlots(), children.RunAsync).RegenerateAsync(land.Path, CancellationToken.None);

        (result.Succeeded, result.Reason, result.ToolchainUnavailable).ShouldBe((false, "missing-generator", false));
        children.Lines.ShouldBeEmpty();
    }

    private static void SeedDriverProject(string land)
    {
        var project = Path.Combine(land, DerivedImpactIndexRegen.ProjectRelative);
        Directory.CreateDirectory(Path.GetDirectoryName(project)!);
        File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");
    }

    // The children of one regeneration: each recorded as "<file> <argv>" with its directory; every child exits 0, and the
    // driver runs print the given outputs in turn (the last one repeats).
    private sealed class RecordingChildren(params string[] driverOutputs)
    {
        private int _runs;

        public List<string> Lines { get; } = [];

        public List<string> Directories { get; } = [];

        public Task<DerivedImpactRegenProcess> RunAsync(string cwd, string file, IReadOnlyList<string> args, CancellationToken ct)
        {
            Lines.Add(file + " " + string.Join(" ", args));
            Directories.Add(cwd);
            var stdout = args[0] == "run" ? driverOutputs[Math.Min(_runs++, driverOutputs.Length - 1)] : "";
            return Task.FromResult(new DerivedImpactRegenProcess(true, stdout));
        }
    }

    private sealed class GrantingSlots : IBuildSlotGate
    {
        public int Acquires { get; private set; }

        public int Releases { get; private set; }

        public Task<BuildSlotHold> AcquireAsync(string label, Action<string> report, CancellationToken ct)
        {
            Acquires++;
            return Task.FromResult(new BuildSlotHold(BuildSlotHoldOutcome.Granted, Guid.NewGuid(), 2, TimeSpan.Zero));
        }

        public Task ReleaseAsync(BuildSlotHold hold, Action<string> report, CancellationToken ct)
        {
            Releases++;
            return Task.CompletedTask;
        }
    }

    private sealed class BuilderRegenerator : IDerivedImpactIndexRegenerator
    {
        public int Calls { get; private set; }

        public Task<DerivedImpactRegenResult> RegenerateAsync(string landWorktree, CancellationToken ct)
        {
            Calls++;
            var path = Path.Combine(landWorktree, DerivedImpactIndexRegen.RelativePath);
            var before = File.Exists(path) ? File.ReadAllBytes(path) : null;
            var index = DerivedImpactIndexBuilder.Build(landWorktree);
            DerivedImpactIndexBuilder.Write(landWorktree, index);
            var changed = before is null || !before.AsSpan().SequenceEqual(index.Utf8);
            return Task.FromResult(new DerivedImpactRegenResult(true, changed, null, 1));
        }
    }

    private sealed class FailingRegenerator : IDerivedImpactIndexRegenerator
    {
        public int Calls { get; private set; }

        public Task<DerivedImpactRegenResult> RegenerateAsync(string landWorktree, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new DerivedImpactRegenResult(false, false, "nonzero", 1));
        }
    }

    private sealed class World : IAsyncDisposable
    {
        public ScratchGitRepo Repo { get; }
        public TemporaryDirectory Remote { get; }
        public AppDbContext Db { get; }
        public AgentTaskLandService Land { get; }
        public AgentTask Task { get; }
        private readonly IAsyncDisposable _schema;

        private World(ScratchGitRepo repo, TemporaryDirectory remote, AppDbContext db, AgentTaskLandService land,
            AgentTask task, IAsyncDisposable schema)
        {
            Repo = repo;
            Remote = remote;
            Db = db;
            Land = land;
            Task = task;
            _schema = schema;
        }

        public static async Task<World> CreateAsync(string name, IDerivedImpactIndexRegenerator regenerator,
            bool seedUnavailableSdk = false)
        {
            var repo = new ScratchGitRepo("antiphon-ft5-" + name);
            var remote = new TemporaryDirectory("antiphon-ft5-" + name + "-remote");
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            try
            {
                await ScratchGitRepo.GitInAsync(remote.Path, "init", "--bare");
                Directory.CreateDirectory(Path.Combine(repo.Path, "tests"));
                await repo.CommitFileAsync("tests/SampleTests.cs", SampleTests);
                await repo.CommitFileAsync(DerivedImpactIndexRegen.RelativePath, "base-index\n");
                await repo.CommitFileAsync("notes.md", "base\n");
                if (seedUnavailableSdk)
                {
                    await repo.CommitFileAsync("global.json", """{"sdk":{"version":"99.0.100","rollForward":"disable"}}""");
                    Directory.CreateDirectory(Path.Combine(repo.Path, Path.GetDirectoryName(DerivedImpactIndexRegen.ProjectRelative)!));
                    await repo.CommitFileAsync(DerivedImpactIndexRegen.ProjectRelative, "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");
                }
                await repo.GitAsync("remote", "add", "origin", remote.Path);
                await repo.GitAsync("push", "-u", "origin", "master");
                var (land, service) = CreateLand(db, repo, configureProtocol: protocol => protocol.IndexRegenerator = regenerator);
                var task = NewTask(repo.Path, mergeTarget: null);
                await service.CreateForTaskAsync(task, CancellationToken.None);
                task.Status = AgentTaskStatus.Succeeded;
                task.CompletedAt = DateTime.UtcNow;
                db.AgentTasks.Add(task);
                await db.SaveChangesAsync();
                return new World(repo, remote, db, land, task, schema);
            }
            catch
            {
                await db.DisposeAsync();
                await schema.DisposeAsync();
                remote.Dispose();
                repo.Dispose();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _schema.DisposeAsync();
            Remote.Dispose();
            Repo.Dispose();
        }
    }
}
