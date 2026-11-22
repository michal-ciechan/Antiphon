using System.Diagnostics;
using System.Text.Json;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

// A spare probe: started before the parent knows what it will run, warmed on what it already knows (the compiled
// model, the world's database, the Kafka native library), then the probe it is given once its config appears. The
// warm-up writes nothing (one read of the world's deliveries) and stops as soon as the config arrives. A warm-up that
// fails for any reason but that stop exits the spare (3), so the parent starts a probe without one, which meets the
// same failure itself: a compiled-model fault on first use still fails its probe. A spare whose parent is gone, or
// that is never given a config within Abandoned, ends itself.
internal static class ProbeSpare
{
    // A world lives at most its scenario's 120 s deadline after it starts a spare, so a spare older than that is no
    // world's; 150 s leaves the parent's own disposal its margin.
    private static readonly TimeSpan Abandoned = TimeSpan.FromSeconds(150);
    private static readonly TimeSpan WarmupRead = TimeSpan.FromSeconds(10);

    public static async Task<string?> WaitAsync(string warmPath, string configPath, int parentId)
    {
        using var stop = new CancellationTokenSource();
        var warmup = WarmAsync(warmPath, stop.Token);
        var waited = Stopwatch.StartNew();
        var parentChecked = Stopwatch.StartNew();
        while (!File.Exists(configPath))
        {
            if (warmup.IsCompleted && warmup.Result is { } failure)
            {
                ProbeLog.Phase("spare-warmup-failed " + failure);
                return null;
            }
            if (waited.Elapsed > Abandoned) return null;
            if (parentChecked.Elapsed > TimeSpan.FromMilliseconds(500))
            {
                if (!ParentAlive(parentId)) return null;
                parentChecked.Restart();
            }
            await Task.Delay(10);
        }
        // Taken: the parent waits for this before it relies on the spare.
        await File.WriteAllTextAsync(configPath + ".taken", "");
        stop.Cancel();
        if (await warmup is { } late)
        {
            ProbeLog.Phase("spare-warmup-failed " + late);
            return null;
        }
        ProbeLog.Phase("spare-given");
        return configPath;
    }

    // Null when warmed or stopped by the config; otherwise the failure's exception type (never its message).
    private static async Task<string?> WarmAsync(string warmPath, CancellationToken stop)
    {
        try
        {
            var warm = JsonSerializer.Deserialize<ProbeWarmup>(await File.ReadAllTextAsync(warmPath, stop));
            if (warm is null) return "NoWarmup";
            ProbeModel.Use(warm.CompiledModelPath);
            stop.ThrowIfCancellationRequested();
            _ = Confluent.Kafka.Library.Version; // loads librdkafka; no client, no connection
            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(stop);
            bounded.CancelAfter(WarmupRead);
            var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(warm.ConnectionString).WithProbeModel();
            await using (var db = new AppDbContext(options.Options))
                _ = await db.ChannelOutboundDeliveries.AsNoTracking().AnyAsync(bounded.Token);
            ProbeLog.Phase("spare-ready");
            return null;
        }
        catch (OperationCanceledException)
        {
            // Stopped by the config, or the bounded read ran out: the probe goes on as it is.
            return null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return ex.GetType().Name;
        }
    }

    private static bool ParentAlive(int parentId)
    {
        try
        {
            using var parent = Process.GetProcessById(parentId);
            return !parent.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}

internal sealed record ProbeWarmup(string ConnectionString, string? CompiledModelPath, string BootstrapServers, string Topic);
