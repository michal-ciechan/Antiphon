// Removes old alternate build outputs (bin-*) only with the producing process's creation records. It replaced the body of
// scripts/cleanup-build-junk.ps1 (unit 21 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md), which stays as the
// thin launcher Windmill's u/lndcobra/antiphon_build_junk_cleanup (Mon 09:00 Europe/London) starts by path; the CLI runs it as
// `antiphon-scripts cleanup-build-junk`.
//
// A bin-* name, its age or a disk owner marker alone grants no deletion authority. A separate process (the CLI, so the
// scheduled run) has no in-memory custody and leaves every output in place ("skip: producer creation records unavailable").
// A producer that holds its creation records calls Run in process with removeOwned: the shared ledger's removal
// (Antiphon.Checkpoints.CheckpointOutputLedger.Remove of its CheckpointCleanupContext, which refuses tracked, linked, changed
// or unrecorded entries and deletes recorded ones without recursive wildcard removal), answering null when it removed the
// directory or the reason it left it. The walk is Get-ChildItem -Directory -Recurse -Filter bin-* -Depth 3: depth first, the
// root's children and three levels below them, hidden and system directories skipped, links listed but not entered. Exit 0.
// Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;

namespace Antiphon.Nightly
{
    /// <summary>The cleanup-build-junk command's options (cleanup-build-junk.ps1 -RepoRoot -SkipIfModifiedWithinMinutes).</summary>
    public sealed class BuildJunkCleanupOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "cleanup-build-junk [--repo-root <dir>] [--skip-if-modified-within-minutes 60]";

        /// <summary>The checkout whose bin-* outputs are walked.</summary>
        public string RepoRoot { get; init; } = "";

        /// <summary>A directory written within this many minutes is left alone.</summary>
        public int SkipIfModifiedWithinMinutes { get; init; } = 60;

        /// <summary>The arguments after the command name; defaultRepoRoot is the checkout the CLI was built in.</summary>
        public static BuildJunkCleanupOptions Parse(IReadOnlyList<string> arguments, string defaultRepoRoot)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--repo-root", "--skip-if-modified-within-minutes" }, Array.Empty<string>());
            var root = flags.Value("--repo-root", "");
            return new BuildJunkCleanupOptions
            {
                RepoRoot = string.IsNullOrWhiteSpace(root) ? defaultRepoRoot ?? "" : root,
                SkipIfModifiedWithinMinutes = flags.Int("--skip-if-modified-within-minutes", 60),
            };
        }
    }

    /// <summary>The bin-* cleanup (cleanup-build-junk.ps1).</summary>
    public static class BuildJunkCleanup
    {
        /// <summary>How deep the walk goes below the root's children (Get-ChildItem -Depth 3).</summary>
        public const int Depth = 3;

        /// <summary>
        /// Walks options.RepoRoot and removes each old bin-* directory removeOwned removes. removeOwned null: no producer custody,
        /// nothing is walked or removed. Every line goes to host.Out; the last is "cleanup-build-junk: &lt;n&gt; dir(s) removed.".
        /// Returns the count removed.
        /// </summary>
        public static int Run(BuildJunkCleanupOptions options, NightlyHost host, IFileSystem files, Func<string, string> removeOwned)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            var removed = 0;
            if (removeOwned == null)
            {
                host.Out.WriteLine("skip: producer creation records unavailable; outputs left in place");
            }
            else
            {
                var cutoff = host.UtcNow.AddMinutes(-options.SkipIfModifiedWithinMinutes);
                var candidates = new List<string>();
                Walk(files, options.RepoRoot, 0, candidates);
                foreach (var directory in candidates)
                {
                    try
                    {
                        if (!files.Directory.Exists(directory)) continue;
                        if (files.Directory.GetLastWriteTimeUtc(directory) > cutoff)
                        {
                            host.Out.WriteLine("skip (recently modified): " + directory);
                            continue;
                        }
                        var reason = removeOwned(directory);
                        if (reason != null)
                        {
                            host.Out.WriteLine("skip: " + directory + " - " + reason);
                            continue;
                        }
                        host.Out.WriteLine("removed: " + directory);
                        removed++;
                    }
                    catch (Exception e)
                    {
                        host.Out.WriteLine("error (skipped): " + directory + " - " + e.Message);
                    }
                }
            }
            host.Out.WriteLine("cleanup-build-junk: " + removed + " dir(s) removed.");
            return removed;
        }

        // Depth first: each child directory in the file system's order, the bin-* ones listed before their own children.
        private static void Walk(IFileSystem files, string directory, int level, List<string> found)
        {
            foreach (var child in files.Directory.EnumerateDirectories(directory))
            {
                var attributes = files.File.GetAttributes(child);
                if ((attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                if (files.Path.GetFileName(child).StartsWith("bin-", StringComparison.OrdinalIgnoreCase)) found.Add(child);
                if (level < Depth && (attributes & FileAttributes.ReparsePoint) == 0) Walk(files, child, level + 1, found);
            }
        }
    }
}
