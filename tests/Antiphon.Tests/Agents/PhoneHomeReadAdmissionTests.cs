using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

/// <summary>
/// CARD-1771: buffer, snapshot, and transcript reads on a sealed phone-home connection admit
/// the phone-home source-generated contracts. A reflection deserialize of the same payload does not.
/// </summary>
[Category("Unit")]
public class PhoneHomeReadAdmissionTests
{
    private static readonly Guid SessionId = Guid.Parse("54e0daae-241f-4234-aa07-c03ebcc7f7d2");
    private const string Ansi = "\u001b[31mred\r\n\u001b[0m";

    [Test]
    public async Task Phone_home_reads_admit_the_source_generated_contracts()
    {
        await using var phone = LivePhone.Start();
        using var watch = RunnerReadJson.Watch();

        var buffer = await phone.Client.GetBufferAsync(SessionId, CancellationToken.None);
        var snapshot = await phone.Client.GetSnapshotAsync(SessionId, CancellationToken.None);
        var transcript = await phone.Client.GetTranscriptAsync(SessionId, CancellationToken.None);

        buffer.Buffer.ShouldBe(Ansi);
        snapshot.RawOutput.ShouldBe(Ansi);
        transcript.Entries.Single().Text.ShouldBe(Ansi);
        watch.Count.ShouldBe(3);
        watch.Admitted[0].ShouldBeSameAs(RunnerReadJson.PhoneBuffer);
        watch.Admitted[1].ShouldBeSameAs(RunnerReadJson.PhoneSnapshot);
        watch.Admitted[2].ShouldBeSameAs(RunnerReadJson.PhoneTranscript);
    }

    private sealed class LivePhone : IAsyncDisposable
    {
        public PhoneHomeRunnerClient Client { get; }
        private readonly PhoneHomeLiveConnection _connection;
        private readonly CancellationTokenSource _stop;
        private readonly Task _loop;

        private LivePhone(PhoneHomeRunnerClient client, PhoneHomeLiveConnection connection, CancellationTokenSource stop, Task loop)
        {
            Client = client;
            _connection = connection;
            _stop = stop;
            _loop = loop;
        }

        public static LivePhone Start()
        {
            var socket = new ReplySocket();
            var connection = new PhoneHomeLiveConnection(
                "runner", Guid.NewGuid(), Guid.NewGuid(), 1, socket, new PhoneHomeLimits(), TimeProvider.System);
            var stop = new CancellationTokenSource();
            var loop = connection.ReceiveLoopAsync(stop.Token);
            return new LivePhone(new PhoneHomeRunnerClient(connection), connection, stop, loop);
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            await _connection.DisposeAsync();
            try { await _loop; } catch (Exception ex) when (ex is OperationCanceledException or WebSocketException) { }
        }
    }

    private sealed class ReplySocket : WebSocket
    {
        private readonly Channel<byte[]> _inbound = Channel.CreateUnbounded<byte[]>();
        private readonly CancellationTokenSource _abort = new();
        private byte[]? _pending;
        private int _offset;
        private WebSocketState _state = WebSocketState.Open;

        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;
        public override void Abort() => CloseNow();
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            CloseNow();
            return Task.CompletedTask;
        }
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            CloseNow();
            return Task.CompletedTask;
        }
        public override void Dispose() => CloseNow();

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            var json = Encoding.UTF8.GetString(buffer.Array!, buffer.Offset, buffer.Count);
            var request = JsonSerializer.Deserialize<PhoneHomeFrame>(json, PhoneHomeFraming.Json)!;
            var payload = request.Operation switch
            {
                PhoneHomeOperation.Buffer => JsonSerializer.SerializeToElement(
                    new RunnerBufferDto(SessionId, Ansi, 4), PhoneHomeFraming.Json),
                PhoneHomeOperation.Snapshot => JsonSerializer.SerializeToElement(
                    new RunnerSnapshotDto(SessionId, Ansi, "screen", 4, DateTime.UnixEpoch), PhoneHomeFraming.Json),
                PhoneHomeOperation.Transcript => JsonSerializer.SerializeToElement(
                    new RunnerTranscriptDto(SessionId,
                    [
                        new RunnerTranscriptEvent(SessionId, 1, "UserPrompt", null, null, null, "user", Ansi,
                            null, null, null, null, null),
                    ], 1), PhoneHomeFraming.Json),
                _ => throw new InvalidOperationException("unexpected " + request.Operation),
            };
            var reply = new PhoneHomeFrame(
                PhoneHomeFrameKind.Result, request.Epoch, request.RequestId, request.Operation, payload);
            _inbound.Writer.TryWrite(JsonSerializer.SerializeToUtf8Bytes(reply, PhoneHomeFraming.Json));
            return Task.CompletedTask;
        }

        public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            if (_abort.IsCancellationRequested)
                return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _abort.Token);
            try
            {
                _pending ??= await _inbound.Reader.ReadAsync(linked.Token);
            }
            catch (OperationCanceledException) when (_abort.IsCancellationRequested)
            {
                return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true);
            }
            var count = Math.Min(buffer.Count, _pending.Length - _offset);
            _pending.AsSpan(_offset, count).CopyTo(buffer.AsSpan());
            _offset += count;
            var end = _offset >= _pending.Length;
            if (end)
            {
                _pending = null;
                _offset = 0;
            }
            return new WebSocketReceiveResult(count, WebSocketMessageType.Text, end);
        }

        private void CloseNow()
        {
            _state = WebSocketState.Aborted;
            _abort.Cancel();
        }
    }
}
