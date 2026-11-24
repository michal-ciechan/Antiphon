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
/// <item>no <c>remote.&lt;name&gt;.*</c>, <c>url.*</c>, <c>include.*</c> or <c>includeIf.*</c> key in the system,
/// global, XDG or command levels the git child itself reads, asked of git once per distinct child environment
/// (<see cref="GitStatusConfigAgreement.ChildRemoteKeys"/>; GIT_CONFIG_PARAMETERS and GIT_CONFIG refuse outright);</item>
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
            if (LocalUrl(repo, name) is not { } url) return null;
            // The child's own non-repository levels, last: the one step that may start git (once per environment).
            if (GitStatusConfigAgreement.ChildRemoteKeys(childEnvironment) is not { } inherited
                || inherited.Any(key => key.StartsWith("url.", StringComparison.OrdinalIgnoreCase)
                    || key.StartsWith("include", StringComparison.OrdinalIgnoreCase)
                    || key.StartsWith("remote." + name + ".", StringComparison.OrdinalIgnoreCase))) return null;
            return Ok(url + "\n");
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
    /// is none, more than one, one under another spelling of the name, an empty or non-printable-ASCII value, or any
    /// url.*, include* or per-worktree key in the repository's own levels.
    /// </summary>
    private static string? LocalUrl(Repository repo, string name)
    {
        if (ConfigOf(repo) is not { } view) return null;
        try
        {
            string? url = null;
            var count = 0;
            foreach (var entry in view.Config)
            {
                if (entry.Level == ConfigurationLevel.Worktree) return null;
                if (entry.Level != ConfigurationLevel.Local) continue;
                var key = entry.Key;
                if (key.StartsWith("url.", StringComparison.OrdinalIgnoreCase)
                    || key.StartsWith("include", StringComparison.OrdinalIgnoreCase)
                    || key.Equals("extensions.worktreeconfig", StringComparison.OrdinalIgnoreCase)) return null;
                if (!key.StartsWith("remote.", StringComparison.OrdinalIgnoreCase)
                    || !key.EndsWith(".url", StringComparison.OrdinalIgnoreCase)) continue;
                var subsection = key["remote.".Length..^".url".Length];
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
        finally
        {
            if (view.Owned) view.Config.Dispose();
        }
    }
}
