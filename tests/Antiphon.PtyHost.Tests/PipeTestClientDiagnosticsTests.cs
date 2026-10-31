using System.IO.Pipes;
using System.Diagnostics;
using Shouldly;
using TUnit.Core;

namespace Antiphon.PtyHost.Tests;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class PipeTestClientDiagnosticsTests
{
    [Test]
    public async Task Closed_peer_reports_host_exit_and_captured_output()
    {
        var failure = await ReadClosedPeer(() =>
            "host pid=123; exit code=23; stderr tail: native failure; log tail: launch failed");
        failure.Message.ShouldContain("exit code=23");
        failure.Message.ShouldContain("stderr tail: native failure");
        failure.Message.ShouldContain("log tail: launch failed");
    }

    [Test]
    public async Task Closed_peer_captures_real_process_exit_stderr_and_bounded_log_tail()
    {
        var root = Path.Combine(Path.GetTempPath(), "antiphon-pipe-diagnostics", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var log = Path.Combine(root, "host.log");
        var start = new ProcessStartInfo(OperatingSystem.IsWindows()
            ? Path.Combine(Environment.SystemDirectory, "cmd.exe") : "/bin/sh")
        { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true };
        if (OperatingSystem.IsWindows())
        {
            start.ArgumentList.Add("/d");
            start.ArgumentList.Add("/c");
            start.ArgumentList.Add("echo custody-stderr 1>&2 & exit /b 23");
        }
        else
        {
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add("echo custody-stderr >&2; exit 23");
        }
        using var host = Process.Start(start)!;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var stderr = await host.StandardError.ReadToEndAsync(timeout.Token);
            await host.WaitForExitAsync(timeout.Token);
            File.WriteAllText(log, "discarded-prefix" + new string('x', 10_000) + "custody-log-tail");
            var failure = await ReadClosedPeer(() => PipeTestClient.DescribeHost(host, log, stderr));
            failure.Message.ShouldContain($"host pid={host.Id}; exit code=23");
            failure.Message.ShouldContain("custody-stderr");
            failure.Message.ShouldContain("custody-log-tail");
            failure.Message.ShouldNotContain("discarded-prefix");
            failure.Message.Length.ShouldBeLessThan(9000);
            failure.InnerException.ShouldBeOfType<EndOfStreamException>();
        }
        finally
        {
            if (!host.HasExited)
            {
                host.Kill(entireProcessTree: true);
                await host.WaitForExitAsync();
            }
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task Failed_diagnostics_preserve_the_transport_failure()
    {
        var failure = await ReadClosedPeer(() => throw new IOException("missing diagnostic file"));
        failure.Message.ShouldContain("Host closed the pipe");
        failure.Message.ShouldContain("Host diagnostics unavailable: IOException");
        failure.InnerException.ShouldBeOfType<EndOfStreamException>();
    }

    private static async Task<EndOfStreamException> ReadClosedPeer(Func<string> diagnostics)
    {
        var name = $"antiphon-pipe-diagnostics-{Guid.NewGuid():N}";
        await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var connected = server.WaitForConnectionAsync(timeout.Token);
        await using var client = await PipeTestClient.ConnectAsync(name, hostDiagnostics: diagnostics);
        await connected;
        await server.DisposeAsync();
        return await Should.ThrowAsync<EndOfStreamException>(() => client.ReadAsync());
    }
}
