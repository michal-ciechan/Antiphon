using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Antiphon.Tests.TestHelpers;

internal static class Card0452LandingCases
{
    public static async Task AssertSeededRemoteSourceAsync(LandingSafetyHarness h)
    {
        (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", "--verify",
            h.Fixture.SourceRef + "^{commit}")).Trim().ShouldBe(h.Fixture.SeedSha);
        await h.Fixture.AssertRemoteSourceAsync();
    }

    public static async Task AssertRemoteSourceAndPushSafetyAsync(LandingSafetyHarness h)
    {
        foreach (var command in h.Fixture.Git.Trace.Where(a => a.Length > 0 && a[0] == "push"))
        {
            command.ShouldNotContain("--mirror");
            command.ShouldNotContain("--force");
            command.ShouldNotContain("-f");
            command.ShouldNotContain("--delete");
            command.ShouldNotContain(a => a.StartsWith("--force-with-lease", StringComparison.Ordinal));
            command.ShouldNotContain("--force-if-includes");
            command.ShouldNotContain(a => a.StartsWith("+", StringComparison.Ordinal));
            command.ShouldNotContain(":" + h.Fixture.SourceRef);
        }
        await h.Fixture.AssertRemoteSourceAsync();
    }

    public static async Task<string> InstallRejectingTargetHookAsync(LandingSafetyHarness h)
    {
        var hooks = Path.Combine(h.Fixture.Remote, "hooks");
        Directory.CreateDirectory(hooks);
        var hook = Path.Combine(hooks, "pre-receive");
        var marker = Path.Combine(hooks, "rejected.marker");
        await File.WriteAllTextAsync(hook, "#!/bin/sh\nwhile read old new ref; do\n  if [ \"$ref\" = 'refs/heads/master' ]; then printf fired > hooks/rejected.marker; exit 1; fi\ndone\nexit 0\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        await h.Fixture.RequiredAsync(h.Fixture.Remote, "config", "core.hooksPath", hooks);
        h.Fixture.Git.HooksPathOverride = hooks;
        return marker;
    }

    public static async Task RunScopedResidueAsync(LandingSafetyHarness h)
    {
        var projectId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        await using var db = h.CreateContext();
        db.Projects.Add(new Project
        {
            Id = projectId, Name = "C452 fixture " + h.Fixture.TaskId.ToString("N"),
            GitRepositoryUrl = h.Fixture.Remote, LocalRepositoryPath = h.Fixture.Repository,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        db.Boards.Add(new Board
        {
            Id = boardId, ProjectId = projectId, Name = "C452 fixture board",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
        task.ProjectId = projectId;
        await db.SaveChangesAsync();
        var settings = new WorktreeResidueSettings { Execute = true, MinSettledMinutes = 0 };
        var gitSettings = new GitSettings { WorktreeBasePath = Path.Combine(h.Fixture.Root, "trees") };
        var scopes = h.Services.GetRequiredService<IServiceScopeFactory>();
        var reservations = new WorkspaceReservationJournal(scopes, h.Clock);
        var retirement = new TaskWorktreeRetirementService(db, h.Clock,
            Options.Create(settings), Options.Create(gitSettings),
            NullLogger<TaskWorktreeRetirementService>.Instance,
            h.Services.GetRequiredService<ILandingGit>(),
            reservations: reservations,
            commands: new RetirementCommandJournal(scopes, h.Clock),
            worktrees: h.Services.GetRequiredService<IWorktreeManager>(),
            leases: h.Services.GetRequiredService<IRepositoryMutationLease>(),
            admission: new WorkspaceUseAdmission(reservations, db));
        var sweep = new WorktreeResidueSweepService(db, h.Services.GetRequiredService<IWorktreeManager>(),
            Options.Create(settings), Options.Create(gitSettings),
            h.Clock, NullLogger<WorktreeResidueSweepService>.Instance, retirement: retirement,
            reservations: reservations);
        var preview = await sweep.PreviewAsync(projectId, boardId, CancellationToken.None);
        var recordedPreview = await db.WorktreeResidueRuns.AsNoTracking().SingleAsync(r => r.Id == preview.Id);
        recordedPreview.ProjectId.ShouldBe(projectId);
        recordedPreview.BoardId.ShouldBe(boardId);
        preview.Rows.ShouldContain(r => r.TaskId == h.Fixture.TaskId);
        await sweep.RunAsync(CancellationToken.None);
        var execution = await db.WorktreeResidueRuns.AsNoTracking().Where(r => !r.Preview)
            .OrderByDescending(r => r.StartedAt).FirstAsync();
        execution.Execute.ShouldBeTrue();
        (await db.WorktreeResidueRunCandidates.AsNoTracking()
            .AnyAsync(c => c.RunId == execution.Id && c.TaskId == h.Fixture.TaskId))
            .ShouldBeTrue("the executing sweep must inspect this task");
    }
}
