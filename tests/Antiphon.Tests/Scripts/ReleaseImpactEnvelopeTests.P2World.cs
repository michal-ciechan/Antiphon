using System.Collections;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.TestHelpers;
using LibGit2Sharp;
using Shouldly;
using P2 = Antiphon.Nightly.ReleaseImpactAdmission;

namespace Antiphon.Tests.Scripts;

public sealed partial class ReleaseImpactEnvelopeTests
{
    private enum ContractChange { None, Selector, Policy, Catalog, Runtime }

    private static readonly Signature P2Author = new("Fixture", "fixture@example.invalid",
        new DateTimeOffset(2026, 11, 26, 8, 0, 0, TimeSpan.Zero));

    private static Repository OpenP2Repository(string root)
    {
        var git = new Repository(Repository.Init(root));
        try
        {
            git.Config.Set("core.autocrlf", false);
            git.Config.Set("commit.gpgsign", false);
            return git;
        }
        catch
        {
            git.Dispose();
            throw;
        }
    }

    private static P2.Installation CreateP2Installation(RcInputFixtureRepository repository)
    {
        using var setup = TestTiming.BeginPhase(TestPhaseKind.FixtureSetup, "p2-fixture-setup");
        using var git = OpenP2Repository(repository.Root);
        var files = new List<P2.ToolFile>();
        foreach (var path in AlgorithmFiles)
        {
            var bytes = ImpactedClasses.RunningSource(path);
            var destination = Path.Combine(repository.Root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllBytes(destination, bytes);
            files.Add(new P2.ToolFile(path, "100644", ReleaseImpactEvidence.BlobId(bytes, 40), (ulong)bytes.Length, IndependentDigest(bytes)));
        }
        repository.Put(".gitattributes", "* -text\n");
        Commands.Stage(git, AlgorithmFiles.Append(".gitattributes"));
        var sha = git.Commit("installed selector source", P2Author, P2Author).Sha;
        return new P2.Installation(repository.Root, sha, "p2-fixture-evaluator", "p2-fixture-extractor", files,
            IndependentDigest(File.ReadAllBytes(typeof(P2).Assembly.Location)), path => File.OpenRead(Path.Combine(repository.Root, path)));
    }

    private sealed class P2World(P2.Installation tool) : IDisposable
    {
        internal const string HotProject = "tests/Hot/Hot.csproj";
        internal const string QuietProject = "tests/Quiet/Quiet.csproj";
        internal const string HotInput = "tests/Hot/input.txt";
        internal const string QuietInput = "tests/Quiet/input.txt";
        internal const string ExtraInput = "shared/linked-input.txt";
        internal const string Readers = "tests/p2-reader-declarations.json";
        internal const string ProducerProject = "src/Producer/Producer.csproj";
        internal const string BypassProject = "src/Bypass/Bypass.csproj";
        internal const string ProducerInput = "src/Producer/input.txt";
        internal const string OldReader = "shared/deleted-reader.txt";
        internal const string NewReader = "shared/added-reader.txt";
        internal const string SelectorSource = "scripts/lib/Antiphon.Nightly/TestSelectionJson.cs";
        private const string EmptyTrust = """{"format":"antiphon-rc-trusted-projects-v1","schema":1,"entries":[]}""";
        private static readonly string[] RequiredTargetRoles = ["sdk", "runtime", "package", "lock", "launcher", "build", "discovery"];
        internal static readonly DateTimeOffset Clock = new(2026, 11, 26, 8, 0, 0, TimeSpan.Zero);

        private readonly RcInputFixtureRepository _repo = new("p2-admission-");
        private readonly Dictionary<string, P2.ToolFile[]> _treeFiles = new(StringComparer.Ordinal);
        private Repository _git = null!;
        private string? _sibling;
        internal P2.Request Request { get; private set; } = null!;
        internal string OriginBase { get; private set; } = "";
        internal string OriginParent { get; private set; } = "";
        internal P2.Chunk[] Chunks { get; private set; } = [];
        internal string Root => _repo.Root;
        internal string Sibling => _sibling ??= _git.ObjectDatabase.CreateCommit(P2Author, P2Author,
            "the same tree outside the baseline history", _git.Lookup<Commit>(Request.BaselineSha).Tree, [], prettifyMessage: true).Sha;

        internal string RewriteOriginRef()
        {
            const string originRef = "refs/heads/original-execution";
            var original = _git.Lookup<Commit>(OriginParent);
            var reference = _git.Refs.Add(originRef, original.Id);
            var rewritten = _git.ObjectDatabase.CreateCommit(P2Author, P2Author, "force-rewritten execution with the same tree",
                original.Tree, [_git.Lookup<Commit>(OriginBase)], prettifyMessage: true);
            _git.Refs.UpdateTarget(reference, rewritten.Id);
            return _git.Lookup<Commit>(originRef).Sha;
        }
        private static string Hash(string text) => IndependentDigest(Encoding.UTF8.GetBytes(text));
        internal P2.Target Target { get; } = new(RcReceiptScope.CurrentPlatform(), "x64", "rc", "net9.0",
            OperatingSystem.IsWindows() ? "win-x64" : "linux-x64", "Release", OperatingSystem.IsWindows(),
            new Dictionary<string, string> { ["UseAppHost"] = "false", ["ContinuousIntegrationBuild"] = "true" },
            ["dotnet", "run", "--no-build"]);

        internal static P2World Create(P2.Installation tool, bool trust = false, bool candidateOnlyTrust = false,
            bool directHit = false, string? historicalSide = null, bool brokenIndex = false, bool staleCatalog = false,
            bool duplicateSimpleName = false, bool bypass = false, ContractChange contractChange = ContractChange.None)
        {
            using var setup = TestTiming.BeginPhase(TestPhaseKind.FixtureSetup, "p2-fixture-setup");
            var world = new P2World(tool);
            try
            {
                world.Seed(trust, candidateOnlyTrust, directHit, historicalSide, brokenIndex, staleCatalog,
                    duplicateSimpleName, bypass, contractChange);
                return world;
            }
            catch
            {
                world.Dispose();
                throw;
            }
        }

        private void Seed(bool trust, bool candidateOnlyTrust, bool directHit, string? historicalSide,
            bool brokenIndex, bool staleCatalog, bool duplicateSimpleName, bool bypass, ContractChange contractChange)
        {
            _git = OpenP2Repository(Root);
            var survivor = trust ? "Probe.OwnTests" : "Probe.HotTests";
            var quietClass = duplicateSimpleName ? "Other.HotTests" : "Probe.QuietTests";
            Chunks =
            [
                new("H", "scripts", "tunit", HotProject, [new(HotProject, "Hot", survivor, "Check", "h1"),
                    .. (trust ? new[] { new P2.Case(HotProject, "Hot", survivor, "Check", "h2") } : [])]),
                new("Q", "scripts", "tunit", QuietProject, [new(QuietProject, "Quiet", quietClass, "Check", "q1"),
                    new(QuietProject, "Quiet", quietClass, "Check", "q2")]),
            ];
            foreach (var path in TestSelection.ContractFiles)
                _repo.Put(path, RcInputFixtureRepository.ReadSource(path));
            if (staleCatalog)
            {
                WriteChangedCatalog();
            }
            _repo.Put(HotProject, trust
                ? "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"../../src/Producer/Producer.csproj\" /></ItemGroup></Project>"
                : "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            var bypassReference = bypass ? "<ProjectReference Include=\"../../src/Bypass/Bypass.csproj\" />" : "";
            _repo.Put(QuietProject, trust
                ? "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"../../src/Producer/Producer.csproj\" />" +
                    bypassReference + "</ItemGroup></Project>"
                : "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            _repo.Put("tests/Hot/HotTests.cs", "namespace Probe; public class HotTests {}");
            _repo.Put("tests/Quiet/QuietTests.cs", "namespace Probe; public class QuietTests {}");
            _repo.Put(HotInput, "baseline\n");
            _repo.Put(QuietInput, "quiet\n");
            _repo.Put(ExtraInput, "read-only compatibility input\n");
            var projects = new Dictionary<string, string[]> { [HotProject] = ["scripts"], [QuietProject] = ["scripts"] };
            if (trust)
            {
                projects[ProducerProject] = ["scripts"];
                _repo.Put(ProducerProject, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
                _repo.Put(ProducerInput, "baseline\n");
            }
            if (bypass)
            {
                projects[BypassProject] = ["scripts"];
                _repo.Put(BypassProject, "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup>" +
                    "<ProjectReference Include=\"../Producer/Producer.csproj\" /></ItemGroup></Project>");
            }
            _repo.Put(RcProjectSelection.CategoryPath, JsonSerializer.Serialize(new { schemaVersion = 1, projects, batches = new[] { HotProject, QuietProject } }));

            var trusted = JsonSerializer.Serialize(new
            {
                format = RcTrustedProjectGate.Format, schema = 1,
                entries = new[] { new { project = ProducerProject, configurations = new[] { "Release|x64" },
                    ownedTests = new[] { new { chunk = "H", cases = new[] { "h1", "h2" } } }, downstream = new[] { QuietProject },
                    contractProducer = "p2-fixture-extractor" } },
            });
            _repo.Put(RcTrustedProjectGate.PolicyPath, trust && !candidateOnlyTrust ? trusted : EmptyTrust);
            var edge = historicalSide switch { "B" => OldReader, "A" => NewReader, _ => HotInput };
            var declarations = new[] { new { path = edge, classes = new[] { "HotTests" } } };
            if (trust) declarations = directHit
                ? [new { path = ProducerInput, classes = new[] { "QuietTests" } }]
                : [];
            var contractPaths = contractChange switch
            {
                ContractChange.Selector => new[] { SelectorSource },
                ContractChange.Policy => [TestSelection.PolicyPath],
                ContractChange.Catalog => [TestSelection.CatalogPath, TestSelection.PolicyPath],
                ContractChange.Runtime => ["global.json"],
                _ => Array.Empty<string>(),
            };
            if (contractPaths.Length > 0)
                declarations = contractPaths.Select(path => new { path, classes = new[] { "HotTests" } }).ToArray();
            var readers = JsonSerializer.Serialize(declarations);
            _repo.Put(Readers, readers);
            _repo.Put(RcChunkSelection.IndexPath, brokenIndex ? BuildIndex("[]") : BuildIndex(readers));
            if (historicalSide == "B") _repo.Put(OldReader, "old reader\n");
            Commands.Stage(_git, "*");
            OriginBase = _git.Commit("fixture origin", P2Author, P2Author).Sha;
            var allowEmpty = new CommitOptions { AllowEmptyCommit = true };
            OriginParent = _git.Commit("fixture origin parent", P2Author, P2Author, allowEmpty).Sha;
            var comparison = _git.Commit("fixture comparison", P2Author, P2Author, allowEmpty).Sha;
            var changedPaths = contractPaths.ToList();
            if (contractChange != ContractChange.None) ChangeContract(contractChange);
            else if (historicalSide == "B")
            {
                _repo.Remove(OldReader);
                changedPaths.Add(OldReader);
            }
            else
            {
                var path = historicalSide == "A" ? NewReader : trust ? ProducerInput : HotInput;
                _repo.Put(path, historicalSide == "A" ? "new reader\n" : "candidate\n");
                changedPaths.Add(path);
            }
            if (candidateOnlyTrust)
            {
                _repo.Put(RcTrustedProjectGate.PolicyPath, trusted);
                changedPaths.Add(RcTrustedProjectGate.PolicyPath);
            }
            if (brokenIndex)
            {
                _repo.Put(RcChunkSelection.IndexPath, BuildIndex(readers));
                changedPaths.Add(RcChunkSelection.IndexPath);
            }
            Commands.Stage(_git, changedPaths);
            var candidate = _git.Commit("fixture candidate", P2Author, P2Author).Sha;
            Request = new P2.Request("p2-fixture", "rc/2026-11-26", comparison, candidate, "run-c", 3, 7, P2.TargetIntentDigest(Target));
        }

        private string WriteChangedCatalog()
        {
            var catalog = TestSelectionCatalog.Parse(RcInputFixtureRepository.ReadSource(TestSelection.CatalogPath));
            ((IDictionary)((object[])catalog["sets"]!)[0])["reason"] = "independently changed compatibility rationale";
            var hash = TestSelectionCatalog.Hash(catalog);
            catalog["catalogHash"] = hash;
            _repo.Put(TestSelection.CatalogPath, TestSelectionJson.Canonical(catalog));
            return hash;
        }

        private void ChangeContract(ContractChange change)
        {
            switch (change)
            {
                case ContractChange.Selector:
                    var source = RcInputFixtureRepository.ReadSource(SelectorSource);
                    source.ShouldContain("MaxDepth = 1024");
                    _repo.Put(SelectorSource, source.Replace("MaxDepth = 1024", "MaxDepth = 512", StringComparison.Ordinal));
                    break;
                case ContractChange.Policy:
                case ContractChange.Catalog:
                    var policy = JsonNode.Parse(RcInputFixtureRepository.ReadSource(TestSelection.PolicyPath))!;
                    if (change == ContractChange.Catalog)
                        policy["selectionCatalog"]!["hash"] = WriteChangedCatalog();
                    else
                        policy["profiles"]!["nightly"]!["description"] = "changed qualification profile rationale";
                    policy["policyHash"] = NightlyPolicy.HashText(policy.ToJsonString());
                    _repo.Put(TestSelection.PolicyPath, policy.ToJsonString());
                    break;
                case ContractChange.Runtime:
                    var runtime = JsonNode.Parse(RcInputFixtureRepository.ReadSource("global.json"))!;
                    runtime["sdk"]!["version"] = "10.0.101";
                    _repo.Put("global.json", runtime.ToJsonString());
                    break;
            }
        }

        private static string BuildIndex(string readers)
        {
            using var parsed = JsonDocument.Parse(readers);
            return JsonSerializer.Serialize(new { schema = 1, edges = parsed.RootElement, cappedPaths = Array.Empty<string>() });
        }

        internal P2.Evaluation Evaluate(P2.Endpoint endpoint, string? unresolved = null)
        {
            // The builder reads literal source declarations from its endpoint. Its only dependency is a file whose
            // path is present in the manifest. The committed index under comparison never reaches this evaluator.
            var regenerated = BuildIndex(Encoding.UTF8.GetString(endpoint.ReadSourceBlob(Readers)));
            return new P2.Evaluation(Hash("p2-fixture-index-builder"), [Readers], Encoding.UTF8.GetBytes(regenerated),
                BuildInputs(endpoint.Sha, endpoint.Tree, endpoint.Files, unresolved), [],
                new Dictionary<string, string> { ["project-evaluator"] = "bounded-two-project" });
        }

        private RcInputSnapshot BuildInputs(string sha, string tree, IReadOnlyList<P2.ToolFile> files, string? unresolved = null) => new()
        {
            Sha = sha, TreeId = tree,
            Unresolved = unresolved is null ? [] : [unresolved],
            Unowned = [],
            Suites = Chunks.Select(chunk =>
            {
                var directory = chunk.Project[..(chunk.Project.LastIndexOf('/') + 1)];
                return new RcInputSuite
                {
                    Id = chunk.Id, Project = chunk.Project,
                    Inputs = files.Where(file => file.Path.StartsWith(directory, StringComparison.Ordinal))
                        .Select(file => new RcInputRecord
                        {
                            Suite = chunk.Id, Project = chunk.Project, Path = file.Path, Mode = file.Mode, Type = "blob",
                            ObjectId = file.BlobId, Present = true, Reason = "fixture-directory",
                        }).ToArray(),
                };
            }).ToArray(),
        };

        internal P2.ObservedInput[] Observe(P2.Request request, bool dirtyQuiet = false) =>
            new[] { (Side: "B", Sha: request.BaselineSha), (Side: "A", Sha: request.CandidateSha) }.SelectMany(pin =>
                RequiredTargetRoles.Select(role => Input(pin.Side, pin.Sha, null, role, false)).Concat(Chunks.SelectMany(chunk =>
                    ObserveChunk(chunk, pin.Side, pin.Sha, pin.Side == "A" && dirtyQuiet)))).ToArray();

        private static IEnumerable<P2.ObservedInput> ObserveChunk(P2.Chunk chunk, string side, string sha, bool dirtyQuiet) =>
            RcChunkDigest.RequiredReceipts.Select(role => Input(side, sha, chunk.Id, role, dirtyQuiet));

        private static P2.ObservedInput Input(string side, string sha, string? chunk, string kind, bool dirtyQuiet)
        {
            var identity = chunk + "/" + kind;
            if (dirtyQuiet && chunk == "Q" && kind == "generated-fixture-recipes-and-outputs") identity += "/edited";
            return new P2.ObservedInput(side, chunk, kind, Hash(identity), Hash("recorded-" + side + "-" + identity), sha);
        }

        private string InputDigest(string sha, P2.Chunk chunk, string side, IEnumerable<P2.ObservedInput> observations)
        {
            var tree = _git.Lookup<Commit>(sha).Tree;
            var treeId = tree.Id.Sha;
            if (!_treeFiles.TryGetValue(treeId, out var records))
                _treeFiles.Add(treeId, records = ReadTree(tree, "").ToArray());
            var source = BuildInputs(sha, treeId, records);
            var cases = chunk.Cases.Select(c => new RcChunkCase
            {
                ClassName = c.Class, Method = c.Method, Suite = chunk.Suite, CaseKey = c.CaseUid, Stable = true,
            }).ToArray();
            var boundary = RcBoundary.NewChunk(cases.Select(c => new RcBoundaryRow
            {
                Suite = c.Suite, Boundary = "scripts", IdentityKey = c.ClassName + "/" + c.Method, CaseKey = c.CaseKey,
            }).ToArray());
            var receipts = observations.Where(r => r.Side == side && r.Chunk == chunk.Id).Select(r => new RcActualReceipt
            {
                Kind = r.Role, Digest = r.Digest, ProvenanceDigest = r.ProvenanceDigest, SourceSha = sha,
            }).ToArray();
            return RcChunkDigest.NewManifest(source, chunk.Id, boundary, cases, receipts).Digest;
        }

        private static IEnumerable<P2.ToolFile> ReadTree(Tree tree, string directory)
        {
            foreach (var entry in tree)
            {
                var path = directory + entry.Name;
                if (entry.Target is Tree child)
                {
                    foreach (var file in ReadTree(child, path + "/"))
                        yield return file;
                    continue;
                }
                var mode = entry.Mode switch
                {
                    Mode.NonExecutableFile => "100644",
                    Mode.ExecutableFile => "100755",
                    _ => throw new InvalidOperationException("unexpected fixture tree entry: " + path),
                };
                yield return new P2.ToolFile(path, mode, entry.Target.Id.Sha, 0, "");
            }
        }

        internal P2.Authority Authority(P2.Request? pins = null, bool dirtyQuiet = false, bool omitQuietRow = false,
            bool removeQuietPin = false, bool duplicateQuietPass = false, bool failQuietExit = false,
            bool flipQuietRead = false, string? carriedFrom = null, bool? ownExecuted = null, bool contractDrift = false,
            bool unresolved = false, bool foreignQuietAssembly = false, bool negativeQuietExit = false, bool dirtyOriginQuiet = false)
        {
            var request = pins ?? Request;
            var observations = Observe(request, dirtyQuiet);
            var b = request.BaselineSha;
            var carryOrigin = carriedFrom is not null;
            var originSha = carriedFrom ?? b;
            var originAttempt = carryOrigin ? 1u : 2u;
            var run = carryOrigin ? "run-b0" : "run-b";
            var selected = Chunks.Where(chunk => chunk.Id != "Q" || !omitQuietRow).ToArray();
            var originalRows = selected.Select(chunk => Stamp(chunk, originSha, run, originAttempt,
                chunk.Id == "Q" ? negativeQuietExit ? -1 : failQuietExit ? 1 : 0 : 0)).ToArray();
            var original = Seal(originSha, run, originAttempt, 1, originalRows);
            var inventory = carryOrigin ? Seal(b, "run-b", 2, 2, originalRows.Select(row =>
                RcChunkReceipt.Carry(row, b, original.Artifact.Digest)).ToArray()) : original;
            var origins = selected.Where(chunk => chunk.Id != "Q" || !removeQuietPin).Select(chunk =>
            {
                var trx = Trx(chunk, chunk.Id == "Q" && duplicateQuietPass);
                if (chunk.Id == "Q" && foreignQuietAssembly)
                    trx = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(trx).Replace("Quiet.dll", "Foreign.dll", StringComparison.Ordinal));
                var identity = Artifact(chunk.Id.ToLowerInvariant() + ".trx", trx);
                if (chunk.Id == "Q" && flipQuietRead) identity = identity with
                {
                    Open = () => new MemoryStream(++QuietOpens == 1 ? trx : Trx(chunk, false, "Failed"), writable: false),
                };
                var originObservations = carryOrigin ? ObserveChunk(chunk, "B", originSha, dirtyOriginQuiet) : observations;
                return new P2.ExecutionPin(chunk.Id, originSha, run, originAttempt, original.Artifact.Digest,
                    InputDigest(originSha, chunk, "B", originObservations), identity, carryOrigin ? original : null);
            }).ToArray();

            P2.OwnExecution[]? own = null;
            if (ownExecuted.HasValue)
            {
                var chunk = Chunks[0];
                var sha = ownExecuted.Value ? request.CandidateSha : b;
                var row = Stamp(chunk, sha, request.RunId, request.Attempt);
                var ownInventory = Seal(sha, request.RunId, request.Attempt, request.Revision, [row]);
                var readInventory = ownExecuted.Value ? ownInventory : Seal(request.CandidateSha, request.RunId, request.Attempt,
                    request.Revision, [RcChunkReceipt.Carry(row, request.CandidateSha, ownInventory.Artifact.Digest)]);
                var ownPin = new P2.ExecutionPin(chunk.Id, sha, request.RunId, request.Attempt, ownInventory.Artifact.Digest,
                    InputDigest(request.CandidateSha, chunk, "A", observations), Artifact("own.trx", Trx(chunk)),
                    ownExecuted.Value ? null : ownInventory);
                own = [new P2.OwnExecution(ownPin, readInventory)];
            }
            P2.TrustContract[] contracts =
            [
                new(ProducerProject, "B", b, tool.Extractor, Hash("surface"), Hash("baseline-contract")),
                new(ProducerProject, "A", request.CandidateSha, tool.Extractor, Hash(contractDrift ? "different" : "surface"),
                    Hash("candidate-contract")),
            ];
            return new P2.Authority(Root, Target, tool, new P2.Approval(request, Clock, Clock.AddHours(1), "history-at-" + request.Revision, false),
                Chunks, [], observations, endpoint => Evaluate(endpoint, unresolved ? "unbounded-residual" : null),
                inventory, origins, own, contracts);
        }

        internal int QuietOpens { get; private set; }

        private OrderedDictionary Stamp(P2.Chunk chunk, string sha, string run, uint attempt, int exit = 0)
        {
            var row = new OrderedDictionary(StringComparer.Ordinal)
            {
                ["id"] = chunk.Suite, ["chunk"] = chunk.Id, ["result"] = "pass", ["exitCode"] = exit,
                ["skipped"] = false, ["runId"] = run, ["attempt"] = attempt,
            };
            var content = RcChunkReceipt.ContentDigest(chunk.Suite, chunk.ExecutionKind, chunk.Cases.Select(c =>
                new RcCaseIdentity(c.Assembly, c.Class, c.Method, c.CaseUid)));
            RcChunkReceipt.Stamp(row, sha, content, new RcReceiptScope { Platform = Target.Os, Architecture = Target.Architecture, Profile = Target.Profile });
            return row;
        }

        private P2.InventoryPin Seal(string sha, string run, uint attempt, uint revision, IDictionary[] rows)
        {
            var path = Path.Combine(_repo.Scratch, Guid.NewGuid().ToString("N") + ".json");
            var seal = RcChunkReceipt.Seal(path, sha, (int)revision, rows);
            var artifact = Artifact("inventory.json", File.ReadAllBytes(path));
            artifact.Digest.ShouldBe(seal);
            return new P2.InventoryPin(run, attempt, revision, sha, artifact);
        }

        internal static P2.Artifact Artifact(string id, byte[] data) =>
            new(id, IndependentDigest(data), (ulong)data.Length, () => new MemoryStream(data, writable: false));

        internal static byte[] Trx(P2.Chunk chunk, bool duplicate = false, string outcome = "Passed")
        {
            var units = string.Concat(chunk.Cases.Select(c =>
                $"<UnitTest id=\"{c.CaseUid}\"><TestMethod className=\"{c.Class}\" name=\"{c.Method}\" codeBase=\"{c.Assembly}.dll\"/></UnitTest>"));
            string Row(P2.Case c) => $"<UnitTestResult testId=\"{c.CaseUid}\" outcome=\"{outcome}\"/>";
            var results = string.Concat(chunk.Cases.Select(Row)) + (duplicate ? Row(chunk.Cases[0]) : "");
            return Encoding.UTF8.GetBytes("<TestRun><TestDefinitions>" + units + "</TestDefinitions><Results>" + results + "</Results></TestRun>");
        }

        public void Dispose()
        {
            using var teardown = TestTiming.BeginPhase(TestPhaseKind.FixtureTeardown, "p2-fixture-teardown");
            var watch = Stopwatch.StartNew();
            _git?.Dispose();
            _repo.Dispose();
            Console.WriteLine($"C2069 phase=teardown elapsedMs={watch.ElapsedMilliseconds}");
        }
    }
}
