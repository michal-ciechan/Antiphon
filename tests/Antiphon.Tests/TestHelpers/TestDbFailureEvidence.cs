namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Profile-413 (rc run f2de5b30): a transient Npgsql failure reaches a test only as EF's
/// "likely due to a transient failure" wrapper, and the TRX keeps no inner exception. Every
/// context built from <see cref="TestDbFixtureLifecycle.BuildOptions(string)"/> writes such a
/// failure's whole exception chain to the test's output, so the next occurrence is attributable.
/// </summary>
internal static class TestDbFailureEvidence
{
    public const string Tag = "[db-failure]";

    private static readonly AsyncLocal<TextWriter?> Override = new();

    /// <summary>The test's own output unless a witness redirects it for its async flow.</summary>
    public static TextWriter Out
    {
        get => Override.Value ?? Console.Out;
        set => Override.Value = value;
    }
}
