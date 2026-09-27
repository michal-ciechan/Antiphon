using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Files;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

// The parent owns the database and directory. No production configuration or
// external runner is loaded by this crash probe.
if (args.Length != 1 || !File.Exists(args[0])) return 2;
var config = JsonSerializer.Deserialize<ProbeConfig>(await File.ReadAllTextAsync(args[0]));
if (config is null || config.DeliveryId == Guid.Empty) return 2;
try
{
    await using var db = new AppDbContext(new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(config.ConnectionString).Options);
    var producer = new EvidenceProducer(config.EvidencePath);
    var pump = new ChannelOutboundDeliveryPump(db, null!,
        new ChannelOutboundFileStore(config.StoreRoot), producer,
        Options.Create(new AntiphonMessagingOptions()), new ProbeClock(config.ClockOffsetSeconds),
        NullLogger<ChannelOutboundDeliveryPump>.Instance);
    if (config.Barrier is not null)
        pump.ProbeBarrierAsync = async (point, id, ct) =>
        {
            if (point != config.Barrier || id != config.DeliveryId) return;
            await using (var stream = new FileStream(config.MarkerPath, FileMode.CreateNew,
                             FileAccess.Write, FileShare.Read, 1, FileOptions.WriteThrough))
            {
                await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(point), ct);
                stream.Flush(flushToDisk: true);
            }
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        };
    await pump.TickAsync(CancellationToken.None);
    return 0;
}
catch (Exception ex)
{
    // The config contains a database password. Never include exception messages
    // or the config contents in the child protocol/output.
    Console.Error.WriteLine(ex is FileNotFoundException missing
        ? $"{ex.GetType().Name}: {Path.GetFileName(missing.FileName)}"
        : ex.GetType().Name);
    return 1;
}

internal sealed record ProbeConfig(string ConnectionString, string StoreRoot, Guid DeliveryId,
    string EvidencePath, string MarkerPath, string? Barrier, int ClockOffsetSeconds);

internal sealed class ProbeClock(int offsetSeconds) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow().AddSeconds(offsetSeconds);
}

internal sealed class EvidenceProducer(string path) : IAntiphonMessagingProducer
{
    public async Task SendAsync(ChannelReply reply, CancellationToken cancellationToken = default)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(reply, Antiphon.Messaging.MessagingJson.Options);
        await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write,
            FileShare.Read, 4096, FileOptions.WriteThrough);
        await stream.WriteAsync(BitConverter.GetBytes(bytes.Length), cancellationToken);
        await stream.WriteAsync(bytes, cancellationToken);
        stream.Flush(flushToDisk: true);
    }
}
