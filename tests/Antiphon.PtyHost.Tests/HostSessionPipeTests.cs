using System.Runtime.InteropServices;
using Antiphon.PtyHost.Protocol;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.PtyHost.Tests;

[Category("PtyHost")]
public class HostSessionPipeTests
{
    private static void SkipIfNotWindows()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new SkipTestException("ConPTY only on Windows");
    }

    [Test]
    public async Task Launch_streams_output_and_exit_with_manifest()
    {
        SkipIfNotWindows();
        await using var host = HostHarness.Start();
        await using var client = await PipeTestClient.ConnectAsync(host.Options.PipeName);

        await client.SendAsync(new HelloMessage(PtyHostProtocol.Version));
        var helloAck = await client.ExpectAsync<HelloAckMessage>();
        helloAck.SessionId.ShouldBe(host.SessionId);
        helloAck.Status.ShouldBe(PtyHostStatus.WaitingForLaunch);

        await client.SendAsync(host.CmdLaunch("echo pty-marker-1", "exit /b 42"));
        var launched = await client.ExpectAsync<LaunchedMessage>();
        launched.ChildPid.ShouldBeGreaterThan(0);

        await client.SendAsync(new AttachMessage(0));
        var chunks = await client.CollectOutputUntilAsync(text => text.Contains("pty-marker-1"));
        chunks.Count.ShouldBeGreaterThan(0);

        var exited = await client.ExpectAsync<ExitedMessage>();
        exited.ExitCode.ShouldBe(42);
        exited.ExitReason.ShouldBe("ProcessExited");

        var manifest = PtyHostManifest.TryLoad(host.ManifestPath);
        manifest.ShouldNotBeNull();
        manifest.SessionId.ShouldBe(host.SessionId);
        manifest.ChildPid.ShouldBe(launched.ChildPid);
        manifest.ExitCode.ShouldBe(42);
        manifest.ExitReason.ShouldBe("ProcessExited");
        manifest.AnsiLogPath.ShouldBe(host.AnsiLogPath);
        manifest.HostPid.ShouldBe(Environment.ProcessId);

        File.ReadAllText(host.AnsiLogPath).ShouldContain("pty-marker-1");
    }

    [Test]
    public async Task Output_while_detached_is_replayed_without_gaps_on_reattach()
    {
        SkipIfNotWindows();
        await using var host = HostHarness.Start();

        long lastSeqSeen;
        // Emits m1 immediately, m2 after ~2s (while we are detached), then holds the child alive.
        var launch = host.CmdLaunch(
            "echo marker-m1",
            "ping -n 3 127.0.0.1 > nul",
            "echo marker-m2",
            "ping -n 60 127.0.0.1 > nul");

        await using (var first = await PipeTestClient.ConnectAsync(host.Options.PipeName))
        {
            await first.SendAsync(new HelloMessage(PtyHostProtocol.Version));
            await first.ExpectAsync<HelloAckMessage>();
            await first.SendAsync(launch);
            await first.ExpectAsync<LaunchedMessage>();
            await first.SendAsync(new AttachMessage(0));
            var chunks = await first.CollectOutputUntilAsync(text => text.Contains("marker-m1"));
            lastSeqSeen = chunks[^1].Seq;
        }

        // Detached: m2 is emitted with no client connected; the host must capture it.
        await Task.Delay(TimeSpan.FromSeconds(3));

        await using var second = await PipeTestClient.ConnectAsync(host.Options.PipeName);
        await second.SendAsync(new HelloMessage(PtyHostProtocol.Version));
        var helloAck = await second.ExpectAsync<HelloAckMessage>();
        helloAck.Status.ShouldBe(PtyHostStatus.Running);

        await second.SendAsync(new AttachMessage(lastSeqSeen));
        var replayed = await second.CollectOutputUntilAsync(text => text.Contains("marker-m2"));

        // Sequence continuity: replay resumes exactly after what we saw, with no gap or duplicate.
        replayed[0].Seq.ShouldBe(lastSeqSeen + 1);
        for (var i = 1; i < replayed.Count; i++)
            replayed[i].Seq.ShouldBe(replayed[i - 1].Seq + 1);

        await second.SendAsync(new KillMessage(5000));
        var exited = await second.ExpectAsync<ExitedMessage>();
        exited.ExitReason.ShouldBe("KilledByRequest");
    }

    [Test]
    public async Task Attach_beyond_ring_returns_resync_and_reattach_at_resync_seq_works()
    {
        SkipIfNotWindows();
        await using var host = HostHarness.Start(o => o with { RingCapChars = 64 });
        await using var client = await PipeTestClient.ConnectAsync(host.Options.PipeName);

        await client.SendAsync(new HelloMessage(PtyHostProtocol.Version));
        await client.ExpectAsync<HelloAckMessage>();

        // Enough output to overflow a 64-char ring several times over.
        await client.SendAsync(host.CmdLaunch(
            "for /l %%i in (1,1,50) do @echo overflow-line-%%i-padding-padding-padding"));
        await client.ExpectAsync<LaunchedMessage>();

        // Wait for the child to finish so all output is in the (overflowed) ring + ansi log.
        await WaitForStatusAsync(client, PtyHostStatus.Exited);

        await client.SendAsync(new AttachMessage(0));
        var resync = await client.ExpectAsync<ResyncMessage>();
        resync.FirstAvailableSeq.ShouldBeGreaterThan(1);
        resync.LastSeq.ShouldBeGreaterThanOrEqualTo(resync.FirstAvailableSeq);

        // The ansi log is the authoritative full record.
        File.ReadAllText(host.AnsiLogPath).ShouldContain("overflow-line-50");

        // Re-attach at the resync point succeeds (and immediately reports the exit).
        await client.SendAsync(new AttachMessage(resync.LastSeq));
        var exited = await client.ExpectAsync<ExitedMessage>();
        exited.LastSeq.ShouldBe(resync.LastSeq);
    }

    [Test]
    public async Task Host_lingers_after_exit_until_shutdown_ack_then_removes_manifest()
    {
        SkipIfNotWindows();
        await using var host = HostHarness.Start();
        await using var client = await PipeTestClient.ConnectAsync(host.Options.PipeName);

        await client.SendAsync(new HelloMessage(PtyHostProtocol.Version));
        await client.ExpectAsync<HelloAckMessage>();
        await client.SendAsync(host.CmdLaunch("echo done", "exit /b 0"));
        await client.ExpectAsync<LaunchedMessage>();
        await client.SendAsync(new AttachMessage(0));
        await client.ExpectAsync<ExitedMessage>();

        // Lingering: still serving state after exit.
        var status = await WaitForStatusAsync(client, PtyHostStatus.Exited);
        status.ExitCode.ShouldBe(0);
        File.Exists(host.ManifestPath).ShouldBeTrue();
        host.RunTask.IsCompleted.ShouldBeFalse();

        await client.SendAsync(new ShutdownMessage());
        await host.RunTask.WaitAsync(TimeSpan.FromSeconds(10));
        File.Exists(host.ManifestPath).ShouldBeFalse();
    }

    [Test]
    public async Task No_launch_within_timeout_self_destructs_without_manifest()
    {
        SkipIfNotWindows();
        await using var host = HostHarness.Start(o => o with { LaunchTimeout = TimeSpan.FromSeconds(1) });
        await using var client = await PipeTestClient.ConnectAsync(host.Options.PipeName);

        await client.SendAsync(new HelloMessage(PtyHostProtocol.Version));
        await client.ExpectAsync<HelloAckMessage>();

        await host.RunTask.WaitAsync(TimeSpan.FromSeconds(15));
        File.Exists(host.ManifestPath).ShouldBeFalse();
    }

    [Test]
    public async Task Command_before_launch_returns_error_frame_and_connection_survives()
    {
        SkipIfNotWindows();
        await using var host = HostHarness.Start();
        await using var client = await PipeTestClient.ConnectAsync(host.Options.PipeName);

        await client.SendAsync(new HelloMessage(PtyHostProtocol.Version));
        await client.ExpectAsync<HelloAckMessage>();

        await client.SendAsync(new InputMessage("too early"));
        var error = (ErrorMessage)await client.ReadAsync();
        error.Code.ShouldBe("commandFailed");

        // The pipe must still be usable after a rejected command.
        await client.SendAsync(new StatusRequestMessage());
        var status = await client.ExpectAsync<StatusReplyMessage>();
        status.Status.ShouldBe(PtyHostStatus.WaitingForLaunch);
    }

    [Test]
    public async Task SendLine_drives_an_interactive_shell()
    {
        SkipIfNotWindows();
        await using var host = HostHarness.Start();
        await using var client = await PipeTestClient.ConnectAsync(host.Options.PipeName);

        await client.SendAsync(new HelloMessage(PtyHostProtocol.Version));
        await client.ExpectAsync<HelloAckMessage>();
        await client.SendAsync(host.InteractiveCmdLaunch());
        await client.ExpectAsync<LaunchedMessage>();
        await client.SendAsync(new AttachMessage(0));

        await client.SendAsync(new SendLineMessage("echo interactive-marker-%RANDOM:~0,1%ok"));
        await client.CollectOutputUntilAsync(text => text.Contains("ok"));

        await client.SendAsync(new SendLineMessage("exit"));
        var exited = await client.ExpectAsync<ExitedMessage>();
        exited.ExitCode.ShouldBe(0);
    }

    private static async Task<StatusReplyMessage> WaitForStatusAsync(
        PipeTestClient client, string wantedStatus)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (true)
        {
            await client.SendAsync(new StatusRequestMessage());
            var status = await client.ExpectAsync<StatusReplyMessage>();
            if (status.Status == wantedStatus)
                return status;
            if (DateTime.UtcNow > deadline)
                throw new System.TimeoutException($"Session never reached status {wantedStatus} (last: {status.Status}).");
            await Task.Delay(200);
        }
    }
}
