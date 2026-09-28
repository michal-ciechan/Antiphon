using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class ExpectationObservationAdapterTests
{
    [Test]
    public async Task C650_Composition_reads_actual_catalog_and_policy()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = await ExpectationTestWorld.CreateAsync(schema.ConnectionString);
        await using var db = world.Db();
        await using var provider = new ServiceCollection().BuildServiceProvider();
        var directory = new PhoneHomeRunnerDirectory(null!,
            Options.Create(new PhoneHomeRunnerSettings { Enabled = true, AllowedRunnerId = "server2" }),
            provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System);
        var adapter = new ExpectationObservationAdapter(db, directory, null!, null!, null!,
            Options.Create(new SubscriptionQuotaGateSettings()), TimeProvider.System);
        var catalog = await adapter.ReferencesAsync(world.Directive, CancellationToken.None);
        catalog.Agents[world.AgentId].BoardId.ShouldBe(world.BoardId);
        catalog.CardBoards[world.CardId].ShouldBe(world.BoardId);
        catalog.ChannelsEnabled[world.Directive.OperatorChannelId].ShouldBeTrue();
        catalog.ConfiguredRunnerIds.ShouldContain("server2");
        ExpectationDirectiveReferences.Evaluate(world.Directive, catalog).ShouldBeEmpty();
        await db.ChatChannels.Where(c => c.Id == world.Directive.OperatorChannelId)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.Enabled, false));
        var changed = await adapter.ReferencesAsync(world.Directive, CancellationToken.None);
        ExpectationDirectiveReferences.Evaluate(world.Directive, changed)
            .Select(f => f.Code).ShouldContain("channel_disabled");
    }

    [Test]
    public async Task C650_Unknown_runner_preserves_other_due_conditions()
    {
        var now = new DateTime(2026, 9, 28, 1, 0, 0, DateTimeKind.Utc);
        var directive = new ExpectationDirectiveSettings { Id = "d", Enabled = true, BoardId = Guid.NewGuid() };
        var note = Guid.NewGuid();
        var eval = ExpectationWatchdogPolicy.Evaluate(new ExpectationSnapshot
        {
            AsOf = now,
            OpenEpisodes = [new ExpectationOpenEpisode { Kind = ExpectationEpisodeKind.CapacityDeficit,
                SubjectKey = ExpectationSubjects.Capacity("d", "server2"), FirstObservedAt = now.AddMinutes(-20) }],
            Notes = [new ExpectationNoteDebt { NotificationId = note, TaskId = Guid.NewGuid(),
                Kind = LandNotificationKind.Held, State = LandNotificationState.Queued,
                CreatedAt = now.AddMinutes(-11) }],
            Lanes = [new ExpectationLaneSnapshot { RunnerId = "server2", Target = 3,
                ObservationKnown = false, DeficitSince = now.AddMinutes(-20) }],
        }, directive);
        eval.UndeliveredNotes.ShouldHaveSingleItem().SubjectKey.ShouldBe(ExpectationSubjects.Note("d", note));
        eval.Capacity.ShouldHaveSingleItem().IsDue.ShouldBeFalse();
        eval.UnknownSubjectKeys.ShouldContain(ExpectationSubjects.Capacity("d", "server2"));
        await Task.CompletedTask;
    }

    [Test]
    public async Task C650_Prepared_remote_path_defeats_repository_wide_fence()
    {
        var now = new DateTime(2026, 9, 28, 1, 0, 0, DateTimeKind.Utc);
        var repo = "repo:/src/antiphon";
        var directive = new ExpectationDirectiveSettings { Id = "d", Enabled = true, BoardId = Guid.NewGuid() };
        var snapshot = new ExpectationSnapshot
        {
            AsOf = now, RepositoryScope = repo,
            Queued =
            [
                new ExpectationQueuedTask { TaskId = Guid.NewGuid(), RepositoryScope = repo,
                    StintStartedAt = now.AddMinutes(-1), HoldClass = ExpectationHoldClass.RepositoryFenced },
                new ExpectationQueuedTask { TaskId = Guid.NewGuid(), RunnerId = "server2",
                    RepositoryScope = repo, StintStartedAt = now.AddMinutes(-1), RemotePrepared = true,
                    HoldClass = ExpectationHoldClass.RepositoryFenced },
            ],
        };
        var eval = ExpectationWatchdogPolicy.Evaluate(snapshot, directive);
        eval.DispatchFence.ShouldBeNull();
        eval.ScopedFences.ShouldNotBeEmpty();
        await Task.CompletedTask;
    }

    [Test]
    public async Task C650_Live_dead_and_unknown_journals_do_not_grant_recovery()
    {
        using var git = new ControlledLandingGit();
        var common = git.CommonDir;
        var children = Path.Combine(common, "antiphon", "children");
        Directory.CreateDirectory(children);
        var path = Path.Combine(children, Guid.NewGuid().ToString("N") + ".json");
        var record = new RepositoryChildJournal.ChildRecord(1, common, 1234, 5678);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(record));
        var now = DateTimeOffset.UtcNow;
        File.SetLastWriteTimeUtc(path, now.AddMinutes(-10).UtcDateTime);
        var inspector = new RepositoryChildJournalInspector(git);
        foreach (var (live, expected) in new (bool?, JournalRecordState)[]
            { (true, JournalRecordState.Alive), (false, JournalRecordState.Dead), (null, JournalRecordState.Unknown) })
        {
            git.ProcessAlive = live;
            var finding = (await inspector.InspectCommonAsync(common, TimeSpan.FromMinutes(5), now,
                CancellationToken.None)).Findings.ShouldHaveSingleItem();
            finding.State.ShouldBe(expected);
            File.Exists(path).ShouldBeTrue();
        }
        git.Commands.ShouldBeEmpty(); // Already resolved common directory: no git command on the read path.

        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = await ExpectationTestWorld.CreateAsync(schema.ConnectionString);
        var taskId = Guid.NewGuid();
        await using var db = world.Db();
        var task = world.Task(taskId, AgentTaskStatus.Queued, null);
        task.RepoPath = git.Repository;
        db.AgentTasks.Add(task);
        db.AgentTaskLandRequests.Add(new AgentTaskLandRequest
        {
            Id = Guid.NewGuid(), TaskId = taskId, IsPending = true,
            RepositoryPathSnapshot = git.Repository, SourceCommonDirectory = common,
            RequestedAt = world.Now, LastEvaluatedAt = world.Now, LastProgressAt = world.Now,
        });
        await db.SaveChangesAsync();
        await using var services = new ServiceCollection().BuildServiceProvider();
        var directory = new PhoneHomeRunnerDirectory(null!,
            Options.Create(new PhoneHomeRunnerSettings { Enabled = true, AllowedRunnerId = "server2" }),
            services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System);
        var adapter = new ExpectationObservationAdapter(db, directory,
            new SubscriptionUsageReader(db, TimeProvider.System), new NeverHeld(),
            new AgentTuiRunnerCatalog(), Options.Create(new SubscriptionQuotaGateSettings()),
            TimeProvider.System, journalInspector: inspector);
        var observed = await adapter.ObserveAsync(world.Directive, CancellationToken.None);
        observed.Journals![ExpectationRepository.For(git.Repository, world.BoardId)].Unknown.ShouldBe(1);
        git.Commands.ShouldBeEmpty();
    }

    [Test]
    public async Task C650_Catchup_precedes_absence_judgment()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = await ExpectationTestWorld.CreateAsync(schema.ConnectionString);
        var taskId = Guid.NewGuid();
        var queue = Note(world, taskId, 1);
        queue.LastDeliveryBaselineSequence = 0;
        queue.DeliveryAttempts = 1;
        await using (var db = world.Db())
        {
            db.AgentTasks.Add(world.Task(taskId, AgentTaskStatus.Succeeded, world.Now.AddMinutes(-20)));
            db.SessionQueuedMessages.Add(queue);
            await db.SaveChangesAsync();
        }
        var catchUp = new InsertPrompt(world, queue);
        var clock = new FakeTimeProvider(new DateTimeOffset(world.Now, TimeSpan.Zero));
        await using var scanDb = world.Db();
        var scan = await world.Service(scanDb, clock, catchUp).ScanAsync(world.Directive,
            ExpectationProbeInput.None, CancellationToken.None);
        catchUp.Calls.ShouldBe(1);
        scan.NudgesCommitted.ShouldBe(0);
        await using var read = world.Db();
        (await read.ExpectationNudges.CountAsync()).ShouldBe(0);
        (await read.SessionQueuedMessages.SingleAsync()).Status.ShouldBe(QueuedMessageStatus.Pending);
    }

    [Test]
    public async Task C650_Paged_observation_eventually_visits_all_subjects()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = await ExpectationTestWorld.CreateAsync(schema.ConnectionString);
        var taskId = Guid.NewGuid();
        await using (var db = world.Db())
        {
            db.AgentTasks.Add(world.Task(taskId, AgentTaskStatus.Succeeded, world.Now.AddMinutes(-30)));
            db.SessionQueuedMessages.AddRange(Enumerable.Range(1, 105).Select(i => Note(world, taskId, i)));
            await db.SaveChangesAsync();
        }
        await using var firstDb = world.Db();
        var first = await new ExpectationSnapshotReader(firstDb).ReadAsync(world.Directive,
            world.Digest, world.Now, ExpectationProbeInput.None, CancellationToken.None);
        first.Notes.Count.ShouldBe(100);
        first.NoteCoverageIncomplete.ShouldBeTrue();
        var clock = new FakeTimeProvider(new DateTimeOffset(world.Now, TimeSpan.Zero));
        await using (var scanDb = world.Db())
            await world.Service(scanDb, clock).ScanAsync(world.Directive, ExpectationProbeInput.None, CancellationToken.None);
        await using var secondDb = world.Db();
        var second = await new ExpectationSnapshotReader(secondDb).ReadAsync(world.Directive,
            world.Digest, world.Now.AddMinutes(1), ExpectationProbeInput.None, CancellationToken.None);
        second.Notes.Count.ShouldBe(5);
        first.Notes.Select(n => n.NotificationId).Concat(second.Notes.Select(n => n.NotificationId))
            .Distinct().Count().ShouldBe(105);
        clock.Advance(TimeSpan.FromMinutes(1));
        await using (var scanDb = world.Db())
            await world.Service(scanDb, clock).ScanAsync(world.Directive, ExpectationProbeInput.None, CancellationToken.None);
        await using var verify = world.Db();
        (await verify.ExpectationEpisodes.CountAsync(e => e.ResolvedAt == null)).ShouldBe(105);
    }

    private static SessionQueuedMessage Note(ExpectationTestWorld world, Guid taskId, int sequence)
    {
        var row = ExpectationTestWorld.Queued(Guid.NewGuid(), world.OwnedSessionId,
            QueuedMessageStatus.Pending, world.Now.AddMinutes(-11), sequence);
        row.Origin = QueuedMessageOrigin.Delegation;
        row.SourceTaskId = taskId;
        row.NoteHeader = "Completion for task " + taskId.ToString("D");
        row.Body = "[task " + taskId.ToString("D") + "] completed " + sequence;
        return row;
    }

    private sealed class InsertPrompt(ExpectationTestWorld world, SessionQueuedMessage queue) : IExpectationCatchUp
    {
        public int Calls { get; private set; }
        public async Task CatchUpAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken ct)
        {
            Calls++;
            sessionIds.ShouldContain(world.OwnedSessionId);
            await using var db = world.Db();
            db.TranscriptEntries.Add(ExpectationTestWorld.Transcript(world.OwnedSessionId, 1,
                TranscriptKinds.UserPrompt, world.Now, queue.Body));
            await db.SaveChangesAsync(ct);
        }
    }
    private sealed class NeverHeld : IModelAvailability
    {
        public Task<bool> IsHeldAsync(AgentKind kind, string alias, CancellationToken ct) => Task.FromResult(false);
    }
}
