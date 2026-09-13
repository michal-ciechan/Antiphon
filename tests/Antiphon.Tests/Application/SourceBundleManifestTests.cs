using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
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

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public class SourceBundleManifestTests
{
    [Test]
    public async Task Bytes_names_thresholds_and_budget_are_truthful()
    {
        using var workspace = new TempDir();
        DisableNewlineConversion(workspace.Path);
        var utf16 = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("zażółć gęślą jaźń 🎯\r\n")).ToArray();
        var utf8Bom = Encoding.UTF8.GetPreamble().Concat("hello \n"u8.ToArray()).ToArray();
        WriteNamed(workspace.Path, "utf16.md", utf16);
        WriteNamed(workspace.Path, "utf8.md", utf8Bom);

        var task = NewTask(workspace.Path);
        var report = "`docs/features/001-kalshi-ref-data-downloader/utf16.md` `docs/features/001-kalshi-ref-data-downloader/utf8.md`";
        await CreateService().TryBuildAsync(task, report, db: null, CancellationToken.None);

        task.DeliverableFileCount.ShouldBe(2);
        var copiedUtf16 = await File.ReadAllBytesAsync(Path.Combine(task.DeliverableBundleDir!, "utf16.md"));
        copiedUtf16.ShouldBe(utf16);
        var copiedUtf8 = await File.ReadAllBytesAsync(Path.Combine(task.DeliverableBundleDir!, "utf8.md"));
        copiedUtf8.ShouldBe(utf8Bom);
        SourceBundleManifest.Read(Path.Combine(task.DeliverableBundleDir!, SourceBundleManifest.FileName))
            .Members.Count.ShouldBe(2);

        using var six = new TempDir();
        var sixNames = Enumerable.Range(1, 6).Select(i => $"{i:00}.md").ToArray();
        WriteDocs(six.Path, sixNames);
        var sixTask = NewTask(six.Path);
        await CreateService().TryBuildAsync(
            sixTask,
            string.Join(" ", sixNames.Select(n => $"`docs/features/001-kalshi-ref-data-downloader/{n}`")),
            db: null, CancellationToken.None);
        Directory.GetFiles(sixTask.DeliverableBundleDir!, "*.md").ShouldBeEmpty();
        var zip = Directory.GetFiles(sixTask.DeliverableBundleDir!, "*-sources.zip").ShouldHaveSingleItem();
        using (var archive = ZipFile.OpenRead(zip))
            archive.Entries.Count.ShouldBe(6);

        using var fortyOne = new TempDir();
        var names41 = Enumerable.Range(1, 41).Select(i => $"{i:00}.md").ToArray();
        WriteDocs(fortyOne.Path, names41);
        var task41 = NewTask(fortyOne.Path);
        await CreateService().TryBuildAsync(
            task41,
            string.Join(" ", names41.Select(n => $"`docs/features/001-kalshi-ref-data-downloader/{n}`")),
            db: null, CancellationToken.None);
        task41.DeliverableFileCount.ShouldBe(41);
        using (var archive = ZipFile.OpenRead(Directory.GetFiles(task41.DeliverableBundleDir!, "*-sources.zip").Single()))
            archive.Entries.Count.ShouldBe(41);

        using var over = new TempDir();
        var huge = new byte[(64L * 1024 * 1024) + 1];
        "x"u8.CopyTo(huge);
        WriteNamed(over.Path, "huge.md", huge);
        WriteNamed(over.Path, "ok.md", "ok"u8.ToArray());
        var overTask = NewTask(over.Path);
        await CreateService().TryBuildAsync(
            overTask,
            "`docs/features/001-kalshi-ref-data-downloader/huge.md` `docs/features/001-kalshi-ref-data-downloader/ok.md`",
            db: null, CancellationToken.None);
        var manifest = SourceBundleManifest.Read(Path.Combine(overTask.DeliverableBundleDir!, SourceBundleManifest.FileName));
        manifest.Incomplete.ShouldBeTrue();
        manifest.Omissions.ShouldContain(o => o.RelativePath.EndsWith("huge.md", StringComparison.OrdinalIgnoreCase));
        DeliverableBundleService.FormatNoteBit(overTask).ShouldContain("incomplete");
    }

    [Test]
    public async Task Only_authorized_source_members_are_implicit()
    {
        using var workspace = new TempDir();
        WriteDocs(workspace.Path, "ok.md");
        var task = NewTask(workspace.Path);
        await CreateService().TryBuildAsync(
            task, "`docs/features/001-kalshi-ref-data-downloader/ok.md`", db: null, CancellationToken.None);
        File.WriteAllText(Path.Combine(task.DeliverableBundleDir!, "stale.pdf"), "%PDF-");
        File.WriteAllText(Path.Combine(task.DeliverableBundleDir!, "render.log"), "no");
        File.WriteAllText(Path.Combine(task.DeliverableBundleDir!, "extra.md"), "no");
        var implied = DeliverableBundleService.ListAttachableFiles(task);
        implied.Select(Path.GetFileName).ShouldBe(["ok.md"]);

        var legacyDir = Path.Combine(workspace.Path, "legacy");
        Directory.CreateDirectory(legacyDir);
        File.WriteAllText(Path.Combine(legacyDir, "a.md"), "a");
        File.WriteAllText(Path.Combine(legacyDir, "stem-sources.zip"), "z");
        File.WriteAllText(Path.Combine(legacyDir, "old.pdf"), "%PDF-");
        var legacy = new AgentTask { DeliverableBundleDir = legacyDir, DeliverableFileCount = 1 };
        DeliverableBundleService.ListAttachableFiles(legacy).Select(Path.GetFileName)
            .ShouldBe(["a.md", "stem-sources.zip"], ignoreOrder: true);

        File.WriteAllText(Path.Combine(task.DeliverableBundleDir!, SourceBundleManifest.FileName), "{");
        DeliverableBundleService.ListAttachableFiles(task).ShouldBeEmpty();
    }

    private static DeliverableBundleService CreateService() =>
        new(new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance),
            Options.Create(new DeliverablesSettings()),
            NullLogger<DeliverableBundleService>.Instance);

    private static AgentTask NewTask(string dir) => new()
    {
        Id = Guid.NewGuid(),
        Title = "Docs",
        Goal = "Write",
        Role = AgentTaskRole.Docs,
        Status = AgentTaskStatus.Succeeded,
        Workspace = WorkspaceMode.Shared,
        WorkingDirectory = dir,
        RepoPath = dir,
    };

    private static void WriteDocs(string root, params string[] names)
    {
        var dir = Path.Combine(root, "docs", "features", "001-kalshi-ref-data-downloader");
        Directory.CreateDirectory(dir);
        foreach (var name in names)
            File.WriteAllText(Path.Combine(dir, name), $"# {name}\n");
    }

    private static void WriteNamed(string root, string name, byte[] bytes)
    {
        var dir = Path.Combine(root, "docs", "features", "001-kalshi-ref-data-downloader");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, name), bytes);
    }

    private static void DisableNewlineConversion(string root)
    {
        File.WriteAllText(Path.Combine(root, ".gitattributes"), "* -text\n");
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-src-bundle").FullName;
        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
        }
    }
}
