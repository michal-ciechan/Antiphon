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
            var candidate = await git.RunAsync(repository, ["ls-tree", "-r", "-z", candidateSha!], ct);
            if (!candidate.Succeeded || !LandVerificationBuildGraph.TryReadGraph(candidate.Output, out var candidateDigest, out var candidateCount))
                return new(false, "unrecognized-build-graph");
            // Embedded match skips before the target manifest is read, so a missing or stale committed manifest
            // still skips when the running server already audited this graph.
            if (LandVerificationBuildGraph.AuditedCount is int audited
                && audited == candidateCount
                && candidateDigest == LandVerificationBuildGraph.Fingerprint)
                return new(true, "non-code");
            var target = await git.RunAsync(repository, ["ls-tree", "-r", "-z", targetSha], ct);
            if (!target.Succeeded
                || !LandVerificationBuildGraph.TryReadGraph(target.Output, out var targetDigest, out var targetCount)
                || candidateDigest != targetDigest
                || candidateCount != targetCount)
                return new(false, "unrecognized-build-graph");
            var shown = await git.RunAsync(repository,
                ["show", "--no-textconv", targetSha + ":" + LandVerificationBuildGraph.ManifestPath], ct);
            if (!shown.Succeeded || shown.Output.Contains('\uFFFD'))
                return new(false, "unrecognized-build-graph");
            var (manifestDigest, manifestCount) = LandVerificationBuildGraph.ParseManifest(shown.Output);
            if (manifestDigest is null || manifestCount is null
                || manifestDigest != targetDigest || manifestCount != targetCount)
                return new(false, "unrecognized-build-graph");
            return new(true, "docs-skip-target-manifest");
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

}
