using System.Diagnostics;
using System.Text.Json;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

// A spare probe: started before the parent knows what it will run, warmed on what it already knows (the compiled
// model, the world's database, the Kafka native library), then the probe it is given once its config appears. The
// warm-up writes nothing (one read of the world's deliveries) and stops as soon as the config arrives. A warm-up that
// fails before the spare takes a config exits the spare (3), so the parent starts a probe without one, which meets the
// same failure itself; one that fails after is only logged, as the probe redoes that work. A spare whose parent is
// gone, or that is never given a config within Abandoned, ends itself.
internal static class ProbeSpare
{
    // The longest world (C519_Converter_handoff: four probes of at most ProbeDeadline + ProbeStartupAndExit = 60 s
    // each, plus its 30 s idle wait) is under 5 minutes; a spare older than that is no world's.
    private static readonly TimeSpan Abandoned = TimeSpan.FromMinutes(5);

    // The probe's first EF read: about 1.5 s on the HP laptop on AC and 3.5 s on battery (2026-11-22 phase logs);
    // 10 s is about 3x the slower. A timed-out read only leaves the probe colder.
    private static readonly TimeSpan WarmupRead = TimeSpan.FromSeconds(10);

    public static async Task<string?> WaitAsync(string warmPath, string configPath, int parentId)
    {
        using var stop = new CancellationTokenSource();
        // Its own task, so polling for the config never waits behind a synchronous model load.
        var warmup = Task.Run(() => WarmAsync(warmPath, stop.Token));
        var waited = Stopwatch.StartNew();
        var parentChecked = Stopwatch.StartNew();
        while (!File.Exists(configPath))
        {
            if (warmup.IsCompleted && warmup.Result is { } failure)
            {
                ProbeLog.Phase("spare-warmup-failed " + failure);
                return null;
            }
            if (waited.Elapsed > Abandoned)
            {
                ProbeLog.Phase("spare-abandoned");
                return null;
            }
            if (parentChecked.Elapsed > TimeSpan.FromMilliseconds(500))
            {
                if (!ParentAlive(parentId))
                {
                    ProbeLog.Phase("spare-parent-gone");
                    return null;
                }
                parentChecked.Restart();
            }
            await Task.Delay(10);
        }
        // Taken, before the config is read: the parent relies on this order to withdraw an untaken config safely.
        await File.WriteAllTextAsync(configPath + ".taken", "");
        stop.Cancel();
        if (await warmup is { } late) ProbeLog.Phase("spare-warmup-failed-after-taken " + late);
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
            try
            {
                var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(warm.ConnectionString).WithProbeModel();
                await using var db = new AppDbContext(options.Options);
                _ = await db.ChannelOutboundDeliveries.AsNoTracking().AnyAsync(bounded.Token);
            }
            catch (OperationCanceledException) when (!stop.IsCancellationRequested)
            {
                ProbeLog.Phase("spare-warmup-read-timeout");
                return null;
            }
            ProbeLog.Phase("spare-ready");
            return null;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            return null; // stopped by the config: the probe goes on as it is
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return ex.GetType().Name;
        }
    }

    // A parent that cannot be read (access denied) counts as alive: Abandoned still ends the spare.
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
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return true;
        }
    }
}

internal sealed record ProbeWarmup(string ConnectionString, string? CompiledModelPath, string BootstrapServers, string Topic);
