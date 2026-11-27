namespace Antiphon.Checkpoints;

internal static class CheckpointBuildPlacement
{
    public const string Refusal = "workspace_evidence_write";
    private static readonly string[] EvidenceRoots =
        ["/work/evidence", "/work/code-evidence", "/work/review-evidence", "/work/merge-evidence", "/work/test-evidence"];

    public static readonly string[] EvaluatedPaths =
    [
        "MSBuildProjectDirectory", "OutputPath", "OutDir", "BaseOutputPath", "BaseIntermediateOutputPath",
        "IntermediateOutputPath", "MSBuildProjectExtensionsPath", "ArtifactsPath", "PublishDir",
        "RestoreOutputPath", "RestorePackagesPath", "PackageOutputPath",
    ];

    public static bool IsEvidencePath(string path)
    {
        if (!OperatingSystem.IsLinux())
            return false;
        if (UnderEvidence(path))
            return true;

        // An output can be new while one of its existing ancestors is a link into evidence.
        var physical = "/";
        foreach (var component in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            physical = Path.Combine(physical, component);
            var entry = new DirectoryInfo(physical);
            if (entry.LinkTarget is not null)
                physical = entry.ResolveLinkTarget(returnFinalTarget: true)!.FullName;
            if (UnderEvidence(physical))
                return true;
        }
        return false;
    }

    private static bool UnderEvidence(string path) => EvidenceRoots.Any(root =>
        path.Equals(root, StringComparison.Ordinal) || path.StartsWith(root + "/", StringComparison.Ordinal));
}
