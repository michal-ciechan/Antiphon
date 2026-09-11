using System.Text.Json;
using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.PtyHost.Tests;

/// <summary>Consumer/storage policy tests. Seeded receipts here are not native acceptance evidence.</summary>
[Category("Unit")]
public class VerificationCustodyStoreTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void C478_G203_ReceiptFlush(bool failFlush)
    {
        using var fixture = new StoreFixture();
        var writes = new FlushProbe(failFlush);
        var files = new VerificationCustodyFiles(writes);
        var store = new VerificationCustodyStore(fixture.Store.Root, fixture.Store.StoreId, files);
        if (failFlush)
        {
            Should.Throw<IOException>(() => store.SaveProducer(fixture.Receipt));
            writes.PublishCount.ShouldBe(0);
            fixture.Store.ReadReceipt(fixture.Binding, fixture.Receipt.Host, accepted: false).ShouldBeNull();
        }
        else
        {
            var bytes = store.SaveProducer(fixture.Receipt);
            writes.PublishCount.ShouldBe(1);
            fixture.Store.ReadReceipt(fixture.Binding, fixture.Receipt.Host, accepted: false).ShouldBe(bytes);
        }
        writes.FlushTrueCount.ShouldBe(1, "durability is an explicit Flush(true) before final rename");
    }

    [Test]
    public void Immutable_receipt_replay_preserves_original_bytes_and_rejects_conflict()
    {
        using var fixture = new StoreFixture();
        var bytes = fixture.Store.SaveProducer(fixture.Receipt);
        fixture.Store.SaveProducer(fixture.Receipt).ShouldBe(bytes);
        var changed = fixture.Receipt with { ObservedAtUtc = fixture.Receipt.ObservedAtUtc.AddSeconds(1) };
        Should.Throw<VerificationCustodyException>(() => fixture.Store.SaveProducer(changed)).Code
            .ShouldBe("verification_custody_integrity_error");
        new VerificationCustodyStore(fixture.Store.Root, fixture.Store.StoreId)
            .ReadReceipt(fixture.Binding, fixture.Receipt.Host, accepted: false).ShouldBe(bytes);
        fixture.Store.AcceptReceipt(bytes, fixture.Binding, fixture.Receipt.Host);
        fixture.Store.ReadReceipt(fixture.Binding, fixture.Receipt.Host, accepted: true).ShouldBe(bytes);
    }

    [Test]
    public void Failed_terminal_commit_has_no_readable_receipt_and_temporary_residue_is_not_proof()
    {
        using var fixture = new StoreFixture();
        var failing = new VerificationCustodyStore(fixture.Store.Root, fixture.Store.StoreId, new FailReceiptWrite());
        Should.Throw<IOException>(() => failing.SaveProducer(fixture.Receipt));
        fixture.Store.ReadReceipt(fixture.Binding, fixture.Receipt.Host, accepted: false).ShouldBeNull();
        fixture.Store.ReadReceipt(fixture.Binding, fixture.Receipt.Host, accepted: true).ShouldBeNull();
        var bytes = fixture.Store.SaveProducer(fixture.Receipt);
        Should.Throw<IOException>(() => failing.AcceptReceipt(bytes, fixture.Binding, fixture.Receipt.Host));
        fixture.Store.ReadReceipt(fixture.Binding, fixture.Receipt.Host, accepted: true).ShouldBeNull();
        fixture.Store.ReadReservations().Single().ShouldBe(fixture.Binding);
    }

    [Test]
    [Arguments("binding")]
    [Arguments("store")]
    public void Corrupt_or_missing_identity_never_becomes_an_empty_ledger(string kind)
    {
        using var fixture = new StoreFixture();
        if (kind == "binding")
        {
            File.WriteAllText(fixture.Store.PathFor(fixture.Binding.ExecutionId, "binding.json"), "{torn");
            Should.Throw<VerificationCustodyException>(() => fixture.Store.ReadReservations().ToArray());
        }
        else
        {
            File.Delete(Path.Combine(fixture.Store.Root, "store.json"));
            Should.Throw<VerificationCustodyException>(() => new VerificationCustodyStore(fixture.Store.Root)).Code
                .ShouldBe("verification_custody_store_identity_missing");
        }
    }

    [Test]
    [Arguments("schemaVersion")]
    [Arguments("sealedAtUtc")]
    [Arguments("outputDrained")]
    [Arguments("activeProcesses")]
    [Arguments("rootPid")]
    [Arguments("host")]
    public void Required_receipt_fields_cannot_be_defaulted_by_deserialization(string field)
    {
        using var fixture = new StoreFixture();
        var bytes = fixture.Store.SaveProducer(fixture.Receipt);
        var document = System.Text.Json.Nodes.JsonNode.Parse(bytes)!;
        document.AsObject().Remove(field).ShouldBeTrue();
        var malformed = JsonSerializer.SerializeToUtf8Bytes(document);
        Should.Throw<VerificationCustodyException>(() => fixture.Store.AcceptReceipt(malformed,
            fixture.Binding, fixture.Receipt.Host));
        fixture.Store.ReadReceipt(fixture.Binding, fixture.Receipt.Host, accepted: true).ShouldBeNull();
    }

    [Test]
    [Arguments("generation")]
    [Arguments("source")]
    [Arguments("creation")]
    [Arguments("store")]
    [Arguments("container")]
    [Arguments("host")]
    public void Crossed_receipt_identity_is_rejected_without_import(string field)
    {
        using var fixture = new StoreFixture();
        var receipt = fixture.Receipt;
        var changed = field switch
        {
            "generation" => receipt with { Binding = receipt.Binding with
                { Generation = receipt.Binding.Generation with { AcceptedStartedAt = receipt.Binding.Generation.AcceptedStartedAt.AddTicks(10) } } },
            "source" => receipt with { Binding = receipt.Binding with
                { Source = receipt.Binding.Source with { SourceOperationId = Guid.NewGuid() } } },
            "creation" => receipt with { Binding = receipt.Binding with
                { Creation = receipt.Binding.Creation with { CreationId = Guid.NewGuid() } } },
            "store" => receipt with { Host = receipt.Host with { RunnerStoreId = Guid.NewGuid() } },
            "container" => receipt with { Host = receipt.Host with { ContainerId = Guid.NewGuid() } },
            _ => receipt with { Host = receipt.Host with { HostInstanceId = Guid.NewGuid() } },
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(changed, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Should.Throw<VerificationCustodyException>(() => fixture.Store.AcceptReceipt(bytes, fixture.Binding, receipt.Host));
        fixture.Store.ReadReceipt(fixture.Binding, receipt.Host, accepted: true).ShouldBeNull();
    }

    [Test]
    public void Only_sealed_no_native_intent_failure_can_produce_never_started()
    {
        using var fixture = new StoreFixture();
        fixture.Store.WriteRecord(fixture.Binding, "runner-start-intent.json", new CustodyStamp(1, DateTime.UtcNow));
        var journal = new HostCustodyJournal(fixture.Store, fixture.Binding, Guid.NewGuid());
        journal.RecordLaunchFailure(unsupported: false);
        using var runner = new RunnerOwner();
        var status = journal.ObserveAsync(runner.Value, () => false, CancellationToken.None).GetAwaiter().GetResult();
        status.State.ShouldBe(VerificationCustodyState.NeverStarted);
        status.Receipt.ShouldNotBeNull();
    }

    private sealed class RunnerOwner : IDisposable
    {
        public Antiphon.Agents.Pty.PtyAgentRunner Value { get; } = new();
        public void Dispose() => Value.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private sealed class FailReceiptWrite : IVerificationCustodyFiles
    {
        private readonly VerificationCustodyFiles _actual = new();
        public byte[]? Read(string path) => _actual.Read(path);
        public void CommitImmutable(string path, byte[] bytes)
        {
            if (path.EndsWith("-receipt.json", StringComparison.Ordinal))
            {
                File.WriteAllBytes(path + ".incomplete.tmp", bytes);
                throw new IOException("Injected failure before final visibility");
            }
            _actual.CommitImmutable(path, bytes);
        }
    }

    private sealed class FlushProbe(bool failFlush) : IVerificationCustodyWriteOperations
    {
        public int FlushTrueCount { get; private set; }
        public int PublishCount { get; private set; }
        private bool _flushed;
        public FileStream CreateTemporary(string path) => new ObservedStream(path, flushToDisk =>
        {
            if (!flushToDisk) return;
            FlushTrueCount++;
            if (failFlush) throw new IOException("Injected Flush(true) failure");
            _flushed = true;
        });
        public void Publish(string temporary, string final)
        {
            _flushed.ShouldBeTrue("must flush before final visibility");
            File.Exists(final).ShouldBeFalse("temporary bytes are not published custody");
            PublishCount++;
            File.Move(temporary, final);
        }
        private sealed class ObservedStream(string path, Action<bool> observe)
            : FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)
        {
            public override void Flush(bool flushToDisk)
            {
                observe(flushToDisk);
                base.Flush(flushToDisk);
            }
        }
    }

    private sealed class StoreFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "antiphon-custody-store-tests", Guid.NewGuid().ToString("N"));
        public VerificationCustodyStore Store { get; }
        public VerificationExecutionBinding Binding { get; }
        public VerificationCustodyReceipt Receipt { get; }
        public StoreFixture()
        {
            Store = new(Path.Combine(_root, "runtime"));
            var now = DateTime.UtcNow;
            Binding = new(Guid.NewGuid(), new(Guid.NewGuid(), Guid.NewGuid(), new string('a', 40)),
                new(Guid.NewGuid(), new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc)),
                new(_root, Path.Combine(_root, ".git"), Path.Combine(_root, "snapshot"),
                    Path.Combine(_root, ".git", "worktrees", "snapshot"), "feat/test", Guid.NewGuid()));
            Store.Reserve(Binding);
            Receipt = new(1, Binding, new(Store.StoreId, Guid.NewGuid(), Guid.NewGuid(), Environment.ProcessId, now),
                4, now, now, "JobObjectBasicAccountingInformation", 0, true, VerificationCustodyState.Exited, 1234, now);
        }
        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
