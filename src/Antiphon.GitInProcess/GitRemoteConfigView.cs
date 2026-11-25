using System.Collections.Concurrent;
using System.Diagnostics;
using LibGit2Sharp;

namespace Antiphon.GitInProcess;

/// <summary>
/// What a git child reads from its own non-repository config levels (system, global/XDG and command) that could
/// decide or break <c>remote get-url</c>: remote.*, branch.*, url.*, include.* and includeIf.* entries, and the
/// safe.* entries, asked of git once per distinct child environment and cached until one of the files it rests on
/// changes. Separate from <see cref="GitStatusConfigAgreement"/>, whose status view and cache it neither reads nor
/// changes.
/// <para>
/// The view rests on files stamped before and after git lists them: the child's system file (named by
/// <c>git var GIT_CONFIG_SYSTEM</c> unless the environment names it or turns it off), every global candidate (Git
/// for Windows takes HOME, else HOMEDRIVE+HOMEPATH when that directory exists, else USERPROFILE; each candidate's
/// <c>.gitconfig</c> and <c>.config/git/config</c>, and XDG_CONFIG_HOME's), every file git names as an origin, and
/// whether each candidate home directory exists. A stamp that differs between the two means the listing may be from
/// either side of an edit: nothing is returned or cached. A candidate file that exists but cannot be read is git's
/// (it warns or dies): nothing is returned or cached. A stamp is length and write time, plus a content hash of a
/// file up to 1 MiB that is read only while its write time is within two seconds of the stamp (a rewrite inside one
/// timestamp tick); a fresh check is otherwise a stat and an open handle (nothing read) per file, since a lock or ACL change that
/// makes git warn leaves the write time alone.
/// </para>
/// <para>
/// safe.* trade-off: the child's safe.* entries must equal those libgit2 reads at its own system, global and XDG
/// levels, or git answers (a safe.directory refusal is git's to give). A host whose server process reads
/// <c>safe.directory=*</c> that its git children do not (or the reverse) therefore always falls back to git.
/// </para>
/// </summary>
internal static class GitRemoteConfigView
{
    private static readonly string[] EnvironmentNames =
    [
        "HOME", "USERPROFILE", "HOMEDRIVE", "HOMEPATH", "XDG_CONFIG_HOME", "APPDATA", "PROGRAMDATA", "PATH",
        "GIT_CONFIG", "GIT_CONFIG_GLOBAL", "GIT_CONFIG_SYSTEM", "GIT_CONFIG_NOSYSTEM", "GIT_CONFIG_PARAMETERS",
        "GIT_CONFIG_COUNT",
    ];

    /// <summary>Paths a child resolves against its own working directory when relative, which the probe's would not match.</summary>
    private static readonly string[] PathVariables = ["GIT_CONFIG_GLOBAL", "GIT_CONFIG_SYSTEM", "HOME", "XDG_CONFIG_HOME"];

    /// <summary>Test seam: runs between git's listing of the child's config and the stamps taken after it.</summary>
    internal static readonly AsyncLocal<Action?> AfterListing = new();

    private enum StampKind { Absent, File, Directory }

    /// <summary>A file's or home directory's state; <see cref="Hash"/> only for a file up to <see cref="HashedLength"/>.</summary>
    private sealed record Stamp(string Path, StampKind Kind, long Length, DateTime Written, DateTime Taken, string? Hash)
    {
        public bool Racy => Kind == StampKind.File && Written > Taken - RacyWindow;
        public bool SameState(Stamp other) =>
            Path == other.Path && Kind == other.Kind && Length == other.Length && Written == other.Written && Hash == other.Hash;
    }

    private sealed record Entry(string Scope, string Key, string? Value);

    private sealed record View(IReadOnlyList<Entry> Entries, Stamp[] Stamps, int HomeCount);

    private static readonly ConcurrentDictionary<string, View> Views = new(StringComparer.Ordinal);
    private const int ViewLimit = 256;
    private const long HashedLength = 1 << 20;
    private static readonly TimeSpan RacyWindow = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Null when nothing in the system, global or command levels of a git child with exactly
    /// <paramref name="childEnvironment"/> could decide a remote's URL or make git's remote.c die or warn, and its safe.*
    /// entries equal those libgit2 reads outside the repository in <paramref name="config"/>; otherwise why git must
    /// answer. Allowed there: branch.sort, branch.autosetupmerge and branch.autosetuprebase with values git accepts,
    /// and remote.pushdefault with a value. Any remote.&lt;name&gt;.*, branch.&lt;name&gt;.*, url.*, include.* or includeIf.*
    /// key refuses, as do GIT_CONFIG_PARAMETERS, GIT_CONFIG, an unparsable GIT_CONFIG_NOSYSTEM, and an empty or relative
    /// GIT_CONFIG_GLOBAL, GIT_CONFIG_SYSTEM, HOME or XDG_CONFIG_HOME.
    /// </summary>
    internal static string? Refusal(IReadOnlyDictionary<string, string?> childEnvironment, Configuration config)
    {
        string? Env(string name) => childEnvironment.TryGetValue(name, out var v) ? v : null;
        if (!string.IsNullOrEmpty(Env("GIT_CONFIG_PARAMETERS"))) return "GIT_CONFIG_PARAMETERS is set";
        if (!string.IsNullOrEmpty(Env("GIT_CONFIG"))) return "GIT_CONFIG is set";
        foreach (var name in PathVariables)
            if (Env(name) is { } value && !IsNullDevice(value) && (value.Length == 0 || !Path.IsPathFullyQualified(value)))
                return name + " is empty or relative";
        if (Env("GIT_CONFIG_NOSYSTEM") is { } raw && GitBool(raw) is null) return "GIT_CONFIG_NOSYSTEM is not a boolean";
        if (Get(Env) is not { } view) return "git could not list the child's config";
        foreach (var entry in view.Entries)
            if (Refused(entry) is { } reason) return reason;
        var mine = new List<string>();
        foreach (var entry in config)
            if (entry.Key.StartsWith("safe.", StringComparison.OrdinalIgnoreCase)
                && entry.Level is not (ConfigurationLevel.Local or ConfigurationLevel.Worktree))
                mine.Add(entry.Key.ToLowerInvariant() + "=" + entry.Value);
        mine.Sort(StringComparer.Ordinal);
        var theirs = view.Entries.Where(e => e.Key.StartsWith("safe.", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Key.ToLowerInvariant() + "=" + (e.Value ?? "<bare>")).Order(StringComparer.Ordinal).ToList();
        // An empty value resets git's list: its place in the order matters, which a sorted comparison does not see.
        if (!mine.SequenceEqual(theirs, StringComparer.Ordinal) || theirs.Any(e => e.EndsWith('=')))
            return "safe.* differs between the git child and libgit2";
        return null;
    }

    /// <summary>Why one listed entry is git's to read, or null when it cannot change or break <c>remote get-url</c>.</summary>
    private static string? Refused(Entry entry)
    {
        var key = entry.Key;
        if (key.StartsWith("url.", StringComparison.OrdinalIgnoreCase) || key.StartsWith("include", StringComparison.OrdinalIgnoreCase))
            return $"{entry.Scope} {key}";
        var remote = key.StartsWith("remote.", StringComparison.OrdinalIgnoreCase);
        var branch = key.StartsWith("branch.", StringComparison.OrdinalIgnoreCase);
        if (!remote && !branch) return null;
        var variable = key[(key.IndexOf('.') + 1)..];
        if (variable.Contains('.')) return $"{entry.Scope} {key}"; // remote.<name>.* or branch.<name>.*
        var value = entry.Value;
        var allowed = remote
            ? variable.Equals("pushdefault", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value)
            : variable.ToLowerInvariant() switch
            {
                "sort" => true, // read by git branch, never by remote.c or git_default_config
                "autosetupmerge" => value is null || value is "always" or "inherit" or "simple" || GitBool(value) is not null,
                "autosetuprebase" => value is "never" or "local" or "remote" or "always",
                _ => false,
            };
        return allowed ? null : $"{entry.Scope} {key}";
    }

    /// <summary>git's boolean parse (true/yes/on, false/no/off, empty, or an integer); null when git would die.</summary>
    private static bool? GitBool(string value) => value.Trim().ToLowerInvariant() switch
    {
        "true" or "yes" or "on" => true,
        "false" or "no" or "off" or "" => false,
        var other => int.TryParse(other, System.Globalization.NumberStyles.AllowLeadingSign,
            System.Globalization.CultureInfo.InvariantCulture, out var n) ? n != 0 : null,
    };

    private static bool IsNullDevice(string path) =>
        path == "/dev/null" || (OperatingSystem.IsWindows() && path.Equals("NUL", StringComparison.OrdinalIgnoreCase));

    private static View? Get(Func<string, string?> env)
    {
        var signature = string.Join('\0', EnvironmentNames.Select(n => n + "=" + env(n)))
            + '\0' + string.Join('\0', Enumerable.Range(0, int.TryParse(env("GIT_CONFIG_COUNT"), out var n) ? Math.Min(n, 64) : 0)
                .Select(i => env("GIT_CONFIG_KEY_" + i) + "=" + env("GIT_CONFIG_VALUE_" + i)));
        if (Views.TryGetValue(signature, out var cached) && Fresh(cached) is { } fresh)
        {
            if (!ReferenceEquals(fresh, cached)) Views.TryUpdate(signature, fresh, cached);
            return fresh;
        }
        var view = Ask(env, cached?.Stamps.SkipLast(cached.HomeCount).Select(s => s.Path) ?? []);
        if (view is not null && (Views.Count < ViewLimit || Views.ContainsKey(signature))) Views[signature] = view;
        return view;
    }

    /// <summary>
    /// <paramref name="view"/> when every stamp still holds (a stat each; a hash only for a stamp taken within two
    /// seconds of its file's write time, which is then settled so later checks stat only), else null.
    /// </summary>
    private static View? Fresh(View view)
    {
        Stamp[]? settled = null;
        for (var i = 0; i < view.Stamps.Length; i++)
        {
            var stamp = view.Stamps[i];
            switch (stamp.Kind)
            {
                case StampKind.Directory:
                    if (!Directory.Exists(stamp.Path)) return null;
                    continue;
                case StampKind.Absent:
                    if (File.Exists(stamp.Path) || Directory.Exists(stamp.Path)) return null;
                    continue;
            }
            var info = new FileInfo(stamp.Path);
            if (!info.Exists || info.Length != stamp.Length || info.LastWriteTimeUtc != stamp.Written) return null;
            if (!stamp.Racy)
            {
                // A lock or an ACL change leaves the write time alone, and git warns on a file it cannot open: an open
                // handle (nothing read) proves it still can.
                if (!CanOpen(stamp.Path)) return null;
                continue;
            }
            if (TakeFile(stamp.Path, isHome: false) is not { } now || !now.SameState(stamp)) return null;
            if (!now.Racy) (settled ??= (Stamp[])view.Stamps.Clone())[i] = now;
        }
        return settled is null ? view : view with { Stamps = settled };
    }

    private static bool CanOpen(string path)
    {
        try
        {
            using var _ = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>
    /// A file's stamp, or for a home (<paramref name="isHome"/>) whether the directory exists. Null when the file exists
    /// but cannot be read, is a directory, or is too large to hash while its write time is still within the racy window.
    /// </summary>
    private static Stamp? TakeFile(string path, bool isHome)
    {
        var taken = DateTime.UtcNow;
        if (isHome)
            return new Stamp(path, Directory.Exists(path) ? StampKind.Directory : StampKind.Absent, 0, default, taken, null);
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
                return Directory.Exists(path) ? null : new Stamp(path, StampKind.Absent, 0, default, taken, null);
            var (length, written) = (info.Length, info.LastWriteTimeUtc);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            string? hash = null;
            if (length <= HashedLength) hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
            var stamp = new Stamp(path, StampKind.File, length, written, taken, hash);
            return stamp.Racy && hash is null ? null : stamp;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// The homes a git child may read <c>.gitconfig</c> and <c>.config/git/config</c> under: HOME, HOMEDRIVE+HOMEPATH
    /// and USERPROFILE, each when set and non-empty; null when one is not a fully qualified path.
    /// </summary>
    private static List<string>? HomeCandidates(Func<string, string?> env)
    {
        static string? NonEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
        var homes = new List<string>();
        if (NonEmpty(env("HOME")) is { } home) homes.Add(home);
        if (NonEmpty(env("HOMEDRIVE")) is { } drive && NonEmpty(env("HOMEPATH")) is { } homePath) homes.Add(drive + homePath);
        if (NonEmpty(env("USERPROFILE")) is { } profile) homes.Add(profile);
        if (homes.Any(h => !Path.IsPathFullyQualified(h))) return null;
        return homes.Select(Path.GetFullPath).Distinct(PathComparer).ToList();
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// git's own system, global and command entries for the remote, branch, url, include and safe sections, under the
    /// child's environment, with the stamps the view rests on; null (and nothing cached) when the system file cannot
    /// be located, a candidate cannot be read, git cannot list, or a stamp changed while git listed.
    /// </summary>
    private static View? Ask(Func<string, string?> env, IEnumerable<string> previous)
    {
        if (HomeCandidates(env) is not { } homes) return null;
        var files = new List<string>();
        if (env("GIT_CONFIG_NOSYSTEM") is not { } noSystem || GitBool(noSystem) is not true)
        {
            if (env("GIT_CONFIG_SYSTEM") is { Length: > 0 } system) files.Add(system);
            else if (LocateSystemFile(env) is { } located) files.Add(located);
            else return null;
        }
        if (env("GIT_CONFIG_GLOBAL") is { Length: > 0 } global) files.Add(global);
        else
        {
            foreach (var home in homes)
            {
                files.Add(Path.Combine(home, ".gitconfig"));
                files.Add(Path.Combine(home, ".config", "git", "config"));
            }
            if (env("XDG_CONFIG_HOME") is { Length: > 0 } xdg) files.Add(Path.Combine(xdg, "git", "config"));
        }
        var known = new HashSet<string>(files.Concat(previous).Where(f => !IsNullDevice(f)).Select(Path.GetFullPath), PathComparer);
        var before = new List<Stamp>();
        foreach (var path in known) if (TakeFile(path, isHome: false) is { } s) before.Add(s); else return null;
        foreach (var home in homes) before.Add(TakeFile(home, isHome: true)!);

        var probeDirectory = Path.Combine(Path.GetTempPath(), "antiphon-git-config-probe");
        Directory.CreateDirectory(probeDirectory);
        if (Run(probeDirectory, env, ["config", "--show-origin", "--show-scope", "-z", "--get-regexp",
                "^(remote|branch|url|include|includeif|safe)\\."]) is not ({ } output, 0 or 1))
            return null;
        AfterListing.Value?.Invoke();

        var entries = new List<Entry>();
        var origins = new HashSet<string>(PathComparer);
        var fields = output.Split('\0');
        for (var i = 0; i + 2 < fields.Length; i += 3)
        {
            var (scope, origin, entry) = (fields[i], fields[i + 1], fields[i + 2]);
            if (scope is "local" or "worktree") continue; // libgit2 reads those itself
            var newline = entry.IndexOf('\n');
            entries.Add(new Entry(scope, newline < 0 ? entry : entry[..newline], newline < 0 ? null : entry[(newline + 1)..]));
            if (origin.StartsWith("file:", StringComparison.Ordinal) && !IsNullDevice(origin[5..])) origins.Add(Path.GetFullPath(origin[5..]));
        }
        // An origin no known file accounts for is an included file; any include already refuses, and nothing else is expected.
        var unknown = origins.Where(o => !known.Contains(o)).ToList();
        if (unknown.Count > 0 && !entries.Any(e => e.Key.StartsWith("include", StringComparison.OrdinalIgnoreCase))) return null;

        var after = new List<Stamp>();
        foreach (var path in known.Concat(unknown)) if (TakeFile(path, isHome: false) is { } s) after.Add(s); else return null;
        foreach (var home in homes) after.Add(TakeFile(home, isHome: true)!);
        // The before stamps cover the known files and the homes, in the same order as the start and end of the after list.
        var beforeFiles = before.Take(known.Count).ToList();
        var beforeHomes = before.Skip(known.Count).ToList();
        if (!beforeFiles.Zip(after.Take(known.Count)).All(p => p.First.SameState(p.Second))
            || !beforeHomes.Zip(after.Skip(after.Count - homes.Count)).All(p => p.First.SameState(p.Second)))
            return null;
        return new View(entries, [.. after], homes.Count); // the homes last
    }

    /// <summary>The system config file a git child would read, named by <c>git var GIT_CONFIG_SYSTEM</c>; null when unknown.</summary>
    private static string? LocateSystemFile(Func<string, string?> env)
    {
        var probeDirectory = Path.Combine(Path.GetTempPath(), "antiphon-git-config-probe");
        Directory.CreateDirectory(probeDirectory);
        return Run(probeDirectory, env, ["var", "GIT_CONFIG_SYSTEM"]) is ({ } named, 0)
            && named.TrimEnd('\n', '\r') is { Length: > 0 } systemFile && !systemFile.Contains('\n')
            && Path.IsPathFullyQualified(systemFile) ? Path.GetFullPath(systemFile) : null;
    }

    /// <summary>One git command outside any repository, under the child's environment: its stdout and exit code.</summary>
    private static (string Output, int ExitCode)? Run(string probeDirectory, Func<string, string?> env, string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = probeDirectory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
        };
        start.Environment.Clear();
        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
            if (e.Key is string k && env(k) is { } v) start.Environment[k] = v;
        foreach (var name in EnvironmentNames) if (env(name) is { } v) start.Environment[name] = v;
        if (int.TryParse(env("GIT_CONFIG_COUNT"), out var count))
            for (var i = 0; i < count; i++)
            {
                start.Environment["GIT_CONFIG_KEY_" + i] = env("GIT_CONFIG_KEY_" + i);
                start.Environment["GIT_CONFIG_VALUE_" + i] = env("GIT_CONFIG_VALUE_" + i);
            }
        start.Environment["GIT_CEILING_DIRECTORIES"] = Path.GetDirectoryName(probeDirectory);
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        try
        {
            using var process = Process.Start(start)!;
            process.StandardInput.Close();
            var read = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30_000)) { try { process.Kill(); } catch (InvalidOperationException) { } return null; }
            return (read.GetAwaiter().GetResult(), process.ExitCode);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return null; }
    }
}
