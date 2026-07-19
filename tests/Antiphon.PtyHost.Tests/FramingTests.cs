using Antiphon.PtyHost.Protocol;
using Shouldly;
using TUnit.Core;

namespace Antiphon.PtyHost.Tests;

[Category("PtyHost")]
public class FramingTests
{
    private static async Task<PtyHostMessage> RoundTrip(PtyHostMessage message)
    {
        using var stream = new MemoryStream();
        await PtyHostFraming.WriteAsync(stream, message, CancellationToken.None);
        stream.Position = 0;
        var read = await PtyHostFraming.ReadAsync(stream, CancellationToken.None);
        read.ShouldNotBeNull();
        return read;
    }

    [Test]
    public async Task All_message_types_round_trip()
    {
        var sessionId = Guid.NewGuid();
        var messages = new PtyHostMessage[]
        {
            new HelloMessage(1),
            new LaunchMessage(
                "cmd.exe", ["/c", "echo hi"], new Dictionary<string, string> { ["K"] = "v" },
                @"C:\work", 120, 30, 512, TranscriptEnabled: true, @"C:\logs\x.ansi.log"),
            new AttachMessage(42),
            new InputMessage("ls\r"),
            new SendLineMessage("echo hello"),
            new ResizeMessage(80, 24),
            new KillMessage(5000),
            new ClearLiveBufferMessage(),
            new StatusRequestMessage(),
            new ShutdownMessage(),
            new HelloAckMessage(1, "1.2.3", sessionId, PtyHostStatus.Running),
            new LaunchedMessage(1234, DateTime.UtcNow),
            new OutputMessage(7, "chunk[0m"),
            new ExitedMessage(0, "ProcessExited", 99),
            new StatusReplyMessage(PtyHostStatus.Exited, 1234, DateTime.UtcNow, 120, 30, 99, 0, "ProcessExited"),
            new ResyncMessage(10, 99),
            new ErrorMessage("code", "message"),
        };

        var json = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        foreach (var message in messages)
        {
            var read = await RoundTrip(message);
            read.GetType().ShouldBe(message.GetType());
            // Records with collection members use reference equality; compare structurally via JSON.
            System.Text.Json.JsonSerializer.Serialize(read, json)
                .ShouldBe(
                    System.Text.Json.JsonSerializer.Serialize(message, json),
                    $"round-trip failed for {message.GetType().Name}");
        }
    }

    [Test]
    public async Task Oversized_frame_length_is_rejected()
    {
        using var stream = new MemoryStream();
        stream.Write(BitConverter.GetBytes(int.MaxValue));
        stream.Write(new byte[16]);
        stream.Position = 0;

        await Should.ThrowAsync<InvalidDataException>(async () =>
            await PtyHostFraming.ReadAsync(stream, CancellationToken.None));
    }

    [Test]
    public async Task Clean_end_of_stream_returns_null()
    {
        using var stream = new MemoryStream();
        var read = await PtyHostFraming.ReadAsync(stream, CancellationToken.None);
        read.ShouldBeNull();
    }

    [Test]
    public async Task Truncated_frame_throws()
    {
        using var full = new MemoryStream();
        await PtyHostFraming.WriteAsync(full, new InputMessage("hello world"), CancellationToken.None);
        var bytes = full.ToArray();

        using var truncated = new MemoryStream(bytes, 0, bytes.Length - 3);
        await Should.ThrowAsync<EndOfStreamException>(async () =>
            await PtyHostFraming.ReadAsync(truncated, CancellationToken.None));
    }
}
