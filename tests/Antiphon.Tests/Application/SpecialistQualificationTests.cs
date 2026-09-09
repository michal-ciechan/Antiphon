using System.Text.RegularExpressions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public class SpecialistQualificationTests
{
    private sealed class SyntheticEvidence : ISpecialistExecutionEvidenceReader
    {
        public Task<SpecialistExecutionEvidence?> ReadAsync(Agent seat, AgentSession session, CancellationToken ct) =>
            Task.FromResult<SpecialistExecutionEvidence?>(new("fixture-capability", SpecialistExecutionEvidenceReader.Hash(
                $"{seat.Id}:{seat.Kind}:{seat.ModelLevel}:{seat.ModelId}:{seat.SystemPromptAppend}:{session.Id}:{session.StartedAt:O}"), 20000,
                SpecialistEvidenceProvenance.Synthetic));
        // This is the explicitly injected external-verifier double. No production switch accepts it.
        public bool Trusts(SpecialistQualificationEvidence evidence) => evidence.Provenance == SpecialistEvidenceProvenance.Synthetic
            && evidence.TaskIds.Distinct().Count() == 2 && evidence.EvidenceNonces.Distinct().Count() == 2;
    }
    private sealed class Available : IModelAvailability
    { public Task<bool> IsHeldAsync(AgentKind kind, string alias, CancellationToken ct) => Task.FromResult(false); }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task Card0415_V07_authorized_batch_uses_real_dispatch_queue_and_settlement_once(bool wrongSemanticAnswer) =>
        ExerciseAsync(wrongSemanticAnswer, false);

    [Test]
    public Task Card0415_V06_qualified_check_wins_from_native_turn_and_duplicate_request_reuses_result() => ExerciseAsync(false, true);

    private static async Task ExerciseAsync(bool wrongSemanticAnswer, bool realCheck)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var settings = new DelegationSettings { CheckInterpreterAgentSlug = "c415-" + Guid.NewGuid().ToString("N"),
            ModernPtyBriefInlineMaxBytes = 128, ModernPtySingleWriteMaxBytes = 128 };
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString, Delegation = settings,
            ConfigureServices = services =>
            {
                services.AddSingleton(sp => new PtyDeliveryProfile(sp.GetRequiredService<IServiceScopeFactory>(),
                    NullLogger<PtyDeliveryProfile>.Instance, Options.Create(settings), backendOverride: "modern"));
                services.AddScoped<ISpecialistExecutionEvidenceReader, SyntheticEvidence>();
                services.AddScoped<IModelAvailability, Available>();
                services.AddSingleton(Options.Create(new SubscriptionQuotaGateSettings { Enabled = false }));
                services.AddScoped<SubscriptionUsageReader>();
                services.AddScoped<SubscriptionQuotaGate>();
                services.AddScoped<SpecialistRequestService>();
                services.AddScoped<DelegationWorkspaceResolver>();
                services.AddDelegationWorktreeGraph(new GitSettings { WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-c415-qualification") });
                services.AddScoped<IDelegateSessionStopper>(sp => sp.GetRequiredService<AgentSessionService>());
                services.AddScoped<AgentTaskService>();
                services.AddSingleton<AgentTaskReplyService>();
                services.AddScoped<AgentTaskDispatcher>();
            },
        });
        Guid candidateId;
        await using (var scope = h.Provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var owner = await db.Agents.SingleAsync(a => a.Id == h.AgentId);
            owner.Kind = AgentKind.ClaudeCode; owner.ModelLevel = AgentModelLevel.Low; owner.Slug = settings.CheckInterpreterAgentSlug;
            owner.StandingSpecialistRole = AgentTaskRole.Check; owner.StandingSpecialistOwnerId = owner.Id;
            db.StandingSpecialistCandidateStates.Add(new() { Id = candidateId = Guid.NewGuid(), AgentId = owner.Id,
                PhysicalAgentId = owner.Id, AgentKind = owner.Kind, ModelLevel = owner.ModelLevel, ModelAlias = "haiku",
                Enabled = true, Status = StandingSpecialistCandidateStatus.Unqualified, QualificationAuthorization = Guid.NewGuid(),
                DeclaredAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, TransientFailures = 1 });
            await db.SaveChangesAsync();
        }
        var submitted = new List<Guid>();
        h.Adapter.OnSubmitted = async body =>
        {
            await using var scope = h.Provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var task = await db.AgentTasks.SingleAsync(t => t.AgentId == h.AgentId && t.Status == AgentTaskStatus.Dispatched);
            submitted.Add(task.Id);
            var nonce = Regex.Match(body, "(?:violet|copper)-[a-f0-9]{12}").Value;
            nonce.ShouldNotBeNullOrEmpty();
            var answer = wrongSemanticAnswer ? "On track, unrelated amber tests are still running."
                : nonce.StartsWith("violet") ? $"On track, {nonce} change has seven focused passes; integration remains pending."
                : $"Needs attention, {nonce} provider has not answered; the harness deadline retries once.";
            var next = (await db.TranscriptEntries.Where(e => e.AgentSessionId == h.SessionId).MaxAsync(e => (long?)e.Sequence) ?? 0) + 1;
            var call = Guid.NewGuid().ToString("N");
            var now = DateTime.UtcNow;
            db.TranscriptEntries.AddRange(
                new() { Id = Guid.NewGuid(), AgentSessionId = h.SessionId, Sequence = next, Kind = TranscriptKinds.UserPrompt, Text = body, Timestamp = now, CreatedAt = now },
                new() { Id = Guid.NewGuid(), AgentSessionId = h.SessionId, Sequence = next + 1, Kind = TranscriptKinds.AssistantText,
                    ApiCallId = call, Text = answer + "\n" + DelegationReportFormatter.ReportToken(task.Id, "done"), CreatedAt = now },
                new() { Id = Guid.NewGuid(), AgentSessionId = h.SessionId, Sequence = next + 2, Kind = TranscriptKinds.TurnEnd,
                    ApiCallId = call, StopReason = "end_turn", CreatedAt = now });
            await db.SaveChangesAsync();
        };
        await using (var scope = h.Provider.CreateAsyncScope())
            (await scope.ServiceProvider.GetRequiredService<SpecialistRequestService>().AuthorizeQualificationAsync(candidateId, CancellationToken.None)).ShouldBeTrue();
        for (var index = 0; index < (wrongSemanticAnswer ? 1 : 2); index++)
        {
            await using (var scope = h.Provider.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<SpecialistRequestService>().ReconcileAsync(CancellationToken.None);
            await using (var scope = h.Provider.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(CancellationToken.None);
            await h.Provider.GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(h.SessionId, CancellationToken.None);
            await using (var scope = h.Provider.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<SpecialistRequestService>().ReconcileAsync(CancellationToken.None);
        }
        await using (var scope = h.Provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var candidate = await db.StandingSpecialistCandidateStates.SingleAsync(c => c.Id == candidateId);
            var diagnostics = candidate.Reason + " | " + string.Join(" | ", await db.SpecialistAttempts.Select(a => a.Outcome + ": " + a.Reason).ToListAsync())
                + " | " + string.Join(" | ", await db.AgentTasks.Select(t => t.Status + ": " + t.FailureReason).ToListAsync())
                + " | " + string.Join(" | ", await db.SessionQueuedMessages.Select(m => m.ExecutionTaskId + ": " + m.DeliveryVerdict + ": floor " + m.LastDeliveryBaselineSequence).ToListAsync())
                + " | " + string.Join(" | ", await db.TranscriptEntries.Select(e => e.Sequence + ": " + e.Kind).ToListAsync());
            candidate.Status.ShouldBe(wrongSemanticAnswer ? StandingSpecialistCandidateStatus.Quarantined : StandingSpecialistCandidateStatus.Qualified, diagnostics);
            candidate.TransientFailures.ShouldBe(wrongSemanticAnswer ? 1 : 0);
            var request = await db.SpecialistRequests.SingleAsync();
            request.Purpose.ShouldBe(SpecialistRequestPurpose.Qualification);
            request.Status.ShouldBe(wrongSemanticAnswer ? SpecialistRequestStatus.Failed : SpecialistRequestStatus.Succeeded);
            request.WinnerAttemptId.ShouldBeNull();
            var tasks = await db.AgentTasks.ToListAsync();
            tasks.Count.ShouldBe(wrongSemanticAnswer ? 1 : 2);
            tasks.All(t => t.Status == AgentTaskStatus.Succeeded && t.Role == AgentTaskRole.Check && t.CardId == null && t.ReplyTo == AgentTaskReplyTo.None).ShouldBeTrue();
            (await db.SessionQueuedMessages.CountAsync(m => m.DeliveryVerdict == DeliveryVerdict.Delivered)).ShouldBe(tasks.Count);
            submitted.Count.ShouldBe(tasks.Count);
            (await scope.ServiceProvider.GetRequiredService<SpecialistRequestService>().AuthorizeQualificationAsync(candidateId, CancellationToken.None)).ShouldBeFalse();
            (await db.SpecialistRequests.CountAsync()).ShouldBe(1);
            (await db.StandingSpecialistHealths.CountAsync()).ShouldBe(0);
        }
        if (realCheck)
        {
            var checkedTask = new AgentTask { Id = Guid.NewGuid(), Title = "checked delegate", Role = AgentTaskRole.Code,
                Status = AgentTaskStatus.Working, CreatedAt = DateTime.UtcNow, CheckCount = 1 };
            checkedTask.RootTaskId = checkedTask.Id;
            await using (var scope = h.Provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.AgentTasks.Add(checkedTask);
                await db.SaveChangesAsync();
            }
            var nonce = "violet-" + Guid.NewGuid().ToString("N")[..12];
            await using var requestScope = h.Provider.CreateAsyncScope();
            var requestService = requestScope.ServiceProvider.GetRequiredService<SpecialistRequestService>();
            var pending = requestService.RunCheckAsync(checkedTask, 1, $"Changed {nonce}.cs; seven focused tests passed; integration pending.", CancellationToken.None);
            await SpecialistTaskRunnerDeadlineTests.UntilAsync(async () =>
            {
                await using var scope = h.Provider.CreateAsyncScope();
                return await scope.ServiceProvider.GetRequiredService<AppDbContext>().AgentTasks.CountAsync(t => t.Role == AgentTaskRole.Check) == 3;
            });
            await using (var scope = h.Provider.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(CancellationToken.None);
            await h.Provider.GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(h.SessionId, CancellationToken.None);
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(10));
            result.Outcome.ShouldBe(SpecialistRunOutcome.Succeeded, result.Reason);
            result.Result.ShouldContain(nonce);
            result.RequestId.ShouldNotBeNull();
            var duplicate = await requestService.RunCheckAsync(checkedTask, 1, "must not replace captured facts", CancellationToken.None);
            duplicate.RequestId.ShouldBe(result.RequestId);
            duplicate.Result.ShouldBe(result.Result);
            submitted.Count.ShouldBe(3, "two qualification prompts, one real-purpose Check, no retry publication or recursive qualification");
            await using var verifyScope = h.Provider.CreateAsyncScope();
            var verify = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var request = await verify.SpecialistRequests.SingleAsync(r => r.Purpose == SpecialistRequestPurpose.Check);
            request.Status.ShouldBe(SpecialistRequestStatus.Succeeded);
            request.WinnerAttemptId.ShouldNotBeNull();
            request.Facts.ShouldContain(nonce);
            request.Facts.ShouldNotContain("must not replace");
            (await verify.SpecialistAttempts.CountAsync(a => a.RequestId == request.Id)).ShouldBe(1);
        }
    }

    [Test]
    public void Card0415_V07_production_verifier_refuses_synthetic_qualification_provenance()
    {
        var verifier = new SpecialistExecutionEvidenceReader(Options.Create(new SupervisionSettings()));
        verifier.Trusts(new("fixture", SpecialistEvidenceProvenance.Synthetic,
            [Guid.NewGuid(), Guid.NewGuid()], ["violet-fresh", "copper-fresh"])).ShouldBeFalse();
    }
}
