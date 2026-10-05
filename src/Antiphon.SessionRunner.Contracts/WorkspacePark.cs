namespace Antiphon.SessionRunner.Contracts;

/// <summary>
/// Immutable caller coordinates for one publication intent. The runtime must independently
/// fence the session generation and idle/input state before using this evidence for release.
/// Repository and endpoint identities are SHA-256 digests, never credential-bearing URLs.
/// </summary>
public sealed record WorkspaceParkBinding(
    Guid ParkId, Guid ActionId, Guid TaskId, int Attempt, Guid BlockEventId,
    Guid AgentId, string RunnerId, Guid RunnerStoreId, Guid SessionId,
    DateTime AcceptedStartedAt, Guid TaskConcurrencyToken, string ReportDigest,
    string RepositoryIdentity, string EndpointFingerprint, string FullRef, string BaselineSha);

public sealed record WorkspaceParkRequest(string Path, WorkspaceParkBinding Binding);

public enum WorkspaceParkOutcome { Published, Held, Unknown }

/// <summary>
/// Source evidence only, not permission to stop a session. Verify again under the runtime's
/// input/generation reservation immediately before release. No source is auto-committed.
/// </summary>
public sealed record WorkspaceParkReceipt(
    Guid ReceiptId, WorkspaceParkRequest Request, string SourceSha, string RemoteSha,
    string? RemoteBeforeSha, bool Clean, bool DescendsFromBaseline, DateTimeOffset ObservedAt);

public sealed record WorkspaceParkResult(
    WorkspaceParkOutcome Outcome, string Reason, WorkspaceParkReceipt? Receipt = null);
