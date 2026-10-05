using Antiphon.Messaging.Client;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Files;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class AgentTaskReplyIntegrationTests
{
    internal static async Task SettleExistingConversionTaskAsync(
        string connectionString, Guid taskId, Guid sessionId)
    {
        await SeedTurnAsync(sessionId, DelegationReportFormatter.TaskMarker(taskId),
            "Wrote the conversion output manifest.", connectionString: connectionString);
        using var factory = new TestScopeFactory(connectionString: connectionString);
        await CreateService(factory).OnTurnEndAsync(sessionId, CancellationToken.None);
    }

    [Test]
    public async Task Internal_conversion_settles_with_usage_but_without_sources_or_follow_up()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var document = Path.Combine(workspace.Path, "docs", "result.md");
        Directory.CreateDirectory(Path.GetDirectoryName(document)!);
        await File.WriteAllTextAsync(document, "# Ordinary source\nSentinel for the normal Custom task.\n");
        var projectId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var inboundId = Guid.NewGuid();
        var converterId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var deliveryId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using (var seed = CreateContext(isolated.ConnectionString))
        {
            seed.Projects.Add(new Project { Id = projectId, Name = "purpose-" + projectId.ToString("N"),
                CreatedAt = now, UpdatedAt = now });
            seed.Boards.Add(new Board { Id = boardId, ProjectId = projectId, Name = "purpose",
                CreatedAt = now, UpdatedAt = now });
            seed.Agents.AddRange(
                new Agent { Id = inboundId, BoardId = boardId, Name = "inbound",
                    Slug = "inbound-" + inboundId.ToString("N"), WorkingDirectory = workspace.Path },
                new Agent { Id = converterId, BoardId = boardId, Name = "converter",
                    Slug = "converter-" + converterId.ToString("N"), WorkingDirectory = workspace.Path });
            seed.ChatChannels.Add(new ChatChannel { Id = channelId, Provider = "fake",
                ExternalId = channelId.ToString("N"), AgentId = inboundId,
                CreatedAt = now, UpdatedAt = now });
            seed.ChannelOutboundDeliveries.Add(new ChannelOutboundDelivery
            {
                Id = deliveryId, SourceKey = Guid.NewGuid().ToString("N"), ChannelId = channelId,
                ProjectId = projectId, InboundAgentId = inboundId, ConverterAgentId = converterId,
                SourceSessionId = Guid.NewGuid(), SendKind = "main", ProfileName = "purpose",
                PromptRevision = new string('a', 64), PromptText = "Convert sources.",
                Trigger = "MarkdownSources", InputPath = Path.Combine(workspace.Path, "input.json"),
                InputSha256 = new string('b', 64), CreatedAt = now, DeadlineAt = now.AddMinutes(2),
            });
            await seed.SaveChangesAsync();
        }

        var (internalTask, internalSession) = await SeedDispatchedTaskAsync(workspace.Path,
            configure: task =>
            {
                task.Role = AgentTaskRole.Custom;
                task.ProjectId = projectId;
                task.AgentId = converterId;
                task.OutboundDeliveryId = deliveryId;
                task.RepoPath = workspace.Path;
            }, connectionString: isolated.ConnectionString);
        var report = "Wrote `docs/result.md`.";
        await SeedTurnAsync(internalSession, DelegationReportFormatter.TaskMarker(internalTask.Id),
            report, inputTokens: 19, outputTokens: 7, connectionString: isolated.ConnectionString);
        using var factory = new TestScopeFactory(connectionString: isolated.ConnectionString);
        await CreateService(factory).OnTurnEndAsync(internalSession, CancellationToken.None);

        await using (var verify = CreateContext(isolated.ConnectionString))
        {
            var settled = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == internalTask.Id);
            settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
            settled.Result.ShouldBe(report);
            settled.ReportEvidence.ShouldBe(AgentTaskReportEvidence.Marked);
            settled.TokensIn.ShouldBe(19);
            settled.TokensOut.ShouldBe(7);
            settled.DeliverableBundleDir.ShouldBeNull();
            settled.DeliverableFileCount.ShouldBe(0);
            settled.CardId.ShouldBeNull();
            settled.ParentSessionId.ShouldBeNull();
            (await verify.AgentTasks.CountAsync(t => t.ParentTaskId == internalTask.Id)).ShouldBe(0);
            (await verify.AgentTasks.CountAsync(t => t.RootTaskId == internalTask.Id
                && t.Id != internalTask.Id)).ShouldBe(0);
            (await verify.AgentTaskLandNotifications.CountAsync(n => n.TaskId == internalTask.Id)).ShouldBe(0);
            (await verify.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == internalTask.Id)).ShouldBe(0);
            (await verify.OutputDistillations.CountAsync(d => d.TaskId == internalTask.Id)).ShouldBe(0);
            (await verify.ChannelOutboundDeliveries.CountAsync(d => d.SourceTaskId == internalTask.Id))
                .ShouldBe(0);
            OutputDistillationService.ShouldRequest(settled,
                new DelegationSettings { OutputDistillerEnabled = true }).ShouldBeFalse();
        }

        var (ordinaryTask, ordinarySession) = await SeedDispatchedTaskAsync(workspace.Path,
            configure: task =>
            {
                task.Role = AgentTaskRole.Custom;
                task.ProjectId = projectId;
                task.RepoPath = workspace.Path;
            }, connectionString: isolated.ConnectionString);
        await SeedTurnAsync(ordinarySession, DelegationReportFormatter.TaskMarker(ordinaryTask.Id),
            report, connectionString: isolated.ConnectionString);
        await CreateService(factory).OnTurnEndAsync(ordinarySession, CancellationToken.None);
        await using var companion = CreateContext(isolated.ConnectionString);
        var normal = await companion.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == ordinaryTask.Id);
        normal.Status.ShouldBe(AgentTaskStatus.Succeeded);
        normal.OutboundDeliveryId.ShouldBeNull();
        normal.DeliverableFileCount.ShouldBe(1);
        var copied = DeliverableBundleService.ListAttachableFiles(normal).ShouldHaveSingleItem();
        (await File.ReadAllBytesAsync(copied)).ShouldBe(await File.ReadAllBytesAsync(document));
    }

    [Test]
    public Task Deferred_is_durable_and_releases_runtime() => VerifyDeferredRuntimeAsync();

    internal async Task VerifyDeferredRuntimeAsync()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        var projectId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var converterId = Guid.NewGuid();
        var storeRoot = Directory.CreateTempSubdirectory("c0418-runtime-outbound-").FullName;
        var files = new ChannelOutboundFileStore(storeRoot);
        var settings = Options.Create(new ChannelOutboundSettings
        {
            UnifiedRecoveryEnabled = true,
            Profiles = new Dictionary<string, ChannelOutboundProfile>
            {
                ["pdf"] = new() { ProjectId = projectId, AgentId = converterId,
                    PromptFile = "convert.md", Trigger = ChannelOutboundTrigger.MarkdownSources },
            },
        });
        var converterHeld = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        var converterMaterialized = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseConverter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? pumpTask = null;
        Task? runtimeTask = null;
        BridgeQueueHarness? h = null;

        async Task RunHeldConverterAsync(Guid deliveryId)
        {
            await using var pumpDb = CreateContext(isolated.ConnectionString);
            var tasks = new AgentTaskService(pumpDb,
                new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
                Options.Create(new DelegationSettings { AllowedRoots = [h!.TempRoot] }),
                new MockEventBus(), new RecordingSessionStopper(), TimeProvider.System,
                NullLogger<AgentTaskService>.Instance);
            var pump = new ChannelOutboundDeliveryPump(pumpDb,
                new OutboundConversionTaskRunner(pumpDb, tasks), files, h.Messaging,
                Options.Create(new AntiphonMessagingOptions()), TimeProvider.System,
                NullLogger<ChannelOutboundDeliveryPump>.Instance, settings,
                new ChannelReplyPreparation(new ChannelReplyAttachmentReader()));
            (await pump.TickAsync(CancellationToken.None)).ShouldBe(1);
            (await pump.TickAsync(CancellationToken.None)).ShouldBe(1);
            converterMaterialized.TrySetResult(deliveryId);
            pump.ProbeBarrierAsync = async (boundary, id, ct) =>
            {
                if (boundary != "before-conversion-observation" || id != deliveryId) return;
                converterHeld.TrySetResult(id);
                await releaseConverter.Task.WaitAsync(ct);
            };
            (await pump.TickAsync(CancellationToken.None)).ShouldBe(1);
        }

        try
        {
            h = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
            {
                ConnectionString = isolated.ConnectionString,
                PreserveDatabaseOnDispose = true,
                Bridge = new ChannelBridgeSettings { Enabled = true, DebounceWindowMs = 0 },
                ConfigureServices = services =>
                {
                    services.AddSingleton<IOptions<ChannelOutboundSettings>>(settings);
                    services.AddSingleton<IChannelOutboundFileStore>(files);
                    services.AddScoped<ChannelOutboundService>(sp =>
                    {
                        var service = new ChannelOutboundService(
                            sp.GetRequiredService<AppDbContext>(), files, h!.Messaging,
                            settings, TimeProvider.System);
                        service.ProbeBarrierAsync = async (boundary, id, ct) =>
                        {
                            if (boundary != "capture-committed") return;
                            await using var observer = CreateContext(isolated.ConnectionString);
                            var capture = await observer.ChannelOutboundDeliveries.AsNoTracking().SingleAsync(d => d.Id == id);
                            capture.State.ShouldBe(ChannelOutboundDeliveryState.Captured);
                            capture.InputPath.ShouldBeEmpty();
                            capture.PreparationAttempts.ShouldBe(0);
                            var member = await observer.SessionQueuedMessages.AsNoTracking()
                                .SingleAsync(m => m.ChannelOutboundDeliveryId == id);
                            member.ChannelReplySettledAt.ShouldBeNull();
                            pumpTask = RunHeldConverterAsync(id);
                            // Materialization and converter creation are setup work, bounded by
                            // the checkpoint. Time only observation after that work commits.
                            var first = await Task.WhenAny(converterMaterialized.Task, pumpTask);
                            if (first == pumpTask) await pumpTask;
                            (await converterMaterialized.Task).ShouldBe(id);
                            first = await Task.WhenAny(converterHeld.Task, pumpTask)
                                .WaitAsync(TimeSpan.FromSeconds(5), ct);
                            if (first == pumpTask) await pumpTask;
                            (await converterHeld.Task).ShouldBe(id);
                        };
                        return service;
                    });
                },
            });
            var root = h.TempRoot;
            var sourcePath = Path.Combine(root, "docs", "source.md");
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            var sourceBytes = "# Source\r\nThe complete source ✨\r\n"u8.ToArray();
            await File.WriteAllBytesAsync(sourcePath, sourceBytes);
            await File.WriteAllTextAsync(Path.Combine(root, "convert.md"), "Convert sources.");
            await using (var seed = CreateContext(isolated.ConnectionString))
            {
                var now = DateTime.UtcNow;
                seed.Projects.Add(new Project { Id = projectId, Name = "runtime-" + projectId.ToString("N"),
                    CreatedAt = now, UpdatedAt = now });
                seed.Boards.Add(new Board { Id = boardId, ProjectId = projectId,
                    Name = "runtime", CreatedAt = now, UpdatedAt = now });
                seed.Agents.Add(new Agent { Id = converterId, BoardId = boardId, Name = "converter",
                    Slug = "converter-" + converterId.ToString("N"), WorkingDirectory = root });
                await seed.SaveChangesAsync();
                await seed.Agents.Where(a => a.Id == h.AgentId)
                    .ExecuteUpdateAsync(u => u.SetProperty(a => a.BoardId, boardId));
            }
            var conversation = await h.BindChannelAsync();
            Guid channelId;
            await using (var binding = CreateContext(isolated.ConnectionString))
            {
                var channel = await binding.ChatChannels.SingleAsync(c => c.ExternalId == conversation);
                channelId = channel.Id;
                channel.OutboundAgentProfile = "pdf";
                await binding.SaveChangesAsync();
            }
            await h.SeedChannelCorrelationAsync("Please send the source", "telegram:" + conversation);

            // Settle the actual source task and deliver its generated note to the parent runtime.
            var (sourceTask, childSession) = await SeedDispatchedTaskAsync(root, h.SessionId,
                t => { t.Role = AgentTaskRole.Docs; t.ProjectId = projectId; t.RepoPath = root; },
                isolated.ConnectionString);
            await SeedTurnAsync(childSession, DelegationReportFormatter.TaskMarker(sourceTask.Id),
                "Wrote `docs/source.md`.", connectionString: isolated.ConnectionString);
            using var factory = new TestScopeFactory(connectionString: isolated.ConnectionString);
            await CreateService(factory).OnTurnEndAsync(childSession, CancellationToken.None);
            await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);

            await using var before = CreateContext(isolated.ConnectionString);
            var note = await before.SessionQueuedMessages.AsNoTracking().SingleAsync(m =>
                m.AgentSessionId == h.SessionId && m.Origin == QueuedMessageOrigin.Delegation);
            note.Status.ShouldBe(QueuedMessageStatus.Sent);
            note.SourceTaskId.ShouldBe(sourceTask.Id);
            note.Body.ShouldContain("deliverable=1 md");
            h.Adapter.SubmittedBodies.ShouldContain(note.Body);
            (await before.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == sourceTask.Id))
                .Status.ShouldBe(AgentTaskStatus.Succeeded);
            h.Messaging.SentReplies.ShouldBeEmpty();

            // The harness adapter writes a synthetic TurnEnd immediately after delivery.
            // Start the answering turn explicitly, as the production transcript does.
            await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, note.Body);
            await h.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText,
                "Here is the complete source document.");
            const string nextPrompt = "Next queued prompt after the source answer";
            var nextId = await h.SeedPendingMessageAsync(nextPrompt);
            var endSequence = await h.CurrentTranscriptMaxSequenceAsync() + 1;
            var turnEnd = new SessionRunnerTranscriptEvent(h.SessionId, endSequence,
                TranscriptKinds.TurnEnd, Guid.NewGuid().ToString("N"), null,
                DateTimeOffset.UtcNow, "assistant", null, null, null, null, null,
                TranscriptKinds.StopReasons.EndTurn);

            // The admission hook starts a real conversion task. Its observation stays held while
            // this runtime must route the source answer and submit the next queued prompt.
            runtimeTask = h.Runtime.ObserveTranscriptAsync(turnEnd, CancellationToken.None);
            // Start the unchanged release budget at the actual converter-held condition,
            // rather than charging the capture/materialization ticks to runtime release.
            var observed = await Task.WhenAny(converterHeld.Task, runtimeTask);
            if (observed == runtimeTask) await runtimeTask;
            converterHeld.Task.IsCompletedSuccessfully.ShouldBeTrue();
            await runtimeTask.WaitAsync(TimeSpan.FromSeconds(5));
            pumpTask.ShouldNotBeNull();
            pumpTask.IsCompleted.ShouldBeFalse();
            h.Adapter.SubmittedBodies.ShouldContain(nextPrompt);
            h.Messaging.SentReplies.ShouldBeEmpty();

            await using var observer = CreateContext(isolated.ConnectionString);
            var intent = await observer.ChannelOutboundDeliveries.AsNoTracking()
                .SingleAsync(d => d.ChannelId == channelId);
            intent.State.ShouldBe(ChannelOutboundDeliveryState.Converting);
            intent.SourceSessionId.ShouldBe(h.SessionId);
            intent.SourceTaskId.ShouldBe(sourceTask.Id);
            intent.ConversionTaskId.ShouldNotBeNull();
            intent.InputSha256.Length.ShouldBe(64);
            var frozen = await files.ReadReplyAsync(intent.InputPath, intent.InputSha256,
                CancellationToken.None);
            frozen.Text.ShouldBe("Here is the complete source document.");
            frozen.Attachments.ShouldHaveSingleItem().Content.ShouldBe(sourceBytes);
            var correlation = await observer.SessionQueuedMessages.AsNoTracking()
                .SingleAsync(m => m.Id == note.Id);
            correlation.ChannelOutboundDeliveryId.ShouldBe(intent.Id);
            correlation.ChannelReplySettledAt.ShouldBeNull();
            (await observer.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == sourceTask.Id))
                .DeliverableDeliveredAt.ShouldBeNull();
            (await observer.ChatChannels.AsNoTracking().SingleAsync(c => c.Id == channelId))
                .LastReplyAt.ShouldBeNull();
            (await observer.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == nextId))
                .Status.ShouldBe(QueuedMessageStatus.Sent);
            (await observer.TranscriptEntries.AsNoTracking().CountAsync(t =>
                t.AgentSessionId == h.SessionId && t.Kind == TranscriptKinds.UserPrompt
                && t.Text == nextPrompt)).ShouldBe(1);

            // A separate connection can lock the claimed intent while the converter remains held.
            await observer.Database.OpenConnectionAsync();
            await using (var claim = observer.Database.GetDbConnection().CreateCommand())
            {
                claim.CommandText = "SELECT \"Id\" FROM \"ChannelOutboundDeliveries\" WHERE \"Id\" = @id FOR UPDATE NOWAIT";
                var parameter = claim.CreateParameter();
                parameter.ParameterName = "id";
                parameter.Value = intent.Id;
                claim.Parameters.Add(parameter);
                (await claim.ExecuteScalarAsync()).ShouldBe(intent.Id);
            }
            (await observer.Projects.Where(p => p.Id == projectId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.UpdatedAt, DateTime.UtcNow))).ShouldBe(1);
        }
        finally
        {
            releaseConverter.TrySetResult();
            if (pumpTask is not null)
                try { await pumpTask.WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (Exception) { /* Preserve the primary assertion failure after barrier release. */ }
            if (runtimeTask is not null)
                try { await runtimeTask.WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (Exception) { /* Await the runtime before disposing its harness. */ }
            if (h is not null)
            {
                await h.DisposeAsync();
                if (Directory.Exists(h.TempRoot)) Directory.Delete(h.TempRoot, recursive: true);
            }
            if (Directory.Exists(storeRoot)) Directory.Delete(storeRoot, recursive: true);
        }
    }
}
