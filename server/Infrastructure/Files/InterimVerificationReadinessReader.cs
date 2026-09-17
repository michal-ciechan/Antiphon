using System.Globalization;
using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Files;

/// <summary>
/// CARD-0544 D-7. Reads two deployment-owned files under the configured state root and fails
/// closed on anything short of a complete, matching, fresh pair:
/// <list type="bullet">
/// <item><c>interim-qualification-receipt.json</c> — the operator attestation S5 (CARD-0545)
/// publishes after acceptance. It cites the committed qualification artifact, the qualified
/// repository/project, policy/script hashes, the accepted manual and scheduled runs, and the
/// recipient plus outage-recovery evidence identities. A filename alone is never evidence.</item>
/// <item><c>last-monitor.json</c> — the nightly health monitor's saved verdict. Fresh only when its
/// <c>RecordedAt</c> is 0..<see cref="InterimVerificationSettings.MonitorFreshMinutes"/> old; it must
/// report Healthy and ReadyForDeferral as real booleans and carry identities matching the receipt.</item>
/// </list>
/// No network call, no Windmill access and no caller-supplied value participates.
/// </summary>
public sealed class InterimVerificationReadinessReader(
    IOptions<InterimVerificationSettings> settings, TimeProvider clock) : IInterimVerificationReadinessReader
{
    public const string ReceiptFileName = "interim-qualification-receipt.json";
    public const string MonitorFileName = "last-monitor.json";

    public async Task<InterimReadiness> ReadAsync(string? repositoryPath, Guid? projectId, CancellationToken ct)
    {
        var options = settings.Value;
        if (!options.Enabled)
            return InterimReadiness.Unready("interim_verification_disabled");
        if (string.IsNullOrWhiteSpace(options.CanonicalRepositoryPath) || string.IsNullOrWhiteSpace(options.StateRoot))
            return InterimReadiness.Unready("readiness_unconfigured");
        if (!SamePath(options.CanonicalRepositoryPath, repositoryPath))
            return InterimReadiness.Unready("readiness_repository_unqualified");
        if (options.ProjectId != projectId)
            return InterimReadiness.Unready("readiness_project_unqualified");

        var receiptRead = await ReadBoundedAsync(Path.Combine(options.StateRoot, ReceiptFileName), options.MaxFileBytes, ct);
        if (receiptRead.Error is not null)
            return InterimReadiness.Unready("qualification_receipt_" + receiptRead.Error);
        var monitorRead = await ReadBoundedAsync(Path.Combine(options.StateRoot, MonitorFileName), options.MaxFileBytes, ct);
        if (monitorRead.Error is not null)
            return InterimReadiness.Unready("monitor_" + monitorRead.Error);

        using (receiptRead.Document)
        using (monitorRead.Document)
        {
            var receipt = receiptRead.Document!.RootElement;
            var monitor = monitorRead.Document!.RootElement;
            if (receipt.ValueKind != JsonValueKind.Object || monitor.ValueKind != JsonValueKind.Object)
                return InterimReadiness.Unready("readiness_malformed");

            if (Int(receipt, "schemaVersion") != 1)
                return InterimReadiness.Unready("qualification_receipt_schema_unsupported");
            if (!SamePath(Text(receipt, "repositoryPath"), options.CanonicalRepositoryPath))
                return InterimReadiness.Unready("qualification_repository_mismatch");
            if (!TryGuidOrNull(receipt, "projectId", out var receiptProject) || receiptProject != options.ProjectId)
                return InterimReadiness.Unready("qualification_project_mismatch");

            var artifactPath = Text(receipt, "qualificationArtifactPath");
            var artifactCommit = Text(receipt, "qualificationArtifactCommitSha");
            if (string.IsNullOrWhiteSpace(artifactPath))
                return InterimReadiness.Unready("qualification_artifact_missing");
            if (!GitObjectId.IsFull(artifactCommit))
                return InterimReadiness.Unready("qualification_revision_invalid");

            var policyHash = Text(receipt, "policyHash");
            var scriptHash = Text(receipt, "scriptHash");
            var manualRun = Text(receipt, "manualRunId");
            var scheduledRun = Text(receipt, "scheduledRunId");
            var scheduledJob = Text(receipt, "scheduledJobId");
            var recipients = TextArray(receipt, "recipientEvidenceIds");
            var outage = TextArray(receipt, "outageRecoveryEvidenceIds");
            if (string.IsNullOrWhiteSpace(policyHash) || string.IsNullOrWhiteSpace(scriptHash))
                return InterimReadiness.Unready("qualification_hashes_missing");
            if (string.IsNullOrWhiteSpace(manualRun) || string.IsNullOrWhiteSpace(scheduledRun)
                || string.IsNullOrWhiteSpace(scheduledJob))
                return InterimReadiness.Unready("qualification_runs_missing");
            if (recipients is not { Count: > 0 })
                return InterimReadiness.Unready("qualification_recipient_evidence_missing");
            if (outage is not { Count: > 0 })
                return InterimReadiness.Unready("qualification_outage_evidence_missing");

            // Monitor verdict: real booleans only, never a string or number that merely looks true.
            if (!Object(monitor, "Health", out var health)
                || !Bool(health, "Healthy", out var healthy) || !Bool(health, "ReadyForDeferral", out var ready))
                return InterimReadiness.Unready("monitor_malformed");
            var recordedText = Text(monitor, "RecordedAt");
            if (recordedText is null || !DateTimeOffset.TryParse(recordedText, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var recorded))
                return InterimReadiness.Unready("monitor_timestamp_invalid");

            var now = clock.GetUtcNow();
            var age = now - recorded.ToUniversalTime();
            if (age < TimeSpan.Zero)
                return InterimReadiness.Unready("monitor_future");
            if (age > TimeSpan.FromMinutes(options.MonitorFreshMinutes))
                return InterimReadiness.Unready("monitor_stale");
            if (!healthy)
                return InterimReadiness.Unready("monitor_unhealthy");
            if (!ready)
                return InterimReadiness.Unready("monitor_not_ready_for_deferral");

            if (!Object(monitor, "Identity", out var identity))
                return InterimReadiness.Unready("monitor_identity_missing");
            if (!SamePath(Text(identity, "RepositoryPath"), options.CanonicalRepositoryPath))
                return InterimReadiness.Unready("monitor_repository_mismatch");
            if (!TryGuidOrNull(identity, "ProjectId", out var monitorProject) || monitorProject != options.ProjectId)
                return InterimReadiness.Unready("monitor_project_mismatch");
            if (!string.Equals(Text(identity, "PolicyHash"), policyHash, StringComparison.Ordinal))
                return InterimReadiness.Unready("monitor_policy_hash_mismatch");
            if (!string.Equals(Text(identity, "ScriptHash"), scriptHash, StringComparison.Ordinal))
                return InterimReadiness.Unready("monitor_script_hash_mismatch");
            var runId = Text(identity, "ScheduledRunId");
            var jobId = Text(identity, "WindmillJobId");
            var jobRunId = Text(identity, "JobNativeRunId");
            if (string.IsNullOrWhiteSpace(runId) || string.IsNullOrWhiteSpace(jobId)
                || !string.Equals(runId, jobRunId, StringComparison.Ordinal))
                return InterimReadiness.Unready("monitor_run_identity_mismatch");

            return new InterimReadiness(true, "ready", new InterimReadinessSnapshot(
                artifactPath!, artifactCommit!, policyHash!, scriptHash!, runId!, jobId!, recipients!,
                recorded.UtcDateTime, now.UtcDateTime));
        }
    }

    private readonly record struct BoundedRead(JsonDocument? Document, string? Error);

    private static async Task<BoundedRead> ReadBoundedAsync(string path, int maxBytes, CancellationToken ct)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
                FileOptions.Asynchronous);
            if (stream.Length > maxBytes)
                return new(null, "oversized");
            var buffer = new byte[stream.Length];
            var read = 0;
            while (read < buffer.Length)
            {
                var n = await stream.ReadAsync(buffer.AsMemory(read), ct);
                if (n == 0) break;
                read += n;
            }
            if (read == 0)
                return new(null, "empty");
            return new(JsonDocument.Parse(buffer.AsMemory(0, read)), null);
        }
        catch (FileNotFoundException) { return new(null, "missing"); }
        catch (DirectoryNotFoundException) { return new(null, "missing"); }
        catch (UnauthorizedAccessException) { return new(null, "unreadable"); }
        catch (IOException) { return new(null, "unreadable"); }
        catch (JsonException) { return new(null, "malformed"); }
    }

    private static bool Property(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static string? Text(JsonElement element, string name) =>
        Property(element, name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Int(JsonElement element, string name) =>
        Property(element, name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n)
            ? n : null;

    private static bool Bool(JsonElement element, string name, out bool result)
    {
        result = false;
        if (!Property(element, name, out var value)) return false;
        if (value.ValueKind == JsonValueKind.True) { result = true; return true; }
        return value.ValueKind == JsonValueKind.False;
    }

    private static bool Object(JsonElement element, string name, out JsonElement value) =>
        Property(element, name, out value) && value.ValueKind == JsonValueKind.Object;

    private static List<string>? TextArray(JsonElement element, string name)
    {
        if (!Property(element, name, out var value) || value.ValueKind != JsonValueKind.Array) return null;
        var items = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString())) return null;
            items.Add(item.GetString()!);
        }
        return items;
    }

    private static bool TryGuidOrNull(JsonElement element, string name, out Guid? result)
    {
        result = null;
        if (!Property(element, name, out var value) || value.ValueKind == JsonValueKind.Null) return true;
        if (value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var parsed))
        {
            result = parsed;
            return true;
        }
        return false;
    }

    internal static bool SamePath(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
