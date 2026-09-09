using System.Text.RegularExpressions;
using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Antiphon.Tests.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public class SpecialistQualificationTests
{
    private sealed class SyntheticEvidence : ISpecialistExecutionEvidenceReader
    {
        public bool Unavailable { get; set; }
        public bool TransportTimeout { get; set; }
        public Task<SpecialistExecutionEvidence?> ReadAsync(Agent seat, AgentSession session, CancellationToken ct) =>
            TransportTimeout ? throw new TaskCanceledException("owned synthetic HTTP timeout with a live caller") :
            Task.FromResult<SpecialistExecutionEvidence?>(Unavailable ? null : new("fixture-capability", SpecialistExecutionEvidenceReader.Hash(
                $"{seat.Id}:{seat.Kind}:{seat.ModelLevel}:{seat.ModelId}:{seat.SystemPromptAppend}:{session.Id}:{session.StartedAt:O}"), 20000,
                SpecialistEvidenceProvenance.Synthetic));
        // This is the explicitly injected external-verifier double. No production switch accepts it.
        public bool Trusts(SpecialistQualificationEvidence evidence) => evidence.Provenance == SpecialistEvidenceProvenance.Synthetic
            && evidence.TaskIds.Distinct().Count() == 2 && evidence.EvidenceNonces.Distinct().Count() == 2;
    }
    private sealed class Available : IModelAvailability
    { public Task<bool> IsHeldAsync(AgentKind kind, string alias, CancellationToken ct) => Task.FromResult(false); }
    private sealed class Lifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource source = new();
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => source.Token;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() => source.Cancel();
        public void Dispose() => source.Dispose();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task Card0415_V07_authorized_batch_uses_real_dispatch_queue_and_settlement_once(bool wrongSemanticAnswer) =>
        ExerciseAsync(wrongSemanticAnswer, false);

    [Test]
    public Task Card0415_V06_qualified_check_wins_from_native_turn_and_duplicate_request_reuses_result() => ExerciseAsync(false, true);

    [Test]
    public Task Card0415_V24_failed_primary_uses_only_the_qualified_declared_alternate_and_resolves_service_health() => ExerciseAsync(false, true, "fallback");

    [Test]
    [Arguments("newer-check")]
    [Arguments("new-generation")]
    [Arguments("transport-timeout")]
    public Task Card0415_V15_old_or_unverifiable_native_completion_cannot_win(string change) => ExerciseAsync(false, true, change);

    [Test]
    public Task Card0415_V08_lost_capability_invalidates_qualification_without_new_paid_work() => ExerciseAsync(false, true, "lost-capability");

    [Test]
    [Arguments("cancel-caller")]
    [Arguments("cancel-host")]
    public Task Card0415_V15_cancellation_closes_owned_attempt_with_distinct_neutral_outcome(string change) => ExerciseAsync(false, true, change);

    private static async Task ExerciseAsync(bool wrongSemanticAnswer, bool realCheck, string? change = null)
    {
        var verifier = new SyntheticEvidence();
        using var lifetime = new Lifetime();
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
                services.AddSingleton<ISpecialistExecutionEvidenceReader>(verifier);
                services.AddSingleton<IHostApplicationLifetime>(lifetime);
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
            var task = await db.AgentTasks.SingleAsync(t => t.Status == AgentTaskStatus.Dispatched);
            var executingSession = task.AgentSessionId!.Value;
            submitted.Add(task.Id);
            if (submitted.Count == 3)
            {
                if (change == "newer-check")
                    await db.AgentTasks.Where(t => t.Role == AgentTaskRole.Code).ExecuteUpdateAsync(u => u.SetProperty(t => t.CheckCount, 2));
                if (change == "new-generation")
                    await db.AgentSessions.Where(s => s.Id == h.SessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.StartedAt, DateTime.UtcNow));
                if (change == "transport-timeout") verifier.TransportTimeout = true;
            }
            var nonce = Regex.Match(body, "(?:violet|copper)-[a-f0-9]{12}").Value;
            nonce.ShouldNotBeNullOrEmpty();
            var answer = wrongSemanticAnswer ? "On track, unrelated amber tests are still running."
                : nonce.StartsWith("violet") ? $"On track, {nonce} change has seven focused passes; integration remains pending."
                : $"Needs attention, {nonce} provider has not answered; the harness deadline retries once.";
            if (change == "fallback" && task.AgentId == h.AgentId && submitted.Count > 4) answer = "ready";
            var next = (await db.TranscriptEntries.Where(e => e.AgentSessionId == executingSession).MaxAsync(e => (long?)e.Sequence) ?? 0) + 1;
            var call = Guid.NewGuid().ToString("N");
            var now = DateTime.UtcNow;
            db.TranscriptEntries.AddRange(
                new() { Id = Guid.NewGuid(), AgentSessionId = executingSession, Sequence = next, Kind = TranscriptKinds.UserPrompt, Text = body, Timestamp = now, CreatedAt = now },
                new() { Id = Guid.NewGuid(), AgentSessionId = executingSession, Sequence = next + 1, Kind = TranscriptKinds.AssistantText,
                    ApiCallId = call, Text = answer + "\n" + DelegationReportFormatter.ReportToken(task.Id, "done"), CreatedAt = now },
                new() { Id = Guid.NewGuid(), AgentSessionId = executingSession, Sequence = next + 2, Kind = TranscriptKinds.TurnEnd,
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
            Guid? alternateSession = change == "fallback" ? await AddAndQualifyAlternateAsync(h) : null;
            var qualificationTurns = submitted.Count;
            if (change == "lost-capability")
            {
                verifier.Unavailable = true;
                await using var scope = h.Provider.CreateAsyncScope();
                (await scope.ServiceProvider.GetRequiredService<SpecialistRequestService>()
                    .AuthorizeQualificationAsync(candidateId, CancellationToken.None)).ShouldBeFalse();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var candidate = await db.StandingSpecialistCandidateStates.SingleAsync(c => c.Id == candidateId);
                candidate.Status.ShouldBe(StandingSpecialistCandidateStatus.Unqualified);
                candidate.QualifiedAt.ShouldBeNull();
                (await db.AgentTasks.CountAsync()).ShouldBe(2);
                submitted.Count.ShouldBe(2);
                return;
            }
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
            using var caller = new CancellationTokenSource();
            var pending = requestService.RunCheckAsync(checkedTask, 1, $"Changed {nonce}.cs; seven focused tests passed; integration pending.", caller.Token);
            await SpecialistTaskRunnerDeadlineTests.UntilAsync(async () =>
            {
                await using var scope = h.Provider.CreateAsyncScope();
                return await scope.ServiceProvider.GetRequiredService<AppDbContext>().AgentTasks.CountAsync(t => t.Role == AgentTaskRole.Check) == qualificationTurns + 1;
            });
            if (change?.StartsWith("cancel-") == true)
            {
                if (change == "cancel-host") lifetime.StopApplication();
                caller.Cancel();
                await Should.ThrowAsync<OperationCanceledException>(async () => await pending);
                await using var scope = h.Provider.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var request = await db.SpecialistRequests.SingleAsync(r => r.Purpose == SpecialistRequestPurpose.Check);
                var attempt = await db.SpecialistAttempts.SingleAsync(a => a.RequestId == request.Id);
                request.Status.ShouldBe(SpecialistRequestStatus.Canceled);
                request.Outcome.ShouldBe(change == "cancel-host" ? SpecialistAttemptOutcome.HostShutdown : SpecialistAttemptOutcome.CallerCanceled);
                attempt.Outcome.ShouldBe(request.Outcome);
                attempt.CompletedAt.ShouldNotBeNull();
                (await db.AgentTasks.SingleAsync(t => t.Id == attempt.TaskId)).Status.ShouldBe(AgentTaskStatus.Canceled);
                (await db.StandingSpecialistCandidateStates.SingleAsync(c => c.Id == candidateId)).TransientFailures.ShouldBe(0);
                submitted.Count.ShouldBe(2);
                return;
            }
            await using (var scope = h.Provider.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(CancellationToken.None);
            await h.Provider.GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(h.SessionId, CancellationToken.None);
            if (alternateSession is Guid nextSession)
            {
                await SpecialistTaskRunnerDeadlineTests.UntilAsync(async () =>
                {
                    await using var scope = h.Provider.CreateAsyncScope();
                    return await scope.ServiceProvider.GetRequiredService<AppDbContext>().AgentTasks.AnyAsync(t => t.Status == AgentTaskStatus.Queued);
                });
                await using (var scope = h.Provider.CreateAsyncScope())
                    await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(CancellationToken.None);
                await h.Provider.GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(nextSession, CancellationToken.None);
            }
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(10));
            if (change is not null && change != "fallback")
            {
                result.Outcome.ShouldNotBe(SpecialistRunOutcome.Succeeded);
                result.Result.ShouldBeNull();
                await using var scope = h.Provider.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var request = await db.SpecialistRequests.SingleAsync(r => r.Purpose == SpecialistRequestPurpose.Check);
                request.WinnerAttemptId.ShouldBeNull();
                var candidate = await db.StandingSpecialistCandidateStates.SingleAsync(c => c.Id == candidateId);
                candidate.TransientFailures.ShouldBe(change == "transport-timeout" ? 1 : 0);
                (await db.AgentTasks.SingleAsync(t => t.Id == submitted.Last())).Status.ShouldBe(AgentTaskStatus.Succeeded);
                return;
            }
            result.Outcome.ShouldBe(SpecialistRunOutcome.Succeeded, result.Reason);
            result.Result.ShouldContain(nonce);
            result.RequestId.ShouldNotBeNull();
            var duplicate = await requestService.RunCheckAsync(checkedTask, 1, "must not replace captured facts", CancellationToken.None);
            duplicate.RequestId.ShouldBe(result.RequestId);
            duplicate.Result.ShouldBe(result.Result);
            submitted.Count.ShouldBe(qualificationTurns + (alternateSession is null ? 1 : 2), "bounded qualification and real Check turns, no recursive qualification or duplicate request work");
            await using var verifyScope = h.Provider.CreateAsyncScope();
            var verify = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var winningRequest = await verify.SpecialistRequests.SingleAsync(r => r.Purpose == SpecialistRequestPurpose.Check);
            winningRequest.Status.ShouldBe(SpecialistRequestStatus.Succeeded);
            winningRequest.WinnerAttemptId.ShouldNotBeNull();
            winningRequest.Facts.ShouldContain(nonce);
            winningRequest.Facts.ShouldNotContain("must not replace");
            (await verify.SpecialistAttempts.CountAsync(a => a.RequestId == winningRequest.Id)).ShouldBe(alternateSession is null ? 1 : 2);
            if (alternateSession is not null)
            {
                var attempts = await verify.SpecialistAttempts.Where(a => a.RequestId == winningRequest.Id).OrderBy(a => a.Ordinal).ToListAsync();
                attempts[0].Outcome.ShouldBe(SpecialistAttemptOutcome.InvalidReading);
                attempts[1].Outcome.ShouldBe(SpecialistAttemptOutcome.ValidReading);
                attempts[1].SessionId.ShouldBe(alternateSession.Value);
                attempts[1].DeadlineAt.ShouldBe(winningRequest.DeadlineAt);
                (await verify.StandingSpecialistCandidateStates.SingleAsync(c => c.Id == candidateId)).Status.ShouldBe(StandingSpecialistCandidateStatus.Quarantined);
                await new StandingSpecialistHealthService(verify, Options.Create(settings), TimeProvider.System, h.EventBus,
                    NullLogger<StandingSpecialistHealthService>.Instance).ReconcileOwnerAsync(h.AgentId, CancellationToken.None);
                (await verify.StandingSpecialistHealths.SingleAsync()).Status.ShouldBe(StandingSpecialistHealthStatus.UsingFallback);
            }
        }
    }

    private static async Task<Guid> AddAndQualifyAlternateAsync(BridgeQueueHarness h)
    {
        var seatId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var candidateId = Guid.NewGuid();
        await using (var scope = h.Provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var owner = await db.Agents.SingleAsync(a => a.Id == h.AgentId);
            var now = DateTime.UtcNow;
            var cwd = owner.WorkingDirectory + "-alternate";
            db.Agents.Add(new() { Id = seatId, Name = "declared alternate", Slug = "alternate-" + seatId.ToString("N"),
                WorkingDirectory = cwd, Kind = AgentKind.ClaudeCode, ModelLevel = AgentModelLevel.Medium,
                StandingSpecialistOwnerId = owner.Id, StandingSpecialistRole = AgentTaskRole.Check, AlwaysOn = true,
                PersistentSessionId = sessionId.ToString(), Status = AgentStatus.Running, CreatedAt = now, UpdatedAt = now });
            db.AgentSessions.Add(new() { Id = sessionId, AgentKind = AgentKind.ClaudeCode, Cwd = cwd,
                SessionBackend = SessionBackend.PtyHost, Status = SessionStatus.Running, CreatedAt = now, StartedAt = now, LastSeenAt = now });
            db.StandingSpecialistCandidateStates.Add(new() { Id = candidateId, AgentId = owner.Id, PhysicalAgentId = seatId,
                AgentKind = AgentKind.ClaudeCode, ModelLevel = AgentModelLevel.Medium, ModelAlias = "sonnet", Enabled = true,
                Status = StandingSpecialistCandidateStatus.Unqualified, QualificationAuthorization = Guid.NewGuid(), DeclaredAt = now, UpdatedAt = now });
            db.StandingSpecialistRoutings.Add(new() { Id = Guid.NewGuid(), AgentId = owner.Id, Enabled = true,
                ConcurrencyToken = Guid.NewGuid(), CandidatesJson = RoutingCandidate.Serialize(new RoutingCandidate[]
                    { new(owner.Kind, owner.ModelLevel), new(AgentKind.ClaudeCode, AgentModelLevel.Medium) }), CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync();
        }
        h.Runtime.Register(sessionId, new FakeAgentProtocolAdapter { OnSubmitted = h.Adapter.OnSubmitted });
        await using (var scope = h.Provider.CreateAsyncScope())
            (await scope.ServiceProvider.GetRequiredService<SpecialistRequestService>().AuthorizeQualificationAsync(candidateId, CancellationToken.None)).ShouldBeTrue();
        for (var index = 0; index < 2; index++)
        {
            await using (var scope = h.Provider.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<SpecialistRequestService>().ReconcileAsync(CancellationToken.None);
            await using (var scope = h.Provider.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(CancellationToken.None);
            await h.Provider.GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(sessionId, CancellationToken.None);
            await using (var scope = h.Provider.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<SpecialistRequestService>().ReconcileAsync(CancellationToken.None);
        }
        await using var verifyScope = h.Provider.CreateAsyncScope();
        (await verifyScope.ServiceProvider.GetRequiredService<AppDbContext>().StandingSpecialistCandidateStates.SingleAsync(c => c.Id == candidateId))
            .Status.ShouldBe(StandingSpecialistCandidateStatus.Qualified);
        return sessionId;
    }

    [Test]
    public void Card0415_V07_production_verifier_refuses_synthetic_qualification_provenance()
    {
        var verifier = new SpecialistExecutionEvidenceReader(Options.Create(new SupervisionSettings()));
        verifier.Trusts(new("fixture", SpecialistEvidenceProvenance.Synthetic,
            [Guid.NewGuid(), Guid.NewGuid()], ["violet-fresh", "copper-fresh"])).ShouldBeFalse();
    }
}
