// CARD-0824 S2: prepare the Windows desktop hot-slot root (unit 51 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md,
// W8 slice C; it replaced scripts/desktop-slot-provision.ps1, which nothing started by path). The CLI runs it as
//
//   antiphon-scripts desktop-slot-provision [--root C:\Antiphon\slots] [--execute] [--what-if] [--remove-empty] [--disk-budget-gib 32]
//
// The default is a dry run. --execute is what changes the disk; --what-if forces a dry run even with --execute.
//
// It creates worktrees, workspace-slots, retained and repos under the root. It does not clone, does not create ws-NN, does not
// write ledger.json and does not run dotnet. When repos/antiphon is already a git repository it sets local core.longpaths=true
// on that clone only (git resolves the clone with GIT_CEILING_DIRECTORIES at its parent, and must answer exactly the clone and
// its .git) and reads the value back; otherwise it prints the clone and config commands. --remove-empty removes the four
// directories when they are all empty and workspace-slots/ledger.json is absent.
//
// Refused before anything (exit 2): a filesystem root, a bare drive root, UNC, \\?\ and \\.\ paths, a segment ending in '.' or
// a space or containing '~', a reparse-point ancestor, a root under or containing C:\src\Antiphon, C:\Antiphon\releases or
// C:\Antiphon\worktrees (or whose children would be), a root with an ancestor named releases, a drive that does not exist, and
// free space below --disk-budget-gib (default 32; below 1 is refused). 8.3 expansion and NTFS junctions are not resolved here.
//
// The lines are the script's, printed when it ends; exit 0 done (or the dry run), 2 refused; usage 2 (the CLI). The disk is
// IFileSystem's (attributes, the drive's free space, the directories), git the tool seam's; the free space and a hook before
// each removal are test seams. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The desktop-slot-provision command's options (desktop-slot-provision.ps1's parameters in kebab case).</summary>
    public sealed class DesktopSlotProvisionOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "desktop-slot-provision [--root C:\\Antiphon\\slots] [--execute] [--what-if] [--remove-empty] [--disk-budget-gib 32]";

        /// <summary>The script's default root.</summary>
        public const string DefaultRoot = "C:\\Antiphon\\slots";

        /// <summary>The slot root.</summary>
        public string Root { get; init; } = DefaultRoot;

        /// <summary>Change the disk (otherwise a dry run).</summary>
        public bool Execute { get; init; }

        /// <summary>A dry run even with Execute.</summary>
        public bool WhatIf { get; init; }

        /// <summary>Remove the four empty directories instead of creating them.</summary>
        public bool RemoveEmpty { get; init; }

        /// <summary>The free space the root's drive must have, in GiB.</summary>
        public int DiskBudgetGiB { get; init; } = 32;

        /// <summary>The flags after the command name.</summary>
        public static DesktopSlotProvisionOptions Parse(IReadOnlyList<string> arguments)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--root", "--disk-budget-gib" }, new[] { "--execute", "--what-if", "--remove-empty" });
            return new DesktopSlotProvisionOptions
            {
                Root = flags.Value("--root", DefaultRoot) ?? "",
                Execute = flags.Switch("--execute"),
                WhatIf = flags.Switch("--what-if"),
                RemoveEmpty = flags.Switch("--remove-empty"),
                DiskBudgetGiB = flags.Int("--disk-budget-gib", 32),
            };
        }
    }

    /// <summary>What desktop-slot-provision reaches outside its disk: git, and the test seams. The CLI binds the production ones.</summary>
    public sealed class DesktopSlotSeams
    {
        /// <summary>git (Find and Run).</summary>
        public IOperatorToolSeams Tools { get; init; }

        /// <summary>Split-Path's platform: Windows (backslashes) or not.</summary>
        public bool Windows { get; init; } = OperatingSystem.IsWindows();

        /// <summary>The script's -FreeSpaceProvider: the free bytes for the root; null reads the drive (DesktopSlotProvision.DriveFreeBytes).</summary>
        public Func<string, long> FreeBytes { get; init; }

        /// <summary>The script's -BeforeRemove test hook: called with each directory before its removal; null in production.</summary>
        public Action<string> BeforeRemove { get; init; }
    }

    /// <summary>The provision's verdict: the exit code and the lines, in order.</summary>
    public sealed record DesktopSlotResult(int ExitCode, IReadOnlyList<string> Lines);

    /// <summary>The desktop hot-slot root (desktop-slot-provision.ps1's body; see the file comment).</summary>
    public static class DesktopSlotProvision
    {
        /// <summary>Done (or the dry run).</summary>
        public const int Done = 0;

        /// <summary>Refused.</summary>
        public const int Refused = 2;

        /// <summary>The repository a dry run tells the operator to clone.</summary>
        public const string CloneUrl = "https://github.com/michal-ciechan/Antiphon";

        /// <summary>The four directories, in the script's order.</summary>
        public static readonly IReadOnlyList<string> Children = new[] { "worktrees", "workspace-slots", "retained", "repos" };

        private static readonly string[] ForbiddenPrefixes = { "C:\\src\\Antiphon", "C:\\Antiphon\\releases", "C:\\Antiphon\\worktrees" };
        private static readonly Regex DriveOnly = new Regex("^[A-Za-z]:$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex DrivePrefix = new Regex("^[A-Za-z]:", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex DriveRooted = new Regex("^[A-Za-z]:\\\\", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex DriveRoot = new Regex("^[A-Za-z]:\\\\$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex DriveRootLoose = new Regex("^[A-Za-z]:[\\\\/]?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>The provision, its lines printed (Write-Host) when it ends; the exit code.</summary>
        public static int Run(DesktopSlotProvisionOptions options, NightlyHost host, IFileSystem files, DesktopSlotSeams seams)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            var result = Provision(options, host, files, seams);
            foreach (var line in result.Lines) host.Out.WriteLine(line);
            return result.ExitCode;
        }

        /// <summary>Invoke-DesktopSlotProvision: the verdict and its lines. A failed directory creation or listing is a stderr line (it was a non-terminating error).</summary>
        public static DesktopSlotResult Provision(DesktopSlotProvisionOptions options, NightlyHost host, IFileSystem files, DesktopSlotSeams seams)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (seams == null) throw new ArgumentNullException(nameof(seams));
            var lines = new List<string>();
            var root = options.Root ?? "";
            if (string.IsNullOrWhiteSpace(root)) return Refuse(lines, "refused: filesystem-root");
            var textReason = TextRefusal(root);
            if (textReason == "unsupported-root") return Refuse(lines, "refused: unsupported-root " + root.Trim());
            var comparable = SlotPath(root);
            if (textReason.Length > 0) return Refuse(lines, "refused: " + textReason + " " + comparable);
            if (ReparseAncestor(files, comparable, seams.Windows)) return Refuse(lines, "refused: reparse-ancestor " + comparable);
            if (!RootCreatable(files, comparable)) return Refuse(lines, "refused: root-unavailable " + comparable);

            var mutating = options.Execute && !options.WhatIf;
            lines.Add("mode=" + (mutating ? "execute" : "dry-run"));
            lines.Add("root=" + comparable);

            if (options.RemoveEmpty) return RemoveEmpty(host, files, seams, comparable, mutating, lines);

            if (options.DiskBudgetGiB < 1) return Refuse(lines, "refused: disk-budget");
            const long oneGiB = 1024L * 1024 * 1024;
            var budgetBytes = options.DiskBudgetGiB * oneGiB;
            var free = seams.FreeBytes != null ? seams.FreeBytes(comparable) : DriveFreeBytes(files, comparable, seams.Windows);
            lines.Add("free-bytes=" + free.ToString(CultureInfo.InvariantCulture));
            lines.Add("budget-bytes=" + budgetBytes.ToString(CultureInfo.InvariantCulture));
            if (free < 0) return Refuse(lines, "refused: free-space-unknown");
            if (free < budgetBytes)
                return Refuse(lines, "refused: low-free-space free=" + free.ToString(CultureInfo.InvariantCulture) + " budget=" + budgetBytes.ToString(CultureInfo.InvariantCulture));

            foreach (var name in Children)
            {
                var path = JoinChild(comparable, name);
                if (!mutating)
                {
                    lines.Add("would-create: " + path);
                    continue;
                }
                if (Exists(files, path))
                {
                    lines.Add("unchanged: " + path);
                    continue;
                }
                // New-Item -ItemType Directory: a failure was a non-terminating error, and the script went on.
                try { files.Directory.CreateDirectory(path); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is NotSupportedException || e is ArgumentException)
                {
                    host.Error.WriteLine(e.Message);
                }
                lines.Add("created: " + path);
            }

            var clone = JoinChild(JoinChild(comparable, "repos"), "antiphon");
            var code = ClonePlan(files, seams, clone, lines, mutating);
            return new DesktopSlotResult(code, lines);
        }

        /// <summary>Get-SlotTextRefusal: the reason the root's text alone refuses ("" when none).</summary>
        public static string TextRefusal(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "filesystem-root";
            var text = path.Trim();
            var slash = text.Replace('/', '\\');
            if (slash.StartsWith("\\\\", StringComparison.CurrentCulture)) return "unsupported-root";
            string comparable;
            if (DrivePrefix.IsMatch(text))
            {
                if (DriveAlias(text)) return "alias-segment";
                if (DriveOnly.IsMatch(text)) text += "\\";
                comparable = DriveSlotPath(text);
            }
            else
            {
                comparable = Path.GetFullPath(text);
                if (UnixAlias(comparable)) return "alias-segment";
            }
            if (FilesystemRoot(comparable)) return "filesystem-root";
            if (ForbiddenRoot(comparable)) return "forbidden-root";
            if (ForbiddenChild(comparable)) return "forbidden-root";
            return "";
        }

        /// <summary>ConvertTo-SlotPath: a drive path normalised by its text (slashes, '.', '..'), anything else Path.GetFullPath.</summary>
        public static string SlotPath(string path)
        {
            var text = (path ?? "").Trim();
            if (DriveOnly.IsMatch(text)) text += "\\";
            if (DrivePrefix.IsMatch(text)) return DriveSlotPath(text);
            return Path.GetFullPath(text);
        }

        /// <summary>Test-ForbiddenSlotRoot: under or containing a forbidden prefix, or under a non-leaf segment named releases.</summary>
        public static bool ForbiddenRoot(string comparable)
        {
            foreach (var prefix in ForbiddenPrefixes)
            {
                if (UnderPrefix(comparable, prefix)) return true;
                if (UnderPrefix(prefix, comparable)) return true;
            }
            var segments = Segments(comparable);
            for (var index = 0; index < segments.Count - 1; index++)
                if (segments[index].Equals("releases", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// Get-DesktopSlotFreeBytes: the free bytes of the drive of the path's nearest existing ancestor; -1 when there is none or
        /// the drive cannot be read.
        /// </summary>
        public static long DriveFreeBytes(IFileSystem files, string path, bool windows)
        {
            if (files == null) throw new ArgumentNullException(nameof(files));
            var probe = path;
            while (true)
            {
                if (Exists(files, probe)) break;
                var parent = PsPath.Parent(probe, windows);
                if (string.IsNullOrEmpty(parent) || parent == probe) break;
                probe = parent;
            }
            if (!Exists(files, probe)) return -1;
            try
            {
                var full = DrivePrefix.IsMatch(probe) ? probe : files.Path.GetFullPath(probe);
                var root = files.Path.GetPathRoot(full);
                if (string.IsNullOrEmpty(root)) return -1;
                return files.DriveInfo.New(root).AvailableFreeSpace;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            {
                return -1;
            }
        }

        private static DesktopSlotResult Refuse(List<string> lines, string line)
        {
            lines.Add(line);
            return new DesktopSlotResult(Refused, lines);
        }

        // ConvertTo-DriveSlotPath: the drive, then the tail's segments without empty ones and '.', each '..' dropping the last.
        private static string DriveSlotPath(string path)
        {
            var slash = path.Replace('/', '\\');
            var drive = slash.Substring(0, 2);
            var tail = slash.Length > 2 ? slash.Substring(2) : "";
            var parts = new List<string>();
            foreach (var segment in tail.Split('\\'))
            {
                if (segment.Length == 0 || segment == ".") continue;
                if (segment == "..")
                {
                    if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
                    continue;
                }
                parts.Add(segment);
            }
            return parts.Count == 0 ? drive + "\\" : drive + "\\" + string.Join("\\", parts);
        }

        // Get-SlotSegments.
        private static IReadOnlyList<string> Segments(string comparable)
        {
            if (DriveRooted.IsMatch(comparable))
            {
                if (comparable.Length <= 3) return Array.Empty<string>();
                var tail = comparable.Substring(3);
                return tail.Length == 0 ? Array.Empty<string>() : tail.Split('\\');
            }
            var unix = comparable;
            if (unix.Length > 1) unix = unix.TrimEnd('/');
            if (unix.Length == 0 || unix == "/") return Array.Empty<string>();
            unix = unix.TrimStart('/');
            return unix.Length == 0 ? Array.Empty<string>() : unix.Split('/');
        }

        // Test-UnderPrefix.
        private static bool UnderPrefix(string path, string prefix)
        {
            if (path.Equals(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            return path.StartsWith(prefix.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
        }

        // Test-SlotAliasSegment ('.EndsWith' with a string argument compares in the current culture, as PowerShell called it).
        private static bool AliasSegment(string segment)
        {
            if (segment == "." || segment == "..") return false;
            if (segment.EndsWith(".", StringComparison.CurrentCulture)) return true;
            if (segment.EndsWith(" ", StringComparison.CurrentCulture)) return true;
            return segment.Contains('~', StringComparison.Ordinal);
        }

        // Test-SlotDriveAlias.
        private static bool DriveAlias(string path)
        {
            var slash = path.Replace('/', '\\');
            var tail = slash.Length > 2 ? slash.Substring(2) : "";
            return tail.Split('\\').Any(segment => segment.Length > 0 && AliasSegment(segment));
        }

        // Test-SlotUnixAlias.
        private static bool UnixAlias(string fullPath)
        {
            var unix = fullPath;
            if (unix.Length > 1) unix = unix.TrimEnd('/');
            if (unix.Length == 0 || unix == "/") return false;
            var body = unix.TrimStart('/');
            if (body.Length == 0) return false;
            return body.Split('/').Any(segment => segment.Length > 0 && AliasSegment(segment));
        }

        // Test-ForbiddenSlotChild.
        private static bool ForbiddenChild(string comparable) => Children.Any(name => ForbiddenRoot(JoinChild(comparable, name)));

        // Test-FilesystemRoot.
        private static bool FilesystemRoot(string comparable) => DriveRoot.IsMatch(comparable) || comparable == "/" || comparable == "\\";

        // Test-SlotReparseAncestor: the root or an existing ancestor (at most 64) is a reparse point. A link is read, not followed.
        private static bool ReparseAncestor(IFileSystem files, string comparable, bool windows)
        {
            var current = comparable;
            for (var seen = 0; seen < 64; seen++)
            {
                FileAttributes? attributes = null;
                try { attributes = files.File.GetAttributes(current); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException) { }
                if (attributes.HasValue && (attributes.Value & FileAttributes.ReparsePoint) != 0) return true;
                if (DriveRootLoose.IsMatch(current)) break;
                if (current == "/" || current == "\\") break;
                var parent = PsPath.Parent(current, windows);
                if (string.IsNullOrEmpty(parent) || parent == current) break;
                current = parent;
            }
            return false;
        }

        // Test-SlotRootCreatable: a drive path's drive root exists.
        private static bool RootCreatable(IFileSystem files, string comparable)
        {
            if (!DriveRooted.IsMatch(comparable)) return true;
            return Exists(files, comparable.Substring(0, 2) + "\\");
        }

        // Join-SlotChild.
        private static string JoinChild(string root, string child)
        {
            if (DriveRooted.IsMatch(root)) return root.TrimEnd('\\') + "\\" + child;
            return root.TrimEnd('/') + "/" + child;
        }

        // ConvertTo-SlotCompare.
        private static string Compare(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            var text = path.Trim().Replace('\\', '/');
            while (text.Length > 1 && text.EndsWith("/", StringComparison.CurrentCulture)) text = text.Substring(0, text.Length - 1);
            return text;
        }

        // Test-SlotSamePath.
        private static bool SamePath(string left, string right)
        {
            var a = Compare(left);
            if (a.Length == 0) return false;
            return a.Equals(Compare(right), StringComparison.OrdinalIgnoreCase);
        }

        // Invoke-SlotGit: git with GIT_CEILING_DIRECTORIES in its environment alone; stdout and stderr together, trimmed. A git
        // that cannot start is code 1 with the reason.
        private static (int Code, string Text) Git(DesktopSlotSeams seams, string ceiling, params string[] arguments)
        {
            try
            {
                var result = seams.Tools.Run(new OperatorToolCall("git", arguments,
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["GIT_CEILING_DIRECTORIES"] = ceiling }));
                return (result.ExitCode, string.Join(Environment.NewLine, result.Lines ?? Array.Empty<string>()).Trim());
            }
            catch (Exception e)
            {
                // The script's catch around the call operator: the reason is the text, the code 1.
                return (1, e.Message.Trim());
            }
        }

        // Add-SlotClonePlan.
        private static int ClonePlan(IFileSystem files, DesktopSlotSeams seams, string repoPath, List<string> lines, bool mutating)
        {
            var gitDir = JoinChild(repoPath, ".git");
            var slash = repoPath.Replace('\\', '/');
            if (!Exists(files, gitDir))
            {
                lines.Add("next-command: git clone " + CloneUrl + " " + slash);
                lines.Add("next-command: git -C " + slash + " config --local core.longpaths true");
                return Done;
            }
            if (!mutating)
            {
                lines.Add("would-set-longpaths=true");
                return Done;
            }
            if (seams.Tools.Find("git") == null)
            {
                lines.Add("refused: git-missing");
                return Refused;
            }
            var ceiling = PsPath.Parent(repoPath, seams.Windows);
            if (string.IsNullOrEmpty(ceiling))
            {
                lines.Add("refused: clone-unresolved " + slash);
                return Refused;
            }
            var top = Git(seams, ceiling, "-C", repoPath, "rev-parse", "--show-toplevel");
            if (top.Code != 0)
            {
                lines.Add("refused: clone-unresolved " + slash);
                return Refused;
            }
            var absolute = Git(seams, ceiling, "-C", repoPath, "rev-parse", "--absolute-git-dir");
            if (absolute.Code != 0)
            {
                lines.Add("refused: clone-unresolved " + slash);
                return Refused;
            }
            if (!SamePath(top.Text, repoPath) || !SamePath(absolute.Text, gitDir))
            {
                lines.Add("refused: clone-mismatch " + slash);
                return Refused;
            }
            var set = Git(seams, ceiling, "-C", repoPath, "--git-dir", gitDir, "--work-tree", repoPath, "config", "--local", "core.longpaths", "true");
            if (set.Code != 0)
            {
                lines.Add("refused: longpaths-not-true");
                return Refused;
            }
            var got = Git(seams, ceiling, "-C", repoPath, "--git-dir", gitDir, "--work-tree", repoPath, "config", "--local", "--get", "core.longpaths");
            // -ne ignores case.
            if (got.Code != 0 || !got.Text.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                lines.Add("refused: longpaths-not-true");
                return Refused;
            }
            lines.Add("longpaths=true");
            return Done;
        }

        // Invoke-SlotRemoveEmpty.
        private static DesktopSlotResult RemoveEmpty(NightlyHost host, IFileSystem files, DesktopSlotSeams seams, string comparable, bool mutating, List<string> lines)
        {
            var ledger = JoinChild(JoinChild(comparable, "workspace-slots"), "ledger.json");
            string reason = null;
            if (Exists(files, ledger)) reason = "ledger";
            if (reason == null)
            {
                foreach (var name in Children)
                {
                    var path = JoinChild(comparable, name);
                    if (!Exists(files, path)) continue;
                    if (ChildCount(host, files, path) > 0)
                    {
                        reason = name + "-child";
                        break;
                    }
                }
            }
            if (reason != null) return Refuse(lines, "refused: remove-empty-blocked reason=" + reason);
            foreach (var name in Children)
            {
                var path = JoinChild(comparable, name);
                if (!mutating)
                {
                    lines.Add("would-remove: " + path);
                    continue;
                }
                seams.BeforeRemove?.Invoke(path);
                if (!Exists(files, path)) continue;
                try { files.Directory.Delete(path, false); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
                {
                    return Refuse(lines, "refused: remove-empty-blocked reason=not-empty");
                }
                lines.Add("removed: " + path);
            }
            return new DesktopSlotResult(Done, lines);
        }

        // Get-ChildItem -LiteralPath <path> -Force: a directory's entries (hidden ones too), a file is itself; a listing that fails
        // is a stderr line and nothing (a non-terminating error).
        private static int ChildCount(NightlyHost host, IFileSystem files, string path)
        {
            if (!files.Directory.Exists(path)) return 1;
            try
            {
                return files.Directory.EnumerateFileSystemEntries(path).Count();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                host.Error.WriteLine(e.Message);
                return 0;
            }
        }

        // Test-Path -LiteralPath: a file or a directory.
        private static bool Exists(IFileSystem files, string path) => !string.IsNullOrEmpty(path) && (files.File.Exists(path) || files.Directory.Exists(path));
    }

    /// <summary>
    /// The path cmdlets the ported operator scripts used: Split-Path -Parent and Join-Path. On Windows both normalise '/' to '\'
    /// (Split-Path drops a trailing separator; a drive's parent is its root, a root's parent is ""; a bare drive "C:" is refused as
    /// Split-Path refused it). Elsewhere the separator is '/', with '\' read as '/'.
    /// </summary>
    public static class PsPath
    {
        private static readonly Regex DriveOnly = new Regex("^[A-Za-z]:$", RegexOptions.CultureInvariant);
        private static readonly Regex DriveRoot = new Regex("^[A-Za-z]:\\\\$", RegexOptions.CultureInvariant);

        /// <summary>Split-Path -Parent.</summary>
        public static string Parent(string path, bool windows)
        {
            if (string.IsNullOrEmpty(path)) return "";
            if (!windows)
            {
                var unix = path.Replace('\\', '/');
                while (unix.Length > 1 && unix.EndsWith('/')) unix = unix.Substring(0, unix.Length - 1);
                if (unix == "/") return "";
                var slashAt = unix.LastIndexOf('/');
                if (slashAt < 0) return "";
                return slashAt == 0 ? "/" : unix.Substring(0, slashAt);
            }
            var text = path.Replace('/', '\\');
            if (DriveOnly.IsMatch(text)) throw new ArgumentException("Cannot process argument because the value of argument \"path\" is not valid.");
            while (text.Length > 1 && text.EndsWith('\\') && !DriveRoot.IsMatch(text) && text != "\\\\") text = text.Substring(0, text.Length - 1);
            if (DriveRoot.IsMatch(text) || text == "\\") return "";
            var at = text.LastIndexOf('\\');
            if (at < 0) return "";
            var parent = text.Substring(0, at);
            if (DriveOnly.IsMatch(parent)) return parent + "\\";
            return parent.Length == 0 ? "\\" : parent;
        }

        /// <summary>Join-Path: one separator between the two.</summary>
        public static string Join(string left, string right, bool windows)
        {
            var separator = windows ? '\\' : '/';
            var other = windows ? '/' : '\\';
            var head = (left ?? "").Replace(other, separator).TrimEnd(separator);
            var tail = (right ?? "").Replace(other, separator).TrimStart(separator);
            return head + separator + tail;
        }
    }
}
