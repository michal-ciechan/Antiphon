using System.Security.Cryptography;
using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

// CARD-0604 D-19 / G-30, R-13. The server's own receipt check, independent of the producer's.
//
// The property at stake is narrow and total: the observation method is DERIVED from the binding's
// backend, never accepted as whichever of the two the receipt claims. A policy that accepted
// either method for either backend would import a receipt saying "JobObjectBasicAccounting" for
// an execution that ran in a cgroup -- a document that reads as Windows job-object evidence and
// is nothing of the kind.
[Category("Unit")]
public sealed class VerificationReceiptPolicyTests
{
    [Test]
    public void Cgroup_method_with_windows_backend_is_invalid()
    {
        var (binding, host) = Identity(VerificationCustodyBackends.WindowsJob);

        Should.Throw<VerificationCustodyException>(() => new VerificationReceiptPolicy()
                .Validate(Bytes(Receipt(binding, host, VerificationCustodyBackends.CgroupProcsEmpty)), binding, host))
            .Code.ShouldBe("verification_custody_invalid_receipt");
    }

    [Test]
    public void Job_method_with_linux_backend_is_invalid()
    {
        var (binding, host) = Identity(VerificationCustodyBackends.LinuxCgroup);

        Should.Throw<VerificationCustodyException>(() => new VerificationReceiptPolicy()
                .Validate(Bytes(Receipt(binding, host, VerificationCustodyBackends.JobObjectAccounting)), binding, host))
            .Code.ShouldBe("verification_custody_invalid_receipt");
    }

    [Test]
    public void Each_backend_accepts_its_own_method()
    {
        foreach (var (backend, method) in new[]
                 {
                     (VerificationCustodyBackends.WindowsJob, VerificationCustodyBackends.JobObjectAccounting),
                     (VerificationCustodyBackends.LinuxCgroup, VerificationCustodyBackends.CgroupProcsEmpty),
                 })
        {
            var (binding, host) = Identity(backend);
            new VerificationReceiptPolicy().Validate(Bytes(Receipt(binding, host, method)), binding, host)
                .ObservationMethod.ShouldBe(method);
        }
    }

    // An unknown backend has no honest method at all, so nothing it produced can be imported.
    [Test]
    public void An_unsupported_backend_has_no_valid_receipt()
    {
        var (binding, host) = Identity("cgroup-v9");

        Should.Throw<VerificationCustodyException>(() => new VerificationReceiptPolicy()
                .Validate(Bytes(Receipt(binding, host, VerificationCustodyBackends.CgroupProcsEmpty)), binding, host))
            .Code.ShouldBe("verification_custody_unsupported_backend");
    }

    // R-13: the import path re-runs the same derivation against the row's stored binding, so a
    // row whose receipt bytes disagree with its own binding's backend cannot be discharged.
    [Test]
    public void Imported_receipt_must_match_binding_backend()
    {
        var (binding, host) = Identity(VerificationCustodyBackends.LinuxCgroup);
        var bytes = Bytes(Receipt(binding, host, VerificationCustodyBackends.JobObjectAccounting));
        var row = new VerificationExecution
        {
            Id = binding.ExecutionId, TaskId = binding.Source.TaskId,
            SourceLandingOperationId = binding.Source.SourceOperationId,
            SessionId = binding.Generation.SessionId, AcceptedStartedAt = binding.Generation.AcceptedStartedAt,
            BindingJson = JsonSerializer.Serialize(binding), ReceiptBytes = bytes,
            ReceiptDigest = Convert.ToHexString(SHA256.HashData(bytes)),
            ReceiptImportedAt = DateTime.UtcNow, HostIdentityJson = JsonSerializer.Serialize(host),
        };

        Should.Throw<VerificationCustodyException>(() => new VerificationReceiptPolicy().ValidateImported(row, binding))
            .Code.ShouldBe("verification_custody_invalid_receipt");

        // The same row with the method its backend actually produces imports cleanly.
        var honest = Bytes(Receipt(binding, host, VerificationCustodyBackends.CgroupProcsEmpty));
        row.ReceiptBytes = honest;
        row.ReceiptDigest = Convert.ToHexString(SHA256.HashData(honest));
        Should.NotThrow(() => new VerificationReceiptPolicy().ValidateImported(row, binding));
    }

    private static (VerificationExecutionBinding Binding, VerificationHostIdentity Host) Identity(string backend)
    {
        var now = DateTime.UtcNow;
        var store = Guid.NewGuid();
        var binding = new VerificationExecutionBinding(Guid.NewGuid(),
            new(Guid.NewGuid(), Guid.NewGuid(), new string('a', 40)),
            new(Guid.NewGuid(), new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc)),
            new(@"C:\repo", @"C:\repo\.git", @"C:\trees\snapshot", @"C:\repo\.git\worktrees\snapshot",
                "feat/card-task-12345678", Guid.NewGuid()),
            backend, store);
        return (binding, new(store, Guid.NewGuid(), Guid.NewGuid(), 123, now));
    }

    private static VerificationCustodyReceipt Receipt(
        VerificationExecutionBinding binding, VerificationHostIdentity host, string method)
    {
        var now = DateTime.UtcNow;
        return new(1, binding, host, 3, now, now, method, 0, true,
            VerificationCustodyState.Exited, 123, now);
    }

    private static byte[] Bytes(VerificationCustodyReceipt receipt) =>
        JsonSerializer.SerializeToUtf8Bytes(receipt, new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
