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
    public Task Superseded_close_does_not_warn_when_the_credit_writer_stop_aborts_the_socket() =>
        AssertIntentionalCloseRaceAsync(new WebSocketException(WebSocketError.InvalidState));

    [Test]
    public Task Intentional_abort_during_close_send_logs_cancellation_at_debug() =>
        AssertIntentionalCloseRaceAsync(new OperationCanceledException());

    private static async Task AssertIntentionalCloseRaceAsync(Exception closeFailure)
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
            BeforeClose = async ct =>
            {
                closing.SetResult(socket.State);
                // The canceled writer drives the production receive loop's Abort while the
                // handshake token is still live.
                try { await receiving!.WaitAsync(ct); }
                catch (OperationCanceledException) when (receiving!.IsCanceled) { }
                ct.IsCancellationRequested.ShouldBeFalse();
                rejectedClose.SetResult(socket.State);
                throw closeFailure;
            },
        };
        await using var connection = NewConnection(socket, logger);
        var originalContext = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new CloseGateContext(closing.Task));
            receiving = connection.ReceiveLoopAsync(stopReceiving.Token);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }

        try
        {
            await connection.DisposeAsync("superseded");

            (await closing.Task.WaitAsync(deadline.Token)).ShouldBe(WebSocketState.Open);
            (await rejectedClose.Task.WaitAsync(deadline.Token)).ShouldBe(WebSocketState.Aborted);
            AssertCloseLog(connection, logger, LogLevel.Debug, closeFailure);
        }
        finally
        {
            closing.TrySetResult(socket.State);
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

        AssertCloseLog(connection, logger, LogLevel.Warning, failure);
    }

    [Test]
    public async Task Close_after_transport_aborts_the_receive_logs_invalid_state_once_at_warning()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var receiveFailure = new WebSocketException(WebSocketError.ConnectionClosedPrematurely);
        var closeFailure = new WebSocketException(WebSocketError.InvalidState);
        var logger = new RecordingLogger<PhoneHomeLiveConnection>();
        Task? receiving = null;
        PhoneHomeTestWebSocket socket = null!;
        socket = new PhoneHomeTestWebSocket
        {
            BeforeClose = async ct =>
            {
                socket.State.ShouldBe(WebSocketState.Open);
                socket.FailNextReceive(receiveFailure, abortSocket: true);
                var observed = await Should.ThrowAsync<WebSocketException>(() => receiving!.WaitAsync(ct));
                observed.ShouldBeSameAs(receiveFailure);
                socket.State.ShouldBe(WebSocketState.Aborted);
                throw closeFailure;
            },
        };
        await using var connection = NewConnection(socket, logger, useEventWindow: false);
        receiving = connection.ReceiveLoopAsync(deadline.Token);

        await connection.DisposeAsync("superseded");
        await connection.DisposeAsync("transport_abort");

        AssertCloseLog(connection, logger, LogLevel.Warning, closeFailure);
    }

    [Test]
    public async Task Exception_thrown_by_intentional_abort_is_logged_once_at_warning()
    {
        var abortFailure = new OperationCanceledException("abort itself failed");
        var closeFailure = new WebSocketException(WebSocketError.InvalidState);
        var socket = new PhoneHomeTestWebSocket
        {
            BeforeClose = _ => Task.FromException(closeFailure),
            OnAbort = () => throw abortFailure,
        };
        var logger = new RecordingLogger<PhoneHomeLiveConnection>();
        await using var connection = NewConnection(socket, logger);

        await connection.DisposeAsync("request_aborted");
        await connection.DisposeAsync("superseded");

        var warning = logger.Entries.ShouldHaveSingleItem();
        warning.Level.ShouldBe(LogLevel.Warning);
        warning.State["RunnerId"].ShouldBe(connection.RunnerId);
        warning.State["Epoch"].ShouldBe(connection.Epoch);
        var failures = warning.Exception.ShouldBeOfType<AggregateException>().Flatten().InnerExceptions;
        failures.ShouldBe([closeFailure, abortFailure]);
    }

    private static void AssertCloseLog(PhoneHomeLiveConnection connection,
        RecordingLogger<PhoneHomeLiveConnection> logger, LogLevel level, Exception failure)
    {
        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(level);
        entry.Exception.ShouldBeSameAs(failure);
        entry.State["RunnerId"].ShouldBe(connection.RunnerId);
        entry.State["Epoch"].ShouldBe(connection.Epoch);
        entry.State["ExceptionType"].ShouldBe(failure.GetType().Name);
    }

    private static PhoneHomeLiveConnection NewConnection(WebSocket socket, ILogger logger, bool useEventWindow = true) =>
        new("test-runner", Guid.NewGuid(), Guid.NewGuid(), 17, socket, new PhoneHomeLimits(),
            TimeProvider.System, eventWindow: useEventWindow ? new PhoneHomeEventWindowOptions(1, 1, 4096) : null,
            closeLogger: logger);

    private sealed class CloseGateContext(Task closing) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) => _ = ResumeAsync(callback, state);

        private async Task ResumeAsync(SendOrPostCallback callback, object? state)
        {
            await closing.ConfigureAwait(false);
            callback(state);
        }
    }
}
