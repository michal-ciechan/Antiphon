using Antiphon.TestSupport;
using System.Diagnostics;
using Antiphon.Checkpoints;
using Antiphon.HostCleanup.Checkpoints;
using Antiphon.Tests.Infrastructure;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

// CARD-1601 replaces CARD-1599's six-second scheduling stopgap with a dedicated I/O thread,
// a three-second silence window, and a separate overall cap. Pool witnesses run in children.
[Category("Unit")]
[ParallelLimiter<CheckpointToolSpawnLimit>]
public sealed class CheckpointReceiptStallBudgetTests : CheckpointTestBase
{
    private static readonly TimeSpan LoadedHostBudget = CheckpointFileReader.StallDeadline;
    private static readonly TimeSpan SlowAnswer = TimeSpan.FromSeconds(2);

    [Test]
    public void read_progress_rearms_silence_window_past_three_seconds()
    {
        // The data read and EOF each answer within 2s; their 4s total exceeds one stall window.
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
        refused.Message.ShouldContain("read-stalled: 0 of 85 bytes; no progress within 3s", Case.Sensitive);
        refused.InnerException.ShouldBeOfType<IOException>();
    }

    // The child is the staged script harness host (checkpoint-pool-witness): it caps its own pool, which this
    // shared test process must not do. It reads with the library CheckpointFileReader. (Unit 62 of the 2026-11-22
    // remaining-ps1 plan dropped the second arm, the copy scripts/lib/checkpoint-file-reader.ps1 embedded, with that file.)
    [Test]
    [NativeLinuxOnly("Linux scheduling witness; no native Windows qualification")]
    public async Task healthy_receipts_complete_with_every_pool_worker_occupied()
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("Linux scheduling witness; no native Windows qualification");
        var scratch = TempDir();
        var (assembly, type) = (typeof(CheckpointFileReader).Assembly.Location, typeof(CheckpointFileReader).FullName!);
        var host = Path.Combine(AppContext.BaseDirectory, "script-harness-host", "Antiphon.ScriptHarnessHost.dll");
        File.Exists(host).ShouldBeTrue("the script harness host must be staged by the build");
        var psi = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in new[] { "exec", host, "checkpoint-pool-witness", assembly, type, Path.Combine(scratch, "receipts") })
            psi.ArgumentList.Add(arg);
        using var child = Process.Start(psi)!;
        RegisterCheckpointChild(child);
        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();
        using var safety = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await child.WaitForExitAsync(safety.Token); }
        catch (OperationCanceledException) { child.Kill(true); await child.WaitForExitAsync(); throw; }
        child.ExitCode.ShouldBe(0, await stdout + await stderr);
        (await stdout).ShouldContain("PASS pool-saturation readers=4");
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

    [Test]
    public async Task blocked_inline_read_is_refused_without_waiting_for_the_syscall()
    {
        var f = new HostCleanupCheckpointFixture();
        using var stream = new BlockedInline();
        f.Io.StreamFactory = () => stream;
        var read = Task.Run(() => ReceiptFile.ReadBytes(f.Context, f.Payload));
        try
        {
            // Startup is a gate, not the refusal budget: a queued pool worker can be
            // starved on a loaded host. The one-second refusal assertion below stays strict.
            stream.Entered.Wait(TimeSpan.FromSeconds(60)).ShouldBeTrue();
            f.Clock.Advance(CheckpointFileReader.StallDeadline);
            await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(1)));
            read.IsCompleted.ShouldBeTrue("the caller must refuse while the synchronous syscall remains blocked");
            var error = await Should.ThrowAsync<IOException>(async () => await read);
            error.Message.ShouldContain("read-stalled");
            (f.Clock.GetUtcNow() - DateTimeOffset.Parse("2026-09-25T00:00:00Z")).ShouldBeLessThan(TimeSpan.FromSeconds(31));
            stream.Closed.IsSet.ShouldBeFalse("the worker retains the handle until its syscall returns");
        }
        finally
        {
            stream.Release.Set();
            try { await read; } catch (IOException) { }
            stream.Closed.Wait(TimeSpan.FromSeconds(5)).ShouldBeTrue("the late read must release its owned stream");
        }
    }

    private sealed class BlockedInline : MemoryStream
    {
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public ManualResetEventSlim Closed { get; } = new();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Entered.Set();
            Release.Wait();
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(0);
        }
        protected override void Dispose(bool disposing) { base.Dispose(disposing); Closed.Set(); }
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
            return ValueTask.FromResult(0); // the reader must check elapsed time before accepting EOF
        }
    }
}
