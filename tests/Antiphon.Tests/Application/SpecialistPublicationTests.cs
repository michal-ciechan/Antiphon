using System.Data.Common;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public class SpecialistPublicationTests
{
    private sealed class FailPublication : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Armed && command.CommandText.Contains("INSERT INTO \"SessionQueuedMessages\"", StringComparison.Ordinal))
            { Armed = false; throw new InvalidOperationException("publication-fixture-write-failed"); }
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    [Test]
    public async Task Card0415_V14_publication_rolls_back_and_two_queue_instances_reuse_one_durable_identity()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var failure = new FailPublication();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        { ConnectionString = schema.ConnectionString, ConfigureDbContext = options => options.AddInterceptors(failure) });
        var taskId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        await using (var scope = h.Provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;
            db.AgentTasks.Add(new() { Id = taskId, RootTaskId = taskId, Status = AgentTaskStatus.Working,
                Role = AgentTaskRole.Code, CheckCount = 1, ParentSessionId = h.SessionId, ReplyTo = AgentTaskReplyTo.Session,
                CreatedAt = now, Title = "publication fixture" });
            // A terminal failed request tests publication independently from model qualification.
            db.SpecialistRequests.Add(new() { Id = requestId, AgentId = h.AgentId, CheckedTaskId = taskId, CheckNumber = 1,
                Purpose = SpecialistRequestPurpose.Check, Status = SpecialistRequestStatus.Failed, Outcome = SpecialistAttemptOutcome.Held,
                StartedAt = now, DeadlineAt = now.AddSeconds(60), CompletedAt = now, HealthAppliedAt = now, CallerMessageId = messageId });
            // Keep the caller busy: persistence must not type into its active turn.
            db.TranscriptEntries.Add(new() { Id = Guid.NewGuid(), AgentSessionId = h.SessionId, Sequence = 1,
                Kind = TranscriptKinds.UserPrompt, Text = "caller is working", CreatedAt = now, Timestamp = now });
            await db.SaveChangesAsync();
        }
        failure.Armed = true;
        var exception = await Should.ThrowAsync<Exception>(() => h.Queue.PublishCheckRequestAsync(requestId, h.SessionId,
            "durable caller note", "durable captured timeline", false, CancellationToken.None));
        exception.ToString().ShouldContain("publication-fixture-write-failed");
        await using (var scope = h.Provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.SpecialistRequests.SingleAsync(r => r.Id == requestId)).CallerPublishedAt.ShouldBeNull();
            (await db.SessionQueuedMessages.CountAsync()).ShouldBe(0);
            (await db.AgentTaskEvents.CountAsync(e => e.Id == requestId)).ShouldBe(0);
        }
        var secondQueue = ActivatorUtilities.CreateInstance<SessionMessageQueueService>(h.Provider);
        await Task.WhenAll(
            h.Queue.PublishCheckRequestAsync(requestId, h.SessionId, "durable caller note", "durable captured timeline", false, CancellationToken.None),
            secondQueue.PublishCheckRequestAsync(requestId, h.SessionId, "durable caller note", "durable captured timeline", false, CancellationToken.None));
        await secondQueue.PublishCheckRequestAsync(requestId, h.SessionId, "must not replace the note", "must not replace the timeline", false, CancellationToken.None);
        await using (var scope = h.Provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var message = await db.SessionQueuedMessages.SingleAsync();
            message.Id.ShouldBe(messageId);
            message.Body.ShouldBe("durable caller note");
            message.Status.ShouldBe(QueuedMessageStatus.Pending);
            (await db.AgentTaskEvents.SingleAsync(e => e.Id == requestId)).Detail.ShouldBe("durable captured timeline");
            (await db.SpecialistRequests.SingleAsync(r => r.Id == requestId)).CallerPublishedAt.ShouldNotBeNull();
            h.Adapter.Inputs.ShouldBeEmpty();
        }
    }
}
