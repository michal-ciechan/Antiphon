using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Rc;

namespace Antiphon.Tests.Scripts;

/// <summary>The arguments of one admission read; a case may change any of them before reading again.</summary>
internal sealed class RcAdmissionReadArgs
{
    public string Path { get; set; } = "";
    public string ExpectedDigest { get; set; } = "";
    public string EvidenceRoot { get; set; } = "";
    public string RepositoryRoot { get; set; } = "";
    public string ExpectedSha { get; set; } = "";
    public string ExpectedRunId { get; set; } = "";
    public string CurrentSha { get; set; } = "";
    public RcChunkInputManifest CurrentManifest { get; set; } = null!;
    public RcBoundaryChunk Chunk { get; set; } = null!;
    public RcAdmissionContext CurrentContext { get; set; } = null!;
    public DateTimeOffset UtcNow { get; set; }

    public RcAdmissionResult Read() => RcAdmission.Read(Path, ExpectedDigest, EvidenceRoot, RepositoryRoot, ExpectedSha, ExpectedRunId, CurrentSha, CurrentManifest, Chunk,
        CurrentContext, UtcNow);
}

/// <summary>
/// The sealed S5b evidence corpus of one executed chunk at a commit of a fixture repository (moved from
/// test-rc-admission-reader.ps1): six actual-input receipts with provenance, four stage proofs, the build output, a
/// passing TRX, a Full anchor and an empty failure history, all retained under results/retained. Proof and envelope
/// are editable JSON; <see cref="FreezeAndRead"/> writes them, freezes a new evidence bundle and reads the admission.
/// No live paths or runner children: only git, against the fixture repository.
/// </summary>
internal sealed class RcAdmissionEvidenceFixture
{
    public const string RunId = "fixture-run";
    public const string Repository = "fixture://antiphon";
    public const string RcLine = "rc/2026-11-13";
    public static readonly DateTimeOffset UtcNow = DateTimeOffset.Parse("2026-11-13T12:00:00Z", CultureInfo.InvariantCulture);
    public static readonly string[] ReceiptKinds = RcChunkDigest.RequiredReceipts;
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private int revision;

    public RcAdmissionEvidenceFixture(string resultsDirectory, string repositoryRoot, string sourceSha)
    {
        ResultsDirectory = resultsDirectory;
        RepositoryRoot = repositoryRoot;
        SourceSha = sourceSha;
        Source = Directory.CreateDirectory(System.IO.Path.Combine(resultsDirectory, "retained")).FullName;
        using var git = new RcInputInventory.GitReader(repositoryRoot);
        git.Run("config", "remote.origin.url", Repository);
        Chunk = RcBoundary.NewChunk([new RcBoundaryRow { Suite = "antiphon", Boundary = "core", IdentityKey = "Example.Tests/Check", CaseKey = "row:a" }]);
        var cases = new[] { new RcChunkCase { Suite = "antiphon", ClassName = "Example.Tests", Method = "Check", CaseKey = "row:a", Stable = true } };
        TreeId = RcInputInventory.Read(git, sourceSha).TreeId;
        Receipts = ReceiptKinds.Select(kind =>
        {
            File.WriteAllText(Retained(kind + ".txt"), "actual fixture bytes " + kind);
            WriteJson(kind + ".proof", new JsonObject
            {
                ["format"] = "antiphon-rc-actual-input-v1", ["sourceSha"] = sourceSha, ["runId"] = RunId, ["kind"] = kind, ["digest"] = RcAdmission.FileDigest(Retained(kind + ".txt")),
            });
            return new RcActualReceipt
            {
                Kind = kind, Digest = RcAdmission.FileDigest(Retained(kind + ".txt")), ProvenanceDigest = RcAdmission.FileDigest(Retained(kind + ".proof")), SourceSha = sourceSha,
            };
        }).ToArray();
        Manifest = RcChunkDigest.NewManifest(RcInputInventory.Read(git, sourceSha), "server", Chunk, cases, Receipts);
        File.WriteAllText(Retained("output.bin"), "fixture assembly and dependency closure");
        OutputDigest = RcAdmission.FileDigest(Retained("output.bin"));
        var stages = new JsonArray();
        foreach (var stage in new[] { "source", "build", "discovery", "launch" })
        {
            WriteJson(stage + ".json", new JsonObject
            {
                ["format"] = "antiphon-rc-stage-proof-v1", ["stage"] = stage, ["sourceSha"] = sourceSha, ["treeId"] = TreeId, ["runId"] = RunId, ["manifestDigest"] = Manifest.Digest,
                ["outputDigest"] = OutputDigest, ["sourceState"] = "clean", ["custody"] = "exclusive-owned-checkout",
            });
            stages.Add(new JsonObject { ["stage"] = stage, ["path"] = stage + ".json" });
        }
        // The persisted chunk and manifest records keep their camelCase field names (as the evidence files hold them).
        Proof = new JsonObject
        {
            ["format"] = "antiphon-rc-execution-proof-v1", ["kind"] = "executed", ["originPath"] = null, ["sourceSha"] = sourceSha, ["treeId"] = TreeId, ["runId"] = RunId,
            ["chunk"] = JsonSerializer.SerializeToNode(Chunk, RcAdmission.RecordJson), ["manifest"] = JsonSerializer.SerializeToNode(Manifest, RcAdmission.RecordJson),
            ["outcome"] = "Passed", ["complete"] = true, ["exitCode"] = 0, ["sourceClean"] = true, ["custody"] = "exclusive-owned-checkout",
            ["assemblyDigest"] = OutputDigest, ["buildOutputDigest"] = OutputDigest, ["discoveryOutputDigest"] = OutputDigest, ["launchOutputDigest"] = OutputDigest,
            ["outputPath"] = "output.bin", ["anchorDigest"] = "", ["stageReceipts"] = stages,
            ["receipts"] = new JsonArray(Receipts.Select(r => (JsonNode)new JsonObject { ["kind"] = r.Kind, ["path"] = r.Kind + ".txt", ["provenancePath"] = r.Kind + ".proof" }).ToArray()),
            ["trxPath"] = "chunk.trx",
            ["caseMap"] = new JsonArray(CaseMapRow("case-1")),
        };
        File.WriteAllText(Retained("chunk.trx"), Trx);
        Contract = new JsonObject
        {
            ["format"] = "antiphon-rc-input-contract-v1", ["inputMapVersion"] = "1", ["sdkVersion"] = "10.0.204", ["runtimeVersion"] = "9.0.0", ["platform"] = "windows",
            ["executionImplementation"] = "compiled",
        };
        foreach (var field in new[]
                 {
                     "policyHash", "catalogHash", "selectorHash", "skipResolverHash", "discoveryHash", "parserHash", "coverageHash", "launcherHash", "custodyHash", "buildHash",
                     "harnessHash", "inputMapHash", "sdkHash", "runtimeHash",
                 })
            Contract[field] = new string('e', 64);
        WriteJson("anchor.json", new JsonObject
        {
            ["format"] = "antiphon-rc-full-anchor-v1", ["sourceSha"] = sourceSha, ["completedAtUtc"] = "2026-11-12T12:00:00Z", ["mode"] = "Full", ["carriedCount"] = 0,
            ["outcome"] = "Passed", ["requiredCount"] = 1, ["executedCount"] = 1, ["caseDigest"] = Manifest.CaseDigest,
        });
        AnchorDigest = RcAdmission.FileDigest(Retained("anchor.json"));
        Proof["anchorDigest"] = AnchorDigest;
        WriteJson("history.json", new JsonObject
        {
            ["format"] = "antiphon-rc-history-v1", ["runId"] = RunId, ["anchorDigest"] = AnchorDigest, ["complete"] = true, ["failures"] = new JsonArray(),
        });
        string[] covered = ["scripts/lib/rc-input-owners.json"];
        Envelope = new JsonObject
        {
            ["format"] = "antiphon-rc-admission-v1", ["sourceSha"] = sourceSha, ["treeId"] = TreeId, ["runId"] = RunId, ["repository"] = Repository, ["platform"] = "windows",
            ["rcLine"] = RcLine, ["inventorySha256"] = "", ["proofPath"] = "proof.json", ["contract"] = Contract.DeepClone(), ["anchorPath"] = "anchor.json",
            ["historyPath"] = "history.json",
            ["history"] = new JsonObject
            {
                ["baselineState"] = "admitted-ancestor", ["admissionChainState"] = "verified", ["historyState"] = "complete", ["globalOwnershipState"] = "resolved",
                ["shadowState"] = "clean",
            },
            ["fullAnchor"] = new JsonObject
            {
                ["kind"] = "executed-full", ["admissionState"] = "verified", ["sourceSha"] = sourceSha, ["closureDigest"] = AnchorDigest,
                ["completedAtUtc"] = "2026-11-12T12:00:00Z", ["platform"] = "windows",
            },
            ["releaseEvidence"] = new JsonObject
            {
                ["status"] = "complete", ["algorithmDigest"] = RcAdmission.AlgorithmDigest(git, sourceSha, covered),
                ["coveredPaths"] = new JsonArray(covered.Select(p => (JsonNode)JsonValue.Create(p)!).ToArray()),
            },
        };
    }

    public const string Trx =
        "<TestRun xmlns=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\"><Results><UnitTestResult testId=\"case-1\" outcome=\"Passed\" /></Results>"
        + "<TestDefinitions><UnitTest id=\"case-1\"><TestMethod className=\"Example.Tests\" name=\"Check\" /></UnitTest></TestDefinitions><ResultSummary outcome=\"Completed\">"
        + "<Counters total=\"1\" executed=\"1\" passed=\"1\" failed=\"0\" error=\"0\" timeout=\"0\" aborted=\"0\" notExecuted=\"0\" /></ResultSummary></TestRun>";

    public string ResultsDirectory { get; }
    public string RepositoryRoot { get; }
    public string SourceSha { get; }
    /// <summary>The retained source run directory that each freeze exports.</summary>
    public string Source { get; }
    public string TreeId { get; }
    public string OutputDigest { get; }
    public string AnchorDigest { get; }
    public RcBoundaryChunk Chunk { get; }
    public RcChunkInputManifest Manifest { get; }
    public RcActualReceipt[] Receipts { get; }
    public JsonObject Proof { get; }
    public JsonObject Envelope { get; }
    public JsonObject Contract { get; }
    /// <summary>The arguments of the latest freeze's read.</summary>
    public RcAdmissionReadArgs Args { get; private set; } = null!;

    public static JsonObject CaseMapRow(string testId) => new()
    {
        ["testId"] = testId, ["suite"] = "antiphon", ["className"] = "Example.Tests", ["method"] = "Check", ["caseKey"] = "row:a",
    };

    public string Retained(string name) => System.IO.Path.Combine(Source, name);

    public void WriteJson(string name, JsonNode value) => File.WriteAllText(Retained(name), value.ToJsonString(Indented));

    public static JsonElement Element(JsonNode node) => JsonSerializer.SerializeToElement(node);

    /// <summary>Writes the proof, exports a new bundle, seals a new envelope and reads the admission.</summary>
    public RcAdmissionResult FreezeAndRead()
    {
        revision++;
        WriteJson("proof.json", Proof);
        var bundle = System.IO.Path.Combine(ResultsDirectory, "bundle-" + revision.ToString(CultureInfo.InvariantCulture));
        var inventory = HistoricalEvidence.Export(Source, bundle, RepositoryRoot, RcInputFixtureRepository.SourceRoot, null, null);
        Envelope["inventorySha256"] = inventory.inventorySha256;
        var path = System.IO.Path.Combine(ResultsDirectory, "admission-" + revision.ToString(CultureInfo.InvariantCulture) + ".json");
        File.WriteAllText(path, Envelope.ToJsonString(Indented));
        Args = new RcAdmissionReadArgs
        {
            Path = path, ExpectedDigest = RcAdmission.FileDigest(path), EvidenceRoot = bundle, RepositoryRoot = RepositoryRoot, ExpectedSha = SourceSha, ExpectedRunId = RunId,
            CurrentSha = SourceSha, CurrentManifest = Manifest, Chunk = Chunk,
            CurrentContext = new RcAdmissionContext { Repository = Repository, Platform = "windows", RcLine = RcLine, Contract = Element(Contract) }, UtcNow = UtcNow,
        };
        return Args.Read();
    }
}
