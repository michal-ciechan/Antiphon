using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class DelegateScriptLandStatusTests
{
    [Test]
    [Arguments("not-requested")]
    [Arguments("held")]
    [Arguments("running")]
    [Arguments("residue-held")]
    [Arguments("landed")]
    [Arguments("already-present")]
    [Arguments("refused")]
    [Arguments("queued")]
    [Arguments("confirmed")]
    [Arguments("legacy")]
    [Arguments("none")]
    public async Task C467_V18_StatusAndAcceptance(string scenario)
    {
        var id = Guid.NewGuid(); var holder = Guid.NewGuid(); var caller = Guid.NewGuid(); var queue = Guid.NewGuid();
        var state = scenario is "held" or "residue-held" ? "Held" : scenario == "running" ? "Running" : "Completed";
        var publication = scenario is "landed" or "residue-held" or "confirmed" ? "Landed" : scenario == "already-present" ? "AlreadyPresent" : "Unconfirmed";
        var receipt = scenario == "confirmed" ? "Confirmed" : scenario == "none" ? "NotRequired" : "AwaitingReceipt";
        var body = JsonSerializer.Serialize(new {
            summary = new { status = "Succeeded", title = "owned status", kind = "Worker", role = "Code", modelLevel = "High" },
            result = "EXACT REPORT AFTER FACTS",
            landRequest = scenario is "not-requested" or "legacy" ? null : new {
                id, state, requestedAt = "2026-09-09T00:00:00Z", attempt = 0, noProgressSeconds = 901,
                holdReasonCode = state == "Held" ? "repository_or_source_writer" : null,
                holdingTaskId = holder, holdingTaskStatus = "Blocked", holdDetail = "owned hold",
                notifications = new[] { new { kind = "Outcome", state = receipt, destinationSessionId = caller,
                    queueMessageId = queue, confirmedAt = scenario == "confirmed" ? "2026-09-09T01:00:00Z" : null,
                    lastErrorCode = scenario == "queued" ? "destination_unavailable" : null } } },
            landing = new { publication, operationId = id, verifiedSha = "verified-source", remoteSha = "observed-remote",
                remoteConfirmedAt = publication == "Unconfirmed" ? null : "2026-09-09T00:10:00Z", cleanup = scenario == "residue-held" ? "Refused" : "Complete" },
            legacyLandReceipt = scenario == "legacy" ? new { state = "LegacyUnverified", eventId = id } : null });
        await using var server = LandApiStub.Compatible(
            new string('e', 40),
            v2Body: JsonSerializer.Serialize(new { requestId = id, status = "queued", notification = scenario == "none" ? "not-required" : "tracked" }),
            taskStatusBody: body);
        var status = await DelegateScriptRunner.RunAsync(server.Url, "-Status", id.ToString());
        status.ExitCode.ShouldBe(0, status.Output); status.Output.ShouldContain("Delegate: Succeeded");
        status.Output.ShouldContain("Publication: " + publication);
        status.Output.IndexOf("Publication:", StringComparison.Ordinal).ShouldBeLessThan(status.Output.IndexOf("EXACT REPORT AFTER FACTS", StringComparison.Ordinal));
        if (scenario is "not-requested" or "legacy") status.Output.ShouldContain("Land: Not requested");
        else { status.Output.ShouldContain("Land: " + state); status.Output.ShouldContain("attempt 0"); status.Output.ShouldContain("Notification: Outcome " + receipt); }
        if (state == "Held") { status.Output.ShouldContain(holder.ToString()); status.Output.ShouldContain("(Blocked)"); }
        if (scenario == "legacy") status.Output.ShouldContain("LegacyUnverified");
        var acceptance = await DelegateScriptRunner.RunAsync(server.Url, "-Land", id.ToString());
        acceptance.ExitCode.ShouldBe(0, acceptance.Output); acceptance.Output.ShouldContain(id.ToString());
        acceptance.Output.ShouldContain("Publication pending"); acceptance.Output.ShouldNotContain("receipt confirmed");
        if (scenario == "none") acceptance.Output.ShouldContain("notification=not-required");
    }

    [Test]
    [Arguments("exception-only")]
    [Arguments("source-only")]
    [Arguments("combined")]
    [Arguments("operation-reason")]
    [Arguments("residue-with-reason")]
    [Arguments("legacy-fields-absent")]
    public async Task C498_StatusPrintsDiagnosticLines(string scenario)
    {
        var id = Guid.NewGuid();
        var diagnostic = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var sha = new string('a', 40);
        object? landRequest = scenario == "legacy-fields-absent"
            ? new { id, state = "Completed", requestedAt = "2026-09-13T00:00:00Z", attempt = 1, noProgressSeconds = 1, notifications = Array.Empty<object>() }
            : scenario == "residue-with-reason"
            ? new { id, state = "Completed", requestedAt = "2026-09-13T00:00:00Z", attempt = 1, noProgressSeconds = 1, notifications = Array.Empty<object>() }
            : new
            {
                id, state = "Completed", requestedAt = "2026-09-13T00:00:00Z", attempt = 1, noProgressSeconds = 1,
                candidateSourceSha = scenario is "combined" ? sha : null,
                sourceRefusalReason = scenario is "source-only" or "combined" ? "status_error" : null,
                terminalFailureCode = scenario is "exception-only" or "combined" or "operation-reason" ? "landing_io_error" : null,
                failureDiagnosticId = scenario is "exception-only" or "combined" or "operation-reason" ? diagnostic : (Guid?)null,
                failureExceptionType = scenario is "exception-only" or "combined" or "operation-reason" ? "IOException" : null,
                sourceDiagnosticCommand = scenario is "source-only" or "combined" ? "git status --porcelain=v1" : null,
                sourceDiagnosticExitCode = scenario is "source-only" or "combined" ? 128 : (int?)null,
                sourceDiagnosticCode = scenario is "source-only" or "combined" ? "git_exit_128" : null,
                sourceDiagnosticExceptionType = scenario == "source-only" ? "LandingGitCommandException" : null,
                notifications = Array.Empty<object>(),
            };
        object? landing = scenario == "legacy-fields-absent" ? null
            : scenario == "residue-with-reason" ? new { publication = "Landed", operationId = id, cleanup = "Refused", reason = "cleanup_refused", verifiedSha = sha, remoteSha = sha, remoteConfirmedAt = "2026-09-13T00:10:00Z" }
            : scenario == "operation-reason" ? new { publication = "Unconfirmed", operationId = id, cleanup = "NotStarted", reason = "target_changed", verifiedSha = (string?)null, remoteSha = (string?)null, remoteConfirmedAt = (string?)null }
            : new { publication = "Unconfirmed", operationId = id, cleanup = "NotStarted", reason = (string?)null, verifiedSha = (string?)null, remoteSha = (string?)null, remoteConfirmedAt = (string?)null };
        var body = JsonSerializer.Serialize(new
        {
            summary = new { status = "Succeeded", title = "owned status", kind = "Worker", role = "Code", modelLevel = "High" },
            result = "EXACT REPORT AFTER FACTS",
            landRequest,
            landing,
        });
        await using var server = LandApiStub.Compatible(
            new string('e', 40),
            v2Body: JsonSerializer.Serialize(new { requestId = id, status = "queued" }),
            taskStatusBody: body);
        var status = await DelegateScriptRunner.RunAsync(server.Url, "-Status", id.ToString());
        status.ExitCode.ShouldBe(0, status.Output);
        status.Output.IndexOf("EXACT REPORT AFTER FACTS", StringComparison.Ordinal).ShouldBeGreaterThan(0);
        if (scenario == "exception-only")
        {
            status.Output.ShouldContain("Land execution failure: landing_io_error; diagnostic " + diagnostic + "; exception IOException");
            status.Output.ShouldNotContain("Source inspection");
        }
        if (scenario == "source-only")
        {
            status.Output.ShouldContain("Source refusal: status_error");
            status.Output.ShouldContain("Source inspection: git status");
            status.Output.ShouldContain("Source inspection exception: ");
            status.Output.ShouldNotContain("Land execution failure");
        }
        if (scenario == "combined")
        {
            status.Output.ShouldContain("Candidate source: ");
            status.Output.ShouldContain("Source refusal: ");
            status.Output.ShouldContain("Land execution failure: ");
            status.Output.ShouldContain("Source inspection: ");
        }
        if (scenario == "operation-reason")
        {
            status.Output.ShouldContain("Land execution failure: ");
            status.Output.ShouldContain("Landing reason: target_changed");
        }
        if (scenario == "residue-with-reason")
        {
            status.Output.ShouldContain("Landing reason: cleanup_refused");
            status.Output.ShouldNotContain("Land execution failure");
        }
        if (scenario == "legacy-fields-absent")
        {
            status.Output.ShouldNotContain("Land execution failure");
            status.Output.ShouldNotContain("Source inspection");
            status.Output.ShouldNotContain("Candidate source:");
            status.Output.ShouldNotContain("Source refusal:");
            status.Output.ShouldNotContain("Landing reason:");
            status.Output.ShouldContain("Land:");
            status.Output.ShouldContain("Publication: Unconfirmed");
        }
    }
}
