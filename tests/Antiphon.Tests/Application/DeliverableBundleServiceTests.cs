using System.Diagnostics;
using System.IO.Compression;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0337 S1: document detection, source copy/zip, PDF failure still keeps sources.</summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public class DeliverableBundleServiceTests
{
    [Test]
    public async Task A_custom_role_report_naming_four_docs_copies_them_and_records_a_render_error_without_a_browser()
    {
        using var workspace = new TempDir();
        WriteDocs(workspace.Path, "01-requirements.md", "02-design.md", "03-api.md", "04-test.md");
        var task = NewTask(workspace.Path, AgentTaskRole.Custom, WorkspaceMode.Worktree);
        var report = """
            Wrote `docs/features/001-kalshi-ref-data-downloader/01-requirements.md`,
            `docs/features/001-kalshi-ref-data-downloader/02-design.md`,
            `docs/features/001-kalshi-ref-data-downloader/03-api.md`,
            `docs/features/001-kalshi-ref-data-downloader/04-test.md`.
            """;

        await CreateService().TryBuildAsync(task, report, db: null, CancellationToken.None);

        task.DeliverableBundleDir.ShouldNotBeNull();
        task.DeliverableBundleDir.ShouldBe(
            Path.Combine(workspace.Path, ".antiphon", "deliverables", DelegationReportFormatter.Short(task.Id)));
        task.DeliverableFileCount.ShouldBe(4);
        task.DeliverablePdfPath.ShouldBeNull();
        task.DeliverableRenderError.ShouldNotBeNull();
        Directory.GetFiles(task.DeliverableBundleDir, "*.md").Length.ShouldBe(4);
        File.Exists(Path.Combine(task.DeliverableBundleDir, "render.log")).ShouldBeTrue();
        DeliverableBundleService.ListAttachableFiles(task).Count.ShouldBe(4);
        DeliverableBundleService.FormatNoteBit(task).ShouldBe("4 md, pdf failed");
    }

    [Test]
    public async Task Docs_cards_and_antiphon_paths_are_never_included()
    {
        using var workspace = new TempDir();
        var features = Path.Combine(workspace.Path, "docs", "features", "one");
        Directory.CreateDirectory(features);
        await File.WriteAllTextAsync(Path.Combine(features, "ok.md"), "# ok");
        Directory.CreateDirectory(Path.Combine(workspace.Path, "docs", "cards"));
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "docs", "cards", "CARD-0001.md"), "# card");
        Directory.CreateDirectory(Path.Combine(workspace.Path, ".antiphon"));
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, ".antiphon", "note.md"), "# note");
        var task = NewTask(workspace.Path, AgentTaskRole.Docs, WorkspaceMode.Shared);
        var report = "`docs/features/one/ok.md` `docs/cards/CARD-0001.md` `.antiphon/note.md`";

        await CreateService().TryBuildAsync(task, report, db: null, CancellationToken.None);

        task.DeliverableFileCount.ShouldBe(1);
        File.Exists(Path.Combine(task.DeliverableBundleDir!, "ok.md")).ShouldBeTrue();
        Directory.GetFiles(task.DeliverableBundleDir!, "*.md").Length.ShouldBe(1);
    }

    [Test]
    public async Task More_than_five_sources_or_a_1mb_source_are_zipped()
    {
        using var workspace = new TempDir();
        var names = Enumerable.Range(1, 6).Select(i => $"{i:00}.md").ToArray();
        WriteDocs(workspace.Path, names);
        var task = NewTask(workspace.Path, AgentTaskRole.Docs, WorkspaceMode.Shared);
        var report = string.Join(" ", names.Select(n => $"`docs/features/001-kalshi-ref-data-downloader/{n}`"));

        await CreateService().TryBuildAsync(task, report, db: null, CancellationToken.None);

        Directory.GetFiles(task.DeliverableBundleDir!, "*.md").ShouldBeEmpty();
        var zip = Directory.GetFiles(task.DeliverableBundleDir!, "*-sources.zip").ShouldHaveSingleItem();
        using var archive = ZipFile.OpenRead(zip);
        archive.Entries.Count.ShouldBe(6);
        archive.Entries.Select(e => e.FullName.Replace('\\', '/'))
            .ShouldContain("docs/features/001-kalshi-ref-data-downloader/01.md");
    }

    [Test]
    public async Task A_code_task_with_a_mixed_diff_and_no_named_doc_gets_no_bundle()
    {
        using var repo = await GitRepo.CreateAsync();
        var baseSha = await repo.HeadAsync();
        Directory.CreateDirectory(Path.Combine(repo.Path, "src"));
        Directory.CreateDirectory(Path.Combine(repo.Path, "docs"));
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "src", "A.cs"), "class A;");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "docs", "note.md"), "# n");
        var task = NewTask(repo.Path, AgentTaskRole.Code, WorkspaceMode.Worktree);
        task.WorktreeBaseSha = baseSha;

        await CreateService().TryBuildAsync(task, "rewrote A.cs and a note.", db: null, CancellationToken.None);

        task.DeliverableBundleDir.ShouldBeNull();
        task.DeliverableFileCount.ShouldBe(0);
    }

    [Test]
    public async Task A_code_task_whose_range_is_all_markdown_gets_a_bundle()
    {
        using var repo = await GitRepo.CreateAsync();
        var baseSha = await repo.HeadAsync();
        Directory.CreateDirectory(Path.Combine(repo.Path, "docs", "features", "one"));
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "docs", "features", "one", "spec.md"), "# spec");
        var task = NewTask(repo.Path, AgentTaskRole.Code, WorkspaceMode.Worktree);
        task.WorktreeBaseSha = baseSha;

        await CreateService().TryBuildAsync(task, "docs only.", db: null, CancellationToken.None);

        task.DeliverableBundleDir.ShouldNotBeNull();
        task.DeliverableFileCount.ShouldBe(1);
        File.Exists(Path.Combine(task.DeliverableBundleDir!, "spec.md")).ShouldBeTrue();
    }

    [Test]
    public async Task A_plan_role_reads_a_branch_only_path_via_git()
    {
        using var repo = await GitRepo.CreateAsync();
        var relative = Path.Combine("docs", "superpowers", "plans", "plan.md");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(repo.Path, relative))!);
        await File.WriteAllTextAsync(Path.Combine(repo.Path, relative), "# Plan on branch");
        await repo.RunAsync("add", "-A");
        await repo.RunAsync("commit", "-m", "plan");
        await repo.RunAsync("branch", "feat/plan");
        File.Delete(Path.Combine(repo.Path, relative));
        var task = NewTask(repo.Path, AgentTaskRole.Plan, WorkspaceMode.Worktree);
        task.WorktreeBranch = "feat/plan";

        await CreateService().TryBuildAsync(
            task, "delivered `docs/superpowers/plans/plan.md`.", db: null, CancellationToken.None);

        task.DeliverableBundleDir.ShouldNotBeNull();
        task.DeliverableFileCount.ShouldBe(1);
        (await File.ReadAllTextAsync(Path.Combine(task.DeliverableBundleDir!, "plan.md")))
            .ShouldContain("Plan on branch");
    }

    [Test]
    public async Task Enabled_false_is_a_no_op()
    {
        using var workspace = new TempDir();
        WriteDocs(workspace.Path, "01-requirements.md");
        var task = NewTask(workspace.Path, AgentTaskRole.Docs, WorkspaceMode.Shared);
        var settings = new DeliverablesSettings
        {
            Enabled = false,
            BrowserPath = Path.Combine(Path.GetTempPath(), "antiphon-no-browser", "msedge.exe"),
        };

        await CreateService(settings).TryBuildAsync(
            task, "`docs/features/001-kalshi-ref-data-downloader/01-requirements.md`", db: null,
            CancellationToken.None);

        task.DeliverableBundleDir.ShouldBeNull();
    }

    private static DeliverableBundleService CreateService(DeliverablesSettings? settings = null)
    {
        settings ??= new DeliverablesSettings
        {
            BrowserPath = Path.Combine(Path.GetTempPath(), "antiphon-no-browser", "msedge.exe"),
            RenderTimeoutSeconds = 2,
        };
        var renderer = new MarkdownPdfRenderer(
            Options.Create(settings), NullLogger<MarkdownPdfRenderer>.Instance);
        var git = new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance);
        return new DeliverableBundleService(
            renderer, git, Options.Create(settings), TimeProvider.System,
            NullLogger<DeliverableBundleService>.Instance);
    }

    private static AgentTask NewTask(string dir, AgentTaskRole role, WorkspaceMode workspace) => new()
    {
        Id = Guid.NewGuid(),
        Title = "Docs task",
        Goal = "Write the spec.",
        Role = role,
        Status = AgentTaskStatus.Succeeded,
        Workspace = workspace,
        WorkingDirectory = dir,
        RepoPath = dir,
        WorktreePath = workspace == WorkspaceMode.Worktree ? dir : null,
    };

    private static void WriteDocs(string root, params string[] names)
    {
        var dir = Path.Combine(root, "docs", "features", "001-kalshi-ref-data-downloader");
        Directory.CreateDirectory(dir);
        foreach (var name in names)
            File.WriteAllText(Path.Combine(dir, name), $"# {name}\n");
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-bundle").FullName;
        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
        }
    }

    private sealed class GitRepo : IDisposable
    {
        public string Path { get; }
        private GitRepo(string path) => Path = path;

        public static async Task<GitRepo> CreateAsync()
        {
            try
            {
                var probe = Process.Start(new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                if (probe is not null)
                    await probe.WaitForExitAsync();
            }
            catch (Exception ex)
            {
                throw new SkipTestException($"git is required for deliverable bundle tests: {ex.Message}");
            }

            var path = Directory.CreateTempSubdirectory("antiphon-bundle-git").FullName;
            var repo = new GitRepo(path);
            await repo.RunAsync("init");
            await repo.RunAsync("config", "user.email", "test@antiphon.dev");
            await repo.RunAsync("config", "user.name", "Antiphon Test");
            await File.WriteAllTextAsync(System.IO.Path.Combine(path, "README.md"), "# repo");
            await repo.RunAsync("add", "README.md");
            await repo.RunAsync("commit", "-m", "init");
            return repo;
        }

        public Task<string> HeadAsync() => RunAsync("rev-parse", "HEAD");

        public async Task<string> RunAsync(params string[] args)
        {
            var start = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = Path,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in args)
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("git failed to start");
            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {stderr}");
            return stdout.Trim();
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (Exception) { }
        }
    }
}
