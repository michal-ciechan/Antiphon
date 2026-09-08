using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskLocalMergeSafetyTests
{
    [Test]
    [Arguments("clean")]
    [Arguments("unicode-path")]
    [Arguments("no-change")]
    [Arguments("parent-dirty")]
    [Arguments("parent-moved-after-rebase")]
    [Arguments("parent-switch-after-rebase")]
    [Arguments("parent-dirty-after-rebase")]
    [Arguments("child-dirty-after-rebase")]
    [Arguments("parent-moved-before-cleanup")]
    public async Task C448_V25_LocalMergeKeepsItsCapturedParentAuthority(string variant)
    {
        await using var h = new LandingSafetyHarness(variant == "unicode-path"
            ? Path.Combine(Path.GetTempPath(), "antiphon-c448-" + Guid.NewGuid().ToString("N") + " space ü") : null);
        await h.InitializeAsync();
        var source = variant == "no-change" ? h.Fixture.SeedSha : await h.AddSourceAsync();
        var parentBytes = Path.Combine(h.Fixture.Repository, "keep.txt");
        var childBytes = Path.Combine(h.Fixture.Source, "keep.txt");
        if (variant == "parent-dirty") await File.WriteAllTextAsync(parentBytes, "parent edits\n");
        var fired = false;
        string? movedParent = null;
        h.Fixture.Git.AfterCommand = async (_, args, result) =>
        {
            if (fired || !result.Succeeded) return;
            if (args.Contains("rebase") && variant.EndsWith("after-rebase", StringComparison.Ordinal)
                || args.Contains("merge") && args.Contains("--ff-only") && variant == "parent-moved-before-cleanup")
            {
                fired = true;
                if (variant.Contains("moved", StringComparison.Ordinal))
                {
                    await h.Fixture.RequiredAsync(h.Fixture.Repository, "commit", "--allow-empty", "-m", "new parent owner");
                    movedParent = (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", "HEAD")).Trim();
                }
                else if (variant.Contains("switch", StringComparison.Ordinal))
                    await h.Fixture.RequiredAsync(h.Fixture.Repository, "checkout", "-b", "different-parent");
                else await File.WriteAllTextAsync(variant.StartsWith("child", StringComparison.Ordinal) ? childBytes : parentBytes,
                    "concurrent edits\n");
            }
        };
        await using var scope = h.Services.CreateAsyncScope();
        await using var db = h.CreateContext();
        var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == h.Fixture.TaskId);
        h.Fixture.Git.Trace.Clear();
        var result = await scope.ServiceProvider.GetRequiredService<DelegationWorktreeService>()
            .TryMergeBackAsync(task, CancellationToken.None);
        if (variant is "clean" or "unicode-path" or "no-change")
        {
            result.Result.ShouldBe(variant != "no-change" ? DelegationWorktreeService.MergeResult.Merged : DelegationWorktreeService.MergeResult.NothingToMerge);
            Directory.Exists(h.Fixture.Source).ShouldBeFalse();
            (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(source);
        }
        else
        {
            if (variant != "parent-dirty") fired.ShouldBeTrue();
            Directory.Exists(h.Fixture.Source).ShouldBeTrue("a changed parent or child revokes local cleanup authority");
            (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.SourceRef)).Trim().ShouldBe(source);
            h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("remove") || a.Contains("-d"));
            if (movedParent is not null) (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", "HEAD")).Trim().ShouldBe(movedParent);
            if (variant.Contains("dirty", StringComparison.Ordinal))
                (await File.ReadAllTextAsync(variant.StartsWith("child", StringComparison.Ordinal) ? childBytes : parentBytes))
                    .ShouldBe(variant == "parent-dirty" ? "parent edits\n" : "concurrent edits\n");
        }
        h.Fixture.Git.Trace.ShouldNotContain(a => a[0] == "push");
        (await db.AgentTaskLandings.CountAsync(o => o.TaskId == task.Id)).ShouldBe(0);
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == task.Id && (e.Type == AgentTaskEventType.Landed || e.Type == AgentTaskEventType.AlreadyPresent))).ShouldBe(0);
        (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(h.Fixture.SeedSha);
        await h.Fixture.AssertRemoteSourceAsync();
    }
}
