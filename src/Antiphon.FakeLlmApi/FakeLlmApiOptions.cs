namespace Antiphon.FakeLlmApi;

/// <summary>Which provider surfaces the stub exposes. Any subset may be enabled.</summary>
public sealed class FakeLlmApiOptions
{
    public bool Claude { get; init; }
    public bool Grok { get; init; }
    public bool Codex { get; init; }

    /// <summary>Optional JSONL sidecar path. Truncates bodies; redacts Authorization to sha256.</summary>
    public string? JsonlPath { get; init; }
}
