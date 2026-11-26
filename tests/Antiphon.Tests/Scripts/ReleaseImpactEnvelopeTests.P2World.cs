using System.Collections;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Antiphon.Nightly;
using Shouldly;
using P2 = Antiphon.Nightly.ReleaseImpactAdmission;

namespace Antiphon.Tests.Scripts;

public sealed partial class ReleaseImpactEnvelopeTests
{
    private sealed class P2World : IAsyncDisposable
    {
        internal const string HotProject = "tests/Hot/Hot.csproj";
        internal const string QuietProject = "tests/Quiet/Quiet.csproj";
        internal const string HotInput = "tests/Hot/input.txt";
        internal const string QuietInput = "tests/Quiet/input.txt";
        internal const string ExtraInput = "shared/linked-input.txt";
        internal const string Readers = "tests/p2-reader-declarations.json";
        internal const string ProducerProject = "src/Producer/Producer.csproj";
        internal const string ProducerInput = "src/Producer/input.txt";
        internal const string OldReader = "shared/deleted-reader.txt";
        internal const string NewReader = "shared/added-reader.txt";
        private const string EmptyTrust = """{"format":"antiphon-rc-trusted-projects-v1","schema":1,"entries":[]}""";
        private static readonly string[] RequiredTargetRoles = ["sdk", "runtime", "package", "lock", "launcher", "build", "discovery"];
        internal static readonly DateTimeOffset Clock = new(2026, 11, 26, 8, 0, 0, TimeSpan.Zero);

        private readonly RcInputFixtureRepository _repo = new("p2-admission-");
        private Fixture? _toolRepo;
        internal P2.Installation Tool { get; private set; } = null!;
        internal P2.Request Request { get; private set; } = null!;
        internal string OriginBase { get; private set; } = "";
        internal P2.Chunk[] Chunks { get; private set; } = [];
        internal string Root => _repo.Root;
        internal string Sibling => _repo.Git("commit-tree", _repo.Git("rev-parse", Request.BaselineSha + "^{tree}"),
            "-m", "the same tree outside the baseline history");
        private static string Hash(string text) => IndependentDigest(Encoding.UTF8.GetBytes(text));
        internal P2.Target Target { get; } = new(RcReceiptScope.CurrentPlatform(), "x64", "rc", "net9.0",
            OperatingSystem.IsWindows() ? "win-x64" : "linux-x64", "Release", OperatingSystem.IsWindows(),
            new Dictionary<string, string> { ["UseAppHost"] = "false", ["ContinuousIntegrationBuild"] = "true" },
            ["dotnet", "run", "--no-build"]);

        internal static async Task<P2World> Create(bool trust = false, bool candidateOnlyTrust = false,
            bool directHit = false, string? historicalSide = null, bool brokenIndex = false, bool staleCatalog = false,
            bool duplicateSimpleName = false)
        {
            var world = new P2World();
            try
            {
                await world.Seed(trust, candidateOnlyTrust, directHit, historicalSide, brokenIndex, staleCatalog, duplicateSimpleName);
                return world;
            }
            catch
            {
                await world.DisposeAsync();
                throw;
            }
        }

        private async Task Seed(bool trust, bool candidateOnlyTrust, bool directHit, string? historicalSide, bool brokenIndex, bool staleCatalog, bool duplicateSimpleName)
        {
            _repo.Init();
            _repo.Git("config", "core.autocrlf", "false");
            _repo.Git("config", "commit.gpgsign", "false");
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
                var catalog = TestSelectionCatalog.Parse(RcInputFixtureRepository.ReadSource(TestSelection.CatalogPath));
                ((IDictionary)((object[])catalog["sets"]!)[0])["reason"] = "independently changed compatibility rationale";
                catalog["catalogHash"] = TestSelectionCatalog.Hash(catalog);
                _repo.Put(TestSelection.CatalogPath, TestSelectionJson.Canonical(catalog));
            }
            _repo.Put(HotProject, trust
                ? "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"../../src/Producer/Producer.csproj\" /></ItemGroup></Project>"
                : "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            _repo.Put(QuietProject, trust
                ? "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"../../src/Producer/Producer.csproj\" /></ItemGroup></Project>"
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
            var readers = JsonSerializer.Serialize(declarations);
            _repo.Put(Readers, readers);
            _repo.Put(RcChunkSelection.IndexPath, brokenIndex ? BuildIndex("[]") : BuildIndex(readers));
            if (historicalSide == "B") _repo.Put(OldReader, "old reader\n");
            OriginBase = _repo.Commit();
            var comparison = _repo.Commit(allowEmpty: true);
            if (historicalSide == "B") _repo.Remove(OldReader);
            else if (historicalSide == "A") _repo.Put(NewReader, "new reader\n");
            else _repo.Put(trust ? ProducerInput : HotInput, "candidate\n");
            if (candidateOnlyTrust) _repo.Put(RcTrustedProjectGate.PolicyPath, trusted);
            if (brokenIndex) _repo.Put(RcChunkSelection.IndexPath, BuildIndex(readers));
            var candidate = _repo.Commit();
            Request = new P2.Request("p2-fixture", "rc/2026-11-26", comparison, candidate, "run-c", 3, 7, P2.TargetIntentDigest(Target));

            _toolRepo = await Fixture.CreateSourceAsync();
            var toolSha = (await _toolRepo.GitAsync("rev-parse", "HEAD")).Trim();
            var toolFiles = new List<P2.ToolFile>();
            foreach (var path in AlgorithmFiles)
            {
                var bytes = File.ReadAllBytes(Path.Combine(_toolRepo.Root, path));
                toolFiles.Add(new P2.ToolFile(path, "100644", ReleaseImpactEvidence.BlobId(bytes, 40), (ulong)bytes.Length, IndependentDigest(bytes)));
            }
            var installationRoot = _toolRepo.Root;
            Tool = new P2.Installation(installationRoot, toolSha, "p2-fixture-evaluator", "p2-fixture-extractor", toolFiles,
                IndependentDigest(File.ReadAllBytes(typeof(P2).Assembly.Location)), path => File.OpenRead(Path.Combine(installationRoot, path)));
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
            new[] { ("B", request.BaselineSha), ("A", request.CandidateSha) }.SelectMany(pin =>
                RequiredTargetRoles.Select(role => Input(pin.Item1, pin.Item2, null, role, dirtyQuiet)).Concat(Chunks.SelectMany(chunk =>
                    RcChunkDigest.RequiredReceipts.Select(role => Input(pin.Item1, pin.Item2, chunk.Id, role, dirtyQuiet))))).ToArray();

        private static P2.ObservedInput Input(string side, string sha, string? chunk, string kind, bool dirtyQuiet)
        {
            var identity = chunk + "/" + kind;
            if (dirtyQuiet && side == "A" && chunk == "Q" && kind == "generated-fixture-recipes-and-outputs") identity += "/edited";
            return new P2.ObservedInput(side, chunk, kind, Hash(identity), Hash("recorded-" + side + "-" + identity), sha);
        }

        private string InputDigest(string sha, P2.Chunk chunk, string side, P2.ObservedInput[] observations)
        {
            var records = _repo.Git("ls-tree", "-r", sha).Split('\n').Select(line =>
            {
                var record = line.Split([' ', '\t'], 4);
                return new P2.ToolFile(record[3].TrimEnd('\r'), record[0], record[2], 0, "");
            }).ToArray();
            var source = BuildInputs(sha, _repo.Git("rev-parse", sha + "^{tree}"), records);
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

        internal P2.Authority Authority(P2.Request? pins = null, bool dirtyQuiet = false, bool omitQuietRow = false,
            bool removeQuietPin = false, bool duplicateQuietPass = false, bool failQuietExit = false,
            bool flipQuietRead = false, bool carryOrigin = false, bool? ownExecuted = null, bool contractDrift = false,
            bool unresolved = false)
        {
            var request = pins ?? Request;
            var observations = Observe(request, dirtyQuiet);
            var b = request.BaselineSha;
            var originSha = carryOrigin ? OriginBase : b;
            var originAttempt = carryOrigin ? 1u : 2u;
            var run = carryOrigin ? "run-b0" : "run-b";
            var selected = Chunks.Where(chunk => chunk.Id != "Q" || !omitQuietRow).ToArray();
            var originalRows = selected.Select(chunk => Stamp(chunk, originSha, run, originAttempt,
                chunk.Id == "Q" && failQuietExit ? 1 : 0)).ToArray();
            var original = Seal(originSha, run, originAttempt, 1, originalRows);
            var inventory = carryOrigin ? Seal(b, "run-b", 2, 2, originalRows.Select(row =>
                RcChunkReceipt.Carry(row, b, original.Artifact.Digest)).ToArray()) : original;
            var origins = selected.Where(chunk => chunk.Id != "Q" || !removeQuietPin).Select(chunk =>
            {
                var trx = Trx(chunk, chunk.Id == "Q" && duplicateQuietPass);
                var identity = Artifact(chunk.Id.ToLowerInvariant() + ".trx", trx);
                if (chunk.Id == "Q" && flipQuietRead) identity = identity with
                {
                    Open = () => new MemoryStream(++QuietOpens == 1 ? trx : Trx(chunk, false, "Failed"), writable: false),
                };
                return new P2.ExecutionPin(chunk.Id, originSha, run, originAttempt, original.Artifact.Digest,
                    InputDigest(b, chunk, "B", observations), identity, carryOrigin ? original : null);
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
                new(ProducerProject, "B", b, Tool.Extractor, Hash("surface"), Hash("baseline-contract")),
                new(ProducerProject, "A", request.CandidateSha, Tool.Extractor, Hash(contractDrift ? "different" : "surface"),
                    Hash("candidate-contract")),
            ];
            return new P2.Authority(Root, Target, Tool, new P2.Approval(request, Clock, Clock.AddHours(1), "history-at-" + request.Revision, false),
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

        public async ValueTask DisposeAsync()
        {
            var watch = Stopwatch.StartNew();
            if (_toolRepo is not null) await _toolRepo.DisposeAsync();
            _repo.Dispose();
            Console.WriteLine($"C2069 phase=teardown elapsedMs={watch.ElapsedMilliseconds}");
        }
    }
}
