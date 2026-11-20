using System.Diagnostics;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Integration")]
[Category("C835")]
[ParallelLimiter<CheckpointToolSpawnLimit>]
public sealed class CheckpointSourceStateTests
{
    [Test]
    public void git_fixture_cleanup_removes_read_only_contents()
    {
        var root = Path.Combine(Path.GetTempPath(), "c835-cleanup-" + Guid.NewGuid().ToString("N"));
        var nested = Directory.CreateDirectory(Path.Combine(root, "nested")).FullName;
        var file = Path.Combine(nested, "object");
        File.WriteAllText(file, "git object");
        File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly);
        File.SetAttributes(nested, File.GetAttributes(nested) | FileAttributes.ReadOnly);
        try
        {
            GitFixtureCleanup.Delete(root);
            Directory.Exists(root).ShouldBeFalse("read-only-git-objects-and-directory-removed");
        }
        finally { GitFixtureCleanup.Delete(root); }
    }

    [Test]
    public void tool_snapshot_of_a_non_utf8_name_is_unknown_not_stamped_absent()
    {
        // Linux only: a file name byte that is not UTF-8 (NTFS names are UTF-16). libgit2 names it with U+FFFD, a
        // path that does not exist; the snapshot must not certify it as absent (CARD-1608), it declines to git,
        // whose strict decode fails closed.
        if (OperatingSystem.IsWindows())
            Skip.Test("non-UTF-8 file names need a byte-named filesystem");
        using var repo = new GitFixture();
        var before = Directory.GetFiles(repo.Root).Length;
        var touch = new ProcessStartInfo("/bin/sh") { WorkingDirectory = repo.Root, UseShellExecute = false };
        foreach (var argument in new[] { "-c", "printf 'x' > \"$(printf 'bad\\377name')\"" })
            touch.ArgumentList.Add(argument);
        using (var made = Process.Start(touch)!) { made.WaitForExit(); made.ExitCode.ShouldBe(0); }
        Directory.GetFiles(repo.Root).Length.ShouldBe(before + 1, "the byte-named file exists");

        var observation = new SourceSnapshot().Capture(repo.Root);
        observation.CaptureStatus.ShouldBe("unknown", $"a renamed path stamped absent is not a known source ({observation.ErrorCode})");
    }

    [Test]
    public void clean_and_ignored_outputs_match_head()
    {
        using var repo = new GitFixture();
        var reader = new SourceSnapshot();
        var clean = reader.Capture(repo.Root);
        clean.CaptureStatus.ShouldBe("known");
        clean.DirtyFiles.ShouldBe(0);
        clean.Commit.ShouldBe(repo.Git("rev-parse", "HEAD").Trim());
        // Independent framing vector for a clean tree (v2): version byte and HEAD bytes, no listed paths.
        using (var framed = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            Frame(framed, [2]);
            Frame(framed, Encoding.UTF8.GetBytes(clean.Commit!));
            clean.Fingerprint.ShouldBe(Convert.ToHexStringLower(framed.GetHashAndReset()), "fixed-vector-parity");
        }

        // And for one untracked file: path, four empty entry fields, then the work-tree token.
        repo.Write("loose.txt", "loose\n");
        using (var framed = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            Frame(framed, [2]);
            Frame(framed, Encoding.UTF8.GetBytes(clean.Commit!));
            Frame(framed, Encoding.UTF8.GetBytes("loose.txt"));
            for (var i = 0; i < 4; i++) Frame(framed, []);
            Frame(framed, Encoding.UTF8.GetBytes("file:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("loose\n")))));
            reader.Capture(repo.Root).Fingerprint.ShouldBe(Convert.ToHexStringLower(framed.GetHashAndReset()), "fixed-untracked-vector");
        }
        File.Delete(Path.Combine(repo.Root, "loose.txt"));

        repo.Write("bin-c835/generated.txt", "ignored");
        repo.Write("obj/generated.txt", "ignored");
        repo.Write(".antiphon/generated.txt", "ignored");
        reader.Capture(repo.Root).Fingerprint.ShouldBe(clean.Fingerprint, "ignored-output-stability");

        repo.Write("bin-c835/tracked.txt", "changed");
        reader.Capture(repo.Root).DirtyFiles.ShouldBe(1, "tracked-output-must-count");
    }

    [Test]
    public void index_and_worktree_edits_are_dirty()
    {
        using var repo = new GitFixture();
        var reader = new SourceSnapshot();
        var clean = reader.Capture(repo.Root);
        repo.Write("seed.txt", "worktree edit");
        var unstaged = reader.Capture(repo.Root);
        unstaged.DirtyFiles.ShouldBe(1, "unstaged");
        unstaged.Fingerprint.ShouldNotBe(clean.Fingerprint);

        repo.Git("add", "seed.txt");
        var staged = reader.Capture(repo.Root);
        staged.DirtyFiles.ShouldBe(1, "staged");
        staged.Fingerprint.ShouldNotBe(unstaged.Fingerprint, "index-participates");
        repo.Write("seed.txt", "seed\n");
        var restoredWorktree = reader.Capture(repo.Root);
        restoredWorktree.DirtyFiles.ShouldBe(1, "staged-with-worktree-restored");
        restoredWorktree.Fingerprint.ShouldNotBe(clean.Fingerprint);

        // Keep porcelain bytes and the HEAD-to-worktree diff identical while the
        // index-only content changes. This fails if cached diff bytes are omitted.
        repo.Git("restore", "--staged", "--worktree", "seed.txt");
        repo.Write("seed.txt", "index first\n");
        repo.Git("add", "seed.txt");
        repo.Write("seed.txt", "seed\n");
        var firstIndex = reader.Capture(repo.Root);
        var firstStatus = repo.Git("status", "--porcelain=v1", "--", "seed.txt");
        repo.Write("seed.txt", "index second\n");
        repo.Git("add", "seed.txt");
        repo.Write("seed.txt", "seed\n");
        var secondIndex = reader.Capture(repo.Root);
        repo.Git("status", "--porcelain=v1", "--", "seed.txt").ShouldBe(firstStatus);
        secondIndex.DirtyFiles.ShouldBe(firstIndex.DirtyFiles);
        secondIndex.Fingerprint.ShouldNotBe(firstIndex.Fingerprint, "index-only-content-change");

        repo.Git("restore", "--staged", "--worktree", "seed.txt");
        repo.Write("seed.txt", "first edit");
        var first = reader.Capture(repo.Root);
        repo.Write("seed.txt", "second edit");
        var second = reader.Capture(repo.Root);
        second.DirtyFiles.ShouldBe(first.DirtyFiles);
        second.Fingerprint.ShouldNotBe(first.Fingerprint, "same-count-content-change");

        repo.Git("restore", "--staged", "--worktree", "seed.txt");
        File.Delete(Path.Combine(repo.Root, "seed.txt"));
        reader.Capture(repo.Root).DirtyFiles!.Value.ShouldBeGreaterThan(0, "tracked-deletion");
        repo.Git("restore", "--worktree", "seed.txt");
        File.Move(Path.Combine(repo.Root, "seed.txt"), Path.Combine(repo.Root, "renamed.txt"));
        reader.Capture(repo.Root).DirtyFiles!.Value.ShouldBeGreaterThan(0, "rename-or-delete-plus-untracked");
        File.Move(Path.Combine(repo.Root, "renamed.txt"), Path.Combine(repo.Root, "seed.txt"));
        File.WriteAllBytes(Path.Combine(repo.Root, "seed.txt"), [0, 255, 0, 1]);
        reader.Capture(repo.Root).DirtyFiles.ShouldBe(1, "binary-worktree-change");
    }

    [Test]
    public void untracked_contents_and_paths_affect_identity()
    {
        using var repo = new GitFixture();
        var reader = new SourceSnapshot();
        var clean = reader.Capture(repo.Root);
        const string unicode = "sp ace-\u00e9.txt";
        repo.Write(unicode, "one\r\ntwo\r\n");
        var first = reader.Capture(repo.Root);
        first.DirtyFiles.ShouldBe(1, "untracked-count");
        repo.Write(unicode, "two\r\nthree\r\n");
        var changed = reader.Capture(repo.Root);
        changed.DirtyFiles.ShouldBe(1);
        changed.Fingerprint.ShouldNotBe(first.Fingerprint, "untracked-content-digest");
        File.Move(Path.Combine(repo.Root, unicode), Path.Combine(repo.Root, "other.txt"));
        reader.Capture(repo.Root).Fingerprint.ShouldNotBe(changed.Fingerprint, "untracked-path-digest");
        File.Delete(Path.Combine(repo.Root, "other.txt"));
        reader.Capture(repo.Root).Fingerprint.ShouldBe(clean.Fingerprint, "delete-restores-clean");
        if (!OperatingSystem.IsWindows())
        {
            repo.Write("line\nbreak.txt", "newline path");
            reader.Capture(repo.Root).DirtyFiles.ShouldBe(1, "nul-delimited-newline-path");
        }
    }

    [Test]
    public void failed_or_unstable_capture_is_unknown()
    {
        var missing = new SourceSnapshot().Capture(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        missing.CaptureStatus.ShouldBe("unknown", "failed-git-is-unknown");
        missing.DirtyFiles.ShouldBeNull();

        using var repo = new GitFixture();
        var head = repo.Git("rev-parse", "HEAD").Trim();
        var calls = 0;
        SourceRead Changing(string directory) => ++calls == 1
            ? new SourceRead(repo.Root, head, [])
            : new SourceRead(repo.Root, head, [new SourceReadEntry("seed.txt", "100644", head, "100644", head)]);
        var unstable = new SourceSnapshot(Changing).Capture(repo.Root);
        unstable.CaptureStatus.ShouldBe("unknown", "two-pass-race-is-unknown");
        unstable.ErrorCode.ShouldBe("capture_changed");
        unstable.DirtyFiles.ShouldBeNull();
        new SourceSnapshot(_ => throw new IOException("injected"))
            .Capture(repo.Root).ErrorCode.ShouldBe("file_io", "injected-read-failure");
        new SourceSnapshot(_ => throw new SourceReadException("libgit2_failed"))
            .Capture(repo.Root).ErrorCode.ShouldBe("libgit2_failed", "reader-refusal-is-unknown");
        new SourceSnapshot(_ => new SourceRead(repo.Root, "not-a-commit", []))
            .Capture(repo.Root).ErrorCode.ShouldBe("head_invalid", "reader-head-is-checked");
        new SourceSnapshot(_ => new SourceRead(repo.Root, head, [new SourceReadEntry("../outside.txt", null, null, null, null)]))
            .Capture(repo.Root).ErrorCode.ShouldBe("path_escape", "listed-path-stays-inside");

        // A version 4 index is not read in process: libgit2 declines and git reads it (same identity as v2).
        var asVersion2 = new SourceSnapshot().Capture(repo.Root);
        repo.Git("update-index", "--index-version", "4");
        var asVersion4 = new SourceSnapshot().Capture(repo.Root);
        asVersion4.CaptureStatus.ShouldBe("known", "index-version-4-through-git");
        asVersion4.Fingerprint.ShouldBe(asVersion2.Fingerprint, "index-version-does-not-change-identity");
        repo.Git("update-index", "--index-version", "2");

        var noHead = Path.Combine(Path.GetTempPath(), "c835-unborn-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(noHead);
        try
        {
            GitFixture.Run("git", noHead, "init", "-q");
            new SourceSnapshot().Capture(noHead).CaptureStatus.ShouldBe("unknown", "unborn-head");
        }
        finally { GitFixtureCleanup.Delete(noHead); }

        // A gitlink (submodule) entry in the index.
        repo.Git("update-index", "--add", "--cacheinfo", "160000," + head + ",submodule");
        new SourceSnapshot().Capture(repo.Root)
            .ErrorCode.ShouldBe("submodule_unsupported", "unsupported-submodule-is-unknown");
    }

    private static void Frame(IncrementalHash hash, byte[] bytes)
    {
        Span<byte> length = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    [Test]
    public void script_and_tool_snapshots_agree()
    {
        using var repo = new GitFixture();
        var nested = Path.Combine(repo.Root, "nested folder");
        Directory.CreateDirectory(nested);
        var reader = new SourceSnapshot();
        foreach (var kind in new[] { "clean", "tracked", "untracked" })
        {
            if (kind == "tracked") repo.Write("seed.txt", "edited");
            if (kind == "untracked") repo.Write("new-\u00e9.txt", "bytes");
            var tool = reader.Capture(nested);
            tool.CaptureStatus.ShouldBe("known", kind);
            var source = System.IO.Path.Combine(GitFixture.ProjectRoot, "scripts", "lib", "checkpoint-source.ps1");
            var command = ". '" + source.Replace("'", "''") + "'; Get-CheckpointSource -Repository '" +
                nested.Replace("'", "''") + "' | ConvertTo-Json -Compress";
            var result = GitFixture.Run("pwsh", repo.Root, "-NoProfile", "-Command", command);
            using var json = JsonDocument.Parse(result);
            var script = json.RootElement;
            script.GetProperty("captureStatus").GetString().ShouldBe("known", kind);
            script.GetProperty("commit").GetString().ShouldBe(tool.Commit, kind);
            script.GetProperty("dirtyFiles").GetInt32().ShouldBe(tool.DirtyFiles!.Value, kind);
            script.GetProperty("fingerprint").GetString().ShouldBe(tool.Fingerprint, kind);
        }

        repo.Git("restore", "--staged", "--worktree", "seed.txt");
        File.Delete(Path.Combine(repo.Root, "new-\u00e9.txt"));
        var linked = Path.Combine(Path.GetTempPath(), "c835-linked-" + Guid.NewGuid().ToString("N"));
        try
        {
            repo.Git("worktree", "add", "--detach", linked);
            File.Exists(Path.Combine(linked, ".git")).ShouldBeTrue("linked-worktree-git-file");
            var linkedNested = Path.Combine(linked, "nested folder");
            Directory.CreateDirectory(linkedNested);
            var tool = reader.Capture(linkedNested);
            var root = reader.Capture(repo.Root);
            tool.Fingerprint.ShouldBe(root.Fingerprint, "linked-root-equality");
            var source = System.IO.Path.Combine(GitFixture.ProjectRoot, "scripts", "lib", "checkpoint-source.ps1");
            var command = ". '" + source.Replace("'", "''") + "'; Get-CheckpointSource -Repository '" +
                linkedNested.Replace("'", "''") + "' | ConvertTo-Json -Compress";
            using var script = JsonDocument.Parse(GitFixture.Run("pwsh", linked, "-NoProfile", "-Command", command));
            script.RootElement.GetProperty("fingerprint").GetString().ShouldBe(tool.Fingerprint,
                "linked-script-tool-parity");
        }
        finally
        {
            repo.Git("worktree", "remove", "--force", linked);
        }
    }

    [Test]
    [Arguments("rm-cached")]
    [Arguments("directory-to-file")]
    [Arguments("intent-to-add")]
    [Arguments("index-version-4")]
    public void script_and_tool_agree_where_git_status_is_special(string shape)
    {
        using var repo = new GitFixture();
        switch (shape)
        {
            case "rm-cached":
                // git prints the path twice: a staged deletion and an untracked file.
                repo.Git("rm", "-q", "--cached", "seed.txt");
                break;
            case "directory-to-file":
                // HEAD holds a directory where the index now holds a file: git prints mH=000000.
                repo.Write("folder/inner.txt", "inner\n");
                repo.Git("add", "folder/inner.txt");
                repo.Git("commit", "-qm", "folder");
                repo.Git("rm", "-q", "-r", "--cached", "folder");
                Directory.Delete(Path.Combine(repo.Root, "folder"), recursive: true);
                repo.Write("folder", "now a file\n");
                repo.Git("add", "folder");
                break;
            case "intent-to-add":
                // A version 3 index: libgit2 declines and the tool reads through git, as the script does.
                repo.Write("planned.txt", "planned\n");
                repo.Git("add", "-N", "planned.txt");
                break;
            case "index-version-4":
                repo.Write("seed.txt", "edited\n");
                repo.Git("update-index", "--index-version", "4");
                break;
        }
        var tool = new SourceSnapshot().Capture(repo.Root);
        tool.CaptureStatus.ShouldBe("known", shape);
        var source = Path.Combine(GitFixture.ProjectRoot, "scripts", "lib", "checkpoint-source.ps1");
        var command = ". '" + source.Replace("'", "''") + "'; Get-CheckpointSource -Repository '" +
            repo.Root.Replace("'", "''") + "' | ConvertTo-Json -Compress";
        using var json = JsonDocument.Parse(GitFixture.Run("pwsh", repo.Root, "-NoProfile", "-Command", command));
        var script = json.RootElement;
        script.GetProperty("captureStatus").GetString().ShouldBe("known", shape);
        script.GetProperty("dirtyFiles").GetInt32().ShouldBe(tool.DirtyFiles!.Value, shape);
        script.GetProperty("fingerprint").GetString().ShouldBe(tool.Fingerprint, shape);
        if (shape == "rm-cached") tool.DirtyFiles.ShouldBe(2, "a staged deletion and an untracked file");
    }

    [Test]
    public void assume_unchanged_edit_is_unknown_in_both_readers() => HiddenIndexEditIsUnknown("--assume-unchanged", "--no-assume-unchanged");

    [Test]
    public void skip_worktree_edit_is_unknown_in_both_readers() => HiddenIndexEditIsUnknown("--skip-worktree", "--no-skip-worktree");

    private static void HiddenIndexEditIsUnknown(string setFlag, string clearFlag)
    {
        using var repo = new GitFixture();
        var reader = new SourceSnapshot();
        void BothReaders(string status, string? code)
        {
            var tool = reader.Capture(repo.Root);
            tool.CaptureStatus.ShouldBe(status, setFlag == "--assume-unchanged"
                ? "assume-unchanged-tool-hidden" : "skip-worktree-tool-hidden");
        tool.ErrorCode.ShouldBe(code, setFlag == "--assume-unchanged"
            ? "assume-unchanged-tool-hidden" : "skip-worktree-tool-hidden");
            var source = Path.Combine(GitFixture.ProjectRoot, "scripts", "lib", "checkpoint-source.ps1");
            var command = ". '" + source.Replace("'", "''") + "'; Get-CheckpointSource -Repository '" +
                repo.Root.Replace("'", "''") + "' | ConvertTo-Json -Compress";
            using var json = JsonDocument.Parse(GitFixture.Run("pwsh", repo.Root, "-NoProfile", "-Command", command));
            var script = json.RootElement;
            script.GetProperty("captureStatus").GetString().ShouldBe(status, setFlag == "--assume-unchanged"
                ? "assume-unchanged-script-hidden" : "skip-worktree-script-hidden");
            script.GetProperty("errorCode").GetString().ShouldBe(code, setFlag == "--assume-unchanged"
                ? "assume-unchanged-script-hidden" : "skip-worktree-script-hidden");
        }

        BothReaders("known", null); // Positive control for each reader.
        repo.Git("update-index", setFlag, "seed.txt");
        repo.Write("seed.txt", "hidden edit\n");
        repo.Git("status", "--porcelain=v1", "--", "seed.txt").ShouldBeEmpty("git-hides-edit");
        BothReaders("unknown", "indexed_path_hidden");
        repo.Git("update-index", clearFlag, "seed.txt");
        repo.Git("restore", "--worktree", "seed.txt");
        BothReaders("known", null); // Clearing the flag restores certification.
    }

    private sealed class GitFixture : IDisposable
    {
        public static string ProjectRoot { get; } = FindProjectRoot();
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "c835-git-" + Guid.NewGuid().ToString("N"));

        public GitFixture()
        {
            Directory.CreateDirectory(Root);
            Git("init", "-q");
            Git("config", "user.name", "Checkpoint Test");
            Git("config", "user.email", "checkpoint@example.invalid");
            Git("config", "core.autocrlf", "false");
            Write(".gitignore", "bin/\nobj/\nbin-*/\n.antiphon/\n");
            Write("seed.txt", "seed\n");
            Write("bin-c835/tracked.txt", "tracked\n");
            Git("add", ".gitignore", "seed.txt");
            Git("add", "-f", "bin-c835/tracked.txt");
            Git("commit", "-qm", "seed");
        }

        public void Write(string path, string value)
        {
            var full = Path.Combine(Root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, value);
        }

        public string Git(params string[] args) => Run("git", Root, args);

        public static string Run(string fileName, string cwd, params string[] args)
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo(fileName)
            {
                WorkingDirectory = cwd,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30_000)) { process.Kill(true); throw new TimeoutException(fileName); }
            var output = stdout.GetAwaiter().GetResult();
            var error = stderr.GetAwaiter().GetResult();
            if (process.ExitCode != 0) throw new InvalidOperationException(fileName + " exit " + process.ExitCode + ": " + error);
            return output;
        }

        public void Dispose() => GitFixtureCleanup.Delete(Root);

        private static string FindProjectRoot()
        {
            var path = new DirectoryInfo(Environment.CurrentDirectory);
            while (path is not null && !File.Exists(Path.Combine(path.FullName, "scripts", "run-checkpoint.ps1")))
                path = path.Parent;
            return path?.FullName ?? throw new DirectoryNotFoundException("checkpoint repository root");
        }
    }
}
