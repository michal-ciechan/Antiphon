using Antiphon.Checkpoints;

namespace Antiphon.Tests.TestHelpers;

/// <summary>Test-only accounting for CARD-0418. A local report cannot certify a live receipt.</summary>
internal static class ChannelOutboundEvidenceAccounting
{
    // These are assertion keys, not broad V/R labels. A single green method under
    // "V-16", for example, cannot certify the independent stamp, retry, source
    // completeness, and multi-target assertions in the plan.
    internal static IReadOnlyCollection<string> RequiredOrdinaryAssertions { get; } =
    [
        "V-1/default-sources", "V-1/eligibility-matrix", "V-1/legacy-config", "V-1/no-renderer",
        "V-2/read-path-bytes", "V-2/inline-zip-thresholds", "V-2/all-members-budget",
        "V-3/implicit-manifest-only", "V-3/legacy-history", "V-3/explicit-pdf-once", "V-3/excluded-paths",
        "V-4/four-source-note", "V-4/x-y-z-outcomes", "V-4/complete-zip-worker-input",
        "V-5/policy-defaults", "V-5/validation-boundaries", "V-5/atomic-patch", "V-5/client-round-trip",
        "V-6/three-send-shapes", "V-6/trigger-matrix", "V-6/passthrough-control",
        "V-7/withheld-replies", "V-7/control-callers", "V-7/healthy-companions",
        "V-8/admission-commit", "V-8/runtime-progress", "V-8/fresh-connection-stamps",
        "V-9/concurrent-identity", "V-9/distinct-targets-and-windows", "V-9/lease-takeover-fencing",
        "V-10/internal-purpose", "V-10/ordinary-companions", "V-10/capability-and-env-sentinels",
        "V-11/refusal-matrix", "V-11/capacity-and-deadline", "V-11/working-owner-race",
        "V-12/generic-output", "V-12/sealed-byte-survival", "V-12/delivery-isolation",
        "V-13/invalid-output-matrix", "V-13/zip-and-link-escape", "V-13/storage-faults",
        "V-14/frozen-route-and-body", "V-14/order-and-control-bypass", "V-14/held-head-resolution",
        "V-15/cuts-c1-c8", "V-15/fresh-connection-oracles", "V-15/task-and-process-ownership",
        "V-16/producer-retries", "V-16/atomic-publication-stamps", "V-16/source-completeness", "V-16/multi-target",
        "V-17/pending-ttl", "V-17/beyond-budget", "V-17/truthful-attention",
        "V-18/revalidate-boundaries", "V-18/pinned-prompt-revision", "V-18/final-claim-race",
        "V-19/renderer-contract", "V-19/failure-cleanup", "V-19/standalone-tool",
        "V-20/parsed-pdf", "V-20/visual-pages", "V-20/source-hashes",
        "V-21/private-migration", "V-21/fresh-di", "V-21/dependency-and-instruction-boundary",
        "V-22/wire-adapters", "V-22/configuration", "V-22/monitor-unknown-and-positive",
        "V-23/serialized-boundaries", "V-23/fallback-and-terminal-overcap", "V-23/near-default-broker-and-key",
        "V-24/isolated-end-to-end", "V-24/controls-and-restart",
        "R-1/default-source-and-no-render", "R-1/completion-note",
        "R-2/source-bytes-and-members", "R-2/manifest-custody", "R-2/publication-completeness",
        "R-3/explicit-policy", "R-3/pinned-identity-and-client",
        "R-4/send-shapes", "R-4/withheld-and-control",
        "R-5/durable-runtime-release", "R-5/no-early-stamps",
        "R-6/concurrent-identity", "R-6/restart-and-targets",
        "R-7/purpose-and-refusals", "R-7/deadline-and-owner",
        "R-8/authorized-inputs", "R-8/sealed-output-and-route",
        "R-9/serialized-cap", "R-9/original-fallback",
        "R-10/frozen-thread", "R-10/ordered-outcomes",
        "R-11/ttl-ownership", "R-11/attention-states",
        "R-12/standalone-tool", "R-12/readable-pdf-and-server-boundary",
        "R-13/transport-and-monitor", "R-13/near-default-broker-and-budget",
        "R-14/per-assertion-actual-run-index", "R-14/no-broad-id-claims",
    ];

    internal static IReadOnlyCollection<string> RequiredControlIds { get; } =
        Enumerable.Range(1, 30).Select(n => $"PC-{n}").ToArray();

    internal sealed record Ordinary(string Assertion, string Method, string Trx);
    internal sealed record Control(string Id, string Variant, string Method,
        string BaselineTrx, string RedTrx, string RestoredTrx, string ExpectedAssertion);
    internal sealed record LiveReceipt(string Installation, string Destination,
        string NativeMessageId, string NativeFileId, string DownloadPath, string DownloadSha256,
        bool IsActualInstallation);

    internal static IReadOnlyList<string> ValidateOrdinary(
        IReadOnlyCollection<string> requiredAssertions, IReadOnlyCollection<Ordinary> rows)
    {
        var errors = new List<string>();
        foreach (var assertion in requiredAssertions)
        {
            if (!IsAssertionKey(assertion))
            {
                errors.Add($"{assertion}: broad or invalid V/R assertion key");
                continue;
            }
            var claims = rows.Where(row => row.Assertion == assertion).ToArray();
            if (claims.Length == 0)
            {
                errors.Add($"{assertion}: no execution evidence");
                continue;
            }
            foreach (var claim in claims)
                RequireGreen(claim.Trx, claim.Method, $"{assertion}: ordinary", errors);
        }
        foreach (var claim in rows.Where(row => !IsAssertionKey(row.Assertion)
                     || !requiredAssertions.Contains(row.Assertion)))
            errors.Add($"{claim.Assertion}: broad, invalid or unrequired V/R claim");
        foreach (var duplicate in rows.GroupBy(row => (row.Assertion, row.Method))
                     .Where(group => group.Count() > 1))
            errors.Add($"{duplicate.Key.Assertion}: duplicate method claim {duplicate.Key.Method}");
        return errors;
    }

    internal static IReadOnlyList<string> ValidateFullOrdinary(IReadOnlyCollection<Ordinary> rows) =>
        ValidateOrdinary(RequiredOrdinaryAssertions, rows);

    private static bool IsAssertionKey(string value)
    {
        var slash = value.IndexOf('/');
        if (slash < 3 || slash == value.Length - 1 || value.IndexOf('/', slash + 1) >= 0)
            return false;
        var id = value[..slash];
        if (!(id.StartsWith("V-", StringComparison.Ordinal)
              || id.StartsWith("R-", StringComparison.Ordinal))
            || !int.TryParse(id.AsSpan(2), out var number) || number < 1)
            return false;
        return value.AsSpan(slash + 1).IndexOfAnyExcept("abcdefghijklmnopqrstuvwxyz0123456789-") < 0;
    }

    internal static IReadOnlyList<string> ValidateControls(
        IReadOnlyCollection<(string Id, string Variant)> requiredVariants,
        IReadOnlyCollection<Control> rows)
    {
        var errors = new List<string>();
        foreach (var required in requiredVariants)
        {
            var claims = rows.Where(row => row.Id == required.Id && row.Variant == required.Variant).ToArray();
            if (claims.Length != 1)
            {
                errors.Add($"{required.Id}/{required.Variant}: expected one control, found {claims.Length}");
                continue;
            }
            var claim = claims[0];
            RequireGreen(claim.BaselineTrx, claim.Method, $"{claim.Id}/{claim.Variant}: baseline", errors);
            var red = TrxReport.Parse(claim.RedTrx);
            if (!red.Ok || red.Executed == 0 || red.Failed == 0
                || !red.Failures.Any(f => f.Name == claim.Method
                    && !string.IsNullOrWhiteSpace(f.StackTrace)
                    && f.Message.Contains(claim.ExpectedAssertion, StringComparison.Ordinal)))
                errors.Add($"{claim.Id}/{claim.Variant}: missing named assertion-red result");
            RequireGreen(claim.RestoredTrx, claim.Method,
                $"{claim.Id}/{claim.Variant}: restored", errors);
            if (claim.BaselineTrx == claim.RedTrx || claim.RedTrx == claim.RestoredTrx
                || claim.BaselineTrx == claim.RestoredTrx)
                errors.Add($"{claim.Id}/{claim.Variant}: phases reuse one TRX");
        }
        return errors;
    }

    internal static IReadOnlyList<string> ValidateLiveReceipt(LiveReceipt? receipt)
    {
        if (receipt is null)
            return ["V-25: native receipt absent"];
        var errors = new List<string>();
        if (!receipt.IsActualInstallation || string.IsNullOrWhiteSpace(receipt.Installation)
            || string.IsNullOrWhiteSpace(receipt.Destination)
            || string.IsNullOrWhiteSpace(receipt.NativeMessageId)
            || string.IsNullOrWhiteSpace(receipt.NativeFileId))
            errors.Add("V-25: actual installation, destination, and native message/file ids required");
        if (!File.Exists(receipt.DownloadPath)
            || !string.Equals(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                File.Exists(receipt.DownloadPath) ? File.ReadAllBytes(receipt.DownloadPath) : [])),
                receipt.DownloadSha256, StringComparison.OrdinalIgnoreCase))
            errors.Add("V-25: downloaded artifact and its hash required");
        // These fields make the claim reviewable; only inspection of the actual
        // destination and native receipt can close the live gate.
        errors.Add("V-25: independent native destination review required");
        return errors;
    }

    private static void RequireGreen(string path, string method, string label, List<string> errors)
    {
        var result = TrxReport.Parse(path);
        if (!result.Ok || result.Executed == 0 || result.Failed != 0 || result.Skipped != 0
            || !result.ExecutedNames.Contains(method, StringComparer.Ordinal))
            errors.Add($"{label}: named non-skipped green execution required");
    }
}
