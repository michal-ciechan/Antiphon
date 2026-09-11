using System.Security.Cryptography;
using System.Text.Json;
using Antiphon.Server.Domain.Entities;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

public sealed class VerificationReceiptPolicy
{
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public VerificationCustodyReceipt Validate(byte[] bytes, VerificationExecutionBinding expected, VerificationHostIdentity host)
    {
        var receipt = JsonSerializer.Deserialize<VerificationCustodyReceipt>(bytes, _json)
            ?? throw new VerificationCustodyException("verification_custody_invalid_receipt");
        if (receipt.SchemaVersion != 1 || receipt.StateRevision < 3 || !receipt.OutputDrained
            || receipt.SealedAtUtc == default || receipt.SealedAtUtc.Kind != DateTimeKind.Utc
            || receipt.ObservedAtUtc.Kind != DateTimeKind.Utc || receipt.ObservedAtUtc < receipt.SealedAtUtc)
            throw new VerificationCustodyException("verification_custody_invalid_receipt");
        if (receipt.Binding != expected || receipt.Host != host || host.RunnerStoreId != expected.RunnerStoreId
            || host.RunnerStoreId == Guid.Empty || host.HostInstanceId == Guid.Empty || host.ContainerId == Guid.Empty
            || host.HostPid <= 0 || host.HostStartTimeUtc == default || host.HostStartTimeUtc.Kind != DateTimeKind.Utc)
            throw new VerificationCustodyException("verification_custody_identity_mismatch");
        var exited = receipt.Disposition == VerificationCustodyState.Exited && receipt.ActiveProcesses == 0
            && receipt.ObservationMethod == "JobObjectBasicAccountingInformation" && receipt.RootPid > 0
            && receipt.RootStartTimeUtc is { Kind: DateTimeKind.Utc };
        var noStart = receipt.Disposition == VerificationCustodyState.NeverStarted && receipt.ActiveProcesses is null
            && receipt.ObservationMethod == "sealed-before-native-start-intent" && receipt.RootPid is null && receipt.RootStartTimeUtc is null;
        if (!exited && !noStart) throw new VerificationCustodyException("verification_custody_invalid_receipt");
        return receipt;
    }

    public void ValidateImported(VerificationExecution row, VerificationExecutionBinding binding)
    {
        if (row.Id != binding.ExecutionId || row.TaskId != binding.Source.TaskId
            || row.SourceLandingOperationId != binding.Source.SourceOperationId || row.SessionId != binding.Generation.SessionId
            || row.AcceptedStartedAt != binding.Generation.AcceptedStartedAt || row.ReceiptBytes is null
            || row.ReceiptImportedAt is null || row.HostIdentityJson is null
            || row.ReceiptDigest != Convert.ToHexString(SHA256.HashData(row.ReceiptBytes)))
            throw new VerificationCustodyException("verification_custody_missing_or_changed_receipt");
        Validate(row.ReceiptBytes, binding, JsonSerializer.Deserialize<VerificationHostIdentity>(row.HostIdentityJson)
            ?? throw new VerificationCustodyException("verification_custody_identity_mismatch"));
    }
}
