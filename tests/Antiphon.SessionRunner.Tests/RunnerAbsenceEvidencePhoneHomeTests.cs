using System.Text.Json;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1153 S3: the phone-home operations through the real <c>PhoneHomeCommandDispatcher</c>
/// and <c>PhoneHomeRuntimeAdapter</c> with production framing in both directions. Design: V-11.
/// The unauthenticated-transport half is the existing
/// <c>PhoneHomeConnectionTests.Authentication_is_required_at_both_endpoints</c> (R-10).
/// </summary>
[Category("Integration")]
public class RunnerAbsenceEvidencePhoneHomeTests
{
    /// <summary>
    /// V-11. prepare-and-certify: both frames round-trip with matching epoch, request id and
    /// operation and the certificate carries the connection's store. unsupported-or-foreign-store:
    /// typed error frames (unsupported operation, foreign store) without entering the runtime.
    /// transcript-of-prepared-id: the Transcript operation for a prepared, never-created id stays
    /// a typed 404 error frame, never an empty result.
    /// </summary>
    [Test]
    [Arguments("prepare-and-certify")]
    [Arguments("unsupported-or-foreign-store")]
    [Arguments("transcript-of-prepared-id")]
    public async Task C1153_Authenticated_operation_preserves_binding(string condition)
    {
        await using var world = await RuntimeWorld.CreateAsync();
        var adapter = new PhoneHomeRuntimeAdapter(world.Runtime, new RunnerBuildDto("test", null, DateTime.UtcNow, DateTime.UtcNow));
        var dispatcher = new PhoneHomeCommandDispatcher(adapter, new PhoneHomeSettings
        {
            Enabled = true, AllowedCwd = world.Root, Capacity = 2,
            CapacityStatePath = Path.Combine(world.Root, "capacity-" + Guid.NewGuid().ToString("N") + ".json"),
        });
        adapter.Capabilities().Features!.ShouldContain(RunnerAbsenceEvidence.Feature, "ready runner advertises the operation");
        var id = Guid.NewGuid();

        switch (condition)
        {
            case "prepare-and-certify":
            {
                var prepare = Frame(PhoneHomeOperation.PrepareAbsenceEvidence, world.Request(id), epoch: 7);
                var prepared = await dispatcher.DispatchAsync(prepare, CancellationToken.None);
                AssertCorrelated(prepared, prepare, PhoneHomeFrameKind.Result);
                var ack = prepared.Payload!.Value.Deserialize<RunnerAbsencePrepared>(PhoneHomeFraming.Json)!;
                ack.State.ShouldBe("Prepared");
                ack.RunnerStoreId.ShouldBe(world.StoreId);

                var nonce = RunnerAbsenceEvidence.NewNonce();
                var certify = Frame(PhoneHomeOperation.CertifyAbsence, world.Request(id, nonce), epoch: 7);
                var certified = await dispatcher.DispatchAsync(certify, CancellationToken.None);
                AssertCorrelated(certified, certify, PhoneHomeFrameKind.Result);
                var certificate = certified.Payload!.Value;
                certificate.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal)
                    .ShouldBe(RunnerAbsenceEvidence.CertificateMembers.OrderBy(n => n, StringComparer.Ordinal));
                certificate.GetProperty("runnerStoreId").GetString().ShouldBe(world.StoreId.ToString("D"));
                certificate.GetProperty("requestNonce").GetString().ShouldBe(nonce);
                certificate.GetProperty("sessionId").GetString().ShouldBe(id.ToString("D"));
                certificate.GetProperty("identityClosed").GetBoolean().ShouldBeTrue();
                world.ReadState(id).ShouldBe(RunnerAbsenceRecordState.ClosedUnused);

                // A delayed Launch for the certified id is the closed-identity frame, not a retryable error.
                var launch = await dispatcher.DispatchAsync(Frame(PhoneHomeOperation.Launch, world.Launch(id) with
                {
                    Exe = "/bin/sh",
                }), CancellationToken.None);
                launch.Kind.ShouldBe(PhoneHomeFrameKind.Error);
                launch.ErrorCode.ShouldBe(RunnerAbsenceEvidence.PhoneHomeClosedIdentityErrorType);
                launch.StatusCode.ShouldBe(409);
                world.Effects.ShouldBeEmpty();
                break;
            }
            case "unsupported-or-foreign-store":
            {
                // An adapter without the evidence service (the interface default) answers unsupported.
                var legacy = new PhoneHomeCommandDispatcher(new LegacySurface(world.StoreId), new PhoneHomeSettings
                {
                    Enabled = true, AllowedCwd = world.Root, Capacity = 1,
                    CapacityStatePath = Path.Combine(world.Root, "capacity-legacy.json"),
                });
                var unsupported = await legacy.DispatchAsync(Frame(PhoneHomeOperation.CertifyAbsence, world.Request(id)), CancellationToken.None);
                unsupported.Kind.ShouldBe(PhoneHomeFrameKind.Error);
                unsupported.ErrorCode.ShouldBe(PhoneHomeProblemTypes.UnsupportedOperation);

                var foreign = world.Request(id) with { RunnerStoreId = Guid.NewGuid() };
                var refused = await dispatcher.DispatchAsync(Frame(PhoneHomeOperation.PrepareAbsenceEvidence, foreign), CancellationToken.None);
                refused.Kind.ShouldBe(PhoneHomeFrameKind.Error);
                refused.ErrorCode.ShouldBe(RunnerAbsenceRefusalCodes.BindingMismatch);
                File.Exists(world.RecordPath(id)).ShouldBeFalse("a foreign-store request never reaches the store");

                var malformed = await dispatcher.DispatchAsync(new PhoneHomeFrame(PhoneHomeFrameKind.Request, 1, Guid.NewGuid(),
                    PhoneHomeOperation.CertifyAbsence, JsonSerializer.SerializeToElement(new { sessionId = id, extra = true })), CancellationToken.None);
                malformed.Kind.ShouldBe(PhoneHomeFrameKind.Error);
                malformed.ErrorCode.ShouldBe(RunnerAbsenceRefusalCodes.InvalidRequest);
                break;
            }
            case "transcript-of-prepared-id":
            {
                (await dispatcher.DispatchAsync(Frame(PhoneHomeOperation.PrepareAbsenceEvidence, world.Request(id)), CancellationToken.None))
                    .Kind.ShouldBe(PhoneHomeFrameKind.Result);
                var transcript = await dispatcher.DispatchAsync(new PhoneHomeFrame(PhoneHomeFrameKind.Request, 1, Guid.NewGuid(),
                    PhoneHomeOperation.Transcript, JsonSerializer.SerializeToElement(new { sessionId = id })), CancellationToken.None);
                transcript.Kind.ShouldBe(PhoneHomeFrameKind.Error, "a prepared id is never an empty transcript");
                transcript.StatusCode.ShouldBe(404);
                transcript.Payload.ShouldBeNull();
                world.ReadState(id).ShouldBe(RunnerAbsenceRecordState.Prepared);
                break;
            }
            default: throw new ArgumentOutOfRangeException(nameof(condition), condition, null);
        }
    }

    private static PhoneHomeFrame Frame(PhoneHomeOperation operation, object payload, long epoch = 1) =>
        new(PhoneHomeFrameKind.Request, epoch, Guid.NewGuid(), operation,
            JsonSerializer.SerializeToElement(payload is RunnerAbsenceRequest r
                ? JsonDocument.Parse(RunnerAbsenceEvidence.RequestBody(r)).RootElement
                : payload, PhoneHomeFraming.Json));

    private static void AssertCorrelated(PhoneHomeFrame reply, PhoneHomeFrame request, PhoneHomeFrameKind kind)
    {
        reply.Kind.ShouldBe(kind, reply.ErrorCode + " " + reply.ErrorDetail);
        reply.Epoch.ShouldBe(request.Epoch);
        reply.RequestId.ShouldBe(request.RequestId);
        reply.Operation.ShouldBe(request.Operation);
    }

    /// <summary>A surface that never implemented the evidence operations (the interface defaults).</summary>
    private sealed class LegacySurface(Guid store) : IPhoneHomeRuntimeSurface
    {
        public Guid RunnerStoreId => store;
        public RunnerCapabilitiesDto Capabilities() => new("InboxConhost", "inbox", "test", false);
        public string Health() => "Healthy";
        public IReadOnlyList<RunnerSessionDto> List() => [];
        public Task<RunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) => throw new KeyNotFoundException();
        public Task<RunnerSessionDto> StartAsync(RunnerLaunchRequest request, CancellationToken ct) => throw new NotSupportedException();
        public RunnerBufferDto GetBuffer(Guid sessionId) => throw new KeyNotFoundException();
        public RunnerSnapshotDto GetSnapshot(Guid sessionId) => throw new KeyNotFoundException();
        public RunnerTranscriptDto GetTranscript(Guid sessionId) => throw new KeyNotFoundException();
        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) => throw new KeyNotFoundException();
        public Task<RunnerConditionalInputResult> SendConditionalInputAsync(Guid sessionId, RunnerConditionalInputRequest request, CancellationToken ct) =>
            throw new KeyNotFoundException();
        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => throw new KeyNotFoundException();
        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => throw new KeyNotFoundException();
        public Task<RunnerKillGenerationResult> KillGenerationAsync(Guid sessionId, DateTime expectedAcceptedStartedAt, CancellationToken ct) =>
            throw new KeyNotFoundException();
        public int OwnedSessionCount => 0;
    }
}
