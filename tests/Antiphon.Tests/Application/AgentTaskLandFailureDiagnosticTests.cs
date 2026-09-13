using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class AgentTaskLandFailureDiagnosticTests
{
    private const string Marker = "synthetic-secret-marker://user:pw@host/?q=1\n\u001b[31m" + "xxxxx";

    [Test]
    [Arguments("before-operation", "io")]
    [Arguments("before-operation", "timeout")]
    [Arguments("before-operation", "unauthorized")]
    [Arguments("before-operation", "concurrency")]
    [Arguments("before-operation", "persistence")]
    [Arguments("before-operation", "generic")]
    [Arguments("before-operation", "canceled-not-shutdown")]
    [Arguments("after-observed", "io")]
    public async Task C498_HostedDrainClassifiesExecutionException(string seam, string type)
    {
        var entries = new List<RecordingLogEntry>();
        await using var h = new LandingProtocolHarness();
        h.Logger = new RecordingLogger<AgentTaskLandService>(entries);
        var hostedLog = new RecordingLogger<AgentTaskLandHostedService>(entries);
        h.AddHostedLandService();
        await h.InitializeAsync();
        ArmThrow(h, seam, type);
        var expected = h.Git.SourceHead;
        await h.RequestAsync(expectedSourceSha: expected);
        var hosted = new AgentTaskLandHostedService(h.Queue, h.Services.GetRequiredService<IServiceScopeFactory>(), hostedLog);
        await hosted.StartAsync(CancellationToken.None);
        try { await WaitTerminalAsync(h); }
        finally { await hosted.StopAsync(CancellationToken.None); }
        var code = type switch
        {
            "io" => "landing_io_error",
            "timeout" => "landing_timeout",
            "unauthorized" => "landing_access_denied",
            "concurrency" => "landing_concurrency_conflict",
            "persistence" => "landing_persistence_failed",
            _ => "landing_unexpected_exception",
        };
        var exceptionType = type switch
        {
            "io" => "IOException",
            "timeout" => "TimeoutException",
            "unauthorized" => "UnauthorizedAccessException",
            "concurrency" => "DbUpdateConcurrencyException",
            "persistence" => "DbUpdateException",
            "generic" => "InvalidOperationException",
            _ => "OperationCanceledException",
        };
        await using var db = h.CreateContext();
        var request = await db.AgentTaskLandRequests.SingleAsync(r => r.TaskId == h.Git.TaskId);
        request.TerminalFailureCode.ShouldBe(code);
        request.FailureExceptionType.ShouldBe(exceptionType);
        request.FailureDiagnosticId.ShouldNotBeNull();
        request.SourceRefusalReason.ShouldBeNull();
        request.State.ShouldBe(LandRequestState.Completed);
        request.IsPending.ShouldBeFalse();
        (await db.AgentTasks.SingleAsync(t => t.Id == h.Git.TaskId)).LandRequestedAt.ShouldBeNull();
        var terminal = await db.AgentTaskEvents.SingleAsync(e => e.LandRequestId == request.Id && e.Type == AgentTaskEventType.LandRefused);
        terminal.Detail.ShouldStartWith($"land unconfirmed: {code}; diagnostic={request.FailureDiagnosticId:N}; exception={exceptionType};");
        if (seam == "before-operation")
        {
            terminal.Detail.ShouldContain("local=null");
            (await db.AgentTaskLandings.CountAsync(o => o.TaskId == h.Git.TaskId)).ShouldBe(0);
        }
        else
        {
            request.SourceResolutionState.ShouldBe(LandSourceResolutionState.Observed);
            terminal.Detail.ShouldContain("local=" + h.Git.SourceHead);
        }
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == h.Git.TaskId && e.Type == AgentTaskEventType.Warning)).ShouldBe(1);
        var outcome = await db.AgentTaskLandNotifications.SingleAsync(n => n.RequestId == request.Id && n.Kind == LandNotificationKind.Outcome);
        outcome.State.ShouldBe(LandNotificationState.NotRequired);
        outcome.Body.ShouldContain($"{code}; diagnostic={request.FailureDiagnosticId:N}; exception={exceptionType}");
        var handled = entries.First(e => e.State.ContainsKey("DiagnosticId"));
        handled.State["TaskId"].ShouldBe(h.Git.TaskId);
        handled.State["RequestId"].ShouldBe(request.Id);
        handled.State["Attempt"].ShouldBe(1);
        handled.State["ExceptionType"].ShouldBe(exceptionType);
        handled.State["Code"].ShouldBe(code);
        handled.State["DiagnosticId"].ShouldBe(request.FailureDiagnosticId);
        AssertNoMarker(request, terminal.Detail, outcome.Body, handled.Message);
        request.FailureExceptionType!.Length.ShouldBeLessThanOrEqualTo(200);
        request.TerminalFailureCode!.Length.ShouldBeLessThanOrEqualTo(100);
    }

    [Test]
    [Arguments("landed")]
    [Arguments("already-present")]
    public async Task C498_FailureAfterPublicationRetainsPublication(string kind)
    {
        await using var h = new LandingProtocolHarness();
        h.AddHostedLandService();
        await h.InitializeAsync();
        if (kind == "already-present") h.Git.SetRemoteContainsSource();
        else await h.AddSourceAsync();
        h.Fault.AfterAcknowledged = phase =>
        {
            if (phase == LandPhase.PublicationConfirmed) throw new IOException(Marker);
            return Task.CompletedTask;
        };
        await h.RequestAsync(expectedSourceSha: h.Git.SourceHead);
        var hosted = new AgentTaskLandHostedService(h.Queue, h.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentTaskLandHostedService>.Instance);
        await hosted.StartAsync(CancellationToken.None);
        try { await WaitTerminalAsync(h); }
        finally { await hosted.StopAsync(CancellationToken.None); }
        await using var db = h.CreateContext();
        var op = await db.AgentTaskLandings.SingleAsync(o => o.TaskId == h.Git.TaskId);
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == h.Git.TaskId && e.Type == AgentTaskEventType.LandRefused)).ShouldBe(0);
        op.Publication.ShouldBe(kind == "already-present" ? LandPublicationOutcome.AlreadyPresent : LandPublicationOutcome.Landed);
        op.LastReason.ShouldBe("landing_interrupted_after_publication");
        op.RemoteConfirmedAt.ShouldNotBeNull();
        op.ObservedRemoteTargetSha.ShouldNotBeNull();
        h.Git.RemoteTarget.ShouldBe(kind == "already-present" ? h.Git.SeedSha : op.VerifiedSourceSha);
        var request = await db.AgentTaskLandRequests.SingleAsync(r => r.TaskId == h.Git.TaskId);
        request.TerminalFailureCode.ShouldBe("landing_interrupted_after_publication");
        request.FailureExceptionType.ShouldBe("IOException");
        request.FailureDiagnosticId.ShouldNotBeNull();
        Directory.Exists(h.Git.Source).ShouldBeTrue();
        var outcome = await db.AgentTaskLandNotifications.SingleAsync(n => n.RequestId == request.Id && n.Kind == LandNotificationKind.Outcome);
        outcome.Body.ShouldContain("publication=" + op.Publication);
        h.Git.Trace.ShouldNotContain(a => a.Contains("remove"));
    }

    [Test]
    [Arguments("timeout")]
    [Arguments("io")]
    [Arguments("generic")]
    public async Task C498_DirectFailAsyncGetsEquivalentEvidence(string type)
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.RequestAsync(expectedSourceSha: h.Git.SourceHead);
        Exception ex = type switch
        {
            "timeout" => new TimeoutException(Marker),
            "io" => new IOException(Marker),
            _ => new InvalidOperationException(Marker),
        };
        await h.FailAsync(ex);
        await using var db = h.CreateContext();
        var request = await db.AgentTaskLandRequests.SingleAsync(r => r.TaskId == h.Git.TaskId);
        request.TerminalFailureCode.ShouldBe(type == "timeout" ? "landing_timeout" : type == "io" ? "landing_io_error" : "landing_unexpected_exception");
        request.FailureExceptionType.ShouldBe(ex.GetType().Name);
        request.FailureDiagnosticId.ShouldNotBeNull();
        var terminal = await db.AgentTaskEvents.SingleAsync(e => e.Type == AgentTaskEventType.LandRefused);
        terminal.Detail.ShouldNotContain("synthetic-secret-marker");
        (await db.AgentTaskLandNotifications.CountAsync(n => n.Kind == LandNotificationKind.Outcome)).ShouldBe(1);
    }

    [Test]
    public async Task C498_RepeatedFailureCallbackIsIdempotent()
    {
        await using var h = new LandingProtocolHarness();
        h.AddHostedLandService();
        await h.InitializeAsync();
        h.Git.BeforeInspection = () => throw new IOException(Marker);
        await h.RequestAsync(expectedSourceSha: h.Git.SourceHead);
        var hosted = new AgentTaskLandHostedService(h.Queue, h.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentTaskLandHostedService>.Instance);
        await hosted.StartAsync(CancellationToken.None);
        try { await WaitTerminalAsync(h); }
        finally { await hosted.StopAsync(CancellationToken.None); }
        await using var first = h.CreateContext();
        var request = await first.AgentTaskLandRequests.SingleAsync(r => r.TaskId == h.Git.TaskId);
        var id = request.FailureDiagnosticId;
        var body = (await first.AgentTaskLandNotifications.SingleAsync(n => n.RequestId == request.Id && n.Kind == LandNotificationKind.Outcome)).Body;
        var digest = (await first.AgentTaskLandNotifications.SingleAsync(n => n.RequestId == request.Id && n.Kind == LandNotificationKind.Outcome)).ContentDigest;
        await h.FailAsync(new InvalidOperationException("second"));
        await using var db = h.CreateContext();
        var stored = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == request.Id);
        stored.FailureDiagnosticId.ShouldBe(id);
        stored.FailureExceptionType.ShouldBe("IOException");
        (await db.AgentTaskEvents.CountAsync(e => e.LandRequestId == request.Id && e.IsLandTerminal)).ShouldBe(1);
        var note = await db.AgentTaskLandNotifications.SingleAsync(n => n.RequestId == request.Id && n.Kind == LandNotificationKind.Outcome);
        note.Body.ShouldBe(body);
        note.ContentDigest.ShouldBe(digest);
    }

    [Test]
    [Arguments("before-save")]
    [Arguments("after-save")]
    [Arguments("commit")]
    [Arguments("after-commit")]
    public async Task C498_TerminalTransactionFaultMatrix(string cut)
    {
        var entries = new List<RecordingLogEntry>();
        await using var h = new LandingProtocolHarness();
        h.Logger = new RecordingLogger<AgentTaskLandService>(entries);
        var hostedLog = new RecordingLogger<AgentTaskLandHostedService>(entries);
        h.AddHostedLandService();
        await h.InitializeAsync();
        h.Fault.TerminalCut = cut;
        h.Fault.EventKind = AgentTaskEventType.LandRefused;
        h.Git.BeforeInspection = () => throw new IOException(Marker);
        await h.RequestAsync(expectedSourceSha: h.Git.SourceHead);
        var hosted = new AgentTaskLandHostedService(h.Queue, h.Services.GetRequiredService<IServiceScopeFactory>(), hostedLog);
        await hosted.StartAsync(CancellationToken.None);
        try { await Task.Delay(TimeSpan.FromSeconds(2)); }
        finally { await hosted.StopAsync(CancellationToken.None); }
        await using var db = h.CreateContext();
        var request = await db.AgentTaskLandRequests.SingleAsync(r => r.TaskId == h.Git.TaskId);
        if (cut is "before-save" or "after-save" or "commit")
        {
            request.IsPending.ShouldBeTrue();
            request.TerminalEventId.ShouldBeNull();
            request.TerminalFailureCode.ShouldBeNull();
            request.FailureDiagnosticId.ShouldBeNull();
            (await db.AgentTasks.SingleAsync(t => t.Id == h.Git.TaskId)).LandRequestedAt.ShouldNotBeNull();
            (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == h.Git.TaskId && e.IsLandTerminal)).ShouldBe(0);
            (await db.AgentTaskLandNotifications.CountAsync(n => n.TaskId == h.Git.TaskId && n.Kind == LandNotificationKind.Outcome)).ShouldBe(0);
            var persist = entries.Single(e => e.Message.Contains("Could not persist land failure"));
            persist.State["PersistenceErrorType"]?.ToString().ShouldBe("InjectedSaveFailure");
            var handled = entries.Single(e => e.State.ContainsKey("DiagnosticId") && e.Message.Contains("Land operation failed"));
            persist.State["DiagnosticId"].ShouldBe(handled.State["DiagnosticId"]);
            h.Fault.TerminalCut = null;
            h.Git.BeforeInspection = null;
            await h.RestartServicesAsync();
            await h.SweepAsync();
            (await h.RunQueuedAsync()).ShouldBe(LandRunResult.Complete);
            await using var landed = h.CreateContext();
            (await landed.AgentTaskLandRequests.SingleAsync(r => r.TaskId == h.Git.TaskId)).TerminalFailureCode.ShouldBeNull();
        }
        else
        {
            request.FailureDiagnosticId.ShouldNotBeNull();
            (await db.AgentTaskEvents.CountAsync(e => e.LandRequestId == request.Id && e.IsLandTerminal)).ShouldBe(1);
            (await db.AgentTaskLandNotifications.CountAsync(n => n.RequestId == request.Id && n.Kind == LandNotificationKind.Outcome)).ShouldBe(1);
            var first = request.FailureDiagnosticId;
            await h.RestartServicesAsync();
            await h.FailAsync(new IOException("retry"));
            await using var again = h.CreateContext();
            var stored = await again.AgentTaskLandRequests.SingleAsync(r => r.Id == request.Id);
            stored.FailureDiagnosticId.ShouldBe(first);
            (await again.AgentTaskEvents.CountAsync(e => e.LandRequestId == request.Id && e.IsLandTerminal)).ShouldBe(1);
        }
    }

    [Test]
    [Arguments("replaced")]
    [Arguments("attempt")]
    public async Task C498_ReplacementBeforeFailureLock(string change)
    {
        var entries = new List<RecordingLogEntry>();
        await using var h = new LandingProtocolHarness();
        h.Logger = new RecordingLogger<AgentTaskLandService>(entries);
        var hostedLog = new RecordingLogger<AgentTaskLandHostedService>(entries);
        h.AddHostedLandService();
        await h.InitializeAsync();
        var queued = await h.RequestAsync(expectedSourceSha: h.Git.SourceHead);
        var armed = 0;
        h.Git.OnSourceObservation = n =>
        {
            if (n != 1) return Task.CompletedTask;
            h.Fault.OnTransactionStarted = async _ =>
            {
                if (Interlocked.Increment(ref armed) != 1) return;
                await using var mut = h.CreateContext();
                var request = await mut.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId);
                var task = await mut.AgentTasks.SingleAsync(t => t.Id == h.Git.TaskId);
                if (change == "replaced")
                {
                    request.IsPending = false;
                    request.State = LandRequestState.Canceled;
                    var replacement = new AgentTaskLandRequest
                    {
                        Id = Guid.NewGuid(), TaskId = task.Id, RequestedAt = DateTime.UtcNow, LastEvaluatedAt = DateTime.UtcNow,
                        LastProgressAt = DateTime.UtcNow, State = LandRequestState.Queued, SchemaVersion = 2,
                        ExpectedSourceSha = h.Git.SourceHead, ApprovalKind = LandApprovalKind.ExplicitCaller,
                    };
                    mut.AgentTaskLandRequests.Add(replacement);
                    task.CurrentLandRequestId = replacement.Id;
                }
                else
                {
                    request.Attempt++;
                    task.LandAttempt++;
                }
                request.ConcurrencyToken = Guid.NewGuid();
                task.ConcurrencyToken = Guid.NewGuid();
                await mut.SaveChangesAsync();
            };
            throw new IOException(Marker);
        };
        var hosted = new AgentTaskLandHostedService(h.Queue, h.Services.GetRequiredService<IServiceScopeFactory>(), hostedLog);
        await hosted.StartAsync(CancellationToken.None);
        try { await Task.Delay(TimeSpan.FromSeconds(2)); }
        finally { await hosted.StopAsync(CancellationToken.None); }
        await using var db = h.CreateContext();
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == h.Git.TaskId && e.IsLandTerminal)).ShouldBe(0);
        (await db.AgentTaskLandRequests.CountAsync(r => r.TaskId == h.Git.TaskId && r.TerminalFailureCode != null)).ShouldBe(0);
        if (change == "replaced")
            (await db.AgentTaskLandRequests.CountAsync(r => r.TaskId == h.Git.TaskId && r.IsPending)).ShouldBe(1);
        entries.ShouldContain(e => Equals(e.State.GetValueOrDefault("Attempt"), 1) || Equals(e.State.GetValueOrDefault("Attempt"), 1L) || Equals(e.State.GetValueOrDefault("Attempt"), 1));
    }

    [Test]
    [Arguments("status_error")]
    [Arguments("identity_io_error")]
    [Arguments("identity_inaccessible")]
    [Arguments("source_dirty")]
    [Arguments("operation-inspection")]
    [Arguments("commit_lookup_failed")]
    public async Task C498_InspectionDiagnosticPropagatesToRequest(string arm)
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        if (arm == "source_dirty")
            File.WriteAllText(Path.Combine(h.Git.Source, "keep.txt"), "dirty\n");
        else if (arm == "commit_lookup_failed")
        {
            h.Git.BeforeCommand = (_, args) => Task.FromResult<LandingGitResult?>(
                args[0] == "rev-parse" && args.Any(a => a.Contains("^{commit}", StringComparison.Ordinal))
                    ? new LandingGitResult(128, "", Marker) : null);
        }
        else
        {
            h.Git.InjectInspection = call =>
            {
                var match = arm == "operation-inspection" ? call == 2 : call == 1;
                if (!match) return null;
                var diagnostic = arm switch
                {
                    "status_error" => new LandInspectionDiagnostic("git status --porcelain=v1", 128, "git_exit_128", null),
                    "identity_io_error" => new LandInspectionDiagnostic("git rev-parse <identity>", 128, "git_exit_128", "LandingGitCommandException"),
                    "identity_inaccessible" => new LandInspectionDiagnostic(null, null, null, "UnauthorizedAccessException"),
                    _ => new LandInspectionDiagnostic("git status --porcelain=v1", 1, "git_exit_1", null),
                };
                var reason = arm == "operation-inspection" ? "status_error" : arm;
                return new LandSourceInspection(null, reason, diagnostic);
            };
        }
        var queued = await h.RequestAsync(expectedSourceSha: h.Git.SourceHead);
        (await h.RunQueuedAsync()).ShouldBe(LandRunResult.Complete);
        await using var db = h.CreateContext();
        var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId);
        request.TerminalFailureCode.ShouldBeNull();
        request.FailureDiagnosticId.ShouldBeNull();
        if (arm == "source_dirty")
        {
            request.SourceRefusalReason.ShouldBe("source_dirty");
            request.SourceDiagnosticCode.ShouldBeNull();
        }
        else if (arm == "commit_lookup_failed")
        {
            request.SourceRefusalReason.ShouldBe("commit_lookup_failed");
            request.SourceDiagnosticCommand.ShouldBe("git rev-parse <identity>");
            request.SourceDiagnosticExitCode.ShouldBe(128);
            request.SourceDiagnosticCode.ShouldBe("git_exit_128");
        }
        else
        {
            request.SourceRefusalReason.ShouldBe(arm == "operation-inspection" ? "status_error" : arm);
            if (arm == "status_error" || arm == "operation-inspection")
                request.SourceDiagnosticCode.ShouldBe(arm == "status_error" ? "git_exit_128" : "git_exit_1");
            if (arm == "identity_io_error") request.SourceDiagnosticCode.ShouldBe("git_exit_128");
        }
        var terminal = await db.AgentTaskEvents.SingleAsync(e => e.LandRequestId == request.Id && e.Type == AgentTaskEventType.LandRefused);
        var outcome = await db.AgentTaskLandNotifications.SingleAsync(n => n.RequestId == request.Id && n.Kind == LandNotificationKind.Outcome);
        if (request.SourceDiagnosticCode is not null)
        {
            terminal.Detail.ShouldContain("command=");
            terminal.Detail.ShouldContain("diagnostic=" + request.SourceDiagnosticCode);
            outcome.Body.ShouldContain("command=");
        }
        terminal.Detail.ShouldNotContain("synthetic-secret-marker");
        outcome.Body.ShouldNotContain("synthetic-secret-marker");
    }

    private static void ArmThrow(LandingProtocolHarness h, string seam, string type)
    {
        Exception Ex() => type switch
        {
            "io" => new IOException(Marker),
            "timeout" => new TimeoutException(Marker),
            "unauthorized" => new UnauthorizedAccessException(Marker),
            "concurrency" => new DbUpdateConcurrencyException(Marker),
            "persistence" => new DbUpdateException(Marker, (Exception?)null),
            "generic" => new InvalidOperationException(Marker),
            _ => new OperationCanceledException(new CancellationToken(true)),
        };
        if (seam == "before-operation")
            h.Git.BeforeInspection = () => throw Ex();
        else
            h.Git.OnSourceObservation = n => n == 2 ? throw Ex() : Task.CompletedTask;
    }

    private static async Task WaitTerminalAsync(LandingProtocolHarness h)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            await using var db = h.CreateContext();
            if (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == h.Git.TaskId && e.IsLandTerminal))
                return;
            await Task.Delay(50);
        }
        throw new TimeoutException("terminal event");
    }

    private static void AssertNoMarker(AgentTaskLandRequest request, string detail, string body, string log)
    {
        foreach (var value in new[] { request.TerminalFailureCode, request.FailureExceptionType, detail, body, log,
                     request.SourceDiagnosticCommand, request.SourceDiagnosticCode, request.SourceDiagnosticExceptionType })
        {
            if (value is null) continue;
            value.ShouldNotContain("synthetic-secret-marker");
            value.ShouldNotContain("\u001b");
        }
        // Outcome Body is the existing multi-line payload; bounded columns and event detail are single-line.
        foreach (var value in new[] { request.TerminalFailureCode, request.FailureExceptionType, detail, log,
                     request.SourceDiagnosticCommand, request.SourceDiagnosticCode, request.SourceDiagnosticExceptionType })
        {
            if (value is null) continue;
            value.ShouldNotContain("\n");
        }
    }
}
