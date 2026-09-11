using System.Net;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class PostLandMutationAdmissionTests
{
    [Test]
    public async Task C478_V01_PublicSurfaceAndMigration()
    {
        var name = "test_c478_upgrade_" + Guid.NewGuid().ToString("N");
        await using (var maintenance = new NpgsqlConnection(TestDbFixture.MaintenanceConnectionString))
        {
            await maintenance.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", maintenance);
            await create.ExecuteNonQueryAsync();
        }
        var connection = new NpgsqlConnectionStringBuilder(TestDbFixture.ConnectionString) { Database = name }.ConnectionString;
        await using var owned = new IsolatedTestSchema(name, connection);
        var options = TestDbFixture.CreateDbContextOptions(connection);
        await using var db = new AppDbContext(options);
        var migrations = db.Database.GetMigrations().ToArray();
        var custody = Array.FindIndex(migrations, m => m.EndsWith("_VerificationExecutionCustody", StringComparison.Ordinal));
        custody.ShouldBeGreaterThan(0);
        await db.GetService<IMigrator>().MigrateAsync(migrations[custody - 1]);
        var historical = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AgentTasks" ("Id", "RootTaskId", "Depth", "Title", "Goal", "Kind", "Role",
                "ModelLevel", "Attempt", "MaxAttempts", "WorkingDirectory", "Ephemeral", "Status", "ReplyTo",
                "ConcurrencyToken", "CreatedAt", "TokensIn", "TokensOut", "CostUsd", "Workspace", "WorktreeBranch", "WorktreePath")
            VALUES ({historical}, {historical}, 0, 'legacy', 'legacy fixture', 0, 0, 0, 0, 1, 'fixture', false,
                {(int)AgentTaskStatus.Succeeded}, 0, {Guid.NewGuid()}, {DateTime.UtcNow}, 0, 0, 0,
                {(int)WorkspaceMode.Shared}, NULL, NULL)
            """);
        await db.GetService<IMigrator>().MigrateAsync();
        await using var observer = new AppDbContext(options);
        var survived = await observer.AgentTasks.SingleAsync(t => t.Id == historical);
        survived.SourceLandingOperationId.ShouldBeNull();
        survived.SourceLandingSha.ShouldBeNull();
        survived.VerificationCleanupResidue.ShouldBeNull();
        survived.VerificationExecutionRevision.ShouldBe(0);

        await using var world = await PostLandMutationWorld.CreateAsync();
        await using var scope = world.Host.Services.CreateAsyncScope();
        var detail = await world.TaskService(scope.ServiceProvider).GetAsync(world.TaskId, default);
        detail.SourceLandingOperationId.ShouldBe(world.Operation);
        detail.SourceLandingSha.ShouldBe(world.Host.Fixture.SeedSha);
        detail.VerificationExecutions.ShouldNotBeNull();
        detail.VerificationExecutions.Count.ShouldBe(0);
        await using var live = world.Host.CreateContext();
        var delete = await live.AgentTasks.SingleAsync(t => t.Id == world.Host.Fixture.TaskId);
        live.AgentTasks.Remove(delete);
        var error = await Should.ThrowAsync<DbUpdateException>(() => live.SaveChangesAsync());
        ((PostgresException)error.InnerException!).SqlState.ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
    }

    [Test]
    public async Task C478_G001_SourceExists()
    {
        await using var world = await PostLandMutationWorld.CreateAsync(provision: false);
        await using var scope = world.Host.Services.CreateAsyncScope();
        var missing = Guid.NewGuid();
        var ex = await Should.ThrowAsync<ConflictException>(() => world.TaskService(scope.ServiceProvider)
            .CreateAsync(world.Request(world.Companion) with { SourceLandingOperationId = missing }, world.Caller, default));
        ex.Code.ShouldBe("verification_source_unconfirmed");
        await using var observer = world.Host.CreateContext();
        (await observer.AgentTasks.CountAsync(t => t.SourceLandingOperationId == missing)).ShouldBe(0);
    }

    [Test]
    public async Task C478_G002_SourceTaskExists()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await using var db = world.Host.CreateContext();
        var source = await db.AgentTasks.SingleAsync(t => t.Id == world.Host.Fixture.TaskId);
        db.AgentTasks.Remove(source);
        var error = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        ((PostgresException)error.InnerException!).SqlState.ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
        (await db.AgentTaskLandings.CountAsync(o => o.Id == world.Operation)).ShouldBe(1);
    }

    [Test]
    public async Task C478_G003_Role()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.CancelOpenAsync();
        await using var scope = world.Host.Services.CreateAsyncScope();
        await Should.ThrowAsync<HttpException>(() => world.TaskService(scope.ServiceProvider)
            .CreateAsync(world.Request(world.Companion) with { Role = AgentTaskRole.Code }, world.Caller, default));
    }

    [Test]
    public async Task C478_G004_Worker()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.CancelOpenAsync();
        await using var scope = world.Host.Services.CreateAsyncScope();
        await Should.ThrowAsync<HttpException>(() => world.TaskService(scope.ServiceProvider)
            .CreateAsync(world.Request(world.Companion) with { Kind = AgentTaskKind.Orchestrator }, world.Caller, default));
    }

    [Test]
    public async Task C478_G005_Workspace()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.CancelOpenAsync();
        await using var scope = world.Host.Services.CreateAsyncScope();
        foreach (var workspace in new[] { WorkspaceMode.ReadOnly, WorkspaceMode.Shared })
            await Should.ThrowAsync<HttpException>(() => world.TaskService(scope.ServiceProvider)
                .CreateAsync(world.Request(world.Companion) with { Workspace = workspace }, world.Caller, default));
    }

    [Test]
    public async Task C478_G006_Standing()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.CancelOpenAsync();
        await using var scope = world.Host.Services.CreateAsyncScope();
        await Should.ThrowAsync<HttpException>(() => world.TaskService(scope.ServiceProvider)
            .CreateAsync(world.Request(world.Companion) with { AgentId = Guid.NewGuid() }, world.Caller, default));
    }

    [Test]
    public async Task C478_G007_MergeTarget()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.CancelOpenAsync();
        await using var scope = world.Host.Services.CreateAsyncScope();
        await Should.ThrowAsync<HttpException>(() => world.TaskService(scope.ServiceProvider)
            .CreateAsync(world.Request(world.Companion) with { MergeTargetRef = "master" }, world.Caller, default));
    }

    [Test]
    public async Task C478_G008_Authorization()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.CancelOpenAsync();
        var stranger = Path.Combine(Path.GetTempPath(), "c478-unauth-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stranger);
        try
        {
            await using var scope = world.Host.Services.CreateAsyncScope();
            var admission = scope.ServiceProvider.GetRequiredService<SourceLandingAdmission>();
            await admission.RequireAuthorizedDirectoryAsync(world.Host.Fixture.Repository, world.Host.Fixture.Repository, [], default);
            await Should.ThrowAsync<ForbiddenException>(() =>
                admission.RequireAuthorizedDirectoryAsync(stranger, world.Host.Fixture.Repository, [], default));
            await using var observer = world.Host.CreateContext();
            (await observer.AgentTasks.CountAsync(t => t.SourceLandingOperationId == world.Operation
                && t.Status != AgentTaskStatus.Canceled)).ShouldBe(0);
        }
        finally { Directory.Delete(stranger, true); }
    }

    [Test]
    public async Task C478_G009_Repository()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.CancelOpenAsync();
        await using (var db = world.Host.CreateContext())
        {
            var op = await db.AgentTaskLandings.SingleAsync(o => o.Id == world.Operation);
            op.RepositoryPath = Path.Combine(world.Host.Fixture.Root, "other-repo");
            op.CommonDirectory = Path.Combine(world.Host.Fixture.Root, "other-repo", ".git");
            Directory.CreateDirectory(op.CommonDirectory);
            await db.SaveChangesAsync();
        }
        await using var scope = world.Host.Services.CreateAsyncScope();
        var ex = await Should.ThrowAsync<ConflictException>(() => world.TaskService(scope.ServiceProvider)
            .CreateAsync(world.Request(world.Companion), world.Caller, default));
        ex.Code.ShouldBe("verification_source_repository_mismatch");
    }

    [Test]
    public async Task C478_G010_Project()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.CancelOpenAsync();
        await using (var db = world.Host.CreateContext())
        {
            var other = new Project { Id = Guid.NewGuid(), Name = "other-project", GitRepositoryUrl = "https://example.test/other.git" };
            db.Projects.Add(other);
            (await db.AgentTasks.SingleAsync(t => t.Id == world.Host.Fixture.TaskId)).ProjectId = other.Id;
            await db.SaveChangesAsync();
        }
        await using var scope = world.Host.Services.CreateAsyncScope();
        var ex = await Should.ThrowAsync<ConflictException>(() => world.TaskService(scope.ServiceProvider)
            .CreateAsync(world.Request(world.Companion), world.Caller, default));
        ex.Code.ShouldBe("verification_source_identity_mismatch");
    }

    [Test]
    public async Task C478_G013_Board()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.CancelOpenAsync();
        Guid foreignCard;
        await using (var db = world.Host.CreateContext())
        {
            var project = await db.Projects.SingleAsync();
            var board = new Board { Id = Guid.NewGuid(), ProjectId = project.Id, Name = "foreign" };
            var column = new BoardColumn { Id = Guid.NewGuid(), BoardId = board.Id, Name = "Backlog", StateKey = "backlog", CardStatus = CardStatus.Backlog };
            var card = new Card { Id = Guid.NewGuid(), BoardId = board.Id, BoardColumnId = column.Id, Identifier = "CARD-0001", Title = "foreign" };
            db.Boards.Add(board); db.BoardColumns.Add(column); db.Cards.Add(card);
            await db.SaveChangesAsync();
            foreignCard = card.Id;
        }
        await using var scope = world.Host.Services.CreateAsyncScope();
        var ex = await Should.ThrowAsync<ConflictException>(() => world.TaskService(scope.ServiceProvider)
            .CreateAsync(world.Request(foreignCard), world.Caller, default));
        ex.Code.ShouldBe("verification_source_card_mismatch");
    }

    [Test]
    public async Task C478_G017_GlobalCap()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.CancelOpenAsync();
        await using (var db = world.Host.CreateContext())
        {
            var project = await db.Projects.SingleAsync();
            (await db.AgentTasks.SingleAsync(t => t.Id == world.Host.Fixture.TaskId)).ProjectId = project.Id;
            for (var i = 0; i < 3; i++)
            {
                var id = Guid.NewGuid();
                db.AgentTasks.Add(new AgentTask
                {
                    Id = id, RootTaskId = id, Title = "cap-" + i, Goal = "cap", Kind = AgentTaskKind.Worker,
                    Role = AgentTaskRole.Code, ProjectId = project.Id, Status = AgentTaskStatus.Working,
                    WorkingDirectory = world.Host.Fixture.Repository, CreatedAt = DateTime.UtcNow,
                });
            }
            await db.SaveChangesAsync();
        }
        await using var scope = world.Host.Services.CreateAsyncScope();
        await using var projects = world.Host.CreateContext();
        var projectId = await projects.Projects.Select(p => p.Id).SingleAsync();
        var caller = new AgentTaskService.Caller(null, null, world.Host.Fixture.Repository, ProjectId: projectId);
        await Should.ThrowAsync<ConcurrencyLimitException>(() => world.TaskService(scope.ServiceProvider)
            .CreateAsync(world.Request(world.Companion), caller, default));
        await using var observer = world.Host.CreateContext();
        (await observer.AgentTasks.CountAsync(t => t.SourceLandingOperationId == world.Operation
            && t.Status != AgentTaskStatus.Canceled)).ShouldBe(0);
    }

    [Test]
    public async Task C478_G018_ProjectCap()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.CancelOpenAsync();
        Guid projectId;
        Guid? otherProject;
        await using (var db = world.Host.CreateContext())
        {
            var project = await db.Projects.SingleAsync();
            projectId = project.Id;
            (await db.AgentTasks.SingleAsync(t => t.Id == world.Host.Fixture.TaskId)).ProjectId = project.Id;
            var occupant = Guid.NewGuid();
            db.AgentTasks.Add(new AgentTask
            {
                Id = occupant, RootTaskId = occupant, Title = "occupant", Goal = "occupant", Kind = AgentTaskKind.Worker,
                Role = AgentTaskRole.Mutation, ProjectId = project.Id, Status = AgentTaskStatus.Working,
                WorkingDirectory = world.Host.Fixture.Repository, CreatedAt = DateTime.UtcNow,
            });
            var other = new Project { Id = Guid.NewGuid(), Name = "unrelated", GitRepositoryUrl = "https://example.test/unrelated.git" };
            db.Projects.Add(other);
            otherProject = other.Id;
            await db.SaveChangesAsync();
        }
        await using var scope = world.Host.Services.CreateAsyncScope();
        var service = world.TaskService(scope.ServiceProvider);
        var caller = new AgentTaskService.Caller(null, null, world.Host.Fixture.Repository, ProjectId: projectId);
        await Should.ThrowAsync<ConcurrencyLimitException>(() =>
            service.CreateAsync(world.Request(world.Companion), caller, default));
        var unrelated = await service.CreateAsync(new CreateAgentTaskRequest("unrelated mutation", Role: AgentTaskRole.Mutation),
            new AgentTaskService.Caller(null, null, world.Host.Fixture.Repository, ProjectId: otherProject), default);
        unrelated.Status.ShouldBe(AgentTaskStatus.Queued);
        await using var observer = world.Host.CreateContext();
        (await observer.AgentTasks.SingleAsync(t => t.Id == unrelated.Id)).SourceLandingOperationId.ShouldBeNull();
    }

    [Test]
    public async Task C478_G019_Provider()
    {
        await using var world = await PostLandMutationWorld.CreateAsync(provision: false, custodySupport: false);
        await using var scope = world.Host.Services.CreateAsyncScope();
        var ex = await Should.ThrowAsync<ConflictException>(() => world.TaskService(scope.ServiceProvider)
            .CreateAsync(world.Request(world.Companion), world.Caller, default));
        ex.Code.ShouldBe("verification_custody_unsupported_backend");
        await using var observer = world.Host.CreateContext();
        (await observer.AgentTasks.CountAsync(t => t.SourceLandingOperationId == world.Operation)).ShouldBe(0);
    }

    [Test]
    public async Task C478_G020_Pin()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.CancelOpenAsync();
        await using var scope = world.Host.Services.CreateAsyncScope();
        await Should.ThrowAsync<HttpException>(() => world.TaskService(scope.ServiceProvider)
            .CreateAsync(world.Request(world.Companion) with { AgentId = Guid.NewGuid() }, world.Caller, default));
        await using var observer = world.Host.CreateContext();
        (await observer.AgentTasks.CountAsync(t => t.SourceLandingOperationId == world.Operation
            && t.Status != AgentTaskStatus.Canceled)).ShouldBe(0);
    }

    [Test]
    public async Task C478_G011_CardRequired()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.CancelOpenAsync();
        await using var scope = world.Host.Services.CreateAsyncScope();
        await Should.ThrowAsync<ConflictException>(() => world.TaskService(scope.ServiceProvider)
            .CreateAsync(world.Request(world.Companion) with { Card = null }, world.Caller, default));
    }

    [Test]
    public async Task C478_G012_DistinctCard()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.CancelOpenAsync();
        await using var scope = world.Host.Services.CreateAsyncScope();
        await Should.ThrowAsync<ConflictException>(() => world.TaskService(scope.ServiceProvider)
            .CreateAsync(world.Request(world.Original), world.Caller, default));
    }

    [Test]
    public async Task C478_G014_Publication()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.CancelOpenAsync();
        await using (var db = world.Host.CreateContext())
        {
            var op = await db.AgentTaskLandings.SingleAsync(o => o.Id == world.Operation);
            op.RemoteConfirmedAt = null;
            await db.SaveChangesAsync();
        }
        await using var scope = world.Host.Services.CreateAsyncScope();
        await Should.ThrowAsync<ConflictException>(() => world.TaskService(scope.ServiceProvider)
            .CreateAsync(world.Request(world.Companion), world.Caller, default));
    }

    [Test]
    public async Task C478_G015_OpenOperation()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await using var scope = world.Host.Services.CreateAsyncScope();
        var ex = await Should.ThrowAsync<ConflictException>(() => world.TaskService(scope.ServiceProvider)
            .CreateAsync(world.Request(world.OtherCompanion), world.Caller, default));
        ex.Message.ShouldContain(world.TaskId.ToString("D"));
    }

    [Test]
    public async Task C478_G016_AtomicAdmission()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.CancelOpenAsync();
        async Task<bool> Attempt(Guid card)
        {
            await using var scope = world.Host.Services.CreateAsyncScope();
            try { await world.TaskService(scope.ServiceProvider).CreateAsync(world.Request(card), world.Caller, default); return true; }
            catch (ConflictException ex) when (ex.Message.Contains("already has open task")) { return false; }
        }
        (await Task.WhenAll(Attempt(world.Companion), Attempt(world.OtherCompanion))).Count(ok => ok).ShouldBe(1);
    }

    [Test]
    public async Task C478_G021_SourcePersist()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await using var observer = world.Host.CreateContext();
        var task = await observer.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
        task.SourceLandingOperationId.ShouldBe(world.Operation);
        task.SourceLandingSha.ShouldBe(world.Host.Fixture.SeedSha);
    }

    [Test]
    public async Task C478_G022_SourceImmutable()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await using var db = world.Host.CreateContext();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
        task.SourceLandingSha = new string('f', 40);
        await Should.ThrowAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Test]
    public async Task C478_G023_SourceDelete()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await using var db = world.Host.CreateContext();
        db.AgentTaskLandings.Remove(await db.AgentTaskLandings.SingleAsync(o => o.Id == world.Operation));
        var error = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        ((PostgresException)error.InnerException!).SqlState.ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
    }

    [Test]
    public async Task C478_G024_Detail()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await using var scope = world.Host.Services.CreateAsyncScope();
        var detail = await world.TaskService(scope.ServiceProvider).GetAsync(world.TaskId, default);
        detail.SourceLandingOperationId.ShouldBe(world.Operation);
        detail.SourceLandingSha.ShouldBe(world.Host.Fixture.SeedSha);
        var historical = await world.TaskService(scope.ServiceProvider).GetAsync(world.Host.Fixture.TaskId, default);
        historical.SourceLandingOperationId.ShouldBeNull();
        historical.SourceLandingSha.ShouldBeNull();
    }

    [Test]
    public async Task C478_G025_CliSource()
    {
        using var server = new StubApi();
        var operation = Guid.NewGuid();
        var card = Guid.NewGuid();
        var run = await DelegateScriptRunner.RunAsync(server.BaseUrl, "-Role", "Mutation", "-Worktree",
            "-SourceLanding", operation.ToString("D"), "-Card", card.ToString("D"), "-Goal", "post-land battery");
        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastCreateBody.ShouldNotBeNull().RootElement;
        body.GetProperty("sourceLandingOperationId").GetString().ShouldBe(operation.ToString("D"));
        body.GetProperty("workspace").GetString().ShouldBe("Worktree");
        body.GetProperty("role").GetString().ShouldBe("Mutation");
        body.GetProperty("card").GetString().ShouldBe(card.ToString("D"));
        server.LastPath.ShouldStartWith("/api/agent-tasks");
        server.LastMethod.ShouldBe("POST");
        server.LandCalls.ShouldBe(0);
    }

    [Test]
    public async Task C478_G026_CliCleanup()
    {
        using var server = new StubApi();
        var taskId = Guid.NewGuid();
        var run = await DelegateScriptRunner.RunAsync(server.BaseUrl, "-CleanupVerification", taskId.ToString("D"));
        run.ExitCode.ShouldBe(0, run.Output);
        server.LastMethod.ShouldBe("POST");
        server.LastPath.ShouldBe($"/api/agent-tasks/{taskId:D}/cleanup-verification");
        server.LandCalls.ShouldBe(0);
        server.CreateCalls.ShouldBe(0);
    }

    private sealed class StubApi : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _pump;

        public StubApi()
        {
            BaseUrl = EphemeralHttpListener.BindLoopback(_listener);
            _pump = Task.Run(PumpAsync);
        }

        public string BaseUrl { get; }
        public string? LastMethod { get; private set; }
        public string? LastPath { get; private set; }
        public JsonDocument? LastCreateBody { get; private set; }
        public int LandCalls { get; private set; }
        public int CreateCalls { get; private set; }

        private async Task PumpAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch (Exception) { return; }
                LastMethod = context.Request.HttpMethod;
                LastPath = context.Request.Url?.AbsolutePath;
                JsonDocument? body = null;
                using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                {
                    var raw = await reader.ReadToEndAsync();
                    if (!string.IsNullOrWhiteSpace(raw))
                        body = JsonDocument.Parse(raw);
                }
                if (LastPath?.EndsWith("/land", StringComparison.Ordinal) == true) LandCalls++;
                else if (LastMethod == "POST" && LastPath is "/api/agent-tasks" or "/api/agent-tasks/")
                {
                    CreateCalls++;
                    LastCreateBody = body;
                }
                var payload = Encoding.UTF8.GetBytes("""{"id":"00000000-0000-0000-0000-000000000001","directoryGone":false}""");
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(payload);
                context.Response.Close();
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Close();
            try { _pump.Wait(TimeSpan.FromSeconds(2)); } catch { }
        }
    }
}
