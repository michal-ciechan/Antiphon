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

public enum WorkspaceParkSourceMode { Published, NoSourceChanges }

/// <summary>
/// Source evidence only, not permission to stop a session. Verify again under the runtime's
/// input/generation reservation immediately before release. No source is auto-committed.
/// </summary>
public sealed record WorkspaceParkReceipt(
    Guid ReceiptId, WorkspaceParkRequest Request, string SourceSha, string? RemoteSha,
    string? RemoteBeforeSha, bool Clean, bool DescendsFromBaseline, DateTimeOffset ObservedAt,
    WorkspaceParkSourceMode SourceMode = WorkspaceParkSourceMode.Published)
{
    public bool HasConsistentSourceMode => SourceMode switch
    {
        WorkspaceParkSourceMode.Published => RemoteSha is not null,
        WorkspaceParkSourceMode.NoSourceChanges => RemoteSha is null,
        _ => false
    };
}

public sealed record WorkspaceParkResult(
    WorkspaceParkOutcome Outcome, string Reason, WorkspaceParkReceipt? Receipt = null);

public sealed record WorkspaceRepositoryIdentityRequest(
    Guid SessionId, string Path, Guid ExpectedRunnerStoreId, DateTime ExpectedAcceptedStartedAt, int Version = 1)
{
    public static bool Supported(RunnerCapabilitiesDto? capabilities) =>
        capabilities?.Features?.Contains(RunnerCapabilityFeatures.WorkspaceRepositoryIdentityV1) == true
        && capabilities.Features.Contains(RunnerCapabilityFeatures.TerminalSeatReleaseV1);
}

public enum WorkspaceRepositoryIdentityOutcome { Read, Held, Unknown }

/// <summary>EndpointRepository is normalized, credential-free repository identity, never a raw URL.</summary>
public sealed record WorkspaceRepositoryIdentity(
    string RepositoryIdentity, string EndpointFingerprint, string EndpointRepository,
    string HeadSha, string FullRef, Guid RunnerStoreId, DateTime AcceptedStartedAt);

public sealed record WorkspaceRepositoryIdentityResult(
    WorkspaceRepositoryIdentityOutcome Outcome, string Reason, WorkspaceRepositoryIdentity? Identity = null);
