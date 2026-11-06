using System.Text.RegularExpressions;

namespace Antiphon.Checkpoints;

public sealed record ConditionalSkipIdentity(string ClassName, string Method, int Count, string Reason);

/// <summary>Source-owned permission data, not evidence that a capability is unavailable.</summary>
public sealed record ConditionalSkipContract(string Id, string Probe, string Project, string Filter,
    int ExpectedTotal, int MinExecuted, IReadOnlyList<ConditionalSkipIdentity> Skips)
{
    public const string ProbeId = "file-symlink-create-v1";

    public static bool IsValidId(string? value) => value is not null
        && Regex.IsMatch(value, @"\A[A-Za-z][A-Za-z0-9._-]*\z", RegexOptions.CultureInvariant)
        && !new[] { "null", "true", "false" }.Contains(value, StringComparer.OrdinalIgnoreCase);

    // Pure admission boundary shared by import and YAML. Runtime/source/probe qualification
    // is a separate S2 boundary; this model never grants permission to a receipt.
    public static void ValidateCombinations(CheckpointManifest manifest, bool mergedAttempts = false)
    {
        var conditional = manifest.Checkpoints.Where(row => row.ConditionalSkips is not null).ToList();
        if (conditional.Count == 0) return;
        foreach (var row in conditional)
        {
            if (!IsValidId(row.ConditionalSkips)) Refuse("conditionalSkips must be a single nonempty contract ID");
            if (row.IsCommand) Refuse("conditional command rows are unsupported");
            if (row.MinExecuted is not > 0) Refuse("conditional rows require a positive execution floor");
            if (row.Repeat != 1) Refuse("conditional rows cannot repeat");
        }
        if (manifest.Rerun.KnownFlaky.Count != 0) Refuse("conditional rows cannot use known-flaky reruns");
        if (manifest.Checkpoints.Any(row => row.Repeat != 1)) Refuse("conditional runs cannot contain repeated siblings");
        if (mergedAttempts) Refuse("conditional merged-attempt evidence is unsupported");
    }

    private static void Refuse(string message) => throw new ManifestValidationException("conditionalSkips", message);
}

public sealed record ConditionalSkipCatalog(string Sha256, IReadOnlyList<ConditionalSkipContract> Contracts);
