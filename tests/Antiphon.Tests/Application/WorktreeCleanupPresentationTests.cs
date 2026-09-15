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
        var reference = new WorktreeCleanupReference(capture.Id, request.Id, Guid.NewGuid(), capture.At,
            WorktreeCleanupCaptureState.Captured, presentation.Summary(capture), presentation.Serialize(capture));
        var note = LandNotificationPayload.Create(request, source, LandNotificationKind.Outcome,
            presentation.Detail("worktree_remove_failed", reference), marker);
        Encoding.UTF8.GetByteCount(note.Body).ShouldBeLessThanOrEqualTo(1024);
        note.Body.ShouldContain(capture.Id.ToString("N")); note.Body.ShouldContain(new string('\u754c', 80));
        note.Body.ShouldContain("PID=4321"); note.Body.ShouldContain("DeleteAccessOpen=32");
        note.Body.ShouldContain("worktree_remove_failed"); note.Body.ShouldEndWith("~");
        if (marker is not null) note.Body.ShouldContain(marker);
        else note.Body.ShouldNotContain("unlanded-sibling=");
        source.Detail.ShouldBe(narrative);
    }

    internal static WorktreeCleanupCapture Capture(int code) => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        DateTime.UtcNow, new("worktree remove", 128, "git_exit_128", null, DateTime.UtcNow, true),
        new(WorktreeLockStatus.Unavailable, "InsufficientPrivileges", DateTime.UtcNow, []),
        new(WorktreeLockStatus.Partial, "Observed", Enumerable.Range(0, 65).Select(i =>
            new WorktreeNativeObservation("DeleteAccessOpen", new string('\u00e9', 180) + "/" + i, DateTime.UtcNow,
                i != 64, i == 64 ? code : null, true)).ToArray()));
}
