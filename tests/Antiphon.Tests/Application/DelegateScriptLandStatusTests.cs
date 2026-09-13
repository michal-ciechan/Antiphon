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
    [Arguments("observed")]
    [Arguments("unavailable")]
    public async Task C499_V29b_StatusPrintsProgressEvidence(string kind)
    {
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var commit = new string('a', 40);
        object progress = kind == "observed"
            ? new { assessment = "ProgressObserved", sources = new[] { new { origin = "RepairSource", ownerTaskId = owner, commit } } }
            : new { assessment = "Indeterminate", reason = "source_remote_unreadable" };
        var body = JsonSerializer.Serialize(new {
            summary = new { status = "Succeeded", title = "repair", kind = "Worker", role = "Code", modelLevel = "High" },
            result = "EXACT REPORT AFTER FACTS",
            progressEvidence = progress,
        });
        await using var server = LandApiStub.Compatible(commit, taskStatusBody: body);
        var status = await DelegateScriptRunner.RunAsync(server.Url, "-Status", id.ToString());
        status.ExitCode.ShouldBe(0, status.Output);
        if (kind == "observed")
            status.Output.ShouldContain($"Progress: repair-source; owner {owner}; commit {commit}");
        else
            status.Output.ShouldContain("Progress: unavailable; reason source_remote_unreadable");
        status.Output.IndexOf("Progress:", StringComparison.Ordinal)
            .ShouldBeLessThan(status.Output.IndexOf("EXACT REPORT AFTER FACTS", StringComparison.Ordinal));
    }
}
