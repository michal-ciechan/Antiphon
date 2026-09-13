using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Files;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0418 F-2: the fixture world every outbound-conversion suite builds on — two projects P and
/// Q, an inbound agent per project, one dedicated converter that is nobody's inbound agent, and
/// three channels: X opted in to the "pdf" profile, Y and Z unconfigured.
///
/// <para>Each world owns a CLONED database, not the shared assembly store. That is not tidiness:
/// <see cref="ChannelOutboundService.PumpOnceAsync"/> scans ChannelOutboundDeliveries GLOBALLY and
/// takes the 16 oldest, so a world sharing a store with any other suite would pump rows it does not
/// own and could be crowded out of its own. Everything else here — the prompt file, the outbound
/// store root, the worker directories — is fixture-owned and removed in
/// <see cref="DisposeAsync"/>.</para>
/// </summary>
internal sealed class ChannelOutboundWorld : IAsyncDisposable
{
    public const string ProfileName = "pdf";
    public const string AltProfileName = "alt";

    private readonly List<AppDbContext> _contexts = [];
    private IsolatedTestSchema _schema = null!;

    public required string Root { get; init; }
    public required string StoreRoot { get; init; }
    public required string ConverterWorkspace { get; init; }
    public required string PromptPath { get; init; }
    public required ChannelOutboundSettings Settings { get; init; }
    public required OutboundProducerSpy Producer { get; init; }
    public required ChannelOutboundFileStore Store { get; init; }

    /// <summary>
    /// Offset-over-real clock. The lease in <see cref="ChannelOutboundService.PumpOnceAsync"/> is
    /// real (30 s by default), so a second pump inside the same test would be refused the claim it
    /// needs. Tests advance this instead of sleeping through a production lease.
    /// </summary>
    public OutboundOffsetClock Clock { get; } = new();

    public Guid ProjectP { get; private set; }
    public Guid ProjectQ { get; private set; }

    /// <summary>Inbound agent for project P (channels X and Y).</summary>
    public Guid AgentA { get; private set; }

    /// <summary>Inbound agent for project Q (channel Z).</summary>
    public Guid AgentB { get; private set; }

    /// <summary>The dedicated converter. Bound as no channel's inbound agent, by design.</summary>
    public Guid AgentC { get; private set; }

    /// <summary>Opted-in conversation: profile "pdf".</summary>
    public Guid ChannelX { get; private set; }

    /// <summary>Same project as X, no outbound profile.</summary>
    public Guid ChannelY { get; private set; }

    /// <summary>Other project, no outbound profile.</summary>
    public Guid ChannelZ { get; private set; }

    public string ConnectionString => _schema.ConnectionString;

    public static async Task<ChannelOutboundWorld> CreateAsync(
        Action<ChannelOutboundSettings>? configure = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "antiphon-outbound-" + Guid.NewGuid().ToString("N"));
        var storeRoot = Path.Combine(root, "store");
        var converterWorkspace = Path.Combine(root, "converter");
        Directory.CreateDirectory(storeRoot);
        Directory.CreateDirectory(converterWorkspace);
        Directory.CreateDirectory(Path.Combine(converterWorkspace, "prompts"));
        var promptPath = Path.Combine(converterWorkspace, "prompts", "pdf.md");
        await File.WriteAllTextAsync(promptPath, "# Convert the frozen outbound request.\n");

        var projectP = Guid.NewGuid();
        var projectQ = Guid.NewGuid();
        var agentC = Guid.NewGuid();
        var settings = new ChannelOutboundSettings
        {
            StoreRoot = storeRoot,
            Profiles =
            {
                [ProfileName] = new ChannelOutboundProfileSettings
                {
                    ProjectId = projectP,
                    AgentId = agentC,
                    PromptFile = "prompts/pdf.md",
                    Trigger = ChannelOutboundTrigger.MarkdownSources,
                    TimeoutSeconds = 120,
                    MaxPending = 8,
                },
            },
        };
        configure?.Invoke(settings);

        var world = new ChannelOutboundWorld
        {
            Root = root,
            StoreRoot = storeRoot,
            ConverterWorkspace = converterWorkspace,
            PromptPath = promptPath,
            Settings = settings,
            Producer = new OutboundProducerSpy(),
            Store = new ChannelOutboundFileStore(
                Options.Create(settings), NullLogger<ChannelOutboundFileStore>.Instance),
        };
        world._schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        world.ProjectP = projectP;
        world.ProjectQ = projectQ;
        world.AgentC = agentC;
        await world.SeedAsync();
        return world;
    }

    public AppDbContext NewContext()
    {
        var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(_schema.ConnectionString));
        lock (_contexts)
            _contexts.Add(db);
        return db;
    }

    /// <summary>
    /// A fresh service over a fresh context — the same shape a restarted process has. Pass
    /// <paramref name="runner"/> only in the suites that are about real task dispatch.
    /// </summary>
    public ChannelOutboundService NewService(
        AppDbContext? db = null,
        TimeProvider? clock = null,
        OutboundConversionTaskRunner? runner = null,
        IAntiphonMessagingProducer? producer = null,
        AntiphonMessagingOptions? messaging = null,
        ChannelBridgeSettings? bridge = null)
    {
        db ??= NewContext();
        var options = Options.Create(Settings);
        return new ChannelOutboundService(
            db,
            producer ?? Producer,
            Store,
            new ChannelOutboundPolicy(db, options),
            new OutboundConversionManifestValidator(options),
            options,
            Options.Create(messaging ?? new AntiphonMessagingOptions()),
            Options.Create(bridge ?? new ChannelBridgeSettings { Enabled = true }),
            clock ?? Clock,
            NullLogger<ChannelOutboundService>.Instance,
            runner);
    }

    /// <summary>Moves this world past one lease window so the next pump can claim.</summary>
    public void AdvancePastLease() => Clock.Advance(TimeSpan.FromSeconds(Settings.LeaseSeconds + 1));

    public ChannelOutboundPolicy NewPolicy(AppDbContext? db = null) =>
        new(db ?? NewContext(), Options.Create(Settings));

    public ChannelOutboundRequest Request(
        ChannelReply reply,
        Guid? channelId = null,
        ChannelOutboundOrigin origin = ChannelOutboundOrigin.AgentReply,
        ChannelOutboundSendKind sendKind = ChannelOutboundSendKind.Main,
        Guid? sessionId = null,
        long? promptSequence = 1,
        long? windowStart = 1,
        long? windowEnd = 2,
        IReadOnlyList<Guid>? correlationIds = null,
        IReadOnlyList<Guid>? sourceTaskIds = null) =>
        new(reply, origin, sendKind, sessionId ?? Guid.Empty, promptSequence, windowStart, windowEnd,
            channelId ?? ChannelX, ProjectP, correlationIds ?? [], sourceTaskIds ?? []);

    public static ChannelReply MarkdownReply(
        string text = "Here are the sources.",
        string conversationId = "X-conversation",
        string? replyHandle = "handle-T1",
        params string[] markdownNames)
    {
        var names = markdownNames.Length == 0 ? ["01-requirements.md"] : markdownNames;
        return new ChannelReply
        {
            Channel = "slack",
            ConversationId = conversationId,
            ReplyHandle = replyHandle,
            Text = text,
            Attachments = names.Select(n => new OutboundAttachment
            {
                Kind = AttachmentKind.File,
                Name = n,
                Mime = "text/markdown",
                Content = System.Text.Encoding.UTF8.GetBytes("# " + n + "\n\nbody\n"),
            }).ToList(),
        };
    }

    /// <summary>Reads one delivery from a context this test has never written through.</summary>
    public async Task<ChannelOutboundDelivery?> ReadDeliveryAsync(Guid id)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(_schema.ConnectionString));
        return await db.ChannelOutboundDeliveries.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);
    }

    public async Task<int> DeliveryCountAsync()
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(_schema.ConnectionString));
        return await db.ChannelOutboundDeliveries.CountAsync();
    }

    /// <summary>Writes a valid worker output manifest plus its files for <paramref name="deliveryId"/>.</summary>
    public async Task<OutboundConversionOutputV1> WriteWorkerOutputAsync(
        Guid deliveryId,
        string disposition = "converted",
        string? replacementText = null,
        params (string Name, byte[] Bytes)[] files)
    {
        var outputDir = Store.OutputDirectory(deliveryId);
        Directory.CreateDirectory(outputDir);
        var descriptors = new List<OutboundConversionFileDescriptor>();
        foreach (var (name, bytes) in files)
        {
            await File.WriteAllBytesAsync(Path.Combine(outputDir, name), bytes);
            descriptors.Add(new OutboundConversionFileDescriptor
            {
                Path = name,
                Mime = name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? "application/pdf" : "text/plain",
                Length = bytes.LongLength,
                Sha256 = SourceBundleManifest.Sha256Hex(bytes),
            });
        }

        var output = new OutboundConversionOutputV1
        {
            Version = 1,
            DeliveryId = deliveryId,
            Disposition = disposition,
            ReplacementText = replacementText,
            Files = descriptors,
        };
        await File.WriteAllTextAsync(
            Path.Combine(outputDir, "manifest.json"),
            JsonSerializer.Serialize(output, OutboundConversionManifestValidator.JsonOptions));
        return output;
    }

    /// <summary>Writes a raw manifest body, so a suite can test malformed/hostile output.</summary>
    public async Task WriteRawOutputManifestAsync(Guid deliveryId, string json)
    {
        var outputDir = Store.OutputDirectory(deliveryId);
        Directory.CreateDirectory(outputDir);
        await File.WriteAllTextAsync(Path.Combine(outputDir, "manifest.json"), json);
    }

    private async Task SeedAsync()
    {
        await using var db = NewContext();
        var now = DateTime.UtcNow;
        db.Projects.AddRange(
            new Project { Id = ProjectP, Name = "P", GitRepositoryUrl = "https://example.invalid/p.git", CreatedAt = now, UpdatedAt = now },
            new Project { Id = ProjectQ, Name = "Q", GitRepositoryUrl = "https://example.invalid/q.git", CreatedAt = now, UpdatedAt = now });

        AgentA = Guid.NewGuid();
        AgentB = Guid.NewGuid();
        db.Agents.AddRange(
            NewAgent(AgentA, "inbound-a", ProjectP, Path.Combine(Root, "a")),
            NewAgent(AgentB, "inbound-b", ProjectQ, Path.Combine(Root, "b")),
            NewAgent(AgentC, "converter-c", ProjectP, ConverterWorkspace));

        ChannelX = Guid.NewGuid();
        ChannelY = Guid.NewGuid();
        ChannelZ = Guid.NewGuid();
        db.ChatChannels.AddRange(
            NewChannel(ChannelX, "X-conversation", AgentA, ProfileName),
            NewChannel(ChannelY, "Y-conversation", AgentA, null),
            NewChannel(ChannelZ, "Z-conversation", AgentB, null));
        await db.SaveChangesAsync();
    }

    private Agent NewAgent(Guid id, string name, Guid projectId, string workspace)
    {
        Directory.CreateDirectory(workspace);
        var now = DateTime.UtcNow;
        return new Agent
        {
            Id = id,
            Name = name,
            Slug = name,
            WorkingDirectory = workspace,
            PoolProjectId = projectId,
            Kind = AgentKind.ClaudeCode,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private static ChatChannel NewChannel(Guid id, string externalId, Guid agentId, string? profile)
    {
        var now = DateTime.UtcNow;
        return new ChatChannel
        {
            Id = id,
            Provider = "slack",
            ExternalId = externalId,
            Kind = ChatChannelKind.Group,
            Title = externalId,
            AgentId = agentId,
            Enabled = true,
            OutboundAgentProfile = profile,
            ReplyHandle = "handle-T1",
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public async ValueTask DisposeAsync()
    {
        List<AppDbContext> contexts;
        lock (_contexts)
        {
            contexts = [.. _contexts];
            _contexts.Clear();
        }

        foreach (var db in contexts)
        {
            try { await db.DisposeAsync(); }
            catch (ObjectDisposedException) { }
        }

        if (_schema is not null)
            await _schema.DisposeAsync();
        // Fixture-owned only: everything here lives under this world's own temp root.
        try { Directory.Delete(Root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>
/// Real time plus a test-controlled offset. Not a frozen clock: production code here measures
/// elapsed wall time, and freezing it would make every bounded wait unbounded.
/// </summary>
internal sealed class OutboundOffsetClock : TimeProvider
{
    private long _offsetTicks;

    public override DateTimeOffset GetUtcNow() =>
        DateTimeOffset.UtcNow.AddTicks(Interlocked.Read(ref _offsetTicks));

    public void Advance(TimeSpan by) => Interlocked.Add(ref _offsetTicks, by.Ticks);
}
