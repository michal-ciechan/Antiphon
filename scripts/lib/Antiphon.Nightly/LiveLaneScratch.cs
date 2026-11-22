// The live-lane manifest's owned scratch (CARD-1545 S1a D-5), compiled in W2-nightly S24. Every scratch directory is a
// leaf of <repo>/.antiphon/live-lane-capture and every dangling census commit is a loose object of the repository;
// each is recorded in a ledger when it is made, and Clear removes exactly what the ledger holds, also after a failure.
// Nothing is written under the system temp directory (TempResidue lists the antiphon-live-lane-* names there, for
// the witness). The census (LiveLaneCensus, W2-nightly S25) keeps one LiveLaneScratch per census session.
// Plain C#. ASCII-only.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>One ledger row: a scratch directory (Kind "dir") or a dangling commit's loose object (Kind "file", Sha).</summary>
    public sealed record LiveLaneScratchEntry(string Path, string Kind, string Sha);

    /// <summary>
    /// New-LiveLaneOwnedScratch, Remove-LiveLaneOwnedScratch, Clear-LiveLaneOwnedScratch, New-LiveLaneDanglingSeed,
    /// Copy-LiveLaneGitBlob and Get-LiveLaneTempResidue over one ledger ($script:LiveLaneScratchLedger).
    /// </summary>
    public sealed class LiveLaneScratch
    {
        private static readonly Regex ObjectSha = new Regex("^[0-9a-f]{40}$", RegexOptions.IgnoreCase);
        private readonly List<LiveLaneScratchEntry> _ledger = new List<LiveLaneScratchEntry>();

        /// <summary>The ledger, in the order the scratch was made.</summary>
        public IReadOnlyList<LiveLaneScratchEntry> Entries => _ledger;

        /// <summary>The capture marker: a path under ...\.antiphon\live-lane-capture\ (Test-LiveLaneUnderCapture).</summary>
        public static bool UnderCapture(string full)
        {
            var sep = Path.DirectorySeparatorChar.ToString();
            var marker = sep + ".antiphon" + sep + "live-lane-capture" + sep;
            return (full ?? "").IndexOf(marker, StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        /// New-LiveLaneOwnedScratch: the directory &lt;repo&gt;/.antiphon/live-lane-capture/&lt;leaf&gt;, made and recorded.
        /// A blank leaf or one with \, / or . refuses (scratch-leaf); a path outside the capture refuses (scratch-escape).
        /// </summary>
        public string NewOwned(string repoRoot, string leaf)
        {
            if (string.IsNullOrWhiteSpace(leaf) || leaf.IndexOfAny(new[] { '\\', '/', '.' }) >= 0) throw new InvalidOperationException("scratch-leaf");
            var capture = Path.Combine(repoRoot, ".antiphon", "live-lane-capture");
            Directory.CreateDirectory(capture);
            var captureFull = Path.GetFullPath(capture);
            var path = Path.GetFullPath(Path.Combine(captureFull, leaf));
            if (!path.StartsWith(captureFull + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new InvalidOperationException("scratch-escape");
            if (!UnderCapture(path)) throw new InvalidOperationException("scratch-escape");
            Directory.CreateDirectory(path);
            _ledger.Add(new LiveLaneScratchEntry(path, "dir", ""));
            return path;
        }

        /// <summary>
        /// Remove-LiveLaneOwnedScratch: removes one recorded entry (it stays in the ledger). An unrecorded path refuses
        /// (scratch-not-owned). A file must be under the capture or be the loose object of its sha (objects/xx/yyyy...);
        /// a directory must be under the capture (scratch-escape). Read-only files are removed too.
        /// </summary>
        public void Remove(string path, string kind, string sha)
        {
            if (path == null) return;
            var full = Path.GetFullPath(path);
            var known = false;
            foreach (var item in _ledger)
                if (LiveLaneJson.Same(item.Path, full)) known = true;
            if (!known) throw new InvalidOperationException("scratch-not-owned");
            if (LiveLaneJson.Same(kind, "file"))
            {
                var underCapture = UnderCapture(full);
                var objectOk = false;
                sha ??= "";
                if (ObjectSha.IsMatch(sha))
                {
                    var name = Path.GetFileName(full);
                    var dir = Path.GetFileName(Path.GetDirectoryName(full));
                    if (LiveLaneJson.Same(dir, sha.Substring(0, 2)) && LiveLaneJson.Same(name, sha.Substring(2))) objectOk = true;
                }
                if (!underCapture && !objectOk) throw new InvalidOperationException("scratch-escape");
                if (File.Exists(full)) DeleteFile(full);
                return;
            }
            if (!UnderCapture(full)) throw new InvalidOperationException("scratch-escape");
            if (Directory.Exists(full)) DeleteTree(full);
            else if (File.Exists(full)) DeleteFile(full);
        }

        /// <summary>Remove-LiveLaneOwnedScratch of a ledger row.</summary>
        public void Remove(LiveLaneScratchEntry entry)
        {
            if (entry != null) Remove(entry.Path, entry.Kind, entry.Sha);
        }

        /// <summary>Clear-LiveLaneOwnedScratch: removes every recorded entry, then empties the ledger (a refusal leaves it as it was).</summary>
        public void Clear()
        {
            foreach (var item in _ledger.ToArray()) Remove(item);
            _ledger.Clear();
        }

        /// <summary>
        /// New-LiveLaneDanglingSeed: a commit of HEAD's tree that no ref reaches ("c1545 dangling non-ancestor"), its loose
        /// object recorded for removal. Returns its sha. The commit is made with a fixed identity and unsigned (it is
        /// never kept), so it does not depend on the caller's git identity or signing setup.
        /// </summary>
        public string DanglingSeed(string repoRoot)
        {
            var tree = Git(repoRoot, "rev-parse", "HEAD^{tree}").Text;
            var sha = Git(repoRoot, "-c", "user.name=c1545", "-c", "user.email=c1545@example.invalid", "-c", "commit.gpgsign=false",
                "commit-tree", tree, "-m", "c1545 dangling non-ancestor").Text;
            if (!ObjectSha.IsMatch(sha)) throw new InvalidOperationException("dangling-seed");
            var rel = "objects/" + sha.Substring(0, 2) + "/" + sha.Substring(2);
            var gitPath = Git(repoRoot, "rev-parse", "--git-path", rel).Text;
            if (string.IsNullOrWhiteSpace(gitPath)) throw new InvalidOperationException("dangling-seed-path");
            if (!Path.IsPathRooted(gitPath)) gitPath = Path.Combine(repoRoot, gitPath);
            var full = Path.GetFullPath(gitPath);
            var name = Path.GetFileName(full);
            var dir = Path.GetFileName(Path.GetDirectoryName(full));
            if (!LiveLaneJson.Same(dir, sha.Substring(0, 2)) || !LiveLaneJson.Same(name, sha.Substring(2))) throw new InvalidOperationException("dangling-seed-path");
            if (!File.Exists(full)) throw new InvalidOperationException("dangling-seed-missing");
            _ledger.Add(new LiveLaneScratchEntry(full, "file", sha));
            return sha;
        }

        /// <summary>
        /// Copy-LiveLaneGitBlob: git cat-file blob &lt;spec&gt; into dest (its directory made). A missing object refuses
        /// (seed-object-missing &lt;spec&gt;), leaving dest empty.
        /// </summary>
        public static void CopyGitBlob(string repoRoot, string spec, string dest)
        {
            var parent = Path.GetDirectoryName(Path.GetFullPath(dest));
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            var start = new ProcessStartInfo("git")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in new[] { "-C", repoRoot, "cat-file", "blob", spec }) start.ArgumentList.Add(argument);
            int code;
            using (var process = Process.Start(start) ?? throw new InvalidOperationException("git did not start"))
            {
                var stderr = process.StandardError.ReadToEndAsync();
                using (var file = File.Create(dest)) process.StandardOutput.BaseStream.CopyTo(file);
                stderr.GetAwaiter().GetResult();
                process.WaitForExit();
                code = process.ExitCode;
            }
            if (code != 0) throw new InvalidOperationException("seed-object-missing " + spec);
        }

        /// <summary>
        /// Get-LiveLaneTempResidue: the full names of the antiphon-live-lane-* entries (hidden ones too) in the system temp
        /// directory and in /tmp when it exists and is another directory.
        /// </summary>
        public static IReadOnlyList<string> TempResidue()
        {
            var roots = new List<string>();
            var temp = Path.GetTempPath();
            if (!string.IsNullOrWhiteSpace(temp)) roots.Add(Path.GetFullPath(temp));
            if (Directory.Exists("/tmp") || File.Exists("/tmp"))
            {
                var unix = Path.GetFullPath("/tmp");
                var have = false;
                foreach (var root in roots)
                    if (LiveLaneJson.Same(root, unix)) have = true;
                if (!have) roots.Add(unix);
            }
            var names = new List<string>();
            var listing = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true, RecurseSubdirectories = false };
            foreach (var root in roots)
            {
                if (!Directory.Exists(root)) continue;
                try
                {
                    foreach (var item in Directory.EnumerateFileSystemEntries(root, "*", listing))
                        if (Path.GetFileName(item).StartsWith("antiphon-live-lane-", StringComparison.Ordinal)) names.Add(item);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
            }
            return names;
        }

        /// <summary>
        /// The witness of D-5 (the old ScratchResidue case of scripts/test-live-lane-manifest.ps1): twice (a run that fails
        /// after making its scratch, then a green one) make an owned directory, a census seed copy (the fixture and the
        /// protected inputs as blobs of HEAD), a policy copy (the protected inputs of the working tree) and a dangling
        /// commit, then clear the ledger. A problem is any of them left behind (residue-&lt;run&gt;,
        /// dangling-commit-&lt;run&gt;), a new antiphon-live-lane-* temp entry (tmp-residue), a seed copy that could not be
        /// made (seed-material-missing) or the green run's failure text. None: accepted.
        /// </summary>
        public static IReadOnlyList<string> ScratchResidueWitness(string repoRoot, string fixtureRel, IReadOnlyList<string> protectedPaths)
        {
            var problems = new List<string>();
            foreach (var label in new[] { "fail", "green" })
            {
                var before = TempResidue();
                var created = new List<string>();
                var sha = "";
                var scratch = new LiveLaneScratch();
                try
                {
                    created.Add(scratch.NewOwned(repoRoot, "residue" + label + Guid.NewGuid().ToString("N")));
                    var head = Git(repoRoot, "rev-parse", "HEAD").Text;
                    var material = scratch.SeedCopy(repoRoot, head, fixtureRel, protectedPaths);
                    if (material == null) problems.Add("seed-material-missing");
                    else created.Add(material);
                    var policy = scratch.NewOwned(repoRoot, "policy" + Guid.NewGuid().ToString("N"));
                    foreach (var rel in protectedPaths)
                    {
                        var dest = Path.Combine(policy, rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        File.Copy(Path.Combine(repoRoot, rel), dest, true);
                    }
                    created.Add(policy);
                    sha = scratch.DanglingSeed(repoRoot);
                    if (label == "fail") throw new InvalidOperationException("forced-scratch-failure");
                }
                catch (Exception e)
                {
                    if (label == "green") problems.Add(e.Message);
                }
                finally
                {
                    scratch.Clear();
                }
                foreach (var path in created)
                    if (!string.IsNullOrWhiteSpace(path) && (Directory.Exists(path) || File.Exists(path))) problems.Add("residue-" + label);
                if (!string.IsNullOrWhiteSpace(sha) && Git(repoRoot, "cat-file", "-t", sha).Exit == 0) problems.Add("dangling-commit-" + label);
                foreach (var name in TempResidue())
                {
                    var seen = false;
                    foreach (var old in before)
                        if (LiveLaneJson.Same(old, name)) seen = true;
                    if (!seen) problems.Add("tmp-residue");
                }
            }
            return problems;
        }

        // The scratch part of Get-LiveLaneCensusSeedMaterial: the fixture and the protected inputs at seed, as blobs, in an
        // owned directory "seed<blob 12><guid>"; null when the fixture has no blob at seed or a copy failed.
        internal string SeedCopy(string repoRoot, string seed, string fixtureRel, IReadOnlyList<string> protectedPaths)
        {
            var blob = Git(repoRoot, "rev-parse", seed + ":" + fixtureRel).Text;
            if (string.IsNullOrWhiteSpace(blob)) return null;
            var before = NewOwned(repoRoot, "seed" + blob.Substring(0, Math.Min(12, blob.Length)) + Guid.NewGuid().ToString("N"));
            var fixtureCopy = Path.Combine(before, fixtureRel);
            var ok = true;
            try { CopyGitBlob(repoRoot, seed + ":" + fixtureRel, fixtureCopy); }
            catch (InvalidOperationException) { ok = false; }
            foreach (var rel in protectedPaths)
            {
                try { CopyGitBlob(repoRoot, seed + ":" + rel, Path.Combine(before, rel)); }
                catch (InvalidOperationException) { ok = false; }
            }
            return ok && File.Exists(fixtureCopy) ? before : null;
        }

        /// <summary>Get-LiveLaneGit: git -C repo &lt;arguments&gt;, stderr dropped; the exit code and stdout trimmed.</summary>
        internal static (int Exit, string Text) Git(string repoRoot, params string[] arguments)
        {
            var start = new ProcessStartInfo("git")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
            };
            start.ArgumentList.Add("-C");
            start.ArgumentList.Add(repoRoot);
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("git did not start");
            var stderr = process.StandardError.ReadToEndAsync();
            var stdout = process.StandardOutput.ReadToEnd();
            stderr.GetAwaiter().GetResult();
            process.WaitForExit();
            return (process.ExitCode, stdout.Trim());
        }

        /// <summary>Remove-Item -Recurse -Force of a directory: read-only and hidden entries too; a link is removed, not followed.</summary>
        internal static void DeleteTree(string directory)
        {
            var info = new DirectoryInfo(directory);
            if (!info.Exists) return;
            if ((info.Attributes & FileAttributes.ReparsePoint) == 0)
            {
                foreach (var entry in info.EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false }))
                {
                    if (entry is DirectoryInfo sub) DeleteTree(sub.FullName);
                    else DeleteFile(entry.FullName);
                }
            }
            info.Attributes = FileAttributes.Directory;
            info.Delete(false);
        }

        private static void DeleteFile(string path)
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System)) != 0) File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
    }
}
