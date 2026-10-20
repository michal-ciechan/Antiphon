namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1380 S2: audited graph inventory, not a candidate-supplied allowlist. The digest covers
/// the ordinal-sorted, NUL-terminated ls-tree records selected below (mode, blob ID and path).
/// Audited at a935ec8973a074a8b075ed313fffc87ba63b3189: 292 entries. Projects embed bundle
/// Markdown and docs JSON, but no docs Markdown or root README in the verification build.
/// Re-audited at fbbb0ee2f1f0c8e5cdddaa5f982b249e886dc34d after the S2 re-pick:
/// the same 292 entries, with five newer-master PowerShell changes for RC candidate identity,
/// its fixture, bounded-reader timing and the desktop smoke guard; no new documentation inputs.
/// Re-audited at bfcb02e8758febf088c2c1f97b3f401e417f0a77 on master 706d99b8a6eb624ddb1bda535b015205b2c0d50e:
/// the same 292 entries; only scripts/validate-checkpoint-receipt.ps1 changed since the prior
/// audit, adding a failed-report verdict check. Imports/resources/entrypoints still consume
/// no docs Markdown or root README in the verification build.
/// Re-audit imports/resources/entrypoints before changing this digest; drift means build.
/// </summary>
internal sealed class LandVerificationBuildGraph
{
    internal const string Fingerprint = "D17D2800E1313509226CE7C83FDE19C8ABFD3AD5BA4A1C4CFA4BAA6EC980C789";

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
