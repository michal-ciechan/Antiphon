namespace Antiphon.FakeLlmApi;

/// <summary>One HTTP request observed by the stub. Memory is the assertion contract.</summary>
public sealed record RecordedRequest(
    long Seq,
    DateTimeOffset UtcTimestamp,
    string Method,
    string Path,
    string QueryString,
    IReadOnlyDictionary<string, string[]> Headers,
    string Body,
    int BodyByteLength,
    int ListenPort);
