using System.Diagnostics;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

/// <summary>
/// CARD-0589 S4: the land verifier's build slot, taken from the local session runner's
/// <c>/build-slots</c> broker with the rules of <c>scripts/lib/build-slot.ps1</c> (D-6): poll in
/// FIFO order at the runner's retry hint, report a <c>BUILD SLOT waiting</c> line on each new reason
/// and once a minute, give up after <see cref="LandingSettings.BuildSlotWaitMinutes"/> (timeout), and
/// build unleased at <c>-maxcpucount:4</c> once the runner has not answered for
/// <see cref="LandingSettings.BuildSlotUnreachableGraceSeconds"/>. The holder is this server
/// process, so the runner reaps the lease if the server dies mid-verification.
/// </summary>
public sealed class SessionRunnerBuildSlotGate(
    ISessionRunnerClient runner,
    TimeProvider time,
    IOptions<LandingSettings> settings,
    ILogger<SessionRunnerBuildSlotGate>? logger = null) : IBuildSlotGate
{
    public const int UnleasedMaxCpuCount = 4;
    private static readonly TimeSpan UnreachableRetry = TimeSpan.FromSeconds(5);

    public async Task<BuildSlotHold> AcquireAsync(string label, Action<string> report, CancellationToken ct)
    {
        var options = settings.Value;
        var wait = TimeSpan.FromMinutes(Math.Max(0, options.BuildSlotWaitMinutes));
        var grace = TimeSpan.FromSeconds(Math.Max(0, options.BuildSlotUnreachableGraceSeconds));
        DateTime processStart;
        using (var self = Process.GetCurrentProcess())
            processStart = self.StartTime.ToUniversalTime();
        var request = new BuildSlotRequest(Environment.ProcessId, processStart, label);

        var started = time.GetTimestamp();
        TimeSpan? unreachableSince = null;
        string? lastReason = null;
        var lastReported = TimeSpan.MinValue;
        var position = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var answer = await runner.AcquireBuildSlotAsync(request, ct);
            var elapsed = time.GetElapsedTime(started);
            TimeSpan delay;
            if (answer?.Grant is { } grant)
            {
                if (grant.Unlimited)
                {
                    Report(report, $"BUILD SLOT unlimited maxcpucount={grant.MaxCpuCount}");
                    return new BuildSlotHold(BuildSlotHoldOutcome.Unlimited, null, grant.MaxCpuCount, elapsed);
                }
                Report(report, $"BUILD SLOT granted lease={grant.LeaseId} waited={(int)elapsed.TotalSeconds}s maxcpucount={grant.MaxCpuCount}");
                return new BuildSlotHold(BuildSlotHoldOutcome.Granted, grant.LeaseId, grant.MaxCpuCount, elapsed,
                    GrantedTimestamp: time.GetTimestamp());
            }

            // The wait deadline wins over fail-open, even for the first unreachable answer.
            if (elapsed >= wait)
            {
                Report(report, $"BUILD SLOT timeout after {(int)wait.TotalMinutes}m position={position}");
                return new BuildSlotHold(BuildSlotHoldOutcome.Timeout, null, 0, elapsed, position);
            }

            if (answer?.Busy is { } busy)
            {
                unreachableSince = null;
                position = busy.QueuePosition;
                delay = RetryAfter(busy.RetryAfterMs);
                if (lastReason != BuildSlotProblemTypes.Busy || elapsed - lastReported >= TimeSpan.FromMinutes(1))
                {
                    Report(report, $"BUILD SLOT waiting label={label} position={busy.QueuePosition} occupied={busy.Occupied}/{busy.Budget} elapsed={(int)elapsed.TotalMinutes}m");
                    (lastReason, lastReported) = (BuildSlotProblemTypes.Busy, elapsed);
                }
            }
            else if (answer?.MemoryFloor is { } floor)
            {
                unreachableSince = null;
                position = floor.QueuePosition;
                delay = RetryAfter(floor.RetryAfterMs);
                if (lastReason != BuildSlotProblemTypes.MemoryFloor || elapsed - lastReported >= TimeSpan.FromMinutes(1))
                {
                    Report(report, $"BUILD SLOT waiting label={label} reason=memory_floor available={floor.AvailableMb}MB floor={floor.FloorMb}MB position={floor.QueuePosition} elapsed={(int)elapsed.TotalMinutes}m");
                    (lastReason, lastReported) = (BuildSlotProblemTypes.MemoryFloor, elapsed);
                }
            }
            else
            {
                unreachableSince ??= elapsed;
                delay = UnreachableRetry;
                if (elapsed - unreachableSince >= grace)
                {
                    Report(report, $"BUILD SLOT unleased reason=runner_unreachable maxcpucount={UnleasedMaxCpuCount} last=no answer");
                    return new BuildSlotHold(BuildSlotHoldOutcome.Unleased, null, UnleasedMaxCpuCount, elapsed);
                }
            }

            var remaining = wait - elapsed;
            await Task.Delay(delay < remaining ? delay : remaining, time, ct);
        }
    }

    public async Task ReleaseAsync(BuildSlotHold hold, Action<string> report, CancellationToken ct)
    {
        if (hold.Outcome != BuildSlotHoldOutcome.Granted || hold.LeaseId is not { } leaseId)
            return;
        var held = (int)time.GetElapsedTime(hold.GrantedTimestamp).TotalSeconds;
        bool released;
        try
        {
            released = await runner.ReleaseBuildSlotAsync(leaseId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(ex, "Build slot lease {LeaseId} release failed", leaseId);
            released = false;
        }
        Report(report, released
            ? $"BUILD SLOT released lease={leaseId} held={held}s"
            : $"BUILD SLOT release failed lease={leaseId} held={held}s; the runner reaps it when this process exits");
    }

    private static TimeSpan RetryAfter(int retryAfterMs) =>
        TimeSpan.FromMilliseconds(Math.Clamp(retryAfterMs, 250, 60_000));

    private void Report(Action<string> report, string line)
    {
        logger?.LogInformation("{BuildSlotLine}", line);
        try { report(line); } catch (Exception) { /* Evidence only; never fails the build. */ }
    }
}
