using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1153 S1: the evidence store and service without a process or a transport.
/// Design: docs/superpowers/plans/2026-10-08-card-1153-test-design.md (V-1..V-4, V-23).
/// Fixture: a temp state root per test, one <c>RunnerAbsenceEvidenceService</c> per runtime
/// epoch, the A-10 inspection seam for runtime entries, artifacts, adoption and the fault latch.
/// </summary>
[Category("Unit")]
public class RunnerAbsenceEvidenceTests
{
    /// <summary>
    /// V-1. Decisive assertions: the record file exists with state Prepared, the exact session
    /// id, normalized generation, store and epoch; the seam saw zero creation calls; no runtime
    /// entry or transcript exists; an identical second prepare returns the same record bytes.
    /// </summary>
    [Test]
    public Task C1153_Prepare_records_only_a_fresh_identity()
    {
        using var world = new RunnerAbsenceEvidenceHarness();
        var id = Guid.NewGuid();
        world.AllFiles().ShouldBeEmpty();

        var first = world.Service.Prepare(world.Request(id));

        first.Refusal.ShouldBeNull();
        first.Value!.State.ShouldBe("Prepared");
        first.Value.SessionId.ShouldBe(id);
        first.Value.RuntimeEpoch.ShouldBe(world.Service.Epoch);
        var record = world.Record(id);
        record.State.ShouldBe(RunnerAbsenceRecordState.Prepared);
        record.SessionId.ShouldBe(id);
        record.AcceptedStartedAt.ShouldBe(SessionGeneration.Normalize(RunnerAbsenceEvidenceHarness.Generation));
        record.AcceptedStartedAt!.Value.Ticks.ShouldNotBe(RunnerAbsenceEvidenceHarness.Generation.Ticks,
            "the fixture generation carries sub-microsecond ticks the record must normalize away");
        record.RunnerStoreId.ShouldBe(world.StoreId);
        record.RuntimeEpoch.ShouldBe(world.Service.Epoch);
        world.AllFiles().ShouldBe([$"absence-evidence/{id:N}.json"],
            "prepare creates the record and nothing else: no manifest, sidecar, transcript or watermark");
        world.RuntimeEntries.ShouldBeEmpty();
        world.Files.Writes.ShouldBe(1);
        var bytes = world.RecordBytes(id);

        var second = world.Service.Prepare(world.Request(id));

        second.Refusal.ShouldBeNull();
        second.Value!.SessionId.ShouldBe(id);
        second.Value.RuntimeEpoch.ShouldBe(first.Value.RuntimeEpoch);
        second.Value.AcceptedStartedAt.ShouldBe(first.Value.AcceptedStartedAt);
        world.RecordBytes(id).ShouldBe(bytes, "an identical prepare returns the original record without rewriting it");
        world.Files.Writes.ShouldBe(1);
        world.AllFiles().ShouldBe([$"absence-evidence/{id:N}.json"]);
        return Task.CompletedTask;
    }

    /// <summary>
    /// V-2. Each case seeds one artifact or record for the id and requires a typed refusal with
    /// no new or overwritten record. Malformed sidecar still refuses (a tolerant loader is not
    /// an absence probe).
    /// </summary>
    [Test]
    [Arguments("live")]
    [Arguments("exited")]
    [Arguments("attempted-record")]
    [Arguments("closed-unused-record")]
    [Arguments("transcript-sidecar")]
    [Arguments("herdr-sidecar")]
    [Arguments("manifest")]
    [Arguments("custody-or-watermark")]
    [Arguments("malformed-sidecar")]
    public Task C1153_Prepare_refuses_existing_evidence(string condition)
    {
        using var world = new RunnerAbsenceEvidenceHarness();
        var control = Guid.NewGuid();
        world.Service.Prepare(world.Request(control)).Refusal.ShouldBeNull("pristine control prepares");

        var id = Guid.NewGuid();
        switch (condition)
        {
            case "live": world.RuntimeEntries[id] = RunnerAbsenceRuntimeEntry.Live; break;
            case "exited": world.RuntimeEntries[id] = RunnerAbsenceRuntimeEntry.Exited; break;
            case "attempted-record": world.SeedRecord(id, RunnerAbsenceRecordState.Attempted, world.Service.Epoch); break;
            case "closed-unused-record": world.SeedRecord(id, RunnerAbsenceRecordState.ClosedUnused, world.Service.Epoch); break;
            case "transcript-sidecar": world.SeedTranscriptSidecar(id); break;
            case "herdr-sidecar": world.SeedHerdrSidecar(id); break;
            case "manifest": world.SeedManifest(id); break;
            case "custody-or-watermark": world.SeedWatermark(id); break;
            case "malformed-sidecar": world.SeedMalformedTranscriptSidecar(id); break;
            default: throw new ArgumentOutOfRangeException(nameof(condition), condition, null);
        }

        var before = world.RecordBytes(id);
        var writes = world.Files.Writes;

        var result = world.Service.Prepare(world.Request(id));

        result.Value.ShouldBeNull($"{condition} must refuse prepare");
        result.Refusal!.Code.ShouldBe(RunnerAbsenceRefusalCodes.IdentityKnown);
        result.Refusal.Status.ShouldBe(409);
        world.RecordBytes(id).ShouldBe(before, "no new or overwritten record");
        world.Files.Writes.ShouldBe(writes);
        return Task.CompletedTask;
    }

    /// <summary>
    /// V-3. A second service instance (new epoch) over the same root, or the same epoch with a
    /// changed store: certify refuses with the typed prior-epoch/store reason, re-prepare refuses,
    /// and the original record bytes are unchanged afterwards.
    /// </summary>
    [Test]
    [Arguments("prepared-prior-epoch")]
    [Arguments("closed-prior-epoch")]
    [Arguments("changed-store")]
    public Task C1153_Restart_or_store_change_never_renews_proof(string condition)
    {
        using var world = new RunnerAbsenceEvidenceHarness();
        var id = Guid.NewGuid();
        world.PrepareOk(id);
        var firstEpoch = world.Service.Epoch;
        string expected;
        switch (condition)
        {
            case "prepared-prior-epoch":
                world.Restart();
                expected = RunnerAbsenceRefusalCodes.PriorEpoch;
                break;
            case "closed-prior-epoch":
                world.Service.Certify(world.Request(id)).Refusal.ShouldBeNull("same-epoch control certifies");
                world.Record(id).State.ShouldBe(RunnerAbsenceRecordState.ClosedUnused);
                world.Restart();
                expected = RunnerAbsenceRefusalCodes.PriorEpoch;
                break;
            case "changed-store":
                world.StoreId = Guid.NewGuid();
                expected = RunnerAbsenceRefusalCodes.BindingMismatch;
                break;
            default: throw new ArgumentOutOfRangeException(nameof(condition), condition, null);
        }

        if (condition != "changed-store")
            world.Service.Epoch.ShouldNotBe(firstEpoch, "a new service instance is a new random epoch");
        var before = world.RecordBytes(id);

        var certify = world.Service.Certify(world.Request(id));
        var certifyOldStore = world.Service.Certify(world.Request(id, store: world.Record(id).RunnerStoreId));
        var reprepare = world.Service.Prepare(world.Request(id));

        certify.Value.ShouldBeNull();
        certify.Refusal!.Code.ShouldBe(expected);
        certifyOldStore.Value.ShouldBeNull("the recorded store cannot certify once the runner store changed or the epoch moved");
        certifyOldStore.Refusal!.Code.ShouldBe(expected);
        reprepare.Value.ShouldBeNull("re-preparation never renews proof");
        reprepare.Refusal!.Status.ShouldBe(409);
        world.RecordBytes(id).ShouldBe(before, "original record bytes unchanged");
        world.Record(id).RuntimeEpoch.ShouldBe(firstEpoch);
        return Task.CompletedTask;
    }

    /// <summary>
    /// V-4. Strict reader: never a positive certificate; corrupt, unknown-schema and denied-read
    /// surface as distinct unknown reasons, not as absence; a lost root after initialization is
    /// unknown, not a blank store.
    /// </summary>
    [Test]
    [Arguments("unprepared")]
    [Arguments("corrupt-record")]
    [Arguments("unknown-schema")]
    [Arguments("denied-read")]
    [Arguments("lost-root")]
    public Task C1153_Unknown_or_unreadable_state_is_not_absence(string condition)
    {
        using var world = new RunnerAbsenceEvidenceHarness();
        var control = Guid.NewGuid();
        world.PrepareOk(control);
        world.Service.Certify(world.Request(control)).Value.ShouldNotBeNull("pristine control certifies");

        var id = Guid.NewGuid();
        RunnerAbsenceReadKind expectedKind;
        string expectedCode;
        int expectedStatus;
        switch (condition)
        {
            case "unprepared":
                expectedKind = RunnerAbsenceReadKind.NoRecord;
                expectedCode = RunnerAbsenceRefusalCodes.NotPrepared;
                expectedStatus = 404;
                break;
            case "corrupt-record":
                world.PrepareOk(id);
                File.WriteAllText(world.RecordPath(id), "{\"version\":1,\"state\":\"Prepared\"");
                expectedKind = RunnerAbsenceReadKind.Corrupt;
                expectedCode = RunnerAbsenceRefusalCodes.Unavailable;
                expectedStatus = 503;
                break;
            case "unknown-schema":
                world.PrepareOk(id);
                File.WriteAllText(world.RecordPath(id),
                    File.ReadAllText(world.RecordPath(id)).Replace("\"version\":1", "\"version\":2"));
                expectedKind = RunnerAbsenceReadKind.UnknownSchema;
                expectedCode = RunnerAbsenceRefusalCodes.Unavailable;
                expectedStatus = 503;
                break;
            case "denied-read":
                world.PrepareOk(id);
                world.Files.ReadFault = path => path == world.RecordPath(id) ? new UnauthorizedAccessException("denied") : null;
                expectedKind = RunnerAbsenceReadKind.Denied;
                expectedCode = RunnerAbsenceRefusalCodes.Unavailable;
                expectedStatus = 503;
                break;
            case "lost-root":
                world.PrepareOk(id);
                Directory.Delete(world.Store.Root, recursive: true);
                expectedKind = RunnerAbsenceReadKind.LostRoot;
                expectedCode = RunnerAbsenceRefusalCodes.Unavailable;
                expectedStatus = 503;
                break;
            default: throw new ArgumentOutOfRangeException(nameof(condition), condition, null);
        }

        world.Store.Read(id).Kind.ShouldBe(expectedKind);

        var certify = world.Service.Certify(world.Request(id));

        certify.Value.ShouldBeNull($"{condition} is never a positive certificate");
        certify.Refusal!.Code.ShouldBe(expectedCode);
        certify.Refusal.Status.ShouldBe(expectedStatus);
        if (condition == "unprepared")
            world.RecordBytes(id).ShouldBeNull("an arbitrary id gains no record from certify");
        if (condition == "lost-root")
        {
            world.Service.Prepare(world.Request(Guid.NewGuid())).Refusal!.Code
                .ShouldBe(RunnerAbsenceRefusalCodes.Unavailable, "a lost root is not a blank store");
            Directory.Exists(world.Store.Root).ShouldBeFalse("nothing recreates the lost root silently");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// V-23. One-condition flip table on the certify service (the S1 lesson). Each case first
    /// admits an independently seeded pristine Prepared identity, then changes exactly one
    /// condition and requires refusal with no state transition. The companion
    /// <c>closed-same-epoch-reissue</c> admits a fresh certificate after the exclusions re-run.
    /// </summary>
    [Test]
    [Arguments("no-record")]
    [Arguments("prepared-prior-epoch")]
    [Arguments("closed-prior-epoch")]
    [Arguments("attempted")]
    [Arguments("wrong-generation")]
    [Arguments("wrong-store")]
    [Arguments("adoption-incomplete")]
    [Arguments("storage-fault-latched")]
    [Arguments("runtime-entry-live")]
    [Arguments("runtime-entry-exited")]
    [Arguments("manifest-present")]
    [Arguments("transcript-sidecar-present")]
    [Arguments("herdr-sidecar-present")]
    [Arguments("custody-reservation-present")]
    [Arguments("watermark-present")]
    [Arguments("nonce-missing")]
    [Arguments("nonce-short")]
    [Arguments("version-0")]
    [Arguments("version-2")]
    [Arguments("empty-session-id")]
    [Arguments("corrupt-record")]
    [Arguments("closed-same-epoch-reissue")]
    public Task C1153_Certify_requires_every_fact(string condition)
    {
        using var world = new RunnerAbsenceEvidenceHarness();

        // Independent pristine positive: same service, same conditions, a different id.
        var control = Guid.NewGuid();
        world.PrepareOk(control);
        var controlNonce = RunnerAbsenceEvidence.NewNonce();
        var admitted = world.Service.Certify(world.Request(control, nonce: controlNonce));
        admitted.Refusal.ShouldBeNull("pristine control must admit");
        AssertCertificate(admitted.Value!, world, control, controlNonce);

        var id = Guid.NewGuid();
        world.PrepareOk(id);
        var request = world.Request(id);
        string? expectedCode;
        switch (condition)
        {
            case "no-record": File.Delete(world.RecordPath(id)); expectedCode = RunnerAbsenceRefusalCodes.NotPrepared; break;
            case "prepared-prior-epoch": world.Restart(); expectedCode = RunnerAbsenceRefusalCodes.PriorEpoch; break;
            case "closed-prior-epoch":
                world.Service.Certify(world.Request(id)).Refusal.ShouldBeNull();
                world.Restart();
                expectedCode = RunnerAbsenceRefusalCodes.PriorEpoch;
                break;
            case "attempted":
                world.Service.RecordCreationAttempt(id, RunnerAbsenceEvidenceHarness.Generation);
                world.Record(id).State.ShouldBe(RunnerAbsenceRecordState.Attempted);
                expectedCode = RunnerAbsenceRefusalCodes.IdentityKnown;
                break;
            case "wrong-generation":
                request = request with { AcceptedStartedAt = RunnerAbsenceEvidenceHarness.Generation.AddTicks(SessionGeneration.MicrosecondTicks) };
                expectedCode = RunnerAbsenceRefusalCodes.BindingMismatch;
                break;
            case "wrong-store": request = request with { RunnerStoreId = Guid.NewGuid() }; expectedCode = RunnerAbsenceRefusalCodes.BindingMismatch; break;
            case "adoption-incomplete": world.AdoptionComplete = false; expectedCode = RunnerAbsenceRefusalCodes.Unavailable; break;
            case "storage-fault-latched":
                // A failed attempt write on another id latches the whole runtime epoch.
                world.Files.WriteFault = _ => new IOException("disk full");
                world.Service.RecordCreationAttempt(Guid.NewGuid(), RunnerAbsenceEvidenceHarness.Generation);
                world.Files.WriteFault = null;
                world.Service.LatchReason.ShouldNotBeNull();
                expectedCode = RunnerAbsenceRefusalCodes.Unavailable;
                break;
            case "runtime-entry-live": world.RuntimeEntries[id] = RunnerAbsenceRuntimeEntry.Live; expectedCode = RunnerAbsenceRefusalCodes.IdentityKnown; break;
            case "runtime-entry-exited": world.RuntimeEntries[id] = RunnerAbsenceRuntimeEntry.Exited; expectedCode = RunnerAbsenceRefusalCodes.IdentityKnown; break;
            case "manifest-present": world.SeedManifest(id); expectedCode = RunnerAbsenceRefusalCodes.IdentityKnown; break;
            case "transcript-sidecar-present": world.SeedTranscriptSidecar(id); expectedCode = RunnerAbsenceRefusalCodes.IdentityKnown; break;
            case "herdr-sidecar-present": world.SeedHerdrSidecar(id); expectedCode = RunnerAbsenceRefusalCodes.IdentityKnown; break;
            case "custody-reservation-present": world.CustodyReservations[id] = true; expectedCode = RunnerAbsenceRefusalCodes.IdentityKnown; break;
            case "watermark-present": world.SeedWatermark(id); expectedCode = RunnerAbsenceRefusalCodes.IdentityKnown; break;
            case "nonce-missing": request = request with { RequestNonce = null! }; expectedCode = RunnerAbsenceRefusalCodes.InvalidRequest; break;
            case "nonce-short": request = request with { RequestNonce = Convert.ToBase64String(new byte[16]) }; expectedCode = RunnerAbsenceRefusalCodes.InvalidRequest; break;
            case "version-0": request = request with { Version = 0 }; expectedCode = RunnerAbsenceRefusalCodes.InvalidRequest; break;
            case "version-2": request = request with { Version = 2 }; expectedCode = RunnerAbsenceRefusalCodes.InvalidRequest; break;
            case "empty-session-id": request = request with { SessionId = Guid.Empty }; expectedCode = RunnerAbsenceRefusalCodes.InvalidRequest; break;
            case "corrupt-record":
                File.WriteAllText(world.RecordPath(id), File.ReadAllText(world.RecordPath(id)).Replace("\"Prepared\"", "\"Prepared \""));
                expectedCode = RunnerAbsenceRefusalCodes.Unavailable;
                break;
            case "closed-same-epoch-reissue":
                world.Service.Certify(world.Request(id)).Refusal.ShouldBeNull();
                world.Record(id).State.ShouldBe(RunnerAbsenceRecordState.ClosedUnused);
                expectedCode = null;
                break;
            default: throw new ArgumentOutOfRangeException(nameof(condition), condition, null);
        }

        var before = world.RecordBytes(id);
        var lookups = world.RuntimeLookups;

        var result = world.Service.Certify(request);

        if (expectedCode is null)
        {
            result.Refusal.ShouldBeNull("a ClosedUnused same-epoch identity reissues after the exclusions re-run");
            AssertCertificate(result.Value!, world, id, request.RequestNonce);
            world.RuntimeLookups.ShouldBeGreaterThan(lookups, "the reissue re-ran the runtime exclusion");
            world.RecordBytes(id).ShouldBe(before, "a reissue does not rewrite the closed record");
            return Task.CompletedTask;
        }

        result.Value.ShouldBeNull($"{condition} must refuse");
        result.Refusal!.Code.ShouldBe(expectedCode, condition);
        world.RecordBytes(id).ShouldBe(before, "a refusal makes no state transition");
        return Task.CompletedTask;
    }

    private static void AssertCertificate(RunnerAbsenceCertificate certificate, RunnerAbsenceEvidenceHarness world, Guid id, string nonce)
    {
        certificate.Version.ShouldBe(1);
        certificate.Outcome.ShouldBe(RunnerAbsenceEvidence.NeverCreated);
        certificate.SessionId.ShouldBe(id);
        certificate.AcceptedStartedAt.ShouldBe(SessionGeneration.Normalize(RunnerAbsenceEvidenceHarness.Generation));
        certificate.RunnerStoreId.ShouldBe(world.StoreId);
        certificate.RuntimeEpoch.ShouldBe(world.Service.Epoch);
        certificate.RequestNonce.ShouldBe(nonce);
        certificate.Complete.ShouldBe(true);
        certificate.CreationObserved.ShouldBe(false);
        certificate.NativeTranscriptPresent.ShouldBe(false);
        certificate.SidecarTranscriptPresent.ShouldBe(false);
        certificate.ProcessPresent.ShouldBe(false);
        certificate.IdentityClosed.ShouldBe(true);
        certificate.ObservedAtUtc.ShouldBe(world.Clock.GetUtcNow().UtcDateTime);
        world.Record(id).State.ShouldBe(RunnerAbsenceRecordState.ClosedUnused, "closed before the reply");
    }
}
