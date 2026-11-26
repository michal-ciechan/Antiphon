#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Antiphon.Nightly;

public static partial class ReleaseImpactAdmission
{
    private sealed record MeasuredChunk(Chunk Chunk, string ContentDigest, RcChunkInputManifest? Baseline, RcChunkInputManifest Candidate);
    private sealed record Selection(Chunk Chunk, string Disposition, Origin? Origin, SelectionReason[] Reasons);

    public static Preparation Prepare(Request request, IReadOnlyList<Chunk>? discovery, Authority? authority)
    {
        if (authority is null || discovery is null) return new Preparation(null, "p2-incomplete:independent-evidence");
        try
        {
            return new Preparation(Derive(request, discovery, authority), "");
        }
        catch (Refusal error)
        {
            return new Preparation(null, error.Message);
        }
        catch (TestSelectionRefusal)
        {
            return new Preparation(null, "p2-input:compatibility");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
            or FormatException or ArgumentException or KeyNotFoundException or OverflowException or RcChunkDigestRefusal or RcInputRefusal
            or RcBoundaryRefusal or NightlyRefusal)
        {
            return new Preparation(null, "p2-incomplete:independent-evidence");
        }
    }

    private static EvidenceSnapshot Derive(Request request, IReadOnlyList<Chunk> discovery, Authority authority)
    {
        Require(!string.IsNullOrWhiteSpace(request.RepositoryId) && !string.IsNullOrWhiteSpace(request.Line) &&
            !string.IsNullOrWhiteSpace(request.RunId), "p2-context:identity");
        var approval = authority.Approval;
        Require(approval.ApprovedRequest == request && !approval.Withdrawn && approval.ObservedAt <= approval.ValidUntil && approval.Watermark.Length > 0,
            "p2-context:approval");
        var target = authority.ObservedTarget;
        Require(target.Profile.Length > 0 && target.Architecture.Length > 0 && target.Tfm.Length > 0 &&
            target.Rid.Length > 0 && target.Configuration.Length > 0, "p2-incomplete:target");
        Require(TargetIntentDigest(target) == request.TargetIntentDigest, "p2-context:target");
        using var git = new GitSnapshot(authority.RepositoryRoot);
        Require(git.Commit(request.BaselineSha) && git.Commit(request.CandidateSha), "p2-context:commit");
        Require(git.Ancestor(request.BaselineSha, request.CandidateSha), "p2-context:ancestry");
        var b = ReadSide(authority, git.ReadTree(request.BaselineSha), "B");
        var a = request.BaselineSha == request.CandidateSha
            ? ReadSide(authority, b.Tree, "A")
            : ReadSide(authority, git.ReadTree(request.CandidateSha), "A");
        var actualTool = ReadTool(authority.Installation);

        var custody = new Custody();
        var inventory = authority.Inventory;
        Require(inventory.Sha == b.Tree.Sha, "p2-receipt:inventory-sha");
        var inventoryRows = custody.Inventory(inventory).Rows.ToDictionary(row => Text(row, "chunk"), StringComparer.Ordinal);

        var baseline = authority.BaselineChunks.ToDictionary(chunk => chunk.Id, StringComparer.Ordinal);
        var current = discovery.ToDictionary(chunk => chunk.Id, StringComparer.Ordinal);
        var origins = authority.Origins.ToDictionary(pin => pin.Chunk, StringComparer.Ordinal);
        Require(inventoryRows.Keys.All(baseline.ContainsKey) && origins.Keys.All(inventoryRows.ContainsKey), "p2-receipt:inventory-roster");
        ValidateUniverse(authority.BaselineChunks, b.ProjectGraph);
        ValidateUniverse(discovery, a.ProjectGraph);
        CheckObservedInputs(authority.ObservedInputs, b.Tree, a.Tree, baseline.Keys, current.Keys);

        var baselineMeasures = baseline.Values.ToDictionary(chunk => chunk.Id, chunk => Measure(authority, "B", b, chunk),
            StringComparer.Ordinal);
        var measured = discovery.Select(chunk => new MeasuredChunk(chunk, ContentDigest(chunk),
            baselineMeasures.GetValueOrDefault(chunk.Id), Measure(authority, "A", a, chunk))).ToArray();
        var receipts = baseline.Values.ToDictionary(chunk => chunk.Id, chunk => ReadReceipt(custody, inventory,
            inventoryRows.GetValueOrDefault(chunk.Id), origins.GetValueOrDefault(chunk.Id), chunk, baselineMeasures[chunk.Id].Digest, target, git),
            StringComparer.Ordinal);

        var changes = git.Diff(b.Tree.Sha, a.Tree.Sha);
        var bGraph = Graph(b, authority.BaselineChunks);
        var aGraph = Graph(a, discovery);
        var history = authority.History.OrderBy(entry => entry.Ordinal).ToArray();
        Require(history.Select(entry => entry.Ordinal).Distinct().Count() == history.Length, "p2-receipt:history-order");
        var owed = history.Where(entry => entry.Revision <= request.Revision)
            .GroupBy(entry => entry.Chunk, StringComparer.Ordinal).Select(group => group.Last())
            .Where(entry => entry.Status == "open" && current.ContainsKey(entry.Chunk))
            .ToDictionary(entry => entry.Chunk, entry => entry.Reason, StringComparer.Ordinal);
        Require(history.All(entry => entry.Revision <= request.Revision && git.Commit(entry.Sha)), "p2-context:history");

        var own = ReadOwnExecutions(custody, authority, measured, git, request);
        var (trust, grants, policyB, policyA) = ComputeTrust(authority, b, a, current, changes, bGraph, aGraph, own);
        Require(trust.Mode != "Full", "p2-incomplete:trust-global");
        var globalReasons = new[] { "catalogHash", "policyHash", "runtimeHash", "selectorHash" }
            .Where(component => !JsonElement.DeepEquals(b.Contract.GetProperty(component), a.Contract.GetProperty(component)))
            .Select(component => "contract-changed:" + component).ToArray();
        var incompleteReasons = (b.Evaluation.Inputs.Unresolved ?? []).Concat(a.Evaluation.Inputs.Unresolved ?? [])
            .Concat(measured.SelectMany(row => row.Candidate.Unknown.Concat(row.Baseline?.Unknown ?? [])))
            .Distinct(StringComparer.Ordinal).ToArray();
        var selection = Select(measured, changes, b, a, bGraph, aGraph, receipts, owed, trust, grants);
        var rows = selection.Select(decision => new Row(decision.Chunk.Id, decision.Disposition, decision.Chunk.Cases, decision.Origin, decision.Reasons)).ToArray();
        var context = Json(new
        {
            format = ReleaseImpactIntegrity.Format, request.RepositoryId, request.Line,
            request.BaselineSha, baselineTree = b.Tree.Id, request.CandidateSha, candidateTree = a.Tree.Id,
            request.RunId, request.Attempt, request.Revision, algorithmSha = authority.Installation.SourceSha,
        });
        var roles = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["target"] = TargetProjection(target, authority.ObservedInputs),
            ["algorithm"] = actualTool,
            ["baselineManifest"] = Json(b.Tree.Files.Values),
            ["candidateManifest"] = Json(a.Tree.Files.Values),
            ["compatibility"] = Json(new
            {
                baseline = b.Contract, candidate = a.Contract,
                scoped = new[] { (Side: "B", b.Evaluation), (Side: "A", a.Evaluation) }
                    .SelectMany(endpoint => endpoint.Evaluation.Compatibility.Select(entry =>
                        new { name = endpoint.Side + "/" + entry.Key, value = entry.Value })),
                globalReasons,
            }),
            ["baselineIndex"] = b.Index,
            ["candidateIndex"] = a.Index,
            ["baselineGraph"] = Json(bGraph),
            ["candidateGraph"] = Json(aGraph),
            ["diff"] = Json(changes),
            ["universe"] = Json(discovery.SelectMany(chunk => chunk.Cases)),
            ["roster"] = Json(measured.Select(row => new
            {
                chunk = row.Chunk.Id, row.Chunk.Suite, row.Chunk.ExecutionKind, row.Chunk.Project, row.ContentDigest,
                baselineInputDigest = row.Baseline?.Digest, candidateInputDigest = row.Candidate.Digest, row.Chunk.Cases,
            })),
            ["inventory"] = Json(new
            {
                format = RcChunkReceipt.InventoryFormat, inventory.RunId, inventory.Attempt, inventory.Revision, inventory.Sha,
                rawByteCount = inventory.Artifact.ByteCount, rawSha256 = inventory.Artifact.Digest,
            }),
            ["receipts"] = Json(receipts.Values.Select(row => row.Projection)),
            ["obligations"] = Json(new
            {
                watermark = approval.Watermark + "/" + approval.ValidUntil.ToString("O", CultureInfo.InvariantCulture), observedAt = approval.ObservedAt.ToString("O", CultureInfo.InvariantCulture),
                approved = true, approval.Withdrawn, history, owed = owed.Select(entry => new { chunk = entry.Key, reason = entry.Value }),
            }),
            ["trust"] = Json(new
            {
                format = RcTrustedProjectGate.Format, baselinePolicy = policyB, candidatePolicy = policyA,
                baselinePolicyDigest = trust.PolicyDigestB, candidatePolicyDigest = trust.PolicyDigestA,
                grants = grants.Select(grant => new
                {
                    grant.Project, grant.Configurations, grant.Downstream, grant.ContractProducer,
                    owned = grant.OwnedTests.Select(entry => new { chunk = entry.Key, cases = entry.Value }),
                }),
                authority.Contracts, ownExecutions = (own ?? []).Select(row => row.Projection),
                reasons = trust.Reasons.Concat(trust.Refused).Distinct(StringComparer.Ordinal),
            }),
            ["selection"] = Json(new
            {
                complete = incompleteReasons.Length == 0, fallback = incompleteReasons.Length + globalReasons.Length == 0 ? "Scoped" : "Full",
                globalReasons, incompleteReasons,
                classes = selection.Where(row => row.Disposition == "execute").SelectMany(row => row.Chunk.Cases)
                    .Select(c => new { c.Project, c.Class }).Distinct(),
                decisions = selection.Select(row => new { chunk = row.Chunk.Id, row.Disposition, row.Origin, row.Reasons }),
            }),
        };
        return new EvidenceSnapshot(request, discovery, rows, context, roles, target.FoldedPaths, authority.OpenProjection);
    }

    public static string TargetIntentDigest(Target target) => ReleaseImpactIntegrity.RawSha256(
        ReleaseImpactIntegrity.ProjectionBytes("target", TargetProjection(target, []), target.FoldedPaths));

    private static JsonElement TargetProjection(Target target, IReadOnlyList<ObservedInput> observed) => Json(new
    {
        target.Os, target.Architecture, target.Profile, target.Tfm, target.Rid, target.Configuration, target.FoldedPaths,
        buildProperties = target.BuildProperties.Select(entry => new { name = entry.Key, value = entry.Value }),
        target.Argv, observed,
    });

    private static JsonElement ReadTool(Installation tool)
    {
        var observed = ReleaseImpactEvidence.AlgorithmIdentity(tool.SourceRepository);
        Require(observed.Sha == tool.SourceSha && tool.Extractor.Length > 0 && tool.EvaluatorVersion.Length > 0
            && tool.ReviewedFiles.Count > 0, "p2-input:algorithm");
        using var assembly = File.OpenRead(typeof(ReleaseImpactAdmission).Assembly.Location);
        var assemblyDigest = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(assembly));
        Require(assemblyDigest == tool.LoadedAssemblyDigest, "p2-input:actual-installation");
        var sources = tool.ReviewedFiles.OrderBy(file => file.Path, StringComparer.Ordinal).Select(file =>
        {
            Require(file.Size <= ReleaseImpactIntegrity.ProofLimit, "p2-incomplete:algorithm-size");
            using var stream = tool.OpenInstalledFile(file.Path);
            var bytes = ReleaseImpactIntegrity.ReadBounded(stream, (int)file.Size, "algorithm-size");
            Require((ulong)bytes.Length == file.Size && ReleaseImpactIntegrity.RawSha256(bytes) == file.Digest
                && ReleaseImpactEvidence.BlobId(bytes, 40) == file.BlobId, "p2-input:actual-installation");
            return new FileBinding(file.Path, file.Mode, file.BlobId, file.Size, file.Digest);
        }).ToArray();
        var rawClosureDigest = RcChunkDigest.ManifestDigest(sources.Select(file => new[]
        {
            file.Path, file.GitMode, file.BlobId, file.RawByteCount.ToString(CultureInfo.InvariantCulture), file.RawSha256,
        }).Prepend(new[] { observed.Digest, assemblyDigest }).ToArray());
        return Json(new
        {
            rawClosureDigest, p2Codec = ReleaseImpactIntegrity.Format, evaluator = tool.EvaluatorVersion,
            graph = RcProjectSelection.PlannerHash, extractor = tool.Extractor,
            replaySelector = RcSelectionReplay.SelectorHash, sources,
        });
    }

    private static void ValidateUniverse(IReadOnlyList<Chunk> chunks, RcProjectGraph graph)
    {
        var identity = new HashSet<Case>();
        foreach (var chunk in chunks)
        {
            Require(chunk.Id.Length > 0 && chunk.Cases.Count > 0 && graph.Projects.ContainsKey(chunk.Project),
                "p2-incomplete:empty-or-unknown-chunk");
            Require(chunk.Cases.All(member => member.Project == chunk.Project && identity.Add(member)), "p2-selection:universe");
        }
    }

    private static readonly string[] TargetObservations = ["sdk", "runtime", "package", "lock", "launcher", "build", "discovery"];

    private static void CheckObservedInputs(IReadOnlyList<ObservedInput> inputs, Tree before, Tree after,
        IEnumerable<string> baseline, IEnumerable<string> current)
    {
        var groups = inputs.GroupBy(input => (input.Side, input.Chunk)).ToDictionary(group => group.Key, group => group.ToArray());
        foreach (var (side, tree, chunks) in new[] { ("B", before, baseline), ("A", after, current) })
        {
            foreach (var chunk in chunks.Cast<string?>().Prepend(null))
            {
                if (!groups.Remove((side, chunk), out var entries)) throw new Refusal("p2-incomplete:observations");
                var kinds = chunk is null ? TargetObservations : RcChunkDigest.RequiredReceipts;
                Require(entries.Select(input => input.Role).OrderBy(role => role, StringComparer.Ordinal)
                    .SequenceEqual(kinds.OrderBy(kind => kind, StringComparer.Ordinal)) && entries.All(input =>
                        input.SourceSha == tree.Sha && RcChunkReceipt.IsDigest(input.Digest) && RcChunkReceipt.IsDigest(input.ProvenanceDigest)),
                    "p2-input:observations:" + side);
            }
        }
        Require(groups.Count == 0, "p2-input:observations");
    }

    private static RcChunkInputManifest Measure(Authority authority, string side, SideSource endpoint, Chunk chunk)
    {
        var cases = chunk.Cases.Select(member => new RcChunkCase
        {
            Suite = chunk.Suite, ClassName = member.Class, Method = member.Method, CaseKey = member.CaseUid, Stable = true,
        }).ToArray();
        var membership = RcBoundary.NewChunk(cases.Select(member => new RcBoundaryRow
        {
            Suite = chunk.Suite, Boundary = "scripts", IdentityKey = member.ClassName + "/" + member.Method, CaseKey = member.CaseKey,
        }).ToArray());
        var input = endpoint.Evaluation.Inputs.Suites.SingleOrDefault(suite => suite.Project == chunk.Project)
            ?? throw new Refusal("p2-incomplete:input-owner");
        var observed = authority.ObservedInputs.Where(item => item.Side == side && item.Chunk == chunk.Id).Select(item => new RcActualReceipt
        {
            Kind = item.Role, Digest = item.Digest, ProvenanceDigest = item.ProvenanceDigest, SourceSha = item.SourceSha,
        }).ToArray();
        var result = RcChunkDigest.NewManifest(endpoint.Evaluation.Inputs, input.Id, membership, cases, observed);
        return result;
    }

    private static GraphEdge[] Graph(SideSource endpoint, IReadOnlyList<Chunk> chunks)
    {
        var graph = new HashSet<GraphEdge>();
        var classes = chunks.SelectMany(chunk => chunk.Cases).Select(member => (member.Project, member.Class)).Distinct().ToArray();
        foreach (var edge in endpoint.Edges)
            foreach (var member in classes.Where(member => member.Class == edge.Class ||
                member.Class.EndsWith("." + edge.Class, StringComparison.Ordinal)))
                graph.Add(new GraphEdge("derived", edge.Path, member.Project, member.Class));
        foreach (var node in endpoint.ProjectGraph.Projects.Values)
        {
            var directory = node.Path[..(node.Path.LastIndexOf('/') + 1)];
            foreach (var path in endpoint.Tree.Files.Keys.Where(path => path.StartsWith(directory, StringComparison.Ordinal)))
                graph.Add(new GraphEdge("owner", path, node.Path, ""));
            foreach (var path in node.Inputs)
                graph.Add(new GraphEdge("linked", path, node.Path, ""));
            foreach (var dependency in node.References)
                graph.Add(new GraphEdge("project", dependency, node.Path, ""));
        }
        foreach (var reader in endpoint.Evaluation.Readers)
            foreach (var glob in reader.Globs.Select(pattern => RcInputInventory.Pattern(pattern)))
                foreach (var path in endpoint.Tree.Files.Keys.Where(glob.IsMatch))
                    foreach (var member in classes.Where(member => member.Class == reader.Reader ||
                        member.Class.EndsWith("." + reader.Reader, StringComparison.Ordinal)))
                        graph.Add(new GraphEdge("runtime", path, member.Project, member.Class));
        return graph.ToArray();
    }
}
