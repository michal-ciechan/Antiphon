using Antiphon.Checkpoints;

namespace Antiphon.Tests.TestHelpers;

/// <summary>Test-only accounting for CARD-0418. A local report cannot certify a live receipt.</summary>
internal static class ChannelOutboundEvidenceAccounting
{
    internal static IReadOnlyCollection<string> RequiredOrdinaryIds { get; } =
        Enumerable.Range(1, 24).Select(n => $"V-{n}")
            .Concat(Enumerable.Range(1, 14).Select(n => $"R-{n}")).ToArray();

    internal static IReadOnlyCollection<string> RequiredControlIds { get; } =
        Enumerable.Range(1, 30).Select(n => $"PC-{n}").ToArray();

    internal sealed record Ordinary(string Id, string Method, string Trx);
    internal sealed record Control(string Id, string Variant, string Method,
        string BaselineTrx, string RedTrx, string RestoredTrx, string ExpectedAssertion);
    internal sealed record LiveReceipt(string Installation, string Destination,
        string NativeMessageId, string NativeFileId, string DownloadPath, string DownloadSha256,
        bool IsActualInstallation);

    internal static IReadOnlyList<string> ValidateOrdinary(
        IReadOnlyCollection<string> requiredIds, IReadOnlyCollection<Ordinary> rows)
    {
        var errors = new List<string>();
        foreach (var id in requiredIds)
        {
            var claims = rows.Where(row => row.Id == id).ToArray();
            if (claims.Length == 0)
            {
                errors.Add($"{id}: no execution evidence");
                continue;
            }
            foreach (var claim in claims)
                RequireGreen(claim.Trx, claim.Method, $"{id}: ordinary", errors);
        }
        foreach (var duplicate in rows.GroupBy(row => (row.Id, row.Method))
                     .Where(group => group.Count() > 1))
            errors.Add($"{duplicate.Key.Id}: duplicate method claim {duplicate.Key.Method}");
        return errors;
    }

    internal static IReadOnlyList<string> ValidateFullOrdinary(IReadOnlyCollection<Ordinary> rows) =>
        ValidateOrdinary(RequiredOrdinaryIds, rows);

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
