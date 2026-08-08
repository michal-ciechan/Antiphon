using System.Diagnostics;
using Antiphon.Server.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// The "Ignore in git" dialog previews a candidate .gitignore line before writing it. The preview
/// has to agree with git exactly — a dialog that lists files git would not actually hide is worse
/// than no dialog — so these run against a REAL repository on disk rather than a fake matcher.
///
/// The scenario throughout is the one that prompted the feature: bin-check/ build output appearing
/// in the file view in two different projects.
/// </summary>
[Category("Integration")]
public class GitIgnorePreviewTests
{
    [Test]
    public async Task A_bare_name_hides_that_folder_everywhere()
    {
        var repo = await NewRepoAsync();
        try
        {
            Write(repo, "server/bin-check/Antiphon.Server.dll", "binary");
            Write(repo, "src/Agents/bin-check/Agents.dll", "binary");
            Write(repo, "server/Program.cs", "code");

            var sets = await Service().PreviewIgnoreAsync(repo, "bin-check/", CancellationToken.None);

            sets.Disappears.ShouldBe(
                ["server/bin-check/Antiphon.Server.dll", "src/Agents/bin-check/Agents.dll"],
                ignoreOrder: true);
            sets.Disappears.ShouldNotContain("server/Program.cs");
            sets.TrackedMatches.ShouldBeEmpty();
        }
        finally
        {
            Cleanup(repo);
        }
    }

    [Test]
    public async Task An_anchored_path_hides_only_that_one()
    {
        var repo = await NewRepoAsync();
        try
        {
            Write(repo, "server/bin-check/Antiphon.Server.dll", "binary");
            Write(repo, "src/Agents/bin-check/Agents.dll", "binary");

            var sets = await Service().PreviewIgnoreAsync(repo, "/server/bin-check/", CancellationToken.None);

            sets.Disappears.ShouldBe(["server/bin-check/Antiphon.Server.dll"]);
        }
        finally
        {
            Cleanup(repo);
        }
    }

    /// <summary>
    /// Why the "only this one" scope writes a LEADING slash. Verified against git 2026-08-09:
    /// a pattern containing a slash anywhere but the end is already anchored, so the two scopes
    /// only diverge for a name at the repo root — and there they diverge completely.
    /// <code>
    ///   bin-check/         -> bin-check/a.dll AND nested/bin-check/b.dll
    ///   /bin-check/        -> bin-check/a.dll only
    ///   nested/bin-check/  -> nested/bin-check/b.dll only (self-anchored by its slash)
    /// </code>
    /// </summary>
    [Test]
    public async Task A_leading_slash_is_what_pins_a_root_level_name_to_the_root()
    {
        var repo = await NewRepoAsync();
        try
        {
            Write(repo, "bin-check/a.dll", "binary");
            Write(repo, "nested/bin-check/b.dll", "binary");
            var service = Service();

            // "Anywhere with this name" — no slash, so git matches the basename at every level.
            var anywhere = await service.PreviewIgnoreAsync(repo, "bin-check/", CancellationToken.None);
            anywhere.Disappears.ShouldBe(
                ["bin-check/a.dll", "nested/bin-check/b.dll"], ignoreOrder: true);

            // "Only this one" — the leading slash is load-bearing here.
            var rootOnly = await service.PreviewIgnoreAsync(repo, "/bin-check/", CancellationToken.None);
            rootOnly.Disappears.ShouldBe(["bin-check/a.dll"]);

            // A multi-segment path is anchored by its own slash; the leading one is belt-and-braces.
            var nested = await service.PreviewIgnoreAsync(repo, "/nested/bin-check/", CancellationToken.None);
            nested.Disappears.ShouldBe(["nested/bin-check/b.dll"]);
        }
        finally
        {
            Cleanup(repo);
        }
    }

    /// <summary>
    /// A folder pattern ends with "/" so it cannot swallow a FILE of the same name.
    /// </summary>
    [Test]
    public async Task A_trailing_slash_matches_only_directories()
    {
        var repo = await NewRepoAsync();
        try
        {
            Write(repo, "bin-check/out.dll", "binary");
            Write(repo, "docs/bin-check", "a file that happens to share the name");

            var dirOnly = await Service().PreviewIgnoreAsync(repo, "bin-check/", CancellationToken.None);
            dirOnly.Disappears.ShouldBe(["bin-check/out.dll"]);

            var either = await Service().PreviewIgnoreAsync(repo, "bin-check", CancellationToken.None);
            either.Disappears.ShouldBe(["bin-check/out.dll", "docs/bin-check"], ignoreOrder: true);
        }
        finally
        {
            Cleanup(repo);
        }
    }

    /// <summary>
    /// Gitignore does not apply to tracked files. Reporting them as "would be hidden" would promise
    /// something git will not do, so they come back in their own bucket for the dialog to warn on.
    /// </summary>
    [Test]
    public async Task A_tracked_file_is_reported_separately_and_does_not_disappear()
    {
        var repo = await NewRepoAsync();
        try
        {
            Write(repo, "docs/notes.md", "committed");
            await GitAsync(repo, "add", "-A");
            await CommitAsync(repo, "seed");
            Write(repo, "scratch/notes.md", "untracked");

            var sets = await Service().PreviewIgnoreAsync(repo, "notes.md", CancellationToken.None);

            sets.Disappears.ShouldBe(["scratch/notes.md"]);
            sets.TrackedMatches.ShouldBe(["docs/notes.md"]);
        }
        finally
        {
            Cleanup(repo);
        }
    }

    [Test]
    public async Task An_empty_pattern_matches_nothing()
    {
        var repo = await NewRepoAsync();
        try
        {
            Write(repo, "server/bin-check/a.dll", "binary");

            var sets = await Service().PreviewIgnoreAsync(repo, "   ", CancellationToken.None);

            sets.Disappears.ShouldBeEmpty();
            sets.TrackedMatches.ShouldBeEmpty();
        }
        finally
        {
            Cleanup(repo);
        }
    }

    /// <summary>The preview must not touch the repository — it runs before the user confirms.</summary>
    [Test]
    public async Task Previewing_never_writes_a_gitignore()
    {
        var repo = await NewRepoAsync();
        try
        {
            Write(repo, "server/bin-check/a.dll", "binary");

            await Service().PreviewIgnoreAsync(repo, "bin-check/", CancellationToken.None);

            File.Exists(Path.Combine(repo, ".gitignore")).ShouldBeFalse();
        }
        finally
        {
            Cleanup(repo);
        }
    }

    // ── Writing ────────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task Adding_the_line_actually_hides_the_files()
    {
        var repo = await NewRepoAsync();
        try
        {
            Write(repo, "server/bin-check/a.dll", "binary");
            Write(repo, "server/Program.cs", "code");
            var service = Service();

            var written = await service.AppendIgnoreAsync(repo, "bin-check/", CancellationToken.None);

            written.ShouldBe(Path.Combine(repo, ".gitignore"));
            (await File.ReadAllTextAsync(written!)).ShouldContain("bin-check/");
            var listed = await service.ListFilesAsync(repo, CancellationToken.None);
            listed.ShouldContain("server/Program.cs");
            listed.ShouldNotContain("server/bin-check/a.dll");
        }
        finally
        {
            Cleanup(repo);
        }
    }

    [Test]
    public async Task Adding_the_same_line_twice_does_not_duplicate_it()
    {
        var repo = await NewRepoAsync();
        try
        {
            var service = Service();
            await service.AppendIgnoreAsync(repo, "bin-check/", CancellationToken.None);
            await service.AppendIgnoreAsync(repo, "bin-check/", CancellationToken.None);

            var lines = await File.ReadAllLinesAsync(Path.Combine(repo, ".gitignore"));
            lines.Count(l => l.Trim() == "bin-check/").ShouldBe(1);
        }
        finally
        {
            Cleanup(repo);
        }
    }

    /// <summary>An existing file without a trailing newline must not get its last line glued to.</summary>
    [Test]
    public async Task Appending_to_a_file_with_no_trailing_newline_keeps_the_lines_separate()
    {
        var repo = await NewRepoAsync();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(repo, ".gitignore"), "node_modules/");

            await Service().AppendIgnoreAsync(repo, "bin-check/", CancellationToken.None);

            var lines = await File.ReadAllLinesAsync(Path.Combine(repo, ".gitignore"));
            lines.ShouldContain("node_modules/");
            lines.ShouldContain("bin-check/");
        }
        finally
        {
            Cleanup(repo);
        }
    }

    [Test]
    public async Task A_directory_that_is_not_a_repository_is_left_alone()
    {
        var plain = Path.Combine(Path.GetTempPath(), $"antiphon-ignore-plain-{Guid.NewGuid():N}");
        Directory.CreateDirectory(plain);
        try
        {
            (await Service().AppendIgnoreAsync(plain, "bin-check/", CancellationToken.None)).ShouldBeNull();
            File.Exists(Path.Combine(plain, ".gitignore")).ShouldBeFalse();
        }
        finally
        {
            Cleanup(plain);
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────

    private static GitWorkspaceService Service() => new(NullLogger<GitWorkspaceService>.Instance);

    private static async Task<string> NewRepoAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"antiphon-ignore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        await GitAsync(root, "init", "-q");
        return root;
    }

    private static void Write(string root, string relativePath, string content)
    {
        var full = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private static Task CommitAsync(string root, string message) =>
        GitAsync(root,
            "-c", "user.email=tests@antiphon.local", "-c", "user.name=Antiphon Tests",
            "commit", "-q", "-m", message);

    private static async Task GitAsync(string workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var process = Process.Start(psi)!;
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {stderr}");
    }

    private static void Cleanup(string root)
    {
        try
        {
            if (!Directory.Exists(root))
                return;
            // git marks objects read-only; clear it or the delete fails on Windows.
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, recursive: true);
        }
        catch
        {
            // Best-effort temp cleanup.
        }
    }
}
