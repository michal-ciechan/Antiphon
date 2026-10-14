using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Antiphon.Tests.Application;

public partial class DelegationDispatchRecoveryBoundaryTests
{
    [Test]
    [Arguments("legacy", "feat/card-task-7f3a2b91")]
    [Arguments("card", "feat/card-1213-code-7f3a2b91")]
    public async Task C1213_Recovered_brief_keeps_branch_protections_without_git_common(string label, string branch)
    {
        // CARD-1213 review dd8ce0cf F1: recovery inserts an absent brief into a live session whose
        // instructions were composed before git-common existed. The queue's ensure path builds the
        // brief from the task row only, so the delivered runner contract itself carries the branch
        // protections, the full base SHA and the exact push command.
        const string baseSha = "0123456789abcdef0123456789abcdef01234567";
        const string legacyStamp = "delegate-basics v1a2b3c4d";
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var seeded = await SeedCurrentAsync(schema.ConnectionString, "Recover this runner brief.");
        try
        {
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            {
                await db.AgentTasks.Where(t => t.Id == seeded.TaskId).ExecuteUpdateAsync(t => t
                    .SetProperty(x => x.Workspace, WorkspaceMode.Worktree)
                    .SetProperty(x => x.RunnerId, "server2")
                    .SetProperty(x => x.WorktreePath, "C:\\Antiphon\\worktrees\\card-task-7f3a2b91")
                    .SetProperty(x => x.RemoteWorktreePath, "/work/worktrees/task-7f3a2b91")
                    .SetProperty(x => x.WorktreeBranch, branch)
                    .SetProperty(x => x.WorktreeBaseSha, baseSha));
                await db.AgentSessions.Where(s => s.Id == seeded.SessionId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.ComposedBundleStamp, legacyStamp));
                (await db.AgentSessions.SingleAsync(s => s.Id == seeded.SessionId)).ComposedBundleStamp
                    .ShouldNotBeNull().ShouldNotContain(InstructionFragments.GitCommon, Case.Sensitive, label + "-legacy-session");
            }

            await using var queue = await OpenQueueAsync(schema.ConnectionString, seeded);
            var ensured = await queue.Queue.EnsureDispatchBriefAsync(RequestFor(seeded), CancellationToken.None);
            ensured.Inserted.ShouldBeTrue(label + "-inserted");
            var brief = await SingleBriefAsync(schema.ConnectionString, seeded.TaskId, label);
            var payload = AuthoritativeBrief(brief).Replace("\r\n", "\n");
            payload.ShouldContain("--- runner branch contract ---", Case.Sensitive, label + "-contract");
            var contract = payload.Split("--- runner branch contract ---")[1].Split("\n\n")[0];
            contract.ShouldContain("fast-forward-only from " + baseSha + ": "
                + DelegationReportFormatterTests.BranchProtections, Case.Sensitive, label + "-protections");
            contract.ShouldContain("git push origin HEAD:refs/heads/" + branch + "`", Case.Sensitive, label + "-push");

            if (await UserPromptCountAsync(schema.ConnectionString, seeded.SessionId) == 0)
                await queue.Queue.FlushSessionAsync(seeded.SessionId, CancellationToken.None);
            var prompts = await UserPromptsAsync(schema.ConnectionString, seeded.SessionId);
            prompts.Count.ShouldBe(1, label + "-prompt");
            PromptSubmissionMatch.IsCompleteIn(brief.Body, prompts[0]).ShouldBeTrue(label + "-complete-prompt");
        }
        finally
        {
            if (Directory.Exists(seeded.Directory))
                Directory.Delete(seeded.Directory, true);
        }
    }
}
