// W2-nightly S15: the path helpers of nightly-common.ps1 and test-phase-paths.ps1 that the tests executor and the release
// selection reader use (plan 2026-11-21-w2-nightly-port-slices.md, slice S15): ConvertTo-NightlyCanonicalPath,
// Test-NightlySharedTree, Test-NightlyAncestorReparse, Get-NightlyFileSha256 and Resolve-QualificationTimingRoot. S27
// deleted test-phase-paths.ps1 (release-cut.ps1 calls TimingRoot); nightly-common.ps1 keeps its copies for the W2-rc
// callers. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.IO;

namespace Antiphon.Nightly
{
    /// <summary>The nightly path rules, as the PowerShell helpers of the same names answer them.</summary>
    public static class NightlyPaths
    {
        /// <summary>The shared main checkout a nightly run refuses without AllowSharedTree.</summary>
        public const string SharedMain = @"C:\src\Antiphon";

        /// <summary>The worktree root a nightly run refuses without AllowSharedTree.</summary>
        public const string SharedWorktrees = @"C:\Antiphon\worktrees";

        /// <summary>The isolated clone the shared-tree refusal names.</summary>
        public const string IsolatedClone = @"C:\Antiphon\nightly\checkout";

        /// <summary>
        /// ConvertTo-NightlyCanonicalPath: the alternate separator replaced, the full path, and a trailing separator
        /// dropped unless the path is a root.
        /// </summary>
        public static string Canonical(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            var normalised = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            var full = Path.GetFullPath(normalised);
            var root = Path.GetPathRoot(full) ?? "";
            if (full.Length > root.Length) full = full.TrimEnd(Path.DirectorySeparatorChar);
            return full;
        }

        /// <summary>Test-NightlySharedTree: the path is C:\src\Antiphon or C:\Antiphon\worktrees, or under either (case-insensitive).</summary>
        public static bool IsSharedTree(string path)
        {
            var canonical = Canonical(path ?? "");
            var sep = Path.DirectorySeparatorChar.ToString();
            foreach (var shared in new[] { Canonical(SharedMain), Canonical(SharedWorktrees) })
            {
                if (string.Equals(canonical, shared, StringComparison.OrdinalIgnoreCase)) return true;
                if (canonical.StartsWith(shared + sep, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>
        /// Test-NightlyAncestorReparse: the path or an ancestor below its root exists and is a reparse point (a link). A
        /// dangling link is skipped (Exists follows links); an entry whose attributes cannot be read is not a link.
        /// </summary>
        public static bool HasReparseAncestor(string path)
        {
            var canonical = Canonical(path ?? "");
            var root = Path.GetPathRoot(canonical) ?? "";
            var cursor = canonical;
            while (cursor.Length > root.Length)
            {
                if (File.Exists(cursor) || Directory.Exists(cursor))
                {
                    FileAttributes? attributes = null;
                    try { attributes = File.GetAttributes(cursor); }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException) { }
                    if (attributes.HasValue && (attributes.Value & FileAttributes.ReparsePoint) != 0) return true;
                }
                var parent = Path.GetDirectoryName(cursor);
                if (string.IsNullOrWhiteSpace(parent) || parent == cursor) break;
                cursor = parent;
            }
            return false;
        }

        /// <summary>
        /// Get-NightlyFileSha256: lower-case hex SHA-256 of the file as a reader sees it (a queued evidence write first);
        /// "missing file for hash &lt;path&gt;" when it is blank or absent.
        /// </summary>
        public static string FileSha256(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !EvidenceFiles.PathExists(path)) throw new NightlyRefusal("missing file for hash " + path);
            if (EvidenceFiles.PendingLength(path) >= 0) return EvidenceFiles.Sha256(path);
            return NightlyClassNames.FileSha256(path);
        }

        /// <summary>
        /// Resolve-QualificationTimingRoot: the given timing root; else, under the log root, timing-&lt;run id&gt; (or timing
        /// without a run id); blank without a log root.
        /// </summary>
        public static string TimingRoot(string timingRoot, string logRoot, string runId)
        {
            if (!string.IsNullOrWhiteSpace(timingRoot)) return timingRoot;
            if (string.IsNullOrWhiteSpace(logRoot)) return "";
            if (!string.IsNullOrWhiteSpace(runId)) return Path.Combine(logRoot, "timing-" + runId);
            return Path.Combine(logRoot, "timing");
        }
    }
}
