using System.Collections.Concurrent;
using System.Diagnostics;
using LibGit2Sharp;

namespace Antiphon.GitInProcess;

/// <summary>
/// Whether libgit2 and a git child read the same values for the config that decides a working-tree status:
/// line endings, ignore and attribute files, case and mode handling, fsmonitor, sparse and split indexes.
/// libgit2 always reads this process's system, XDG and global files and none of git's environment overrides
/// (GIT_CONFIG_NOSYSTEM, GIT_CONFIG_GLOBAL, GIT_CONFIG_COUNT, <c>-c</c>), while a runner may isolate its
/// children with them. So libgit2 may answer a status only where, key by key, its merged value equals the
/// value the child would use: the child's own system/global/command values, asked of git once per distinct
/// config environment (and again whenever one of those files changes), with the repository's local and
/// worktree levels between global and command, as git orders them.
/// The same listing also carries the remote.*, branch.*, url.* and include* key names and the safe.* entries of those levels, for
/// <see cref="ChildRemoteKeys"/> (<c>remote get-url</c>, <see cref="InProcessGitReads.TryAnswerRemoteUrl"/>).
/// </summary>
internal static class GitStatusConfigAgreement
{
    /// <summary>
    /// Keys whose value can change a status or ls-files --others verdict. Not core.longpaths: it only decides
    /// whether git can open a long path, and libgit2 fails on one whatever its value, so git answers anyway.
    /// </summary>
    internal static readonly string[] Keys =
    [
        "core.autocrlf", "core.eol", "core.excludesfile", "core.attributesfile", "core.ignorecase", "core.filemode",
        "core.symlinks", "core.trustctime", "core.checkstat", "core.precomposeunicode", "core.fsmonitor",
        "core.splitindex", "core.sparsecheckout", "core.sparsecheckoutcone", "core.worktree",
        "core.bare", "core.protectntfs", "index.sparse", "index.version", "extensions.worktreeconfig",
    ];

    private static readonly HashSet<string> BoolKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "core.ignorecase", "core.filemode", "core.symlinks", "core.trustctime", "core.precomposeunicode",
        "core.splitindex", "core.sparsecheckout", "core.sparsecheckoutcone", "core.bare",
        "core.protectntfs", "index.sparse", "extensions.worktreeconfig",
    };

    private static readonly string[] EnvironmentNames =
    [
        "HOME", "USERPROFILE", "HOMEDRIVE", "HOMEPATH", "XDG_CONFIG_HOME", "APPDATA", "PROGRAMDATA", "PATH",
        "GIT_CONFIG", "GIT_CONFIG_GLOBAL", "GIT_CONFIG_SYSTEM", "GIT_CONFIG_NOSYSTEM", "GIT_CONFIG_PARAMETERS",
        "GIT_CONFIG_COUNT", "GIT_ATTR_NOSYSTEM",
    ];

    private sealed record ChildConfig(
        IReadOnlyDictionary<string, string> Inherited, IReadOnlyDictionary<string, string> Command,
        (string Path, string Hash)[] Stamps, string? DefaultIgnore, string? DefaultAttributes,
        IReadOnlyList<string> RemoteKeys, IReadOnlyList<string> SafeEntries, bool SystemLocated);

    /// <summary>
    /// Keys that can decide a remote's URL or make git's remote.c die or warn: remote.* and branch.* keys, URL
    /// rewrites (url.*) and include/includeIf.
    /// </summary>
    private static bool IsRemoteKey(string key) =>
        key.StartsWith("remote.", StringComparison.OrdinalIgnoreCase) || key.StartsWith("url.", StringComparison.OrdinalIgnoreCase)
        || key.StartsWith("branch.", StringComparison.OrdinalIgnoreCase) || key.StartsWith("include", StringComparison.OrdinalIgnoreCase);

    /// <summary>Paths a child resolves against its own working directory when relative, which the probe's would not match.</summary>
    private static readonly string[] PathVariables = ["GIT_CONFIG_GLOBAL", "GIT_CONFIG_SYSTEM", "HOME", "XDG_CONFIG_HOME"];

    /// <summary>Test seam: runs between git's listing of the child's config and the stamps taken after it.</summary>
    internal static readonly AsyncLocal<Action?> AfterListing = new();

    private static readonly ConcurrentDictionary<string, ChildConfig> Views = new(StringComparer.Ordinal);
    private const int ViewLimit = 256;

    /// <summary>
    /// True when every status key agrees. <paramref name="childEnvironment"/> is the child's whole environment
    /// (null: this process's); <paramref name="overrides"/> are <c>-c key=value</c> pairs on the command itself.
    /// </summary>
    public static bool Agrees(Configuration config, IReadOnlyDictionary<string, string?>? childEnvironment,
        IReadOnlyList<KeyValuePair<string, string>> overrides)
        => Disagreement(config, childEnvironment, overrides) is null;

    /// <summary>Null when every status key agrees, otherwise what differs (for tests and diagnostics).</summary>
    internal static string? Disagreement(Repository repo, IReadOnlyDictionary<string, string?>? childEnvironment,
        IReadOnlyList<KeyValuePair<string, string>> overrides)
    {
        if (InProcessGitReads.ConfigOf(repo) is not { } view) return "per-worktree config is in play";
        try { return Disagreement(view.Config, childEnvironment, overrides); }
        finally { if (view.Owned) view.Config.Dispose(); }
    }

    /// <summary>
    /// As above, against <paramref name="config"/>: the repository's config as git reads it
    /// (<c>InProcessGitReads.ConfigOf</c>, never a linked worktree's <c>Repository.Config</c>).
    /// </summary>
    internal static string? Disagreement(Configuration config, IReadOnlyDictionary<string, string?>? childEnvironment,
        IReadOnlyList<KeyValuePair<string, string>> overrides)
    {
        string? Env(string name) => childEnvironment is null ? Environment.GetEnvironmentVariable(name)
            : childEnvironment.TryGetValue(name, out var v) ? v : null;
        foreach (var name in new[] { "GIT_CONFIG_PARAMETERS", "GIT_CONFIG", "GIT_ATTR_NOSYSTEM" })
            if (!string.IsNullOrEmpty(Env(name))) return name + " is set";
        var child = View(Env).View;
        if (child is null) return "git could not list the child's config";
        var command = new Dictionary<string, string>(child.Command, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in overrides) command[key] = value;
        foreach (var key in Keys)
        {
            var wanted = command.TryGetValue(key, out var c) ? c
                : Level(config, key, ConfigurationLevel.Worktree) ?? Level(config, key, ConfigurationLevel.Local)
                  ?? (child.Inherited.TryGetValue(key, out var i) ? i : null);
            var seen = config.Get<string>(key)?.Value;
            if (!Same(key, wanted, seen)) return $"{key}: git child '{wanted}', libgit2 '{seen}'";
        }
        // Unset excludesFile/attributesFile: both read a default file beside their XDG config.
        if (command.ContainsKey("core.excludesfile") is false && config.Get<string>("core.excludesfile") is null
            && !SameDefault(child.DefaultIgnore, "ignore"))
            return "default ignore file differs: " + child.DefaultIgnore;
        if (command.ContainsKey("core.attributesfile") is false && config.Get<string>("core.attributesfile") is null
            && !SameDefault(child.DefaultAttributes, "attributes"))
            return "default attributes file differs: " + child.DefaultAttributes;
        return null;
    }

    /// <summary>
    /// The remote.*, branch.*, url.*, include.* and includeIf.* key names a git child with <paramref name="childEnvironment"/>
    /// reads from its system, global (XDG included) and command levels, asked of git under that environment and cached
    /// like the status view. Null when git could not list them, when the system file could not be located (a later
    /// edit to it would go unseen), when config comes from GIT_CONFIG_PARAMETERS or GIT_CONFIG, not modelled here, when
    /// GIT_CONFIG_GLOBAL, GIT_CONFIG_SYSTEM, HOME or XDG_CONFIG_HOME is set but empty or relative (the child resolves
    /// it against its own working directory, the probe against another), or when the child's safe.* entries differ
    /// from those libgit2 reads in <paramref name="config"/> (a safe.directory refusal is git's to give).
    /// </summary>
    internal static IReadOnlyList<string>? ChildRemoteKeys(IReadOnlyDictionary<string, string?> childEnvironment, Configuration config)
    {
        string? Env(string name) => childEnvironment.TryGetValue(name, out var v) ? v : null;
        if (!string.IsNullOrEmpty(Env("GIT_CONFIG_PARAMETERS")) || !string.IsNullOrEmpty(Env("GIT_CONFIG"))) return null;
        foreach (var name in PathVariables)
            if (Env(name) is { } value && !IsNullDevice(value) && (value.Length == 0 || !Path.IsPathFullyQualified(value))) return null;
        var (signature, child) = View(Env);
        if (child is null) return null;
        if (!child.SystemLocated)
        {
            // Nothing was listed from the system file (absent, or none of the listed keys): git names its path, stamped
            // so that a later edit is seen. Only this view needs it, so only this view starts the second git.
            if (LocateSystemFile(Env) is not { } systemFile) return null;
            var located = child with { Stamps = [.. child.Stamps, Stamp(systemFile)], SystemLocated = true };
            Views.TryUpdate(signature, located, child);
            child = located;
        }
        var mine = new List<string>();
        foreach (var entry in config)
            if (entry.Key.StartsWith("safe.", StringComparison.OrdinalIgnoreCase)
                && entry.Level is not (ConfigurationLevel.Local or ConfigurationLevel.Worktree))
                mine.Add(entry.Key.ToLowerInvariant() + "=" + entry.Value);
        mine.Sort(StringComparer.Ordinal);
        var theirs = child.SafeEntries.Order(StringComparer.Ordinal).ToList();
        // An empty value resets git's list: its place in the order matters, which a sorted comparison does not see.
        if (!mine.SequenceEqual(theirs, StringComparer.Ordinal) || theirs.Any(e => e.EndsWith('='))) return null;
        return child.RemoteKeys;
    }

    private static bool IsNullDevice(string path) =>
        path == "/dev/null" || (OperatingSystem.IsWindows() && path.Equals("NUL", StringComparison.OrdinalIgnoreCase));

    private static string? Level(Configuration config, string key, ConfigurationLevel level)
    {
        try { return config.Get<string>(key, level)?.Value; }
        catch (LibGit2SharpException) { return null; }
    }

    private static bool Same(string key, string? wanted, string? seen)
    {
        if (wanted is null || seen is null) return wanted is null && seen is null;
        if (wanted.StartsWith('~') || seen.StartsWith('~')) return false; // expanded against different homes
        if (BoolKeys.Contains(key)) return Bool(wanted) is { } w && Bool(seen) is { } s && w == s;
        return string.Equals(wanted.Trim(), seen.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool? Bool(string value) => value.Trim().ToLowerInvariant() switch
    {
        "true" or "yes" or "on" or "1" => true,
        "false" or "no" or "off" or "0" or "" => false,
        _ => null,
    };

    /// <summary>The child's default file agrees with libgit2's when it is the same file, or neither exists.</summary>
    private static bool SameDefault(string? childFile, string name)
    {
        var mine = GlobalSettings.GetConfigSearchPaths(ConfigurationLevel.Xdg)
            .Where(p => !string.IsNullOrEmpty(p)).Select(p => Path.GetFullPath(Path.Combine(p, name))).ToArray();
        if (childFile is not null && mine.Any(p => GitText.PathsEqual(p, childFile))) return true;
        return (childFile is null || !File.Exists(childFile)) && !mine.Any(File.Exists);
    }

    private static (string Signature, ChildConfig? View) View(Func<string, string?> env)
    {
        var signature = string.Join('\0', EnvironmentNames.Select(n => n + "=" + env(n)))
            + '\0' + string.Join('\0', Enumerable.Range(0, int.TryParse(env("GIT_CONFIG_COUNT"), out var n) ? Math.Min(n, 64) : 0)
                .Select(i => env("GIT_CONFIG_KEY_" + i) + "=" + env("GIT_CONFIG_VALUE_" + i)));
        Views.TryGetValue(signature, out var cached);
        if (cached is not null && Fresh(cached)) return (signature, cached);
        var view = Ask(env, cached?.Stamps.Select(s => s.Path) ?? []);
        if (view is not null && (Views.Count < ViewLimit || Views.ContainsKey(signature))) Views[signature] = view;
        return (signature, view);
    }

    private static bool Fresh(ChildConfig view) => view.Stamps.All(s => Stamp(s.Path) == s);

    private const long HashedLength = 1 << 20;

    /// <summary>
    /// A file's state: its content hash up to 1 MiB (a rewrite inside one timestamp tick is still a change), its
    /// length and write time above that; "absent"; a directory's creation time; an unreadable file never matches.
    /// </summary>
    private static (string Path, string Hash) Stamp(string path)
    {
        try
        {
            if (Directory.Exists(path)) return (path, "dir:" + Directory.GetCreationTimeUtc(path).Ticks);
            var info = new FileInfo(path);
            if (!info.Exists) return (path, "absent");
            if (info.Length > HashedLength) return (path, "large:" + info.Length + ":" + info.LastWriteTimeUtc.Ticks);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return (path, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (path, "unreadable:" + Guid.NewGuid());
        }
    }

    /// <summary>
    /// The homes a git child may read <c>.gitconfig</c> and <c>.config/git/config</c> under. Git for Windows: HOME,
    /// else HOMEDRIVE+HOMEPATH (when that directory exists), else USERPROFILE; empty values are ignored. Every
    /// candidate is stamped (an extra one only costs a miss); the one git would use comes first.
    /// </summary>
    private static List<string> HomeCandidates(Func<string, string?> env)
    {
        static string? NonEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
        var drive = NonEmpty(env("HOMEDRIVE")) is { } d && NonEmpty(env("HOMEPATH")) is { } p ? d + p : null;
        var homes = new List<string>();
        if (NonEmpty(env("HOME")) is { } home) homes.Add(home);
        if (drive is not null && Directory.Exists(drive)) homes.Add(drive);
        if (NonEmpty(env("USERPROFILE")) is { } profile) homes.Add(profile);
        if (drive is not null) homes.Add(drive); // stamped even while absent: creating it changes git's choice
        return homes.Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToList();
    }

    /// <summary>The files a child's view rests on that are known before asking git: global candidates and the system file.</summary>
    private static List<string> KnownFiles(Func<string, string?> env, List<string> homes)
    {
        var files = new List<string>();
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
        if (env("GIT_CONFIG_SYSTEM") is { Length: > 0 } system) files.Add(system);
        return files;
    }

    private static bool Stampable(string path) =>
        !path.EndsWith("NUL", StringComparison.OrdinalIgnoreCase) && path != "/dev/null";

    /// <summary>
    /// git's own non-repository config for the status and remote keys, under the child's environment. The files known
    /// beforehand (and <paramref name="previous"/>, those of an earlier view) are stamped before and after git lists
    /// them; a change in between means the listing may be from either side of the edit, so no view is returned.
    /// </summary>
    private static ChildConfig? Ask(Func<string, string?> env, IEnumerable<string> previous)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var homes = HomeCandidates(env);
        var origins = new HashSet<string>(comparer);
        foreach (var file in KnownFiles(env, homes).Concat(previous))
            if (Stampable(file)) origins.Add(Path.GetFullPath(file));
        var before = origins.Select(Stamp).ToArray();

        var probeDirectory = Path.Combine(Path.GetTempPath(), "antiphon-git-config-probe");
        Directory.CreateDirectory(probeDirectory);
        if (Run(probeDirectory, env, ["config", "--show-origin", "--show-scope", "-z", "--get-regexp",
                "^(core|index|extensions|remote|branch|url|include|includeif|safe)\\."]) is not ({ } output, 0 or 1))
            return null;
        AfterListing.Value?.Invoke();

        var inherited = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var command = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var remoteKeys = new List<string>();
        var safeEntries = new List<string>();
        var systemLocated = false;
        var fields = output.Split('\0');
        for (var i = 0; i + 2 < fields.Length; i += 3)
        {
            var (scope, origin, entry) = (fields[i], fields[i + 1], fields[i + 2]);
            var newline = entry.IndexOf('\n');
            var key = newline < 0 ? entry : entry[..newline];
            var value = newline < 0 ? "true" : entry[(newline + 1)..]; // a bare key is boolean true
            if (scope is "local" or "worktree") continue; // libgit2 reads those itself
            if (origin.StartsWith("file:", StringComparison.Ordinal))
            {
                origins.Add(Path.GetFullPath(origin[5..]));
                if (scope == "system") systemLocated = true;
            }
            if (key.StartsWith("safe.", StringComparison.OrdinalIgnoreCase))
            {
                safeEntries.Add(key.ToLowerInvariant() + "=" + (newline < 0 ? "<bare>" : value));
                continue;
            }
            if (IsRemoteKey(key)) { remoteKeys.Add(key); continue; }
            (scope == "command" ? command : inherited)[key] = value;
        }
        if (env("GIT_CONFIG_SYSTEM") is { Length: > 0 } || IsTrue(env("GIT_CONFIG_NOSYSTEM"))) systemLocated = true;
        var stamps = origins.Where(Stampable).Select(Stamp).ToArray();
        if (before.Any(b => !stamps.Contains(b))) return null;
        var home = homes.FirstOrDefault();
        var xdg = env("XDG_CONFIG_HOME") is { Length: > 0 } x ? Path.Combine(x, "git")
            : home is null ? null : Path.Combine(home, ".config", "git");
        return new ChildConfig(inherited, command, stamps,
            xdg is null ? null : Path.GetFullPath(Path.Combine(xdg, "ignore")),
            xdg is null ? null : Path.GetFullPath(Path.Combine(xdg, "attributes")),
            remoteKeys, safeEntries, systemLocated);
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

    private static bool IsTrue(string? value) => value?.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";

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
