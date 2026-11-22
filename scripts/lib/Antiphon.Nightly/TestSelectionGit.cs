// CARD-1313: the git reads of the test selection, the rc project planner and the live-lane fingerprint. The 2026-11-22
// plan (unit 3) ported them from scripts/lib/test-selection.ps1 (Invoke-TestSelectionGit, Read-TestSelectionBlobBatch,
// Add-TestSelectionBlobPrefetch, Get-TestSelectionBlob), which is deleted.
//
// Each read runs git with the hardened environment of CheckpointProcessRunner (no inherited repository, index or config
// variables, no system or global config, no replace objects, no optional locks) and its -c settings (no fsmonitor, hooks,
// external diff or attributes file, no automatic gc or maintenance), 30 s per read; stdout is strict UTF-8 (a byte order
// mark is read as the reader detects it). The reads made here never run a filter or textconv driver (cat-file blob
// without --filters, show --no-textconv, ls-tree, diff --name-status, rev-parse, rev-list, merge-base, grep without
// --textconv), so the runner's driver lookup is not repeated. Fixture repositories that git add or commit use the runner
// itself (Invoke-C487FixtureGit, scripts/lib/c487-harness.ps1).
//
// Answers fixed by full commit ids are kept for the process: an object's type, a path's bytes at a commit, a recursive
// tree listing, a tree id, a committer time, a merge base and ancestry (both answers). A missing object or any refusal
// is asked of git every time. Plain C#. ASCII only.
#nullable disable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Antiphon.Nightly
{
    /// <summary>One git read: the exit code, stdout and stderr as text, and whether it ran out of time (exit 128 then).</summary>
    public sealed class TestSelectionGitResult
    {
        /// <summary>git's exit code; 128 when the read timed out.</summary>
        public int Exit { get; init; }

        /// <summary>Standard output as strict UTF-8 text.</summary>
        public string Stdout { get; init; } = "";

        /// <summary>Standard error.</summary>
        public string Stderr { get; init; } = "";

        /// <summary>True when the read was stopped after TimeoutMilliseconds.</summary>
        public bool TimedOut { get; init; }
    }

    /// <summary>Invoke-TestSelectionGit and the batched blob reads.</summary>
    public static class TestSelectionGit
    {
        /// <summary>The time one read may take.</summary>
        public const int TimeoutMilliseconds = 30000;

        private const int OutputLimit = 16777216;
        private const int KeptLimit = 4194304;
        private const int KeptCount = 1024;

        private static readonly Regex FullSha = new Regex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant);
        private static readonly Regex AnyCaseSha = new Regex("^[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant);
        private static readonly Regex BlobAtCommit = new Regex("^[0-9a-fA-F]{40}:[^:]", RegexOptions.CultureInvariant);
        private static readonly Regex TreeOfCommit = new Regex(@"^[0-9a-f]{40}\^\{tree\}$", RegexOptions.CultureInvariant);
        private static readonly Regex BatchEntry = new Regex("^100(?:644|755) blob [0-9a-f]{40} +([0-9]+)\t(.+)$", RegexOptions.CultureInvariant);
        private static readonly ConcurrentDictionary<string, TestSelectionGitResult> Kept = new ConcurrentDictionary<string, TestSelectionGitResult>(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, string> Prefetched = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        /// <summary>A read that must exit 0.</summary>
        public static TestSelectionGitResult Run(string root, string[] arguments) => Run(root, arguments, new[] { 0 });

        /// <summary>
        /// Invoke-TestSelectionGit: git with the arguments in root. A timeout or an exit outside allowedExit is refused
        /// (git-read-failed:&lt;first argument&gt;:&lt;exit&gt;), and so is more than 16 MiB of output (git-output-too-large).
        /// </summary>
        public static TestSelectionGitResult Run(string root, string[] arguments, int[] allowedExit)
        {
            arguments ??= Array.Empty<string>();
            allowedExit ??= new[] { 0 };
            var directory = Path.GetFullPath(root);
            var ancestry = arguments.Length == 4 && arguments[0] == "merge-base" && arguments[1] == "--is-ancestor" && FullSha.IsMatch(arguments[2]) && FullSha.IsMatch(arguments[3]);
            string key = null;
            if (ancestry || Fixed(arguments))
            {
                key = directory + "\0" + string.Join("\0", arguments);
                if (Kept.TryGetValue(key, out var kept) && Array.IndexOf(allowedExit, kept.Exit) >= 0) return kept;
            }
            var (exit, bytes, error, timedOut) = Execute(directory, arguments);
            if (timedOut || Array.IndexOf(allowedExit, exit) < 0) throw new TestSelectionRefusal("git-read-failed:" + First(arguments) + ":" + exit);
            if (bytes.Length > OutputLimit) throw new TestSelectionRefusal("git-output-too-large");
            string text;
            try { text = Decode(bytes, 0, bytes.Length); }
            catch (DecoderFallbackException) { throw new TestSelectionRefusal("git-output-undecodable:" + First(arguments)); }
            var result = new TestSelectionGitResult { Exit = exit, Stdout = text, Stderr = error };
            if (key != null && (exit == 0 || ancestry && exit == 1) && text.Length <= KeptLimit && Kept.Count < KeptCount) Kept[key] = result;
            return result;
        }

        private static string First(string[] arguments) => arguments.Length > 0 ? arguments[0] : "";

        private static bool Fixed(string[] a) =>
            (a.Length == 5 && a[0] == "ls-tree" && a[1] == "-r" && a[2] == "-z" && a[3] == "--full-tree" && FullSha.IsMatch(a[4]))
            || (a.Length == 2 && a[0] == "rev-parse" && TreeOfCommit.IsMatch(a[1]))
            || (a.Length == 3 && a[0] == "merge-base" && FullSha.IsMatch(a[1]) && FullSha.IsMatch(a[2]))
            || (a.Length == 4 && a[0] == "show" && a[1] == "-s" && a[2] == "--format=%ct" && FullSha.IsMatch(a[3]))
            || (a.Length == 3 && a[0] == "cat-file" && (a[1] == "-t" && AnyCaseSha.IsMatch(a[2]) || a[1] == "blob" && BlobAtCommit.IsMatch(a[2])));

        /// <summary>
        /// Read-TestSelectionBlobBatch: the text of many paths at one full commit id, two git processes per hundred paths
        /// (ls-tree -l gives each blob's size, one show writes their bytes back to back). A chunk with any mismatch is left
        /// out whole, a blob that does not decode on its own; the caller reads a path left out by itself.
        /// </summary>
        public static Dictionary<string, string> ReadBlobBatch(string root, string sha, string[] paths)
        {
            var texts = new Dictionary<string, string>(StringComparer.Ordinal);
            if (sha == null || !FullSha.IsMatch(sha)) return texts;
            var all = (paths ?? Array.Empty<string>()).ToArray();
            var directory = Path.GetFullPath(root);
            for (var at = 0; at < all.Length; at += 100)
            {
                var chunk = all.Skip(at).Take(100).ToArray();
                try
                {
                    var listing = Run(root, new[] { "--literal-pathspecs", "ls-tree", "-l", "-z", "--full-tree", sha, "--" }.Concat(chunk).ToArray()).Stdout;
                    var sizes = new Dictionary<string, long>(StringComparer.Ordinal);
                    foreach (var entry in listing.Split('\0'))
                    {
                        if (entry.Length == 0) continue;
                        var match = BatchEntry.Match(entry);
                        if (!match.Success) throw new InvalidDataException("batch-entry-shape");
                        sizes[match.Groups[2].Value] = long.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    }
                    long total = 0;
                    foreach (var path in chunk)
                    {
                        if (!sizes.TryGetValue(path, out var size)) throw new InvalidDataException("batch-entry-missing");
                        total += size;
                    }
                    if (sizes.Count != chunk.Length || total > OutputLimit) throw new InvalidDataException("batch-entry-count");
                    var (exit, bytes, _, timedOut) = Execute(directory, new[] { "show", "--no-textconv", "--no-color" }.Concat(chunk.Select(path => sha + ":" + path)).ToArray());
                    if (timedOut || exit != 0 || bytes.LongLength != total) throw new InvalidDataException("batch-show-failed");
                    var offset = 0;
                    foreach (var path in chunk)
                    {
                        var length = (int)sizes[path];
                        try { texts[path] = Decode(bytes, offset, length); }
                        catch (DecoderFallbackException) { }
                        offset += length;
                    }
                }
                catch (Exception)
                {
                    // Any fault leaves the whole chunk to single reads, as the PowerShell catch did.
                    foreach (var path in chunk) texts.Remove(path);
                }
            }
            return texts;
        }

        /// <summary>
        /// Add-TestSelectionBlobPrefetch: reads the paths at a full commit id in one batch and keeps the answered texts
        /// (up to 4 MiB each, 1024 in all) for Blob. Fewer than two paths not yet kept are left to Blob.
        /// </summary>
        public static void Prefetch(string root, string sha, string[] paths)
        {
            if (sha == null || !FullSha.IsMatch(sha)) return;
            var prefix = Path.GetFullPath(root) + "\0" + sha + "\0";
            var missing = (paths ?? Array.Empty<string>()).Where(path => !Prefetched.ContainsKey(prefix + path)).ToArray();
            if (missing.Length < 2 || Prefetched.Count >= KeptCount) return;
            foreach (var pair in ReadBlobBatch(root, sha, missing))
                if (pair.Value.Length <= KeptLimit) Prefetched[prefix + pair.Key] = pair.Value;
        }

        /// <summary>Get-TestSelectionBlob: the text of path at sha (a prefetched text, else cat-file blob, which must exit 0).</summary>
        public static string Blob(string root, string sha, string path)
        {
            if (Prefetched.TryGetValue(Path.GetFullPath(root) + "\0" + sha + "\0" + path, out var text)) return text;
            return Run(root, new[] { "cat-file", "blob", sha + ":" + path }).Stdout;
        }

        /// <summary>Strict UTF-8 as the process reader decodes it: a byte order mark is detected and dropped.</summary>
        private static string Decode(byte[] bytes, int offset, int length)
        {
            using var reader = new StreamReader(new MemoryStream(bytes, offset, length, false), new UTF8Encoding(false, true), true);
            return reader.ReadToEnd();
        }

        private static (int Exit, byte[] Stdout, string Stderr, bool TimedOut) Execute(string directory, string[] arguments)
        {
            var start = new ProcessStartInfo("git")
            {
                WorkingDirectory = directory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
            };
            var nullPath = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
            foreach (var name in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_CONFIG", "GIT_CONFIG_PARAMETERS", "GIT_CONFIG_COUNT" }) start.Environment.Remove(name);
            start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
            start.Environment["GIT_NO_REPLACE_OBJECTS"] = "1";
            start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            start.Environment["GIT_CONFIG_GLOBAL"] = nullPath;
            foreach (var setting in new[] { "core.fsmonitor=false", "core.hooksPath=" + nullPath, "diff.external=", "core.attributesFile=" + nullPath, "gc.auto=0", "maintenance.auto=false" })
            {
                start.ArgumentList.Add("-c");
                start.ArgumentList.Add(setting);
            }
            foreach (var argument in arguments) start.ArgumentList.Add(argument ?? "");
            using var process = new Process { StartInfo = start };
            process.Start();
            var output = new MemoryStream();
            var copy = process.StandardOutput.BaseStream.CopyToAsync(output);
            var error = process.StandardError.ReadToEndAsync();
            var watch = Stopwatch.StartNew();
            var exited = process.WaitForExit(TimeoutMilliseconds);
            var drained = exited && Task.WaitAll(new Task[] { copy, error }, Math.Max(0, TimeoutMilliseconds - (int)watch.ElapsedMilliseconds));
            if (!drained)
            {
                try { process.Kill(true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
                try { process.WaitForExit(1000); } catch (InvalidOperationException) { }
                return (128, Array.Empty<byte>(), "", true);
            }
            process.WaitForExit();
            return (process.ExitCode, output.ToArray(), error.Result ?? "", false);
        }
    }
}
