using System.Security.Cryptography;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// <see cref="GitTreeTemplate"/>: a clone must be what a fresh build with the same token values would be (same
/// trees, refs, worktree registrations, remotes, clean status, valid objects), from each template source
/// (built once, a folder, an archive), and must never write to its template.
/// </summary>
[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
public sealed class GitTreeTemplateTests
{
    [Test]
    public async Task Clone_matches_a_fresh_build_with_the_same_task_id()
    {
        var taskId = Guid.NewGuid();
        await using var fresh = new LandingGitFixture(taskId: taskId);
        await fresh.BuildAsync();
        var template = await LandingGitFixture.TemplateAsync();
        var before = DigestTree(template.Directory);
        await using var cloned = new LandingGitFixture(taskId: taskId);
        var clone = template.CloneTo(cloned.Root, Tokens(taskId));
        cloned.SeedSha = clone.Map(template.Values["seed"]);

        await AssertEquivalentAsync(fresh, cloned);

        // Mutating the clone (a commit, a ref, a gc that repacks objects) never reaches the template.
        await File.WriteAllTextAsync(Path.Combine(cloned.Repository, "later.txt"), "later\n");
        await cloned.RequiredAsync(cloned.Repository, "add", "later.txt");
        await cloned.RequiredAsync(cloned.Repository, "commit", "-m", "later");
        await cloned.RequiredAsync(cloned.Repository, "gc", "--quiet");
        DigestTree(template.Directory).ShouldBe(before, "a clone must not write to its template");
    }

    [Test]
    public async Task Clone_from_an_archive_and_from_a_folder_matches_a_fresh_build()
    {
        var template = await LandingGitFixture.TemplateAsync();
        var scratch = Path.Combine(Path.GetTempPath(), "antiphon-c448-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var archive = Path.Combine(scratch, "template.zip");
            template.SaveArchive(archive);
            var fromArchive = GitTreeTemplate.FromArchive(archive);
            fromArchive.WrittenRoot.ShouldBe(template.WrittenRoot);
            GitTreeTemplate.FromArchive(archive).Directory.ShouldBe(fromArchive.Directory, "an archive is extracted once");

            // A folder copy (here: the extracted archive copied elsewhere) is a template too.
            var folder = Path.Combine(scratch, "folder");
            CopyDirectory(fromArchive.Directory, folder);
            var fromFolder = GitTreeTemplate.FromDirectory(folder);

            foreach (var source in new[] { fromArchive, fromFolder })
            {
                var taskId = Guid.NewGuid();
                await using var fresh = new LandingGitFixture(taskId: taskId);
                await fresh.BuildAsync();
                await using var cloned = new LandingGitFixture(taskId: taskId);
                var clone = source.CloneTo(cloned.Root, Tokens(taskId));
                cloned.SeedSha = clone.Map(source.Values["seed"]);
                await AssertEquivalentAsync(fresh, cloned);
            }
        }
        finally
        {
            GitTreeTemplate.DeleteTree(scratch);
        }
    }

    [Test]
    public async Task Clone_of_a_template_with_packed_objects_matches_a_fresh_build()
    {
        // Packed objects take the libgit2 rewrite path instead of the loose byte-level one.
        var template = await GitTreeTemplate.BuildOnceAsync("landing-git-fixture-packed", ["task"], async (root, tokens) =>
        {
            var builder = new LandingGitFixture(root, Guid.ParseExact(tokens["task"], "N"));
            await builder.BuildAsync();
            foreach (var repository in new[] { builder.Repository, builder.Remote, builder.Observer })
                await builder.RequiredAsync(repository, "gc", "--quiet");
            return new() { ["seed"] = builder.SeedSha };
        });
        Directory.EnumerateFiles(Path.Combine(template.Directory, "remote.git", "objects", "pack"), "*.pack").ShouldNotBeEmpty();
        var taskId = Guid.NewGuid();
        await using var fresh = new LandingGitFixture(taskId: taskId);
        await fresh.BuildAsync();
        await using var cloned = new LandingGitFixture(taskId: taskId);
        var clone = template.CloneTo(cloned.Root, Tokens(taskId));
        cloned.SeedSha = clone.Map(template.Values["seed"]);
        await AssertEquivalentAsync(fresh, cloned);
    }

    [Test]
    public async Task Clones_are_independent_of_each_other()
    {
        await using var first = new LandingGitFixture();
        await using var second = new LandingGitFixture();
        await first.InitializeAsync();
        await second.InitializeAsync();
        first.SeedSha.ShouldNotBe(second.SeedSha, "each task id is in its seed commit");
        await File.WriteAllTextAsync(Path.Combine(first.Repository, "only-first.txt"), "x\n");
        await first.RequiredAsync(first.Repository, "add", "only-first.txt");
        await first.RequiredAsync(first.Repository, "commit", "-m", "first only");
        await first.RequiredAsync(first.Repository, "push", "origin", "HEAD:refs/heads/master");
        (await second.RequiredAsync(second.Remote, "rev-parse", "refs/heads/master")).Trim().ShouldBe(second.SeedSha);
        (await second.RequiredAsync(second.Repository, "status", "--porcelain")).ShouldBeEmpty();
    }

    [Test]
    public async Task Clone_refuses_unknown_tokens_and_length_changes()
    {
        var template = await LandingGitFixture.TemplateAsync();
        var root = Path.Combine(Path.GetTempPath(), "antiphon-c448-" + Guid.NewGuid().ToString("N"));
        try
        {
            Should.Throw<ArgumentException>(() => template.CloneTo(root, new Dictionary<string, string> { ["task"] = "short" }));
            Should.Throw<ArgumentException>(() => template.CloneTo(root, new Dictionary<string, string> { ["other"] = Guid.NewGuid().ToString("N") }));
        }
        finally
        {
            GitTreeTemplate.DeleteTree(root);
        }
    }

    private static Dictionary<string, string> Tokens(Guid taskId) => new() { ["task"] = taskId.ToString("N") };

    private static async Task AssertEquivalentAsync(LandingGitFixture fresh, LandingGitFixture cloned)
    {
        // Same refs, pointing at the same trees, in every repository; the seed is the clone's master.
        foreach (var (a, b) in new[] { (fresh.Repository, cloned.Repository), (fresh.Remote, cloned.Remote), (fresh.Observer, cloned.Observer) })
        {
            var freshRefs = await RefTreesAsync(fresh, a);
            var clonedRefs = await RefTreesAsync(cloned, b);
            clonedRefs.ShouldBe(freshRefs, $"refs and trees of {Path.GetFileName(b)}");
            (await cloned.RequiredAsync(b, "fsck", "--full", "--strict", "--no-dangling")).ShouldNotContain("error");
        }
        (await cloned.RequiredAsync(cloned.Remote, "rev-parse", "refs/heads/master")).Trim().ShouldBe(cloned.SeedSha);
        (await cloned.RequiredAsync(cloned.Remote, "rev-parse", cloned.SourceRef)).Trim().ShouldBe(cloned.SeedSha);

        // The task id is in the committed sentinel and the source branch, as a fresh build writes them.
        foreach (var dir in new[] { cloned.Repository, cloned.Source })
            (await File.ReadAllTextAsync(Path.Combine(dir, "fixture-owner.txt"))).Trim().ShouldBe(cloned.TaskId.ToString("N"));
        (await cloned.RequiredAsync(cloned.Source, "symbolic-ref", "HEAD")).Trim().ShouldBe(cloned.SourceRef);

        // Worktree registrations, gitfiles and remotes point into the clone's own root.
        Normalize(await fresh.RequiredAsync(fresh.Repository, "worktree", "list", "--porcelain"), fresh.Root)
            .ShouldBe(Normalize(await cloned.RequiredAsync(cloned.Repository, "worktree", "list", "--porcelain"), cloned.Root));
        SamePath((await cloned.RequiredAsync(cloned.Repository, "config", "--get", "remote.origin.url")).Trim(), cloned.Remote).ShouldBeTrue();
        SamePath((await cloned.RequiredAsync(cloned.Observer, "config", "--get", "remote.origin.url")).Trim(), cloned.Remote).ShouldBeTrue();
        (await cloned.RequiredAsync(cloned.Source, "rev-parse", "--absolute-git-dir")).Trim().Replace('/', '\\')
            .ShouldStartWith(cloned.Root.Replace('/', '\\'), Case.Insensitive);

        // Clean, and (on Windows, where the copied stat data is trusted) a status does not rewrite the index.
        foreach (var dir in new[] { cloned.Repository, cloned.Source, cloned.Observer })
        {
            var index = Path.Combine((await cloned.RequiredAsync(dir, "rev-parse", "--absolute-git-dir")).Trim(), "index");
            var before = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(index)));
            (await cloned.RequiredAsync(dir, "status", "--porcelain", "--untracked-files=all")).ShouldBeEmpty();
            if (OperatingSystem.IsWindows())
                Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(index))).ShouldBe(before,
                    $"status must not need to refresh the cloned index of {dir}");
        }
    }

    private static async Task<string> RefTreesAsync(LandingGitFixture fixture, string repository)
    {
        var refs = await fixture.RequiredAsync(repository, "for-each-ref", "--format=%(refname) %(objectname)");
        var lines = new List<string>();
        foreach (var line in refs.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var name = line.Split(' ')[0].Replace(fixture.TaskId.ToString("N"), "<task>");
            var tree = (await fixture.RequiredAsync(repository, "rev-parse", line.Split(' ')[1] + "^{tree}")).Trim();
            lines.Add(name + " " + tree);
        }
        return string.Join("\n", lines.Order(StringComparer.Ordinal));
    }

    private static string Normalize(string text, string root) =>
        text.Replace('\\', '/').Replace(root.Replace('\\', '/'), "<root>", StringComparison.OrdinalIgnoreCase)
            .Split('\n').Where(l => !l.StartsWith("HEAD ", StringComparison.Ordinal)).Aggregate("", (a, l) => a + l + "\n");

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    private static string DigestTree(string root)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes(Path.GetRelativePath(root, file)));
            hash.AppendData(File.ReadAllBytes(file));
            hash.AppendData(BitConverter.GetBytes(File.GetLastWriteTimeUtc(file).Ticks));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void CopyDirectory(string from, string to)
    {
        foreach (var dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories).Prepend(from))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            File.Copy(file, target);
            File.SetCreationTimeUtc(target, File.GetCreationTimeUtc(file));
            File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(file));
        }
    }
}
