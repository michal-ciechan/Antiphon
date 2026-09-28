using System.Text.Json;
using System.Net;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Agents;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Api;

[Category("Integration")]
public sealed class ExpectationStatusTests
{
    [Test]
    public async Task C650_Status_exposes_scan_and_delivery_evidence()
    {
        await using var f = await Fixture.CreateAsync();
        var task = Guid.NewGuid();
        await using (var db = f.World.Db())
        {
            db.AgentTasks.Add(f.World.Task(task, AgentTaskStatus.Dispatched, f.World.Now.AddMinutes(-20)));
            await db.SaveChangesAsync();
        }
        await f.ScanAsync();
        var status = await f.ReadAsync();
        status.Enabled.ShouldBeTrue();
        status.ConfigDigest.ShouldBe(f.World.Digest);
        status.LastSuccessfulScanAt.ShouldBe(f.World.Now);
        status.Lanes.Single(l => l.RunnerId == "local").Running.ShouldBe(1);
        status.Episodes.ShouldHaveSingleItem().SubjectKey.ShouldContain(task.ToString("D"));
        var nudge = status.Nudges.ShouldHaveSingleItem();
        nudge.AttemptState.ShouldBe("None");
        nudge.AuditCommentId.ShouldNotBe(Guid.Empty);
    }

    [Test]
    public async Task C650_Status_requires_authorized_board_and_paginates()
    {
        await using var f = await Fixture.CreateAsync();
        await using (var db = f.World.Db())
        {
            db.AgentTasks.Add(f.World.Task(Guid.NewGuid(), AgentTaskStatus.Dispatched,
                f.World.Now.AddMinutes(-20)));
            db.AgentTasks.Add(f.World.Task(Guid.NewGuid(), AgentTaskStatus.Dispatched,
                f.World.Now.AddMinutes(-20)));
            await db.SaveChangesAsync();
        }
        await f.ScanAsync();
        (await f.ReadAsync(skip: 0, take: 1)).HasMore.ShouldBeTrue();
        var page1 = await f.ReadAsync(skip: 0, take: 1);
        var page2 = await f.ReadAsync(skip: 1, take: 1);
        page1.Episodes.ShouldHaveSingleItem().Id.ShouldNotBe(page2.Episodes.ShouldHaveSingleItem().Id);
        page2.HasMore.ShouldBeFalse();
        await Should.ThrowAsync<Antiphon.Server.Application.Exceptions.NotFoundException>(() =>
            f.ReadBoardAsync(Guid.NewGuid()));

        await using var host = new AntiphonWebAppFactory();
        using var client = host.CreateClient();
        string connection;
        using (var scope = host.Services.CreateScope())
            connection = scope.ServiceProvider.GetRequiredService<Antiphon.Server.Infrastructure.Data.AppDbContext>()
                .Database.GetDbConnection().ConnectionString;
        var own = await ExpectationTestWorld.CreateAsync(connection);
        var foreignBoardId = Guid.NewGuid();
        var token = "c650-" + Guid.NewGuid().ToString("N");
        await using (var db = own.Db())
        {
            db.Boards.Add(new Board
            {
                Id = foreignBoardId, ProjectId = own.ProjectId,
                Name = "c650-foreign-" + foreignBoardId.ToString("N"),
                CreatedAt = own.Now, UpdatedAt = own.Now,
            });
            var task = own.Task(Guid.NewGuid(), AgentTaskStatus.Working, own.Now);
            task.TokenHash = AgentTaskService.HashToken(token);
            db.AgentTasks.Add(task);
            await db.SaveChangesAsync();
        }
        client.DefaultRequestHeaders.Add("X-Antiphon-Task-Token", token);
        (await client.GetAsync("/api/expectation-watchdog?boardId=" + own.BoardId.ToString("D")))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/api/expectation-watchdog?boardId=" + foreignBoardId.ToString("D")))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task C650_Status_never_exposes_channel_address_or_transcripts()
    {
        await using var f = await Fixture.CreateAsync();
        var task = Guid.NewGuid();
        await using (var db = f.World.Db())
        {
            db.AgentTasks.Add(f.World.Task(task, AgentTaskStatus.Dispatched, f.World.Now.AddMinutes(-20)));
            db.TranscriptEntries.Add(ExpectationTestWorld.Transcript(f.World.OwnedSessionId, 1,
                Antiphon.SessionRunner.Contracts.TranscriptKinds.AssistantText, f.World.Now,
                "PRIVATE_TRANSCRIPT_C650"));
            await db.SaveChangesAsync();
        }
        await f.ScanAsync();
        var json = JsonSerializer.Serialize(await f.ReadAsync());
        json.ShouldNotContain("c650-operator-");
        json.ShouldNotContain("PRIVATE_TRANSCRIPT_C650");
        json.ShouldNotContain("OperatorPageBody");
        json.ShouldContain("AuditCommentId");
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(IsolatedTestSchema schema, ExpectationTestWorld world)
        { Schema = schema; World = world; }
        public IsolatedTestSchema Schema { get; }
        public ExpectationTestWorld World { get; }
        public static async Task<Fixture> CreateAsync()
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            return new Fixture(schema, await ExpectationTestWorld.CreateAsync(schema.ConnectionString));
        }
        public async Task ScanAsync()
        {
            await using var db = World.Db();
            var clock = new FakeTimeProvider(new DateTimeOffset(World.Now, TimeSpan.Zero));
            await World.Service(db, clock).ScanAsync(World.Directive, ExpectationProbeInput.None, CancellationToken.None);
        }
        public Task<ExpectationWatchdogStatus> ReadAsync(int skip = 0, int take = 20) =>
            ReadBoardAsync(World.BoardId, skip, take);
        public async Task<ExpectationWatchdogStatus> ReadBoardAsync(Guid board, int skip = 0, int take = 20)
        {
            await using var db = World.Db();
            await using var services = new ServiceCollection().BuildServiceProvider();
            var directory = new PhoneHomeRunnerDirectory(null!,
                Options.Create(new PhoneHomeRunnerSettings { Enabled = true, AllowedRunnerId = "server2" }),
                services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System);
            var adapter = new ExpectationObservationAdapter(db, directory, null!, null!, null!,
                Options.Create(new SubscriptionQuotaGateSettings()), TimeProvider.System);
            var status = new ExpectationWatchdogStatusService(db, adapter,
                Options.Create(new ExpectationWatchdogSettings { Enabled = true, Directives = [World.Directive] }),
                new FakeTimeProvider(new DateTimeOffset(World.Now, TimeSpan.Zero)));
            return await status.GetAsync(board, skip, take, CancellationToken.None);
        }
        public ValueTask DisposeAsync() => Schema.DisposeAsync();
    }
}
