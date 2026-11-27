using System.Security.Cryptography;
using System.Text;

namespace Antiphon.SessionRunner.Contracts;

/// <summary>
/// CARD-0514 D-8: generation-and-sequence-conditional maintenance write.
/// Distinct from unguarded <c>POST /sessions/{id}/input</c>. Older runners that lack
/// <see cref="RunnerCapabilityFeatures.ConditionalMaintenanceInputV1"/> must never receive
/// a fallback raw input from automatic RC.
/// </summary>
public static class ConditionalInputOutcomes
{
    public const string Written = "written";
    public const string StaleObservation = "stale-observation";
    public const string GenerationMismatch = "generation-mismatch";
    public const string Missing = "missing";
    public const string Exited = "exited";
    public const string Unsupported = "unsupported";
    public const string Unknown = "unknown";
    public const string ModalBlocked = "modal-blocked";
    public const string ObservationUnknown = "observation-unknown";
    public const string ComposerMismatch = "composer-mismatch";
}

public sealed record RunnerConditionalInputRequest(
    DateTime ExpectedAcceptedStartedAt,
    long ExpectedLastSequence,
    string Input,
    bool RequireClaudeDialogClear = false,
    long? ExpectedInputRevision = null,
    Guid? ExpectedRuntimeEpoch = null,
    ClaudeDialogInputIntent DialogIntent = ClaudeDialogInputIntent.None,
    string? ExpectedComposerDigest = null,
    bool RequireOwnedComposer = false);

public static class ComposerInput
{
    /// <summary>A digest of the exact LF-encoded composer text, without normalization.</summary>
    public static string Digest(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}

// Finite internal maintenance actions; none can carry arbitrary task text into a dialog.
public enum ClaudeDialogInputIntent { None, StartupTrust, StartupEffortKeep, StartupEffortSwitch, UnsafeDeleteNo, RemoteControlDismiss }

public sealed record RunnerConditionalInputResult(
    Guid SessionId,
    string Outcome,
    DateTime? AcceptedStartedAt = null,
    long? LastSequence = null);
