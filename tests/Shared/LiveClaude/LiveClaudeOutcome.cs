namespace Antiphon.TestSupport.LiveClaude;

/// <summary>S1c rows in the shape master's S1b classifier reads. Skip text is the setup name only.</summary>
internal static class LiveClaudeOutcome
{
    public const string Uid = "s1c.outcome.0";

    public static string? SetupStatus(string reason) => reason switch
    {
        "premise-absent" => "StartupDefaultAbsent",
        "bridge-unestablished" or "bridge-unobserved" => "BridgeNotEstablished",
        "trust-uncleared" => "TrustNotCleared",
        "tui-unready" or "settings-unobserved" => "ReadyTimeout",
        _ => null
    };

    public static Dictionary<string, object?> PremiseAbsent() => Row("Skipped", "StartupDefaultAbsent", null);

    public static Dictionary<string, object?> Skipped(string setup) => Row("Skipped", setup, null);

    public static Dictionary<string, object?> AssertionFailure() => Row("Failed", "Ready", null);

    public static Dictionary<string, object?> MissingSetup()
    {
        var row = Row("Failed", "Ready", null);
        row.Remove("Setup");
        return row;
    }

    public static Dictionary<string, object?> ProviderError(string jsonl)
    {
        var rows = LiveClaudeDeliveryEvidence.ReadJsonl(jsonl, new("session-s1c", "generation", "native"));
        var prompt = rows.Single(r => r.Kind == "UserPrompt");
        var error = rows.Single(r => r.Error && r.ErrorType is not null);
        if (string.IsNullOrWhiteSpace(prompt.Request) || prompt.Request != error.Request)
            throw new InvalidOperationException("C1545 outcome request link missing");
        var transcript = new object[]
        {
            Event("UserPrompt", prompt.Sequence, prompt.Text, prompt.Binding.Session, prompt.Request, null),
            Event("ApiError", error.Sequence, null, error.Binding.Session, error.Request, error.ErrorType)
        };
        var row = Row("Failed", "Ready", transcript);
        row["Prompt"] = prompt.Text;
        row["Session"] = prompt.Binding.Session;
        row["Request"] = prompt.Request;
        return row;
    }

    private static Dictionary<string, object?> Row(string state, string setup, object[]? transcript) => new(StringComparer.Ordinal)
    {
        ["Uid"] = Uid,
        ["State"] = state,
        ["Setup"] = setup,
        ["Prompt"] = "s1c assertion",
        ["Session"] = "session-s1c",
        ["Request"] = "request-s1c",
        ["Transcript"] = transcript ?? Array.Empty<object>()
    };

    private static Dictionary<string, object?> Event(string type, long sequence, string? body, string session, string? request, string? error)
        => new(StringComparer.Ordinal)
        {
            ["Type"] = type,
            ["Sequence"] = sequence,
            ["Body"] = body ?? "",
            ["Session"] = session,
            ["Request"] = request ?? "",
            ["Error"] = error ?? ""
        };
}
