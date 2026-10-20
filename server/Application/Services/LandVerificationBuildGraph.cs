namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1380 S2: audited graph inventory, not a candidate-supplied allowlist. The digest covers
/// the ordinal-sorted, NUL-terminated ls-tree records selected below (mode, blob ID and path).
/// Audited at a935ec8973a074a8b075ed313fffc87ba63b3189: 292 entries. Projects embed bundle
/// Markdown and docs JSON, but no docs Markdown or root README in the verification build.
/// Re-audit imports/resources/entrypoints before changing this digest; drift means build.
/// </summary>
internal sealed class LandVerificationBuildGraph
{
    internal const string Fingerprint = "45F95B3ED7699BC2EA32731BBB58A5B1070B9E90158BC35F94B3E1904DEB1AD1";

    internal bool Includes(string path)
    {
        var name = Path.GetFileName(path);
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension.EndsWith("proj", StringComparison.Ordinal)
            || extension is ".props" or ".targets" or ".sln" or ".slnx" or ".slnf" or ".rsp"
                or ".ps1" or ".sh" or ".cmd" or ".bat" or ".py"
            || name.StartsWith("Directory.Build.", StringComparison.OrdinalIgnoreCase)
            || name.ToLowerInvariant() is "global.json" or "nuget.config" or "packages.lock.json"
                or "packages.config" or "package.json" or "package-lock.json" or "yarn.lock"
                or "pnpm-lock.yaml" or ".gitmodules" or ".gitattributes" or "dotnet-tools.json";
    }
}
