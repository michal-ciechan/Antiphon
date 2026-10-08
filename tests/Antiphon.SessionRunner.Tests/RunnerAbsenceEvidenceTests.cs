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
        world.AllFiles().ShouldBe(EvidenceFiles(id),
            "prepare creates the record and the store identity (F1) and nothing else: no manifest, sidecar, transcript or watermark");
        world.RuntimeEntries.ShouldBeEmpty();
        world.Files.Writes.ShouldBe(4, "first write initializes closure log, header and anchor, then the record");
        var bytes = world.RecordBytes(id);

        var second = world.Service.Prepare(world.Request(id));

        second.Refusal.ShouldBeNull();
        second.Value!.SessionId.ShouldBe(id);
        second.Value.RuntimeEpoch.ShouldBe(first.Value.RuntimeEpoch);
        second.Value.AcceptedStartedAt.ShouldBe(first.Value.AcceptedStartedAt);
        world.RecordBytes(id).ShouldBe(bytes, "an identical prepare returns the original record without rewriting it");
        world.Files.Writes.ShouldBe(4);
        world.AllFiles().ShouldBe(EvidenceFiles(id));
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
    [Arguments("store-header-changed")]
    [Arguments("replayed-nonce")]
    [Arguments("stale-issued-at")]
    [Arguments("future-issued-at")]
    [Arguments("issued-in-epoch-window")]
    [Arguments("issued-at-missing")]
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
            case "prepared-prior-epoch":
                world.Restart();
                request = world.Request(id); // F2: signed after the new epoch's replay window
                expectedCode = RunnerAbsenceRefusalCodes.PriorEpoch;
                break;
            case "closed-prior-epoch":
                world.Service.Certify(world.Request(id)).Refusal.ShouldBeNull();
                world.Restart();
                request = world.Request(id);
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
            case "store-header-changed":
                // F1: a replaced store header is unknown evidence, never a blank or healthy store.
                File.WriteAllText(Path.Combine(world.Store.Root, "store.json"),
                    $"{{\"version\":1,\"incarnation\":\"{Guid.NewGuid():D}\"}}");
                expectedCode = RunnerAbsenceRefusalCodes.Unavailable;
                break;
            // F2: replay admission. Each flips only the request's freshness or its nonce's history.
            case "replayed-nonce":
                // The identical signed request was already presented (cross-route: as a prepare).
                world.Service.Prepare(request).Refusal.ShouldBeNull("the first presentation is admitted");
                expectedCode = RunnerAbsenceRefusalCodes.Replayed;
                break;
            case "stale-issued-at":
                request = request with { IssuedAtUtc = request.IssuedAtUtc!.Value - RunnerAbsenceEvidence.RequestFreshness - TimeSpan.FromSeconds(1) };
                expectedCode = RunnerAbsenceRefusalCodes.StaleRequest;
                break;
            case "future-issued-at":
                request = request with { IssuedAtUtc = request.IssuedAtUtc!.Value + RunnerAbsenceEvidence.RequestFreshness + TimeSpan.FromSeconds(1) };
                expectedCode = RunnerAbsenceRefusalCodes.StaleRequest;
                break;
            case "issued-in-epoch-window":
                // Fresh (2 s old) but issued before this epoch's start plus the window: a possible
                // pre-restart capture meeting an empty replay cache.
                request = request with { IssuedAtUtc = request.IssuedAtUtc!.Value - TimeSpan.FromSeconds(2) };
                expectedCode = RunnerAbsenceRefusalCodes.StaleRequest;
                break;
            case "issued-at-missing": request = request with { IssuedAtUtc = null }; expectedCode = RunnerAbsenceRefusalCodes.InvalidRequest; break;
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

    /// <summary>
    /// CARD-1153 F1 (Review 1a174347). A certificate already issued cannot be revoked, so the
    /// closure must survive every later storage failure: each case certifies the id, damages
    /// exactly one thing, then requires that the read-only fence and the attempt marker both refuse
    /// with the closed-identity type (no Attempted write) and that a fresh certify never answers.
    /// A pristine control on another id still admits a launch marker before the damage.
    /// </summary>
    [Test]
    [Arguments("corrupt-record")]
    [Arguments("truncated-record")]
    [Arguments("zero-length-record")]
    [Arguments("deleted-record")]
    [Arguments("directory-in-place")]
    [Arguments("denied-read")]
    [Arguments("read-error")]
    [Arguments("store-wiped")]
    [Arguments("store-replaced")]
    [Arguments("lost-root")]
    [Arguments("reverted-record")]
    [Arguments("anchor-lost")]
    public Task C1153_Closure_survives_storage_failure(string fault)
    {
        using var world = new RunnerAbsenceEvidenceHarness();
        var control = Guid.NewGuid();
        world.Service.RequireOpenIdentity(control);
        world.Service.RecordCreationAttempt(control, RunnerAbsenceEvidenceHarness.Generation);
        world.Record(control).State.ShouldBe(RunnerAbsenceRecordState.Attempted, "pristine control admits creation");

        var id = Guid.NewGuid();
        world.PrepareOk(id);
        world.Service.Certify(world.Request(id)).Value.ShouldNotBeNull("the certificate is issued before the damage");
        var path = world.RecordPath(id);
        var closed = File.ReadAllBytes(path);
        switch (fault)
        {
            case "corrupt-record": File.WriteAllText(path, "{\"version\":1,\"state\":\"Clo"); break;
            case "truncated-record": File.WriteAllBytes(path, closed[..(closed.Length / 2)]); break;
            case "zero-length-record": File.WriteAllBytes(path, []); break;
            case "deleted-record": File.Delete(path); break;
            case "directory-in-place": File.Delete(path); Directory.CreateDirectory(path); break;
            case "denied-read":
                world.Files.ReadFault = p => p == path ? new UnauthorizedAccessException("denied") : null;
                break;
            case "read-error":
                world.Files.ReadFault = p => p == path ? new IOException("disk read error") : null;
                break;
            case "store-wiped":
                // The volume lost every evidence file while the directory itself survived.
                foreach (var file in Directory.GetFiles(world.Store.Root)) File.Delete(file);
                break;
            case "store-replaced":
                // A different store (its own header) now sits where this one was.
                foreach (var file in Directory.GetFiles(world.Store.Root)) File.Delete(file);
                File.WriteAllText(Path.Combine(world.Store.Root, "store.json"),
                    $"{{\"version\":1,\"incarnation\":\"{Guid.NewGuid():D}\"}}");
                File.WriteAllBytes(Path.Combine(world.Store.Root, "closed.log"), []);
                break;
            case "lost-root": Directory.Delete(world.Store.Root, recursive: true); break;
            case "reverted-record":
                // A well-formed record that is no longer ClosedUnused (an older copy restored).
                File.WriteAllBytes(path, RunnerAbsenceEvidenceStore.Serialize(world.Store.Read(id).Record! with
                {
                    State = RunnerAbsenceRecordState.Attempted,
                }));
                break;
            case "anchor-lost":
                // Anchor and every evidence file gone while this process runs: not a fresh store.
                File.Delete(world.Store.Root + ".identity.json");
                foreach (var file in Directory.GetFiles(world.Store.Root)) File.Delete(file);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(fault), fault, null);
        }

        var writes = world.Files.Writes;

        Should.Throw<SessionIdentityClosedException>(() => world.Service.RequireOpenIdentity(id),
            $"{fault}: the fence refuses a creation it cannot prove open").SessionId.ShouldBe(id);
        Should.Throw<SessionIdentityClosedException>(
            () => world.Service.RecordCreationAttempt(id, RunnerAbsenceEvidenceHarness.Generation),
            $"{fault}: the marker refuses too").SessionId.ShouldBe(id);
        world.Files.Writes.ShouldBe(writes, "a refused creation writes no Attempted marker");

        world.Files.ReadFault = null;
        if (fault is "denied-read" or "read-error")
            world.Store.Read(id).Record!.State.ShouldBe(RunnerAbsenceRecordState.ClosedUnused, "the closure itself was never rewritten");
        var reissue = world.Service.Certify(world.Request(id));
        reissue.Value.ShouldBeNull($"{fault}: unknown evidence never certifies absence");
        reissue.Refusal.ShouldNotBeNull();
        return Task.CompletedTask;
    }

    /// <summary>
    /// V-28, CARD-1153 F1 round 2 (Review a086fe80). A certificate is returned only after every
    /// name its closure depends on is durable. Each case runs prepare and certify of a fresh
    /// store on a simulated volume (<see cref="PowerLossEvidenceFiles"/>), loses power immediately
    /// after the named operation (before any later directory sync), under each writeback outcome
    /// (no pending name, every pending name, only the latest), restarts, and requires: the store
    /// is usable (anchor last, each name synced before the next step depends on it); an id whose
    /// certificate was returned or whose closure was logged refuses delayed creation and
    /// certification; an id with neither is still open. Then the recovered store certifies a new
    /// id and that certificate survives a second power loss with no writeback.
    /// <c>sync-fails</c>: a directory sync that cannot complete refuses the certificate and latches.
    /// </summary>
    [Test]
    [Arguments("root-created")]
    [Arguments("closure-log-created")]
    [Arguments("header-written")]
    [Arguments("anchor-written")]
    [Arguments("prepared-record-written")]
    [Arguments("closure-appended")]
    [Arguments("closed-record-written")]
    [Arguments("certificate-returned")]
    [Arguments("session-log-path-created")]
    [Arguments("adopted-unsynced-store")]
    [Arguments("sync-fails")]
    public Task C1153_Certified_closure_survives_power_loss(string boundary)
    {
        if (boundary == "sync-fails")
        {
            using var faulty = new RunnerAbsenceEvidenceHarness();
            var closing = Guid.NewGuid();
            faulty.PrepareOk(closing);
            faulty.Files.SyncFault = p => p == faulty.Store.Root ? new IOException("directory fsync failed") : null;

            var refused = faulty.Service.Certify(faulty.Request(closing));

            refused.Value.ShouldBeNull("no certificate before the closure's names are durable");
            refused.Refusal!.Code.ShouldBe(RunnerAbsenceRefusalCodes.Unavailable);
            faulty.Service.LatchReason.ShouldNotBeNull("a failed sync latches evidence for this epoch");
            return Task.CompletedTask;
        }

        foreach (var writeback in new[] { Writeback.None, Writeback.All, Writeback.Latest })
        {
            var label = $"{boundary}/{writeback}";
            using var world = new RunnerAbsenceEvidenceHarness();
            var volume = new PowerLossEvidenceFiles(Path.GetDirectoryName(world.Root)!, world.Root);
            world.StoreFiles = volume;
            if (boundary == "session-log-path-created")
                world.StorePath = Path.Combine(world.Root, "state", "sessions");
            world.Restart();
            if (boundary == "adopted-unsynced-store")
            {
                world.PrepareOk(Guid.NewGuid());
                volume.ForgetDurability();
                world.Restart();
            }

            var id = Guid.NewGuid();
            var record = $"write:{id:N}.json";
            var crashAt = boundary switch
            {
                "root-created" => "mkdir:absence-evidence",
                "closure-log-created" => "write:closed.log",
                "header-written" => "write:store.json",
                "anchor-written" => "write:absence-evidence.identity.json",
                "prepared-record-written" => record,
                _ => null,
            };
            RunnerAbsenceCertificate? certificate = null;
            try
            {
                volume.CrashAfter = op => op == crashAt;
                world.Service.Prepare(world.Request(id)).Refusal.ShouldBeNull(label);
                volume.CrashAfter = boundary switch
                {
                    "closure-appended" => op => op == "append:closed.log",
                    "closed-record-written" => op => op == record,
                    _ => null,
                };
                var certified = world.Service.Certify(world.Request(id));
                certified.Refusal.ShouldBeNull(label);
                certificate = certified.Value;
            }
            catch (SimulatedPowerLoss) { }

            var certifiedBeforeLoss = boundary is "certificate-returned" or "session-log-path-created" or "adopted-unsynced-store";
            (certificate is not null).ShouldBe(certifiedBeforeLoss, $"{label}: only an uninterrupted sequence returns a certificate");
            volume.Crash(writeback);
            world.Restart();

            world.Store.UnknownReason.ShouldBeNull($"{label}: power loss at any step leaves a usable store");
            if (certifiedBeforeLoss)
                world.Store.Read(id).Record.ShouldNotBeNull(label).State.ShouldBe(RunnerAbsenceRecordState.ClosedUnused,
                    $"{label}: the certified record itself is durable, not only its closure-log line");
            if (certifiedBeforeLoss || boundary is "closure-appended" or "closed-record-written")
            {
                Should.Throw<SessionIdentityClosedException>(() => world.Service.RequireOpenIdentity(id),
                    $"{label}: a certified or logged closure survives power loss").SessionId.ShouldBe(id);
                Should.Throw<SessionIdentityClosedException>(
                    () => world.Service.RecordCreationAttempt(id, RunnerAbsenceEvidenceHarness.Generation), label);
                world.Service.Certify(world.Request(id)).Value.ShouldBeNull($"{label}: no second certificate");
            }
            else
                Should.NotThrow(() => world.Service.RequireOpenIdentity(id), $"{label}: no closure, so the id is still open");

            // The recovered store issues a certificate that survives power loss with no writeback.
            world.Restart();
            var next = Guid.NewGuid();
            world.PrepareOk(next);
            world.Service.Certify(world.Request(next)).Value.ShouldNotBeNull($"{label}: the recovered store certifies");
            volume.Crash(Writeback.None);
            world.Restart();
            world.Store.Read(next).Record.ShouldNotBeNull(label).State.ShouldBe(RunnerAbsenceRecordState.ClosedUnused, $"{label}: durable closed record");
            Should.Throw<SessionIdentityClosedException>(() => world.Service.RequireOpenIdentity(next),
                $"{label}: the next certificate is durable when returned").SessionId.ShouldBe(next);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// V-29, CARD-1153 F2 round 2 (Review a086fe80). Replay admission is monotone: once a request
    /// was admitted, no later wall-clock movement of the runner, cache eviction or flood admits it
    /// again. The server's clock is <c>Clock</c> (it signs issuedAtUtc); the runner's wall clock is
    /// <c>RunnerClock</c>, which can step without monotonic time moving. Each case admits a
    /// prepare request <c>replayed</c>, moves time, and presents the identical signed request again.
    /// </summary>
    [Test]
    [Arguments("expiry-then-rollback")]
    [Arguments("rollback-then-identical")]
    [Arguments("flood-then-replay-at-cap")]
    [Arguments("forward-jump")]
    [Arguments("small-step-under-skew")]
    public Task C1153_Replay_admission_is_monotone(string scenario)
    {
        using var world = new RunnerAbsenceEvidenceHarness();
        if (scenario == "small-step-under-skew")
        {
            // The runner's wall clock runs 20 s ahead of the server's for the whole epoch.
            world.RunnerClock.WallOffset = TimeSpan.FromSeconds(20);
            world.Restart();
            world.Clock.Advance(TimeSpan.FromSeconds(20));
        }

        var replayed = world.Request(Guid.NewGuid());
        world.Service.Prepare(replayed).Refusal.ShouldBeNull("the first presentation is admitted");
        RunnerAbsenceRefusal? refusal;
        switch (scenario)
        {
            case "expiry-then-rollback":
                // The Review's sequence: a later request 30.5 s on retires nothing it could revive,
                // then the runner's wall clock steps back one second.
                world.Clock.Advance(TimeSpan.FromSeconds(30.5));
                world.Service.Prepare(world.Request(Guid.NewGuid())).Refusal.ShouldBeNull("a later fresh request");
                world.RunnerClock.WallOffset -= TimeSpan.FromSeconds(1);
                refusal = world.Service.Prepare(replayed).Refusal;
                refusal.ShouldNotBeNull("a replay after expiry and a clock rollback is refused");
                refusal.Code.ShouldBe(RunnerAbsenceRefusalCodes.Unavailable);
                refusal.Reason.ShouldContain("stepped backwards");
                world.Clock.Advance(RunnerAbsenceEvidence.RequestFreshness + TimeSpan.FromSeconds(1));
                world.Service.Prepare(world.Request(Guid.NewGuid())).Refusal.ShouldBeNull("admission resumes one window after the step");
                break;
            case "rollback-then-identical":
                world.Clock.Advance(TimeSpan.FromSeconds(31));
                world.Service.Prepare(world.Request(Guid.NewGuid())).Refusal.ShouldBeNull("a later fresh request");
                world.RunnerClock.WallOffset -= TimeSpan.FromSeconds(70);
                world.Service.Certify(world.Request(Guid.NewGuid())).Refusal.ShouldNotBeNull("no request is admitted right after the step");
                // One window of monotonic time later the step refusal has ended and the runner's
                // wall clock (8 s behind the replay's issuedAtUtc) would call the replay fresh.
                world.Clock.Advance(TimeSpan.FromSeconds(31));
                refusal = world.Service.Prepare(replayed).Refusal;
                refusal.ShouldNotBeNull("the identical request is refused while the runner clock is behind");
                refusal.Code.ShouldBe(RunnerAbsenceRefusalCodes.StaleRequest, "issued a window before a request already seen");
                break;
            case "flood-then-replay-at-cap":
                world.Clock.Advance(TimeSpan.FromSeconds(29));
                for (var i = 1; i < RunnerAbsenceEvidenceService.MaxConsumedNonces; i++)
                    world.Service.Certify(world.Request(Guid.NewGuid())).Refusal!.Code.ShouldBe(RunnerAbsenceRefusalCodes.NotPrepared);
                world.Service.Prepare(replayed).Refusal!.Code.ShouldBe(RunnerAbsenceRefusalCodes.Replayed,
                    "a full cache still holds every nonce whose request could be admitted");
                world.Service.Certify(world.Request(Guid.NewGuid())).Refusal!.Code.ShouldBe(RunnerAbsenceRefusalCodes.Unavailable,
                    "a full cache refuses a new nonce instead of evicting a live one");
                world.Clock.Advance(TimeSpan.FromSeconds(2));
                world.Service.Prepare(world.Request(Guid.NewGuid())).Refusal.ShouldBeNull(
                    "a request a window after the oldest retires it and is admitted");
                world.RunnerClock.WallOffset -= TimeSpan.FromSeconds(40);
                world.Service.Certify(world.Request(Guid.NewGuid())).Refusal.ShouldNotBeNull("no request is admitted right after the step");
                world.Clock.Advance(TimeSpan.FromSeconds(31));
                refusal = world.Service.Prepare(replayed).Refusal;
                refusal.ShouldNotBeNull("the retired nonce's request is refused");
                refusal.Code.ShouldBe(RunnerAbsenceRefusalCodes.StaleRequest, "retired below the high-water mark, never revived by eviction");
                break;
            case "forward-jump":
                world.RunnerClock.WallOffset += TimeSpan.FromHours(1);
                world.Service.Prepare(world.Request(Guid.NewGuid())).Refusal!.Code.ShouldBe(RunnerAbsenceRefusalCodes.StaleRequest,
                    "a runner clock an hour ahead admits nothing");
                world.RunnerClock.WallOffset -= TimeSpan.FromHours(1);
                refusal = world.Service.Prepare(world.Request(Guid.NewGuid())).Refusal;
                refusal.ShouldNotBeNull("the jump back is a backward step");
                refusal.Code.ShouldBe(RunnerAbsenceRefusalCodes.Unavailable);
                world.Service.Prepare(replayed).Refusal.ShouldNotBeNull("the replay is refused during the step window");
                world.Clock.Advance(RunnerAbsenceEvidence.RequestFreshness + TimeSpan.FromSeconds(1));
                world.Service.Prepare(world.Request(Guid.NewGuid())).Refusal.ShouldBeNull("admission resumes one window after the step");
                world.Service.Prepare(replayed).Refusal.ShouldNotBeNull("and the replay stays refused");
                break;
            case "small-step-under-skew":
                // A step below the detection tolerance while the replay is just inside the window:
                // only the retained nonce refuses it.
                world.Clock.Advance(TimeSpan.FromSeconds(10.4));
                world.Service.Prepare(world.Request(Guid.NewGuid())).Refusal.ShouldBeNull("a later fresh request");
                world.RunnerClock.WallOffset -= TimeSpan.FromSeconds(0.45);
                refusal = world.Service.Prepare(replayed).Refusal;
                refusal.ShouldNotBeNull("a replay just inside the window after a small step is refused");
                refusal.Code.ShouldBe(RunnerAbsenceRefusalCodes.Replayed, "its nonce is retained until a later request retires it");
                break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// V-30, CARD-1153 F3 round 2 (Review a086fe80). A closure is trusted only while its record
    /// and its closure-log entry agree. Two ids are certified (<c>id</c> last), one disagreement is
    /// made, and then, each without a prior fence call latching anything: certify of the id
    /// refuses (no certificate); after a restart the read-only fence and the attempt marker both
    /// refuse with the closed-identity type and write nothing. The damage edits lines and bytes
    /// without knowing the log's line format. (A logged closure whose record is missing or not
    /// ClosedUnused is V-25 <c>deleted-record</c> and <c>reverted-record</c>.)
    /// </summary>
    [Test]
    [Arguments("record-without-log-entry")]
    [Arguments("last-entry-truncated")]
    [Arguments("record-differs-from-logged-closure")]
    [Arguments("duplicate-entry")]
    [Arguments("reordered-entries")]
    public Task C1153_Closure_record_and_log_must_agree(string damage)
    {
        using var world = new RunnerAbsenceEvidenceHarness();
        var first = Guid.NewGuid();
        var id = Guid.NewGuid();
        foreach (var closing in new[] { first, id })
        {
            world.PrepareOk(closing);
            world.Service.Certify(world.Request(closing)).Value.ShouldNotBeNull("the pristine closure certifies");
        }

        var log = Path.Combine(world.Store.Root, "closed.log");
        var lines = File.ReadAllText(log).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l + "\n").ToList();
        lines.Count.ShouldBe(2);
        switch (damage)
        {
            case "record-without-log-entry": File.WriteAllBytes(log, []); break; // Review: an empty, well-formed log
            case "last-entry-truncated": File.WriteAllText(log, lines[0]); break;
            case "record-differs-from-logged-closure":
                File.WriteAllBytes(world.RecordPath(id), RunnerAbsenceEvidenceStore.Serialize(world.Store.Read(id).Record! with
                {
                    UpdatedAtUtc = world.Store.Read(id).Record!.UpdatedAtUtc.AddSeconds(1),
                }));
                break;
            case "duplicate-entry": File.AppendAllText(log, lines[1]); break;
            case "reordered-entries": File.WriteAllText(log, lines[1] + lines[0]); break;
            default: throw new ArgumentOutOfRangeException(nameof(damage), damage, null);
        }

        world.Store.Read(id).IsKnown.ShouldBeFalse($"{damage}: disagreeing closure metadata is unknown evidence");
        var reissue = world.Service.Certify(world.Request(id));
        reissue.Value.ShouldBeNull($"{damage}: no certificate from disagreeing closure metadata");
        reissue.Refusal!.Code.ShouldBe(RunnerAbsenceRefusalCodes.Unavailable);

        world.Restart();
        var writes = world.Files.Writes;
        Should.Throw<SessionIdentityClosedException>(() => world.Service.RequireOpenIdentity(id),
            $"{damage}: delayed creation is refused").SessionId.ShouldBe(id);
        Should.Throw<SessionIdentityClosedException>(
            () => world.Service.RecordCreationAttempt(id, RunnerAbsenceEvidenceHarness.Generation), damage).SessionId.ShouldBe(id);
        world.Files.Writes.ShouldBe(writes, "a refused creation writes nothing");
        return Task.CompletedTask;
    }

    /// <summary>
    /// V-31, CARD-1153 round 2: deterministic controls for two fail-closed lines the previous
    /// round left without one (the anchor-last order is V-28 <c>anchor-written</c>; the 4096 cap
    /// is V-29 <c>flood-then-replay-at-cap</c>). <c>attempt-store-turns-unknown</c>: the store reads
    /// healthy for the marker's admission and unknown when the marker writes, so the marker
    /// refuses and latches instead of letting the launch proceed. <c>closure-log-length-malformed</c>:
    /// a closure log with a partial line reads unknown and refuses creation and certification.
    /// </summary>
    [Test]
    [Arguments("attempt-store-turns-unknown")]
    [Arguments("closure-log-length-malformed")]
    public Task C1153_Store_guard_lines_fail_closed(string guard)
    {
        using var world = new RunnerAbsenceEvidenceHarness();
        var control = Guid.NewGuid();
        world.PrepareOk(control);
        world.Service.Certify(world.Request(control)).Value.ShouldNotBeNull("pristine control certifies");
        var id = Guid.NewGuid();
        var anchor = world.Store.Root + ".identity.json";
        switch (guard)
        {
            case "attempt-store-turns-unknown":
                var anchorReads = 0;
                world.Files.ReadFault = p => p == anchor && ++anchorReads == 2 ? new IOException("anchor unreadable") : null;
                Should.Throw<SessionIdentityClosedException>(
                    () => world.Service.RecordCreationAttempt(id, RunnerAbsenceEvidenceHarness.Generation),
                    "the store turned unknown between the admission read and the marker write").SessionId.ShouldBe(id);
                anchorReads.ShouldBe(2, "admission read the anchor once, the write once");
                world.Files.ReadFault = null;
                world.RecordBytes(id).ShouldBeNull("no marker was written");
                world.Service.LatchReason.ShouldNotBeNull("evidence is latched for the epoch");
                break;
            case "closure-log-length-malformed":
                File.AppendAllText(Path.Combine(world.Store.Root, "closed.log"), "abcde");
                world.Store.UnknownReason.ShouldNotBeNull("a partial closure-log line is a damaged store");
                world.Service.Certify(world.Request(control)).Value.ShouldBeNull("no certificate from a damaged store");
                Should.Throw<SessionIdentityClosedException>(() => world.Service.RequireOpenIdentity(id),
                    "a damaged store refuses creation of any id").SessionId.ShouldBe(id);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(guard), guard, null);
        }

        return Task.CompletedTask;
    }

    private static string[] EvidenceFiles(Guid id) => new[]
    {
        "absence-evidence.identity.json", $"absence-evidence/{id:N}.json", "absence-evidence/closed.log",
        "absence-evidence/store.json",
    }.OrderBy(p => p, StringComparer.Ordinal).ToArray();

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
