using System.IO.Pipes;
using Shouldly;
using TUnit.Core;

namespace Antiphon.PtyHost.Tests;

[Category("Integration")]
public sealed class PipeTestClientDiagnosticsTests
{
    [Test]
    public async Task Closed_peer_reports_host_exit_and_captured_output()
    {
        var name = $"antiphon-pipe-diagnostics-{Guid.NewGuid():N}";
        await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var connected = server.WaitForConnectionAsync(timeout.Token);
        await using var client = await PipeTestClient.ConnectAsync(name,
            hostDiagnostics: () => "host pid=123; exit code=23; stderr tail: native failure; log tail: launch failed");
        await connected;
        await server.DisposeAsync();
        var failure = await Should.ThrowAsync<EndOfStreamException>(() => client.ReadAsync());
        failure.Message.ShouldContain("exit code=23");
        failure.Message.ShouldContain("stderr tail: native failure");
        failure.Message.ShouldContain("log tail: launch failed");
    }
}
