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

    [Test]
    public async Task Unowned_root_is_refused()
    {
        // Outside the owned temp prefix, with an owner file, so only the prefix check can refuse it.
        var outside = Directory.CreateTempSubdirectory("antiphon-c79-not-owned-");
        CrashWorker? worker = null;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(outside.FullName, "owner"), "x");
            worker = CheckCompactionFixture.Start(new CrashWorkerRequest
            {
                Root = outside.FullName, Scenario = "scan",
            });
            using var limit = new CancellationTokenSource(CheckCompactionFixture.ReadyBudget);
            try
            {
                await worker.Process.WaitForExitAsync(limit.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException(
                    $"unowned root was still running after {CheckCompactionFixture.ReadyBudget}.{Environment.NewLine}{worker.Describe()}");
            }

            worker.Process.WaitForExit();
            worker.Process.ExitCode.ShouldBe(1);
            worker.Error.ShouldContain("Unowned compaction crash worker.");
            File.Exists(Path.Combine(outside.FullName, "scans.txt")).ShouldBeFalse();
        }
        finally
        {
            if (worker is not null && !worker.Process.HasExited)
                worker.Process.Kill(entireProcessTree: true);
            worker?.Process.Dispose();
            outside.Delete(recursive: true);
        }
    }

    // Stop waits one grace period, then the kill. The child's own budget is 180 s, so a limit
    // just past the grace cannot be satisfied by that budget expiring after disposal returns.
    private static readonly TimeSpan DisposalLimit = CheckCompactionFixture.StopGrace + TimeSpan.FromSeconds(15);

    private static async Task AssertDisposalEndsChildAsync(CrashRoot root, CrashWorkerRequest request, string marker)
    {
        var pid = 0;
        CrashWorker? worker = null;
        Task? disposal = null;
        try
        {
            worker = CheckCompactionFixture.Start(request);
            pid = worker.Process.Id;
            var until = DateTime.UtcNow + CheckCompactionFixture.ReadyBudget;
            while (Directory.GetFiles(root.FullPath, marker).Length == 0)
            {
                if (worker.Process.HasExited)
                    throw new InvalidOperationException($"child exited before {marker}: {worker.Error}");
                (DateTime.UtcNow < until).ShouldBeTrue($"child never wrote {marker}");
                await Task.Delay(50);
            }

            // Stands in for a failed assertion while the child is running. The limit covers
            // disposal itself: a check that starts only after DisposeAsync returns cannot tell
            // the kill from the child's 180 s budget.
            disposal = worker.DisposeAsync().AsTask();
            var finished = await Task.WhenAny(disposal, Task.Delay(DisposalLimit));
            (finished == disposal).ShouldBeTrue(
                $"disposal did not finish within {DisposalLimit}; crash worker {pid} alive={IsAlive(pid)}");
            await disposal;
            disposal = null;
            IsAlive(pid).ShouldBeFalse($"crash worker {pid} outlived disposal");
        }
        finally
        {
            if (IsAlive(pid))
            {
                try
                {
                    Process.GetProcessById(pid).Kill(entireProcessTree: true);
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                }
            }

            if (disposal is not null)
            {
                try
                {
                    await disposal.WaitAsync(TimeSpan.FromSeconds(15));
                }
                catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
                {
                }
            }
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
}
