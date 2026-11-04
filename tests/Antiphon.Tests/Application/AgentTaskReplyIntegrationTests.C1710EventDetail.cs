using System.Text;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1710: the shared-checkout settlement Warning joined every dirty path, so a reported
/// directory full of long Unicode names reached the 4000-char Detail cut, which split an emoji
/// and failed the strict UTF-8 save. The list is now the CARD-1637 D-1 bounded summary.
/// </summary>
public partial class AgentTaskReplyIntegrationTests
{
    [Test]
    public async Task C1710_a_shared_report_naming_a_directory_of_long_unicode_names_writes_a_bounded_valid_warning()
    {
        using var repo = new ScratchGitRepo("antiphon-reply-c1710-unicode");
        const string emoji = "\U0001F600";
        var names = Enumerable.Range(0, 40)
            .Select(i => $"u/{i:D2}a" + string.Concat(Enumerable.Repeat(emoji, 60)) + ".md")
            .ToList();
        Directory.CreateDirectory(Path.Combine(repo.Path, "u"));
        foreach (var name in names)
            await File.WriteAllTextAsync(Path.Combine(repo.Path, name), "uncommitted\n");
        const string prefix = "Report names 40 file(s) still uncommitted in the shared checkout: ";
        var unbounded = prefix + string.Join(", ", names) + ".";
        char.IsHighSurrogate(unbounded[3999]).ShouldBeTrue("the unbounded join would be cut inside an emoji at 4000");
        var factory = new TestScopeFactory(
            repo.WorktreeRoot, delegation: new DelegationSettings { CommitOnSettle = false });
        var parentSessionId = await SeedSessionAsync(repo.Path);
        var (task, sessionId) = await SeedDispatchedTaskAsync(repo.Path, parentSessionId, t =>
        {
            t.RepoPath = repo.Path;
            t.Workspace = WorkspaceMode.Shared;
        });

        await SeedTurnAsync(
            sessionId, DelegationReportFormatter.TaskMarker(task.Id), "Wrote the notes under `u/`.");
        await CreateService(factory, new DelegationSettings { CommitOnSettle = false })
            .OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var warning = await verify.AgentTaskEvents.SingleAsync(e =>
            e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Warning
                && e.Detail.Contains("still uncommitted"));
        Should.NotThrow(() => new UTF8Encoding(false, true).GetBytes(warning.Detail));
        warning.Detail.ShouldBe(prefix + string.Join(", ", names.Take(15)) + ", ... and 25 more.");
        var note = await verify.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == parentSessionId);
        note.NoteHeader.ShouldContain("git=uncommitted:40");
    }
}
