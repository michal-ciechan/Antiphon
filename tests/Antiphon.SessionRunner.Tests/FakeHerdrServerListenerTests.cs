using System.Net.Sockets;
using System.Text;
using Antiphon.SessionRunner;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[ParallelLimiter<ProcessSpawnLimit>]
public class FakeHerdrServerListenerTests
{
    private static async Task<Exception?> ErrorWithinAsync(Task task, string assertion)
    {
        Task.WhenAny(task, Task.Delay(2000)).GetAwaiter().GetResult().ShouldBe(task, assertion);
        try { await task; return null; }
        catch (Exception ex) { return ex; }
    }

    private static HerdrClient Client(FakeHerdrServer fake) => new(new HerdrSettings
    {
        Enabled = true, SocketPath = fake.EndpointPath, ConnectTimeoutMs = 500
    });

    [Test]
    public async Task C801_StartupFailureFaultsAllWaiters()
    {
        var original = new SocketException((int)SocketError.AddressAlreadyInUse);
        await using var fake = new FakeHerdrServer(transportFactory: endpoint =>
            new FakeHerdrTransport(endpoint) { BindFailure = original });
        var first = fake.WaitUntilListeningAsync();
        var second = fake.WaitUntilListeningAsync();
        fake.Start();
        var loopError = await ErrorWithinAsync(fake.LoopCompletion, "C801_ALL_WAITERS_SETTLED");
        var firstError = await ErrorWithinAsync(first, "C801_ALL_WAITERS_SETTLED");
        var secondError = await ErrorWithinAsync(second, "C801_ALL_WAITERS_SETTLED");
        firstError.ShouldBeSameAs(original, "C801_START_ORIGINAL_CAUSE");
        secondError.ShouldBeSameAs(original, "C801_START_ORIGINAL_CAUSE");
        loopError.ShouldBeSameAs(original, "C801_START_ORIGINAL_CAUSE");
        fake.ListenerFault.ShouldBeSameAs(original, "C801_START_ORIGINAL_CAUSE");
    }

    [Test]
    public async Task C801_AcceptFailureFaultsLaterWaiters()
    {
        var original = new SocketException((int)SocketError.ConnectionAborted);
        await using var fake = new FakeHerdrServer(transportFactory: endpoint =>
            new FakeHerdrTransport(endpoint) { AcceptFailure = original });
        fake.Start();
        (await ErrorWithinAsync(fake.LoopCompletion, "C801_ALL_WAITERS_SETTLED")).ShouldBeSameAs(original);
        (await ErrorWithinAsync(fake.WaitUntilListeningAsync(), "C801_ALL_WAITERS_SETTLED"))
            .ShouldBeSameAs(original, "C801_START_ORIGINAL_CAUSE");
        fake.ListenerFault.ShouldBeSameAs(original);
    }

    [Test]
    public async Task C801_UnawaitedFailureIsObserved()
    {
        var original = new IOException("terminal bind");
        await using var fake = new FakeHerdrServer(transportFactory: endpoint =>
            new FakeHerdrTransport(endpoint) { BindFailure = original });
        fake.Start();
        (await ErrorWithinAsync(fake.LoopCompletion, "C801_ALL_WAITERS_SETTLED")).ShouldBeSameAs(original);
        fake.ListenerFault.ShouldBeSameAs(original);
    }

    [Test]
    public async Task C801_DefaultReadinessDeadline()
    {
        var clock = new FakeTimeProvider();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fake = new FakeHerdrServer(timeProvider: clock) { StartGate = gate.Task };
        var readiness = fake.WaitUntilListeningAsync();
        fake.Start();
        try
        {
            clock.Advance(TimeSpan.FromMilliseconds(4999));
            readiness.IsCompleted.ShouldBeFalse("C801_DEFAULT_DEADLINE_SETTLED");
            clock.Advance(TimeSpan.FromMilliseconds(1));
            (await ErrorWithinAsync(readiness, "C801_DEFAULT_DEADLINE_SETTLED"))
                .ShouldBeOfType<TimeoutException>("C801_DEFAULT_DEADLINE_ERROR")
                .Message.ShouldContain(fake.EndpointPath);
        }
        finally { gate.TrySetResult(); }
    }

    [Test]
    public async Task C801_CallerCancellationDoesNotStopListener()
    {
        await using var fake = new FakeHerdrServer();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        (await ErrorWithinAsync(fake.WaitUntilListeningAsync(cancelled.Token), "caller cancellation")
            is OperationCanceledException).ShouldBeTrue();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        (await Client(fake).ConnectAndValidateAsync(CancellationToken.None)).Protocol.ShouldBe(20);
    }

    [Test]
    public async Task C801_DisposeBeforeReadySettlesWait()
    {
        var fake = new FakeHerdrServer();
        var wait = fake.WaitUntilListeningAsync();
        await fake.DisposeAsync();
        (await ErrorWithinAsync(wait, "dispose settles readiness"))
            .ShouldBeOfType<TaskCanceledException>();
    }

    [Test]
    public async Task C801_ParallelEndpointsAreIsolated()
    {
        await using var first = new FakeHerdrServer();
        await using var second = new FakeHerdrServer();
        first.EndpointPath.ShouldNotBe(second.EndpointPath);
        first.Start(); second.Start();
        await Task.WhenAll(first.WaitUntilListeningAsync(), second.WaitUntilListeningAsync());
        await Task.WhenAll(Client(first).ConnectAndValidateAsync(CancellationToken.None),
            Client(second).ConnectAndValidateAsync(CancellationToken.None));
        first.Requests.Count.ShouldBe(1);
        second.Requests.Count.ShouldBe(1);
    }

    [Test]
    public async Task C801_EndpointLengthAndPermissions()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start(); await fake.WaitUntilListeningAsync();
        if (!OperatingSystem.IsWindows())
        {
            (Encoding.UTF8.GetByteCount(fake.EndpointPath) + 1 < 104)
                .ShouldBeTrue("C801_SHORT_NATIVE_ENDPOINT");
            var directory = Path.GetDirectoryName(fake.EndpointPath)!;
            (File.GetUnixFileMode(directory) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite |
                UnixFileMode.OtherRead | UnixFileMode.OtherWrite)).ShouldBe(UnixFileMode.None, "C801_PRIVATE_DIRECTORY");
            (File.GetUnixFileMode(fake.EndpointPath) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite |
                UnixFileMode.OtherRead | UnixFileMode.OtherWrite)).ShouldBe(UnixFileMode.None, "C801_PRIVATE_SOCKET");
        }
        (await Client(fake).ConnectAndValidateAsync(CancellationToken.None)).Protocol.ShouldBe(20);
    }

    [Test]
    public async Task C801_BindCollisionDoesNotDeleteOwner()
    {
        await using var endpoint = new FakeHerdrEndpoint();
        await using (var owner = new FakeHerdrServer(endpoint: endpoint))
        {
            owner.Start(); await owner.WaitUntilListeningAsync();
            NativeFileIdentity.TryRead(endpoint.Path, out var original).ShouldBeTrue();
            await using (var contender = new FakeHerdrServer(endpoint: endpoint))
            {
                contender.Start();
                (await ErrorWithinAsync(contender.LoopCompletion, "collision must terminate"))
                    .ShouldBeOfType<IOException>();
            }
            NativeFileIdentity.TryRead(endpoint.Path, out var after).ShouldBeTrue();
            after.ShouldBe(original, "C801_OWNER_ENDPOINT_PRESERVED");
            (await Client(owner).ConnectAndValidateAsync(CancellationToken.None)).Protocol.ShouldBe(20);
            if (!OperatingSystem.IsWindows())
            {
                using var collision = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                Should.Throw<SocketException>(() => collision.Bind(new UnixDomainSocketEndPoint(endpoint.Path)));
                File.Delete(endpoint.Path);
                File.WriteAllText(endpoint.Path, "replacement-owned-by-test");
                NativeFileIdentity.TryRead(endpoint.Path, out var replacement).ShouldBeTrue();
                replacement.ShouldNotBe(original, "C801_REPLACEMENT_PRESERVED");
            }
        }
        if (!OperatingSystem.IsWindows())
        {
            File.ReadAllText(endpoint.Path).ShouldBe("replacement-owned-by-test", "C801_REPLACEMENT_PRESERVED");
            File.Delete(endpoint.Path);
        }
    }

    [Test]
    public async Task C801_DisposeAllowsLeaseRebind()
    {
        await using var endpoint = new FakeHerdrEndpoint();
        await using (var first = new FakeHerdrServer(endpoint: endpoint))
        {
            first.Start(); await first.WaitUntilListeningAsync();
            (await Client(first).ConnectAndValidateAsync(CancellationToken.None)).Protocol.ShouldBe(20);
        }
        if (!OperatingSystem.IsWindows())
            NativeFileIdentity.TryRead(endpoint.Path, out _).ShouldBeFalse("C801_OWNED_SOCKET_REMOVED");
        await using (var second = new FakeHerdrServer(endpoint: endpoint))
        {
            second.Start(); await second.WaitUntilListeningAsync();
            (await Client(second).ConnectAndValidateAsync(CancellationToken.None)).Protocol.ShouldBe(20);
        }
    }

    [Test, Category("Integration")]
    public async Task C801_CrashedOwnerLeaseReclaimed()
    {
        var assembly = typeof(FakeHerdrServer).Assembly.Location;
        string endpointPath;
        await using (var child = await HerdrTestProcess.StartOwnedFixtureAsync(assembly))
        {
            endpointPath = child.EndpointPath!;
            var client = new HerdrClient(new HerdrSettings { Enabled = true, SocketPath = endpointPath });
            (await client.ConnectAndValidateAsync(CancellationToken.None)).InstanceId.ShouldBe(child.Identity);
        }
        if (!OperatingSystem.IsWindows())
        {
            NativeFileIdentity.TryRead(endpointPath, out _).ShouldBeTrue();
            FakeHerdrEndpoint.ReclaimDeadLeases();
            NativeFileIdentity.TryRead(endpointPath, out _).ShouldBeFalse("C801_CRASH_RESIDUE_REMOVED");
            Directory.Exists(Path.GetDirectoryName(endpointPath)!).ShouldBeFalse("C801_CRASH_RESIDUE_REMOVED");
        }
        await using var fake = new FakeHerdrServer();
        fake.Start(); await fake.WaitUntilListeningAsync();
        (await Client(fake).ConnectAndValidateAsync(CancellationToken.None)).Protocol.ShouldBe(20);
    }

    [Test]
    public async Task C801_LiveOrForeignLeasePreserved()
    {
        await using var owner = new FakeHerdrServer();
        owner.Start(); await owner.WaitUntilListeningAsync();
        var path = owner.EndpointPath;
        var scratch = Path.Combine(Path.GetTempPath(), $"ah-foreign-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratch);
        var sentinel = Path.Combine(scratch, "sentinel");
        try
        {
            File.WriteAllText(sentinel, "foreign");
            FakeHerdrEndpoint.ReclaimDeadLeases();
            File.ReadAllText(sentinel).ShouldBe("foreign", "C801_FOREIGN_LEASE_PRESERVED");
            (await Client(owner).ConnectAndValidateAsync(CancellationToken.None)).Protocol.ShouldBe(20);
            if (!OperatingSystem.IsWindows())
                NativeFileIdentity.TryRead(path, out _).ShouldBeTrue("C801_FOREIGN_LEASE_PRESERVED");
        }
        finally { File.Delete(sentinel); Directory.Delete(scratch); }
    }
}
