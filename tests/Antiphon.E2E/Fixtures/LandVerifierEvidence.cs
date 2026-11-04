using System.Diagnostics;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;

namespace Antiphon.E2E.Fixtures;

/// <summary>
/// rc 0b05506b e2e-all-split-2: after the land worktree's last <c>git status</c> the land runs its
/// verifier (a host build slot from the owned runner, then a real <c>dotnet build</c> of the owned
/// repository), which spawns no protocol Git. A receipt wait renewed only by protocol-git records saw
/// 60 s of quiet while the verification was in flight. This decorator records
/// <c>protocol-verify-*.json</c>: <c>start</c>, a <c>running</c> heartbeat while the inner verification is
/// in flight, and <c>end</c> with the verdict. Heartbeats stop at <see cref="DefaultBudget"/>, so a
/// stalled verifier still fails the receipt one quiet window later.
/// </summary>
internal sealed class LandVerifierEvidence(string root, ILandingVerifier inner, TimeSpan? heartbeat = null, TimeSpan? budget = null)
    : ILandingVerifier
{
    public static readonly TimeSpan DefaultHeartbeat = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Provisional, not a measured p95 (test-performance-standards R5): the owned verify build is
    /// 3.4-6.4 s cold on server2 (n=5, 2026-11-04) and was still running past 60 s on the loaded Windows
    /// qualification host; no Windows sample of a completed verification exists yet. The <c>end</c>
    /// record's <c>elapsedSeconds</c> is that sample in every retained evidence root. Inside the 600 s
    /// receipt cap.
    /// </summary>
    public static readonly TimeSpan DefaultBudget = TimeSpan.FromSeconds(300);

    // Orders records whose write times tie on a coarse file clock (CARD-1703 A-2).
    private static long _sequence;

    private readonly TimeSpan _heartbeat = heartbeat ?? DefaultHeartbeat;
    private readonly TimeSpan _budget = budget ?? DefaultBudget;

    public Task<LandingVerification> VerifyAsync(string worktree, string? filter, CancellationToken ct)
        => ObserveAsync(null, () => inner.VerifyAsync(worktree, filter, ct));

    public Task<LandingVerification> VerifyAsync(string worktree, string? filter,
        LandingVerificationCorrelation correlation, CancellationToken ct)
        => ObserveAsync(correlation, () => inner.VerifyAsync(worktree, filter, correlation, ct));

    private async Task<LandingVerification> ObserveAsync(LandingVerificationCorrelation? correlation,
        Func<Task<LandingVerification>> verify)
    {
        var elapsed = Stopwatch.StartNew();
        await RecordAsync("start", correlation, elapsed, null);
        var run = verify();
        while (!run.IsCompleted && elapsed.Elapsed < _budget)
        {
            await Task.WhenAny(run, Task.Delay(_heartbeat));
            if (!run.IsCompleted && elapsed.Elapsed < _budget)
                await RecordAsync("running", correlation, elapsed, null);
        }
        LandingVerification result;
        try { result = await run; }
        catch
        {
            await RecordAsync("end", correlation, elapsed, false);
            throw;
        }
        await RecordAsync("end", correlation, elapsed, result.Passed);
        return result;
    }

    private async Task RecordAsync(string phase, LandingVerificationCorrelation? correlation, Stopwatch elapsed, bool? passed)
    {
        // Evidence only: a write failure never changes the verification.
        try
        {
            var path = Path.Combine(root, $"protocol-verify-{Guid.NewGuid():N}.json");
            await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(new {
                phase, sequence = Interlocked.Increment(ref _sequence),
                operationId = correlation?.OperationId, requestId = correlation?.RequestId,
                elapsedSeconds = Math.Round(elapsed.Elapsed.TotalSeconds, 1), passed, at = DateTime.UtcNow,
                pid = Environment.ProcessId }));
            File.Move(path + ".tmp", path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
