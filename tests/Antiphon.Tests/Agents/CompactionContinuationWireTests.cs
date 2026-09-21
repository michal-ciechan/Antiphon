using System.Text.Json;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

[Category("Integration")]
public class CompactionContinuationWireTests
{
    [Test]
    public async Task Stop_request_round_trips_and_unsupported_observation_is_not_success()
    {
        var attempt = Guid.NewGuid();
        var accepted = new DateTime(2026, 9, 14, 22, 50, 7, DateTimeKind.Utc);
        var request = new CompactionContinuationStopRequest(
            attempt, accepted, "boundary-1", "cont-1", 10, "bind-1", 3, 4);
        var json = JsonSerializer.Serialize(request);
        var restored = JsonSerializer.Deserialize<CompactionContinuationStopRequest>(json);
        restored.ShouldNotBeNull();
        restored.AttemptId.ShouldBe(attempt);
        restored.NativeBoundaryIdentity.ShouldBe("boundary-1");
        restored.ThresholdMinutes.ShouldBe(10);

        var fake = new FakeSessionRunnerClient();
        var unseen = await fake.ObserveCompactionAsync(Guid.NewGuid(), CancellationToken.None);
        unseen.IsSuccessful.ShouldBeFalse();
        unseen.Status.ShouldBe(CompactionObservationStatuses.Unsupported);

        fake.CompactionObservation = new CompactionTailObservation(
            CompactionObservationStatuses.Success, true, 8, "bind-1", 3, 4, "boundary-1", "cont-1");
        fake.CompactionStopResult = new CompactionContinuationStopResult(
            Guid.NewGuid(), attempt, true, CompactionStopOutcomes.Exited, accepted);
        var observed = await fake.ObserveCompactionAsync(Guid.NewGuid(), CancellationToken.None);
        observed.IsSuccessful.ShouldBeTrue();
        var stopped = await fake.StopCompactionContinuationAsync(observed.TranscriptRevision == 3
            ? Guid.NewGuid() : Guid.Empty, request, CancellationToken.None);
        stopped.ConfirmsExit.ShouldBeTrue();
        stopped.Outcome.ShouldBe(CompactionStopOutcomes.Exited);
        fake.CompactionStops.Count.ShouldBe(1);
        CompactionContinuationStopCapability.Feature.ShouldBe("compactionContinuationStopV1");
    }
}
