using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

public sealed partial class RunnerWorkspaceService
{
    /// <summary>CARD-1677 S1: the runner repositories retention and its inventory may name (primary first).</summary>
    internal IReadOnlyList<string> RunnerRepositories()
    {
        var repositories = new List<string> { _repository };
        try
        {
            if (Directory.Exists(_policy.RepositoriesRoot))
                repositories.AddRange(Directory.EnumerateDirectories(_policy.RepositoriesRoot)
                    .Where(path => Directory.Exists(Path.Combine(path, ".git"))));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The caller treats a missing repository as "not registered", which keeps the tree.
        }

        return repositories.Select(path => path.Replace('\\', '/')).Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// CARD-1677 S-1. The live-origin endpoint for <paramref name="repository"/>: the runner-owned clone
    /// source for the primary, never the name <c>origin</c> (which does not resolve under the inspection
    /// runner's <c>--git-dir=/dev/null</c>). A secondary checkout's configured origin is used only when this
    /// runner's policy admits it and it derives that checkout's own directory name; otherwise null (keep).
    /// </summary>
    internal async Task<string?> LiveOriginEndpointAsync(string repository, CancellationToken ct)
    {
        if (PathsEqual(repository, _repository))
            return _cloneSource;
        var url = await InspectionGitAsync(repository, ct, "remote", "get-url", "origin");
        if (url.ExitCode != 0
            || !RepositoryCloneSource.TryNormalize(url.Stdout, out var identity)
            || !RepositoryCloneSource.TryNormalize(_cloneSource, out var primary)
            || !RepositoryCloneSource.IsAdmitted(identity, _policy.AllowedCloneSources, primary)
            || !RepositoryCloneSource.TryDeriveName(identity, out var name)
            || !string.Equals(name, Path.GetFileName(repository.TrimEnd('/', '\\')), StringComparison.Ordinal))
            return null;
        return identity;
    }
}
