using System.IO.Pipes;
using Antiphon.PtyHost.Client;
using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;
using Antiphon.TestSupport;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.PtyHost.Tests;

[Category("Integration")]
[NativeLinuxOnly("Linux placement negotiation runs on the Unix request pipe.")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class Card2060PlacementHandshakeTests
{
    [Test]
    public async Task OldHostGetsNoTrackedLaunch()
    {
        RequireLinux();
        var launch = TrackedLaunch();
        var sessionId = launch.VerificationBinding!.Generation.SessionId;
        var pipeName = PtyHostProtocol.PipeNameFor(sessionId);
        using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var pipe = Server(pipeName);
        var sent = 0;
        var peer = Task.Run(async () =>
        {
            await HelloAsync(pipe, sessionId, ["verificationCustodyV1"], bound.Token);
            while (await PtyHostFraming.ReadAsync(pipe, bound.Token) is { } message)
            {
                if (message is LaunchMessage)
                {
                    sent++;
                    await PtyHostFraming.WriteAsync(pipe, new LaunchedMessage(1, DateTime.UtcNow), bound.Token);
                }
                else
                {
                    message.ShouldBeOfType<StatusRequestMessage>();
                    await ReplyStatusAsync(pipe, bound.Token);
                    return;
                }
            }
        }, bound.Token);

        try
        {
            await using var client = await PtyHostClient.ConnectAsync(pipeName, TimeSpan.FromSeconds(5), bound.Token);
            string? reason = null;
            try { await client.LaunchAsync(launch, bound.Token); }
            catch (VerificationCustodyException ex) { reason = ex.Code; }

            // The peer reads the status request as a fence after any preceding launch frame.
            await client.GetStatusAsync(bound.Token);
            await peer;
            sent.ShouldBe(0);
            reason.ShouldBe("verification_custody_unsupported_backend");
        }
        finally
        {
            bound.Cancel();
            try { await peer; } catch (OperationCanceledException) when (bound.IsCancellationRequested) { }
        }
    }

    [Test]
    public async Task CancelledClientRetiresSentStream()
    {
        RequireLinux();
        var launch = TrackedLaunch();
        var sessionId = launch.VerificationBinding!.Generation.SessionId;
        var pipeName = PtyHostProtocol.PipeNameFor(sessionId);
        using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var request = CancellationTokenSource.CreateLinkedTokenSource(bound.Token);
        await using var pipe = Server(pipeName);
        var sent = new TaskCompletionSource<LaunchMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var followOn = 0;
        var peer = Task.Run(async () =>
        {
            await HelloAsync(pipe, sessionId, ["verificationCustodyV1", LinuxPlacementLaunch.Feature], bound.Token);
            sent.SetResult((await PtyHostFraming.ReadAsync(pipe, bound.Token)).ShouldBeOfType<LaunchMessage>());
            if (await PtyHostFraming.ReadAsync(pipe, bound.Token) is { } next)
            {
                next.ShouldBeOfType<StatusRequestMessage>();
                followOn++;
                await ReplyStatusAsync(pipe, bound.Token);
            }
        }, bound.Token);

        try
        {
            await using var client = await PtyHostClient.ConnectAsync(pipeName, TimeSpan.FromSeconds(5), bound.Token);
            var pendingLaunch = client.LaunchAsync(launch, request.Token);
            var frame = await sent.Task.WaitAsync(bound.Token);
            frame.VerificationBinding.ShouldBe(launch.VerificationBinding);
            frame.LinuxPlacementDeadline.ShouldNotBeNull();
            frame.LinuxPlacementDeadline!.Value.ShouldBeGreaterThan(0);
            var queued = client.GetStatusAsync(bound.Token);
            queued.IsCompleted.ShouldBeFalse();
            request.Cancel();
            await Should.ThrowAsync<OperationCanceledException>(async () => await pendingLaunch);
            try { await queued; } catch (EndOfStreamException) { }

            await peer;
            followOn.ShouldBe(0);
        }
        finally
        {
            bound.Cancel();
            try { await peer; } catch (OperationCanceledException) when (bound.IsCancellationRequested) { }
        }
    }

    private static NamedPipeServerStream Server(string pipeName) =>
        new(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

    private static async Task HelloAsync(NamedPipeServerStream pipe, Guid sessionId,
        IReadOnlyList<string> features, CancellationToken ct)
    {
        await pipe.WaitForConnectionAsync(ct);
        (await PtyHostFraming.ReadAsync(pipe, ct)).ShouldBeOfType<HelloMessage>();
        await PtyHostFraming.WriteAsync(pipe, new HelloAckMessage(PtyHostProtocol.Version, "fixture",
            sessionId, PtyHostStatus.WaitingForLaunch, features, Guid.NewGuid()), ct);
    }

    private static ValueTask ReplyStatusAsync(NamedPipeServerStream pipe, CancellationToken ct) =>
        PtyHostFraming.WriteAsync(pipe, new StatusReplyMessage(PtyHostStatus.WaitingForLaunch,
            null, null, 80, 24, 0, null, null), ct);

    private static LaunchMessage TrackedLaunch()
    {
        var store = Guid.NewGuid();
        var binding = new VerificationExecutionBinding(Guid.NewGuid(),
            new(Guid.NewGuid(), Guid.NewGuid(), new string('a', 40)),
            new(Guid.NewGuid(), SessionGeneration.Normalize(DateTime.UtcNow)),
            new("/tmp", "/tmp/.git", "/tmp/snapshot", "/tmp/.git/worktrees/snapshot",
                "feat/c2060", Guid.NewGuid()), VerificationCustodyBackends.LinuxCgroup, RunnerStoreId: store);
        return new LaunchMessage("/bin/sh", [], new Dictionary<string, string>(), binding.Creation.WorktreePath,
            80, 24, 0, false, "/tmp/ansi", VerificationBinding: binding, RunnerStoreId: store);
    }

    private static void RequireLinux()
    {
        if (!OperatingSystem.IsLinux())
            throw new SkipTestException("Requires Linux placement negotiation.");
    }
}
