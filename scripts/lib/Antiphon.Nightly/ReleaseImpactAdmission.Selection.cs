#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Antiphon.Nightly;

public static partial class ReleaseImpactAdmission
{
    private static Receipt[]? ReadOwnExecutions(Custody custody, Authority authority, MeasuredChunk[] chunks, GitSnapshot git, Request request)
    {
        if (authority.CurrentExecutions is null) return null;
        var own = new List<Receipt>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (pin, inventory) in authority.CurrentExecutions)
        {
            Require(inventory.Sha == request.CandidateSha && inventory.RunId == request.RunId && inventory.Attempt == request.Attempt &&
                inventory.Revision == request.Revision && seen.Add(pin.Chunk), "p2-receipt:own-inventory");
            var chunk = chunks.SingleOrDefault(row => row.Chunk.Id == pin.Chunk) ?? throw new Refusal("p2-receipt:own-chunk");
            var row = custody.Inventory(inventory).Rows.SingleOrDefault(item => Text(item, "chunk") == pin.Chunk);
            own.Add(ReadReceipt(custody, inventory, row, pin, chunk.Chunk, chunk.Candidate.Digest, authority.ObservedTarget, git, owned: true));
        }
        return own.ToArray();
    }

    private static (RcTrustDecision Decision, RcTrustEntry[] Grants, FileBinding BaselinePolicy, FileBinding CandidatePolicy) ComputeTrust(
        Authority authority, SideSource b, SideSource a, Dictionary<string, Chunk> chunks, Change[] changes,
        GraphEdge[] bGraph, GraphEdge[] aGraph, Receipt[]? own)
    {
        var policyB = b.Tree.Text(RcTrustedProjectGate.PolicyPath);
        var policyA = a.Tree.Text(RcTrustedProjectGate.PolicyPath);
        var rawB = b.Tree.File(RcTrustedProjectGate.PolicyPath);
        var rawA = a.Tree.File(RcTrustedProjectGate.PolicyPath);
        var refusal = new List<string>();
        var configuration = authority.ObservedTarget.Configuration + "|" + authority.ObservedTarget.Architecture;
        var grants = RcTrustedProjectGate.Entries(policyB, refusal).Where(entry =>
            entry.Configurations.Contains(configuration, StringComparer.Ordinal) && entry.ContractProducer == authority.Installation.Extractor).ToArray();
        var policyChanged = rawB.RawSha256 != rawA.RawSha256;
        if (policyChanged) grants = [];
        Require(grants.Select(grant => grant.Project).Distinct(StringComparer.Ordinal).Count() == grants.Length,
            "p2-input:trust-project");
        foreach (var grant in grants)
            foreach (var (chunk, cases) in grant.OwnedTests)
                Require(chunks.TryGetValue(chunk, out var discovery) && discovery.Cases.Select(member => member.CaseUid)
                    .OrderBy(uid => uid, StringComparer.Ordinal).SequenceEqual(cases.OrderBy(uid => uid, StringComparer.Ordinal)),
                    "p2-input:trust-owned-roster");

        string Policy(IEnumerable<RcTrustEntry> entries) => JsonSerializer.Serialize(new
        {
            format = RcTrustedProjectGate.Format, schema = 1,
            entries = entries.Select(entry => new
            {
                entry.Project, entry.Configurations, entry.Downstream, entry.ContractProducer,
                ownedTests = entry.OwnedTests.Select(pair => new { chunk = pair.Key, cases = pair.Value }),
            }),
        }, P2Json);

        var projects = ProjectEdges(bGraph.Concat(aGraph));
        var direct = Hits(changes, b, a, bGraph, aGraph);
        var qualifying = chunks.Values.Select(chunk => new RcTrustChunk
        {
            Id = chunk.Id, Project = chunk.Project,
            Consumes = projects.Select(group => group.Key).Where(producer => producer != chunk.Project && Route(projects, producer, chunk.Project) is not null).ToArray(),
            DirectHit = direct.Any(hit => Applies(hit.Edge, chunk)),
        }).ToArray();
        var results = own?.Select(row => new RcOwnResult
        {
            Chunk = row.Chunk, Disposition = row.Disposition, Result = row.Reason is null ? row.Result : "unproven",
            Skipped = row.Skipped ? 1 : 0, TimedOut = row.TimedOut,
            PassedCases = row.RawCases.Where(result => result.Outcome == "Passed").Select(result => result.CaseKey).ToArray(),
        }).ToArray();
        var contracts = authority.Contracts.Where(contract =>
            (contract.Side == "B" && contract.Sha == b.Tree.Sha || contract.Side == "A" && contract.Sha == a.Tree.Sha) &&
            contract.Extractor == authority.Installation.Extractor &&
            RcChunkReceipt.IsDigest(contract.ContractHash) && RcChunkReceipt.IsDigest(contract.EvidenceDigest))
            .Select(contract => new RcContractReceipt
            {
                Project = contract.Project, Side = contract.Side, ExtractorVersion = contract.Extractor, ContractHash = contract.ContractHash,
            }).ToArray();
        var gatedPolicy = Policy(grants);
        var decision = RcTrustedProjectGate.Decide(gatedPolicy, gatedPolicy,
            changes.Select(change => change.Path).Distinct(StringComparer.Ordinal).ToArray(), qualifying, results, contracts);
        decision.PolicyDigestB = RcTrustedProjectGate.PolicyDigest(policyB);
        decision.PolicyDigestA = RcTrustedProjectGate.PolicyDigest(policyA);
        decision.Refused = refusal.Concat(decision.Refused).Distinct(StringComparer.Ordinal).ToArray();
        if (policyChanged) decision.Reasons = ["trust-policy-changed", .. decision.Reasons];
        return (decision, grants, rawB, rawA);
    }

    private sealed record Hit(Change Change, GraphEdge Edge);

    private static Hit[] Hits(Change[] changes, SideSource b, SideSource a, GraphEdge[] oldGraph, GraphEdge[] newGraph)
    {
        var hits = new HashSet<Hit>();
        var graphB = oldGraph.ToLookup(edge => edge.Source, StringComparer.Ordinal);
        var graphA = newGraph.ToLookup(edge => edge.Source, StringComparer.Ordinal);
        foreach (var change in changes)
        {
            var endpoint = change.Side == "B" ? b : a;
            var graph = change.Side == "B" ? graphB : graphA;
            var direct = graph[change.Path].Where(edge => edge.Kind != "project").ToArray();
            if (endpoint.Capped.Contains(change.Path, StringComparer.Ordinal))
                Require(direct.Any(edge => edge.Kind is "owner" or "linked"), "p2-incomplete:capped:" + change.Side);
            if (change.Path is not (RcChunkSelection.IndexPath or RcTrustedProjectGate.PolicyPath))
                Require(direct.Length > 0, "p2-incomplete:unbounded:" + change.Side);
            foreach (var edge in direct) hits.Add(new Hit(change, edge));
        }

        if (b.Tree.File(RcChunkSelection.IndexPath).BlobId != a.Tree.File(RcChunkSelection.IndexPath).BlobId)
        {
            var baselineMetadata = b.Index.GetProperty("metadata");
            var candidateMetadata = a.Index.GetProperty("metadata");
            Require(JsonElement.DeepEquals(baselineMetadata, candidateMetadata), "p2-incomplete:index-metadata");
            foreach (var (side, source, opposite) in new[] { ("B", oldGraph, newGraph), ("A", newGraph, oldGraph) })
                foreach (var edge in source.Except(opposite).Where(edge => edge.Kind == "derived"))
                    hits.Add(new Hit(new Change(edge.Source, "M", side), edge));
        }
        return hits.OrderBy(hit => hit.Change.Path, StringComparer.Ordinal).ThenBy(hit => hit.Change.Side, StringComparer.Ordinal)
            .ThenBy(hit => hit.Edge.Kind, StringComparer.Ordinal).ThenBy(hit => hit.Edge.Destination, StringComparer.Ordinal)
            .ThenBy(hit => hit.Edge.Class, StringComparer.Ordinal).ToArray();
    }

    private static ILookup<string, string> ProjectEdges(IEnumerable<GraphEdge> graph) =>
        graph.Where(edge => edge.Kind == "project").OrderBy(edge => edge.Destination, StringComparer.Ordinal)
            .ToLookup(edge => edge.Source, edge => edge.Destination, StringComparer.Ordinal);

    private static bool Applies(GraphEdge edge, Chunk chunk) => edge.Destination == chunk.Project &&
        (edge.Class.Length == 0 || chunk.Cases.Any(member => member.Class == edge.Class));

    private static string[]? Route(ILookup<string, string> graph, string start, string destination,
        IReadOnlyCollection<string>? excludedFirstHops = null)
    {
        var queue = new Queue<string>();
        var seen = new Dictionary<string, string?>(StringComparer.Ordinal) { [start] = null };
        queue.Enqueue(start);
        while (queue.TryDequeue(out var current))
        {
            if (current == destination)
            {
                var chain = new List<string>();
                for (string? node = current; node is not null; node = seen[node]) chain.Add(node);
                chain.Reverse();
                return chain.ToArray();
            }
            foreach (var next in graph[current])
            {
                // A grant names edges from this producer. Removing a downstream node would also hide routes
                // that reach it by an independent edge through another project.
                if (current == start && (excludedFirstHops?.Contains(next, StringComparer.Ordinal) ?? false)) continue;
                if (seen.TryAdd(next, current)) queue.Enqueue(next);
            }
        }
        return null;
    }

    private static Selection[] Select(MeasuredChunk[] chunks, Change[] changes, SideSource b, SideSource a,
        GraphEdge[] bGraph, GraphEdge[] aGraph, IReadOnlyDictionary<string, Receipt> receipts,
        IReadOnlyDictionary<string, string> owed, RcTrustDecision trust, RcTrustEntry[] grants)
    {
        var impacts = Hits(changes, b, a, bGraph, aGraph);
        var oldProjects = ProjectEdges(bGraph);
        var newProjects = ProjectEdges(aGraph);
        var result = new List<Selection>();
        foreach (var entry in chunks)
        {
            var chunk = entry.Chunk;
            var reasons = new List<SelectionReason>();
            var gated = trust.Of(chunk.Id);
            foreach (var hit in impacts)
            {
                var (change, edge) = hit;
                if (Applies(edge, chunk))
                {
                    reasons.Add(new SelectionReason(edge.Kind, change.Path, change.Side, [edge.Destination]));
                    continue;
                }
                if (edge.Class.Length > 0 || edge.Destination == chunk.Project) continue;
                var graph = change.Side == "B" ? oldProjects : newProjects;
                var chain = Route(graph, edge.Destination, chunk.Project);
                if (chain is null) continue;
                var grant = grants.SingleOrDefault(grant => grant.Project == edge.Destination);
                var ownerDirectory = edge.Destination[..(edge.Destination.LastIndexOf('/') + 1)];
                var onlyTrustedEdges = grant is not null && change.Path.StartsWith(ownerDirectory, StringComparison.Ordinal)
                    && Route(graph, edge.Destination, chunk.Project, grant.Downstream) is null;
                var released = gated.Reasons.Contains("trust-gate-passed:" + edge.Destination, StringComparer.Ordinal);
                var held = gated.Reasons.Contains("held:" + edge.Destination, StringComparer.Ordinal);
                if (!(onlyTrustedEdges && (released || held))) reasons.Add(new SelectionReason("project", change.Path, change.Side, chain));
            }

            if (gated.Decision != "carry" || gated.Reasons.Length > 0)
                reasons.AddRange(gated.Reasons.Select(reason => new SelectionReason("trust", null, null, [reason])));
            if (owed.TryGetValue(chunk.Id, out var obligation))
                reasons.Add(new SelectionReason("later-obligation", null, null, [obligation]));
            var prior = receipts.GetValueOrDefault(chunk.Id);
            var sameContent = prior is not null && prior.ContentDigest == entry.ContentDigest;
            var equivalent = entry.Baseline is not null && !RcChunkDigest.Compare(entry.Baseline, entry.Candidate).Dirty;
            var dirty = !sameContent || !equivalent || prior?.Reason is not null;
            if (!sameContent) reasons.Add(new SelectionReason("content", null, null, ["no-matching-content"]));
            if (!equivalent) reasons.Add(new SelectionReason("inputs", null, null, ["input-digest-changed"]));
            if (prior?.Reason is { } priorReason) reasons.Add(new SelectionReason("receipt", null, null, [priorReason]));

            var state = gated.Decision == "held" && !dirty && !owed.ContainsKey(chunk.Id) && reasons.All(reason => reason.Kind == "trust")
                ? "held"
                : reasons.Count > 0 && (gated.Decision != "carry" || reasons.Any(reason => reason.Kind != "trust") || dirty)
                    ? "execute"
                    : "carry-candidate";
            Require(state != "carry-candidate" || prior?.Origin is not null, "p2-incomplete:origin");
            result.Add(new Selection(chunk, state, state == "carry-candidate" ? prior!.Origin : null, reasons.ToArray()));
        }
        return result.ToArray();
    }
}
