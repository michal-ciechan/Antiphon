using System.Diagnostics;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Antiphon.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.Application.C0824PublishedBranchDeletionTests;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0824 S2 V-44 (CP-18). Delivery of <see cref="PhoneHomeOperation.WorkspaceBranchRefsDelete"/> (op 48) from the
/// deletion sweep to a runner over the real phone-home socket: the production <see cref="PhoneHomeRunnerBranchRefs"/>,
/// the request and response JSON, the runner dispatcher's mutation lock and a scratch runner repository (the
/// <see cref="PhoneHomeMirrorJoinTests.JoinWorld"/> recipient). The receipts are an independent read of the runner repository's
/// refs and the recorded origin delete, never the frame or the <c>ok</c> response. Origin itself is the recording git
/// seam: CP-12 proves the origin push with real git.
/// </summary>
[NativeLinuxOnly(NativeLinuxReason)]
[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class C0824BranchRefsDeliveryTests
{
    internal const string NativeLinuxReason = "CARD-0824 S2 V-44: the phone-home recipient world requires Linux FIFOs and Unix file modes (CARD-1032 JoinWorld).";
    private static readonly DateTime Start = new(2026, 11, 3, 9, 0, 0, DateTimeKind.Utc);

    [Test]
    [Timeout(120_000)]
    public async Task C0824_idle_runner_deletes_refs_then_origin(CancellationToken ct)
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        await using var w = await DeliveryWorld.StartAsync(ct);
        await w.SweepAsync(ct);
        (await w.RunnerRefsAsync()).ShouldBeEmpty("runner-refs-gone");
        (await w.RunnerRefAsync("refs/heads/main")).ShouldNotBeNull("other runner refs untouched");
        w.Git.Deleted.Count.ShouldBe(1, "origin-deleted-once");
        w.RefsAtOriginDelete.ShouldNotBeNull();
        w.RefsAtOriginDelete!.ShouldBeEmpty("runner-before-origin");
        w.Runners.Results.Count.ShouldBe(1);
        w.Runners.Results[0].Deleted!.Order(StringComparer.Ordinal).AsEnumerable().ShouldBe([w.LocalRef, w.TrackingRef], "response-round-trip");
        var row = await w.RowAsync(ct);
        row.BranchDeletionState.ShouldBe(LandBranchDeletionState.Deleted);
        row.RunnerRefsDeletedAt.ShouldNotBeNull();
    }

    [Test]
    [Timeout(120_000)]
    public async Task C0824_busy_runner_defers_origin_until_released(CancellationToken ct)
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        await using var w = await DeliveryWorld.StartAsync(ct, hook: true);
        // An earlier mirror's checkout hook holds the runner's mutation lock (the CARD-1032 FIFO hold).
        var holder = await w.Join.SeedAsync();
        w.Join.ArmHold();
        w.Join.Begin(holder);
        (await w.Join.Entered(ct)).ShouldBe("x");

        var sweep = w.SweepAsync(ct);
        // Observation window: the op is queued behind the lock, so neither the runner refs nor origin move.
        for (var turn = 0; turn < 50 && !sweep.IsCompleted; turn++)
            await Task.Delay(20, ct);
        sweep.IsCompleted.ShouldBeFalse("busy-waits");
        (await w.RunnerRefsAsync()).Count.ShouldBe(2, "busy-refs-kept");
        w.Git.Deleted.ShouldBeEmpty("busy-no-origin-delete");

        w.Join.ReleaseHold();
        await sweep.WaitAsync(TimeSpan.FromSeconds(45), ct);
        await w.Join.Idle(ct);
        (await w.RunnerRefsAsync()).ShouldBeEmpty("released-refs-gone");
        w.Git.Deleted.Count.ShouldBe(1, "released-origin-deleted");
        w.RefsAtOriginDelete!.ShouldBeEmpty("runner-before-origin");
        (await w.RowAsync(ct)).BranchDeletionState.ShouldBe(LandBranchDeletionState.Deleted);
    }

    [Test]
    [Timeout(120_000)]
    public async Task C0824_disconnected_runner_defers_origin(CancellationToken ct)
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        await using var w = await DeliveryWorld.StartAsync(ct);
        await w.Join.Connection.StopAsync(ct).WaitAsync(TimeSpan.FromSeconds(10), ct);
        await w.SweepAsync(ct);
        var row = await w.RowAsync(ct);
        row.BranchDeletionState.ShouldBe(LandBranchDeletionState.Deferred, "offline-deferred");
        row.BranchDeletionReason.ShouldBe("runner_unreachable", "offline-deferred");
        row.RunnerRefsDeletedAt.ShouldBeNull();
        w.Git.Deleted.ShouldBeEmpty("offline-origin-untouched");
        (await w.RunnerRefsAsync()).Count.ShouldBe(2, "offline-refs-kept");
    }

    [Test]
    [Timeout(120_000)]
    public async Task C0824_crash_between_handoffs_recovers_once(CancellationToken ct)
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        await using var w = await DeliveryWorld.StartAsync(ct);
        // The runner deleted its refs, then the server dies before RunnerRefsDeletedAt is saved.
        w.Fault.Armed = true;
        await Should.ThrowAsync<InjectedSaveFault>(() => w.SweepAsync(ct));
        (await w.RunnerRefsAsync()).ShouldBeEmpty("runner-deleted-before-crash");
        w.Git.Deleted.ShouldBeEmpty("origin-untouched-at-crash");
        (await w.RowAsync(ct)).RunnerRefsDeletedAt.ShouldBeNull("not-saved");

        // Restart: a fresh context re-asks the runner, which answers idempotently over the socket; origin is deleted once.
        await w.SweepAsync(ct);
        w.Runners.Results.Count.ShouldBe(2, "runner-op-repeated");
        w.Runners.Results[1].Deleted.ShouldBeEmpty("second-delivery-idempotent");
        w.Runners.Results[1].Absent!.Order(StringComparer.Ordinal).AsEnumerable().ShouldBe([w.LocalRef, w.TrackingRef], "second-delivery-idempotent");
        w.Git.Deleted.Count.ShouldBe(1, "origin-deleted-once");
        var row = await w.RowAsync(ct);
        row.BranchDeletionState.ShouldBe(LandBranchDeletionState.Deleted);
        row.RunnerRefsDeletedAt.ShouldNotBeNull();
    }

    internal sealed class InjectedSaveFault() : Exception("injected save fault after the runner step");

    /// <summary>Throws once on the save that records <c>RunnerRefsDeletedAt</c>.</summary>
    private sealed class RunnerRefsSaveFault : SaveChangesInterceptor
    {
        public bool Armed { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<AgentTaskLanding>().Any(e =>
                    e.Property(o => o.RunnerRefsDeletedAt).IsModified && e.Entity.RunnerRefsDeletedAt is not null))
            {
                Armed = false;
                throw new InjectedSaveFault();
            }
            return base.SavingChangesAsync(eventData, result, ct);
        }
    }

    /// <summary>The production runner seam, recording each decoded response.</summary>
    private sealed class RecordingProductionRunners(IRunnerBranchRefs inner) : IRunnerBranchRefs
    {
        public List<RunnerBranchRefsResult> Results { get; } = [];

        public Task<bool> SupportsAsync(string runnerId, CancellationToken ct) => inner.SupportsAsync(runnerId, ct);

        public async Task<RunnerBranchRefsResult> DeleteAsync(string runnerId, string branch, string expectedSha, CancellationToken ct)
        {
            var result = await inner.DeleteAsync(runnerId, branch, expectedSha, ct);
            Results.Add(result);
            return result;
        }
    }

    private sealed class DeliveryWorld : IAsyncDisposable
    {
        public required PhoneHomeMirrorJoinTests.JoinWorld Join { get; init; }
        public required Guid OwnerId { get; init; }
        public required Guid OperationId { get; init; }
        public required RecordingProductionRunners Runners { get; init; }
        public RecordingLandingGit Git { get; } = new();
        public RunnerRefsSaveFault Fault { get; } = new();
        public MutableMonitor Monitor { get; } = new(Enabled(Start.AddDays(-10)));
        public List<string>? RefsAtOriginDelete { get; private set; }
        public string Branch => DelegateBranchName.Legacy(OwnerId);
        public string LocalRef => "refs/heads/" + Branch;
        public string TrackingRef => "refs/remotes/origin/" + Branch;

        public static async Task<DeliveryWorld> StartAsync(CancellationToken ct, bool hook = false)
        {
            var join = await PhoneHomeMirrorJoinTests.JoinWorld.StartAsync(ct, hook);
            try
            {
                var ownerId = Guid.NewGuid();
                var branch = DelegateBranchName.Legacy(ownerId);
                // The runner primary holds both refs at the landed source SHA, as a retired legacy mirror leaves them.
                RunGit(join.Clone, "update-ref", "refs/heads/" + branch, join.Sha);
                RunGit(join.Clone, "update-ref", "refs/remotes/origin/" + branch, join.Sha);
                var op = PublishedLandingRow.Build(ownerId, branch, Start.AddDays(-5));
                op.OriginalSourceSha = op.ReviewedSourceSha = op.SourceLocalSha = op.SourceRemoteSha = join.Sha;
                await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(join.Schema.ConnectionString)))
                {
                    var owner = Task_(ownerId, branch, AgentTaskStatus.Succeeded);
                    owner.RunnerId = join.RunnerId;
                    db.AgentTasks.Add(owner);
                    db.AgentTaskLandings.Add(op);
                    await db.SaveChangesAsync(ct);
                }
                var w = new DeliveryWorld
                {
                    Join = join, OwnerId = ownerId, OperationId = op.Id,
                    Runners = new RecordingProductionRunners(new PhoneHomeRunnerBranchRefs(join.Host.Directory)),
                };
                w.Monitor.Value.SourceBranchDeletionRunnerIds = [join.RunnerId];
                w.Git.OnDelete = () => w.RefsAtOriginDelete = w.ReadRunnerRefs();
                return w;
            }
            catch
            {
                await join.DisposeAsync();
                throw;
            }
        }

        public async Task<PublishedBranchDeletionRunResult> SweepAsync(CancellationToken ct)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>(TestDbFixture.CreateDbContextOptions(Join.Schema.ConnectionString))
                .AddInterceptors(Fault).Options;
            await using var db = new AppDbContext(options);
            return await new PublishedBranchDeletionService(db, Git, new FreeLease(), Runners, Monitor,
                new FakeTimeProvider(Start), NullLogger<PublishedBranchDeletionService>.Instance).RunAsync(ct);
        }

        public async Task<AgentTaskLanding> RowAsync(CancellationToken ct)
        {
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(Join.Schema.ConnectionString));
            return await db.AgentTaskLandings.AsNoTracking().SingleAsync(o => o.Id == OperationId, ct);
        }

        /// <summary>An independent read of the runner repository: every ref that names the branch.</summary>
        public Task<List<string>> RunnerRefsAsync() => Task.Run(ReadRunnerRefs);

        public Task<string?> RunnerRefAsync(string name) => Task.Run(() =>
        {
            var (code, output) = TryGit(Join.Clone, "rev-parse", "--verify", "-q", name);
            return code == 0 ? output.Trim() : null;
        });

        private List<string> ReadRunnerRefs()
        {
            var (code, output) = TryGit(Join.Clone, "for-each-ref", "--format=%(refname)");
            code.ShouldBe(0, output);
            return [.. output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(name => name.EndsWith("/" + Branch, StringComparison.Ordinal)).Order(StringComparer.Ordinal)];
        }

        private static void RunGit(string cwd, params string[] args)
        {
            var (code, output) = TryGit(cwd, args);
            if (code != 0) throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {output}");
        }

        private static (int Code, string Output) TryGit(string cwd, params string[] args)
        {
            var psi = new ProcessStartInfo("git") { WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in args) psi.ArgumentList.Add(arg);
            psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
            using var process = Process.Start(psi)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, stdout.Result + stderr);
        }

        public ValueTask DisposeAsync() => Join.DisposeAsync();
    }
}
