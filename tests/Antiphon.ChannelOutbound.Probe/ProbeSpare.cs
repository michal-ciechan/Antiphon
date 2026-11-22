using System.Diagnostics;
using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

// A spare probe: started before the parent knows what it will run, warmed on what it already knows (the compiled
// model, the world's database and broker), then the probe it is given once its config appears. The warm-up writes
// nothing: one read of the world's deliveries and a Kafka client that sends nothing. A failed warm-up only leaves the
// probe cold. A spare nobody gives a config to ends itself.
internal static class ProbeSpare
{
    private static readonly TimeSpan Abandoned = TimeSpan.FromMinutes(10);

    public static async Task<string?> WaitAsync(string warmPath, string configPath)
    {
        try
        {
            var warm = JsonSerializer.Deserialize<ProbeWarmup>(await File.ReadAllTextAsync(warmPath));
            if (warm is not null)
            {
                ProbeModel.Use(warm.CompiledModelPath);
                var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(warm.ConnectionString).WithProbeModel();
                await using (var db = new AppDbContext(options.Options))
                    _ = await db.ChannelOutboundDeliveries.AsNoTracking().AnyAsync();
                using (new KafkaAntiphonMessagingProducer(Options.Create(new AntiphonMessagingOptions
                    { BootstrapServers = warm.BootstrapServers, OutboundTopic = warm.Topic }))) { }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // ProbeLog's contract: the exception type, never its message.
            ProbeLog.Phase("spare-warmup-failed " + ex.GetType().Name);
        }
        ProbeLog.Phase("spare-ready");
        var waited = Stopwatch.StartNew();
        while (!File.Exists(configPath))
        {
            if (waited.Elapsed > Abandoned) return null;
            await Task.Delay(10);
        }
        ProbeLog.Phase("spare-given");
        return configPath;
    }
}

internal sealed record ProbeWarmup(string ConnectionString, string? CompiledModelPath, string BootstrapServers, string Topic);
