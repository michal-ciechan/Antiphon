using System.Diagnostics;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1494: a crash-worker child must not outlive a test whose wait failed. Before the fix a
/// failed assertion skipped the stop and the drain, and the orphan kept writing into its root.
/// </summary>
[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public class CheckCompactionWorkerLifetimeTests
{
    [Test]
    public async Task Crash_worker_is_killed_when_the_wait_fails()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var parent = await BridgeQueueHarness.CreateAsync(new()
        {
            AlwaysOn = false, ConnectionString = schema.ConnectionString,
        });
        await using var root = CheckCompactionFixture.CreateRoot();
        await AssertDisposalEndsChildAsync(root, new CrashWorkerRequest
        {
            Root = root.FullPath, Scenario = "scan", ConnectionString = schema.ConnectionString,
            SessionId = parent.SessionId, AgentId = parent.AgentId,
        }, "scans.txt");
    }

    [Test]
    public async Task Held_crash_worker_is_killed_when_the_wait_fails()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var seat = await CheckCompactionFixture.SeedConfirmedAsync(schema.ConnectionString);
        await using var root = CheckCompactionFixture.CreateRoot();
        // A held sweep never reads "stop", so only the kill can end it.
        await AssertDisposalEndsChildAsync(root, new CrashWorkerRequest
        {
            Root = root.FullPath, Scenario = "sweep", Hold = "resume-committed",
            ConnectionString = seat.ConnectionString, SessionId = seat.SessionId, AgentId = seat.AgentId,
            EpisodeId = seat.EpisodeId, Accepted = seat.Accepted,
        }, "held-*");
    }

    private static async Task AssertDisposalEndsChildAsync(CrashRoot root, CrashWorkerRequest request, string marker)
    {
        var pid = 0;
        try
        {
            try
            {
                await using var worker = CheckCompactionFixture.Start(request);
                pid = worker.Process.Id;
                var until = DateTime.UtcNow.AddSeconds(90);
                while (Directory.GetFiles(root.FullPath, marker).Length == 0)
                {
                    if (worker.Process.HasExited)
                        throw new InvalidOperationException($"child exited before {marker}: {worker.Error}");
                    (DateTime.UtcNow < until).ShouldBeTrue($"child never wrote {marker}");
                    await Task.Delay(50);
                }

                // Stands in for a failed assertion while the child is running.
                throw new WaitFailedException();
            }
            catch (WaitFailedException)
            {
            }

            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (IsAlive(pid) && DateTime.UtcNow < deadline)
                await Task.Delay(50);
            IsAlive(pid).ShouldBeFalse($"crash worker {pid} outlived the failed wait");
        }
        finally
        {
            // The red witness must not leave its own orphan behind.
            if (IsAlive(pid))
                Process.GetProcessById(pid).Kill(entireProcessTree: true);
        }
    }

    private static bool IsAlive(int pid)
    {
        if (pid == 0)
            return false;
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private sealed class WaitFailedException : Exception;
}
