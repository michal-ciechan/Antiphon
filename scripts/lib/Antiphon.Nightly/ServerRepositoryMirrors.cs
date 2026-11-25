// CARD-1776 repository mirrors for Linux land checkouts. It replaced scripts/server2-repository-mirrors.ps1 (unit 52 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md, W8 slice C; nothing started the script by path); the CLI runs it as
//
//   antiphon-scripts server2-repository-mirrors --table-file <file> [--what-if | --execute]
//       [--token-file /run/antiphon/github-token/token] [--policy-file /run/antiphon/push-allow-list] [--repos-root /work/repos]
//
// (scripts/antiphon server2-repository-mirrors on sh, scripts\antiphon.cmd on the desktop).
//
// Default is --what-if. It prints the before/after table and does not call git or docker. --execute clones or fetches each row
// under --repos-root only after the mounted token file is readable and non-empty. A missing credential refuses with
// credential-absent before any git process. The Antiphon project's gitRepositoryUrl is empty; the table remote is the desktop
// checkout origin, recorded by the operator. Rollback restores the before column of Project.LocalRepositoryPath. This command does
// not rewrite the database and never prints a token: the token file is opened and one byte read to prove it is readable, as the
// script did, and its bytes go nowhere (git's credential helper reads the mount; no token is in argv or the environment).
//
// The table: one row per line, "<name>|<before>|<remote>" or "<name>|<before>|<after>|<remote>"; blank lines and lines starting
// with # are skipped. Layout: /work/repos/<name> on the production work volume. --execute writes the allow-list (each row's
// remote as https://github.com/<owner>/<repo>.git, lower case, one per line) through a temporary file beside it, then, row by row,
// runs `git -C <after> fetch --prune origin` when <after>/.git exists, else `git clone -- <remote> <after>` (git on PATH, on this
// process's console, with GIT_TERMINAL_PROMPT=0 added to the caller's environment).
//
// The script's steps, checks and lines are kept exactly, with PowerShell's string semantics where they decide a row: -match is a
// case-insensitive regex, -eq/-ne a case-insensitive invariant-culture comparison, and String.StartsWith/EndsWith with a string
// the current culture's comparison. Exit codes: 0 plan or ok, 2 refusal (one "SERVER2-MIRRORS refuse exit=2 reason=<reason>"
// line), 3 ambiguous-mode; 1 an I/O failure the script did not catch (one host.Error line; the script's terminating error);
// usage 2 (NightlyUsageException, the CLI). Files are IFileSystem's, git runs on the host's launcher, the link test and the git
// resolution are ServerRepositoryMirrorsSeams. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The server2-repository-mirrors command's options (server2-repository-mirrors.ps1's parameters, in kebab case).</summary>
    public sealed class ServerRepositoryMirrorsOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "server2-repository-mirrors --table-file <file> [--what-if | --execute] [--token-file " + DefaultTokenFile + "]"
            + " [--policy-file " + DefaultPolicyFile + "] [--repos-root " + DefaultReposRoot + "]";

        /// <summary>The mounted token file the script checked by default.</summary>
        public const string DefaultTokenFile = "/run/antiphon/github-token/token";

        /// <summary>The allow-list the script wrote by default.</summary>
        public const string DefaultPolicyFile = "/run/antiphon/push-allow-list";

        /// <summary>Layout: /work/repos/&lt;name&gt; on the production work volume.</summary>
        public const string DefaultReposRoot = "/work/repos";

        /// <summary>--what-if: print the plan (also the default when --execute is not given).</summary>
        public bool WhatIf { get; init; }

        /// <summary>--execute: write the allow-list and clone or fetch each row.</summary>
        public bool Execute { get; init; }

        /// <summary>--table-file: the operator's table.</summary>
        public string TableFile { get; init; } = "";

        /// <summary>--token-file: the mounted token file, checked readable and non-empty before any git.</summary>
        public string TokenFile { get; init; } = DefaultTokenFile;

        /// <summary>--policy-file: the allow-list written before any git.</summary>
        public string PolicyFile { get; init; } = DefaultPolicyFile;

        /// <summary>--repos-root: the mirrors' parent, POSIX rooted.</summary>
        public string ReposRoot { get; init; } = DefaultReposRoot;

        /// <summary>The caller's environment, which each git child gets with GIT_TERMINAL_PROMPT=0 (the script's $env: assignment). Null: none but that.</summary>
        public IReadOnlyDictionary<string, string> Environment { get; init; }

        /// <summary>The arguments after the command name. An empty value is taken as given (the script's checks refuse it).</summary>
        public static ServerRepositoryMirrorsOptions Parse(IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environment)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--table-file", "--token-file", "--policy-file", "--repos-root" }, new[] { "--what-if", "--execute" });
            return new ServerRepositoryMirrorsOptions
            {
                WhatIf = flags.Switch("--what-if"),
                Execute = flags.Switch("--execute"),
                TableFile = flags.Value("--table-file", ""),
                TokenFile = flags.Value("--token-file", DefaultTokenFile),
                PolicyFile = flags.Value("--policy-file", DefaultPolicyFile),
                ReposRoot = flags.Value("--repos-root", DefaultReposRoot),
                Environment = environment,
            };
        }
    }

    /// <summary>What the command observes outside its files and launcher.</summary>
    public sealed class ServerRepositoryMirrorsSeams
    {
        /// <summary>
        /// Get-Item's LinkType test on the token file: true for a symbolic link, a junction, any other reparse point, a hard link (a
        /// file with more than one name) or a node that cannot be read. Default: HistoryNodeIdentity on this machine.
        /// </summary>
        public Func<string, bool> IsLink { get; init; } = path =>
        {
            var node = HistoryNodeIdentity.Read(path, false);
            return node.Linked || node.Unreadable;
        };

        /// <summary>The call operator's application for git: its path, null when there is none. Default: OwnedLauncher.FindApplication.</summary>
        public Func<string, string> Resolve { get; init; } = OwnedLauncher.FindApplication;

        /// <summary>[guid]::NewGuid().ToString('N'): the temporary allow-list's suffix.</summary>
        public Func<string> NewId { get; init; } = () => Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
    }

    /// <summary>One table row as the script recorded it.</summary>
    public sealed record ServerRepositoryMirrorRow(string Name, string Before, string After, string Remote, string Identity);

    /// <summary>The CARD-1776 repository mirrors (server2-repository-mirrors.ps1).</summary>
    public static class ServerRepositoryMirrors
    {
        /// <summary>The last line of the plan and of a mirrored run.</summary>
        public const string RollbackLine = "rollback restores the before column";

        /// <summary>The plan's first line.</summary>
        public const string PlannedLine = "SERVER2-MIRRORS plan exit=0 reason=planned";

        /// <summary>A mirrored run's first line.</summary>
        public const string MirroredLine = "SERVER2-MIRRORS ok exit=0 reason=mirrored";

        private const string GitHub = "https://github.com/";

        // PowerShell's -match: a case-insensitive regex in the current culture.
        private static readonly Regex Token = new Regex("(?i)ghp_|github_pat_|gho_|ghu_|ghs_|ghr_", RegexOptions.IgnoreCase);
        private static readonly Regex UrlCredential = new Regex(@"://[^/\s]+:[^/\s]+@", RegexOptions.IgnoreCase);
        private static readonly Regex Slug = new Regex(@"^[a-z0-9][a-z0-9._-]{0,80}$", RegexOptions.IgnoreCase);
        private static readonly Regex Remote = new Regex(@"^https://github.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+(\.git)?$", RegexOptions.IgnoreCase);

        private sealed class MirrorStop : Exception
        {
            public MirrorStop(int code) => Code = code;

            public int Code { get; }
        }

        /// <summary>
        /// Runs the command: the plan (--what-if, the default) or the mirrors (--execute). Returns the exit code; every refusal is its
        /// one SERVER2-MIRRORS line on host.Out.
        /// </summary>
        public static int Run(ServerRepositoryMirrorsOptions options, NightlyHost host, IFileSystem files, ServerRepositoryMirrorsSeams seams = null)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            seams ??= new ServerRepositoryMirrorsSeams();
            try
            {
                return Mirror(options, host, files, seams);
            }
            catch (MirrorStop stop)
            {
                return stop.Code;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is Win32Exception || e is InvalidOperationException
                || e is ArgumentException || e is NotSupportedException || e is System.Security.SecurityException)
            {
                // The script stopped here with a terminating error ($ErrorActionPreference = 'Stop'): exit 1.
                host.Error.WriteLine(e.Message);
                return 1;
            }
        }

        /// <summary>
        /// The table's rows, refusing as the script did (secret-in-table, volume-forbidden, path-forbidden, remote-unrecorded,
        /// remote-not-https) with the reason; root is --repos-root already trimmed. Null reason: the rows are valid (possibly none).
        /// </summary>
        public static IReadOnlyList<ServerRepositoryMirrorRow> ReadRows(IEnumerable<string> lines, string root, out string refusal)
        {
            refusal = null;
            var rows = new List<ServerRepositoryMirrorRow>();
            foreach (var raw in lines ?? Array.Empty<string>())
            {
                var line = (raw ?? "").Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.CurrentCulture)) continue;
                if (IsSecret(line)) { refusal = "secret-in-table"; return null; }
                if (IsForbiddenVolume(line) || IsEscape(line))
                {
                    refusal = IsForbiddenVolume(line) ? "volume-forbidden" : "path-forbidden";
                    return null;
                }
                var parts = line.Split('|');
                string name, before, after = "", remote;
                if (parts.Length == 3)
                {
                    name = parts[0].Trim();
                    before = parts[1].Trim();
                    remote = parts[2].Trim();
                }
                else if (parts.Length == 4)
                {
                    name = parts[0].Trim();
                    before = parts[1].Trim();
                    after = parts[2].Trim();
                    remote = parts[3].Trim();
                }
                else { refusal = "path-forbidden"; return null; }

                if (remote.Length == 0) { refusal = "remote-unrecorded"; return null; }
                var slug = SlugOf(before);
                if (!Slug.IsMatch(slug)) { refusal = "path-forbidden"; return null; }
                if (!PsEquals(name, slug)) { refusal = "path-forbidden"; return null; }
                var derived = root + "/" + slug;
                if (after.Length == 0) after = derived;
                if (!PsEquals(after, derived)) { refusal = "path-forbidden"; return null; }
                if (!Remote.IsMatch(remote)) { refusal = "remote-not-https"; return null; }
                rows.Add(new ServerRepositoryMirrorRow(name, before, after, remote, Identity(remote)));
            }
            return rows;
        }

        /// <summary>Test-MirrorSecret: a GitHub token prefix anywhere, or a URL with user:password@.</summary>
        public static bool IsSecret(string value) => Token.IsMatch(value ?? "") || UrlCredential.IsMatch(value ?? "");

        /// <summary>Test-MirrorEscape: a ".." segment once backslashes read as slashes.</summary>
        public static bool IsEscape(string value)
        {
            foreach (var segment in (value ?? "").Replace("\\", "/").Split('/'))
                if (PsEquals(segment, "..")) return true;
            return false;
        }

        /// <summary>Test-MirrorForbiddenVolume: the Postgres, DinD or Windmill volumes.</summary>
        public static bool IsForbiddenVolume(string value)
        {
            var lower = (value ?? "").ToLowerInvariant();
            return lower.Contains("antiphon_pgdata", StringComparison.Ordinal) || lower.Contains("dind-data", StringComparison.Ordinal)
                || lower.Contains("windmill", StringComparison.Ordinal);
        }

        /// <summary>Get-MirrorSlug: the before column's last segment, lower case; empty for an empty path.</summary>
        public static string SlugOf(string before)
        {
            var normalized = (before ?? "").Replace("\\", "/").TrimEnd('/');
            if (normalized.Length == 0) return "";
            var parts = normalized.Split('/');
            return parts[parts.Length - 1].ToLowerInvariant();
        }

        /// <summary>Get-MirrorIdentity: https://github.com/ and the remote's path with .git, lower case (the allow-list entry).</summary>
        public static string Identity(string remote)
        {
            var path = remote.Substring(GitHub.Length);
            if (!path.EndsWith(".git", StringComparison.CurrentCulture)) path = path + ".git";
            return GitHub + path.ToLowerInvariant();
        }

        // PowerShell's -eq on strings: a case-insensitive comparison in the invariant culture.
        private static bool PsEquals(string left, string right) =>
            CultureInfo.InvariantCulture.CompareInfo.Compare(left, right, CompareOptions.IgnoreCase) == 0;

        private static void Stop(NightlyHost host, string kind, int code, string reason)
        {
            host.Out.WriteLine("SERVER2-MIRRORS " + kind + " exit=" + code.ToString(CultureInfo.InvariantCulture) + " reason=" + reason);
            throw new MirrorStop(code);
        }

        private static int Mirror(ServerRepositoryMirrorsOptions options, NightlyHost host, IFileSystem files, ServerRepositoryMirrorsSeams seams)
        {
            if (options.WhatIf && options.Execute) Stop(host, "refuse", 3, "ambiguous-mode");
            var planOnly = !options.Execute;

            var tableFile = options.TableFile ?? "";
            if (string.IsNullOrWhiteSpace(tableFile) || !files.File.Exists(tableFile)) Stop(host, "refuse", 2, "table-missing");

            var root = (options.ReposRoot ?? "").TrimEnd('/').TrimEnd('\\');
            if (root.Length == 0 || !root.StartsWith("/", StringComparison.CurrentCulture)) Stop(host, "refuse", 2, "path-forbidden");

            var rows = ReadRows(files.File.ReadAllLines(tableFile), root, out var refusal);
            if (refusal != null) Stop(host, "refuse", 2, refusal);
            if (rows.Count == 0) Stop(host, "refuse", 2, "table-missing");

            if (planOnly)
            {
                host.Out.WriteLine(PlannedLine);
                foreach (var row in rows)
                    host.Out.WriteLine("mirror name=" + row.Name + " before=" + row.Before + " after=" + row.After + " remote=" + row.Remote);
                host.Out.WriteLine(RollbackLine);
                return 0;
            }

            var environment = new Dictionary<string, string>(StringComparer.Ordinal);
            if (options.Environment != null)
                foreach (var pair in options.Environment)
                    if (!string.Equals(pair.Key, "GIT_TERMINAL_PROMPT", StringComparison.OrdinalIgnoreCase)) environment[pair.Key] = pair.Value;
            environment["GIT_TERMINAL_PROMPT"] = "0";

            if (!FileUsable(files, seams, options.TokenFile)) Stop(host, "refuse", 2, "credential-absent");
            var policyFile = options.PolicyFile ?? "";
            if (string.IsNullOrWhiteSpace(policyFile) || !files.Path.IsPathRooted(policyFile)) Stop(host, "refuse", 2, "credential-absent");
            foreach (var row in rows)
            {
                var parent = files.Path.GetDirectoryName(row.After);
                if (string.IsNullOrWhiteSpace(parent) || !files.Directory.Exists(parent)) Stop(host, "refuse", 2, "mirror-parent-missing");
            }
            var policyParent = files.Path.GetDirectoryName(policyFile);
            if (string.IsNullOrWhiteSpace(policyParent) || !files.Directory.Exists(policyParent)) Stop(host, "refuse", 2, "policy-unwritable");

            var policyTemp = policyFile + "." + seams.NewId() + ".tmp";
            var policyText = new StringBuilder();
            foreach (var row in rows)
            {
                if (policyText.Length > 0) policyText.Append('\n');
                policyText.Append(row.Identity);
            }
            policyText.Append('\n');
            files.File.WriteAllText(policyTemp, policyText.ToString());
            // Remove-Item without -Recurse: a file, or an empty directory; a directory with entries fails (exit 1).
            if (files.File.Exists(policyFile)) files.File.Delete(policyFile);
            else if (files.Directory.Exists(policyFile)) files.Directory.Delete(policyFile);
            files.File.Move(policyTemp, policyFile);

            foreach (var row in rows)
            {
                var gitDir = files.Path.Combine(row.After, ".git");
                if (files.File.Exists(gitDir) || files.Directory.Exists(gitDir))
                {
                    if (Git(host, seams, environment, "-C", row.After, "fetch", "--prune", "origin") != 0) Stop(host, "refuse", 2, "fetch-failed");
                }
                else
                {
                    // git clone -- <remote> <directory>; git fetch updates an existing mirror
                    if (Git(host, seams, environment, "clone", "--", row.Remote, row.After) != 0) Stop(host, "refuse", 2, "clone-failed");
                }
            }

            host.Out.WriteLine(MirroredLine);
            host.Out.WriteLine(RollbackLine);
            return 0;
        }

        // Test-MirrorFileUsable: rooted, a file, not a link, not empty, and one byte reads. The byte is discarded.
        private static bool FileUsable(IFileSystem files, ServerRepositoryMirrorsSeams seams, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (!files.Path.IsPathRooted(path)) return false;
            if (!files.File.Exists(path)) return false;
            if (seams.IsLink(path)) return false;
            if (files.FileInfo.New(path).Length <= 0) return false;
            try
            {
                using var stream = files.File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                return stream.ReadByte() >= 0;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException
                || e is System.Security.SecurityException)
            {
                return false;
            }
        }

        // `& git @arguments`: git as the call operator found it, on this console's streams, waited for; its exit code.
        private static int Git(NightlyHost host, ServerRepositoryMirrorsSeams seams, IReadOnlyDictionary<string, string> environment, params string[] arguments)
        {
            var git = seams.Resolve?.Invoke("git");
            if (git == null)
                throw new InvalidOperationException("The term 'git' is not recognized as a name of a cmdlet, function, script file, or executable program.");
            return host.Launch(new NightlyLaunch(git, arguments, "", Environment: environment)).ExitCode;
        }
    }
}
