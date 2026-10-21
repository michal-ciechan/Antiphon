using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain;

namespace Antiphon.Server.Application.Services;

/// <summary>Classifies pinned Git objects after rebase. Every unknown input requires a build.</summary>
internal sealed class LandVerificationPolicy(ILandingGit git)
{
    internal sealed record Decision(bool Skip, string Reason);

    internal async Task<Decision> DecideAsync(string repository, string targetRef, string targetSha,
        string? candidateSha, string? filter, bool forceBuild, CancellationToken ct)
    {
        if (forceBuild) return new(false, "forced-build");
        if (!string.IsNullOrWhiteSpace(filter)) return new(false, "selected-tests");
        if (targetRef != "refs/heads/master") return new(false, "target-requires-build");
        if (!GitObjectId.IsFull(targetSha) || !GitObjectId.IsFull(candidateSha)) return new(false, "invalid-object-id");
        try
        {
            // No worktree/index diff, external diff helper, text conversion, or task prose participates.
            var diff = await git.RunAsync(repository,
                ["diff-tree", "--no-commit-id", "--raw", "-r", "-z", "--no-abbrev", "--no-ext-diff",
                    "--no-textconv", "--find-renames", targetSha, candidateSha!, "--"], ct);
            if (!diff.Succeeded || !OnlyDocumentation(diff.Output)) return new(false, "diff-requires-build");
            var tree = await git.RunAsync(repository, ["ls-tree", "-r", "-z", candidateSha!], ct);
            if (!tree.Succeeded || !MatchesAuditedGraph(tree.Output)) return new(false, "unrecognized-build-graph");
            return new(true, "non-code");
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return new(false, "policy-read-failed");
        }
    }

    private static bool OnlyDocumentation(string raw)
    {
        if (raw.Length == 0) return true;
        if (!raw.EndsWith('\0') || raw.Contains('\uFFFD')) return false;
        var fields = raw.Split('\0');
        var i = 0;
        while (i < fields.Length - 1)
        {
            var header = fields[i++].Split(' ');
            if (header.Length != 5 || header[0].Length != 7 || header[0][0] != ':'
                || !GitObjectId.IsFull(header[2]) || !GitObjectId.IsFull(header[3])
                || header[2].Length != header[3].Length) return false;
            var oldMode = header[0][1..];
            var newMode = header[1];
            var status = header[4];
            var rename = status.StartsWith('R') && status.Length > 1
                && status[1..].All(char.IsAsciiDigit) && int.TryParse(status[1..], out var score) && score <= 100;
            if (!(status is "A" or "D" or "M" || rename)) return false;
            // Only ordinary, non-executable files. Symlinks, gitlinks and all mode/type changes build.
            if (oldMode != (status == "A" ? "000000" : "100644")
                || newMode != (status == "D" ? "000000" : "100644")) return false;
            if (header[2].All(c => c == '0') != (status == "A")
                || header[3].All(c => c == '0') != (status == "D")) return false;
            if (i >= fields.Length - 1 || !DocumentationPath(fields[i++])) return false;
            if (rename && (i >= fields.Length - 1 || !DocumentationPath(fields[i++]))) return false;
        }
        return i == fields.Length - 1;
    }

    private static bool DocumentationPath(string path) => SafePath(path)
        && (path == "README.md" || path.StartsWith("docs/", StringComparison.Ordinal) && path.EndsWith(".md", StringComparison.Ordinal));

    private static bool SafePath(string path) => path.Length > 0 && !path.Contains('\\')
        && !path.Any(char.IsControl) && !path.Contains('\uFFFD')
        && path.Split('/').All(component => component is not ("" or "." or ".."));

    private static bool MatchesAuditedGraph(string raw)
    {
        if (!raw.EndsWith('\0') || raw.Contains('\uFFFD')) return false;
        var graph = new LandVerificationBuildGraph();
        var selected = new List<string>();
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in raw.Split('\0').SkipLast(1))
        {
            var tab = row.IndexOf('\t');
            if (tab < 0) return false;
            var metadata = row[..tab].Split(' ');
            var path = row[(tab + 1)..];
            if (metadata.Length != 3 || !GitObjectId.IsFull(metadata[2]) || !SafePath(path) || !paths.Add(path)) return false;
            if (!((metadata[0] is "100644" or "100755" or "120000") && metadata[1] == "blob"
                || metadata[0] == "160000" && metadata[1] == "commit")
                || metadata[2].All(c => c == '0')) return false;
            if (!graph.Includes(path)) continue;
            if (metadata[0] is not ("100644" or "100755") || metadata[1] != "blob") return false;
            selected.Add(row);
        }
        var audited = LandVerificationBuildGraph.AuditedCount;
        return audited is int count
            && count == selected.Count
            && LandVerificationBuildGraph.FingerprintOf(selected) == LandVerificationBuildGraph.Fingerprint;
    }
}
