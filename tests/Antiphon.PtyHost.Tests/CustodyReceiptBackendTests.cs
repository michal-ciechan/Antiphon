using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.PtyHost.Tests;

// CARD-0604 D-19 / G-30, R-13, R-14. A receipt's observation method is not a free-text label the
// producer chooses: it is derived from the binding's backend, so the only honest method for a
// Windows binding is the job object's accounting and the only honest method for a Linux binding
// is the cgroup's own procs file. Accepting either method for either backend is exactly how a
// Linux execution ends up sealed with a fabricated windows-job-v1 receipt.
//
// These execute on Windows; the backend is data, not a platform.
[Category("Unit")]
public sealed class CustodyReceiptBackendTests
{
    [Test]
    public void Cgroup_method_requires_linux_backend()
    {
        using var fixture = new StoreFixture(VerificationCustodyBackends.WindowsJob);

        Should.Throw<VerificationCustodyException>(() =>
                fixture.Validate(fixture.Receipt(VerificationCustodyBackends.CgroupProcsEmpty)))
            .Code.ShouldBe("verification_custody_invalid_receipt");
    }

    [Test]
    public void Job_method_requires_windows_backend()
    {
        using var fixture = new StoreFixture(VerificationCustodyBackends.LinuxCgroup);

        Should.Throw<VerificationCustodyException>(() =>
                fixture.Validate(fixture.Receipt(VerificationCustodyBackends.JobObjectAccounting)))
            .Code.ShouldBe("verification_custody_invalid_receipt");
    }

    [Test]
    public void Receipt_bytes_round_trip_for_both_backends()
    {
        foreach (var (backend, method) in new[]
                 {
                     (VerificationCustodyBackends.WindowsJob, VerificationCustodyBackends.JobObjectAccounting),
                     (VerificationCustodyBackends.LinuxCgroup, VerificationCustodyBackends.CgroupProcsEmpty),
                 })
        {
            using var fixture = new StoreFixture(backend);
            var bytes = fixture.Store.SaveProducer(fixture.Receipt(method));

            // The accepted document is the producer's exact bytes, not a re-serialisation: the
            // digest the server records has to be of what the producer actually wrote (R-13).
            fixture.Store.AcceptReceipt(bytes, fixture.Binding, fixture.Host);
            var accepted = fixture.Store.ReadReceipt(fixture.Binding, fixture.Host, accepted: true);
            accepted.ShouldNotBeNull();
            accepted.ShouldBe(bytes);
            fixture.Store.ValidateReceipt(accepted, fixture.Binding, fixture.Host)
                .ObservationMethod.ShouldBe(method);
        }
    }

    // R-14: both zero descendants AND a drained output are required for Exited. Either alone is
    // a Draining observation the seal must not turn into terminal evidence.
    [Test]
    public void Exited_requires_zero_descendants_and_drained_output()
    {
        using var fixture = new StoreFixture(VerificationCustodyBackends.LinuxCgroup);

        Should.Throw<VerificationCustodyException>(() => fixture.Validate(
            fixture.Receipt(VerificationCustodyBackends.CgroupProcsEmpty) with { ActiveProcesses = 2 }));
        Should.Throw<VerificationCustodyException>(() => fixture.Validate(
            fixture.Receipt(VerificationCustodyBackends.CgroupProcsEmpty) with { OutputDrained = false }));
    }

    // The never-started method is backend-independent: nothing ran, so there is nothing for a
    // backend to have observed.
    [Test]
    public void Never_started_method_is_accepted_for_both_backends()
    {
        foreach (var backend in new[] { VerificationCustodyBackends.WindowsJob, VerificationCustodyBackends.LinuxCgroup })
        {
            using var fixture = new StoreFixture(backend);
            var receipt = fixture.Receipt(VerificationCustodyBackends.NeverStartedMethod) with
            {
                Disposition = VerificationCustodyState.NeverStarted,
                ActiveProcesses = null, RootPid = null, RootStartTimeUtc = null,
            };
            Should.NotThrow(() => fixture.Validate(receipt));
        }
    }

    private sealed class StoreFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "c604-receipt-" + Guid.NewGuid().ToString("N"));

        public VerificationCustodyStore Store { get; }
        public VerificationExecutionBinding Binding { get; }
        public VerificationHostIdentity Host { get; }

        public StoreFixture(string backend)
        {
            Directory.CreateDirectory(_root);
            Store = new(Path.Combine(_root, "custody"));
            var now = DateTime.UtcNow;
            var snapshot = Path.Combine(_root, "snapshot");
            Directory.CreateDirectory(snapshot);
            Binding = new(Guid.NewGuid(), new(Guid.NewGuid(), Guid.NewGuid(), new string('a', 40)),
                new(Guid.NewGuid(), new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc)),
                new(_root, Path.Combine(_root, ".git"), snapshot,
                    Path.Combine(_root, ".git", "worktrees", "snapshot"), "feat/test", Guid.NewGuid()),
                backend, Store.StoreId);
            Store.Reserve(Binding);
            Host = new(Store.StoreId, Guid.NewGuid(), Guid.NewGuid(), Environment.ProcessId, now);
        }

        public VerificationCustodyReceipt Receipt(string method)
        {
            var now = DateTime.UtcNow;
            return new(1, Binding, Host, 4, now, now, method, 0, true,
                VerificationCustodyState.Exited, Environment.ProcessId, now);
        }

        public VerificationCustodyReceipt Validate(VerificationCustodyReceipt receipt) =>
            Store.ValidateReceipt(
                System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(receipt,
                    new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)),
                Binding, Host);

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }
    }
}
