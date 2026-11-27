using System.Net.WebSockets;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.SessionRunner.Tests.TestHelpers;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class PhoneHomeCloseRaceTests
{
    [Test]
    public async Task Superseded_close_does_not_warn_when_the_credit_writer_stop_aborts_the_socket()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var stopReceiving = new CancellationTokenSource();
        var closing = new TaskCompletionSource<WebSocketState>(TaskCreationOptions.RunContinuationsAsynchronously);
        var rejectedClose = new TaskCompletionSource<WebSocketState>(TaskCreationOptions.RunContinuationsAsynchronously);
        var logger = new RecordingLogger<PhoneHomeLiveConnection>();
        Task? receiving = null;
        PhoneHomeTestWebSocket socket = null!;
        socket = new PhoneHomeTestWebSocket
        {
            BeforeAbort = () => closing.Task.WaitAsync(deadline.Token).GetAwaiter().GetResult(),
            BeforeClose = async ct =>
            {
                closing.SetResult(socket.State);
                // The canceled writer drives the production receive loop's Abort before the
                // close checks state, as ManagedWebSocket does at handshake entry.
                try { await receiving!.WaitAsync(ct); }
                catch (OperationCanceledException) when (receiving!.IsCanceled) { }
                rejectedClose.SetResult(socket.State);
                throw new WebSocketException(WebSocketError.InvalidState);
            },
        };
        await using var connection = NewConnection(socket, logger);
        receiving = connection.ReceiveLoopAsync(stopReceiving.Token);

        try
        {
            await connection.DisposeAsync("superseded");

            (await closing.Task.WaitAsync(deadline.Token)).ShouldBe(WebSocketState.Open);
            (await rejectedClose.Task.WaitAsync(deadline.Token)).ShouldBe(WebSocketState.Aborted);
            logger.Entries.Where(entry => entry.Level == LogLevel.Warning).ShouldBeEmpty();
        }
        finally
        {
            stopReceiving.Cancel();
            try { await receiving; }
            catch (OperationCanceledException) when (receiving.IsCanceled) { }
        }
    }

    [Test]
    public async Task Superseded_close_warns_once_for_invalid_state_while_the_socket_is_open()
    {
        var failure = new WebSocketException(WebSocketError.InvalidState, "close handshake failed on an open socket");
        var socket = new PhoneHomeTestWebSocket { BeforeClose = _ => Task.FromException(failure) };
        var logger = new RecordingLogger<PhoneHomeLiveConnection>();
        await using var connection = NewConnection(socket, logger);

        await connection.DisposeAsync("superseded");
        await connection.DisposeAsync("request_aborted");

        var warning = logger.Entries.Where(entry => entry.Level == LogLevel.Warning).ShouldHaveSingleItem();
        warning.Exception.ShouldBeSameAs(failure);
        warning.State["RunnerId"].ShouldBe(connection.RunnerId);
        warning.State["Epoch"].ShouldBe(connection.Epoch);
    }

    private static PhoneHomeLiveConnection NewConnection(WebSocket socket, ILogger logger) =>
        new("test-runner", Guid.NewGuid(), Guid.NewGuid(), 17, socket, new PhoneHomeLimits(),
            TimeProvider.System, eventWindow: new PhoneHomeEventWindowOptions(1, 1, 4096), closeLogger: logger);
}
