namespace Antiphon.Card0490.NativeHarness;

public sealed record EvidenceIdentity(string O, string L, string Task, string Creation, string Pc, string Phase, string Run, string Source, string Method, string Nonce);

public sealed record Frame(int DeclaredLength, int ActualLength, string DeclaredDigest, string ActualDigest, bool Final);

public sealed record RunResult(
    bool Accepted,
    EvidenceIdentity Identity,
    IReadOnlyList<string> ActualMethods,
    int ExecutedCases,
    IReadOnlyList<string> ExpectedCases,
    bool AnySkipped,
    string? FailureKind,
    bool WriteSucceeded,
    bool ReadbackMatches,
    bool Canceled,
    string EvidenceKind);

public static class NativeEvidencePolicy
{
    public static bool Accept(RunResult result, EvidenceIdentity expected, string prescribedAssertion)
    {
        if (result.Canceled) return false;
        if (result.EvidenceKind == "FixtureProbe") return false;
        if (!IdentitiesEqual(result.Identity, expected)) return false;
        if (result.ActualMethods.Count != 1 || result.ActualMethods[0] != expected.Method) return false;
        if (result.ExecutedCases <= 0) return false;
        if (result.ExpectedCases.Except(result.ActualMethods).Any() && result.ExpectedCases.Count > result.ActualMethods.Count)
            return false;
        if (result.AnySkipped) return false;
        if (result.FailureKind is not null && result.FailureKind != prescribedAssertion) return false;
        if (!result.WriteSucceeded || !result.ReadbackMatches) return false;
        return result.Accepted;
    }

    public static bool AcceptFrame(Frame frame) =>
        frame.DeclaredLength == frame.ActualLength
        && string.Equals(frame.DeclaredDigest, frame.ActualDigest, StringComparison.Ordinal)
        && frame.DeclaredLength > 0;

    public static bool IdentitiesEqual(EvidenceIdentity left, EvidenceIdentity right) => left == right;
}
