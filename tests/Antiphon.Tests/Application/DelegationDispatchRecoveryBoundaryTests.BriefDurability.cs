using System.Text;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
namespace Antiphon.Tests.Application;

/// <summary>CARD-1150 S2 repair F1: a failed Working probe after the insert keeps the committed brief.</summary>
public partial class DelegationDispatchRecoveryBoundaryTests
{
    [Test]
    public async Task C1150_Working_probe_failure_keeps_the_committed_brief()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var seeded = await SeedCurrentAsync(
            schema.ConnectionString, "Keep this brief when the working probe fails.\ncafé \u2603\n" + new string('z', 1200));
        try
        {
            var loader = new FailingStateLoader();
            await using (var faulted = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
            {
                ConnectionString = schema.ConnectionString,
                PreserveDatabaseOnDispose = true,
                AttachSessionId = seeded.SessionId,
                AttachAgentId = seeded.AgentId,
                ConfigureServices = services => services.AddSingleton(new SessionStateStore(
                    loader, TimeProvider.System, Options.Create(new SessionStateSettings { Enabled = true }))),
            }))
            {
                var failure = await Should.ThrowAsync<IOException>(
                    () => faulted.Queue.EnsureDispatchBriefAsync(RequestFor(seeded), CancellationToken.None));
                failure.Message.ShouldBe(FailingStateLoader.Message, "F1");
                loader.Calls.ShouldBeGreaterThan(0, "F1 the injected probe fault was reached");
            }

            var marker = DelegationReportFormatter.TaskMarker(seeded.TaskId);
            var kept = await SingleBriefAsync(schema.ConnectionString, seeded.TaskId, "F1 the probe fault rolled back the brief");
            kept.Status.ShouldBe(QueuedMessageStatus.Pending, "F1");
            kept.DeliveryAttempts.ShouldBe(0, "F1");
            kept.Body.Contains(marker, StringComparison.Ordinal).ShouldBeTrue("F1");
            var keptBody = Encoding.UTF8.GetBytes(kept.Body);
            var spillPath = DispatchBriefEvidence.AbsoluteSpillPath(kept.Body);
            spillPath.ShouldNotBeNull("F1 the long goal is spilled");
            var keptSpill = await File.ReadAllBytesAsync(spillPath);
            var payload = Encoding.UTF8.GetString(keptSpill);
            payload.Contains(marker, StringComparison.Ordinal).ShouldBeTrue("F1");
            payload.Contains(seeded.Goal, StringComparison.Ordinal).ShouldBeTrue("F1");
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            {
                var task = await db.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId);
                task.Status.ShouldBe(AgentTaskStatus.Dispatched, "F1");
                task.FailureReason.ShouldBeNull("F1");
                (await db.AgentSessions.SingleAsync(s => s.Id == seeded.SessionId)).Status
                    .ShouldBe(SessionStatus.Running, "F1");
            }

            (await UserPromptCountAsync(schema.ConnectionString, seeded.SessionId)).ShouldBe(0, "F1");

            // Recovery with a healthy probe reuses that exact row, then the recipient receives it whole.
            await using var healthy = await OpenQueueAsync(schema.ConnectionString, seeded);
            var again = await healthy.Queue.EnsureDispatchBriefAsync(RequestFor(seeded), CancellationToken.None);
            again.Kind.ShouldBe(DispatchBriefKind.Reuse, "F1");
            again.Inserted.ShouldBeFalse("F1");
            again.MessageId.ShouldBe(kept.Id, "F1");
            await healthy.Queue.FlushSessionAsync(seeded.SessionId, CancellationToken.None);
            var delivered = await SingleBriefAsync(schema.ConnectionString, seeded.TaskId, "F1");
            delivered.Id.ShouldBe(kept.Id, "F1");
            Encoding.UTF8.GetBytes(delivered.Body).ShouldBe(keptBody, "F1");
            (await File.ReadAllBytesAsync(spillPath)).ShouldBe(keptSpill, "F1");
            var prompts = await UserPromptsAsync(schema.ConnectionString, seeded.SessionId);
            prompts.Count.ShouldBe(1, "F1");
            PromptSubmissionMatch.IsCompleteIn(delivered.Body, prompts[0]).ShouldBeTrue("F1");
        }
        finally
        {
            if (Directory.Exists(seeded.Directory))
                Directory.Delete(seeded.Directory, true);
        }
    }

    private sealed class FailingStateLoader : ISessionStateLoader
    {
        public const string Message = "C1150 F1 injected working-state failure";
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);

        public Task<IReadOnlyDictionary<Guid, SessionStateSnapshot>> LoadAsync(
            IReadOnlyCollection<Guid> sessionIds, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            throw new IOException(Message);
        }

        public Task<IReadOnlySet<Guid>> LoadPinnedIdsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());
    }
}
