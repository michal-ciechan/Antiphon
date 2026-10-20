using Antiphon.E2E.Fixtures;
using Shouldly;
using TUnit.Core;

namespace Antiphon.E2E;

[Category("Unit")]
public class CreatedResourceTests
{
    [Test]
    [Timeout(5_000)]
    public async Task Cancelled_create_disposes_late_success(CancellationToken cancellationToken)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // Inline completion makes the late-disposal assertion independent of timers or a browser.
        var creation = new TaskCompletionSource<DisposableProbe>();
        var resource = new DisposableProbe();
        var used = false;
        var use = CreatedResource.UseAsync(creation.Task, _ =>
        {
            used = true;
            return Task.CompletedTask;
        }, cancellation.Token);

        await cancellation.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => use);
        creation.SetResult(resource);

        resource.DisposeCount.ShouldBe(1);
        used.ShouldBeFalse();
    }

    [Test]
    public async Task Successful_body_keeps_resource_until_return_then_disposes()
    {
        var resource = new DisposableProbe();
        var used = false;
        await CreatedResource.UseAsync(Task.FromResult(resource), actual =>
        {
            actual.ShouldBeSameAs(resource);
            actual.DisposeCount.ShouldBe(0);
            used = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        used.ShouldBeTrue();
        resource.DisposeCount.ShouldBe(1);
    }

    [Test]
    public async Task Failed_body_disposes_resource_and_preserves_exception()
    {
        var resource = new DisposableProbe();
        var failure = new InvalidOperationException("body failed");
        var actual = await Should.ThrowAsync<InvalidOperationException>(() =>
            CreatedResource.UseAsync(Task.FromResult(resource), _ => Task.FromException(failure),
                CancellationToken.None));

        actual.ShouldBeSameAs(failure);
        resource.DisposeCount.ShouldBe(1);
    }

    private sealed class DisposableProbe : IDisposable
    {
        public int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }
}
