using System.Text.Json;
using System.Threading.Channels;
using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Antiphon.Tests.TestHelpers;

internal sealed record ChannelOutboundCrashRequest(
    string Root, string ConnectionString, Guid SessionId, Guid AgentId,
    Guid SourceId, string Path, string Cut, bool Recover);

internal sealed record ChannelOutboundCutReceipt(int Pid, Guid Mvid, Guid PublicationId, string Cut);

/// <summary>A process that the parent kills at a durable outbound boundary.</summary>
internal static class ChannelOutboundCrashWorker
{
    internal const string Marker = "ANTIPHON_C519_OUTBOUND_CRASH_WORKER";

    internal static async Task RunAsync(string encoded)
    {
        var request = JsonSerializer.Deserialize<ChannelOutboundCrashRequest>(encoded)
            ?? throw new InvalidOperationException("Missing outbound crash request.");
        var owned = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory,
            ".antiphon", "acceptance", "card-0519")) + Path.DirectorySeparatorChar;
        var root = Path.GetFullPath(request.Root);
        if (!root.StartsWith(owned, StringComparison.Ordinal)
            || !File.Exists(Path.Combine(root, "owner")))
            throw new InvalidOperationException("Unowned outbound crash worker root.");
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var observer = new PassObserver();
        await using var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            ConnectionString = request.ConnectionString,
            PreserveDatabaseOnDispose = true,
            AttachSessionId = request.SessionId,
            AttachAgentId = request.AgentId,
            Bridge = new ChannelBridgeSettings
            {
                Enabled = true, DebounceWindowMs = 0, OutboundScanSeconds = 1,
                OutboundRetrySeconds = 1, OutboundSendTimeoutSeconds = 4,
                OutboundAttemptLeaseSeconds = 5, OutboundMaxAttempts = 3,
                OutboundPageSize = 2,
            },
            ConfigureServices = services =>
            {
                services.AddSingleton<IAntiphonMessagingProducer>(new ReceiptProducer(root));
                services.AddSingleton<ChannelOutboundBoundary>(new CutBoundary(root, request.Cut));
                services.AddSingleton<ChannelOutboundRecoveryObserver>(observer);
            },
        });
        if (!request.Recover)
        {
            await harness.Dispatcher.OnTurnEndAsync(request.SessionId, budget.Token);
            throw new InvalidOperationException("Crash cut was not reached: " + request.Cut);
        }

        var worker = harness.Provider.GetRequiredService<ChannelOutboundRecoveryWorker>();
        await worker.StartAsync(budget.Token);
        try
        {
            for (var pass = 0; pass < 25; pass++)
            {
                await observer.NextAsync(budget.Token).WaitAsync(TimeSpan.FromSeconds(20), budget.Token);
                await using var db = new Antiphon.Server.Infrastructure.Data.AppDbContext(
                    TestDbFixture.CreateDbContextOptions(request.ConnectionString));
                if (await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(
                    db.ChannelOutboundPublications.Where(p => p.Path == request.Path
                        && p.State == "Published" && p.Sources.Any(s => s.QueueMessageId == request.SourceId)),
                    budget.Token))
                    return;
            }
            throw new InvalidOperationException("Outbound recovery did not publish the original source.");
        }
        finally { await worker.StopAsync(CancellationToken.None); }
    }

    private sealed class ReceiptProducer(string root) : IAntiphonMessagingProducer
    {
        public Task SendAsync(ChannelReply reply, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.AppendAllText(Path.Combine(root, "receipts.ndjson"),
                JsonSerializer.Serialize(reply, Antiphon.Messaging.MessagingJson.Options)
                    + Environment.NewLine);
            return Task.CompletedTask;
        }
    }

    private sealed class CutBoundary(string root, string cut) : ChannelOutboundBoundary
    {
        public override async Task ReachAsync(string boundary, Guid publicationId, CancellationToken ct)
        {
            if (boundary != cut) return;
            var receipt = new ChannelOutboundCutReceipt(
                Environment.ProcessId, typeof(ChannelOutboundCrashWorker).Module.ModuleVersionId,
                publicationId, boundary);
            await File.WriteAllTextAsync(Path.Combine(root, "cut.json"),
                JsonSerializer.Serialize(receipt), ct);
            await Task.Delay(Timeout.Infinite, ct);
        }
    }

    private sealed class PassObserver : ChannelOutboundRecoveryObserver
    {
        private readonly Channel<long> _passes = Channel.CreateUnbounded<long>();
        public override Task ScanCompletedAsync(long pass, CancellationToken ct)
        {
            _passes.Writer.TryWrite(pass);
            return Task.CompletedTask;
        }
        public Task<long> NextAsync(CancellationToken ct) => _passes.Reader.ReadAsync(ct).AsTask();
    }
}
