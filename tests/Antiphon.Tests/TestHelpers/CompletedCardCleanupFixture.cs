using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Files;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Antiphon.SessionRunner.Contracts;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace Antiphon.Tests.TestHelpers;

/// <summary>Real Git, external report storage and isolated PostgreSQL. Every tree is fixture-owned.</summary>
internal sealed class CompletedCardCleanupFixture : IAsyncDisposable
{
    public LandingSafetyHarness Host { get; }
    public Guid CardId { get; private set; }
    public Guid DoneColumnId { get; private set; }
    public Guid ReviewColumnId { get; private set; }
    public Guid TaskId => Host.Fixture.TaskId;
    public string Tree { get; private set; } = "";
    public string Sentinel => Path.Combine(Tree, ".antiphon", "checkpoints", "sentinel.trx");
    public string OriginalBytes => "fixture evidence survives refusal\n";
    public Guid TargetId { get; private set; }
    public Guid EndpointId { get; private set; }

    private CompletedCardCleanupFixture()
    {
        var checkout = new DirectoryInfo(AppContext.BaseDirectory);
        while (checkout is not null && !File.Exists(Path.Combine(checkout.FullName, "Antiphon.sln"))) checkout = checkout.Parent;
        var root = Path.Combine(checkout?.FullName ?? throw new InvalidOperationException("checkout missing"),
            ".antiphon", "test-output", "c1017", Guid.NewGuid().ToString("N"));
        Host = new LandingSafetyHarness(root)
        {
            ConfigureServices = services =>
            {
                services.AddSingleton<CardDoneArtifactPreservation>();
                services.AddScoped<CardWorktreeCleanupExecutor>();
                services.AddScoped<CardWorktreeCleanupService>();
                services.AddSingleton(Options.Create(new WorktreeResidueSettings { MinSettledMinutes = 120 }));
            }
        };
    }

    public static async Task<CompletedCardCleanupFixture> CreateAsync(string? leaf = null)
    {
        var f = new CompletedCardCleanupFixture();
        await f.Host.InitializeAsync();
        var branch = "feat/card-task-" + f.TaskId.ToString("N")[..8];
        f.Tree = Path.Combine(f.Host.Fixture.Root, "trees", leaf ?? "card-task-" + f.TaskId.ToString("N")[..8]);
        if (leaf is null)
        {
            await using var lease = await f.Host.Services.GetRequiredService<IRepositoryMutationLease>()
                .TryAcquireAsync(f.Host.Fixture.Repository, CancellationToken.None);
            lease.ShouldNotBeNull();
            await f.Host.Services.GetRequiredService<IWorktreeManager>().CreateVerificationAsync(
                f.Host.Fixture.Repository, "task-" + f.TaskId.ToString("N")[..8], f.Host.Fixture.SeedSha, lease!, CancellationToken.None);
        }
        else await f.Host.Fixture.RequiredAsync(f.Host.Fixture.Repository, "worktree", "add", "-b", branch, f.Tree, "HEAD");
        Directory.CreateDirectory(Path.GetDirectoryName(f.Sentinel)!);
        await File.WriteAllTextAsync(f.Sentinel, f.OriginalBytes);
        await using var db = f.Host.CreateContext();
        var project = new Project { Id = Guid.NewGuid(), Name = "c1017 " + Guid.NewGuid(), GitRepositoryUrl = "https://example.test/fixture.git", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var board = new Board { Id = Guid.NewGuid(), Project = project, Name = "fixture", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var review = new BoardColumn { Id = Guid.NewGuid(), Board = board, StateKey = "review", Name = "Review", CardStatus = CardStatus.Review, ColumnOrder = 0 };
        var done = new BoardColumn { Id = Guid.NewGuid(), Board = board, StateKey = "done", Name = "Done", CardStatus = CardStatus.Done, IsTerminal = true, ColumnOrder = 1 };
        board.Columns.Add(review); board.Columns.Add(done);
        var card = new Card { Id = Guid.NewGuid(), Board = board, BoardColumn = review, Identifier = "CARD-1017", Title = "cleanup fixture",
            Status = CardStatus.Review, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, ConcurrencyToken = Guid.NewGuid() };
        db.Cards.Add(card);
        f.CardId = card.Id; f.DoneColumnId = done.Id; f.ReviewColumnId = review.Id;
        var task = await db.AgentTasks.SingleAsync(t => t.Id == f.TaskId);
        task.CardId = card.Id; task.Attempt = 1; task.WorktreePath = f.Tree; task.WorkingDirectory = f.Tree;
        task.WorktreeBranch = branch; task.WorktreeBaseSha = f.Host.Fixture.SeedSha;
        task.CompletedAt = DateTime.UtcNow.AddHours(-3); task.Result = "complete canonical report\n";
        await db.SaveChangesAsync();
        await f.MoveAsync(f.DoneColumnId);
        return f;
    }

    public async Task MoveAsync(Guid column)
    {
        await using var db = Host.CreateContext();
        var card = await db.Cards.SingleAsync(c => c.Id == CardId);
        await new CardService(db, null!, null!, null!, new MockEventBus(), TimeProvider.System, null!)
            .MoveAsync(CardId, new MoveCardRequest(column, card.ConcurrencyToken, "fixture"), CancellationToken.None);
    }

    public async Task ChangeTaskAsync(Action<AgentTask> change)
    {
        await using var db = Host.CreateContext();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == TaskId);
        change(task);
        await db.SaveChangesAsync();
    }

    public async Task BindSourceLandingAsync()
    {
        await using var db = Host.CreateContext();
        var sourceTaskId = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask { Id = sourceTaskId, RootTaskId = sourceTaskId, Title = "source publication", Goal = "fixture",
            Status = AgentTaskStatus.Succeeded, Role = AgentTaskRole.Code, Workspace = WorkspaceMode.Shared, CreatedAt = DateTime.UtcNow });
        var operationId = Guid.NewGuid();
        var common = await Host.Fixture.Git.CommonDirectoryAsync(Host.Fixture.Repository, CancellationToken.None);
        var admin = (await Host.Fixture.RequiredAsync(Tree, "rev-parse", "--absolute-git-dir")).Trim();
        var branch = "feat/card-task-" + TaskId.ToString("N")[..8];
        db.AgentTaskLandings.Add(new AgentTaskLanding
        {
            Id = operationId, TaskId = sourceTaskId, SchemaVersion = 3, Phase = LandPhase.Complete,
            Publication = LandPublicationOutcome.Landed, VerifiedSourceSha = Host.Fixture.SeedSha,
            OriginalSourceSha = Host.Fixture.SeedSha, CommonDirectory = common, RepositoryPath = Host.Fixture.Repository,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var recorded = await Host.Services.GetRequiredService<IWorktreeManager>().ReadVerificationCreationAsync(Tree, CancellationToken.None);
        recorded.ShouldNotBeNull();
        var creation = new VerificationCreationCoordinates(Host.Fixture.Repository, common, Tree, admin, branch, recorded!.CreationId);
        var seal = new VerificationCleanupSeal(Guid.NewGuid(), 0, creation, [], true, DateTime.UtcNow);
        var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == TaskId);
        var restoration = new VerificationRestoration(1, new(TaskId, operationId, Host.Fixture.SeedSha), creation.CreationId,
            true, "fixture fully restored", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(task.Result!))), []);
        var root = Path.Combine(common, "antiphon", "verification", operationId.ToString("N"), TaskId.ToString("N"));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "restoration.json"), JsonSerializer.Serialize(restoration, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        await db.AgentTasks.Where(t => t.Id == TaskId).ExecuteUpdateAsync(s => s
            .SetProperty(t => t.SourceLandingOperationId, operationId).SetProperty(t => t.Role, AgentTaskRole.Mutation)
            .SetProperty(t => t.SourceLandingSha, Host.Fixture.SeedSha).SetProperty(t => t.MergeTargetRef, (string?)null)
            .SetProperty(t => t.VerificationCustodyContractVersion, 1)
            .SetProperty(t => t.VerificationCreationJson, JsonSerializer.Serialize(creation, (JsonSerializerOptions?)null))
            .SetProperty(t => t.VerificationCleanupSealJson, JsonSerializer.Serialize(seal, (JsonSerializerOptions?)null)));
    }

    public async Task DiscoverAsync()
    {
        await using var scope = Host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<CardWorktreeCleanupService>();
        TargetId = (await service.DiscoverAsync(CardId, CancellationToken.None)).Single();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        EndpointId = await db.CardWorktreeCleanupEndpoints.Where(e => e.TargetId == TargetId).Select(e => e.Id).SingleOrDefaultAsync();
    }

    public async Task<WorktreeRemoval> CleanupAsync(Action<CardWorktreeCleanupExecutor>? configure = null)
    {
        if (TargetId == Guid.Empty) await DiscoverAsync();
        await using var scope = Host.Services.CreateAsyncScope();
        configure?.Invoke(scope.ServiceProvider.GetRequiredService<CardWorktreeCleanupExecutor>());
        return await scope.ServiceProvider.GetRequiredService<CardWorktreeCleanupService>()
            .TryCleanupLocalAsync(TargetId, null, CancellationToken.None);
    }

    public async Task<string?> SentinelAsync() => File.Exists(Sentinel) ? await File.ReadAllTextAsync(Sentinel) : null;
    public async Task<int> IntentsAsync()
    {
        await using var db = Host.CreateContext();
        return await db.CardWorktreeCleanupEndpoints.CountAsync(e => e.OperationId != null);
    }
    public async Task AssertRemovedAsync()
    {
        Directory.Exists(Tree).ShouldBeFalse();
        var registrations = await Host.Fixture.Git.LiveRegistrationsAsync(Host.Fixture.Repository, CancellationToken.None);
        registrations.Any(r => r.Path == Tree).ShouldBeFalse();
        await using var db = Host.CreateContext();
        var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == TaskId);
        (await File.ReadAllTextAsync(task.ResultFilePath!)).ShouldBe(task.Result);
        (await Host.Fixture.RequiredAsync(Host.Fixture.Remote, "rev-parse", "refs/heads/master")).Trim().ShouldBe(Host.Fixture.SeedSha);
    }
    public async ValueTask DisposeAsync()
    {
        await Host.Services.DisposeAsync();
        await Host.Schema.DisposeAsync();
        var root = Path.GetFullPath(Host.Fixture.Root);
        if (Path.GetFileName(Path.GetDirectoryName(root)) != "c1017"
            || !Guid.TryParseExact(Path.GetFileName(root), "N", out _)
            || (await File.ReadAllTextAsync(Path.Combine(Host.Fixture.Repository, "fixture-owner.txt"))).Trim() != TaskId.ToString("N"))
            throw new InvalidOperationException("C1017 fixture ownership changed");
        Antiphon.Server.Infrastructure.Git.WorktreeNoFollowDelete.Delete(root);
    }
}
