using System.Globalization;
using System.Text.Json.Nodes;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Tests.Agents;

/// <summary>
/// CARD-1153 fixture: the version 1 never-created certificate as JSON, and one-member flips
/// for the validator and transport tables (V-12, V-22). The member set below is the closed
/// whitelist; a member missing from a pristine shape, or an extra member, is a refusal.
/// Signing is applied by Code with the production signer over the final bytes; this class
/// never signs, so a shape alone can never be mistaken for an authenticated certificate.
/// </summary>
internal static class AbsenceCertificateShape
{
    public static readonly IReadOnlyList<string> Members =
    [
        "version", "outcome", "sessionId", "acceptedStartedAt", "runnerStoreId", "runtimeEpoch",
        "requestNonce", "complete", "creationObserved", "nativeTranscriptPresent",
        "sidecarTranscriptPresent", "processPresent", "identityClosed", "observedAtUtc",
    ];

    public const string NeverCreated = "never_created";

    public static JsonObject Pristine(Guid sessionId, DateTime generation, Guid store, Guid epoch, string nonce, DateTime observedAtUtc) =>
        new()
        {
            ["version"] = 1,
            ["outcome"] = NeverCreated,
            ["sessionId"] = sessionId.ToString("D"),
            ["acceptedStartedAt"] = Stamp(SessionGeneration.Normalize(generation)),
            ["runnerStoreId"] = store.ToString("D"),
            ["runtimeEpoch"] = epoch.ToString("D"),
            ["requestNonce"] = nonce,
            ["complete"] = true,
            ["creationObserved"] = false,
            ["nativeTranscriptPresent"] = false,
            ["sidecarTranscriptPresent"] = false,
            ["processPresent"] = false,
            ["identityClosed"] = true,
            ["observedAtUtc"] = Stamp(observedAtUtc),
        };

    /// <summary>
    /// Returns a copy with exactly one condition changed. Transport conditions (elapsed, age,
    /// authentication) are not members and are driven by the test, not by this shape.
    /// </summary>
    public static JsonObject Flip(JsonObject pristine, string condition)
    {
        var shape = (JsonObject)pristine.DeepClone();
        var generation = DateTime.Parse(shape["acceptedStartedAt"]!.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        switch (condition)
        {
            case "version-missing": shape.Remove("version"); break;
            case "version-0": shape["version"] = 0; break;
            case "version-2": shape["version"] = 2; break;
            case "outcome-missing": shape.Remove("outcome"); break;
            case "outcome-other": shape["outcome"] = "created"; break;
            case "sessionId-missing": shape.Remove("sessionId"); break;
            case "sessionId-mismatch": shape["sessionId"] = Guid.NewGuid().ToString("D"); break;
            case "sessionId-empty": shape["sessionId"] = Guid.Empty.ToString("D"); break;
            case "generation-missing": shape.Remove("acceptedStartedAt"); break;
            case "generation-one-microsecond": shape["acceptedStartedAt"] = Stamp(generation.AddTicks(SessionGeneration.MicrosecondTicks)); break;
            case "generation-sub-microsecond": shape["acceptedStartedAt"] = Stamp(generation.AddTicks(1)); break;
            case "store-missing": shape.Remove("runnerStoreId"); break;
            case "store-mismatch": shape["runnerStoreId"] = Guid.NewGuid().ToString("D"); break;
            case "store-empty": shape["runnerStoreId"] = Guid.Empty.ToString("D"); break;
            case "epoch-missing": shape.Remove("runtimeEpoch"); break;
            case "epoch-empty": shape["runtimeEpoch"] = ""; break;
            case "nonce-missing": shape.Remove("requestNonce"); break;
            case "nonce-mismatch": shape["requestNonce"] = Convert.ToBase64String(new byte[32]); break;
            case "complete-missing": shape.Remove("complete"); break;
            case "complete-false": shape["complete"] = false; break;
            case "creationObserved-missing": shape.Remove("creationObserved"); break;
            case "creationObserved-true": shape["creationObserved"] = true; break;
            case "native-missing": shape.Remove("nativeTranscriptPresent"); break;
            case "native-true": shape["nativeTranscriptPresent"] = true; break;
            case "sidecar-missing": shape.Remove("sidecarTranscriptPresent"); break;
            case "sidecar-true": shape["sidecarTranscriptPresent"] = true; break;
            case "process-missing": shape.Remove("processPresent"); break;
            case "process-true": shape["processPresent"] = true; break;
            case "identityClosed-missing": shape.Remove("identityClosed"); break;
            case "identityClosed-false": shape["identityClosed"] = false; break;
            case "unknown-extra-member": shape["retained"] = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(condition), condition, "not a member flip");
        }

        return shape;
    }

    private static string Stamp(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);
}
