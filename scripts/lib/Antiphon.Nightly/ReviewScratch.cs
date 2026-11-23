// CARD-1896 and CARD-1981: the owned container for review/master scratch clones, and its removal. It replaced
// scripts/new-review-scratch.ps1 and scripts/remove-review-scratch.ps1 (unit 25 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md); the CLI runs them as `antiphon-scripts new-review-scratch`
// and `antiphon-scripts remove-review-scratch` (scripts/antiphon on sh, scripts\antiphon.cmd on Windows).
//
//   new-review-scratch --task-id <guid> --owner-worktree <dir> [--temp-root <dir>]
//       Creates <temp-root>/antiphon-review-<first 8 hex of the task id> and writes its owner marker
//       .antiphon-scratch-owner (the owner's full path with '/' separators, UTF-8, created new), then prints the
//       container's path. The owner must exist and be a task-<8hex> (matching the task id) or ws-<name> directory directly
//       under a directory named worktrees (the runner's worktree root). An existing container is refused: its ownership is
//       never replaced. Clone into a child (for example master). This never registers an existing tree or grants deletion:
//       the runner's temp sweep (RunnerTempSweep's review-clone family) rechecks ownership, age, processes and live git.
//   remove-review-scratch --root <container> [--temp-root <dir>]
//       Removes one container new-review-scratch created: its name, its parent (the temp root) and its owner marker are
//       required. Every entry is walked without following links first: a symlink or other reparse point anywhere, or a
//       .git that is not directly under a top-level child, is refused before anything is removed. A top-level child whose
//       .git is a directory (a standalone clone) is refused; one whose .git is a file (a linked worktree) is removed with
//       `git worktree remove -- <child>`, never --force, and only when `git worktree list --porcelain` of its repository
//       names that child (paths compared in full form, so git's '/' separators on Windows match). Then the container is
//       removed. Any refusal keeps the container.
//
// The temp root's default is the CLI's (the system temp path). git runs on the host's git seam; files on IFileSystem. Exit
// 0 done; 1 refused (one stderr line, the container kept); usage errors are NightlyUsageException (the CLI's exit 2). This
// does not enable the temp sweep. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The new-review-scratch command's options (new-review-scratch.ps1 -TaskId -OwnerWorktree -TempRoot).</summary>
    public sealed class NewReviewScratchOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "new-review-scratch --task-id <guid> --owner-worktree <dir> [--temp-root <dir>]";

        /// <summary>The task the container serves.</summary>
        public Guid TaskId { get; init; }

        /// <summary>The owning task or workspace-slot worktree, as given.</summary>
        public string OwnerWorktree { get; init; } = "";

        /// <summary>The directory the container is created in.</summary>
        public string TempRoot { get; init; } = "";

        /// <summary>The arguments after the command name; defaultTempRoot is the CLI's temp path.</summary>
        public static NewReviewScratchOptions Parse(IReadOnlyList<string> arguments, string defaultTempRoot)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--task-id", "--owner-worktree", "--temp-root" }, Array.Empty<string>());
            var task = flags.Required("--task-id");
            if (!Guid.TryParse(task, out var id)) throw new NightlyUsageException("--task-id expects a GUID, not '" + task + "'");
            var temp = flags.Value("--temp-root", "");
            return new NewReviewScratchOptions
            {
                TaskId = id,
                OwnerWorktree = flags.Required("--owner-worktree"),
                TempRoot = temp.Length > 0 ? temp : defaultTempRoot ?? "",
            };
        }
    }

    /// <summary>The remove-review-scratch command's options (remove-review-scratch.ps1 -Root -TempRoot).</summary>
    public sealed class RemoveReviewScratchOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "remove-review-scratch --root <container> [--temp-root <dir>]";

        /// <summary>The container, as given.</summary>
        public string Root { get; init; } = "";

        /// <summary>The directory the container must sit directly under.</summary>
        public string TempRoot { get; init; } = "";

        /// <summary>The arguments after the command name; defaultTempRoot is the CLI's temp path.</summary>
        public static RemoveReviewScratchOptions Parse(IReadOnlyList<string> arguments, string defaultTempRoot)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--root", "--temp-root" }, Array.Empty<string>());
            var temp = flags.Value("--temp-root", "");
            return new RemoveReviewScratchOptions
            {
                Root = flags.Required("--root"),
                TempRoot = temp.Length > 0 ? temp : defaultTempRoot ?? "",
            };
        }
    }

    /// <summary>The review scratch container: creation and removal.</summary>
    public static class ReviewScratch
    {
        /// <summary>The container name's prefix (RunnerTempSweep's review-clone family names this file as its producer).</summary>
        public const string Prefix = "antiphon-review-";

        /// <summary>The owner marker's name.</summary>
        public const string Marker = ".antiphon-scratch-owner";

        private static readonly Regex OwnerLeaf = new Regex("^(task-[0-9a-f]{8}|ws-[A-Za-z0-9-]+)$", RegexOptions.CultureInvariant);

        private static readonly Regex ContainerLeaf = new Regex("^antiphon-review-[0-9a-f]{8}$", RegexOptions.CultureInvariant);

        /// <summary>Creates the container and its marker; 0 after its path on host.Out, or 1 after one host.Error line.</summary>
        public static int New(NewReviewScratchOptions options, NightlyHost host, IFileSystem files)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            var owner = files.Path.GetFullPath(options.OwnerWorktree).TrimEnd(files.Path.DirectorySeparatorChar);
            if (!files.Directory.Exists(owner)) return Refuse(host, "Owner worktree must exist.");
            var leaf = files.Path.GetFileName(owner);
            if (!OwnerLeaf.IsMatch(leaf) || files.Path.GetFileName(files.Path.GetDirectoryName(owner)) != "worktrees")
                return Refuse(host, "Owner must be a task or workspace-slot directory under the runner worktrees root.");
            var shortId = options.TaskId.ToString("N").Substring(0, 8);
            if (leaf.StartsWith("task-", StringComparison.Ordinal) && leaf != "task-" + shortId) return Refuse(host, "Task id must match its owner worktree.");
            var root = files.Path.Combine(files.Path.GetFullPath(options.TempRoot), Prefix + shortId);
            if (files.File.Exists(root) || files.Directory.Exists(root)) return Refuse(host, "Scratch container already exists; do not replace its ownership.");
            files.Directory.CreateDirectory(root);
            using (var stream = files.File.Open(files.Path.Combine(root, Marker), FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = Encoding.UTF8.GetBytes(owner.Replace('\\', '/'));
                stream.Write(bytes, 0, bytes.Length);
            }
            host.Out.WriteLine(root);
            return 0;
        }

        /// <summary>Removes the container; 0, or 1 after one host.Error line (the container kept).</summary>
        public static int Remove(RemoveReviewScratchOptions options, NightlyHost host, IFileSystem files)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            var temp = Trimmed(files.Path.GetFullPath(options.TempRoot));
            var full = Trimmed(files.Path.GetFullPath(options.Root));
            if (!ContainerLeaf.IsMatch(files.Path.GetFileName(full))) return Refuse(host, "Not a review scratch container.");
            if (files.Path.GetDirectoryName(full) != temp) return Refuse(host, "Container must sit directly under the temp root.");
            var container = files.DirectoryInfo.New(full);
            // A link is refused whether or not its target is there; a missing path's attributes are all set, so it is
            // asked for only once the directory exists.
            if (container.LinkTarget != null || (container.Exists && (container.Attributes & FileAttributes.ReparsePoint) != 0))
                return Refuse(host, "Refusing scratch container reparse point; container kept.");
            if (!container.Exists) return Refuse(host, "Scratch container does not exist.");

            // Walk every entry without following links. A reparse point, and any .git that is not directly under a top-level
            // child, stops removal before anything is deleted.
            var pending = new Queue<IDirectoryInfo>();
            pending.Enqueue(container);
            while (pending.Count > 0)
            {
                foreach (var entry in pending.Dequeue().EnumerateFileSystemInfos())
                {
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) return Refuse(host, "Refusing reparse point inside the scratch container; container kept.");
                    if (entry.Name == ".git")
                    {
                        var parts = files.Path.GetRelativePath(full, entry.FullName).Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length != 2) return Refuse(host, "Refusing nested git metadata inside the scratch container; container kept.");
                    }
                    if ((entry.Attributes & FileAttributes.Directory) != 0) pending.Enqueue(files.DirectoryInfo.New(entry.FullName));
                }
            }

            var marker = files.Path.Combine(full, Marker);
            if (!files.File.Exists(marker)) return Refuse(host, "Missing scratch owner marker.");

            var prefix = full + files.Path.DirectorySeparatorChar;
            foreach (var child in files.Directory.GetDirectories(full))
            {
                var resolved = Trimmed(files.Path.GetFullPath(child));
                if (!resolved.StartsWith(prefix, StringComparison.Ordinal)) return Refuse(host, "Refusing child outside the scratch container; container kept.");
                var git = files.Path.Combine(child, ".git");
                if (files.Directory.Exists(git)) return Refuse(host, "Refusing standalone clone; container kept.");
                if (!files.File.Exists(git)) continue;
                var refusal = RemoveWorktree(host, files, child, resolved);
                if (refusal != null) return Refuse(host, refusal);
            }
            ClearReadOnly(files, full);
            files.Directory.Delete(full, true);
            return 0;
        }

        // A linked worktree child: removed through its repository, or the reason it was not.
        private static string RemoveWorktree(NightlyHost host, IFileSystem files, string child, string resolved)
        {
            const string Failed = "git worktree remove failed; container kept.";
            var common = host.RunGit(child, "rev-parse", "--path-format=absolute", "--git-common-dir");
            var lines = common.Output.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToArray();
            if (common.ExitCode != 0 || lines.Length != 1) return Failed;
            var main = files.Path.GetDirectoryName(lines[0]);
            if (string.IsNullOrWhiteSpace(main)) return Failed;
            var listing = host.RunGit(main, "worktree", "list", "--porcelain");
            if (listing.ExitCode != 0) return Failed;
            var named = false;
            foreach (var raw in listing.Output.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (!line.StartsWith("worktree ", StringComparison.Ordinal)) continue;
                var listed = line.Substring(9);
                if (listed.Length > 0 && Trimmed(files.Path.GetFullPath(listed)) == resolved) named = true;
            }
            if (!named) return "git worktree list does not name this child; container kept.";
            if (host.RunGit(main, "worktree", "remove", "--", resolved).ExitCode != 0) return Failed;
            return null;
        }

        // Remove-Item -Force removes read-only files; Directory.Delete does not, so they are made writable first. The walk
        // above refused every reparse point, so nothing outside the container is reached.
        private static void ClearReadOnly(IFileSystem files, string directory)
        {
            foreach (var file in files.Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                var attributes = files.File.GetAttributes(file);
                if ((attributes & FileAttributes.ReadOnly) != 0) files.File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
            }
        }

        private static string Trimmed(string path) => path.TrimEnd('\\', '/');

        private static int Refuse(NightlyHost host, string message)
        {
            host.Error.WriteLine(message);
            return 1;
        }
    }
}
