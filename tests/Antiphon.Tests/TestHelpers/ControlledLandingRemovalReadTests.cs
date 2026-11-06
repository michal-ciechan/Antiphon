using Antiphon.Server.Infrastructure.Git;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<GitProcessSpawnLimit>]
public sealed class ControlledLandingRemovalReadTests
{
    [Test]
    public async Task Tracked_index_and_commit_reads_match_real_git_before_and_after_set_aside()
    {
        using var controlled = new ControlledLandingGit();
        using var real = new ScratchGitRepo();
        await real.GitAsync("config", "core.autocrlf", "false");
        foreach (var name in new[] { "keep.txt", "fixture-owner.txt" })
            File.Copy(Path.Combine(controlled.Source, name), Path.Combine(real.Path, name));
        await real.GitAsync("add", ".");
        await real.GitAsync("commit", "-m", "seed");
        var realSeed = (await real.GitReadAsync("rev-parse", "HEAD")).Trim();

        async Task Compare(string directory, params string[] args)
        {
            var result = await controlled.RunAsync(directory, args, default);
            result.Succeeded.ShouldBeTrue(result.Diagnostic);
            result.Output.ShouldBe(await real.GitReadAsync(args), string.Join(' ', args));
            result.WroteStandardError.ShouldBeFalse();
        }

        await Compare(controlled.Source, "ls-files", "-z");
        await Compare(controlled.Source, "ls-files", "--stage", "-z");
        (await controlled.RequiredAsync(controlled.Repository, "ls-tree", "-r", "-z", "--full-tree", controlled.SeedSha))
            .ShouldBe(await real.GitReadAsync("ls-tree", "-r", "-z", "--full-tree", realSeed));

        const string relative = "nested/a space.txt";
        foreach (var directory in new[] { controlled.Source, real.Path })
        {
            Directory.CreateDirectory(Path.Combine(directory, "nested"));
            await File.WriteAllTextAsync(Path.Combine(directory, relative), "staged bytes\n");
            await File.WriteAllTextAsync(Path.Combine(directory, "loose.txt"), "untracked\n");
        }
        await controlled.RequiredAsync(controlled.Source, "add", relative);
        await real.GitAsync("add", relative);
        await File.WriteAllTextAsync(Path.Combine(controlled.Source, relative), "unstaged bytes\n");
        await File.WriteAllTextAsync(Path.Combine(real.Path, relative), "unstaged bytes\n");
        await Compare(controlled.Source, "ls-files", "-z");
        await Compare(controlled.Source, "ls-files", "--stage", "-z");
        // Neither a staged addition nor a later disk edit changes an old commit's tree.
        (await controlled.RequiredAsync(controlled.Repository, "ls-tree", "-r", "-z", "--full-tree", controlled.SeedSha))
            .ShouldBe(await real.GitReadAsync("ls-tree", "-r", "-z", "--full-tree", realSeed));
        await controlled.RequiredAsync(controlled.Source, "commit", "-m", "staged addition");
        await real.GitAsync("commit", "-m", "staged addition");
        var committed = await controlled.RequiredAsync(controlled.Repository, "ls-tree", "-r", "-z", "--full-tree", controlled.SourceHead);
        committed.ShouldBe(await real.GitReadAsync("ls-tree", "-r", "-z", "--full-tree", "HEAD"));
        // Remote-only empty commits inherit their parent's tree, not the stale local target index.
        var remote = controlled.AdvanceRemoteSource();
        await real.GitAsync("commit", "--allow-empty", "-m", "remote-only");
        (await controlled.RequiredAsync(controlled.Repository, "ls-tree", "-r", "-z", "--full-tree", remote)).ShouldBe(committed);
        var absent = await controlled.RunAsync(controlled.Repository,
            ["ls-tree", "-r", "-z", "--full-tree", new string('f', 40)], default);
        absent.Succeeded.ShouldBeFalse();

        var aside = WorktreeSetAside.SetAsidePath(controlled.Source);
        Directory.Move(controlled.Source, aside);
        await Compare(aside, "ls-files", "-z");
        await Compare(aside, "ls-files", "--stage", "-z");
        await Compare(aside, "-c", "core.safecrlf=false", "hash-object", "--", relative, "keep.txt");
    }

    [Test]
    public async Task Hash_object_matches_real_git_for_bytes_batches_and_missing_paths()
    {
        using var controlled = new ControlledLandingGit();
        using var real = new ScratchGitRepo();
        await real.GitAsync("config", "core.autocrlf", "false");
        foreach (var directory in new[] { controlled.Source, real.Path })
        {
            await File.WriteAllBytesAsync(Path.Combine(directory, "binary file"), [0, 255, 128, 13, 10]);
            await File.WriteAllTextAsync(Path.Combine(directory, "text file"), "line\r\n");
        }
        string[][] batches = [["binary file", "text file"], ["text file", "absent"], ["absent"]];
        foreach (var batch in batches)
        {
            string[] args = ["-c", "core.safecrlf=false", "hash-object", "--", .. batch];
            var expected = await ScratchGitRepo.GitInAsync(real.Path, args);
            var actual = await controlled.RunAsync(controlled.Source, args, default);
            actual.Succeeded.ShouldBe(expected.Ok);
            actual.Output.ShouldBe(expected.StdOut);
            actual.WroteStandardError.ShouldBe(!expected.Ok);
        }
        (await controlled.RequiredAsync(controlled.Source, "ls-files", "-z"))
            .ShouldBe("fixture-owner.txt\0keep.txt\0", "hash-object must not stage the files it reads");
    }
}
