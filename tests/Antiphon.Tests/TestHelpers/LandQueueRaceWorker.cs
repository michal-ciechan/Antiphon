using System.Diagnostics;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Antiphon.Tests.TestHelpers;

/// <summary>Two real queue providers in separate processes rendezvous after an absent-key read.</summary>
internal static class LandQueueRaceWorker
{
    internal const string Marker = "ANTIPHON_C467_QUEUE_WORKER";
    private sealed record Settings(string Root, Guid Session, Guid Task, Guid Notification);

    internal static async Task RunAsync(string encoded)
    {
        var settings = JsonSerializer.Deserialize<Settings>(encoded)!;
        var owned = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".antiphon", "acceptance", "card-0467")) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(settings.Root).StartsWith(owned, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(Path.Combine(settings.Root, "owner"))) throw new InvalidOperationException("Unowned queue race worker");
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var h = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false,
            ConnectionString = Environment.GetEnvironmentVariable("ANTIPHON_C467_QUEUE_CONNECTION")!,
            ConfigureServices = services => services.AddSingleton<LandDeliveryBoundary>(new Rendezvous(settings.Root)) });
        Guid row = default;
        await h.Queue.EnqueueAsync(settings.Session, "immutable keyed body", MessageSendMode.WhenIdle, budget.Token,
            QueuedMessageOrigin.Delegation, sourceTaskId: settings.Task, contentDigest: "digest", deliverIfIdle: false,
            sourceLandNotificationId: settings.Notification, onCreated: id => row = id);
        h.Adapter.Inputs.ShouldBeEmpty(); row.ShouldNotBe(Guid.Empty);
        await File.WriteAllTextAsync(Path.Combine(settings.Root, $"{Environment.ProcessId}.result.json"),
            JsonSerializer.Serialize(new { row, pid = Environment.ProcessId, mvid = typeof(LandQueueRaceWorker).Assembly.ManifestModule.ModuleVersionId, inputs = h.Adapter.Inputs.Count }));
        // No shared TestDbFixture was started in this child. Dispose only its provider; parent owns the database.
        h.Scope.Dispose(); await h.Provider.DisposeAsync();
    }

    internal static async Task<(Guid First, Guid Second)> RunPairAsync(string connection, Guid session, Guid task, Guid note)
    {
        var root = Path.GetFullPath(Path.Combine(".antiphon", "acceptance", "card-0467", "queue-race-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root); await File.WriteAllTextAsync(Path.Combine(root, "owner"), Environment.ProcessId.ToString());
        var workers = new List<(Process Process, Task<string> Output, Task<string> Error)>();
        try
        {
            for (var i = 0; i < 2; i++)
            {
                var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Environment.CurrentDirectory };
                foreach (var arg in new[] { typeof(LandQueueRaceWorker).Assembly.Location, "--treenode-filter", "/*/*/AgentTaskLandNotificationRecoveryTests/C467_V09*" }) start.ArgumentList.Add(arg);
                start.Environment[Marker] = JsonSerializer.Serialize(new Settings(root, session, task, note));
                start.Environment["ANTIPHON_C467_QUEUE_CONNECTION"] = connection;
                var process = Process.Start(start)!;
                workers.Add((process, process.StandardOutput.ReadToEndAsync(), process.StandardError.ReadToEndAsync()));
            }
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(100));
            while (Directory.GetFiles(root, "*.absent").Length != 2 && workers.All(w => !w.Process.HasExited)) await Task.Delay(50, budget.Token);
            Directory.GetFiles(root, "*.absent").Length.ShouldBe(2, "both processes must observe absence before either inserts");
            await File.WriteAllTextAsync(Path.Combine(root, "release"), "both absent reads observed");
            await Task.WhenAll(workers.Select(w => w.Process.WaitForExitAsync(budget.Token)));
            foreach (var worker in workers) worker.Process.ExitCode.ShouldBe(0, "queue worker; retained evidence: " + root);
            var rows = new List<Guid>();
            foreach (var worker in workers)
            {
                using var result = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, $"{worker.Process.Id}.result.json")));
                rows.Add(result.RootElement.GetProperty("row").GetGuid());
            }
            return (rows[0], rows[1]);
        }
        finally
        {
            foreach (var worker in workers)
            {
                if (!worker.Process.HasExited) worker.Process.Kill(entireProcessTree: false);
                await worker.Process.WaitForExitAsync();
                await File.WriteAllTextAsync(Path.Combine(root, $"{worker.Process.Id}.stdout.log"), await worker.Output);
                await File.WriteAllTextAsync(Path.Combine(root, $"{worker.Process.Id}.stderr.log"), await worker.Error);
                worker.Process.Dispose();
            }
        }
    }

    private sealed class Rendezvous(string root) : LandDeliveryBoundary
    {
        public override async Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
        {
            if (boundary != "queue-key-absent") return;
            await File.WriteAllTextAsync(Path.Combine(root, $"{Environment.ProcessId}.absent"), identity.ToString(), ct);
            while (!File.Exists(Path.Combine(root, "release"))) await Task.Delay(25, ct);
        }
    }
}
