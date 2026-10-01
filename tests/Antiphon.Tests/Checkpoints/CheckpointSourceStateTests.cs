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
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointSourceStateTests
{
    [Test]
    public void clean_and_ignored_outputs_match_head()
    {
        using var repo = new GitFixture();
        var reader = new SourceSnapshot();
        var clean = reader.Capture(repo.Root);
        clean.CaptureStatus.ShouldBe("known");
        clean.DirtyFiles.ShouldBe(0);
        clean.Commit.ShouldBe(repo.Git("rev-parse", "HEAD").Trim());
        // Independent framing vector for a clean tree: version byte, HEAD bytes,
        // then three empty records (porcelain, HEAD diff, cached diff).
        using (var framed = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            static void Frame(IncrementalHash hash, byte[] bytes)
            {
                Span<byte> length = stackalloc byte[8];
                BinaryPrimitives.WriteInt64BigEndian(length, bytes.Length);
                hash.AppendData(length);
                hash.AppendData(bytes);
            }
            Frame(framed, [1]);
            Frame(framed, Encoding.UTF8.GetBytes(clean.Commit!));
            Frame(framed, []);
            Frame(framed, []);
            Frame(framed, []);
            clean.Fingerprint.ShouldBe(Convert.ToHexStringLower(framed.GetHashAndReset()), "fixed-vector-parity");
        }

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
        var calls = 0;
        byte[] ChangingGit(string directory, IReadOnlyList<string> args)
        {
            if (args.SequenceEqual(["status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignore-submodules=none"]))
                return ++calls == 1 ? [] : Encoding.UTF8.GetBytes(" M seed.txt\0");
            return args[0] switch
            {
                "rev-parse" when args.Count > 1 && args[1] == "--show-toplevel" => Encoding.UTF8.GetBytes(repo.Root + "\n"),
                "rev-parse" => Encoding.UTF8.GetBytes(repo.Git("rev-parse", "HEAD")),
                _ => [],
            };
        }
        var unstable = new SourceSnapshot(ChangingGit).Capture(repo.Root);
        unstable.CaptureStatus.ShouldBe("unknown", "two-pass-race-is-unknown");
        unstable.ErrorCode.ShouldBe("capture_changed");
        unstable.DirtyFiles.ShouldBeNull();
        new SourceSnapshot((_, _) => throw new IOException("injected"))
            .Capture(repo.Root).ErrorCode.ShouldBe("file_io", "injected-read-failure");
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

        public void Dispose() => Directory.Delete(Root, recursive: true);

        private static string FindProjectRoot()
        {
            var path = new DirectoryInfo(Environment.CurrentDirectory);
            while (path is not null && !File.Exists(Path.Combine(path.FullName, "scripts", "run-checkpoint.ps1")))
                path = path.Parent;
            return path?.FullName ?? throw new DirectoryNotFoundException("checkpoint repository root");
        }
    }
}
