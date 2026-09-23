using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

// CARD-0604 D-19 / G-29. The ledger is the last place a launch can be refused before a tracked
// child exists, and the question it must ask is equality, not membership: "is this binding's
// backend the one THIS runner advertises?" A runner that accepted any supported backend would
// take a Windows binding on Linux, run it, and then have to either fabricate a Windows receipt or
// leave the reserved execution unresolvable forever -- the two outcomes CARD-0598 exists to
// prevent. A refusal still reserves and still writes unsupported.json, so the server resolves it.
//
// These execute on Windows; the backend under test is injected, not inferred.
[Category("Unit")]
public sealed class RunnerCustodyLedgerBackendTests
{
    [Test]
    public void Foreign_backend_binding_is_refused()
    {
        using var fixture = new LedgerFixture();
        var binding = fixture.Binding(VerificationCustodyBackends.LinuxCgroup);

        var error = Should.Throw<VerificationCustodyException>(() =>
            fixture.Ledger.PrepareStart(fixture.Request(binding), "modern", VerificationCustodyBackends.WindowsJob));

        error.Code.ShouldBe("verification_custody_unsupported_backend");
        // Reserved and marked, not silently dropped: the server must be able to resolve this row.
        fixture.Ledger.Store.ReadRecord<CustodyFailure>(binding, "unsupported.json").ShouldNotBeNull();
        fixture.Ledger.Store.ReadRecord<CustodyStamp>(binding, "runner-start-intent.json").ShouldBeNull();
    }

    [Test]
    public void A_runner_that_advertises_nothing_refuses_every_binding()
    {
        using var fixture = new LedgerFixture();
        var binding = fixture.Binding(VerificationCustodyBackends.WindowsJob);

        Should.Throw<VerificationCustodyException>(() =>
                fixture.Ledger.PrepareStart(fixture.Request(binding), "modern", null))
            .Code.ShouldBe("verification_custody_unsupported_backend");
    }

    // The Linux backend is accepted only when the runtime handed the ledger the probe's answer.
    // On Windows the platform arm also has to agree, so this asserts the refusal an unprobed or
    // mismatched runner gets rather than a green Linux launch that cannot happen here.
    [Test]
    public void Linux_backend_accepted_only_when_probe_passed()
    {
        using var fixture = new LedgerFixture();
        var binding = fixture.Binding(VerificationCustodyBackends.LinuxCgroup);

        // Probe said nothing: refused.
        Should.Throw<VerificationCustodyException>(() =>
                fixture.Ledger.PrepareStart(fixture.Request(binding), "modern", null))
            .Code.ShouldBe("verification_custody_unsupported_backend");

        // Probe said linux-cgroup-v1, and the binding agrees -- so the backend equality check
        // passes and what refuses is the Windows platform arm, not the backend comparison.
        using var second = new LedgerFixture();
        var linux = second.Binding(VerificationCustodyBackends.LinuxCgroup);
        var refusal = Should.Throw<VerificationCustodyException>(() =>
            second.Ledger.PrepareStart(second.Request(linux), "modern", VerificationCustodyBackends.LinuxCgroup));
        refusal.Code.ShouldBe("verification_custody_unsupported_backend");
        second.Ledger.Store.ReadRecord<CustodyFailure>(linux, "unsupported.json").ShouldNotBeNull();
    }

    // R-16: the Windows lane is byte-for-byte what it was, with the backend now stated explicitly.
    [Test]
    public void Windows_path_unchanged()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new LedgerFixture();
        var binding = fixture.Binding(VerificationCustodyBackends.WindowsJob);

        var created = fixture.Ledger.PrepareStart(fixture.Request(binding), "modern", VerificationCustodyBackends.WindowsJob);

        created.ShouldBeTrue();
        fixture.Ledger.Store.ReadRecord<CustodyStamp>(binding, "runner-start-intent.json")!.Revision.ShouldBe(1);
        fixture.Ledger.Store.ReadRecord<CustodyFailure>(binding, "unsupported.json").ShouldBeNull();

        // And the exact replay is still a replay, not a second creation.
        fixture.Ledger.PrepareStart(fixture.Request(binding), "modern", VerificationCustodyBackends.WindowsJob)
            .ShouldBeFalse();
    }

    // A Windows binding that asks for the inbox conhost is still refused: on Windows the job
    // object IS the containment, and it only exists on the modern spawn path.
    [Test]
    public void Windows_backend_still_requires_modern_conpty()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new LedgerFixture();
        var binding = fixture.Binding(VerificationCustodyBackends.WindowsJob);

        Should.Throw<VerificationCustodyException>(() =>
                fixture.Ledger.PrepareStart(fixture.Request(binding), "inbox", VerificationCustodyBackends.WindowsJob))
            .Code.ShouldBe("verification_custody_unsupported_backend");
    }

    private sealed class LedgerFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "c604-ledger-" + Guid.NewGuid().ToString("N"));

        public RunnerCustodyLedger Ledger { get; }
        public string Snapshot { get; }

        public LedgerFixture()
        {
            Directory.CreateDirectory(_root);
            Snapshot = Path.Combine(_root, "snapshot");
            Directory.CreateDirectory(Snapshot);
            Ledger = new(Path.Combine(_root, "custody"));
        }

        public VerificationExecutionBinding Binding(string backend)
        {
            var now = DateTime.UtcNow;
            return new(Guid.NewGuid(), new(Guid.NewGuid(), Guid.NewGuid(), new string('a', 40)),
                new(Guid.NewGuid(), new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc)),
                new(_root, Path.Combine(_root, ".git"), Snapshot,
                    Path.Combine(_root, ".git", "worktrees", "snapshot"), "feat/test", Guid.NewGuid()),
                backend, Ledger.Store.StoreId);
        }

        public RunnerLaunchRequest Request(VerificationExecutionBinding binding) =>
            new(binding.Generation.SessionId, "cmd.exe", [], new Dictionary<string, string>(),
                binding.Creation.WorktreePath, 80, 24, VerificationBinding: binding);

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }
    }
}
