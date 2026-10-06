using System.Diagnostics;
using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Antiphon.Tests.TestHelpers;

internal static class PostLandMutationDeliveryWorker
{
    internal const string Marker = "ANTIPHON_C478_DELIVERY_WORKER";

    private sealed record Settings(
        string Connection,
        Guid Note,
        string Ready,
        string? Mode = null,
        string? Root = null,
        string? TaskId = null,
        string? Cut = null);

    internal static async Task RunAsync(string encoded)
    {
        var settings = JsonSerializer.Deserialize<Settings>(encoded)
            ?? throw new InvalidOperationException("Missing delivery worker settings");
        if (settings.Mode == "publication-commit")
        {
            await RunPublicationCommitAsync(settings);
            return;
        }

        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            AlwaysOn = false,
            ConnectionString = settings.Connection,
            ConfigureServices = services => services.AddSingleton<LandDeliveryBoundary>(new Hang(settings.Ready)),
        });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(settings.Connection));
        var service = new AgentTaskLandNotificationService(db, h.Queue, new CompletionNoteFlushQueue(), h.Runtime,
            TimeProvider.System, new Hang(settings.Ready));
        await service.ReconcileAsync(settings.Note, CancellationToken.None);
    }

    internal static async Task CrashAtReceiptSaveAsync(string connection, Guid note, string assembly)
    {
        var ready = Path.Combine(Path.GetTempPath(), "c478-delivery-" + Guid.NewGuid().ToString("N"));
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Environment.CurrentDirectory,
        };
        foreach (var arg in new[] { assembly, "--treenode-filter",
                     "/*/*/PostLandMutationDeliveryTests/C478_V09a_LandProducerToCaller" })
            start.ArgumentList.Add(arg);
        start.Environment[Marker] = JsonSerializer.Serialize(new Settings(connection, note, ready));
        using var worker = Process.Start(start)
            ?? throw new InvalidOperationException("receipt-before-save worker did not start");
        var stdout = worker.StandardOutput.ReadToEndAsync();
        var stderr = worker.StandardError.ReadToEndAsync();
        try
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            while (!File.Exists(ready) && !worker.HasExited) await Task.Delay(50, budget.Token);
            File.Exists(ready).ShouldBeTrue(worker.HasExited ? await stderr : "receipt-before-save crash cut not reached");
        }
        finally
        {
            if (!worker.HasExited) worker.Kill(entireProcessTree: false);
            await worker.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
        }
    }

    /// <summary>
    /// CARD-1122 / CARD-0890: the publication-commit cut used to LoadFrom this assembly inside
    /// pwsh, which dies in the module initializer before worker-ready.json exists. The dotnet
    /// host supplies the test dependency context, the same way <see cref="CrashAtReceiptSaveAsync"/> does.
    /// </summary>
    internal static Process StartPublicationCommit(
        string connection, string root, Guid taskId, string cut, string ready, string assembly)
    {
        if (cut is not ("C14" or "resume"))
            throw new InvalidOperationException("Unowned publication-commit cut");
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Environment.CurrentDirectory,
        };
        foreach (var arg in new[] { assembly, "--treenode-filter",
                     "/*/*/PostLandMutationDeliveryTests/C478_V09a_LandProducerToCaller" })
            start.ArgumentList.Add(arg);
        start.Environment[Marker] = JsonSerializer.Serialize(new Settings(
            connection, taskId, ready, "publication-commit", root, taskId.ToString("N"), cut));
        start.Environment["ANTIPHON_C448_TEST_CONNECTION"] = connection;
        return Process.Start(start)
            ?? throw new InvalidOperationException("publication-commit worker did not start");
    }

    private static async Task RunPublicationCommitAsync(Settings settings)
    {
        var root = Path.GetFullPath(settings.Root ?? "");
        var ready = Path.GetFullPath(settings.Ready);
        var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()))
            + Path.DirectorySeparatorChar;
        var ownerPath = Path.Combine(root, "canonical", "fixture-owner.txt");
        var owner = File.Exists(ownerPath) ? (await File.ReadAllTextAsync(ownerPath)).Trim() : "";
        if (!root.StartsWith(temp, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(root).StartsWith("antiphon-c448-", StringComparison.Ordinal)
            || !string.Equals(Path.GetDirectoryName(ready), root, StringComparison.OrdinalIgnoreCase)
            || settings.Cut is not ("C14" or "resume")
            || owner != settings.TaskId)
            throw new InvalidOperationException("Unowned publication-commit worker");
        await LandingSafetyHarness.RunCrashWorkerAsync(root, settings.TaskId!, settings.Cut!, ready);
    }

    private sealed class Hang(string ready) : LandDeliveryBoundary
    {
        public override async Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
        {
            if (boundary != "receipt-before-save") return;
            await File.WriteAllTextAsync(ready, Environment.ProcessId.ToString(), ct);
            await Task.Delay(Timeout.Infinite, ct);
        }
    }
}
