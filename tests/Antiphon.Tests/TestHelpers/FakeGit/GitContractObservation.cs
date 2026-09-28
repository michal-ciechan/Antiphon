namespace Antiphon.Tests.TestHelpers.FakeGit;

/// <summary>Independent command and file observations in backend-neutral commit order.</summary>
public static class GitContractObservation
{
    public static async Task<object> CaptureAsync(TestGitBackend git,
        string[] branches, string[] tags, string[] paths)
    {
        var names = branches.Concat(tags).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var refs = new Dictionary<string, string>(StringComparer.Ordinal);
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        var commits = new Dictionary<string, object>(StringComparer.Ordinal);

        async Task<string> Visit(string oid)
        {
            if (ids.TryGetValue(oid, out var known)) return known;
            var parentLine = (await git.RequiredAsync("rev-list", "--parents", "-n", "1", oid))
                .Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var parents = new List<string>();
            foreach (var parent in parentLine.Skip(1)) parents.Add(await Visit(parent));
            var label = "c" + (ids.Count + 1);
            ids[oid] = label;
            var tree = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var path in paths.Prepend("README.md").Distinct(StringComparer.Ordinal))
            {
                var show = await git.RunAsync("show", $"{oid}:{path}");
                tree[path] = show.ExitCode == 0 ? show.Stdout : null;
            }
            commits[label] = new { parents, tree };
            return label;
        }

        foreach (var name in names)
            refs[name] = await Visit((await git.RequiredAsync("rev-parse", name)).Trim());
        var head = await Visit((await git.RequiredAsync("rev-parse", "HEAD")).Trim());
        var branch = (await git.RequiredAsync("branch", "--show-current")).Trim();
        var working = new Dictionary<string, string?>(StringComparer.Ordinal);
        var index = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var path in paths.Prepend("README.md").Distinct(StringComparer.Ordinal))
        {
            var file = Path.Combine(git.RepoPath, path);
            working[path] = git.Files.File.Exists(file) ? git.Files.File.ReadAllText(file) : null;
            var staged = await git.RunAsync("show", ":" + path);
            index[path] = staged.ExitCode == 0 ? staged.Stdout : null;
        }
        return new { branch, head, refs, commits, index, working };
    }
}
