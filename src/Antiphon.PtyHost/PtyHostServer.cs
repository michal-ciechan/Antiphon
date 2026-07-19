using System.IO.Pipes;
using System.Reflection;
using System.Threading.Channels;
using Antiphon.PtyHost.Protocol;

namespace Antiphon.PtyHost;

/// <summary>
/// Named-pipe server for one host: accepts one runner connection at a time, dispatches frames to
/// the <see cref="HostSession"/>, and pumps host->client messages through a per-connection channel
/// so replies and live output stay strictly ordered. A dropped pipe (runner restarting) is normal:
/// the session keeps running and the server loops back to accept the next runner.
/// </summary>
public sealed class PtyHostServer(PtyHostOptions options, HostSession session, HostLog log)
{
    private static readonly string HostVersion =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";

    public async Task RunAsync(CancellationToken ct)
    {
        session.StartLaunchTimeout();

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = session.ExitRequested.ContinueWith(_ => lifetime.Cancel(), TaskScheduler.Default);

        while (!lifetime.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = new NamedPipeServerStream(
                    options.PipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(lifetime.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            log.Info("Runner connected");
            try
            {
                await ServeConnectionAsync(pipe, lifetime.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex) when (ex is IOException or EndOfStreamException or InvalidDataException)
            {
                log.Error("Connection dropped", ex);
            }
            finally
            {
                await pipe.DisposeAsync();
                log.Info("Runner disconnected");
            }
        }

        var reason = session.ExitRequested.IsCompleted ? await session.ExitRequested : "cancelled";
        log.Info($"Host exiting: {reason}");
    }

    private async Task ServeConnectionAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var outbound = Channel.CreateUnbounded<PtyHostMessage>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        var pump = Task.Run(async () =>
        {
            try
            {
                await foreach (var message in outbound.Reader.ReadAllAsync(connectionCts.Token))
                    await PtyHostFraming.WriteAsync(pipe, message, connectionCts.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException)
            {
                // Peer vanished mid-write; the read loop notices and tears down.
            }
        }, connectionCts.Token);

        try
        {
            var hello = await PtyHostFraming.ReadAsync(pipe, connectionCts.Token);
            if (hello is not HelloMessage clientHello)
            {
                outbound.Writer.TryWrite(new ErrorMessage("protocol", "First frame must be hello."));
                return;
            }

            if (clientHello.ProtocolVersion != PtyHostProtocol.Version)
                log.Info($"Client protocol v{clientHello.ProtocolVersion}, host v{PtyHostProtocol.Version} - continuing (append-only protocol)");

            outbound.Writer.TryWrite(session.GetHelloAck(HostVersion));

            while (true)
            {
                var message = await PtyHostFraming.ReadAsync(pipe, connectionCts.Token);
                if (message is null)
                    return;

                try
                {
                    if (await DispatchAsync(message, outbound.Writer, connectionCts.Token))
                        return;
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
                {
                    // A bad command must not cost the runner its connection to a live session.
                    outbound.Writer.TryWrite(new ErrorMessage("commandFailed", ex.Message));
                }
            }
        }
        finally
        {
            session.Detach(outbound.Writer);
            outbound.Writer.TryComplete();
            // Give the pump a moment to flush queued frames (replies, final Exited) before teardown.
            try
            {
                await pump.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // Pump teardown failures are connection-local; the session lives on.
            }
            connectionCts.Cancel();
        }
    }

    /// <summary>Handles one frame; returns true when the connection should close (shutdown).</summary>
    private async Task<bool> DispatchAsync(
        PtyHostMessage message,
        ChannelWriter<PtyHostMessage> outbound,
        CancellationToken ct)
    {
        switch (message)
        {
            case LaunchMessage launch:
                outbound.TryWrite(await session.LaunchAsync(launch, ct));
                return false;

            case AttachMessage attach:
                if (session.Attach(attach.LastSeq, outbound) is { } resyncOrError)
                    outbound.TryWrite(resyncOrError);
                return false;

            case InputMessage input:
                await session.WriteInputAsync(input.Data, ct);
                return false;

            case SendLineMessage sendLine:
                await session.SendLineAsync(sendLine.Line, ct);
                return false;

            case ResizeMessage resize:
                session.Resize(resize.Cols, resize.Rows);
                return false;

            case KillMessage kill:
                await session.KillAsync(TimeSpan.FromMilliseconds(kill.TimeoutMs));
                return false;

            case ClearLiveBufferMessage:
                session.ClearLiveBuffer();
                return false;

            case StatusRequestMessage:
                outbound.TryWrite(session.GetStatus());
                return false;

            case ShutdownMessage:
                session.Shutdown();
                return true;

            default:
                outbound.TryWrite(new ErrorMessage(
                    "unexpectedMessage", $"Host cannot handle {message.GetType().Name}."));
                return false;
        }
    }
}
