using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class WorktreeCleanupPresentationTests
{
    [Test]
    [Arguments(32, true)] [Arguments(33, true)] [Arguments(5, false)]
    public void C443_BoundedCapturePreservesQualifyingTail(int code, bool sharing)
    {
        var capture = Capture(code);
        var presentation = new WorktreeCleanupPresentation();
        var json = presentation.Serialize(capture);
        var committed = JsonSerializer.Deserialize<WorktreeCleanupCapture>(json)!;
        Encoding.UTF8.GetByteCount(json).ShouldBeLessThanOrEqualTo(32768);
        committed.Native.Observations.Count.ShouldBeLessThan(capture.Native.Observations.Count);
        committed.Omitted.ShouldBe(capture.Native.Observations.Count - committed.Native.Observations.Count);
        committed.Native.HasSharingConflict.ShouldBe(sharing);
        if (sharing)
        {
            committed.Native.Observations.ShouldContain(capture.Native.Observations[^1]);
            presentation.Summary(committed).ShouldContain($"DeleteAccessOpen={code}");
        }
    }

    [Test]
    [Arguments(false, 0)] [Arguments(true, 0)]
    [Arguments(false, 1)] [Arguments(true, 1)]
    [Arguments(false, 2)] [Arguments(true, 2)]
    public void C443_DiagnosticEnvelopeFitsFallbackUtf8(bool differentShas, int siblings)
    {
        var request = new AgentTaskLandRequest { Id = Guid.NewGuid(), TaskId = Guid.NewGuid(),
            ExpectedSourceSha = new('a', 40), LocalBeforeSha = new(differentShas ? 'b' : 'a', 40),
            RemoteSourceSha = new(differentShas ? 'c' : 'a', 40), CandidateSourceSha = new(differentShas ? 'd' : 'a', 40) };
        var marker = siblings == 0 ? null : "unlanded-sibling=" + string.Join(",", Enumerable.Range(1, siblings)
            .Select(i => $"{i:D8}:feat/\u754c-plan-{i}"));
        var narrative = "full publication narrative" + (marker is null ? "" : ", " + marker);
        var source = new AgentTaskEvent { Id = Guid.NewGuid(), Type = AgentTaskEventType.LandedWithResidue,
            LandingPublication = LandPublicationOutcome.Landed, LandingCleanup = LandCleanupStatus.Refused,
            Detail = narrative };
        var capture = Capture(32) with { Handles = new(WorktreeLockStatus.OwnersObserved, "Observed", DateTime.UtcNow,
            [new(new string('\u754c', 80), 4321, new string('\u754c', 512))]) };
        var presentation = new WorktreeCleanupPresentation();
        var reference = new WorktreeCleanupReference(capture.Id, capture.RequestId, capture.OperationId, capture.At,
            WorktreeCleanupCaptureState.Captured, presentation.Summary(capture), presentation.Serialize(capture));
        var note = LandNotificationPayload.Create(request, source, LandNotificationKind.Outcome,
            reference, Enumerable.Range(1, siblings).Select(i => $"{i:D8}:feat/\u754c-plan-{i}").ToArray(), "worktree_remove_failed");
        Encoding.UTF8.GetByteCount(note.Body).ShouldBeLessThanOrEqualTo(1024);
        note.Body.ShouldContain(capture.Id.ToString("N")); note.Body.ShouldContain(new string('\u754c', 80));
        note.Body.ShouldContain("PID=4321"); note.Body.ShouldContain("DeleteAccessOpen=32");
        note.Body.ShouldContain("worktree_remove_failed"); note.Body.ShouldEndWith("~");
        if (marker is not null) note.Body.ShouldContain(marker);
        else note.Body.ShouldNotContain("unlanded-sibling=");
        source.Detail.ShouldBe(narrative);
    }

    [Test]
    [Arguments(false, 4, 189, false, true)] [Arguments(true, 4, 189, false, true)]
    [Arguments(false, 64, 189, false, true)] [Arguments(true, 64, 189, false, true)]
    [Arguments(false, 128, 10, false, false)] [Arguments(true, 128, 10, false, false)]
    [Arguments(false, 1, 230, false, true)] [Arguments(true, 1, 230, false, true)]
    [Arguments(false, 1, 14, true, true)] [Arguments(true, 1, 14, true, true)]
    [Arguments(false, 0, 0, true, true)] [Arguments(true, 0, 0, true, true)]
    [Arguments(false, 32, 189, true, true)] [Arguments(true, 32, 189, true, true)]
    [Arguments(false, 64, 189, true, false)] [Arguments(true, 64, 189, true, false)]
    [Arguments(true, 4, 189, false, true, 64)] [Arguments(true, 128, 10, false, false, 64)]
    [Arguments(true, 32, 189, true, true, 64)] [Arguments(true, 1, 14, true, true, 64)]
    public void C443_D9_DiagnosticsSurviveEitherEnvelopeExtreme(bool differentShas, int siblings,
        int branchLength, bool verbose, bool owners, int shaLength = 40)
    {
        var request = new AgentTaskLandRequest { Id = Guid.NewGuid(), TaskId = Guid.NewGuid(),
            ExpectedSourceSha = new('a', shaLength), LocalBeforeSha = new(differentShas ? 'b' : 'a', shaLength),
            RemoteSourceSha = new(differentShas ? 'c' : 'a', shaLength), CandidateSourceSha = new(differentShas ? 'd' : 'a', shaLength) };
        var tokens = Enumerable.Range(1, siblings)
            .Select(i => $"{i:D8}:feat/{i:D3}-" + new string('s', branchLength - 9)).ToArray();
        var marker = siblings == 0 ? null : "unlanded-sibling=" + string.Join(",", tokens);
        var source = new AgentTaskEvent { Id = Guid.NewGuid(), Type = AgentTaskEventType.LandedWithResidue,
            LandingPublication = LandPublicationOutcome.Landed, LandingCleanup = LandCleanupStatus.Refused,
            Detail = "full publication narrative; " + marker };
        var capture = Capture(32) with { Handles = new(owners ? WorktreeLockStatus.OwnersObserved : WorktreeLockStatus.Unavailable,
            owners ? "Observed" : "InsufficientPrivileges", DateTime.UtcNow,
            owners ? [new(verbose ? new string('\u754c', 80) : "dotnet", 4321, verbose ? new string('\u754c', 512) : ".")] : []) };
        var presentation = new WorktreeCleanupPresentation();
        var reference = new WorktreeCleanupReference(capture.Id, capture.RequestId, capture.OperationId, capture.At,
            WorktreeCleanupCaptureState.Captured, presentation.Summary(capture), presentation.Serialize(capture));
        var note = LandNotificationPayload.Create(request, source, LandNotificationKind.Outcome,
            reference, tokens, verbose ? new string('\u754c', 400) : "cleanup failed");

        Encoding.UTF8.GetByteCount(note.Body).ShouldBeLessThanOrEqualTo(1024);
        note.Body.ShouldContain($"expected={request.ExpectedSourceSha}");
        note.Body.ShouldContain($"local={(differentShas ? request.LocalBeforeSha : "expected")}");
        note.Body.ShouldContain($"remote={(differentShas ? request.RemoteSourceSha : "expected")}");
        note.Body.ShouldContain($"candidate={(differentShas ? request.CandidateSourceSha : "expected")}");
        note.Body.ShouldContain($"capture={capture.Id:N}");
        note.Body.ShouldContain(capture.At.ToString("O"));
        note.Body.ShouldContain("Git git_exit_128 exit=128");
        note.Body.ShouldContain("DeleteAccessOpen=32");
        if (owners)
        {
            note.Body.ShouldContain(verbose ? new string('\u754c', 80) : "dotnet");
            note.Body.ShouldContain("PID=4321");
        }
        else note.Body.ShouldContain("InsufficientPrivileges");
        if (siblings > 0)
        {
            var siblingLine = note.Body.Split('\n').Single(line => line.StartsWith("unlanded-sibling="));
            if (siblingLine.Contains(" siblings, showing first "))
            {
                siblingLine.ShouldStartWith($"unlanded-sibling={siblings} siblings, showing first ");
                var countAndNames = siblingLine.Split("showing first ")[1].Split(": ", 2);
                var shown = int.Parse(countAndNames[0]);
                shown.ShouldBeLessThan(siblings);
                if (shown > 0) countAndNames[1].ShouldBe(string.Join(",", tokens.Take(shown)));
                else countAndNames.Length.ShouldBe(1);
            }
            else siblingLine.ShouldBe(marker);
        }
        else note.Body.ShouldNotContain("unlanded-sibling=");
        note.Body.ShouldNotContain("\ufffd");
        source.Detail.ShouldBe("full publication narrative; " + marker);
    }

    [Test]
    [Arguments("captured")] [Arguments("interrupted")] [Arguments("malformed")] [Arguments("mismatch")]
    public void C443_D9_PriorCaptureKeepsItsIdentityAndAvailability(string evidence)
    {
        var capture = Capture(33);
        var presentation = new WorktreeCleanupPresentation();
        var json = presentation.Serialize(capture);
        var reference = new WorktreeCleanupReference(capture.Id, capture.RequestId, capture.OperationId, capture.At,
            evidence == "interrupted" ? WorktreeCleanupCaptureState.Interrupted : WorktreeCleanupCaptureState.Captured,
            new string('\u754c', 600), evidence switch { "interrupted" => null, "malformed" => "{", _ => json }, true);
        if (evidence == "mismatch") reference = reference with { AttemptId = Guid.NewGuid() };
        var request = new AgentTaskLandRequest { Id = Guid.NewGuid(), TaskId = capture.TaskId,
            ExpectedSourceSha = new('a', 64), LocalBeforeSha = new('b', 64),
            RemoteSourceSha = new('c', 64), CandidateSourceSha = new('d', 64) };
        var source = new AgentTaskEvent { Id = Guid.NewGuid(), Type = AgentTaskEventType.LandingCleanup,
            LandingPublication = LandPublicationOutcome.AlreadyPresent, LandingCleanup = LandCleanupStatus.Complete };
        var note = LandNotificationPayload.Create(request, source, LandNotificationKind.Outcome, reference,
            Enumerable.Range(0, 64).Select(i => $"{i:D8}:feat/" + new string('s', 180)).ToArray(), new string('\u754c', 400));
        Encoding.UTF8.GetByteCount(note.Body).ShouldBeLessThanOrEqualTo(1024);
        note.Body.ShouldContain($"capture={reference.AttemptId:N}");
        note.Body.ShouldContain($"prior attempt request={capture.RequestId:N}");
        note.Body.ShouldContain(capture.At.ToString("O"));
        note.Body.ShouldContain("unlanded-sibling=64 siblings, showing first 0");
        if (evidence == "captured")
        {
            note.Body.ShouldContain("InsufficientPrivileges");
            note.Body.ShouldContain("Git git_exit_128 exit=128");
            note.Body.ShouldContain("DeleteAccessOpen=33");
        }
        else
        {
            note.Body.ShouldContain($"{reference.State}; diagnostic evidence unavailable");
            note.Body.ShouldNotContain("DeleteAccessOpen=33");
        }
        reference.Summary.ShouldBe(new string('\u754c', 600));
    }

    internal static WorktreeCleanupCapture Capture(int code) => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        DateTime.UtcNow, new("worktree remove", 128, "git_exit_128", null, DateTime.UtcNow, true),
        new(WorktreeLockStatus.Unavailable, "InsufficientPrivileges", DateTime.UtcNow, []),
        new(WorktreeLockStatus.Partial, "Observed", Enumerable.Range(0, 65).Select(i =>
            new WorktreeNativeObservation("DeleteAccessOpen", new string('\u00e9', 180) + "/" + i, DateTime.UtcNow,
                i != 64, i == 64 ? code : null, true)).ToArray()));
}
