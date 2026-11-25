using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Antiphon.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// Replays twelve historical rc failures through two pure selections over the
/// stored path lists. The owner-map replay uses the committed RC input inventory.
/// The proposed project closure selects a suite only when a changed path sits under
/// the suite csproj or a project it reaches by ProjectReference (reflexive, transitive,
/// every Include, conditions included, no Directory.Build import walk) or the path
/// is a build-graph root. Linked Compile items are not edges. Statuses are computed
/// and compared with the fixture, so a map or graph change that moves a case fails
/// until the recorded known-unsound set is updated.
/// </summary>
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class IncrementalSelectionSoundnessTests
{
    private static readonly string[] RequiredRuns =
    [
        "6591e378", "d88de419", "0b292fa4", "97150a0b", "34d41b7e", "f2de5b30", "a6278259",
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    // In-process: the HistoricalSelectionSoundness case (RcInputOwnersMapTests) replays the corpus through the committed
    // owner inventory (Antiphon.Nightly.RcInputInventory), with its three rows: contract, unowned and composer-inputs.
    [Test, Timeout(150_000)]
    public void Recorded_owner_map_status_matches_the_committed_map(CancellationToken cancellationToken)
    {
        _ = LoadCorpus(RepoRoot());
        cancellationToken.ThrowIfCancellationRequested();
        RcInputOwnersMapTests.HistoricalSelectionSoundness();
    }

    [Test]
    public void Owner_globs_match_paths_with_repeated_directory_names()
    {
        var pattern = "tests/" + string.Concat(Enumerable.Repeat("**/part/", 16));
        var path = "tests/" + string.Concat(Enumerable.Repeat("part/", 34));
        var owner = RcInputInventory.Pattern(pattern + "marker/Final.cs");

        owner.IsMatch(path + "changed/Final.cs").ShouldBeFalse();
        owner.IsMatch(path + "marker/Final.cs").ShouldBeTrue();

        var longPrefix = new string('x', 2046);
        RcInputInventory.Pattern(longPrefix + "?*").IsMatch(longPrefix + "z").ShouldBeTrue();
        RcInputInventory.Pattern("**/NuGet.config", ignoreCase: true).IsMatch("nested/nuget.CONFIG").ShouldBeTrue();
        RcInputInventory.Pattern("src/**/a?*.cs").IsMatch("src/ab.cs").ShouldBeTrue();
        RcInputInventory.Pattern("src/**/a?*.cs").IsMatch("src/dir/a/b.cs").ShouldBeFalse();
    }

    [Test, Timeout(60_000)]
    public void Project_closure_misses_match_the_recorded_known_unsound_set()
    {
        var root = RepoRoot();
        var corpus = LoadCorpus(root);
        var graph = ProjectGraph.Load(root);
        var missed = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var item in corpus.Cases!)
        {
            var status = graph.Selects(item.SuiteProject, item.ChangedPaths!) ? "selected" : "missed";
            status.ShouldBe(item.M1, item.Id);
            if (status == "missed")
                missed.Add(item.Id);
        }

        var recorded = new SortedSet<string>(corpus.KnownUnsound!, StringComparer.Ordinal);
        string.Join(",", missed).ShouldBe(string.Join(",", recorded));
    }

    private static Corpus LoadCorpus(string root)
    {
        var path = Path.Combine(root, "tests", "fixtures", "incremental-selection-soundness.json");
        var corpus = JsonSerializer.Deserialize<Corpus>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException("soundness fixture did not parse.");
        if (corpus.SchemaVersion != 1)
            throw new InvalidOperationException("soundness fixture schemaVersion must be 1.");
        if (corpus.KnownUnsound is null || corpus.Cases is null)
            throw new InvalidOperationException("soundness fixture is missing knownUnsound or cases.");
        if (corpus.Cases.Length is < 1 or > 12)
            throw new InvalidOperationException("soundness fixture must hold 1 to 12 cases.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var runs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in corpus.Cases)
        {
            if (string.IsNullOrWhiteSpace(item.Id) || !ids.Add(item.Id))
                throw new InvalidOperationException("soundness case id is missing or duplicated: " + item.Id);
            if (item.Run is null || !Regex.IsMatch(item.Run, "^[0-9a-f]{8}$", RegexOptions.CultureInvariant))
                throw new InvalidOperationException(item.Id + " run is not an 8-hex qualification id.");
            runs.Add(item.Run);
            if (item.IntroducingCommit is null || !Regex.IsMatch(item.IntroducingCommit, "^[0-9a-f]{40}$", RegexOptions.CultureInvariant))
                throw new InvalidOperationException(item.Id + " introducing commit is not 40 hex.");
            if (string.IsNullOrWhiteSpace(item.FailingClass) || string.IsNullOrWhiteSpace(item.Reason))
                throw new InvalidOperationException(item.Id + " needs a failing class and a reason.");
            if (item.Note is null || !item.Note.StartsWith("docs/", StringComparison.Ordinal)
                || !File.Exists(Path.Combine(root, item.Note.Replace('/', Path.DirectorySeparatorChar))))
                throw new InvalidOperationException(item.Id + " note is missing: " + item.Note);
            if (item.CurrentMap is not ("selected" or "missed") || item.M1 is not ("selected" or "missed"))
                throw new InvalidOperationException(item.Id + " status must be selected or missed.");
            if (item.SuiteProject is null || !item.SuiteProject.EndsWith(".csproj", StringComparison.Ordinal)
                || item.SuiteProject.Contains('\\') || !File.Exists(Path.Combine(root, item.SuiteProject.Replace('/', Path.DirectorySeparatorChar))))
                throw new InvalidOperationException(item.Id + " suite project is missing: " + item.SuiteProject);
            if (item.ChangedPaths is null || item.ChangedPaths.Length == 0)
                throw new InvalidOperationException(item.Id + " has no changed paths.");

            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var changed in item.ChangedPaths)
            {
                if (string.IsNullOrWhiteSpace(changed) || changed.Contains('\\') || changed.StartsWith('/')
                    || changed.Split('/').Contains("..") || !paths.Add(changed))
                    throw new InvalidOperationException(item.Id + " has an invalid changed path: " + changed);
            }

            var projectName = item.SuiteProject[(item.SuiteProject.LastIndexOf('/') + 1)..^".csproj".Length];
            if (!string.Equals(item.FailingClass, projectName, StringComparison.Ordinal))
            {
                var suiteDirectory = Path.Combine(root, Path.GetDirectoryName(item.SuiteProject.Replace('/', Path.DirectorySeparatorChar))!);
                if (!ClassFileExists(suiteDirectory, item.FailingClass))
                    throw new InvalidOperationException(item.Id + " failing class is not in " + item.SuiteProject);
            }
        }

        foreach (var recorded in corpus.KnownUnsound)
        {
            if (!ids.Contains(recorded))
                throw new InvalidOperationException("knownUnsound id is not a case: " + recorded);
        }

        if (corpus.KnownUnsound.Distinct(StringComparer.Ordinal).Count() != corpus.KnownUnsound.Length)
            throw new InvalidOperationException("knownUnsound contains a duplicate.");
        foreach (var required in RequiredRuns)
        {
            if (!runs.Contains(required))
                throw new InvalidOperationException("corpus is missing qualification run " + required);
        }

        foreach (var run in runs)
        {
            if (!RequiredRuns.Contains(run, StringComparer.Ordinal))
                throw new InvalidOperationException("corpus run is outside the seven qualification runs: " + run);
        }

        return corpus;
    }

    private static bool ClassFileExists(string suiteDirectory, string className)
    {
        var stack = new Stack<string>();
        stack.Push(suiteDirectory);
        var fileName = className + ".cs";
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                var name = Path.GetFileName(sub);
                if (name is "bin" or "obj" or "node_modules")
                    continue;
                stack.Push(sub);
            }

            if (Directory.EnumerateFiles(dir, fileName).Any())
                return true;
        }

        return false;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Antiphon.sln was not found.");
    }

    private static bool IsBuildInput(string path)
    {
        var slash = path.LastIndexOf('/');
        var name = slash < 0 ? path : path[(slash + 1)..];
        if (name.Equals("Directory.Packages.props", StringComparison.OrdinalIgnoreCase)
            || name.Equals("global.json", StringComparison.OrdinalIgnoreCase)
            || name.Equals("NuGet.Config", StringComparison.OrdinalIgnoreCase)
            || name.Equals("packages.lock.json", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.StartsWith("Directory.Build.", StringComparison.OrdinalIgnoreCase))
            return true;
        return name.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnderProject(string path, string csproj)
    {
        var slash = csproj.LastIndexOf('/');
        if (slash < 0)
            return string.Equals(path, csproj, StringComparison.Ordinal);
        return path.StartsWith(csproj[..(slash + 1)], StringComparison.Ordinal);
    }

    private sealed class ProjectGraph
    {
        private readonly Dictionary<string, string[]> _references;
        private readonly Dictionary<string, HashSet<string>> _closures = new(StringComparer.Ordinal);

        private ProjectGraph(Dictionary<string, string[]> references) => _references = references;

        public static ProjectGraph Load(string root)
        {
            var projects = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var full in EnumerateProjects(root))
            {
                var relative = Path.GetRelativePath(root, full).Replace('\\', '/');
                projects.Add(relative, ReadReferences(relative, full));
            }

            if (projects.Count < 10)
                throw new InvalidOperationException("project graph found too few csproj files.");
            foreach (var pair in projects)
            {
                foreach (var reference in pair.Value)
                {
                    if (!projects.ContainsKey(reference))
                        throw new InvalidOperationException("missing ProjectReference target: " + pair.Key + " -> " + reference);
                }
            }

            return new ProjectGraph(projects);
        }

        public bool Selects(string suiteProject, string[] changedPaths)
        {
            if (changedPaths.Length == 0)
                throw new InvalidOperationException("empty changed path list.");
            if (!_closures.TryGetValue(suiteProject, out var closure))
            {
                closure = Closure(suiteProject);
                _closures.Add(suiteProject, closure);
            }

            foreach (var changed in changedPaths)
            {
                if (IsBuildInput(changed))
                    return true;
                foreach (var project in closure)
                {
                    if (IsUnderProject(changed, project))
                        return true;
                }
            }

            return false;
        }

        private HashSet<string> Closure(string suiteProject)
        {
            if (!_references.ContainsKey(suiteProject))
                throw new InvalidOperationException("suite project is not in the graph: " + suiteProject);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<string>();
            seen.Add(suiteProject);
            queue.Enqueue(suiteProject);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var reference in _references[current])
                {
                    if (seen.Add(reference))
                        queue.Enqueue(reference);
                }
            }

            return seen;
        }

        private static IEnumerable<string> EnumerateProjects(string root)
        {
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var dir = stack.Pop();
                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    var name = Path.GetFileName(sub);
                    if (name is ".git" or "bin" or "obj" or "node_modules" or ".antiphon")
                        continue;
                    stack.Push(sub);
                }

                foreach (var file in Directory.EnumerateFiles(dir, "*.csproj"))
                    yield return file;
            }
        }

        private static string[] ReadReferences(string csproj, string fullPath)
        {
            var slash = csproj.LastIndexOf('/');
            if (slash < 0)
                throw new InvalidOperationException("csproj is not in a directory: " + csproj);
            var directory = csproj[..(slash + 1)];
            var document = XDocument.Load(fullPath);
            var references = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var element in document.Descendants().Where(item => item.Name.LocalName == "ProjectReference"))
            {
                var include = element.Attribute("Include")?.Value;
                if (string.IsNullOrWhiteSpace(include))
                    throw new InvalidOperationException("ProjectReference without Include in " + csproj);
                var resolved = Resolve(directory, csproj, include);
                if (seen.Add(resolved))
                    references.Add(resolved);
            }

            return references.ToArray();
        }

        private static string Resolve(string directory, string csproj, string include)
        {
            var value = include.Replace('\\', '/').Trim();
            if (value.Length == 0 || value.StartsWith('/') || value.Contains('$') || value.Contains('@')
                || value.Contains('%') || value.Contains(':'))
                throw new InvalidOperationException("unresolved ProjectReference in " + csproj + ": " + include);
            var parts = new List<string>();
            foreach (var part in (directory + value).Split('/'))
            {
                if (part == "..")
                {
                    if (parts.Count == 0)
                        throw new InvalidOperationException("outside-repository: " + csproj + " -> " + include);
                    parts.RemoveAt(parts.Count - 1);
                }
                else if (part.Length > 0 && part != ".")
                {
                    parts.Add(part);
                }
            }

            var resolved = string.Join('/', parts);
            if (!resolved.EndsWith(".csproj", StringComparison.Ordinal))
                throw new InvalidOperationException("project reference is not a csproj: " + csproj + " -> " + resolved);
            return resolved;
        }
    }

    private sealed class Corpus
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; }

        [JsonPropertyName("knownUnsound")]
        public string[]? KnownUnsound { get; set; }

        [JsonPropertyName("cases")]
        public Case[]? Cases { get; set; }
    }

    private sealed class Case
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("run")]
        public string? Run { get; set; }

        [JsonPropertyName("failingClass")]
        public string? FailingClass { get; set; }

        [JsonPropertyName("suiteProject")]
        public string? SuiteProject { get; set; }

        [JsonPropertyName("introducingCommit")]
        public string? IntroducingCommit { get; set; }

        [JsonPropertyName("note")]
        public string? Note { get; set; }

        [JsonPropertyName("changedPaths")]
        public string[]? ChangedPaths { get; set; }

        [JsonPropertyName("currentMap")]
        public string? CurrentMap { get; set; }

        [JsonPropertyName("m1")]
        public string? M1 { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }
    }
}
