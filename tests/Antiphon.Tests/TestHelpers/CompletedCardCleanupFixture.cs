using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Files;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

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
        await f.Host.Fixture.RequiredAsync(f.Host.Fixture.Repository, "worktree", "add", "-b", branch, f.Tree, "HEAD");
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
    public ValueTask DisposeAsync() => Host.DisposeAsync();
}
