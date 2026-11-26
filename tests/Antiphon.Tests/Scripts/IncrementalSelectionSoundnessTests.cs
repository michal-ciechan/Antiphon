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
    // CARD-1836 S5 (CP-5, V-5, R-4, R-5, G-14..G-19): the offline five-run replay exposes a hidden miss and blocks activation.
    [Test, Timeout(60_000)]
    public void Five_run_replay_exposes_a_missed_failure_before_activation(CancellationToken cancellationToken)
    {
        using var world = new ReplayWorld();
        var rows = new Antiphon.Tests.Scripts.Nightly.NightlyCaseRows();
        var asOf = "2026-11-25T12:00:00Z";
        (int Exit, string Out, string Err) Replay(string root, string output)
        {
            var o = new StringWriter();
            var e = new StringWriter();
            var exit = RcSelectionReplay.Run(["--input-root", root, "--output", output, "--as-of", asOf, "--repository-root", world.Repo], o, e);
            return (exit, o.ToString(), e.ToString());
        }
        JsonElement Doc(string path)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.Clone();
        }

        var missedRoot = world.Root("missed", ReplayWorld.Missed);
        var missedTree = ReplayWorld.TreeHash(missedRoot);
        var outsideBefore = Directory.GetFileSystemEntries(world.Fixture.Root).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        var missedOut = world.Fixture.PathOf("out-missed");
        var missed = Replay(missedRoot, missedOut);
        var verdict = Doc(Path.Combine(missedOut, "verdict.json"));
        var missedRows = verdict.GetProperty("missedFailures").EnumerateArray().ToArray();
        var k = world.Chunk("K");
        rows.Check(missed.Exit == 1 && verdict.GetProperty("activation").GetString() == "blocked" && missedRows.Length == 1
            && missedRows[0].GetProperty("run").GetString() == world.RunId(3) && missedRows[0].GetProperty("suite").GetString() == "antiphon"
            && missedRows[0].GetProperty("contentDigest").GetString() == k.Digest && missedRows[0].GetProperty("case").GetString() == "k1"
            && missedRows[0].GetProperty("sha").GetString() == world.Sha(3) && missedRows[0].GetProperty("baseline").GetString() == world.Sha(2)
            && missedRows[0].GetProperty("reason").GetString()!.Length > 0 && missedRows[0].GetProperty("trx").GetString() == "runs/" + world.Run8(3) + "/logs/antiphon-" + k.Id(3) + ".trx",
            "s5-missed-failure-listed", "exit=" + missed.Exit + " " + missed.Err + " " + verdict.GetProperty("missedFailures"));

        var unsafeRows = verdict.GetProperty("unsafeCarries").EnumerateArray().ToArray();
        var o5 = world.Chunk("O");
        rows.Check(unsafeRows.Any(u => u.GetProperty("run").GetString() == world.RunId(5) && u.GetProperty("contentDigest").GetString() == o5.Digest
            && u.GetProperty("actual").GetString() == "unknown"), "s5-unknown-is-unsafe", verdict.GetProperty("unsafeCarries").ToString());

        var caught = verdict.GetProperty("caughtFailures").EnumerateArray().Select(c => c.GetProperty("run").GetString() + "/" + c.GetProperty("contentDigest").GetString()).ToArray();
        rows.Check(caught.Contains(world.RunId(2) + "/" + world.Chunk("A").Digest) && caught.Contains(world.RunId(4) + "/" + world.Chunk("E").Digest)
            && verdict.GetProperty("failureRecall").ValueKind == JsonValueKind.Number && Math.Abs(verdict.GetProperty("failureRecall").GetDouble() - 0.75) < 1e-9,
            "s5-caught-failures", string.Join(",", caught) + " recall=" + verdict.GetProperty("failureRecall"));

        var r4Caught = verdict.GetProperty("caughtFailures").EnumerateArray().Where(c => c.GetProperty("run").GetString() == world.RunId(4)).ToArray();
        var p4 = Doc(Path.Combine(missedOut, "predictions", "4.json")).GetProperty("predictions").EnumerateArray().ToArray();
        rows.Check(r4Caught.Length == 1 && r4Caught[0].GetProperty("chunk").GetString() == world.Chunk("E").Id(4) && world.Chunk("E").Id(4) != world.Chunk("E").Id(3)
            && r4Caught[0].GetProperty("contentDigest").GetString() == world.Chunk("E").Digest, "s5-content-not-ordinal", string.Join(",", r4Caught.Select(c => c.ToString())));

        var k4 = p4.Single(p => p.GetProperty("contentDigest").GetString() == k.Digest);
        rows.Check(k4.GetProperty("chunk").GetString() == k.Id(4) && k4.GetProperty("decision").GetString() == "carry" && k4.GetProperty("originRun").GetString() == world.RunId(2),
            "s5-chain-hides-skipped-results", k4.ToString());

        var teacher = Doc(Path.Combine(missedOut, "teacher-forced.json"));
        var teacherK4 = teacher.GetProperty("runs")[3].GetProperty("decisions").EnumerateArray().Single(d => d.GetProperty("contentDigest").GetString() == k.Digest);
        rows.Check(teacherK4.GetProperty("decision").GetString() == "run" && missedRows.Length == 1 && !File.ReadAllText(Path.Combine(missedOut, "verdict.json")).Contains("teacher", StringComparison.Ordinal),
            "s5-teacher-forced-separate", teacherK4.ToString());
        cancellationToken.ThrowIfCancellationRequested();

        // Blinding: flip or remove the outcome of every chunk each run carried; the sealed predictions keep their bytes.
        var carried = Enumerable.Range(1, 5).ToDictionary(i => i, i => Doc(Path.Combine(missedOut, "predictions", i + ".json")).GetProperty("predictions").EnumerateArray()
            .Where(p => p.GetProperty("decision").GetString() == "carry").Select(p => p.GetProperty("chunk").GetString()!).ToHashSet());
        var perturbedRoot = world.Root("perturbed", ReplayWorld.Missed, carried);
        var perturbedOut = world.Fixture.PathOf("out-perturbed");
        var perturbed = Replay(perturbedRoot, perturbedOut);
        var same = Directory.GetFiles(Path.Combine(missedOut, "predictions")).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        rows.Check(carried.Values.Sum(c => c.Count) > 0 && same.Length >= 5
            && same.SequenceEqual(Directory.GetFiles(Path.Combine(perturbedOut, "predictions")).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal))
            && same.All(n => File.ReadAllBytes(Path.Combine(missedOut, "predictions", n!)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(perturbedOut, "predictions", n!))))
            && ReplayWorld.TreeHash(perturbedRoot) != ReplayWorld.TreeHash(missedRoot), "s5-blind-perturbation", "exit=" + perturbed.Exit + " " + perturbed.Err);

        var zeroRoot = world.Root("zero", ReplayWorld.AllFull);
        var zeroOut = world.Fixture.PathOf("out-zero");
        var zero = Replay(zeroRoot, zeroOut);
        rows.Check(zero.Exit == 3 && Doc(Path.Combine(zeroOut, "verdict.json")).GetProperty("reason").GetString() == "insufficient:no-carry-opportunity", "s5-zero-carry-insufficient",
            "exit=" + zero.Exit + " " + zero.Err);

        var cleanRoot = world.Root("clean", ReplayWorld.Clean);
        var cleanTree = ReplayWorld.TreeHash(cleanRoot);
        var beforeClean = Directory.GetFileSystemEntries(world.Fixture.Root).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        var cleanOut = world.Fixture.PathOf("out-clean");
        var clean = Replay(cleanRoot, cleanOut);
        var cleanVerdict = Doc(Path.Combine(cleanOut, "verdict.json"));
        var afterClean = Directory.GetFileSystemEntries(world.Fixture.Root).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        rows.Check(clean.Exit == 0 && cleanVerdict.GetProperty("activation").GetString() == "criterion-met" && !cleanVerdict.GetProperty("enables").GetBoolean()
            && afterClean.SequenceEqual(beforeClean.Append(cleanOut).OrderBy(p => p, StringComparer.Ordinal)) && ReplayWorld.TreeHash(cleanRoot) == cleanTree,
            "s5-clean-cohort-admits", "exit=" + clean.Exit + " " + clean.Err + " " + cleanVerdict.GetProperty("reason"));

        var inside = Replay(missedRoot, Path.Combine(missedRoot, "out"));
        rows.Check(ReplayWorld.TreeHash(missedRoot) == missedTree && inside.Exit == 2 && inside.Err.Contains("output-inside-input", StringComparison.Ordinal)
            && !Directory.Exists(Path.Combine(missedRoot, "out")) && outsideBefore.All(p => Directory.Exists(p) || File.Exists(p)), "s5-input-read-only", inside.Err);

        var runs = verdict.GetProperty("runs").EnumerateArray().ToArray();
        rows.Check(runs.Length == 5 && runs.All(r => r.GetProperty("predictedRun").GetInt32() + r.GetProperty("predictedCarry").GetInt32() + r.GetProperty("predictedHeld").GetInt32()
            == r.GetProperty("universe").GetInt32() && r.GetProperty("universe").GetInt32() == 6 && r.GetProperty("reconciled").GetBoolean() && r.GetProperty("predictedHeld").GetInt32() == 0),
            "s5-reconciles-universe", string.Join(" ", runs.Select(r => r.GetProperty("predictedRun") + "+" + r.GetProperty("predictedCarry"))));

        rows.ShouldAllPass(11, "s5-missed-failure-listed", "s5-unknown-is-unsafe", "s5-caught-failures", "s5-content-not-ordinal", "s5-blind-perturbation",
            "s5-chain-hides-skipped-results", "s5-teacher-forced-separate", "s5-zero-carry-insufficient", "s5-clean-cohort-admits", "s5-input-read-only", "s5-reconciles-universe");
    }

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

    internal static Corpus LoadCorpus(string root)
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

    internal static string RepoRoot()
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

    internal sealed class ProjectGraph
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

    internal sealed class Corpus
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; }

        [JsonPropertyName("knownUnsound")]
        public string[]? KnownUnsound { get; set; }

        [JsonPropertyName("cases")]
        public Case[]? Cases { get; set; }
    }

    internal sealed class Case
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

/// <summary>
/// CARD-1836 S5: the replay fixture. One scratch git repository with the predecessor P, the five candidates R1..R5 (each
/// commit carries the derived index and the trust policy) and a zero-carry chain Z1..Z5 of global changes, and input roots
/// laid out like the server2 custody copy (MANIFEST.sha256, custody.json, runs/&lt;run8&gt;/{logs,timing,attempt,candidate},
/// state/) with synthesized per-case TRX. R1 is a global (Full) change; R2 hits A and K and A fails; R3 hits A only and K
/// fails (the deliberate miss); R4 drops an old-only edge to E, renumbers the ordinals and E fails; R5 changes the trusted
/// Messaging project privately, its own tests fail and one carried chunk (O) has no row or TRX.
/// </summary>
internal sealed class ReplayWorld : IDisposable
{
    public sealed record Spec(string Name, string Project, string Class, string Case, string[] Consumes, double Wall)
    {
        public string Digest => RcChunkReceipt.ContentDigest("antiphon", "native", [new RcCaseIdentity("Antiphon.Tests", "Ns." + Class, "M", Case)])!;

        /// <summary>The chunk id at run index i (0 = P): A, K, E and O renumber from R4 on.</summary>
        public string Id(int run) => Name switch
        {
            "A" => run < 4 ? "g-1" : "g-2",
            "K" => run < 4 ? "g-2" : "g-3",
            "E" => run < 4 ? "g-3" : "g-4",
            "O" => run < 4 ? "g-4" : "g-1",
            _ => Class,
        };
    }

    public static readonly Spec[] Specs =
    [
        new("A", "tests/Alpha/Alpha.Tests.csproj", "AlphaTests", "a1", [], 10), new("K", "tests/Kappa/Kappa.Tests.csproj", "KappaTests", "k1", [], 20),
        new("E", "tests/Echo/Echo.Tests.csproj", "EchoTests", "e1", [], 30), new("O", "tests/Other/Other.Tests.csproj", "OtherTests", "o1", [], 40),
        new("MT", "tests/Messaging/Messaging.Tests.csproj", "MessagingTests", "mt1", ["src/Messaging/Messaging.csproj"], 50),
        new("SV", "tests/Server/Server.Tests.csproj", "ServerTests", "sv1", ["src/Server/Server.csproj", "src/Messaging/Messaging.csproj"], 60),
    ];

    /// <summary>The missed cohort: A fails at R2, K at R3 (carried), E at R4, MT at R5, and O's R5 evidence is absent.</summary>
    public static string Missed(int run, string chunk) => (run, chunk) switch
    {
        (2, "A") or (3, "K") or (4, "E") or (5, "MT") => "fail",
        (5, "O") => "absent",
        _ => "pass",
    };

    /// <summary>The same cohort with K passing at R3 and O's evidence present.</summary>
    public static string Clean(int run, string chunk) => (run, chunk) is (3, "K") or (5, "O") ? "pass" : Missed(run, chunk);

    /// <summary>The zero-carry cohort: every run a global change, every chunk clean.</summary>
    public static string AllFull(int run, string chunk) => "pass";

    private readonly string[] _shas = new string[6];
    private readonly string[] _zero = new string[6];

    public ReplayWorld()
    {
        Fixture = Antiphon.Tests.Scripts.Nightly.NightlyFixture.Create("c1836-replay");
        Repo = Fixture.PathOf("repo");
        Directory.CreateDirectory(Repo);
        Git("init", "-q", "-b", "main");
        Git("config", "core.autocrlf", "false");
        var projects = new Dictionary<string, string[]>
        {
            ["src/Messaging/Messaging.csproj"] = ["messaging"], ["src/Server/Server.csproj"] = ["server"],
        };
        foreach (var spec in Specs) projects[spec.Project] = [spec.Name.ToLowerInvariant()];
        Put("tests/rc-project-categories.json", JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["schemaVersion"] = 1, ["projects"] = projects, ["batches"] = Specs.Select(s => s.Project).ToArray(),
        }));
        const string Sdk = "<Project Sdk=\"Microsoft.NET.Sdk\">";
        string Refs(params string[] refs) => Sdk + "<ItemGroup>" + string.Concat(refs.Select(r => "<ProjectReference Include=\"" + r + "\" />")) + "</ItemGroup></Project>";
        Put("src/Messaging/Messaging.csproj", Sdk + "</Project>");
        Put("src/Server/Server.csproj", Refs("../Messaging/Messaging.csproj"));
        Put("tests/Messaging/Messaging.Tests.csproj", Refs("../../src/Messaging/Messaging.csproj"));
        Put("tests/Server/Server.Tests.csproj", Refs("../../src/Server/Server.csproj"));
        foreach (var spec in Specs.Take(4)) Put(spec.Project, Sdk + "</Project>");
        Put("Directory.Build.props", "<Project />");
        Put("src/Messaging/Internal.cs", "p");
        Put("tests/Alpha/A.cs", "p");
        Put("tests/Kappa/K.cs", "p");
        Put("tests/Echo/E.cs", "p");
        Put("tests/Other/O.cs", "p");
        Put("tests/Other/EchoData.txt", "p");
        Put("tests/derived-impact-index.json", Index(true));
        Put("scripts/lib/rc-trusted-projects.json", JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["format"] = "antiphon-rc-trusted-projects-v1", ["schema"] = 1, ["entries"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["project"] = "src/Messaging/Messaging.csproj", ["configurations"] = new[] { "Release|x64" },
                    ["ownedTests"] = new object[] { new Dictionary<string, object> { ["chunk"] = "MessagingTests", ["cases"] = new[] { "mt1" } } },
                    ["downstream"] = new[] { "src/Server/Server.csproj" }, ["contractProducer"] = "antiphon-contract-v1",
                },
            },
        }));
        _shas[0] = _zero[0] = Commit();
        Put("Directory.Build.props", "<Project><!-- r1 --></Project>");
        _shas[1] = Commit();
        Put("tests/Alpha/A.cs", "r2");
        Put("tests/Kappa/K.cs", "r2");
        _shas[2] = Commit();
        Put("tests/Alpha/A.cs", "r3");
        _shas[3] = Commit();
        File.Delete(Path.Combine(Repo, "tests", "Other", "EchoData.txt"));
        Put("tests/derived-impact-index.json", Index(false));
        _shas[4] = Commit();
        Put("src/Messaging/Internal.cs", "r5");
        _shas[5] = Commit();
        Git("checkout", "-q", "--detach", _zero[0]);
        for (var i = 1; i <= 5; i++)
        {
            Put("Directory.Build.props", "<Project><!-- z" + i + " --></Project>");
            _zero[i] = Commit();
        }
    }

    public Antiphon.Tests.Scripts.Nightly.NightlyFixture Fixture { get; }
    public string Repo { get; }
    public string Sha(int run) => _shas[run];
    public string RunId(int run) => "run-" + Run8(run) + "-attempt";
    public string Run8(int run) => run == 0 ? "p0000000" : "r" + run + "000000";
    public Spec Chunk(string name) => Specs.Single(s => s.Name == name);

    private static string Index(bool echoEdge) => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["schema"] = 1, ["cap"] = 25, ["cappedPaths"] = Array.Empty<object>(),
        ["edges"] = echoEdge ? new object[] { new Dictionary<string, object> { ["path"] = "tests/Other/EchoData.txt", ["classes"] = new[] { "EchoTests" } } } : Array.Empty<object>(),
    });

    /// <summary>An input root for a cohort (Missed, Clean: P,R1..R5; AllFull: P,Z1..Z5). perturb flips or removes the outcome of the named chunk ids per run.</summary>
    public string Root(string name, Func<int, string, string> outcome, Dictionary<int, HashSet<string>>? perturb = null)
    {
        var root = Fixture.PathOf("input-" + name);
        var shas = outcome == AllFull ? _zero : _shas;
        var runs = new List<object>();
        for (var run = 0; run <= 5; run++)
        {
            var dir = Path.Combine(root, "runs", Run8(run));
            var logs = Path.Combine(dir, "logs");
            var rows = new List<object>();
            var chunks = new List<object>();
            foreach (var spec in Specs)
            {
                var id = spec.Id(outcome == AllFull ? 0 : run);
                chunks.Add(new Dictionary<string, object>
                {
                    ["suite"] = "antiphon", ["chunk"] = id, ["project"] = spec.Project, ["classes"] = new[] { spec.Class }, ["consumes"] = spec.Consumes,
                    ["priorWallSeconds"] = spec.Wall,
                    ["members"] = new object[] { new Dictionary<string, string> { ["assembly"] = "Antiphon.Tests", ["class"] = "Ns." + spec.Class, ["method"] = "M", ["case"] = spec.Case } },
                });
                var result = outcome(run, spec.Name);
                if (perturb != null && run > 0 && perturb[run].Contains(id)) result = result == "pass" ? (spec.Name.Length % 2 == 0 ? "absent" : "fail") : "pass";
                if (result == "absent") continue;
                var trx = "antiphon-" + id + ".trx";
                Antiphon.Tests.Scripts.Nightly.NightlyFixture.WriteTrx(Path.Combine(logs, trx),
                    [new Antiphon.Tests.Scripts.Nightly.NightlyTrxRow(spec.Case, "Ns." + spec.Class, "M", result == "pass" ? "Passed" : "Failed")]);
                rows.Add(new Dictionary<string, object?>
                {
                    ["id"] = "antiphon", ["chunk"] = id, ["result"] = result == "pass" ? "pass" : "FAIL", ["exitCode"] = result == "pass" ? 0 : 1, ["timedOut"] = false,
                    ["skipped"] = false, ["evidenceReasons"] = Array.Empty<string>(), ["trx"] = "C:\\Antiphon\\releases\\candidates\\c\\logs\\x\\" + trx,
                    ["sha"] = shas[run], ["contentDigest"] = spec.Digest, ["disposition"] = "executed",
                    ["platform"] = "windows", ["architecture"] = "x64", ["profile"] = "rc",
                });
            }
            Write(Path.Combine(logs, "groups-planned.json"), new Dictionary<string, object> { ["chunks"] = chunks, ["census"] = Array.Empty<object>() });
            Write(Path.Combine(logs, "dispatch-schedule.json"), new { order = chunks.Count });
            Write(Path.Combine(logs, "summary.json"), new Dictionary<string, object> { ["sha"] = shas[run], ["suites"] = rows });
            Write(Path.Combine(dir, "timing", "setup-phases.json"), new { phases = Array.Empty<object>() });
            Write(Path.Combine(dir, "attempt", "selection-attempt.json"), new { runId = RunId(run), requestedMode = "Full" });
            Write(Path.Combine(dir, "attempt", "contracts.json"), new object[]
            {
                new { project = "src/Messaging/Messaging.csproj", side = "B", extractorVersion = "contract-v1", contractHash = "h1" },
                new { project = "src/Messaging/Messaging.csproj", side = "A", extractorVersion = "contract-v1", contractHash = "h1" },
            });
            Write(Path.Combine(dir, "candidate", "candidate.json"), new { sha = shas[run] });
            if (run > 0) runs.Add(new { run8 = Run8(run), runId = RunId(run), stamp = "2026-11-2" + run + "-0100", cid = "rc-fixture", sha = shas[run] });
        }
        Write(Path.Combine(root, "state", "s1", "last-run.json"), new { runId = RunId(5) });
        Write(Path.Combine(root, "custody.json"), new
        {
            asOf = "2026-11-25T12:00:00Z", runs, predecessor = new { run8 = Run8(0), runId = RunId(0), sha = shas[0] }, missing = Array.Empty<string>(),
        });
        var lines = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .OrderBy(p => p, StringComparer.Ordinal).Select(p => Sha256(File.ReadAllBytes(Path.Combine(root, p))) + "  " + p);
        File.WriteAllText(Path.Combine(root, "MANIFEST.sha256"), string.Join("\n", lines) + "\n");
        return root;
    }

    /// <summary>A digest of every file under root (relative path and bytes).</summary>
    public static string TreeHash(string root)
    {
        var text = new System.Text.StringBuilder();
        foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
            text.Append(Path.GetRelativePath(root, file)).Append('\0').Append(Sha256(File.ReadAllBytes(file))).Append('\n');
        return Sha256(System.Text.Encoding.UTF8.GetBytes(text.ToString()));
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

    private static void Write(string path, object value) => Antiphon.Tests.Scripts.Nightly.NightlyFixture.WriteJson(path, value);

    private string Git(params string[] args) => Antiphon.Tests.Scripts.Nightly.NightlyRcChain.Git(Repo, args);

    private void Put(string path, string text)
    {
        var full = Path.Combine(Repo, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text, new System.Text.UTF8Encoding(false));
    }

    private string Commit()
    {
        Git("add", "--all");
        Git("-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "-c", "commit.gpgsign=false", "commit", "-qm", "fixture", "--allow-empty");
        return Git("rev-parse", "HEAD").Trim();
    }

    public void Dispose() => Fixture.Dispose();
}
