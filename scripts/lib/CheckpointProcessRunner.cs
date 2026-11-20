using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Antiphon.Checkpoints
{
    // Compiled by the tool and loaded by PowerShell: one exit-and-drain implementation.
    public static class CheckpointProcessRunner
    {
        public sealed class Result
        {
            public int Exit { get; set; }
            public string Stdout { get; set; } = "";
            public string Stderr { get; set; } = "";
            public bool TimedOut { get; set; }
        }

        public sealed class BytesResult
        {
            public int Exit { get; set; }
            public byte[] Stdout { get; set; } = Array.Empty<byte>();
            public string Stderr { get; set; } = "";
            public bool TimedOut { get; set; }
        }

        // An already-exited child lost the job-assignment race and still has a result.
        // A live child that cannot be assigned is process custody, not a timeout.
        // string, not string?: this file is also Add-Type'd without a nullable context.
        public static string CustodyRefusal(bool assigned, bool exited) =>
            assigned || exited ? null : "process custody unavailable";

        public static Result Run(ProcessStartInfo start, int timeoutMs, bool git, CancellationToken cancellationToken = default)
            => RunObserved(start, timeoutMs, git, null, cancellationToken);

        // Invocation-local observation keeps process-count witnesses on the actual launch path.
        internal static Result RunObserved(ProcessStartInfo start, int timeoutMs, bool git,
            Action<ProcessStartInfo> beforeStart, CancellationToken cancellationToken = default)
            => RunObserved(start, timeoutMs, git, beforeStart, -1, cancellationToken);

        // driverSettingsCacheMs below 0 means DriverSettingsCacheMilliseconds; tests pass their own.
        internal static Result RunObserved(ProcessStartInfo start, int timeoutMs, bool git,
            Action<ProcessStartInfo> beforeStart, int driverSettingsCacheMs, CancellationToken cancellationToken = default)
        {
            var result = RunCaptured(start, timeoutMs, git, ReadTextAsync, cancellationToken, beforeStart, driverSettingsCacheMs);
            return new Result { Exit = result.Exit, Stdout = result.Stdout ?? "", Stderr = result.Stderr, TimedOut = result.TimedOut };
        }

        public static BytesResult RunBytes(ProcessStartInfo start, int timeoutMs, bool git, CancellationToken cancellationToken = default)
        {
            var result = RunCaptured(start, timeoutMs, git, ReadBytesAsync, cancellationToken);
            return new BytesResult { Exit = result.Exit, Stdout = result.Stdout ?? Array.Empty<byte>(), Stderr = result.Stderr, TimedOut = result.TimedOut };
        }

        private sealed class Captured<T>
        {
            public int Exit { get; set; }
            public T Stdout { get; set; } = default!;
            public string Stderr { get; set; } = "";
            public bool TimedOut { get; set; }
        }

        private static Captured<T> RunCaptured<T>(ProcessStartInfo start, int timeoutMs, bool git,
            Func<Process, CancellationToken, Task<T>> readStdout, CancellationToken cancellationToken,
            Action<ProcessStartInfo> beforeStart = null, int driverSettingsCacheMs = -1)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeoutMs);
            try
            {
                if (git)
                {
                    // This exact index-only query never reads file contents or invokes
                    // filter/textconv drivers. Keep its per-entry Git revalidation, without
                    // starting a second process to discover drivers it cannot invoke.
                    var arguments = start.ArgumentList;
                    var indexOnly = arguments.Count >= 6 && arguments[0] == "-C"
                        && arguments[2] == "ls-files" && arguments[3] == "-z" && arguments[4] == "--";
                    HardenGit(start);
                    if (indexOnly)
                        return RunCoreAsync(start, readStdout, deadline.Token, beforeStart).GetAwaiter().GetResult();
                    // Disable named filter/textconv drivers too. Config inspection itself has the
                    // same whole-operation deadline and cannot invoke these drivers.
                    var config = new ProcessStartInfo(start.FileName) { WorkingDirectory = start.WorkingDirectory };
                    foreach (var pair in start.Environment) config.Environment[pair.Key] = pair.Value;
                    HardenGit(config);
                    // The read's whole leading option block (-c, -C, --git-dir, --work-tree, ...): the lookup reads
                    // the repository and the config the read will use (a caller's -c include.path included).
                    var leading = ReadLeadingOptions(start.ArgumentList);
                    foreach (var option in leading.Prefix) config.ArgumentList.Add(option);
                    config.ArgumentList.Add("config"); config.ArgumentList.Add("--null");
                    config.ArgumentList.Add("--get-regexp");
                    config.ArgumentList.Add("^(filter\\..*\\.(clean|smudge|process|required)|diff\\..*\\.textconv)$");
                    var settings = DriverSettings(config, driverSettingsCacheMs < 0 ? DriverSettingsCacheMilliseconds : driverSettingsCacheMs, deadline.Token, beforeStart, !leading.Other);
                    // The disabling pairs go after the read's own leading options, just before its subcommand: git
                    // applies command-line config in order, so a caller's -c (an include naming a driver) cannot
                    // switch a driver back on after it.
                    var at = leading.Prefix.Count;
                    foreach (var entry in settings.Split('\0'))
                    {
                        var split = entry.IndexOf('\n');
                        if (split < 1) continue;
                        var key = entry.Substring(0, split);
                        if (!(key.StartsWith("filter.", StringComparison.OrdinalIgnoreCase) || key.StartsWith("diff.", StringComparison.OrdinalIgnoreCase))) continue;
                        start.ArgumentList.Insert(at++, "-c");
                        start.ArgumentList.Insert(at++, key + (key.EndsWith(".required", StringComparison.OrdinalIgnoreCase) ? "=false" : "="));
                    }
                }
                return RunCoreAsync(start, readStdout, deadline.Token, beforeStart).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { return new Captured<T> { Exit = 128, TimedOut = true }; }
        }

        // How long one driver-settings answer is reused for the same git, directory and environment.
        // 0 (the default) asks git before every call. The release-selection worker turns it on for its
        // own short life: a config change made while it runs is then seen by the next worker.
        public static int DriverSettingsCacheMilliseconds { get; set; }

        private static readonly object DriverSettingsGate = new object();
        private static readonly Dictionary<string, KeyValuePair<long, string>> DriverSettingsCache = new Dictionary<string, KeyValuePair<long, string>>();
        private static readonly AsyncLocal<Dictionary<string, string>> ScopedDriverSettings = new AsyncLocal<Dictionary<string, string>>();

        // One driver-settings answer per scope for the same git, directory, environment and query:
        // a source snapshot's back-to-back reads ask git once instead of before every read. Nothing the
        // checkpoint starts runs inside a snapshot, and a process still alive then can already run code
        // itself, so reusing the answer lets no build-written config run a driver. Each new scope (the
        // next snapshot, after the build) asks again. Callers dispose the scope; it does not outlive them.
        // A scope opened inside an active one joins it (same read block): only the outermost scope owns the
        // answers, so there is never more than one scope to close or revive.
        public static IDisposable DriverSettingsScope()
        {
            if (ScopedDriverSettings.Value != null) return JoinedScope.Instance;
            var own = new Dictionary<string, string>(StringComparer.Ordinal);
            ScopedDriverSettings.Value = own;
            return new DriverSettingsScopeExit(own);
        }

        private sealed class JoinedScope : IDisposable
        {
            public static readonly JoinedScope Instance = new JoinedScope();
            public void Dispose() { }
        }

        private sealed class DriverSettingsScopeExit : IDisposable
        {
            private readonly Dictionary<string, string> _own;
            private bool _disposed;
            public DriverSettingsScopeExit(Dictionary<string, string> own) { _own = own; }
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                // Disposed from another flow: leave that flow's value alone. Either way the answers are dropped.
                if (ReferenceEquals(ScopedDriverSettings.Value, _own)) ScopedDriverSettings.Value = null;
                lock (DriverSettingsGate) _own.Clear();
            }
        }

        // The directory part of a driver-settings key: the same checkout spelled C:\repo, C:/repo or C:\Repo\
        // is one answer (git's config does not depend on the spelling). A subfolder stays a separate key.
        private static string DirectoryKey(string directory)
        {
            if (string.IsNullOrEmpty(directory)) return "";
            try { directory = Path.GetFullPath(directory).TrimEnd('\\', '/'); }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException || ex is System.Security.SecurityException) { }
            return OperatingSystem.IsWindows() ? directory.ToUpperInvariant() : directory;
        }

        private static string DriverSettings(ProcessStartInfo config, int lifetime, CancellationToken cancellationToken, Action<ProcessStartInfo> beforeStart, bool filesMayAnswer)
        {
            string key = null;
            var scoped = ScopedDriverSettings.Value;
            if (lifetime > 0 || scoped != null)
            {
                var parts = new List<string> { config.FileName, DirectoryKey(config.WorkingDirectory) };
                foreach (var pair in config.Environment) parts.Add(pair.Key + "=" + pair.Value);
                parts.Sort(2, parts.Count - 2, StringComparer.Ordinal);
                parts.AddRange(config.ArgumentList);
                key = string.Join("\0", parts);
                lock (DriverSettingsGate)
                {
                    if (scoped != null && scoped.TryGetValue(key, out var inScope))
                        return inScope;
                    if (lifetime > 0 && DriverSettingsCache.TryGetValue(key, out var cached) && Environment.TickCount64 - cached.Key < lifetime)
                        return cached.Value;
                }
            }
            if (filesMayAnswer && NoDriverConfigured(config))
            {
                if (key != null)
                    lock (DriverSettingsGate)
                    {
                        if (lifetime > 0) DriverSettingsCache[key] = new KeyValuePair<long, string>(Environment.TickCount64, "");
                        if (scoped != null) scoped[key] = "";
                    }
                return "";
            }
            var settings = RunCoreAsync(config, ReadTextAsync, cancellationToken, beforeStart).GetAwaiter().GetResult();
            var stdout = settings.Stdout ?? "";
            // Exit 1 is "no driver configured"; anything else is not an answer worth keeping.
            if (key != null && !settings.TimedOut && (settings.Exit == 0 || settings.Exit == 1))
                lock (DriverSettingsGate)
                {
                    if (lifetime > 0) DriverSettingsCache[key] = new KeyValuePair<long, string>(Environment.TickCount64, stdout);
                    if (scoped != null) scoped[key] = stdout;
                }
            return stdout;
        }

        // The global options a git command line opens with, up to its subcommand: all of them as given (Prefix),
        // the -c settings and -C directories among them in order, and whether any other global option (--git-dir,
        // --work-tree, --no-pager, ...) is there too.
        private sealed class LeadingOptions
        {
            public readonly List<string> Prefix = new List<string>();
            public readonly List<string> Settings = new List<string>();
            public readonly List<string> Directories = new List<string>();
            public bool Other;
        }

        private static LeadingOptions ReadLeadingOptions(IList<string> arguments)
        {
            var options = new LeadingOptions();
            for (var i = 0; i < arguments.Count; i++)
            {
                var argument = arguments[i];
                if (!argument.StartsWith("-", StringComparison.Ordinal)) break;
                options.Prefix.Add(argument);
                // Global options whose value is the next argument.
                if ((argument == "-c" || argument == "-C" || argument == "--git-dir" || argument == "--work-tree"
                    || argument == "--namespace" || argument == "--config-env") && i + 1 < arguments.Count)
                {
                    var value = arguments[++i];
                    options.Prefix.Add(value);
                    if (argument == "-c") options.Settings.Add(value);
                    else if (argument == "-C") options.Directories.Add(value);
                    else options.Other = true;
                    continue;
                }
                options.Other = true;
            }
            return options;
        }

        private static readonly System.Text.RegularExpressions.Regex HeadValue = new System.Text.RegularExpressions.Regex(
            "^(ref: refs/.+|[0-9a-f]{40}|[0-9a-f]{64})$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        // Whether git accepts this directory as a repository's git dir: a valid HEAD (a ref under refs/ or a full
        // object id), and objects and refs (or a commondir that holds them, for a linked worktree's git dir).
        private static bool LooksLikeGitDir(string directory)
        {
            var head = Path.Combine(directory, "HEAD");
            if (!File.Exists(head) || !HeadValue.IsMatch(File.ReadAllText(head).Trim())) return false;
            return File.Exists(Path.Combine(directory, "commondir"))
                || (Directory.Exists(Path.Combine(directory, "objects")) && Directory.Exists(Path.Combine(directory, "refs")));
        }

        // A path segment "..": after a link, git's chdir lands in the link target's parent while text collapse does
        // not, so such a path is git's to resolve.
        private static bool HasParentSegment(string path) =>
            path.Split('\\', '/').Any(segment => segment == "..");

        // Whether a directory or any of its ancestors is a link (reparse point).
        private static bool AnyLinkOnPath(string directory)
        {
            for (var current = new DirectoryInfo(directory); current != null; current = current.Parent)
                if (!current.Exists || (current.Attributes & FileAttributes.ReparsePoint) != 0) return true;
            return false;
        }

        // A -c setting a caller passes that can name a driver or pull in more config.
        private static bool NamesDriverOrInclude(string setting)
        {
            var equals = setting.IndexOf('=');
            var key = (equals < 0 ? setting : setting.Substring(0, equals)).ToLowerInvariant();
            return key.StartsWith("include", StringComparison.Ordinal) || key.StartsWith("filter.", StringComparison.Ordinal)
                || (key.StartsWith("diff.", StringComparison.Ordinal) && key.EndsWith(".textconv", StringComparison.Ordinal));
        }

        // The repository git finds from this directory, as far as its files settle it the way git's discovery does:
        // each directory's .git first, then the directory itself as a git dir. Its git dir, its top level (null for a
        // git dir found as the directory itself: bare, or a directory inside .git) and the config files git reads
        // there. Null for any doubt: a link on the path (git climbs the real path), a .git git might skip or read
        // differently, a gitdir: pointer or commondir it would resolve after a link, no repository at all.
        private sealed class RepositoryFiles
        {
            public string GitDir;
            public string TopLevel;
            public string CommonDir;
            public List<string> ConfigFiles;
        }

        private static RepositoryFiles FindRepositoryFiles(string start)
        {
            string gitDir = null;
            string top = null;
            for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
            {
                if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0) return null;
                var here = directory.FullName;
                var dotGit = Path.Combine(here, ".git");
                if (Directory.Exists(dotGit))
                {
                    if ((File.GetAttributes(dotGit) & FileAttributes.ReparsePoint) != 0 || !LooksLikeGitDir(dotGit)) return null;
                    gitDir = dotGit;
                    top = here;
                    break;
                }
                if (File.Exists(dotGit))
                {
                    // Exactly git's gitfile form (read_gitfile): one "gitdir: <path>" line; anything else is git's.
                    var pointer = File.ReadAllText(dotGit).TrimEnd('\r', '\n');
                    if (!pointer.StartsWith("gitdir: ", StringComparison.Ordinal) || pointer.IndexOfAny(new[] { '\r', '\n' }) >= 0) return null;
                    var target = pointer.Substring("gitdir: ".Length);
                    if (target.Length == 0) return null;
                    if (HasParentSegment(target)) return null;
                    gitDir = Path.GetFullPath(Path.Combine(here, target));
                    if (AnyLinkOnPath(gitDir) || !LooksLikeGitDir(gitDir)) return null;
                    top = here;
                    break;
                }
                // A git dir itself (bare, or a directory inside .git): git uses it as the repository.
                if (LooksLikeGitDir(here)) { gitDir = here; break; }
            }
            if (gitDir == null) return null;
            var files = new List<string> { Path.Combine(gitDir, "config"), Path.Combine(gitDir, "config.worktree") };
            var commonDir = gitDir;
            var commonPointer = Path.Combine(gitDir, "commondir");
            if (File.Exists(commonPointer))
            {
                // gitDir's path has no link on it (checked above), so collapsing the leading "../" run git
                // writes ("../..") lands where git's does; a ".." anywhere after that is git's to resolve.
                var commonText = File.ReadAllText(commonPointer).Trim();
                if (commonText.Split('/', '\\').SkipWhile(segment => segment == "..").Any(segment => segment == "..")) return null;
                var common = Path.GetFullPath(Path.Combine(gitDir, commonText));
                if (AnyLinkOnPath(common)) return null;
                files.Add(Path.Combine(common, "config"));
                files.Add(Path.Combine(common, "config.worktree"));
                commonDir = common;
            }
            return new RepositoryFiles { GitDir = gitDir, TopLevel = top, CommonDir = commonDir, ConfigFiles = files };
        }

        // The one "bare" line git init writes for a checkout. Any other mention of "bare" (true in any form git
        // accepts, valueless, quoted, on its section header's line) is a repository that may have no work tree.
        private static readonly System.Text.RegularExpressions.Regex PlainBareFalse = new System.Text.RegularExpressions.Regex(
            @"^[ \t]*bare[ \t]*=[ \t]*false[ \t]*$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline
            | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        private static bool SaysBare(string config) =>
            PlainBareFalse.Replace(config.Replace("\r\n", "\n"), "").IndexOf("bare", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// [top level, absolute git dir] for this directory as `git rev-parse --show-toplevel --absolute-git-dir`
        /// names them (this process's full paths, not git's forward-slash spelling), read from the repository's
        /// files with no git child. Null when git must answer: a variable that moves git's repository or discovery
        /// (GIT_DIR, GIT_WORK_TREE, GIT_COMMON_DIR, GIT_CEILING_DIRECTORIES, GIT_DISCOVERY_ACROSS_FILESYSTEM), a ".."
        /// in the path, any doubt FindRepositoryFiles has, a git dir with no work tree (bare, or a directory inside
        /// .git), config text that mentions "worktree" (core.worktree, extensions.worktreeConfig), "include" (an
        /// included file the scan does not read) or "bare" anywhere but git init's own "bare = false" line (a remote
        /// URL with that word included: declining only costs a git child), or, off Windows, a start below its top
        /// level (git stops at filesystem boundaries the files cannot show). git rejects a repository it does not
        /// trust (safe.directory) at its next read.
        /// </summary>
        public static string[] FindRepository(string start)
        {
            var found = FindRepositoryChecked(start);
            return found == null ? null : new[] { found.TopLevel, found.GitDir };
        }

        private static RepositoryFiles FindRepositoryChecked(string start)
        {
            try
            {
                foreach (var name in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_COMMON_DIR", "GIT_CEILING_DIRECTORIES", "GIT_DISCOVERY_ACROSS_FILESYSTEM" })
                    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name))) return null;
                if (string.IsNullOrEmpty(start) || HasParentSegment(start)) return null;
                var full = Path.GetFullPath(start);
                var found = FindRepositoryFiles(full);
                if (found == null || found.TopLevel == null) return null;
                // Off Windows git stops climbing at a filesystem boundary (no GIT_DISCOVERY_ACROSS_FILESYSTEM), which
                // these files cannot see: there, only a directory that is itself the top level is answered. On
                // Windows a mounted folder is a reparse point, already declined.
                if (!OperatingSystem.IsWindows()
                    && !string.Equals(found.TopLevel.TrimEnd('/'), full.TrimEnd('/'), StringComparison.Ordinal)) return null;
                foreach (var file in found.ConfigFiles)
                {
                    if (Directory.Exists(file)) return null;
                    if (!File.Exists(file)) continue;
                    var text = File.ReadAllText(file);
                    if (text.IndexOf("worktree", StringComparison.OrdinalIgnoreCase) >= 0
                        || text.IndexOf("include", StringComparison.OrdinalIgnoreCase) >= 0 || SaysBare(text)) return null;
                }
                return found;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException
                || ex is NotSupportedException || ex is System.Security.SecurityException)
            {
                return null;
            }
        }

        private static bool SamePath(string a, string b) => string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

        private static readonly System.Text.RegularExpressions.Regex FullObjectId = new System.Text.RegularExpressions.Regex(
            "^(?:[0-9a-f]{40}|[0-9a-f]{64})$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        // FindRepository's repository, only when this directory is itself its top level (where git run here prints
        // root-relative answers); null otherwise.
        private static RepositoryFiles AtTopLevel(string start)
        {
            var found = FindRepositoryChecked(start);
            return found != null && SamePath(found.TopLevel, start) ? found : null;
        }

        /// <summary>
        /// [top level, git dir] when this directory is itself the top level of a repository FindRepository settles
        /// from its files; null otherwise (git answers).
        /// </summary>
        public static string[] TopLevelRepository(string start)
        {
            var found = AtTopLevel(start);
            return found == null ? null : new[] { found.TopLevel, found.GitDir };
        }

        /// <summary>
        /// `git rev-parse HEAD` at this checkout's top level, read from its files: the commit HEAD names, directly
        /// (detached) or through a branch ref (refs/heads/...), loose in the common dir or else in packed-refs. Null
        /// when git must answer: not a top level FindRepository settles, a reftable repository, an unborn branch, a
        /// HEAD naming anything but a branch, a ref that is itself symbolic or not a full object id.
        /// </summary>
        public static string ReadHead(string topLevel)
        {
            try
            {
                var found = AtTopLevel(topLevel);
                if (found == null || Directory.Exists(Path.Combine(found.CommonDir, "reftable"))) return null;
                var head = File.ReadAllText(Path.Combine(found.GitDir, "HEAD")).TrimEnd('\r', '\n');
                if (FullObjectId.IsMatch(head)) return head;
                if (!head.StartsWith("ref: refs/heads/", StringComparison.Ordinal)) return null;
                var name = head.Substring("ref: ".Length);
                if (name.IndexOf('\\') >= 0 || HasParentSegment(name) || name.EndsWith("/", StringComparison.Ordinal)) return null;
                var loose = Path.Combine(found.CommonDir, name.Replace('/', Path.DirectorySeparatorChar));
                if (Directory.Exists(loose)) return null;
                if (File.Exists(loose))
                {
                    var value = File.ReadAllText(loose).TrimEnd('\r', '\n');
                    return FullObjectId.IsMatch(value) ? value : null;
                }
                var packed = Path.Combine(found.CommonDir, "packed-refs");
                if (!File.Exists(packed)) return null;
                foreach (var line in File.ReadLines(packed))
                {
                    if (line.Length == 0 || line[0] == '#' || line[0] == '^') continue;
                    var space = line.IndexOf(' ');
                    if (space > 0 && string.Equals(line.Substring(space + 1), name, StringComparison.Ordinal))
                    {
                        var oid = line.Substring(0, space);
                        return FullObjectId.IsMatch(oid) ? oid : null;
                    }
                }
                return null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException
                || ex is NotSupportedException || ex is System.Security.SecurityException)
            {
                return null;
            }
        }

        // Whether the driver lookup's answer is empty without asking git (the read's leading options are all -c and
        // -C; DriverSettings does not ask otherwise). The lookup is hardened (GIT_CONFIG_NOSYSTEM,
        // GIT_CONFIG_GLOBAL on the null device), so git reads only the repository's own config files: config in its
        // git dir and common dir, config.worktree, and whatever they include. When the repository git would find has
        // files that never mention "filter", "diff" or "include", no key can match the lookup
        // (filter.*.clean|smudge|process|required, diff.*.textconv) and git would print nothing. The search follows
        // git's: from the read's directory (its -C), each directory's .git first, then the directory itself as a git
        // dir. Any doubt asks git: an executable other than plain git, a variable that redirects git's repository or
        // config, a caller -c naming a driver or an include, a ".." in the directories (git resolves it after links),
        // a link anywhere on the path (git climbs the real path), a .git git might skip or read differently (invalid
        // HEAD, no objects and refs), no repository, an unreadable file, or one of those words anywhere in the files
        // (a URL containing "diff" included).
        private static bool NoDriverConfigured(ProcessStartInfo config)
        {
            try
            {
                var executable = config.FileName ?? "";
                var plainGit = OperatingSystem.IsWindows()
                    ? string.Equals(executable, "git", StringComparison.OrdinalIgnoreCase) || string.Equals(executable, "git.exe", StringComparison.OrdinalIgnoreCase)
                    : executable == "git";
                if (!plainGit) return false;
                var nullPath = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
                if (!config.Environment.TryGetValue("GIT_CONFIG_NOSYSTEM", out var noSystem) || noSystem != "1") return false;
                if (!config.Environment.TryGetValue("GIT_CONFIG_GLOBAL", out var global)
                    || !string.Equals(global, nullPath, StringComparison.OrdinalIgnoreCase)) return false;
                foreach (var name in new[] { "GIT_DIR", "GIT_COMMON_DIR", "GIT_WORK_TREE", "GIT_CONFIG", "GIT_CONFIG_PARAMETERS", "GIT_CONFIG_COUNT", "GIT_CEILING_DIRECTORIES", "GIT_DISCOVERY_ACROSS_FILESYSTEM" })
                    if (config.Environment.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value)) return false;
                var leading = ReadLeadingOptions(config.ArgumentList);
                if (leading.Other || leading.Settings.Any(NamesDriverOrInclude)) return false;
                var working = string.IsNullOrEmpty(config.WorkingDirectory) ? Environment.CurrentDirectory : config.WorkingDirectory;
                if (HasParentSegment(working) || leading.Directories.Any(HasParentSegment)) return false;
                var start = Path.GetFullPath(working);
                foreach (var directory in leading.Directories)
                    start = Path.GetFullPath(Path.Combine(start, directory));
                var found = FindRepositoryFiles(start);
                if (found == null) return false;
                var files = found.ConfigFiles;
                var read = false;
                foreach (var file in files)
                {
                    if (Directory.Exists(file)) return false;
                    if (!File.Exists(file)) continue;
                    var text = File.ReadAllText(file);
                    if (text.IndexOf("filter", StringComparison.OrdinalIgnoreCase) >= 0
                        || text.IndexOf("diff", StringComparison.OrdinalIgnoreCase) >= 0
                        || text.IndexOf("include", StringComparison.OrdinalIgnoreCase) >= 0)
                        return false;
                    read = true;
                }
                return read;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException
                || ex is NotSupportedException || ex is System.Security.SecurityException)
            {
                return false;
            }
        }

        private static Task<string> ReadTextAsync(Process process, CancellationToken cancellationToken) =>
            process.StandardOutput.ReadToEndAsync(cancellationToken);

        private static async Task<byte[]> ReadBytesAsync(Process process, CancellationToken cancellationToken)
        {
            using var output = new MemoryStream();
            await process.StandardOutput.BaseStream.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            return output.ToArray();
        }

        private static void HardenGit(ProcessStartInfo start)
        {
            var nullPath = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
            foreach (var name in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_CONFIG", "GIT_CONFIG_PARAMETERS", "GIT_CONFIG_COUNT" }) start.Environment.Remove(name);
            start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
            start.Environment["GIT_NO_REPLACE_OBJECTS"] = "1";
            start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            start.Environment["GIT_CONFIG_GLOBAL"] = nullPath;
            // Disable fsmonitor, hooks, external diff, attributes, auto-gc and
            // auto-maintenance for this child. These settings do not identify or
            // remove a Windows pipe holder. Desktop C1313_BaselineAuthority still
            // has to read holder= before that hang can be called fixed.
            foreach (var setting in new[] { "core.fsmonitor=false", "core.hooksPath=" + nullPath, "diff.external=", "core.attributesFile=" + nullPath, "gc.auto=0", "maintenance.auto=false" })
            { start.ArgumentList.Insert(0, setting); start.ArgumentList.Insert(0, "-c"); }
        }

        private static async Task<Captured<T>> RunCoreAsync<T>(ProcessStartInfo start,
            Func<Process, CancellationToken, Task<T>> readStdout, CancellationToken cancellationToken,
            Action<ProcessStartInfo> beforeStart)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (OperatingSystem.IsLinux())
            {
                var executable = start.FileName;
                var exists = File.Exists(executable);
                if (!exists && !executable.Contains("/"))
                    foreach (var directory in (start.Environment["PATH"] ?? "").Split(Path.PathSeparator))
                        if (File.Exists(Path.Combine(directory, executable))) { exists = true; break; }
                if (!exists) throw new System.ComponentModel.Win32Exception(2);
            }
            start.UseShellExecute = false; start.RedirectStandardOutput = true; start.RedirectStandardError = true; start.CreateNoWindow = true;
            if (OperatingSystem.IsLinux())
            {
                // A private session keeps descendants addressable after the direct child exits.
                var executable = start.FileName; start.FileName = "setsid";
                start.ArgumentList.Insert(0, executable);
            }
            using var process = new Process { StartInfo = start };
            IntPtr job = IntPtr.Zero;
            beforeStart?.Invoke(start);
            if (!process.Start()) throw new InvalidOperationException("process did not start");
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    job = CreateJobObject(IntPtr.Zero, null);
                    var assigned = job != IntPtr.Zero && AssignProcessToJobObject(job, process.Handle);
                    var refusal = CustodyRefusal(assigned, Exited(process));
                    if (refusal is not null)
                        throw new InvalidOperationException(refusal);
                }
                var stdout = readStdout(process, cancellationToken);
                var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
                // This token covers BOTH direct-child exit and inherited redirected descriptors.
                await Task.WhenAll(process.WaitForExitAsync(cancellationToken), stdout, stderr).WaitAsync(cancellationToken).ConfigureAwait(false);
                return new Captured<T> { Exit = process.ExitCode, Stdout = await stdout.ConfigureAwait(false), Stderr = await stderr.ConfigureAwait(false) };
            }
            finally
            {
                if (OperatingSystem.IsLinux()) Kill(-process.Id, 9);
                if (job != IntPtr.Zero) { TerminateJobObject(job, 128); CloseHandle(job); }
                try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
                // Abandon pipes on expiry; never synchronously join a drain here.
                try { if (!process.HasExited) process.WaitForExit(1000); } catch (InvalidOperationException) { }
            }
        }

        private static bool Exited(Process process)
        {
            try { return process.HasExited; }
            catch (InvalidOperationException) { return false; }
        }

        [DllImport("libc", EntryPoint = "kill")] private static extern int Kill(int pid, int signal);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateJobObject(IntPtr attributes, string name);
        [DllImport("kernel32.dll")] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll")] private static extern bool TerminateJobObject(IntPtr job, uint exitCode);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    }
}
