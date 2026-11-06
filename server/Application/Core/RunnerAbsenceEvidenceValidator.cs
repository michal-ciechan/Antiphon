using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Server.Application.Dtos;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

/// <summary>How the transport authenticated the certificate bytes. Only Verified can prove.</summary>
public enum RunnerAbsenceAuthentication { Verified, Missing, Mismatch }

/// <summary>
/// What the caller asked for. <paramref name="RunnerStoreId"/> is the session's bound store, or for
/// a local session with no bound store the runner's advertised store (A-7); never empty.
/// </summary>
public sealed record RunnerAbsenceExpectation(
    Guid SessionId, DateTime AcceptedStartedAt, Guid RunnerStoreId, string RequestNonce);

/// <summary>
/// CARD-1153 D-4: the one structural validator every transport uses. A whitelist: the result is
/// Proven only for an authenticated, version 1, exact-member-set certificate whose every positive
/// fact is present and true/false as required, for the exact session, generation
/// (<see cref="SessionGeneration.Equal"/>), store and nonce, a nonempty epoch, received within
/// five seconds of sending and still within five seconds of receipt. Everything else is Unknown.
/// </summary>
public static class RunnerAbsenceEvidenceValidator
{
    public static readonly TimeSpan MaxElapsed = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(5);

    public static SessionRunnerAbsenceEvidenceResult Validate(
        JsonNode? body,
        RunnerAbsenceExpectation expected,
        RunnerAbsenceAuthentication authentication,
        DateTimeOffset sentAt,
        DateTimeOffset receivedAt,
        DateTimeOffset now)
    {
        if (authentication != RunnerAbsenceAuthentication.Verified)
            return Refuse("certificate not authenticated: " + authentication);
        if (receivedAt < sentAt || receivedAt - sentAt > MaxElapsed)
            return Refuse("request elapsed beyond the five-second bound");
        if (now < receivedAt || now - receivedAt > Lifetime)
            return Refuse("certificate expired");
        if (expected.SessionId == Guid.Empty || expected.RunnerStoreId == Guid.Empty
            || !RunnerAbsenceEvidence.IsValidNonce(expected.RequestNonce))
            return Refuse("expectation incomplete");
        if (body is not JsonObject certificate)
            return Refuse("certificate is not an object");

        var members = certificate.Select(p => p.Key).ToList();
        if (members.Count != RunnerAbsenceEvidence.CertificateMembers.Count
            || members.Distinct(StringComparer.Ordinal).Count() != members.Count
            || !members.All(m => RunnerAbsenceEvidence.CertificateMembers.Contains(m, StringComparer.Ordinal)))
            return Refuse("certificate member set is not version 1");

        if (!TryInt(certificate["version"], out var version) || version != RunnerAbsenceEvidence.Version)
            return Refuse("version");
        if (!TryString(certificate["outcome"], out var outcome) || outcome != RunnerAbsenceEvidence.NeverCreated)
            return Refuse("outcome");
        if (!TryGuid(certificate["sessionId"], out var sessionId) || sessionId == Guid.Empty || sessionId != expected.SessionId)
            return Refuse("sessionId");
        if (!TryStamp(certificate["acceptedStartedAt"], out var generation)
            || !SessionGeneration.Equal(generation, expected.AcceptedStartedAt))
            return Refuse("acceptedStartedAt");
        if (!TryGuid(certificate["runnerStoreId"], out var store) || store == Guid.Empty || store != expected.RunnerStoreId)
            return Refuse("runnerStoreId");
        if (!TryGuid(certificate["runtimeEpoch"], out var epoch) || epoch == Guid.Empty)
            return Refuse("runtimeEpoch");
        if (!TryString(certificate["requestNonce"], out var nonce) || nonce != expected.RequestNonce)
            return Refuse("requestNonce");
        if (!IsBool(certificate["complete"], true)) return Refuse("complete");
        if (!IsBool(certificate["creationObserved"], false)) return Refuse("creationObserved");
        if (!IsBool(certificate["nativeTranscriptPresent"], false)) return Refuse("nativeTranscriptPresent");
        if (!IsBool(certificate["sidecarTranscriptPresent"], false)) return Refuse("sidecarTranscriptPresent");
        if (!IsBool(certificate["processPresent"], false)) return Refuse("processPresent");
        if (!IsBool(certificate["identityClosed"], true)) return Refuse("identityClosed");
        // Audit metadata only: never compared against a clock.
        if (!TryStamp(certificate["observedAtUtc"], out var observed))
            return Refuse("observedAtUtc");

        return new(SessionRunnerAbsenceEvidenceKind.Proven,
            new SessionRunnerAbsenceEvidence(sessionId, SessionGeneration.Normalize(generation), store, epoch,
                receivedAt, receivedAt + Lifetime, observed),
            "never_created");
    }

    private static SessionRunnerAbsenceEvidenceResult Refuse(string reason) =>
        SessionRunnerAbsenceEvidenceResult.Unknown("absence certificate refused: " + reason);

    private static bool TryInt(JsonNode? node, out int value)
    {
        value = 0;
        return node is JsonValue v && v.GetValueKind() == JsonValueKind.Number && v.TryGetValue(out value);
    }

    private static bool TryString(JsonNode? node, out string value)
    {
        value = "";
        if (node is not JsonValue v || v.GetValueKind() != JsonValueKind.String || !v.TryGetValue<string>(out var s))
            return false;
        value = s;
        return true;
    }

    private static bool TryGuid(JsonNode? node, out Guid value)
    {
        value = Guid.Empty;
        return TryString(node, out var s) && Guid.TryParseExact(s, "D", out value);
    }

    private static bool TryStamp(JsonNode? node, out DateTime value)
    {
        value = default;
        return TryString(node, out var s)
            && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value)
            && value.Kind == DateTimeKind.Utc;
    }

    private static bool IsBool(JsonNode? node, bool required) =>
        node is JsonValue v
        && v.GetValueKind() is JsonValueKind.True or JsonValueKind.False
        && v.GetValue<bool>() == required;
}
