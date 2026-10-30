using System.Text.Json;
using Antiphon.PtyHost.Protocol;
using Shouldly;
using TUnit.Core;
using Old = Antiphon.PtyHost.Tests.FrozenV1;

namespace Antiphon.PtyHost.Tests;

[Category("Unit")]
public class Card1619ExitWireTests
{
    [Test]
    public async Task Old_host_frame_is_unknown_to_new_reader()
    {
        using var stream = new MemoryStream();
        await Old.PtyHostFraming.WriteAsync(stream, new Old.ExitedMessage(0, "ProcessExited", 17), default);
        stream.Position = 0;
        var actual = (ExitedMessage)(await PtyHostFraming.ReadAsync(stream, default))!;
        actual.ExitSignal.ShouldBeNull();
        actual.ExitCode.ShouldBe(0);
        actual.LastSeq.ShouldBe(17);
    }

    [Test]
    public async Task New_host_frame_is_read_by_frozen_v1_reader()
    {
        using var stream = new MemoryStream();
        await PtyHostFraming.WriteAsync(stream, new ExitedMessage(0, "ProcessExited", 18, 9), default);
        var bytes = stream.ToArray();
        BitConverter.ToInt32(bytes, 0).ShouldBe(bytes.Length - 4);
        using var payload = JsonDocument.Parse(bytes.AsMemory(4));
        payload.RootElement.GetProperty("$type").GetString().ShouldBe("exited");
        payload.RootElement.GetProperty("exitSignal").GetInt32().ShouldBe(9);
        stream.Position = 0;
        var old = (Old.ExitedMessage)(await Old.PtyHostFraming.ReadAsync(stream, default))!;
        old.ExitCode.ShouldBe(0);
        old.LastSeq.ShouldBe(18);
        old.ExitReason.ShouldBe("ProcessExited");
        PtyHostProtocol.Version.ShouldBe(1);
    }

    [Test]
    public async Task Status_and_attach_preserve_signal()
    {
        foreach (var message in new PtyHostMessage[] {
            new StatusReplyMessage("Exited", 123, DateTime.UnixEpoch, 80, 24, 19, 0, "ProcessExited", 15),
            new ExitedMessage(0, "ProcessExited", 19, 15) })
        {
            using var stream = new MemoryStream();
            await PtyHostFraming.WriteAsync(stream, message, default);
            stream.Position = 0;
            (await PtyHostFraming.ReadAsync(stream, default)).ShouldBe(message);
            stream.Position = 0;
            (await Old.PtyHostFraming.ReadAsync(stream, default)).ShouldNotBeNull();
        }
    }

    [Test]
    public void Manifest_both_versions_round_trip()
    {
        var old = JsonSerializer.Deserialize<Old.PtyHostManifest>(JsonSerializer.Serialize(Manifest(9)))!;
        old.ExitCode.ShouldBe(0);
        var actual = JsonSerializer.Deserialize<PtyHostManifest>(JsonSerializer.Serialize(old))!;
        actual.ExitSignal.ShouldBeNull();
        actual.SessionId.ShouldBe(old.SessionId);
        var current = JsonSerializer.Deserialize<PtyHostManifest>(JsonSerializer.Serialize(Manifest(15)))!;
        current.ExitSignal.ShouldBe(15);
        current.SchemaVersion.ShouldBe(1);
    }

    [Test]
    public async Task Unknown_signal_number_is_preserved()
    {
        using var stream = new MemoryStream();
        await PtyHostFraming.WriteAsync(stream, new ExitedMessage(0, "ProcessExited", 1, 127), default);
        stream.Position = 0;
        ((ExitedMessage)(await PtyHostFraming.ReadAsync(stream, default))!).ExitSignal.ShouldBe(127);
    }

    [Test]
    public async Task Null_signal_is_not_normalized_to_zero()
    {
        using var stream = new MemoryStream();
        await PtyHostFraming.WriteAsync(stream, new ExitedMessage(23, "ProcessExited", 1), default);
        stream.Position = 0;
        ((ExitedMessage)(await PtyHostFraming.ReadAsync(stream, default))!).ExitSignal.ShouldBeNull();
        JsonSerializer.Deserialize<PtyHostManifest>(JsonSerializer.Serialize(Manifest(null)))!.ExitSignal.ShouldBeNull();
    }

    [Test]
    public void Old_manifest_writer_drops_signal_and_new_reader_keeps_unknown()
    {
        var root = Path.Combine(Path.GetTempPath(), "card1619-wire-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "manifest.json");
        try
        {
            Manifest(9).SaveAtomic(path);
            var old = Old.PtyHostManifest.TryLoad(path)!;
            old.ShouldNotBeNull();
            old.SaveAtomic(path);
            File.ReadAllText(path).ShouldNotContain("exitSignal");
            var current = PtyHostManifest.TryLoad(path)!;
            current.ExitSignal.ShouldBeNull();
            current.ExitCode.ShouldBe(0);
            current.AcceptedStartedAt.ShouldBe(DateTime.UnixEpoch);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static PtyHostManifest Manifest(int? signal) => new()
    {
        SessionId = Guid.NewGuid(), PipeName = "frozen-v1", HostPid = 123,
        HostStartTimeUtc = DateTime.UnixEpoch, CreatedAtUtc = DateTime.UnixEpoch,
        AcceptedStartedAt = DateTime.UnixEpoch, ExitCode = 0, ExitSignal = signal,
        ExitReason = "ProcessExited", ExitedAtUtc = DateTime.UnixEpoch,
    };
}
