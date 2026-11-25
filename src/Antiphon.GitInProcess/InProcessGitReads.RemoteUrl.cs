using Antiphon.Server.Application.Dtos;
using LibGit2Sharp;

namespace Antiphon.GitInProcess;

/// <summary>
/// <c>remote get-url &lt;name&gt;</c> answered in process: the one exception to "config is never read" (operator,
/// 2026-11-24), kept as narrow as git's own rule for it. git prints the remote's first <c>remote.&lt;name&gt;.url</c>
/// in config order (system, global/XDG, local, worktree, command), after <c>url.&lt;base&gt;.insteadOf</c>
/// rewriting, or reads <c>$GIT_DIR/remotes/&lt;name&gt;</c> and <c>$GIT_DIR/branches/&lt;name&gt;</c> for a remote
/// config does not name. The answer is given only where that is one value read from the repository's own config
/// file, printed as written:
/// <list type="bullet">
/// <item>exactly one <c>remote.&lt;name&gt;.url</c> in the repository's local config (libgit2's view of the file git
/// reads, <see cref="ConfigOf"/>), non-empty printable ASCII, the remote name spelled with the same case;</item>
/// <item>no <c>url.*</c>, <c>include.*</c> or <c>includeIf.*</c> key in that file, and no per-worktree config
/// (<c>config.worktree</c>, <c>extensions.worktreeConfig</c>);</item>
/// <item>no line of that file continued with a backslash (libgit2 and git end such a value differently), and every
/// remote.* and branch.* entry in it one git's remote.c reads without dying or warning (<see cref="LocalUrl"/>);</item>
/// <item>no <c>remote.*</c>, <c>branch.*</c>, <c>url.*</c>, <c>include.*</c> or <c>includeIf.*</c> key in the system,
/// global, XDG or command levels the git child itself reads, asked of git once per distinct child environment, and
/// the same safe.* entries there as libgit2 reads here (<see cref="GitStatusConfigAgreement.ChildRemoteKeys"/>;
/// GIT_CONFIG_PARAMETERS, GIT_CONFIG and a relative config or home path refuse outright);</item>
/// <item>no <c>remotes/&lt;name&gt;</c> or <c>branches/&lt;name&gt;</c> entry in the git or common directory, no
/// variable in the child's environment that redirects the repository, and nothing <see cref="Unusual"/>.</item>
/// </list>
/// Everything else, a missing remote included (git's "error: No such remote", exit 2, on stderr), runs git.
/// </summary>
internal static partial class InProcessGitReads
{
    private static readonly System.Text.RegularExpressions.Regex RemoteName = new(
        "^[A-Za-z0-9_][A-Za-z0-9._-]*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static readonly string[] RepositoryRedirects =
    [
        "GIT_DIR", "GIT_COMMON_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_OBJECT_DIRECTORY",
        "GIT_ALTERNATE_OBJECT_DIRECTORIES", "GIT_NAMESPACE", "GIT_CEILING_DIRECTORIES", "GIT_DISCOVERY_ACROSS_FILESYSTEM",
        "GIT_REPLACE_REF_BASE", "GIT_NO_REPLACE_OBJECTS", "GIT_SHALLOW_FILE", "GIT_GRAFT_FILE",
    ];

    /// <summary>Whether <paramref name="arguments"/> is <c>[--no-optional-locks] remote get-url &lt;name&gt;</c>.</summary>
    internal static bool IsRemoteGetUrl(IReadOnlyList<string> arguments) =>
        arguments is ["remote", "get-url", _] or ["--no-optional-locks", "remote", "get-url", _];

    /// <summary>
    /// git's output for <c>remote get-url &lt;name&gt;</c> (always exit 0, nothing on stderr) run in
    /// <paramref name="repository"/> by a child with exactly <paramref name="childEnvironment"/>, or null to run git.
    /// </summary>
    internal static LandingGitResult? TryAnswerRemoteUrl(string repository, IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?> childEnvironment)
    {
        if (!Enabled || !IsRemoteGetUrl(arguments)) return null;
        var name = arguments[^1];
        if (!RemoteName.IsMatch(name)) return null;
        foreach (var redirect in RepositoryRedirects)
            if (childEnvironment.TryGetValue(redirect, out var value) && !string.IsNullOrEmpty(value)) return null;
        // Injected command-level keys are listed by the probe too; one naming a remote, a rewrite or an include is
        // refused here as well, before any git or libgit2 work.
        if (InjectedKeys(childEnvironment) is not { } injected
            || injected.Any(key => key.StartsWith("remote.", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("url.", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("include", StringComparison.OrdinalIgnoreCase))) return null;
        try
        {
            using var repo = new Repository(repository);
            if (Unusual(repo)) return null;
            var gitDir = repo.Info.Path;
            if (CommonDirectory(gitDir) is not { } common) return null;
            foreach (var dir in new[] { gitDir, common })
            {
                if (File.Exists(Path.Combine(dir, "config.worktree"))) return null;
                foreach (var legacy in new[] { "remotes", "branches" })
                {
                    var path = Path.Combine(dir, legacy, name);
                    if (File.Exists(path) || Directory.Exists(path)) return null;
                }
            }
            // A line continued with a backslash: libgit2 and git disagree on where the value ends (git stops at a comment
            // line that follows, libgit2 joins the next line into the value), so any continued line is git's to read.
            if (HasContinuedLine(Path.Combine(common, "config"))) return null;
            if (ConfigOf(repo) is not { } view) return null;
            try
            {
                if (LocalUrl(view.Config, name) is not { } url) return null;
                // The child's own non-repository levels, last: the one step that may start git (once per environment).
                // git's remote.c reads every remote.* and branch.* key of every level and dies or warns on a bad one;
                // those levels are only listed by name, so any such key there is git's.
                if (GitStatusConfigAgreement.ChildRemoteKeys(childEnvironment, view.Config) is not { } inherited
                    || inherited.Any(key => key.StartsWith("url.", StringComparison.OrdinalIgnoreCase)
                        || key.StartsWith("include", StringComparison.OrdinalIgnoreCase)
                        || key.StartsWith("remote.", StringComparison.OrdinalIgnoreCase)
                        || key.StartsWith("branch.", StringComparison.OrdinalIgnoreCase))) return null;
                return Ok(url + "\n");
            }
            finally
            {
                if (view.Owned) view.Config.Dispose();
            }
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException or OperationCanceledException))
        {
            return null;
        }
    }

    /// <summary>The GIT_CONFIG_KEY_n names a child injects through GIT_CONFIG_COUNT; null for a count git would refuse.</summary>
    private static List<string>? InjectedKeys(IReadOnlyDictionary<string, string?> environment)
    {
        var keys = new List<string>();
        if (!environment.TryGetValue("GIT_CONFIG_COUNT", out var raw) || string.IsNullOrEmpty(raw)) return keys;
        if (!int.TryParse(raw, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var count)
            || count > 64) return null;
        for (var i = 0; i < count; i++)
        {
            if (!environment.TryGetValue("GIT_CONFIG_KEY_" + i, out var key) || string.IsNullOrEmpty(key)
                || !environment.ContainsKey("GIT_CONFIG_VALUE_" + i)) return null;
            keys.Add(key);
        }
        return keys;
    }

    /// <summary>
    /// The one <c>remote.&lt;name&gt;.url</c> of the repository's local config file as git reads it, or null when there
    /// is none, more than one, one under another spelling of the name, an empty or non-printable-ASCII value, any
    /// url.*, include* or per-worktree key in the repository's own levels, or any remote.* or branch.* entry git's
    /// remote.c could die or warn on: a bare (valueless) key, a remote name starting with '/', a remote variable
    /// other than url, pushurl and fetch, a fetch refspec other than the default
    /// <c>[+]refs/heads/*:refs/remotes/&lt;remote&gt;/*</c>, or a branch variable other than remote and merge.
    /// </summary>
    private static string? LocalUrl(Configuration config, string name)
    {
        string? url = null;
        var count = 0;
        foreach (var entry in config)
        {
            if (entry.Level == ConfigurationLevel.Worktree) return null;
            if (entry.Level != ConfigurationLevel.Local) continue;
            var key = entry.Key;
            if (key.StartsWith("url.", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("include", StringComparison.OrdinalIgnoreCase)
                || key.Equals("extensions.worktreeconfig", StringComparison.OrdinalIgnoreCase)) return null;
            if (key.StartsWith("branch.", StringComparison.OrdinalIgnoreCase))
            {
                if (!PlainBranchEntry(key, entry.Value)) return null;
                continue;
            }
            if (!key.StartsWith("remote.", StringComparison.OrdinalIgnoreCase)) continue;
            var rest = key["remote.".Length..];
            var dot = rest.LastIndexOf('.');
            if (dot < 0)
            {
                if (!rest.Equals("pushdefault", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(entry.Value)) return null;
                continue;
            }
            var (subsection, variable) = (rest[..dot], rest[(dot + 1)..]);
            if (subsection.Length == 0 || subsection[0] == '/' || entry.Value is null) return null;
            if (variable.Equals("pushurl", StringComparison.OrdinalIgnoreCase)) continue;
            if (variable.Equals("fetch", StringComparison.OrdinalIgnoreCase))
            {
                var refspec = entry.Value.StartsWith('+') ? entry.Value[1..] : entry.Value;
                if (!refspec.Equals("refs/heads/*:refs/remotes/" + subsection + "/*", StringComparison.Ordinal)) return null;
                continue;
            }
            if (!variable.Equals("url", StringComparison.OrdinalIgnoreCase)) return null;
            if (!subsection.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            // Another case of the name is another remote to git (the subsection is case-sensitive), but libgit2's
            // spelling of it is not proven here: git's.
            if (!subsection.Equals(name, StringComparison.Ordinal)) return null;
            count++;
            url = entry.Value;
        }
        // An empty value resets git's list (git 2.46+) and a bare key is a config error: both git's.
        if (count != 1 || string.IsNullOrEmpty(url) || url.Any(c => c < 0x20 || c > 0x7e)) return null;
        return url;
    }

    /// <summary><c>branch.&lt;name&gt;.remote</c> or <c>branch.&lt;name&gt;.merge</c> with a non-empty value.</summary>
    private static bool PlainBranchEntry(string key, string? value)
    {
        var rest = key["branch.".Length..];
        var dot = rest.LastIndexOf('.');
        if (dot <= 0 || string.IsNullOrEmpty(value)) return false;
        var variable = rest[(dot + 1)..];
        return variable.Equals("remote", StringComparison.OrdinalIgnoreCase) || variable.Equals("merge", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether the config file has a line ending in a backslash (or cannot be read).</summary>
    private static bool HasContinuedLine(string configFile)
    {
        try
        {
            using var stream = new FileStream(configFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
            while (reader.ReadLine() is { } line)
                if (line.EndsWith('\\')) return true;
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return true; }
    }
}
