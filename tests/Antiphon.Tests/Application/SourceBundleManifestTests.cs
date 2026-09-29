using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class SourceBundleManifestTests
{
    [Test]
    public async Task Bytes_names_thresholds_and_budget_are_truthful()
    {
        var root = Directory.CreateTempSubdirectory("c0418-source-matrix-").FullName;
        try
        {
            var inputs = new Dictionary<string, byte[]>
            {
                ["docs/alpha/same.md"] = Encoding.Unicode.GetPreamble()
                    .Concat(Encoding.Unicode.GetBytes("# Zażółć\r\nend  \r\n")).ToArray(),
                ["docs/beta/same.md"] = Encoding.UTF8.GetPreamble()
                    .Concat(Encoding.UTF8.GetBytes("# emoji ✨\nend\n")).ToArray(),
                ["docs/beta/third.md"] = [0, 10, 13, 255],
                ["docs/beta/fourth.md"] = Encoding.UTF8.GetBytes("# four\n"),
                ["docs/beta/exact.md"] = new byte[1024 * 1024],
                ["docs/beta/sixth.md"] = Encoding.UTF8.GetBytes("# six\n"),
            };
            foreach (var (path, bytes) in inputs)
                await WriteAsync(root, path, bytes);

            var five = await BuildAsync(root, inputs.Keys.Take(5).ToArray());
            var fiveManifest = await ManifestAsync(five);
            fiveManifest.Complete.ShouldBeTrue();
            fiveManifest.Sources.Count.ShouldBe(5);
            fiveManifest.Sources.All(s => s.ZipEntry is null).ShouldBeTrue();
            fiveManifest.Sources.Select(s => s.StoredFile).Distinct(StringComparer.OrdinalIgnoreCase)
                .Count().ShouldBe(5);
            foreach (var source in fiveManifest.Sources)
                await AssertMemberAsync(five, source, inputs[source.OriginalRelativePath]);
            DeliverableBundleService.FormatNoteBit(five).ShouldBe("5 md");

            var six = await BuildAsync(root, inputs.Keys.ToArray());
            var sixManifest = await ManifestAsync(six);
            sixManifest.Sources.Count.ShouldBe(6);
            sixManifest.Sources.Select(s => s.StoredFile).Distinct().Count().ShouldBe(1);
            foreach (var source in sixManifest.Sources)
            {
                source.ZipEntry.ShouldBe(source.OriginalRelativePath);
                await AssertMemberAsync(six, source, inputs[source.OriginalRelativePath]);
            }
            DeliverableBundleService.FormatNoteBit(six).ShouldBe("6 md, sources zip");

            var oversizedPath = "docs/beta/over-one.md";
            var oversizedBytes = new byte[1024 * 1024 + 1];
            await WriteAsync(root, oversizedPath, oversizedBytes);
            var oversized = await BuildAsync(root, [oversizedPath]);
            var oversizedSource = (await ManifestAsync(oversized)).Sources.ShouldHaveSingleItem();
            oversizedSource.ZipEntry.ShouldBe(oversizedPath);
            await AssertMemberAsync(oversized, oversizedSource, oversizedBytes);

            var manyPaths = Enumerable.Range(1, 64).Select(i => $"docs/many/{i:D2}.md").ToArray();
            foreach (var path in manyPaths)
                await WriteAsync(root, path, Encoding.UTF8.GetBytes("# " + path));
            var many = await BuildAsync(root, manyPaths);
            var manyManifest = await ManifestAsync(many);
            manyManifest.Complete.ShouldBeTrue();
            manyManifest.Sources.Select(s => s.OriginalRelativePath).ShouldBe(manyPaths);
            foreach (var source in manyManifest.Sources)
                await AssertMemberAsync(many, source, Encoding.UTF8.GetBytes("# " + source.OriginalRelativePath));

            var exactPath = "docs/budget/exact.md";
            var extraPath = "docs/budget/extra.md";
            await WriteAsync(root, exactPath, new byte[64 * 1024 * 1024]);
            await WriteAsync(root, extraPath, [42]);
            var budget = await BuildAsync(root, [exactPath, extraPath]);
            var budgetManifest = await ManifestAsync(budget);
            budgetManifest.Complete.ShouldBeFalse();
            budgetManifest.Sources.ShouldHaveSingleItem().OriginalRelativePath.ShouldBe(exactPath);
            budgetManifest.Omitted.ShouldHaveSingleItem().OriginalRelativePath.ShouldBe(extraPath);
            budgetManifest.Omitted[0].Length.ShouldBe(1);
            await AssertMemberAsync(budget, budgetManifest.Sources[0], new byte[64 * 1024 * 1024]);
            DeliverableBundleService.FormatNoteBit(budget).ShouldContain("sources incomplete");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    public async Task Only_authorized_source_members_are_implicit()
    {
        var root = Directory.CreateTempSubdirectory("c0418-source-custody-").FullName;
        try
        {
            await WriteAsync(root, "docs/good/one.md", Encoding.UTF8.GetBytes("# one"));
            await WriteAsync(root, "docs/cards/CARD-0001.md", Encoding.UTF8.GetBytes("# generated"));
            await WriteAsync(root, ".antiphon/secret.md", Encoding.UTF8.GetBytes("# excluded"));
            await WriteAsync(root, "outside.md", Encoding.UTF8.GetBytes("# traversal"));
            var task = await BuildAsync(root,
                ["docs/good/one.md", "docs/cards/CARD-0001.md", ".antiphon/secret.md",
                    "docs/../outside.md"]);
            var source = DeliverableBundleService.ListAttachableFiles(task).ShouldHaveSingleItem();
            var bundle = task.DeliverableBundleDir!;
            foreach (var name in new[] { "stale.pdf", "render.html", "render.log", "unlisted.md",
                         "unrelated.zip", "part.tmp" })
                await File.WriteAllTextAsync(Path.Combine(bundle, name), "stale");
            DeliverableBundleService.ListAttachableFiles(task).ShouldBe([source]);

            var manifestPath = Path.Combine(bundle, DeliverableBundleService.SourceManifestName);
            await File.WriteAllTextAsync(manifestPath, "{broken");
            DeliverableBundleService.ListAttachableFiles(task).ShouldBeEmpty();
            await File.WriteAllTextAsync(manifestPath, "{\"version\":2,\"complete\":true,\"sources\":[]}");
            DeliverableBundleService.ListAttachableFiles(task).ShouldBeEmpty();

            File.Delete(manifestPath);
            await File.WriteAllTextAsync(Path.Combine(bundle, "old-sources.zip"), "legacy");
            DeliverableBundleService.ListAttachableFiles(task).Order()
                .ShouldBe(new[] { source, Path.Combine(bundle, "unlisted.md"),
                    Path.Combine(bundle, "old-sources.zip") }.Order());
            task.DeliverablePdfPath = Path.Combine(bundle, "stale.pdf");
            task.DeliverableRenderError = "historical failure";
            File.Exists(task.DeliverablePdfPath).ShouldBeTrue();
            task.DeliverableRenderError.ShouldBe("historical failure");
            DeliverableBundleService.ListAttachableFiles(task).ShouldNotContain(task.DeliverablePdfPath);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static async Task WriteAsync(string root, string relative, byte[] bytes)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, bytes);
    }

    private static async Task<AgentTask> BuildAsync(string root, string[] paths)
    {
        var task = new AgentTask
        {
            Id = Guid.NewGuid(), Title = "source matrix", Goal = "write docs", Role = AgentTaskRole.Docs,
            Status = AgentTaskStatus.Succeeded, Workspace = WorkspaceMode.Shared,
            WorkingDirectory = root, RepoPath = root,
        };
        var service = new DeliverableBundleService(
            new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance),
            Options.Create(new DeliverablesSettings()), NullLogger<DeliverableBundleService>.Instance);
        await service.TryBuildAsync(task, string.Join(' ', paths.Select(p => $"`{p}`")),
            db: null, CancellationToken.None);
        task.DeliverableBundleDir.ShouldNotBeNull();
        return task;
    }

    private static async Task<DeliverableBundleService.SourceManifest> ManifestAsync(AgentTask task) =>
        JsonSerializer.Deserialize<DeliverableBundleService.SourceManifest>(
            await File.ReadAllTextAsync(Path.Combine(task.DeliverableBundleDir!,
                DeliverableBundleService.SourceManifestName)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private static async Task AssertMemberAsync(AgentTask task,
        DeliverableBundleService.SourceMember source, byte[] expected)
    {
        var path = Path.Combine(task.DeliverableBundleDir!, source.StoredFile);
        byte[] actual;
        if (source.ZipEntry is null)
            actual = await File.ReadAllBytesAsync(path);
        else
        {
            using var archive = ZipFile.OpenRead(path);
            var entry = archive.GetEntry(source.ZipEntry)!;
            using var buffer = new MemoryStream();
            await using var input = entry.Open();
            await input.CopyToAsync(buffer);
            actual = buffer.ToArray();
        }
        actual.ShouldBe(expected);
        source.Length.ShouldBe(expected.LongLength);
        source.Sha256.ShouldBe(Convert.ToHexString(SHA256.HashData(expected)).ToLowerInvariant());
    }
}
