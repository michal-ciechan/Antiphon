using System.Diagnostics;
using System.Text.Json;
using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class CheckpointTempScopeTests : CheckpointTestBase
{
    [Test]
    public void allocation_requires_an_owner()
    {
        Should.Throw<InvalidOperationException>(() => new UnopenedProbe().Allocate());
    }

    [Test]
    public async Task root_is_registered_before_return()
    {
        var observed = false;
        await using var scope = new CheckpointTestScope(beforeReturn: (root, roots) =>
            observed = roots.Contains(root));
        var path = scope.TempDir();
        observed.ShouldBeTrue();
        File.Exists(Path.Combine(path, CheckpointTestScope.MarkerName)).ShouldBeTrue();
    }

    [Test]
    public async Task sealed_scope_awaits_registered_work()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scope = new CheckpointTestScope();
        var root = scope.TempDir();
        scope.Register(gate.Task);
        var disposal = scope.DisposeAsync().AsTask();
        Directory.Exists(root).ShouldBeTrue();
        disposal.IsCompleted.ShouldBeFalse();
        gate.SetResult();
        await disposal;
        Directory.Exists(root).ShouldBeFalse();
        File.Exists(Path.Combine(Path.GetTempPath(), ".checkpoint-temp-roots",
            Path.GetFileName(root) + ".json")).ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => scope.TempDir());
    }

    [Test]
    public async Task live_child_prevents_scope_deletion()
    {
        var scope = new CheckpointTestScope(new UnknownProbe());
        var root = scope.TempDir();
        using var child = Process.GetCurrentProcess();
        scope.Register(child);
        try
        {
            var failure = await Should.ThrowAsync<IOException>(async () => await scope.DisposeAsync());
            failure.Message.ShouldContain("identity-unknown");
            Directory.Exists(root).ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(root, true);
            CheckpointUsageEvent.Write("root-delete", root);
        }
    }

    [Test]
    public async Task canceled_test_gets_a_fresh_cleanup_token()
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var scope = new CheckpointTestScope();
        var root = scope.TempDir();
        await scope.DisposeAsync();
        canceled.IsCancellationRequested.ShouldBeTrue();
        Directory.Exists(root).ShouldBeFalse();
    }

    [Test]
    public async Task teardown_preserves_failure_and_attempts_other_roots()
    {
        string? denied = null;
        var scope = new CheckpointTestScope(beforeDelete: root =>
        {
            if (root == denied) throw new IOException("delete-denied");
        });
        denied = scope.TempDir();
        var other = scope.TempDir();
        var failure = await Should.ThrowAsync<IOException>(async () =>
            await scope.DisposeAsync(new InvalidOperationException("original-assertion")));
        failure.Message.ShouldContain("original-assertion");
        failure.Message.ShouldContain("delete-denied");
        Directory.Exists(denied).ShouldBeTrue();
        Directory.Exists(other).ShouldBeFalse();
        Directory.Delete(denied, true);
        CheckpointUsageEvent.Write("root-delete", denied);
    }

    [Test]
    public void marker_failure_rolls_back_empty_root()
    {
        string? root = null;
        var scope = new CheckpointTestScope(beforeMarker: path =>
        {
            root = path;
            throw new IOException("marker-write-failed");
        });
        Should.Throw<IOException>(() => scope.TempDir()).Message.ShouldContain("marker-write-failed");
        root.ShouldNotBeNull();
        Directory.Exists(root).ShouldBeFalse();
    }

    [Test]
    public async Task parallel_scopes_have_distinct_attempt_ownership()
    {
        await using var first = new CheckpointTestScope();
        await using var second = new CheckpointTestScope();
        var a = first.TempDir();
        var b = second.TempDir();
        a.ShouldNotBe(b);
        var markerA = JsonSerializer.Deserialize<CheckpointRootMarker>(File.ReadAllText(Path.Combine(a, CheckpointTestScope.MarkerName)))!;
        var markerB = JsonSerializer.Deserialize<CheckpointRootMarker>(File.ReadAllText(Path.Combine(b, CheckpointTestScope.MarkerName)))!;
        markerA.AttemptId.ShouldNotBe(markerB.AttemptId);
        markerA.RootId.ShouldNotBe(markerB.RootId);
    }

    [Test]
    public async Task owned_disposal_does_not_require_dead_test_host()
    {
        var scope = new CheckpointTestScope();
        var root = scope.TempDir();
        new ProcessIdentityProbe().Observe(new ProcessIdentityProbe().Current()).Verdict.ShouldBe(ProcessVerdict.AliveSame);
        await scope.DisposeAsync();
        Directory.Exists(root).ShouldBeFalse();
    }

    private sealed class UnopenedProbe : CheckpointTestBase
    {
        public string Allocate() => TempDir();
    }

    private sealed class UnknownProbe : ProcessIdentityProbe
    {
        public override ProcessObservation Observe(ProcessIdentity? expected) => new(ProcessVerdict.Unknown, "identity-unknown");
    }
}
