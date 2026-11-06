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
        (string Path, long Length, DateTime Written)[] Stamps, string? DefaultIgnore, string? DefaultAttributes);

    private static readonly ConcurrentDictionary<string, ChildConfig> Views = new(StringComparer.Ordinal);
    private const int ViewLimit = 256;

    /// <summary>
    /// True when every status key agrees. <paramref name="childEnvironment"/> is the child's whole environment
    /// (null: this process's); <paramref name="overrides"/> are <c>-c key=value</c> pairs on the command itself.
    /// </summary>
    public static bool Agrees(Repository repo, IReadOnlyDictionary<string, string?>? childEnvironment,
        IReadOnlyList<KeyValuePair<string, string>> overrides)
        => Disagreement(repo, childEnvironment, overrides) is null;

    /// <summary>Null when every status key agrees, otherwise what differs (for tests and diagnostics).</summary>
    internal static string? Disagreement(Repository repo, IReadOnlyDictionary<string, string?>? childEnvironment,
        IReadOnlyList<KeyValuePair<string, string>> overrides)
    {
        string? Env(string name) => childEnvironment is null ? Environment.GetEnvironmentVariable(name)
            : childEnvironment.TryGetValue(name, out var v) ? v : null;
        foreach (var name in new[] { "GIT_CONFIG_PARAMETERS", "GIT_CONFIG", "GIT_ATTR_NOSYSTEM" })
            if (!string.IsNullOrEmpty(Env(name))) return name + " is set";
        var child = View(Env);
        if (child is null) return "git could not list the child's config";
        var command = new Dictionary<string, string>(child.Command, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in overrides) command[key] = value;
        foreach (var key in Keys)
        {
            var wanted = command.TryGetValue(key, out var c) ? c
                : Level(repo, key, ConfigurationLevel.Worktree) ?? Level(repo, key, ConfigurationLevel.Local)
                  ?? (child.Inherited.TryGetValue(key, out var i) ? i : null);
            var seen = repo.Config.Get<string>(key)?.Value;
            if (!Same(key, wanted, seen)) return $"{key}: git child '{wanted}', libgit2 '{seen}'";
        }
        // Unset excludesFile/attributesFile: both read a default file beside their XDG config.
        if (command.ContainsKey("core.excludesfile") is false && repo.Config.Get<string>("core.excludesfile") is null
            && !SameDefault(child.DefaultIgnore, "ignore"))
            return "default ignore file differs: " + child.DefaultIgnore;
        if (command.ContainsKey("core.attributesfile") is false && repo.Config.Get<string>("core.attributesfile") is null
            && !SameDefault(child.DefaultAttributes, "attributes"))
            return "default attributes file differs: " + child.DefaultAttributes;
        return null;
    }

    private static string? Level(Repository repo, string key, ConfigurationLevel level)
    {
        try { return repo.Config.Get<string>(key, level)?.Value; }
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

    private static ChildConfig? View(Func<string, string?> env)
    {
        var signature = string.Join('\0', EnvironmentNames.Select(n => n + "=" + env(n)))
            + '\0' + string.Join('\0', Enumerable.Range(0, int.TryParse(env("GIT_CONFIG_COUNT"), out var n) ? Math.Min(n, 64) : 0)
                .Select(i => env("GIT_CONFIG_KEY_" + i) + "=" + env("GIT_CONFIG_VALUE_" + i)));
        if (Views.TryGetValue(signature, out var cached) && Fresh(cached)) return cached;
        var view = Ask(env);
        if (view is not null && (Views.Count < ViewLimit || Views.ContainsKey(signature))) Views[signature] = view;
        return view;
    }

    private static bool Fresh(ChildConfig view) => view.Stamps.All(s => Stamp(s.Path) == s);

    private static (string Path, long Length, DateTime Written) Stamp(string path)
    {
        var info = new FileInfo(path);
        return info.Exists ? (path, info.Length, info.LastWriteTimeUtc) : (path, -1, default);
    }

    /// <summary>git's own non-repository config for the status keys, under the child's environment.</summary>
    private static ChildConfig? Ask(Func<string, string?> env)
    {
        var probeDirectory = Path.Combine(Path.GetTempPath(), "antiphon-git-config-probe");
        Directory.CreateDirectory(probeDirectory);
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
        foreach (var arg in new[] { "config", "--show-origin", "--show-scope", "-z", "--get-regexp", "^(core|index|extensions)\\." })
            start.ArgumentList.Add(arg);
        string output;
        int exit;
        try
        {
            using var process = Process.Start(start)!;
            process.StandardInput.Close();
            var read = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30_000)) { try { process.Kill(); } catch (InvalidOperationException) { } return null; }
            output = read.GetAwaiter().GetResult();
            exit = process.ExitCode;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return null; }
        if (exit is not (0 or 1)) return null;

        var inherited = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var command = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var origins = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var fields = output.Split('\0');
        for (var i = 0; i + 2 < fields.Length; i += 3)
        {
            var (scope, origin, entry) = (fields[i], fields[i + 1], fields[i + 2]);
            var newline = entry.IndexOf('\n');
            var key = newline < 0 ? entry : entry[..newline];
            var value = newline < 0 ? "true" : entry[(newline + 1)..]; // a bare key is boolean true
            if (scope is "local" or "worktree") continue; // libgit2 reads those itself
            (scope == "command" ? command : inherited)[key] = value;
            if (origin.StartsWith("file:", StringComparison.Ordinal)) origins.Add(Path.GetFullPath(origin[5..]));
        }
        var home = env("HOME") ?? env("USERPROFILE");
        var xdg = env("XDG_CONFIG_HOME") is { Length: > 0 } x ? Path.Combine(x, "git")
            : home is null ? null : Path.Combine(home, ".config", "git");
        // Files that could start to matter: the child's global files and the system file, even while absent.
        if (env("GIT_CONFIG_GLOBAL") is { Length: > 0 } global) origins.Add(Path.GetFullPath(global));
        else
        {
            if (home is not null) origins.Add(Path.GetFullPath(Path.Combine(home, ".gitconfig")));
            if (xdg is not null) origins.Add(Path.GetFullPath(Path.Combine(xdg, "config")));
        }
        if (env("GIT_CONFIG_SYSTEM") is { Length: > 0 } system) origins.Add(Path.GetFullPath(system));
        var stamps = origins.Where(p => !p.EndsWith("NUL", StringComparison.OrdinalIgnoreCase) && p != "/dev/null")
            .Select(Stamp).ToArray();
        return new ChildConfig(inherited, command, stamps,
            xdg is null ? null : Path.GetFullPath(Path.Combine(xdg, "ignore")),
            xdg is null ? null : Path.GetFullPath(Path.Combine(xdg, "attributes")));
    }
}
