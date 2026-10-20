using System.Diagnostics;
using System.Runtime.InteropServices;
using Shouldly;

namespace Antiphon.Tests.Checkpoints;

internal static class CheckpointBoundedProbe
{
    internal sealed record Result(int Exit, string Stdout, string Stderr, long Milliseconds, bool Watchdog);

    internal static async Task<Result> RunAsync(ProcessStartInfo start, string nonce,
        Action<Process> register, CancellationToken cancellationToken)
    {
        start.UseShellExecute = false;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        var clock = Stopwatch.StartNew();
        using var process = Process.Start(start)!;
        register(process);
        var identity = $"{process.Id}:{process.StartTime.ToUniversalTime().Ticks}:{nonce}";
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        var completion = Task.WhenAll(process.WaitForExitAsync(cancellationToken), stdout, stderr);
        var watchdog = false;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(8));
            try { await completion.WaitAsync(deadline.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                watchdog = true;
            }
            var elapsed = clock.ElapsedMilliseconds;
            if (watchdog)
            {
                process.Kill(entireProcessTree: true);
                await completion.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
            }
            var output = await stdout;
            output.Contains("C1233_READY " + identity, StringComparison.Ordinal)
                .ShouldBeTrue("the actual script invocation must be reached");
            return new Result(process.ExitCode, output, await stderr, elapsed, watchdog);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
            }
        }
    }

    internal static void Fifo(string path)
    {
        MkFifo(path, 0x180).ShouldBe(0, "mkfifo failed: " + Marshal.GetLastPInvokeError());
    }

    internal static void Executable(string path, string content)
    {
        File.WriteAllText(path, content);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MkFifo(string path, uint mode);
}
