using System.Diagnostics;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class CardFileBoardLookupTests
{
    [Test]
    [Arguments("restore|--staged|--|docs/cards/board/x.md", false)]
    [Arguments("reset|HEAD", false)]
    [Arguments("reset|--soft|HEAD", false)]
    [Arguments("reset|--hard|HEAD", true)]
    [Arguments("reset|--merge|HEAD", true)]
    [Arguments("checkout|--|.", true)]
    [Arguments("switch|-", true)]
    [Arguments("stash|pop", true)]
    [Arguments("restore|--worktree|--|docs/cards/board/x.md", true)]
    [Arguments("restore|--|docs/cards/board/x.md", true)]
    [Arguments("status|--porcelain", false)]
    [Arguments("diff|--name-only|HEAD", false)]
    [Arguments("commit|-m|note", false)]
    [Arguments("-C|elsewhere|checkout|--|.", true)]
    [Arguments("-c|user.name=test|status", false)]
    public async Task Repair2_Git_commands_are_classified_as_worktree_changes(string command, bool restores)
    {
        CardFileBoardLookup.GitMayRestoreWorktree(command.Split('|')).ShouldBe(restores);
        await Task.CompletedTask;
    }

    [Test]
    public async Task Repair2_Git_in_another_repository_leaves_the_board_skipped()
    {
        await using var prepared = await PrepareSkippedExportAsync();
        await using var read = prepared.World.Db();
        var board = (await _lookup.GetAsync(read, default)).Boards.Single(b => b.Id == prepared.World.BoardId);
        _lookup.NeedsInspection(board).ShouldBeFalse();
        using var other = new ScratchGitRepo("c700-other");
        await other.CommitFileAsync("seed", "other\n");
        var git = new LandingGit(_lookup);
        var before = _lookup.Reinspection;
        await git.RunAsync(other.Path, ["checkout", "--", "."], default);
        _lookup.Reinspection.ShouldBe(before + 1);
        _lookup.NeedsInspection(board).ShouldBeFalse();
        await git.RunAsync(prepared.World.Repo.Path, ["status", "--porcelain"], default);
        _lookup.Reinspection.ShouldBe(before + 1);
        _lookup.NeedsInspection(board).ShouldBeFalse();
        await git.RunAsync(prepared.World.Repo.Path, ["checkout", "--", "."], default);
        _lookup.NeedsInspection(board).ShouldBeTrue();
    }

    [Test]
    public async Task Repair2_Staged_restore_is_cleared_on_the_next_sweep()
    {
        await using var prepared = await PrepareRemovedExportAsync();
        await StageExportAsync(prepared);
        var removed = (await prepared.Service.SyncAllAsync()).Single();
        removed.Deleted.ShouldBe(2);
        await AssertExportGoneAsync(prepared.Repo);
    }

    [Test]
    public async Task Repair2_Staged_restore_is_cleared_on_the_next_tick()
    {
        await using var prepared = await PrepareRemovedExportAsync();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.Zero));
        var scopes = new SweepScopes(() => SweepScope(prepared, autoCommit: false));
        using var hosted = new CardTaskFileSyncHostedService(scopes,
            Options.Create(new CardFileSyncSettings { Enabled = true, IntervalSeconds = 60, OptedOutReinspectionMinutes = 15 }),
            NullLogger<CardTaskFileSyncHostedService>.Instance, _lookup, time);
        await hosted.StartAsync(default);
        (await prepared.Service.SyncAllAsync()).Single().Policy!.RemovalPending.ShouldBeFalse();
        (await prepared.Service.SyncAllAsync()).ShouldBeEmpty();
        await StageExportAsync(prepared);
        await AdvanceTickAsync(time, scopes, TimeSpan.FromSeconds(60));
        await AssertExportGoneAsync(prepared.Repo);
        await hosted.StopAsync(default);
    }

    [Test]
    public async Task Repair2_Backstop_removes_a_head_only_export_after_the_interval()
    {
        await using var prepared = await PrepareRemovedExportAsync();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.Zero));
        var scopes = new SweepScopes(() => SweepScope(prepared, autoCommit: true));
        using var hosted = new CardTaskFileSyncHostedService(scopes,
            Options.Create(new CardFileSyncSettings { Enabled = true, IntervalSeconds = 60, OptedOutReinspectionMinutes = 15 }),
            NullLogger<CardTaskFileSyncHostedService>.Instance, _lookup, time);
        await hosted.StartAsync(default);
        (await PublishingService(prepared).SyncAllAsync()).Single().Policy!.RemovalPending.ShouldBeFalse();
        (await PublishingService(prepared).SyncAllAsync()).ShouldBeEmpty();
        await LeaveHeadOnlyExportAsync(prepared);
        var skippedAt = _lookup.Reinspection;
        for (var tick = 0; tick < 14; tick++)
            await AdvanceTickAsync(time, scopes, TimeSpan.FromSeconds(60));
        time.GetUtcNow().ShouldBe(new DateTimeOffset(2026, 9, 25, 0, 14, 0, TimeSpan.Zero));
        _lookup.Reinspection.ShouldBe(skippedAt);
        (await HeadNamesAsync(prepared.Repo)).ShouldContain("docs/cards/");
        await using (var read = prepared.World.Db())
        {
            var board = (await _lookup.GetAsync(read, default)).Boards.Single(b => b.Id == prepared.World.BoardId);
            _lookup.NeedsInspection(board).ShouldBeFalse();
        }
        await AdvanceTickAsync(time, scopes, TimeSpan.FromSeconds(60));
        _lookup.Reinspection.ShouldBe(skippedAt + 1);
        (await HeadNamesAsync(prepared.Repo)).ShouldNotContain("docs/cards/");
        Directory.GetFiles(prepared.World.DirectoryPath, "*.md").ShouldBeEmpty();
        await hosted.StopAsync(default);
    }

    [Test]
    public async Task Repair2_Whole_index_commit_omits_staged_opted_out_cards()
    {
        await using var prepared = await PrepareRemovedExportAsync();
        await StageExportAsync(prepared);
        await File.WriteAllTextAsync(Path.Combine(prepared.Repo, "notes.txt"), "notes\n");
        var git = new GitService(NullLogger<GitService>.Instance, _lookup);
        (await git.CommitAllChangesAsync(prepared.Repo, "agent commit", default)).ShouldBeTrue();
        var head = await HeadNamesAsync(prepared.Repo);
        head.ShouldContain("notes.txt");
        head.ShouldNotContain("docs/cards/");
        await AssertExportGoneAsync(prepared.Repo);
    }

    [Test]
    public async Task Repair2_Commit_only_without_paths_omits_staged_opted_out_cards()
    {
        await using var prepared = await PrepareRemovedExportAsync();
        await StageExportAsync(prepared);
        await File.WriteAllTextAsync(Path.Combine(prepared.Repo, "notes.txt"), "notes\n");
        await GitOk(prepared.Repo, "add", "--", "notes.txt");
        var git = new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance, cardFiles: _lookup);
        var commit = await git.CommitOnlyAsync(prepared.Repo, null, "whole index", [], default);
        commit.Code.ShouldBe(0, commit.Stderr);
        var head = await HeadNamesAsync(prepared.Repo);
        head.ShouldContain("notes.txt");
        head.ShouldNotContain("docs/cards/");
        await AssertExportGoneAsync(prepared.Repo);
    }

    [Test]
    public async Task Repair2_Land_diff_touching_an_opted_out_directory_is_refused()
    {
        await using var prepared = await PrepareRemovedExportAsync();
        var parent = (await GitOk(prepared.Repo, "rev-parse", "HEAD")).Trim();
        var reason = await prepared.Service.OptedOutLandRefusalAsync(prepared.Repo, parent, prepared.ExportSha, default);
        reason.ShouldBe("opted_out_card_files");
    }

    [Test]
    public async Task Repair2_Land_diff_on_an_opted_in_board_is_allowed()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var world = new CardFilePrivacyWorld(isolated.ConnectionString);
        await world.InitializeAsync();
        await world.AddCardAsync();
        await using var db = Context(isolated.ConnectionString, new QueryCounter());
        var service = Service(world, db, autoCommit: true);
        (await service.SyncBoardAsync(world.BoardId)).Sha.ShouldNotBeNull();
        var child = (await GitOk(world.Repo.Path, "rev-parse", "HEAD")).Trim();
        var parent = (await GitOk(world.Repo.Path, "rev-parse", "HEAD~1")).Trim();
        (await service.OptedOutLandRefusalAsync(world.Repo.Path, parent, child, default)).ShouldBeNull();
    }

    private async Task<RemovedExport> PrepareRemovedExportAsync()
    {
        var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = new CardFilePrivacyWorld(isolated.ConnectionString);
        AppDbContext? db = null;
        try
        {
            await world.InitializeAsync();
            await world.AddCardAsync();
            db = Context(isolated.ConnectionString, new QueryCounter());
            var service = Service(world, db, autoCommit: true);
            var published = await service.SyncBoardAsync(world.BoardId);
            published.Written.ShouldBe(2);
            published.Sha.ShouldNotBeNull();
            var exportSha = (await GitOk(world.Repo.Path, "rev-parse", "HEAD")).Trim();
            await using (var edit = world.Db())
                await edit.Boards.Where(b => b.Id == world.BoardId).ExecuteUpdateAsync(s => s.SetProperty(b => b.SyncCardFiles, false));
            _lookup.Invalidate();
            var cleaned = (await service.SyncAllAsync()).Single();
            cleaned.Deleted.ShouldBe(2);
            cleaned.Policy!.RemovalPending.ShouldBeFalse();
            Directory.GetFiles(world.DirectoryPath, "*.md").ShouldBeEmpty();
            (await HeadNamesAsync(world.Repo.Path)).ShouldNotContain("docs/cards/");
            (await service.SyncAllAsync()).ShouldBeEmpty();
            var prepared = new RemovedExport(isolated, world, db, service, exportSha, isolated.ConnectionString);
            db = null;
            return prepared;
        }
        catch
        {
            if (db is not null) await db.DisposeAsync();
            await world.DisposeAsync();
            await isolated.DisposeAsync();
            throw;
        }
    }

    private CardTaskFileService Service(CardFilePrivacyWorld world, AppDbContext db, bool autoCommit) => new(db, world.Gate,
        new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance), NullLogger<CardTaskFileService>.Instance,
        new CardFileTestRepository(), Options.Create(new CardFileSyncSettings { AutoCommit = autoCommit, IntervalSeconds = 0 }), _lookup);

    private CardTaskFileService PublishingService(RemovedExport prepared) => prepared.Service;

    private static async Task StageExportAsync(RemovedExport prepared)
    {
        await GitOk(prepared.Repo, "checkout", prepared.ExportSha, "--", "docs/cards/board");
        Directory.GetFiles(prepared.World.DirectoryPath, "*.md").Length.ShouldBe(2);
        (await GitOk(prepared.Repo, "diff", "--cached", "--name-only")).ShouldContain("docs/cards/");
    }

    private static async Task LeaveHeadOnlyExportAsync(RemovedExport prepared)
    {
        await GitOk(prepared.Repo, "checkout", prepared.ExportSha, "--", "docs/cards/board");
        await GitOk(prepared.Repo, "commit", "-m", "reintroduce opted-out export");
        foreach (var path in Directory.GetFiles(prepared.World.DirectoryPath, "*.md"))
        {
            var relative = Path.GetRelativePath(prepared.Repo, path).Replace('\\', '/');
            await GitOk(prepared.Repo, "rm", "--cached", "--", relative);
            File.Delete(path);
        }
        Directory.GetFiles(prepared.World.DirectoryPath, "*.md").ShouldBeEmpty();
        (await HeadNamesAsync(prepared.Repo)).ShouldContain("docs/cards/");
        (await GitOk(prepared.Repo, "ls-files", "--", "docs/cards")).Trim().ShouldBeEmpty();
    }

    private static async Task AssertExportGoneAsync(string repo)
    {
        Directory.GetFiles(Path.Combine(repo, "docs", "cards", "board"), "*.md").ShouldBeEmpty();
        (await GitOk(repo, "ls-files", "--", "docs/cards")).Trim().ShouldBeEmpty();
    }

    private static async Task<string> HeadNamesAsync(string repo) =>
        await GitOk(repo, "ls-tree", "-r", "--name-only", "HEAD");

    private IServiceScope SweepScope(RemovedExport prepared, bool autoCommit)
    {
        var db = Context(prepared.ConnectionString, new QueryCounter());
        var service = Service(prepared.World, db, autoCommit);
        return new SweepScopeHandle(db, service);
    }

    private static async Task AdvanceTickAsync(FakeTimeProvider time, SweepScopes scopes, TimeSpan interval)
    {
        var seen = scopes.Completed;
        await Task.Delay(50);
        time.Advance(interval);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (scopes.Completed == seen)
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("Card-file tick did not finish.");
            await Task.Delay(20);
        }
    }

    private static async Task<string> GitOk(string directory, params string[] args)
    {
        var result = await Git(directory, args);
        result.Code.ShouldBe(0, result.Stderr);
        return result.Stdout;
    }

    private static async Task<(int Code, string Stdout, string Stderr)> Git(string directory, params string[] args)
    {
        var start = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException($"git {string.Join(' ', args)} timed out");
        }
        return (process.ExitCode, await stdout, await stderr);
    }

    private sealed class RemovedExport(
        IAsyncDisposable isolated, CardFilePrivacyWorld world, AppDbContext db, CardTaskFileService service,
        string exportSha, string connectionString)
        : IAsyncDisposable
    {
        public CardFilePrivacyWorld World { get; } = world;
        public CardTaskFileService Service { get; } = service;
        public string ExportSha { get; } = exportSha;
        public string Repo => World.Repo.Path;
        public string ConnectionString { get; } = connectionString;
        public async ValueTask DisposeAsync()
        {
            await db.DisposeAsync();
            await world.DisposeAsync();
            await isolated.DisposeAsync();
        }
    }

    private sealed class SweepScopes(Func<IServiceScope> create) : IServiceScopeFactory
    {
        public int Completed;
        public IServiceScope CreateScope() => new CompletingScope(create(), () => Interlocked.Increment(ref Completed));

        private sealed class CompletingScope(IServiceScope inner, Action completed) : IServiceScope
        {
            public IServiceProvider ServiceProvider => inner.ServiceProvider;
            public void Dispose()
            {
                inner.Dispose();
                completed();
            }
        }
    }

    private sealed class SweepScopeHandle(AppDbContext db, CardTaskFileService service) : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; } = new Provider(service);
        public void Dispose() => db.Dispose();

        private sealed class Provider(CardTaskFileService service) : IServiceProvider
        {
            public object? GetService(Type serviceType) =>
                serviceType == typeof(CardTaskFileService) ? service : null;
        }
    }
}
