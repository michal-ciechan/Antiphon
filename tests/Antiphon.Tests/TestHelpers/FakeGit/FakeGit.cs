using System.IO.Abstractions;
using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Tests.TestHelpers.FakeGit;

/// <summary>Fixture-owned Git graph for the pilot command vocabulary.</summary>
public sealed class FakeGit : IGitCommandExecutor
{
    private sealed record Commit(string Id, string[] Parents, Dictionary<string, string> Tree, string Message);
    private sealed class Repository
    {
        public string Branch = "master";
        public Dictionary<string, string> Branches = new(StringComparer.Ordinal);
        public Dictionary<string, string> Tags = new(StringComparer.Ordinal);
        public Dictionary<string, string> Index = new(StringComparer.Ordinal);
        public Dictionary<string, Commit> Commits = new(StringComparer.Ordinal);
        public int NextId = 1;
    }

    private readonly IFileSystem _files;
    private readonly Dictionary<string, Repository> _repositories = new(StringComparer.Ordinal);
    private readonly List<string> _failures = [];
    private readonly List<GitCommandTrace> _trace = [];
    public IReadOnlyList<string> Failures => _failures;
    public IReadOnlyList<GitCommandTrace> Trace => _trace;

    public FakeGit(IFileSystem files) => _files = files;

    public Task<GitCommandResult> ExecuteAsync(string workingDirectory,
        IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var path = _files.Path.GetFullPath(workingDirectory);
        try
        {
            var result = Execute(path, arguments);
            _trace.Add(new GitCommandTrace(path, arguments.ToArray(), result));
            return Task.FromResult(result);
        }
        catch (NotSupportedException ex)
        {
            _failures.Add(ex.Message);
            throw;
        }
    }

    private GitCommandResult Execute(string path, IReadOnlyList<string> a)
    {
        if (a.Count == 0)
            throw Unsupported(a);
        var verb = a[0];
        if (Match(a, "init") || Match(a, "init", "-b", "master"))
        {
            if (!_files.Directory.Exists(path)) _files.Directory.CreateDirectory(path);
            _files.Directory.CreateDirectory(_files.Path.Combine(path, ".git"));
            _repositories.Add(path, new Repository());
            return Ok();
        }
        if (!_repositories.TryGetValue(path, out var repo))
            return Fail("fatal: not a git repository");
        if (verb == "config" && a.Count == 3 &&
            (a[1] == "user.email" || a[1] == "user.name")) return Ok();
        if (verb == "add" && a.Count == 2)
        {
            var prefix = a[1].Replace('\\', '/').TrimEnd('/');
            if (prefix.StartsWith('-')) throw Unsupported(a);
            var entries = WorkingFiles(path);
            var selected = prefix == "." ? entries : entries.Where(e =>
                e.Key == prefix || e.Key.StartsWith(prefix + "/", StringComparison.Ordinal));
            foreach (var (name, value) in selected) repo.Index[name] = value;
            return Ok();
        }
        if (verb == "commit" && a.Count is 3 or 5 && a[1] == "-m" &&
            (a.Count == 3 || a[3] == "--trailer" && a[4] == "antiphon=true"))
        {
            if (repo.Index.Count == 0 && !repo.Branches.ContainsKey(repo.Branch))
                return Fail("nothing to commit");
            var message = a[2] + (a.Count == 5 ? "\n\nantiphon: true" : "");
            var parents = Head(repo) is { } head ? new[] { head } : Array.Empty<string>();
            var commit = NewCommit(repo, parents, repo.Index, message);
            repo.Branches[repo.Branch] = commit.Id;
            return Ok($"[{repo.Branch} {commit.Id[..7]}] {a[2]}\n");
        }
        if (verb == "checkout")
        {
            if (a.Count == 2 && repo.Branches.ContainsKey(a[1]))
            {
                Checkout(path, repo, a[1]);
                return Ok();
            }
            if (a.Count is 3 or 4 && a[1] == "-b")
            {
                var name = a[2];
                if (repo.Branches.ContainsKey(name)) return Fail($"fatal: a branch named '{name}' already exists", 128);
                var from = a.Count == 4 ? Resolve(repo, a[3]) : Head(repo);
                if (from is null) return Fail($"fatal: '{(a.Count == 4 ? a[3] : "HEAD")}' is not a commit");
                repo.Branches[name] = from;
                Checkout(path, repo, name);
                return Ok();
            }
            return Fail($"error: pathspec '{string.Join(' ', a.Skip(1))}' did not match any file(s) known to git");
        }
        if (Match(a, "branch", "--show-current")) return Ok(repo.Branch + "\n");
        if (Match(a, "branch", "--list"))
            return Ok(string.Concat(repo.Branches.Keys.Order(StringComparer.Ordinal)
                .Select(name => (name == repo.Branch ? "* " : "  ") + name + "\n")));
        if (verb == "branch" && a.Count == 2 && !a[1].StartsWith('-'))
        {
            if (repo.Branches.ContainsKey(a[1])) return Fail($"fatal: a branch named '{a[1]}' already exists", 128);
            if (Head(repo) is not { } head) return Fail("fatal: not a valid object name: HEAD");
            repo.Branches[a[1]] = head;
            return Ok();
        }
        if (verb == "rev-parse" && a.Count == 2)
            return Resolve(repo, a[1]) is { } oid ? Ok(oid + "\n") : Missing(a[1]);
        if (verb == "rev-list" && a.Count == 4 && a[2] == "-n" && a[3] == "1")
            throw Unsupported(a);
        if (verb == "rev-list" && a.Count == 4 && a[1] == "-n" && a[2] == "1")
            return Resolve(repo, a[3]) is { } oid ? Ok(oid + "\n") : Missing(a[3]);
        if (verb == "rev-list" && a.Count == 5 && a[1] == "--parents" && a[2] == "-n" && a[3] == "1")
            return Resolve(repo, a[4]) is { } oid
                ? Ok(oid + (repo.Commits[oid].Parents.Length == 0 ? "" : " " + string.Join(' ', repo.Commits[oid].Parents)) + "\n")
                : Missing(a[4]);
        if (verb == "tag" && a.Count == 2 && a[1] == "--list")
            return Ok(string.Concat(repo.Tags.Keys.Order(StringComparer.Ordinal).Select(x => x + "\n")));
        if (verb == "tag" && a.Count is 2 or 3)
        {
            if (repo.Tags.ContainsKey(a[1])) return Fail($"fatal: tag '{a[1]}' already exists", 128);
            var target = a.Count == 3 ? Resolve(repo, a[2]) : Head(repo);
            if (target is null) return Missing(a.Count == 3 ? a[2] : "HEAD");
            repo.Tags[a[1]] = target;
            return Ok();
        }
        if (Match(a, "log", "-1", "--format=%B"))
            return Head(repo) is { } current ? Ok(repo.Commits[current].Message + "\n\n") : Missing("HEAD");
        if (verb == "show" && a.Count == 2 && a[1].StartsWith(':'))
            return repo.Index.TryGetValue(a[1][1..], out var indexed)
                ? Ok(indexed) : MissingPath(a[1][1..], "the index");
        if (verb == "show" && a.Count == 2 && a[1].Contains(':'))
        {
            var split = a[1].Split(':', 2);
            var id = Resolve(repo, split[0]);
            return id is not null && repo.Commits[id].Tree.TryGetValue(split[1], out var bytes)
                ? Ok(bytes) : id is null ? Missing(split[0]) : MissingPath(split[1], $"'{split[0]}'");
        }
        if (verb == "merge" && a.Count == 5 && a[1] == "--no-ff" && a[3] == "-m")
        {
            var first = Head(repo);
            var second = Resolve(repo, a[2]);
            if (first is null || second is null) return Missing(a[2]);
            var tree = new Dictionary<string, string>(repo.Commits[first].Tree, StringComparer.Ordinal);
            foreach (var (name, bytes) in repo.Commits[second].Tree)
            {
                if (tree.TryGetValue(name, out var old) && old != bytes &&
                    repo.Commits[first].Tree.TryGetValue(name, out var firstBytes) &&
                    repo.Commits[second].Tree.TryGetValue(name, out var secondBytes) &&
                    firstBytes != secondBytes) return Fail("CONFLICT (content): merge conflict");
                tree[name] = bytes;
            }
            var merge = NewCommit(repo, [first, second], tree, a[4]);
            repo.Branches[repo.Branch] = merge.Id;
            repo.Index = new(tree, StringComparer.Ordinal);
            WriteTree(path, tree, WorkingFiles(path).Keys);
            return Ok();
        }
        if (verb == "diff" && a.Count is 2 or 4 && a[1].Contains("..", StringComparison.Ordinal)
            && (a.Count == 2 || a[2] == "--"))
        {
            var split = a[1].Split("..", 2, StringSplitOptions.None);
            var left = Resolve(repo, split[0]);
            var right = Resolve(repo, split[1]);
            if (left is null || right is null) return Missing(a[1]);
            return Ok(Diff(repo.Commits[left].Tree, repo.Commits[right].Tree,
                a.Count == 4 ? a[3] : null));
        }
        throw Unsupported(a);
    }

    private static bool Match(IReadOnlyList<string> actual, params string[] expected) =>
        actual.SequenceEqual(expected, StringComparer.Ordinal);
    private static GitCommandResult Ok(string stdout = "") => new(0, stdout, "");
    private static GitCommandResult Fail(string stderr, int exitCode = 1) =>
        new(exitCode, "", stderr + "\n");
    private static GitCommandResult Missing(string name) =>
        Fail($"fatal: ambiguous argument '{name}': unknown revision or path not in the working tree.\nUse '--' to separate paths from revisions, like this:\n'git <command> [<revision>...] -- [<file>...]'", 128);
    private static GitCommandResult MissingPath(string path, string scope) =>
        Fail($"fatal: path '{path}' exists on disk, but not in {scope}", 128);
    private static NotSupportedException Unsupported(IReadOnlyList<string> arguments) =>
        new($"Unsupported FakeGit vector: [{string.Join(", ", arguments)}]");
    private static string? Head(Repository repo) => repo.Branches.GetValueOrDefault(repo.Branch);
    private static string? Resolve(Repository repo, string name) => name == "HEAD" ? Head(repo)
        : repo.Branches.GetValueOrDefault(name) ?? repo.Tags.GetValueOrDefault(name)
        ?? (repo.Commits.ContainsKey(name) ? name : null);

    private static Commit NewCommit(Repository repo, string[] parents,
        Dictionary<string, string> tree, string message)
    {
        var id = repo.NextId++.ToString("x40");
        var commit = new Commit(id, parents, new(tree, StringComparer.Ordinal), message);
        repo.Commits[id] = commit;
        return commit;
    }

    private Dictionary<string, string> WorkingFiles(string path) =>
        _files.Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Where(f => !_files.Path.GetRelativePath(path, f).Replace('\\', '/').StartsWith(".git/", StringComparison.Ordinal))
            .ToDictionary(f => _files.Path.GetRelativePath(path, f).Replace('\\', '/'),
                f => _files.File.ReadAllText(f), StringComparer.Ordinal);

    private void Checkout(string path, Repository repo, string branch)
    {
        IEnumerable<string> previous = Head(repo) is { } old
            ? repo.Commits[old].Tree.Keys : Array.Empty<string>();
        repo.Branch = branch;
        var tree = repo.Commits[repo.Branches[branch]].Tree;
        WriteTree(path, tree, previous);
        repo.Index = new(tree, StringComparer.Ordinal);
    }

    private void WriteTree(string path, Dictionary<string, string> tree, IEnumerable<string> previous)
    {
        foreach (var name in previous.Except(tree.Keys))
        {
            var file = _files.Path.Combine(path, name);
            if (_files.File.Exists(file)) _files.File.Delete(file);
        }
        foreach (var (name, value) in tree)
        {
            var file = _files.Path.Combine(path, name);
            _files.Directory.CreateDirectory(_files.Path.GetDirectoryName(file)!);
            _files.File.WriteAllText(file, value);
        }
    }

    private static string Diff(Dictionary<string, string> left,
        Dictionary<string, string> right, string? filter)
    {
        var result = new System.Text.StringBuilder();
        foreach (var name in left.Keys.Union(right.Keys).Order(StringComparer.Ordinal))
        {
            if (filter is not null && name != filter) continue;
            left.TryGetValue(name, out var old);
            right.TryGetValue(name, out var newer);
            if (old == newer) continue;
            result.Append($"diff --git a/{name} b/{name}\n--- a/{name}\n+++ b/{name}\n");
            foreach (var line in (old ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries)) result.Append('-').Append(line).Append('\n');
            foreach (var line in (newer ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries)) result.Append('+').Append(line).Append('\n');
        }
        return result.ToString();
    }
}

public sealed record GitCommandTrace(string WorkingDirectory, string[] Arguments,
    GitCommandResult Result, string Role = "observer");
