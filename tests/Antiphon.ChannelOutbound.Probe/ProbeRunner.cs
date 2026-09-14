using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Files;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Antiphon.ChannelOutbound.Probe;

/// <summary>What the parent asks one probe launch to do.</summary>
public sealed record ProbeConfig
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public required string ConnectionString { get; init; }
    public required string StoreRoot { get; init; }
    public required string MarkerDirectory { get; init; }
    public required string ProducerEvidencePath { get; init; }
    public required string Nonce { get; init; }

    /// <summary>"admit" runs one SendAsync; "pump" runs one PumpOnceAsync.</summary>
    public required string Action { get; init; }

    /// <summary>Barrier to park at forever so the parent can kill this process there.</summary>
    public string? DieAtBarrier { get; init; }

    /// <summary>Fire the IntentCommit barrier BEFORE the commit rather than after it.</summary>
    public bool PauseBeforeIntentCommit { get; init; }

    public required Guid ChannelId { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid ConverterAgentId { get; init; }
    public required string ProfileName { get; init; }
    public required string PromptFile { get; init; }
    public int TimeoutSeconds { get; init; } = 300;

    public string? ReplyJson { get; init; }
    public long PromptSequence { get; init; } = 1;

    /// <summary>"accept", "refuse", or "accept-then-park" (records, then never returns).</summary>
    public string ProducerMode { get; init; } = "accept";
}

public static class ProbeRunner
{
    public static async Task<int> RunAsync(ProbeConfig config, CancellationToken ct)
    {
        Directory.CreateDirectory(config.MarkerDirectory);

        var settings = new ChannelOutboundSettings
        {
            StoreRoot = config.StoreRoot,
            Profiles =
            {
                [config.ProfileName] = new ChannelOutboundProfileSettings
                {
                    ProjectId = config.ProjectId,
                    AgentId = config.ConverterAgentId,
                    PromptFile = config.PromptFile,
                    Trigger = ChannelOutboundTrigger.MarkdownSources,
                    TimeoutSeconds = config.TimeoutSeconds,
                    MaxPending = 8,
                },
            },
        };
        var options = Options.Create(settings);

        var store = new ChannelOutboundFileStore(options, NullLogger<ChannelOutboundFileStore>.Instance);
        store.TestBarrier = (name, token) => ReachAsync(config, name, Guid.Empty, token);

        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(config.ConnectionString, npgsql =>
                {
                    npgsql.MigrationsAssembly("Antiphon.Server");
                    npgsql.SetPostgresVersion(16, 0);
                })
                .Options);

        var producer = new EvidenceSinkProducer(config);
        var service = new ChannelOutboundService(
            db,
            producer,
            store,
            new ChannelOutboundPolicy(db, options),
            new OutboundConversionManifestValidator(options),
            options,
            Options.Create(new AntiphonMessagingOptions()),
            Options.Create(new ChannelBridgeSettings { Enabled = true }),
            TimeProvider.System,
            NullLogger<ChannelOutboundService>.Instance)
        {
            TestPauseBeforeCommit = config.PauseBeforeIntentCommit,
        };
        service.TestBarrier = (name, id, token) => ReachAsync(config, name, id, token);

        if (config.Action == "pump")
        {
            var handled = await service.PumpOnceAsync(ct);
            Console.WriteLine("probe pumped " + handled);
            return 0;
        }

        var reply = JsonSerializer.Deserialize<ChannelReply>(
            config.ReplyJson ?? throw new InvalidDataException("admit needs replyJson"),
            Antiphon.Messaging.MessagingJson.Options)
            ?? throw new InvalidDataException("replyJson deserialized to null");

        var result = await service.SendAsync(
            new ChannelOutboundRequest(
                reply,
                ChannelOutboundOrigin.AgentReply,
                ChannelOutboundSendKind.Main,
                SessionId: null,
                PromptSequence: config.PromptSequence,
                TextWindowStart: config.PromptSequence,
                TextWindowEnd: config.PromptSequence + 1,
                ChannelId: config.ChannelId,
                ProjectId: config.ProjectId,
                CorrelationIds: [],
                SourceTaskIds: []),
            ct);
        Console.WriteLine("probe admitted " + result.Status + " " + result.DeliveryId?.ToString("D"));
        return 0;
    }

    /// <summary>
    /// Announces a barrier, then parks forever if this launch was told to die here. The marker is
    /// written and flushed BEFORE parking, so the parent never kills a process that has not yet
    /// reached the cut it is testing.
    /// </summary>
    private static async Task ReachAsync(ProbeConfig config, string name, Guid deliveryId, CancellationToken ct)
    {
        var marker = Path.Combine(config.MarkerDirectory, name + ".marker");
        await using (var stream = new FileStream(marker, FileMode.Create, FileAccess.Write, FileShare.Read))
        await using (var writer = new StreamWriter(stream, Encoding.UTF8))
        {
            await writer.WriteLineAsync(
                $"{config.Nonce} {deliveryId:D} {Environment.ProcessId}");
            await writer.FlushAsync(ct);
            stream.Flush(flushToDisk: true);
        }

        if (string.Equals(config.DieAtBarrier, name, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("probe parked at " + name);
            await Console.Out.FlushAsync(ct);
            await Task.Delay(Timeout.Infinite, ct);
        }
    }
}

/// <summary>
/// A producer whose acceptances OUTLIVE this process. The record is appended and flushed to disk
/// before the call can return, so a parent that kills the probe mid-publish can still tell whether
/// the broker had taken the bytes.
/// </summary>
internal sealed class EvidenceSinkProducer(ProbeConfig config) : IAntiphonMessagingProducer
{
    public async Task SendAsync(ChannelReply reply, CancellationToken cancellationToken = default)
    {
        if (config.ProducerMode == "refuse")
            throw new InvalidOperationException("probe producer refused before acceptance");

        var json = JsonSerializer.Serialize(reply, Antiphon.Messaging.MessagingJson.Options);
        var bytes = Encoding.UTF8.GetBytes(json);
        var line = JsonSerializer.Serialize(new
        {
            pid = Environment.ProcessId,
            nonce = config.Nonce,
            sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            channel = reply.Channel,
            conversationId = reply.ConversationId,
            replyHandle = reply.ReplyHandle,
            attachments = reply.Attachments.Select(a => a.Name).ToArray(),
        });

        Directory.CreateDirectory(Path.GetDirectoryName(config.ProducerEvidencePath)!);
        await using (var stream = new FileStream(
            config.ProducerEvidencePath, FileMode.Append, FileAccess.Write, FileShare.Read))
        await using (var writer = new StreamWriter(stream, Encoding.UTF8))
        {
            await writer.WriteLineAsync(line);
            await writer.FlushAsync(cancellationToken);
            stream.Flush(flushToDisk: true);
        }

        if (config.ProducerMode == "accept-then-park")
        {
            Console.WriteLine("probe parked after producer acceptance");
            await Console.Out.FlushAsync(cancellationToken);
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }
}
