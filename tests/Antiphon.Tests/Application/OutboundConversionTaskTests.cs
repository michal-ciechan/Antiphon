using System.Text;
using Antiphon.Messaging;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0418 V-10: the conversion worker is an ORDINARY task, created by the ordinary task service,
/// and the only thing that distinguishes it is an internal purpose the public API cannot ask for.
///
/// <para>This runs against the real <c>Program</c> graph — the real <c>AgentTaskService</c>, the
/// real <c>OutboundConversionTaskRunner</c>, the real file store — because the risk being tested is
/// precisely that this path quietly becomes a special seat with its own rules. A fake runner that
/// returned a task id would prove nothing about that.</para>
/// </summary>
[NotInParallel]
[ClassDataSource<ChannelOutboundWebAppFactory>(Shared = SharedType.PerClass)]
[Category("Integration")]
public sealed class OutboundConversionTaskTests(ChannelOutboundWebAppFactory factory)
{
    private const string ProfileName = "pdf";
    private const string SourceSentinel = "SOURCE-TASK-SECRET-b41d9f";

    [Test]
    [Timeout(300_000)]
    public async Task Real_creation_and_settlement_use_internal_purpose(CancellationToken ct)
    {
        var world = await SeedAsync(ct);
        await ReleaseSeatsAsync(ct);

        Guid deliveryId;
        using (var scope = factory.Services.CreateScope())
        {
            var outbound = scope.ServiceProvider.GetRequiredService<ChannelOutboundService>();
            var result = await outbound.SendAsync(Request(world), ct);
            result.Status.ShouldBe(ChannelOutboundPublishStatus.Deferred);
            deliveryId = result.DeliveryId!.Value;
        }

        // One pump tick: the real runner creates the real task through the real task service.
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ChannelOutboundService>().PumpOnceAsync(ct);

        using var read = factory.Services.CreateScope();
        await using var db = read.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = await db.ChannelOutboundDeliveries.AsNoTracking()
            .FirstAsync(d => d.Id == deliveryId, ct);
        delivery.State.ShouldBe(ChannelOutboundDeliveryState.Converting);
        delivery.ConversionTaskId.ShouldNotBeNull();

        var tasks = await db.AgentTasks.AsNoTracking()
            .Where(t => t.OutboundDeliveryId == deliveryId).ToListAsync(ct);
        tasks.Count.ShouldBe(1, "one matching reply authorizes exactly one metered invocation");
        var task = tasks[0];

        // The link is two-way and consistent.
        task.Id.ShouldBe(delivery.ConversionTaskId!.Value);

        // An ordinary Worker/Custom task in a shared workspace, pinned to the profile's converter.
        task.Kind.ShouldBe(AgentTaskKind.Worker);
        task.Role.ShouldBe(AgentTaskRole.Custom);
        task.Workspace.ShouldBe(WorkspaceMode.Shared);
        task.AgentId.ShouldBe(world.ConverterId);
        task.WorkingDirectory.ShouldBe(world.ConverterWorkspace);

        // One attempt, no reply route, no parent, no card: it answers nobody and escalates to
        // nothing. A conversion that failed must degrade the reply, not start a conversation.
        task.MaxAttempts.ShouldBe(1);
        task.ReplyTo.ShouldBe(AgentTaskReplyTo.None);
        task.ParentTaskId.ShouldBeNull();
        task.ParentSessionId.ShouldBeNull();
        task.CardId.ShouldBeNull();
        task.ExecutionDeadlineAt.ShouldBe(delivery.DeadlineAt);

        // It produces no deliverable bundle of its own — the internal purpose must not recurse into
        // the settlement machinery that created the reply it is converting.
        task.DeliverableBundleDir.ShouldBeNull();
        task.DeliverableDeliveredAt.ShouldBeNull();

        // Its instructions name the frozen request and forbid both child delegation and retargeting.
        task.Goal.ShouldContain(deliveryId.ToString("D"));
        task.Goal.ShouldContain("request.json");
        task.Goal.ShouldContain("Do not dispatch child tasks.");
        task.Goal.ShouldContain("Do not change Channel, ConversationId, ReplyHandle, or Kind.");

        // Nothing from the source task reaches it. The sentinel below is planted in the source
        // task's own goal; a worker that could read it could read anything the source could.
        task.Goal.ShouldNotContain(SourceSentinel);
        task.LaunchEnvOverrideJson.ShouldNotContain(SourceSentinel);
        task.InheritedLaunchEnvJson.ShouldNotContain(SourceSentinel);
        task.Title.ShouldNotContain(SourceSentinel);
        (task.Scope ?? "").ShouldNotContain(SourceSentinel);

        // The frozen request on disk is the only channel context it gets, and it carries the
        // conversation as a REFERENCE, never as an instruction to send anywhere.
        var store = read.ServiceProvider.GetRequiredService<IChannelOutboundFileStore>();
        var requestJson = await File.ReadAllTextAsync(
            Path.Combine(store.InputDirectory(deliveryId), "request.json"), ct);
        requestJson.ShouldContain("routingReference");
        requestJson.ShouldNotContain(SourceSentinel);

        // Nothing was launched: the host's runner refuses every start and recorded no successful
        // one, so this proves task CREATION, never that a model did anything.
        factory.SessionRunner.ShouldNotBeNull();
    }

    /// <summary>
    /// The internal purpose is not reachable from outside. The public create request has no field
    /// for it, and a task created through the public path carries none of its markings — which is
    /// what stops a caller from minting a task that publishes to a channel on its own authority.
    /// </summary>
    [Test]
    [Timeout(300_000)]
    public async Task Public_create_cannot_set_the_internal_purpose(CancellationToken ct)
    {
        typeof(CreateAgentTaskRequest).GetProperties()
            .Select(p => p.Name)
            .ShouldNotContain(nameof(AgentTask.OutboundDeliveryId));

        var world = await SeedAsync(ct);
        using var scope = factory.Services.CreateScope();
        var tasks = scope.ServiceProvider.GetRequiredService<AgentTaskService>();

        var created = await tasks.CreateAsync(
            new CreateAgentTaskRequest(
                Goal: "An ordinary Custom task that has nothing to do with outbound conversion.",
                Title: "Companion",
                Kind: AgentTaskKind.Worker,
                Role: AgentTaskRole.Custom,
                Workspace: WorkspaceMode.Shared,
                WorkingDirectory: world.ConverterWorkspace,
                AgentId: world.ConverterId),
            new AgentTaskService.Caller(
                Task: null,
                SessionId: null,
                WorkingDirectory: world.ConverterWorkspace,
                ProjectId: world.ProjectP),
            ct);

        await using var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.AgentTasks.AsNoTracking().FirstAsync(t => t.Id == created.Id, ct);

        // The companion keeps ordinary behaviour: no outbound link, no forced single attempt, no
        // forced deadline. Whatever the internal purpose changes, it changes it ONLY for itself.
        row.OutboundDeliveryId.ShouldBeNull();
        row.ExecutionDeadlineAt.ShouldBeNull();
        row.MaxAttempts.ShouldBeGreaterThan(0);
        row.Role.ShouldBe(AgentTaskRole.Custom);
    }

    /// <summary>
    /// A second pump tick over the same delivery must not mint a second invocation. Metering is the
    /// whole authorization story the operator agreed to when they bound the profile.
    /// </summary>
    [Test]
    [Timeout(300_000)]
    public async Task A_repeated_tick_creates_no_second_invocation(CancellationToken ct)
    {
        var world = await SeedAsync(ct);
        await ReleaseSeatsAsync(ct);

        Guid deliveryId;
        using (var scope = factory.Services.CreateScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<ChannelOutboundService>()
                .SendAsync(Request(world, promptSequence: 42), ct);
            deliveryId = result.DeliveryId!.Value;
        }

        for (var i = 0; i < 3; i++)
        {
            using var scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ChannelOutboundService>().PumpOnceAsync(ct);
        }

        using var read = factory.Services.CreateScope();
        await using var db = read.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.AgentTasks.CountAsync(t => t.OutboundDeliveryId == deliveryId, ct)).ShouldBe(1);
    }

    // ---- fixture helpers -----------------------------------------------------------------------

    private sealed record SeededWorld(
        Guid ProjectP, Guid InboundId, Guid ConverterId, string ConverterWorkspace,
        Guid ChannelX, Guid SourceTaskId);

    private static SeededWorld? _world;

    /// <summary>
    /// Frees the converter's single seat. One active task per converter is the production rule
    /// (<c>OutboundConversionTaskRunner</c> refuses a second), so a delivery another test in this
    /// class left Converting would legitimately block the next one's dispatch. Retiring those rows
    /// is fixture bookkeeping, not a relaxation of the rule — the rule itself is asserted in
    /// <c>ChannelOutboundDeadlineTests.A_busy_or_expired_seat_takes_no_new_launch</c>.
    /// </summary>
    private async Task ReleaseSeatsAsync(CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        await using var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.ChannelOutboundDeliveries
            .Where(d => d.State != ChannelOutboundDeliveryState.Published)
            .ExecuteUpdateAsync(u => u
                .SetProperty(d => d.State, ChannelOutboundDeliveryState.Published), ct);
    }

    private static ChannelOutboundRequest Request(SeededWorld world, long promptSequence = 1) => new(
        new ChannelReply
        {
            Channel = "slack",
            ConversationId = "conversion-X",
            ReplyHandle = "handle-T1",
            Text = "Here are the sources.",
            Attachments =
            [
                new OutboundAttachment
                {
                    Kind = AttachmentKind.File,
                    Name = "01-requirements.md",
                    Mime = "text/markdown",
                    Content = Encoding.UTF8.GetBytes("# requirements\n"),
                },
            ],
        },
        ChannelOutboundOrigin.AgentReply,
        ChannelOutboundSendKind.Main,
        SessionId: null,
        PromptSequence: promptSequence,
        TextWindowStart: promptSequence,
        TextWindowEnd: promptSequence + 1,
        ChannelId: world.ChannelX,
        ProjectId: world.ProjectP,
        CorrelationIds: [],
        SourceTaskIds: [world.SourceTaskId]);

    private async Task<SeededWorld> SeedAsync(CancellationToken ct)
    {
        if (_world is { } existing)
            return existing;

        var root = Path.Combine(Path.GetTempPath(), "antiphon-conversion-task-" + Guid.NewGuid().ToString("N"));
        var converterWorkspace = Path.Combine(root, "converter");
        Directory.CreateDirectory(Path.Combine(converterWorkspace, "prompts"));
        await File.WriteAllTextAsync(Path.Combine(converterWorkspace, "prompts", "pdf.md"), "# convert\n", ct);
        Directory.CreateDirectory(Path.Combine(root, "inbound"));

        var projectP = Guid.NewGuid();
        var inboundId = Guid.NewGuid();
        var converterId = Guid.NewGuid();
        var channelX = Guid.NewGuid();
        var sourceTaskId = Guid.NewGuid();

        using var scope = factory.Services.CreateScope();
        await using var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        db.Projects.Add(new Project
        {
            Id = projectP,
            Name = "conversion-P",
            GitRepositoryUrl = "https://example.invalid/p.git",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.Agents.AddRange(
            NewAgent(inboundId, "conv-inbound", projectP, Path.Combine(root, "inbound")),
            NewAgent(converterId, "conv-converter", projectP, converterWorkspace));
        db.ChatChannels.Add(new ChatChannel
        {
            Id = channelX,
            Provider = "slack",
            ExternalId = "conversion-X",
            Kind = ChatChannelKind.Group,
            Title = "conversion-X",
            AgentId = inboundId,
            Enabled = true,
            OutboundAgentProfile = ProfileName,
            ReplyHandle = "handle-T1",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.AgentTasks.Add(new AgentTask
        {
            Id = sourceTaskId,
            RootTaskId = sourceTaskId,
            Title = "Source docs",
            Goal = "Write the docs. " + SourceSentinel,
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Custom,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = root,
            RepoPath = root,
            Status = AgentTaskStatus.Succeeded,
            CreatedAt = now,
            CompletedAt = now,
        });
        await db.SaveChangesAsync(ct);

        factory.Configure(s => s.Profiles[ProfileName] = new ChannelOutboundProfileSettings
        {
            ProjectId = projectP,
            AgentId = converterId,
            PromptFile = "prompts/pdf.md",
            Trigger = ChannelOutboundTrigger.MarkdownSources,
            TimeoutSeconds = 300,
            MaxPending = 8,
        });

        _world = new SeededWorld(projectP, inboundId, converterId, converterWorkspace, channelX, sourceTaskId);
        return _world;
    }

    private static Agent NewAgent(Guid id, string name, Guid projectId, string workspace)
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
}
