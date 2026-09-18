using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0552 V-552-79..82. The sweep is the one tick that spends, so every test host in this
/// assembly must have it off and the hosted service must be able to prove it does nothing.
/// </summary>
[NotInParallel]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
[Category("Integration")]
public sealed class MutationAutoDispatchHostTests
{
    private readonly AntiphonWebAppFactory _factory;

    public MutationAutoDispatchHostTests(AntiphonWebAppFactory factory) => _factory = factory;

    [Before(Test)]
    public Task ResetAsync() => _factory.ResetAsync();

    [Test]
    public async Task C552_H01_TestHostHasTheSweepOffAndCreatesNothing()
    {
        _factory.Services.GetRequiredService<IOptions<DelegationSettings>>()
            .Value.MutationAutoDispatch.Enabled.ShouldBeFalse();
        _factory.Services.GetServices<IHostedService>()
            .Count(s => s is MutationAutoDispatchHostedService).ShouldBe(1);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (companion, original, owner, op) = await SeedDebtAsync(db);
        try
        {
            var tick = await scope.ServiceProvider.GetRequiredService<MutationAutoDispatchSweep>()
                .TickAsync(CancellationToken.None);

            tick.Reason.ShouldBe("disabled");
            tick.Created.ShouldBe(0);
            (await db.AgentTasks.CountAsync(t => t.CardId == companion)).ShouldBe(0);
        }
        finally
        {
            await db.AgentTaskLandings.Where(o => o.Id == op).ExecuteDeleteAsync();
            await db.AgentTasks.Where(t => t.Id == owner).ExecuteDeleteAsync();
            await db.Cards.Where(c => c.Id == companion || c.Id == original).ExecuteDeleteAsync();
        }
    }

    [Test]
    public void C552_H02_ProductionRunnerGuardDisablesTheSweep()
    {
        ProductionRunnerGuard.MutationAutoDispatchEnvVar.ShouldBe("Delegation__MutationAutoDispatch__Enabled");
        Environment.GetEnvironmentVariable(ProductionRunnerGuard.MutationAutoDispatchEnvVar).ShouldBe("false");
    }

    [Test]
    [Category("Unit")]
    public async Task C552_H03_HostedServiceReturnsWhenDisabled()
    {
        var hosted = new MutationAutoDispatchHostedService(
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new DelegationSettings { MutationAutoDispatch = { Enabled = false } }),
            NullLogger<MutationAutoDispatchHostedService>.Instance);

        await hosted.StartAsync(CancellationToken.None);

        hosted.ExecuteTask.ShouldNotBeNull().Wait(TimeSpan.FromSeconds(1)).ShouldBeTrue();
    }

    [Test]
    [Category("Unit")]
    public void C552_H04_SweepHasNoDispatcherOrRunnerDependency()
    {
        var parameters = typeof(MutationAutoDispatchSweep).GetConstructors().Single()
            .GetParameters().Select(p => p.ParameterType).ToList();

        parameters.ShouldNotContain(typeof(AgentTaskDispatcher));
        parameters.ShouldNotContain(typeof(ISessionRunnerClient));
        parameters.ShouldNotContain(typeof(SessionMessageQueueService));
        parameters.ShouldNotContain(typeof(CardService));
    }

    private static async Task<(Guid Companion, Guid Original, Guid Owner, Guid Operation)> SeedDebtAsync(AppDbContext db)
    {
        var now = DateTime.UtcNow;
        var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync();
        var board = new Board
        {
            Id = Guid.NewGuid(), ProjectId = project?.Id ?? Guid.NewGuid(), Name = "C552 host",
            MaxConcurrentSessions = 1, CreatedAt = now, UpdatedAt = now,
        };
        if (project is null)
        {
            var created = new Project
            {
                Id = board.ProjectId, Name = "c552-host", GitRepositoryUrl = "https://example.test/c552.git",
                CreatedAt = now, UpdatedAt = now,
            };
            db.Projects.Add(created);
        }

        var column = new BoardColumn
        {
            Id = Guid.NewGuid(), BoardId = board.Id, StateKey = "backlog", Name = "Backlog",
            ColumnOrder = 0, CardStatus = CardStatus.Backlog, CreatedAt = now, UpdatedAt = now,
        };
        var original = new Card
        {
            Id = Guid.NewGuid(), BoardId = board.Id, BoardColumnId = column.Id, Identifier = "H552-0001",
            Title = "original", Status = CardStatus.Done, CompletedAt = now, CreatedAt = now, UpdatedAt = now,
        };
        var companion = new Card
        {
            Id = Guid.NewGuid(), BoardId = board.Id, BoardColumnId = column.Id, Identifier = "H552-0002",
            Title = "Post-land verification: H552-0001", Status = CardStatus.Backlog,
            CreatedAt = now, UpdatedAt = now,
        };
        var ownerId = Guid.NewGuid();
        var owner = new AgentTask
        {
            Id = ownerId, RootTaskId = ownerId, Title = "owner", Goal = "owner",
            Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code, Status = AgentTaskStatus.Succeeded,
            Workspace = WorkspaceMode.Worktree, WorkingDirectory = @"C:\tmp\c552-host",
            RepoPath = @"C:\tmp\c552-host", WorktreeBranch = "feat/c552", CardId = original.Id,
            ProjectId = board.ProjectId, CreatedAt = now.AddHours(-2), CompletedAt = now.AddHours(-2),
        };
        db.AddRange(board, column, original, companion, owner);
        await db.SaveChangesAsync();

        var sha = new string('b', 40);
        var op = new AgentTaskLanding
        {
            Id = Guid.NewGuid(), TaskId = owner.Id, Active = true,
            Phase = LandPhase.PublicationConfirmed, Publication = LandPublicationOutcome.Landed,
            Cleanup = LandCleanupStatus.Complete, Mode = LandOperationMode.Fresh,
            OriginalSourceSha = new string('c', 40), RebasedSourceSha = sha, VerifiedSourceSha = sha,
            ObservedRemoteTargetSha = sha, TargetBeforeSha = new string('a', 40),
            TargetFullRef = "refs/heads/master", DestinationFullRef = "refs/heads/master",
            SourceFullRef = "refs/heads/source", RepositoryPath = owner.RepoPath!,
            CommonDirectory = owner.RepoPath!, WorktreePath = owner.RepoPath!, GitDirectory = owner.RepoPath!,
            SourcePinned = true, TargetPinned = true, PreparedPinned = true, VerificationPassed = true,
            VerifiedAt = now, RemoteFingerprint = new string('a', 64), RemoteConfirmedAt = now.AddHours(-1),
            ConfirmationMethod = "push-endpoint-read-fetch-ancestry", VerificationCardId = companion.Id,
            CreatedAt = now, UpdatedAt = now,
        };
        op.RecoveryRefPrefix = $"refs/antiphon/land/{owner.Id:N}/{op.Id:N}";
        db.AgentTaskLandings.Add(op);
        await db.SaveChangesAsync();
        return (companion.Id, original.Id, owner.Id, op.Id);
    }
}
