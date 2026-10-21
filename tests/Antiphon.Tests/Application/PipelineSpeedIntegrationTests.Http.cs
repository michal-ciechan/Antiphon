using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Security;
using Antiphon.Tests.TestHelpers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class PipelineSpeedIntegrationTests
{
    private static readonly JsonSerializerOptions ProgramJson = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };
    [Test]
    public async Task C1281_B2HttpAdmissionHolds()
    {
        foreach (var variant in new[] { "dependency", "overlap", "decision", "stage", "host", "quota", "stage-cap", "placement", "workflow", "eligibility", "override", "settlement" })
        {
            await using var host = await PipelineHandoffPilotTests.PilotHost.CreateAsync(services =>
            {
                services.AddScoped<DelegationOpenGate>();
                services.AddScoped<SubscriptionUsageReader>();
                services.AddSingleton(Options.Create(new SubscriptionQuotaGateSettings()));
                services.AddScoped<SubscriptionQuotaGate>();
            }, settings => { settings.MaxConcurrentTasks = 512; settings.RolePolicy["Code"].RecommendedInFlight = 1; });
            using var consumed = await host.Client.PostAsJsonAsync(host.Url,
                host.Request(variant == "eligibility" ? "consumed" : "eligible", "explicit caller decision"));
            consumed.StatusCode.ShouldBe(HttpStatusCode.OK);
            var action = (await consumed.Content.ReadFromJsonAsync<PipelineHandoffActionDto>())!.Action;
            var request = ChildRequest(host.World) with { AgentKind = AgentKind.ClaudeCode,
                Scope = "server/Application/Services/AgentTaskService.cs" };
            await using (var db = host.World.CreateContext())
            {
                if (variant == "dependency")
                {
                    var blocker = new Card { Id = Guid.NewGuid(), BoardId = host.World.Board.Id,
                        BoardColumnId = host.World.Card.BoardColumnId, Identifier = "CARD-9998", Title = "blocker" };
                    db.Add(blocker); db.Add(new CardRelationship { Id = Guid.NewGuid(), CardId = host.World.Card.Id,
                        RelatedCardId = blocker.Id, Kind = CardRelationshipKind.BlockedBy });
                }
                if (variant == "decision") (await db.Cards.SingleAsync(c => c.Id == host.World.Card.Id)).Status = CardStatus.NeedsDecision;
                if (variant == "stage") (await db.AgentTasks.SingleAsync(t => t.Id == host.World.Owner.Id)).NextStage = PipelineHandoffKind.Review;
                if (variant == "settlement")
                {
                    var predecessor = await db.AgentTasks.SingleAsync(t => t.Id == host.World.Owner.Id);
                    predecessor.CompletedAt = predecessor.CompletedAt!.Value.AddSeconds(1);
                }
                if (variant == "workflow") (await db.PipelineHandoffActions.SingleAsync(a => a.Id == action.Id)).WorkflowRunId = Guid.NewGuid();
                if (variant is "overlap" or "stage-cap")
                    db.Add(new AgentTask { Id = Guid.NewGuid(), RootTaskId = Guid.NewGuid(), Title = "occupant", Goal = "occupant",
                        ProjectId = host.World.Project.Id, WorkingDirectory = host.World.RepositoryPath, RepoPath = host.World.RepositoryPath,
                        Scope = variant == "overlap" ? request.Scope : null, Role = AgentTaskRole.Code,
                        Workspace = variant == "overlap" ? WorkspaceMode.Worktree : WorkspaceMode.ReadOnly, Status = AgentTaskStatus.Working });
                if (variant == "host") db.HostBudgets.Add(new HostBudget { HostId = "local", MaxInFlight = 0, Reason = "drain" });
                if (variant == "quota") db.SubscriptionUsageSamples.Add(new SubscriptionUsageSample
                {
                    Id = Guid.NewGuid(), Provider = AgentKind.ClaudeCode, SubscriptionKey = "ClaudeCode", RemainingPercent = 1,
                    ResetsAt = host.Clock.GetUtcNow().UtcDateTime.AddHours(8), ObservedAt = host.Clock.GetUtcNow().UtcDateTime,
                    AgentSessionId = host.World.CallerSessionId, SourceCommand = "/status", ParseStatus = SubscriptionUsageParseStatus.Parsed,
                });
                await db.SaveChangesAsync();
            }
            if (variant == "placement") request = request with { RunnerId = "unknown-fixture-runner" };
            if (variant == "override") request = request with { IgnoreConcurrencyLimit = true };
            var code = variant switch
            {
                "host" => "handoff_capacity_held", "quota" => "subscription_quota_low", "stage-cap" => "concurrency_limit",
                "placement" => "validation_failed", "workflow" => "handoff_workflow_changed", "eligibility" => "handoff_not_eligible",
                "settlement" => "handoff_predecessor_changed",
                _ => "handoff_" + variant + "_held",
            };
            using var refused = await host.Client.PostAsJsonAsync(host.Url + $"/{action.Id:D}/child", new { task = request, acceptSpend = true });
            refused.StatusCode.ShouldBe(variant == "placement" ? HttpStatusCode.UnprocessableEntity : HttpStatusCode.Conflict, variant);
            using var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());
            problem.RootElement.GetProperty("code").GetString().ShouldBe(code, variant);
            await host.RestartHttpAsync();
            var fresh = await host.ReadAsync();
            fresh.Action.ChildTaskId.ShouldBeNull(variant + ": refused child not acknowledged");
            fresh.Observations.Last().Detail.ShouldBe("Child request refused: " + code);
            fresh.Observations.Last().NextObservationAt.ShouldNotBeNull();
            await using var read = host.World.CreateContext();
            (await read.AgentTasks.CountAsync(t => t.CardId == host.World.Card.Id && t.Id != host.World.Owner.Id)).ShouldBe(0);
        }
    }

    [Test]
    public async Task C1281_B2HttpChildRecovery()
    {
        await using var host = await PipelineHandoffPilotTests.PilotHost.CreateAsync(delegation: settings => settings.MaxConcurrentTasks = 512);
        using var consumed = await host.Client.PostAsJsonAsync(host.Url, host.Request("eligible", "caller authorizes next stage"));
        consumed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var action = (await consumed.Content.ReadFromJsonAsync<PipelineHandoffActionDto>())!.Action;
        var url = host.Url + $"/{action.Id:D}/child";
        var request = ChildRequest(host.World);
        using (var other = host.App.GetTestClient())
        {
            other.DefaultRequestHeaders.Add("X-Antiphon-Task-Token", await host.CredentialAsync("other-recipient"));
            using var denied = await other.PostAsJsonAsync(url, new { task = request, acceptSpend = true });
            denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden, "foreign-child-request-refused");
        }
        foreach (var variant in new[] { "spend", "tracker" })
        {
            using var held = await host.Client.PostAsJsonAsync(url, new { task = request,
                acceptSpend = variant != "spend", trigger = variant == "tracker" ? "tracker" : "caller" });
            await RequireProblemAsync(held, "handoff_" + (variant == "spend" ? "spend" : "trigger") + "_held");
            await host.RestartHttpAsync();
            var read = await host.ReadAsync();
            read.Action.ChildTaskId.ShouldBeNull("refusal-durable-zero-child");
            read.Observations.Last().Detail.Contains("handoff_", StringComparison.Ordinal).ShouldBeTrue("refusal-durable-zero-child");
            read.Observations.Last().NextObservationAt.ShouldNotBeNull("refusal-next-observation");
        }
        var recipientToken = await host.CredentialAsync("allowed");
        host.Client.DefaultRequestHeaders.Remove(OperatorTokenFile.Header);
        host.Client.DefaultRequestHeaders.Add("X-Antiphon-Task-Token", recipientToken);
        host.App.Services.GetRequiredService<IEventBus>().ShouldBeOfType<MockEventBus>().ThrowOnceOnEvent = "AgentTaskChanged";
        using var lost = await host.Client.PostAsJsonAsync(url, new { task = request, acceptSpend = true });
        lost.StatusCode.ShouldBe(HttpStatusCode.InternalServerError, "post-commit-event-cut-reached");
        var committed = (await host.ReadAsync()).Action.ChildTaskId.ShouldNotBeNull("committed-child-visible-after-event-failure");
        await host.RestartHttpAsync();
        host.Client.DefaultRequestHeaders.Remove(OperatorTokenFile.Header);
        host.Client.DefaultRequestHeaders.Add("X-Antiphon-Task-Token", recipientToken);
        var pair = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            host.Client.PostAsJsonAsync(url, new { task = request, acceptSpend = true })));
        try
        {
            foreach (var response in pair)
            {
                response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
                (await response.Content.ReadFromJsonAsync<AgentTaskCreatedDto>())!.Id.ShouldBe(committed, "same-key-child-count == 1");
            }
        }
        finally { foreach (var response in pair) response.Dispose(); }
        using var changed = await host.Client.PostAsJsonAsync(url, new { task = request with { Goal = "changed" }, acceptSpend = true });
        await RequireProblemAsync(changed, "handoff_request_changed");
        await using var fresh = host.World.CreateContext();
        (await fresh.AgentTasks.CountAsync(t => t.CardId == host.World.Card.Id && t.Id != host.World.Owner.Id)).ShouldBe(1);
    }

    [Test]
    public async Task C1281_B2ObservationReadback()
    {
        await new PipelineHandoffPilotTests().C1281_DelayAccounting();
        await new PipelineHandoffPilotTests().C1281_WriterReceipt();
    }

    [Test]
    public async Task C1281_WiredRoundtrip()
    {
        await using (var correction = await AdvisoryChangePolicyTests.CorrectionWorld.CreateAsync())
        await using (var factory = new WiringFactory(correction: correction))
        using (var client = factory.CreateClient())
        {
            client.DefaultRequestHeaders.Add(OperatorTokenFile.Header, OperatorTokenFile.ReadOrCreate(factory.OperatorTokenPath));
            using var response = await client.PostAsJsonAsync(CorrectionUrl(correction), correction.Request);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, "real-program-correction-route: " + await response.Content.ReadAsStringAsync());
            using var scope = factory.Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<ICheckpointReceiptValidator>().ShouldNotBeNull();
            scope.ServiceProvider.GetRequiredService<ReviewEvidenceCorrectionService>().ShouldNotBeNull();
            factory.SessionRunner.LaunchAttempts.ShouldBeEmpty();
        }
    }

    [Test]
    public async Task C1281_WiredRoundtripDelivery()
    {
        foreach (var busy in new[] { false, true })
        {
            await using var rig = await C544DeliveryRig.CreateAsync(busy);
            var (task, _) = await rig.SettleReviewAsync(next: "code");
            await rig.DeliverAsync("wired caller"); await rig.ScanAsync();
            await ConsumeAndRequestAsync(rig, task);
        }
    }

    [Test]
    [Arguments("obligation-insert")]
    [Arguments("settled-committed")]
    [Arguments("note-insert")]
    [Arguments("note-committed")]
    [Arguments("wakeup-dropped")]
    [Arguments("render-committed")]
    [Arguments("spill-written")]
    [Arguments("attempt-committed")]
    [Arguments("prompt-accepted")]
    public async Task C1281_WiredDeliveryRecovery(string cut)
    {
        foreach (var busy in new[] { false, true })
        foreach (var spill in new[] { false, true })
        {
            var label = $"{cut} busy={busy} spill={spill}";
            await using var rig = await C544DeliveryRig.CreateAsync(busy, spill: spill);
            if (cut == "wakeup-dropped") rig.Boundary.DropCompletionWakeup = true;
            else if (cut is not ("render-committed" or "spill-written" or "attempt-committed" or "prompt-accepted")) rig.Fault.Cut = cut;
            var (task, _) = await rig.SettleReviewAsync(next: "code", padding: spill ? 40 : 0);
            if (cut == "obligation-insert")
            {
                rig.Fault.Throws.ShouldBe(1, label + ": cut reached");
                (await rig.NotificationAsync(task)).ShouldBeNull();
                await rig.RestartAsync();
                await rig.World.Services.GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(
                    (await rig.World.TaskAsync(task)).AgentSessionId!.Value, CancellationToken.None);
            }
            var note = (await rig.NotificationAsync(task)).ShouldNotBeNull();
            if (cut is "render-committed" or "spill-written" or "attempt-committed" or "prompt-accepted")
            {
                rig.Fault.TaskId = task; rig.Fault.Cut = cut;
                try
                {
                    if (busy) await rig.EndCallerTurnAsync(); else await rig.FlushAsync();
                    if (cut == "prompt-accepted") await rig.ScanAsync();
                }
                catch (IOException error) when (error.Message.StartsWith("c544 completion persistence cut", StringComparison.Ordinal)) { }
            }
            if (cut != "wakeup-dropped") rig.Fault.Throws.ShouldBe(1, label + ": cut reached");
            else rig.Boundary.DropCompletionWakeup.ShouldBeFalse(label + ": dropped-wakeup-cut-reached");
            await rig.RestartAsync(); rig.World.Clock.Advance(TimeSpan.FromMinutes(10));
            await rig.ScanAsync(); if (rig.Busy) await rig.EndCallerTurnAsync();
            await rig.FlushAsync(); await rig.ScanAsync();
            (await rig.NotificationAsync(task))!.Id.ShouldBe(note.Id, "same-notification-after-restart");
            await VerificationRoundDeliveryTests.AssertReceivedOnceAsync(rig, task, label, ["next=code"], expectKind: null,
                expectSpill: spill);
            await ConsumeAndRequestAsync(rig, task);
        }
    }

    private static async Task ConsumeAndRequestAsync(C544DeliveryRig rig, Guid task)
    {
        var note = (await rig.NotificationAsync(task)).ShouldNotBeNull();
        var queue = await rig.RowAsync(task);
        var wire = TaskCompletionNotification.TryReadDelivery(note.CompletionDeliveryJson).ShouldNotBeNull();
        var predecessor = await rig.World.TaskAsync(task);
        var receipt = new PipelineHandoffReceiptReference(note.Id, queue.Id, rig.World.CallerSessionId,
            queue.LastDeliveryGeneration!.Value, note.ConfirmingPromptSequence!.Value, wire.WireSha256);
        await using var factory = new WiringFactory(rig.World);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(OperatorTokenFile.Header, OperatorTokenFile.ReadOrCreate(factory.OperatorTokenPath));
        var url = $"/api/agent-tasks/{task:D}/handoff-actions";
        (await client.GetFromJsonAsync<List<PipelineHandoffActionDto>>(url, ProgramJson))!.ShouldBeEmpty("delivered-not-consumed");
        var request = new PipelineHandoffActionRequest(predecessor.Attempt, note.SourceEventId, AgentTaskRole.Code,
            0, "eligible", "explicit caller consumption", Receipt: receipt);
        foreach (var wrong in new[] { receipt with { ReceiptSequence = queue.LastDeliveryBaselineSequence ?? 0 },
            receipt with { RecipientSessionId = Guid.NewGuid() }, receipt with { RecipientGeneration = receipt.RecipientGeneration.AddSeconds(-1) },
            receipt with { WireDigest = new string('0', 64) } })
        {
            using var denied = await client.PostAsJsonAsync(url, request with { Receipt = wrong }, ProgramJson);
            await RequireProblemAsync(denied, "handoff_foreign_receipt");
        }
        await using (var transcript = rig.World.CreateContext())
        {
            var prompt = await transcript.TranscriptEntries.SingleAsync(t => t.AgentSessionId == receipt.RecipientSessionId
                && t.Sequence == receipt.ReceiptSequence);
            var complete = prompt.Text;
            prompt.Text = wire.WireText[..Math.Min(30, wire.WireText.Length - 1)];
            await transcript.SaveChangesAsync();
            try
            {
                using var clipped = await client.PostAsJsonAsync(url, request, ProgramJson);
                await RequireProblemAsync(clipped, "handoff_foreign_receipt");
            }
            finally { prompt.Text = complete; await transcript.SaveChangesAsync(); }
        }
        using var saved = await client.PostAsJsonAsync(url, request, ProgramJson);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        var action = (await saved.Content.ReadFromJsonAsync<PipelineHandoffActionDto>(ProgramJson))!.Action;
        action.ReceivedAt.ShouldBe(note.ConfirmedAt); action.ConsumedAt.ShouldNotBeNull();
        action.ChildDispatchedAt.ShouldBeNull("unknown-dispatch-null");
        using var child = await client.PostAsJsonAsync(url + $"/{action.Id:D}/child",
            new { task = ChildRequest(rig.World) with { RunnerId = "local" }, acceptSpend = true }, ProgramJson);
        child.StatusCode.ShouldBe(HttpStatusCode.OK, "wired-child: " + await child.Content.ReadAsStringAsync());
        var id = (await child.Content.ReadFromJsonAsync<AgentTaskCreatedDto>(ProgramJson))!.Id;
        var read = (await client.GetFromJsonAsync<List<PipelineHandoffActionDto>>(url, ProgramJson))!.ShouldHaveSingleItem();
        read.Action.ChildTaskId.ShouldBe(id); read.Action.ChildDispatchedAt.ShouldBeNull();
        read.RequestedWithinFiveMinutes.ShouldBe(true, "actual-child-within-five-minutes");
        factory.SessionRunner.LaunchAttempts.ShouldBeEmpty();
    }

    private static async Task RequireProblemAsync(HttpResponseMessage response, string code)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, code + ": " + await response.Content.ReadAsStringAsync());
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString().ShouldBe(code);
    }

    private sealed class WiringFactory(C544World? world = null, AdvisoryChangePolicyTests.CorrectionWorld? correction = null) : AntiphonWebAppFactory
    {
        protected override string ConnectionString => world?.Schema.ConnectionString ?? correction!.ConnectionString;
        protected override void ApplyTestOverrides(IServiceCollection services)
        {
            // Isolate external/background activity; production route and component registrations must stand alone.
            services.RemoveAll<IHostedService>();
            services.RemoveAll<ISessionRunnerDirectory>();
            services.AddSingleton<ISessionRunnerDirectory>(new SingleRunnerDirectory(SessionRunner));
            if (world is not null)
            {
                services.AddSingleton(Options.Create(world.Delegation));
                services.AddSingleton<TimeProvider>(world.Clock);
            }
            if (correction is not null)
                services.Configure<CheckpointReceiptSettings>(settings =>
                {
                    settings.StorageRoot = Path.Combine(correction.Root, "wired-retained");
                    settings.ValidatorScript = Path.Combine(correction.Repository, "scripts", "validate-checkpoint-receipt.ps1");
                    settings.Producers = [new(correction.Request.ProducerIdentity,
                        Path.Combine(correction.ProducerRoot, "producer-pack-index.json"), correction.Request.BundleDigest,
                        correction.Request.RepositoryPath, correction.SelectionRepository)];
                });
        }
    }
}
