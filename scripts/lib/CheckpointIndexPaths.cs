#nullable disable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.Checkpoints
{
    // Compiled by run-checkpoint.ps1 (Import-CheckpointCompiledSource) and linked into Antiphon.Tests. Answers
    // "would `git ls-files -z -- <paths>` run at this checkout print anything" from the index file itself, so the
    // script's tracked-path guards start no git process after the first. The caller passes the toplevel git itself
    // reported for this checkout in this run (`rev-parse --show-toplevel`, which validates the repository's
    // metadata and ownership and resolves core.worktree from any config source); without it, or when it is not this
    // checkout, git answers. Paths are looked up in their normalised form, the file the write would open (Windows drops
    // trailing dots and spaces). Null too for: a path with a pathspec glob character, one outside the checkout or
    // beyond a symlinked parent; a gitdir it cannot locate; a missing index; and any index that is not a plain
    // version-2 SHA-1 index with a valid checksum, only
    // stage-0 regular, executable or symlink entries and no required extension (the same rules as GitInProcess'
    // index listing, pinned against git there). Case-insensitive on Windows, which can only call more paths tracked
    // (a refusal), never fewer. The index is re-read on every call.
    public static class CheckpointIndexPaths
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public static bool? AnyTracked(string root, string[] fullPaths, string verifiedToplevel)
        {
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(verifiedToplevel) || fullPaths == null || fullPaths.Length == 0)
                return null;
            var windows = OperatingSystem.IsWindows();
            var comparison = windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var relatives = new List<string>();
            try
            {
                root = Path.GetFullPath(root).TrimEnd('\\', '/');
                if (!string.Equals(root, Path.GetFullPath(verifiedToplevel).TrimEnd('\\', '/'), comparison)) return null;
                foreach (var path in fullPaths)
                {
                    if (string.IsNullOrEmpty(path) || path.IndexOfAny(new[] { '*', '?', '[' }) >= 0 || !windows && path.IndexOf('\\') >= 0)
                        return null;
                    // The form the write itself opens: on Windows "bin.\x" opens "bin\x" (trailing dots and spaces
                    // dropped), which git's pathspec would not match, so the lookup uses the normalised path.
                    var full = Path.GetFullPath(path);
                    var relative = Path.GetRelativePath(root, full).Replace('\\', '/');
                    if (relative == ".") relative = "";
                    if (relative == ".." || relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative))
                        return null;
                    relative = relative.TrimEnd('/');
                    var parts = relative.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    var parent = root;
                    for (var i = 0; i < parts.Length - 1; i++)
                    {
                        parent = Path.Combine(parent, parts[i]);
                        if (!Directory.Exists(parent) && !File.Exists(parent)) break;
                        if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0) return null;
                    }
                    relatives.Add(relative);
                }
                var data = ReadIndex(root);
                if (data == null) return null;
                var entries = ListIndex(data, out _);
                if (entries == null) return null;
                foreach (var entry in entries)
                    foreach (var relative in relatives)
                        if (relative.Length == 0 || entry.Equals(relative, comparison) || entry.StartsWith(relative + "/", comparison))
                            return true;
                return false;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException
                || ex is NotSupportedException || ex is DecoderFallbackException)
            {
                return null;
            }
        }

        // Whether `git ls-files -v -z` at this checkout root would tag any entry assume-unchanged (lower case), for
        // the script's source snapshot, which refuses an index that hides edits. Called with the toplevel and the
        // absolute git dir (git's rev-parse answer, or CheckpointProcessRunner.FindRepository's, which declines
        // core.worktree), right after a git status there succeeded; the index is read only when <root>/.git leads to
        // that same git dir (core.worktree moves the top level away from the git dir). A plain version-2 index
        // carries only that flag (skip-worktree and intent-to-add need version 3): true when any entry has it, false
        // when none does. Null (ask ls-files -v) for anything else: another git dir, a version 3 or 4 index, a split
        // or sparse index, an unmerged or gitlink entry, no index.
        public static bool? AnyHiddenEntry(string root, string gitDir)
        {
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(gitDir)) return null;
            try
            {
                var top = Path.GetFullPath(root).TrimEnd('\\', '/');
                var found = GitDirectory(top);
                var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (found == null || !string.Equals(Path.GetFullPath(found).TrimEnd('\\', '/'),
                        Path.GetFullPath(gitDir).TrimEnd('\\', '/'), comparison))
                    return null;
                // The index of the git dir just checked, not a second resolution of <root>/.git.
                var data = ReadIndexAt(found);
                if (data == null) return null;
                return ListIndex(data, out var assumeUnchanged) == null ? (bool?)null : assumeUnchanged;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException
                || ex is NotSupportedException || ex is DecoderFallbackException)
            {
                return null;
            }
        }

        // The checkout's index file bytes, or null when its gitdir or index cannot be found or read whole.
        private static byte[] ReadIndex(string root)
        {
            var gitDir = GitDirectory(root);
            return gitDir == null ? null : ReadIndexAt(gitDir);
        }

        // A git dir's index file bytes, or null when it is missing or cannot be read whole.
        private static byte[] ReadIndexAt(string gitDir)
        {
            var index = Path.Combine(gitDir, "index");
            if (!File.Exists(index)) return null;
            using (var stream = new FileStream(index, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (stream.Length > int.MaxValue / 2) return null;
                var data = new byte[stream.Length];
                var read = 0;
                while (read < data.Length)
                {
                    var n = stream.Read(data, read, data.Length - read);
                    if (n <= 0) return null;
                    read += n;
                }
                return data;
            }
        }

        // <root>/.git as a directory, or the "gitdir: <path>" a .git file names (a linked worktree).
        private static string GitDirectory(string root)
        {
            var dotGit = Path.Combine(root, ".git");
            if (Directory.Exists(dotGit)) return dotGit;
            if (!File.Exists(dotGit)) return null;
            var text = File.ReadAllText(dotGit).Trim();
            if (!text.StartsWith("gitdir:", StringComparison.Ordinal)) return null;
            var target = text.Substring(7).Trim();
            if (target.Length == 0 || target.IndexOf('\n') >= 0) return null;
            var full = Path.GetFullPath(Path.IsPathRooted(target) ? target : Path.Combine(root, target));
            return Directory.Exists(full) ? full : null;
        }


        // Paths of a plain version-2 SHA-1 index, in file order; null for anything else (see the class comment).
        // assumeUnchanged: some entry carries the assume-valid bit (0x8000), which `ls-files -v` prints lower case.
        internal static List<string> ListIndex(byte[] data, out bool assumeUnchanged)
        {
            assumeUnchanged = false;
            const int Header = 12, Hash = 20, Fixed = 62;
            if (data.Length < Header + Hash || data[0] != (byte)'D' || data[1] != (byte)'I' || data[2] != (byte)'R' || data[3] != (byte)'C'
                || BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4)) != 2)
                return null;
            var body = data.Length - Hash;
            if (!System.Security.Cryptography.SHA1.HashData(data.AsSpan(0, body)).AsSpan().SequenceEqual(data.AsSpan(body))) return null;
            var count = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8));
            var paths = new List<string>();
            var at = Header;
            for (var i = 0u; i < count; i++)
            {
                if (at + Fixed + 1 > body) return null;
                var mode = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at + 24));
                var flags = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(at + 60));
                if ((flags & 0x7000) != 0) return null;
                if ((flags & 0x8000) != 0) assumeUnchanged = true;
                if (mode != 0x81A4 && mode != 0x81ED && mode != 0xA000) return null;
                var nameStart = at + Fixed;
                int nameLength = flags & 0xFFF;
                if (nameLength == 0xFFF)
                {
                    var nul = data.AsSpan(nameStart, body - nameStart).IndexOf((byte)0);
                    if (nul < 0) return null;
                    nameLength = nul;
                }
                if (nameLength == 0 || nameStart + nameLength >= body || data[nameStart + nameLength] != 0) return null;
                for (var b = nameStart; b < nameStart + nameLength; b++)
                    if (data[b] < 0x20 || data[b] == 0x7f) return null;
                paths.Add(StrictUtf8.GetString(data, nameStart, nameLength));
                at += (Fixed + nameLength + 8) & ~7;
            }
            while (at < body)
            {
                if (at + 8 > body) return null;
                var signature = data[at];
                var size = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at + 4));
                if (signature < (byte)'A' || signature > (byte)'Z' || size > (uint)(body - at - 8)) return null;
                at += 8 + (int)size;
            }
            return paths;
        }
    }
}
