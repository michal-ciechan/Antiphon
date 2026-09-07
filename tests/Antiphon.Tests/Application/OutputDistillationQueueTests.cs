using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public class OutputDistillationQueueTests
{
    [Test]
    public void Defaults_and_legacy_contract_are_preserved()
    {
        var settings = new DelegationSettings();
        settings.OutputDistillerMode.ShouldBe(OutputDistillerMode.Shadow);
        settings.OutputDistillerWaitSeconds.ShouldBe(45);
        settings.OutputDistillerQueueCapacity.ShouldBe(3);
        settings.OutputDistillerMaxBacklog.ShouldBe(3);
        Enum.GetValues<DistillationOutcome>().Take(12).Select(v => (int)v).ShouldBe(Enumerable.Range(0, 12));
        new AgentTask().ExecutionDeadlineAt.ShouldBeNull();
        new SessionQueuedMessage().ExecutionDeadlineAt.ShouldBeNull();
        new OutputDistillationRecord().DeadlineAt.ShouldBeNull();
    }

    [Test]
    public void Capacity_rejects_without_dropping_an_accepted_request()
    {
        var queue = new OutputDistillationQueue();
        var requests = Enumerable.Range(0, 5).Select(_ => new DistillRequest(Guid.NewGuid(), null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddSeconds(45), OutputDistillerMode.Shadow)).ToArray();
        foreach (var request in requests.Take(3)) queue.TryEnqueue(request).ShouldBeTrue();
        queue.TryEnqueue(requests[3]).ShouldBeFalse();
        queue.TryDequeue(out var first).ShouldBeTrue();
        first.ShouldBe(requests[0]);
        queue.TryEnqueue(requests[4]).ShouldBeTrue();
        foreach (var expected in new[] { requests[1], requests[2], requests[4] })
        {
            queue.TryDequeue(out var actual).ShouldBeTrue();
            actual.ShouldBe(expected);
        }
        queue.Complete();
        queue.TryEnqueue(requests[0]).ShouldBeFalse();
    }
}
