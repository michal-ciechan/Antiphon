using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

// CARD-0547 D-6: the Commit-child audit asks Git's trailer parser, not a hardcoded spelling.
public partial class AgentTaskReplyIntegrationTests
{
    private static async Task<(ScratchGitRepo Repo, Antiphon.Server.Domain.Entities.AgentTask Task, Guid SessionId, Guid Parent, string Sha7)>
        SeedC547AuditAsync(Func<ScratchGitRepo, Task> commit)
    {
        var seeded = await SeedC527Async(t => { t.Role = AgentTaskRole.Commit; t.CommitOnSettle = CommitOnSettlePolicy.Never; }, "c547");
        var repo = seeded.Repo;
        var baseline = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        await commit(repo);
        var sha7 = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim()[..7];
        await using (var db = CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == seeded.Task.Id);
            task.CommitBaselineSha = baseline;
            task.CommitUpstreamBaselineJson = JsonSerializer.Serialize(new GitWorkspaceService.UpstreamSnapshot(true, null, null));
            await db.SaveChangesAsync();
        }
        return (repo, seeded.Task, seeded.SessionId, seeded.Parent, sha7);
    }

    private static bool IsTrailerRead(string[] args) =>
        args[0] == "log" && args.Any(a => a.StartsWith("--format=%(trailers:", StringComparison.Ordinal));

    [Test]
    [Arguments(":")]
    [Arguments("=")]
    [Arguments("%")]
    public async Task C547_commit_child_audit_accepts_a_gated_commit_under_each_separator(string separator)
    {
        var seeded = await SeedC547AuditAsync(async repo =>
        {
            await repo.GitAsync("config", "trailer.separators", separator);
            await File.WriteAllTextAsync(Path.Combine(repo.Path, "a.md"), "a");
            await repo.GitAsync("add", "a.md");
            await repo.GitAsync("commit", "-m", "gated child commit", "--trailer", "antiphon-commit=gated");
            (await repo.GitReadAsync("log", "-1", "--format=%B")).ShouldContain($"antiphon-commit{separator} gated");
        });
        using var repo = seeded.Repo;
        const string report = "Committed a.md.";
        var factory = C527Factory(repo.WorktreeRoot);
        AttachTerminal(factory, seeded.Parent);
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), report);
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        await Queue(factory).FlushSessionAsync(seeded.Parent, CancellationToken.None);

        await using var verify = CreateContext();
        var outsideTheGate = await verify.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == seeded.Task.Id
            && e.Type == AgentTaskEventType.Warning && e.Detail.Contains("outside the gate"));
        outsideTheGate.ShouldBeFalse();
        (await verify.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == seeded.Task.Id
            && e.Type == AgentTaskEventType.Warning && e.Detail.Contains("audit unavailable"))).ShouldBeFalse();
        (await verify.AgentIncidents.AnyAsync(i => i.SessionId == seeded.SessionId)).ShouldBeFalse();
        var receipt = await AssertParentReceivedNoteAsync(seeded.Parent, seeded.Task, report);
        receipt.Prompt.Text.ShouldNotContain("outside the gate");
    }

    [Test]
    public async Task C547_commit_child_audit_flags_a_body_prose_mention_without_a_trailer_block()
    {
        var seeded = await SeedC547AuditAsync(async repo =>
        {
            await File.WriteAllTextAsync(Path.Combine(repo.Path, "a.md"), "a");
            await repo.GitAsync("add", "a.md");
            await repo.GitAsync("commit", "-m",
                "ungated\n\nSee antiphon-commit: gated in the docs.\n\nTrailing prose line without separator");
            (await repo.GitReadAsync("log", "-1", "--format=%(trailers:only)")).Trim().ShouldBeEmpty();
        });
        using var repo = seeded.Repo;
        var factory = C527Factory(repo.WorktreeRoot);
        AttachTerminal(factory, seeded.Parent);
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), "Committed.");
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var warning = await verify.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == seeded.Task.Id
            && e.Type == AgentTaskEventType.Warning && e.Detail.Contains("outside the gate"));
        warning.Detail.ShouldContain($"commit {seeded.Sha7} was made outside the gate");
        warning.Detail.ShouldNotContain("REVERT");
        (await verify.AgentIncidents.AnyAsync(i => i.SessionId == seeded.SessionId)).ShouldBeFalse();
    }

    [Test]
    public async Task C547_commit_child_audit_read_failure_is_unavailable_not_outside_the_gate()
    {
        var seeded = await SeedC547AuditAsync(async repo =>
        {
            await File.WriteAllTextAsync(Path.Combine(repo.Path, "a.md"), "a");
            await repo.GitAsync("add", "a.md");
            await repo.GitAsync("commit", "-m", "gated child commit", "--trailer", "antiphon-commit=gated");
        });
        using var repo = seeded.Repo;
        const string report = "Committed under a flaky git.";
        var spy = new RecordingGitWorkspaceService();
        spy.OverrideRun = args => IsTrailerRead(args) ? (128, "", "trailers unavailable") : null;
        var factory = C527Factory(repo.WorktreeRoot, gitSpy: spy);
        AttachTerminal(factory, seeded.Parent);
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), report);
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        await Queue(factory).FlushSessionAsync(seeded.Parent, CancellationToken.None);

        await using var verify = CreateContext();
        await verify.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == seeded.Task.Id
            && e.Type == AgentTaskEventType.Warning
            && e.Detail == $"commit {seeded.Sha7} audit unavailable: trailers unavailable");
        (await verify.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == seeded.Task.Id
            && e.Type == AgentTaskEventType.Warning && e.Detail.Contains("outside the gate"))).ShouldBeFalse();
        var receipt = await AssertParentReceivedNoteAsync(seeded.Parent, seeded.Task, report);
        receipt.Prompt.Text.ShouldContain("audit unavailable");
        receipt.Prompt.Text.ShouldContain("trailers unavailable");
        receipt.Prompt.Text.ShouldNotContain("outside the gate");
    }
}
