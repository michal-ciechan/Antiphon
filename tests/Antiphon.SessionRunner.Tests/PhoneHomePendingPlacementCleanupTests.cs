using System.Text.Json;
using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;
using Antiphon.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Integration")]
[NativeLinuxOnly("Real runner admission for Linux cgroup placement requires Linux.")]
public sealed class PhoneHomePendingPlacementCleanupTests
{
    [Test]
    public async Task Failed_placement_removes_the_session_and_unblocks_the_next_launch()
    {
        const string placementFailure = "verification_custody_start_failed";
        const string nextProviderReached = "test_next_launch_reached_provider";
        var wait = TimeSpan.FromSeconds(5);
        var root = ShortSocketRoot.Create("c2084");
        try
        {
            var checkout = Directory.CreateDirectory(Path.Combine(root, "checkout")).FullName;
            var logPath = Path.Combine(root, "logs");
            await using var runtime = new SessionRunnerRuntime(
                Options.Create(new SessionRunnerSettings { SessionLogPath = logPath }),
                NullLogger<SessionRunnerRuntime>.Instance)
            {
                CustodyEnvironment = new LinuxCustodyProbeTests.FakeEnvironment(),
            };
            var adapter = new PhoneHomeRuntimeAdapter(runtime, RunnerBuildIdentity.Resolve());
            adapter.VerificationCustodyBackend.ShouldBe(VerificationCustodyBackends.LinuxCgroup);
            var dispatcher = new PhoneHomeCommandDispatcher(adapter, new PhoneHomeSettings
            {
                Enabled = true,
                AllowedCwd = checkout,
                Capacity = 1,
                CapacityStatePath = Path.Combine(logPath, "capacity"),
                RawExeAllowList = ["/bin/sh"],
            });
            var sessionId = Guid.NewGuid();
            var generation = SessionGeneration.Normalize(DateTime.UtcNow);
            var binding = new VerificationExecutionBinding(Guid.NewGuid(),
                new(Guid.NewGuid(), Guid.NewGuid(), new string('a', 40)),
                new(sessionId, generation),
                new(root, Path.Combine(root, ".git"), checkout, Path.Combine(root, ".git", "worktrees", "checkout"),
                    "feat/card-task-20840000", Guid.NewGuid()),
                VerificationCustodyBackends.LinuxCgroup, runtime.RunnerStoreId);
            var failed = new RunnerLaunchRequest(sessionId, "/bin/sh", [], new Dictionary<string, string>(),
                checkout, 80, 24, AcceptedStartedAt: generation, VerificationBinding: binding);
            var next = failed with { SessionId = Guid.NewGuid(), VerificationBinding = null };
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var refusePlacement = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            runtime.CreationEffectForTest = async (id, _) =>
            {
                // The real StartCore has inserted the session. Hold it at the provider
                // boundary, then fail through its production cleanup path without spawning.
                if (id == sessionId)
                {
                    entered.SetResult();
                    await refusePlacement.Task;
                    throw new VerificationCustodyException(placementFailure);
                }
                id.ShouldBe(next.SessionId);
                throw new VerificationCustodyException(nextProviderReached);
            };

            var first = DispatchAsync(failed);
            Task<PhoneHomeFrame> nextReply;
            try
            {
                await entered.Task.WaitAsync(wait);
                runtime.List().ShouldContain(session => session.SessionId == sessionId);
                nextReply = DispatchAsync(next);
                nextReply.IsCompleted.ShouldBeFalse("the next Launch must wait for the failed placement to unwind");
            }
            finally
            {
                refusePlacement.SetResult();
                var failure = await first.WaitAsync(wait);
                failure.Kind.ShouldBe(PhoneHomeFrameKind.Error);
                failure.ErrorCode.ShouldBe(placementFailure);
            }

            runtime.List().ShouldNotContain(session => session.SessionId == sessionId,
                "failed placement leaked its session table entry");
            PhoneHomeFrame admitted;
            try { admitted = await nextReply.WaitAsync(wait); }
            catch (TimeoutException ex)
            {
                throw new TimeoutException("Failed placement leaked its pending reservation and blocked the next Launch.", ex);
            }
            admitted.Kind.ShouldBe(PhoneHomeFrameKind.Error);
            // Only the provider hook can return this code, after capacity and generation admission.
            admitted.ErrorCode.ShouldBe(nextProviderReached, "the next Launch must reach the provider with capacity one");

            Task<PhoneHomeFrame> DispatchAsync(RunnerLaunchRequest request) =>
                dispatcher.DispatchAsync(new PhoneHomeFrame(PhoneHomeFrameKind.Request, 1, Guid.NewGuid(),
                    PhoneHomeOperation.Launch, JsonSerializer.SerializeToElement(request, PhoneHomeFraming.Json)),
                    CancellationToken.None);
        }
        finally { ShortSocketRoot.Delete(root); }
    }
}
