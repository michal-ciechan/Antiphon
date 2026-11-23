using System.Collections;
using System.Collections.Specialized;
using System.Text;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1313 S6-A/S6-B: fixtures of the release selection attempt (Antiphon.Nightly.ReleaseSelectionAttempt.Create), the C#
/// twins of the deleted drivers' builders (2026-11-22 plan, unit 8): a git repository holding the selection contract's
/// inputs, commits made with git plumbing on any parent (New-RSCommit), a request (New-RSRequest / New-Request) and a
/// full-release baseline receipt with real pinned git bytes and synthetic passing TRX (Write-RSReceipt /
/// Write-BaselineArchive). The receipt proves the reader only; it is not a production supplier fixture.
/// </summary>
internal sealed class SelectionRepository : IDisposable
{
    private readonly NightlyFixture _fixture;

    /// <summary>A repository (named with a space) with an empty history.</summary>
    public SelectionRepository(string prefix)
    {
        _fixture = NightlyFixture.Create(prefix);
        Root = _fixture.PathOf("repo with spaces");
        Directory.CreateDirectory(Root);
        Git("init", "-q");
        Git("config", "user.name", "RS fixture");
        Git("config", "user.email", "fixture@example.invalid");
        Git("config", "core.autocrlf", "false");
        Git("config", "commit.gpgsign", "false");
    }

    public string Root { get; }

    /// <summary>A path under the fixture (outside the repository).</summary>
    public string PathOf(params string[] parts) => _fixture.PathOf(parts);

    public string Git(params string[] arguments) => NightlyRcChain.Git(Root, arguments).Trim();

    /// <summary>Writes a file under the repository's working tree.</summary>
    public void Put(string relative, string text)
    {
        var path = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    public string Read(string relative) => File.ReadAllText(Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>The selection contract's inputs as this checkout holds them (TestSelection.ContractFiles).</summary>
    public void PutContractFiles()
    {
        foreach (var file in TestSelection.ContractFiles) Put(file, File.ReadAllText(Path.Combine(NightlyPlanCases.Repo, file.Replace('/', Path.DirectorySeparatorChar))));
    }

    /// <summary>
    /// New-RSCommit: the change applied on top of parent (or as a root commit when parent is empty), committed without
    /// moving any branch. Returns the commit.
    /// </summary>
    public string Commit(string parent, string message, Action change)
    {
        if (parent.Length > 0) Git("read-tree", "--reset", "-u", parent);
        else
        {
            Git("rm", "-rq", "--cached", "--ignore-unmatch", ".");
            foreach (var entry in new DirectoryInfo(Root).EnumerateFileSystemInfos())
            {
                if (entry.Name == ".git") continue;
                if (entry is DirectoryInfo directory) directory.Delete(true);
                else entry.Delete();
            }
        }
        change();
        Git("add", "--all");
        var tree = Git("write-tree");
        return parent.Length > 0 ? Git("commit-tree", tree, "-p", parent, "-m", message) : Git("commit-tree", tree, "-m", message);
    }

    /// <summary>
    /// Write-RSPolicy: the execution policy (and the selection catalog) edited and their hashes and pin kept in agreement,
    /// so the selector still accepts the commit.
    /// </summary>
    public void EditPolicy(Action<IDictionary>? policyEdit, Action<IDictionary>? catalogEdit)
    {
        var policy = (IDictionary)TestSelectionJson.Parse(Read(TestSelection.PolicyPath));
        if (catalogEdit != null)
        {
            var catalog = (IDictionary)TestSelectionJson.Parse(Read(TestSelection.CatalogPath));
            catalogEdit(catalog);
            catalog["catalogHash"] = TestSelectionCatalog.Hash(catalog);
            Put(TestSelection.CatalogPath, Json(catalog));
            ((IDictionary)policy["selectionCatalog"]!)["hash"] = catalog["catalogHash"];
        }
        policyEdit?.Invoke(policy);
        policy["policyHash"] = NightlyPolicy.HashText(Json(policy));
        Put(TestSelection.PolicyPath, Json(policy));
    }

    public void Dispose() => _fixture.Dispose();

    /// <summary>A value as indented JSON (PlainJson, as the writers write it).</summary>
    public static string Json(object value)
    {
        using var buffer = new MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(buffer, new System.Text.Json.JsonWriterOptions { Indented = true }))
            PlainJson.Write(writer, value);
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}

internal static class ReleaseSelectionFixtures
{
    public const string CardId = "e83e4f3f-7b8c-45e1-8d93-5b337e343f53";

    /// <summary>New-RSRequest: a Full attempt of candidateSha in releaseRoot (run run-1, attempt 1), its timing root named with a non-ASCII letter.</summary>
    public static ReleaseSelectionRequest Request(string repositoryRoot, string releaseRoot, string candidateSha, Action<ReleaseSelectionRequest>? change = null)
    {
        var request = new ReleaseSelectionRequest
        {
            RepositoryRoot = repositoryRoot, Repository = "fixture/repository", ReleaseRoot = releaseRoot, CandidateId = "rc-2026-11-08-fixture",
            CandidateRef = "rc/2026-11-08", CandidateSha = candidateSha, BaselineSha = "", SelectionMode = "Full", Profile = "rc", RunId = "run-1", Attempt = "1",
            BaselineReceiptPath = "", QualificationPath = "", TimingRoot = Path.Combine(releaseRoot, "timing root λ"), ReleaseIntentPath = "", TrainManifestPath = "",
        };
        change?.Invoke(request);
        return request;
    }

    /// <summary>A copy of a request.</summary>
    public static ReleaseSelectionRequest Copy(ReleaseSelectionRequest request, Action<ReleaseSelectionRequest>? change = null)
    {
        var copy = new ReleaseSelectionRequest();
        foreach (var property in typeof(ReleaseSelectionRequest).GetProperties().Where(p => p.CanRead && p.CanWrite)) property.SetValue(copy, property.GetValue(request));
        change?.Invoke(copy);
        return copy;
    }

    /// <summary>Bind-Request: Create, then the request carries the attempt's two paths, their digests and its baseline (the hop's expectation).</summary>
    public static NightlySelectionBinding Bind(ReleaseSelectionRequest request)
    {
        var bound = ReleaseSelectionAttempt.Create(request);
        request.SelectionAttemptPath = bound.SelectionAttemptPath;
        request.SelectionAttemptDigest = bound.SelectionAttemptDigest;
        request.SelectionPath = bound.SelectionPath;
        request.SelectionDigest = bound.SelectionDigest;
        request.BaselineSha = bound.BaselineSha;
        return bound;
    }

    /// <summary>Create's refusal text, or "" when it wrote an attempt.</summary>
    public static string Refusal(ReleaseSelectionRequest request) => NightlyDiagnosticCases.Refusal(() => ReleaseSelectionAttempt.Create(request));

    /// <summary>Every file under a root: its relative path and SHA-256, so two states compare byte for byte.</summary>
    public static string Tree(string root)
    {
        if (!Directory.Exists(root)) return "";
        var files = new OrderedDictionary(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            files[file.Substring(root.Length)] = NightlyPaths.FileSha256(file);
        return TestSelectionJson.Canonical(files);
    }

    /// <summary>The attempt files under a release root.</summary>
    public static int Attempts(string releaseRoot) =>
        Directory.Exists(Path.Combine(releaseRoot, "candidates")) ? Directory.GetFiles(Path.Combine(releaseRoot, "candidates"), "selection-attempt.json", SearchOption.AllDirectories).Length : 0;

    /// <summary>[string] of a field of a document Create returned (an attempt, a selection, a proposal).</summary>
    public static string Field(object? document, string name) => document is IDictionary map ? PsValue.Str(map[name]) : PsValue.Str(PsValue.Prop(document, name));

    /// <summary>The string items of a field.</summary>
    public static string[] Fields(object? document, string name)
    {
        var value = document is IDictionary map ? map[name] : PsValue.Prop(document, name);
        return value is IEnumerable items and not string ? items.Cast<object>().Select(PsValue.Str).ToArray() : value == null ? [] : [PsValue.Str(value)];
    }

    /// <summary>A JSON document read back (keys exactly).</summary>
    public static IDictionary ReadJson(string path) => (IDictionary)TestSelectionJson.Parse(File.ReadAllText(path));

    /// <summary>
    /// Write-RSReceipt / Write-BaselineArchive: a full-release baseline receipt for sha under releaseRoot/&lt;directory&gt;/&lt;name&gt;.json,
    /// its candidate's artifacts under releaseRoot/archive/&lt;name&gt;. Returns the receipt's path.
    /// </summary>
    public static string WriteReceipt(string repositoryRoot, string releaseRoot, string sha, string name, string directory = "baselines",
        string repository = "fixture/repository", string candidateRef = "rc/2026-11-08")
    {
        var candidateRoot = "archive/" + name;
        var root = Path.Combine(releaseRoot, "archive", name);
        var dir = Path.Combine(root, "authority");
        var contract = TestSelection.Contract(repositoryRoot, sha, "rc");
        var blob = ReleaseAuthority.ReadPinnedBlob(repositoryRoot, sha);
        var policy = ReleaseAuthority.PolicyFromBytes(blob.Bytes);
        var candidateId = "historical-" + name;
        var authorityPath = Path.Combine(dir, "authority.json");
        PlainJson.WriteAtomic(authorityPath, Map(("schemaVersion", 1), ("kind", "release-authority"), ("repository", repository), ("profile", "rc"), ("sha", sha),
            ("candidateId", candidateId), ("candidateRef", candidateRef), ("intentId", "historical-intent"), ("releaseCardId", CardId),
            ("requiredSuites", policy.RequiredSuites), ("exclusions", policy.Exclusions), ("policyHash", policy.PolicyHash), ("policyBlobId", blob.BlobId),
            ("policyRawSha256", ReleaseAuthority.Sha256(blob.Bytes))));
        var discovery = new OrderedDictionary();
        foreach (var suite in policy.RequiredSuites)
            if (suite != "client" && suite != "scripts") discovery[suite] = new object[] { Map(("uid", suite + "-one"), ("class", "Fixture"), ("method", "One")) };
        var plan = ReleaseAuthority.Plan(root, discovery, Map(("client", new[] { "client-tests" }), ("scripts", new[] { "script-tests" })));
        var chunks = new List<object>();
        foreach (IDictionary suite in (IEnumerable)plan["suites"]!)
        {
            if (!(suite["chunks"] is IEnumerable suiteChunks)) continue;
            foreach (IDictionary chunk in suiteChunks)
            {
                var id = (string)chunk["id"]!;
                var trx = Path.Combine(root, "evidence", id + ".trx");
                NightlyFixture.WriteTrx(trx, [new NightlyTrxRow(((string[])chunk["uids"]!)[0], "Fixture", "One")]);
                chunks.Add(Map(("suite", suite["id"]), ("chunk", id), ("exitCode", 0), ("trx", "evidence/" + id + ".trx"), ("trxSha256", NightlyPaths.FileSha256(trx))));
            }
        }
        var authorityDigest = NightlyPaths.FileSha256(authorityPath);
        PlainJson.WriteAtomic(Path.Combine(dir, "ledger.json"), Map(("sha", sha), ("candidateId", candidateId), ("intentId", "historical-intent"), ("runId", "baseline-run"),
            ("selection", "full"), ("seamed", false), ("diagnostic", false), ("noReport", false), ("teardownSucceeded", true), ("authorityDigest", authorityDigest),
            ("executionPlanDigest", NightlyPaths.FileSha256(Path.Combine(dir, "execution-plan.json"))),
            ("prepublicationReceipt", Map(("kind", "prepublication"), ("correlation", Map(("sha", sha), ("runId", "baseline-run"))), ("recipient", Map(("cardId", CardId), ("revision", 4))))),
            ("chunks", chunks.ToArray()),
            ("rosters", new object[]
            {
                Map(("suite", "client"), ("exitCode", 0), ("entries", new object[] { Map(("id", "client-tests"), ("outcome", "passed")) })),
                Map(("suite", "scripts"), ("exitCode", 0), ("entries", new object[] { Map(("id", "script-tests"), ("outcome", "passed")) })),
            })));
        PlainJson.WriteAtomic(Path.Combine(root, "complete-green.json"), Map(("profile", "rc"), ("trigger", "rc"), ("ref", candidateRef), ("sha", sha), ("expectedSha", sha),
            ("candidateId", candidateId), ("runId", "baseline-run"), ("policyHash", policy.PolicyHash), ("exitCode", 0), ("coverageComplete", true), ("testsPassed", true),
            ("reportDelivered", true), ("coverageScope", "Full")));
        var pinned = new PinnedAuthority { Authority = AuthorityRecord.Read(authorityPath), Digest = authorityDigest };
        var publication = ReleaseAuthority.Publication(root, pinned, new string('a', 64), false);
        var manifest = ReleaseGatePublication.NewManifest(Map(("repository", repository), ("candidateId", candidateId), ("sha", sha), ("publicationAuthorityDigest", publication.Digest)));
        PlainJson.WriteAtomic(Path.Combine(root, "release-manifest.json"), manifest);
        PlainJson.WriteAtomic(Path.Combine(root, "publish.json"), Map(("steps", new[] { "published" }), ("sha", sha), ("publicationAuthorityDigest", publication.Digest),
            ("manifestDigest", ReleaseGatePublication.ManifestDigest(manifest)), ("releaseId", "historical-release"), ("releaseUrl", "https://example.invalid/release")));
        var files = new OrderedDictionary();
        foreach (var file in new[] { "authority/authority.json", "authority/execution-plan.json", "authority/discovery.json", "authority/ledger.json",
                     "authority/publication-authority.json", "release-manifest.json", "publish.json", "complete-green.json" })
            files[file] = NightlyPaths.FileSha256(Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar)));
        var path = Path.Combine(releaseRoot, directory, name + ".json");
        PlainJson.WriteAtomic(path, Map(("format", ReleaseSelectionBaseline.Format), ("coverageScope", "Full"), ("profile", "rc"), ("repository", repository), ("sha", sha),
            ("platform", contract["platform"]), ("contract", contract), ("candidateRoot", candidateRoot), ("files", files)));
        return path;
    }

    /// <summary>Rewrites a JSON document after an edit of its parsed map.</summary>
    public static void EditJson(string path, Action<IDictionary> edit)
    {
        var document = ReadJson(path);
        edit(document);
        PlainJson.WriteAtomic(path, document);
    }

    public static OrderedDictionary Map(params (string Key, object? Value)[] pairs)
    {
        var map = new OrderedDictionary(StringComparer.Ordinal);
        foreach (var (key, value) in pairs) map[key] = value;
        return map;
    }
}
