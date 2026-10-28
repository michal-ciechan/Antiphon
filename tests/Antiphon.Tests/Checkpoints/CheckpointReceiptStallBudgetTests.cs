using System.Diagnostics;
using Antiphon.Checkpoints;
using Antiphon.HostCleanup.Checkpoints;
using Antiphon.Tests.Infrastructure;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

// CARD-1599: the Linux receipt reader treats a slow answer as a stall once the
// controlled clock passes 3s, even when the bytes then arrive. The loaded-host
// budget is 6s (twice the 3s censor of those refusals). A read that never
// returns bytes is still refused at that budget.
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointReceiptStallBudgetTests : CheckpointTestBase
{
    private static readonly TimeSpan LoadedHostBudget = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan SlowAnswer = TimeSpan.FromSeconds(4);

    [Test]
    public void slow_but_progressing_read_past_three_seconds_completes()
    {
        // red: one chunk that answers at 4s is refused as read-stalled (0 of 85) by the 3s window.
        var f = new HostCleanupCheckpointFixture();
        f.Io.Platform.ShouldBe(CheckpointPlatform.Linux);
        var payload = Receipt(85);
        f.Io.Put(f.Payload, payload);
        f.Io.StreamFactory = () => new Paced(payload, f.Clock, SlowAnswer);

        var read = ReceiptFile.ReadBytes(f.Context, f.Payload);

        read.ShouldBe(payload);
        f.Io.Count("stream").ShouldBe(1);
    }

    [Test]
    public void read_that_never_progresses_is_refused_within_the_loaded_host_budget()
    {
        var f = new HostCleanupCheckpointFixture();
        f.Io.Platform.ShouldBe(CheckpointPlatform.Linux);
        var payload = Receipt(85);
        f.Io.Put(f.Payload, payload);
        f.Io.StreamFactory = () => new Silent(f.Clock, LoadedHostBudget);

        var refused = Should.Throw<IOException>(() => ReceiptFile.ReadBytes(f.Context, f.Payload));

        refused.Message.ShouldContain("receipt-read-refused", Case.Sensitive);
        refused.Message.ShouldContain("read-stalled: 0 of 85 bytes; no progress within 6s", Case.Sensitive);
        refused.InnerException.ShouldBeOfType<IOException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task healthy_receipts_complete_with_every_pool_worker_occupied(bool embedded)
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("Linux scheduling witness; no native Windows qualification");
        var psi = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-NoProfile", "-File", Path.Combine(CheckpointFixtures.RepoRoot, "scripts/test-checkpoint-pool-saturation.ps1"),
                     "-Repository", CheckpointFixtures.RepoRoot, "-Scratch", TempDir(), "-Assembly", typeof(CheckpointFileReader).Assembly.Location })
            psi.ArgumentList.Add(arg);
        if (embedded) psi.ArgumentList.Add("-Embedded");
        using var child = Process.Start(psi)!;
        RegisterCheckpointChild(child);
        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();
        using var safety = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await child.WaitForExitAsync(safety.Token); }
        catch (OperationCanceledException) { child.Kill(true); await child.WaitForExitAsync(); throw; }
        child.ExitCode.ShouldBe(0, await stdout + await stderr);
    }

    [Test]
    public void one_byte_trickle_is_refused_by_an_overall_deadline()
    {
        var f = new HostCleanupCheckpointFixture();
        var bytes = Receipt(256);
        f.Io.Put(f.Payload, bytes);
        f.Io.StreamFactory = () => new Trickle(bytes, f.Clock);
        var refusal = Should.Throw<IOException>(() => ReceiptFile.ReadBytes(f.Context, f.Payload));
        refusal.Message.ShouldContain("read-deadline-exceeded");
        (f.Clock.GetUtcNow() - DateTimeOffset.Parse("2026-09-25T00:00:00Z")).ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(31));
    }

    private sealed class Trickle(byte[] bytes, ControlledTimeProvider clock) : MemoryStream(bytes, false)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            return base.ReadAsync(buffer[..1], cancellationToken);
        }
    }

    private static byte[] Receipt(int size)
    {
        var bytes = new byte[size];
        for (var i = 0; i < bytes.Length; i++) bytes[i] = (byte)(i + 1);
        return bytes;
    }

    private sealed class Paced(byte[] bytes, ControlledTimeProvider clock, TimeSpan delay) : MemoryStream(bytes, writable: false)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            clock.Advance(delay);
            cancellationToken.ThrowIfCancellationRequested();
            return base.ReadAsync(buffer, cancellationToken);
        }
    }

    private sealed class Silent(ControlledTimeProvider clock, TimeSpan budget) : MemoryStream([])
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            clock.Advance(budget);
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("a silent read must be refused when the stall budget elapses");
        }
    }
}
