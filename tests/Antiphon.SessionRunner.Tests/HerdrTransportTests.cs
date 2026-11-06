using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[ParallelLimiter<ProcessSpawnLimit>]
public class HerdrTransportTests
{
    private static HerdrClient Client(string path, int timeout = 500) =>
        new(Options.Create(new HerdrSettings { Enabled = true, SocketPath = path, ConnectTimeoutMs = timeout }));

    [Test]
    public async Task C801_NativePing()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start(); await fake.WaitUntilListeningAsync();
        var error = await CaptureAsync(() => Client(fake.EndpointPath).ConnectAndValidateAsync(CancellationToken.None));
        error.ShouldBeNull("C801_NATIVE_PING_NO_ERROR");
        var pong = await Client(fake.EndpointPath).ConnectAndValidateAsync(CancellationToken.None);
        pong.Protocol.ShouldBe(20);
        fake.Requests.ShouldContain(r => r.GetProperty("method").GetString() == "ping");
        var disabled = new HerdrClient(Options.Create(new HerdrSettings { SocketPath = fake.EndpointPath }));
        (await CaptureAsync(() => disabled.ConnectAndValidateAsync(CancellationToken.None)))
            .ShouldBeOfType<HerdrBackendUnavailableException>("C801_DISABLED_NO_CONNECTION");
        fake.Requests.Count.ShouldBe(2);
    }

    [Test]
    public async Task C801_RequestAndSubscriptionCoexist()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start(); await fake.WaitUntilListeningAsync();
        var client = Client(fake.EndpointPath);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var events = client.SubscribeEventsAsync([new HerdrSubscription("pane.closed")], cts.Token)
            .GetAsyncEnumerator(cts.Token);
        var next = events.MoveNextAsync().AsTask();
        try
        {
            var deadline = Stopwatch.StartNew();
            while (fake.SubscriptionRecords.Count == 0 && deadline.Elapsed < TimeSpan.FromSeconds(2))
                await Task.Delay(10);
            fake.SubscriptionRecords.Count.ShouldBe(1, "subscription registration");
            using var rpcDeadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
            var rpcError = await CaptureAsync(() => client.SendRequestAsync("workspace.list", new { }, rpcDeadline.Token));
            rpcError.ShouldBeNull("C801_RPC_WHILE_SUBSCRIBED");
            fake.EnqueueEvent("pane_closed", new { pane_id = "w1:p1" });
            (await next.WaitAsync(TimeSpan.FromSeconds(2))).ShouldBeTrue("C801_EVENT_STILL_DELIVERED");
            events.Current.Name.ShouldBe("pane_closed", "C801_EVENT_STILL_DELIVERED");
        }
        finally { cts.Cancel(); }
    }

    [Test]
    public async Task C801_ConnectCancellation()
    {
        await using var endpoint = new FakeHerdrEndpoint();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        (await CaptureAsync(() => Client(endpoint.Path).ConnectAndValidateAsync(cancelled.Token))
            is OperationCanceledException).ShouldBeTrue();
    }

    [Test]
    public async Task C801_ConnectDeadline()
    {
        await using var endpoint = new FakeHerdrEndpoint();
        if (!OperatingSystem.IsWindows())
            HerdrTransport.PendingConnectOverride.Value = ct => Task.Delay(Timeout.Infinite, ct);
        try
        {
            var attempt = CaptureAsync(() => Client(endpoint.Path, timeout: 20).ConnectAndValidateAsync(CancellationToken.None));
            (await Task.WhenAny(attempt, Task.Delay(2000))).ShouldBe(attempt, "C801_CONNECT_DEADLINE_SETTLED");
            (await attempt).ShouldBeOfType<HerdrBackendUnavailableException>()
                .Message.ShouldContain(endpoint.Path);
        }
        finally { HerdrTransport.PendingConnectOverride.Value = null; }
    }

    [Test]
    public async Task C801_MissingEndpoint()
    {
        await using var endpoint = new FakeHerdrEndpoint();
        (await CaptureAsync(() => Client(endpoint.Path).ConnectAndValidateAsync(CancellationToken.None)))
            .ShouldBeOfType<HerdrBackendUnavailableException>();
    }

    [Test]
    public async Task C801_MalformedResponse()
    {
        await using var endpoint = new FakeHerdrEndpoint();
        var server = ServeOneAsync(endpoint, async (reader, writer) =>
        {
            (await reader.ReadLineAsync()).ShouldNotBeNull();
            await writer.WriteLineAsync("not json");
        });
        (await CaptureAsync(() => Client(endpoint.Path).ConnectAndValidateAsync(CancellationToken.None)))
            .ShouldBeOfType<HerdrProtocolException>();
        await server;
    }

    [Test]
    public async Task C801_PathOverridePrecedence()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start(); await fake.WaitUntilListeningAsync();
        var configured = new HerdrSettings { Enabled = true, SocketPath = "missing-socket", Session = "missing" };
        var client = new HerdrClient(configured, fake.EndpointPath);
        client.ResolveSocketPath().ShouldBe(fake.EndpointPath, "C801_CONFIGURED_ENDPOINT");
        (await client.ConnectAndValidateAsync(CancellationToken.None)).Protocol.ShouldBe(20);
        configured.SocketPath = fake.EndpointPath;
        new HerdrClient(configured).ResolveSocketPath().ShouldBe(fake.EndpointPath, "C801_CONFIGURED_ENDPOINT");
    }

    [Test]
    public void C801_NamedAndDefaultResolution()
    {
        var environment = new Dictionary<string, string?>
        {
            ["XDG_CONFIG_HOME"] = "/config/é", ["HOME"] = "/home/test"
        };
        string? Get(string key) => environment.GetValueOrDefault(key);
        string Folder(Environment.SpecialFolder _) => "/appdata";
        var settings = new HerdrSettings { Session = "named" };
        HerdrEndpointResolver.Resolve(settings, null, Get, Folder, false)
            .ShouldBe("/config/é/herdr/sessions/named/herdr.sock");
        settings.Session = "default";
        HerdrEndpointResolver.Resolve(settings, null, Get, Folder, false)
            .ShouldBe("/config/é/herdr/herdr.sock");
        settings.Session = null;
        HerdrEndpointResolver.Resolve(settings, null, Get, Folder, true)
            .ShouldBe(Path.Combine("/appdata", "herdr", "herdr.sock"));
        settings.SocketPath = "relative";
        Should.Throw<HerdrBackendUnavailableException>(() =>
            HerdrEndpointResolver.Resolve(settings, null, Get, Folder, false));
    }

    [Test]
    public void C801_UnixResolutionUsesPosixRulesAcrossHostSeparators()
    {
        HerdrEndpointResolver.IsUnixAbsolute("/config/é").ShouldBeTrue();
        HerdrEndpointResolver.IsUnixAbsolute(@"C:\config\herdr").ShouldBeFalse();
        HerdrEndpointResolver.UnixJoin("/config/é", "herdr", "sessions", "named", "herdr.sock")
            .ShouldBe("/config/é/herdr/sessions/named/herdr.sock");
        HerdrEndpointResolver.UnixJoin("/home/test", ".config", "herdr", "herdr.sock")
            .ShouldBe("/home/test/.config/herdr/herdr.sock");
        string? Get(string key) => key == "XDG_CONFIG_HOME" ? @"C:\config\herdr" :
            key == "HOME" ? "/home/test" : null;
        HerdrEndpointResolver.Resolve(new HerdrSettings { Session = "named" }, null, Get,
                _ => @"C:\Users\host\AppData", false)
            .ShouldBe("/home/test/.config/herdr/sessions/named/herdr.sock");
        HerdrEndpointResolver.Resolve(new HerdrSettings(), null, _ => null,
                _ => @"C:\Users\host\AppData", false)
            .ShouldBe("/tmp/herdr/herdr.sock");
        HerdrEndpointResolver.Resolve(new HerdrSettings(), null, _ => null,
                _ => @"C:\Users\host\AppData", false, () => "/custom/tmp/")
            .ShouldBe("/custom/tmp/herdr/herdr.sock");
    }

    [Test]
    [Arguments("/config", "/home/test", "/abs", null, "/abs/herdr.sock")]
    [Arguments("/x/cfg//", "/home/test", "named", null, "/x/cfg//herdr/sessions/named/herdr.sock")]
    [Arguments(null, "/x/home//", "named", null, "/x/home//.config/herdr/sessions/named/herdr.sock")]
    [Arguments("/config", "/home/test", "named//", null, "/config/herdr/sessions/named//herdr.sock")]
    [Arguments("/config///", "/home/test", "named", null, "/config///herdr/sessions/named/herdr.sock")]
    [Arguments("/config//", "/home/test", "named", "/override//herdr.sock", "/config//herdr/sessions/named/herdr.sock")]
    public void C801_UnixResolutionPreservesPosixCombineSemantics(
        string? xdg, string? home, string session, string? socketOverride, string expected)
    {
        string? Get(string key) => key switch
        {
            "XDG_CONFIG_HOME" => xdg,
            "HOME" => home,
            _ => null
        };
        var settings = new HerdrSettings { Session = session };
        var resolved = HerdrEndpointResolver.Resolve(settings, null, Get, _ => "/appdata", false);
        resolved.ShouldBe(expected);
        if (socketOverride is not null)
            HerdrEndpointResolver.Resolve(settings, socketOverride, Get, _ => "/appdata", false)
                .ShouldBe(socketOverride);
    }

    [Test, Category("Integration")]
    public async Task C801_ConnectedPeerIdentity()
    {
        await using var endpoint = new FakeHerdrEndpoint();
        await using (var child = await HerdrTestProcess.StartAsync(endpoint.Path))
        {
            var client = Client(endpoint.Path);
            var first = await client.ConnectAndValidateAsync(CancellationToken.None);
            var second = await client.ConnectAndValidateAsync(CancellationToken.None);
            first.InstanceId.ShouldBe(child.Identity, "C801_PEER_IS_CHILD");
            second.InstanceId.ShouldBe(first.InstanceId, "C801_SAME_PEER_STABLE");
            using var parent = Process.GetCurrentProcess();
            first.InstanceId.ShouldNotBe($"{parent.Id}:{parent.StartTime.ToUniversalTime().Ticks}");
        }
        if (!OperatingSystem.IsWindows()) File.Delete(endpoint.Path);
    }

    [Test, Category("Integration")]
    public async Task C801_DifferentPeerIdentityAfterRestart()
    {
        await using var endpoint = new FakeHerdrEndpoint();
        string? firstIdentity;
        await using (var first = await HerdrTestProcess.StartAsync(endpoint.Path))
            firstIdentity = (await Client(endpoint.Path).ConnectAndValidateAsync(CancellationToken.None)).InstanceId;
        if (!OperatingSystem.IsWindows()) File.Delete(endpoint.Path);
        await using (var second = await HerdrTestProcess.StartAsync(endpoint.Path))
        {
            var replaced = (await Client(endpoint.Path).ConnectAndValidateAsync(CancellationToken.None)).InstanceId;
            replaced.ShouldBe(second.Identity, "C801_PEER_IS_CHILD");
            replaced.ShouldNotBe(firstIdentity, "C801_REPLACED_PEER_DIFFERS");
        }
        if (!OperatingSystem.IsWindows()) File.Delete(endpoint.Path);
    }

    [Test]
    public async Task C801_UnavailableIdentityRefusesDisposal()
    {
        await using var fixture = new HerdrPaneDisposalFixture();
        await fixture.StartAsync();
        var lookupCalls = 0;
        if (OperatingSystem.IsWindows()) HerdrPeerIdentity.ForceUnavailable.Value = true;
        else HerdrPeerIdentity.NativePidOverride.Value = _ => { Interlocked.Increment(ref lookupCalls); return null; };
        try
        {
            var refusal = await Should.ThrowAsync<HerdrLaunchException>(() =>
                fixture.Backend.InspectAsync(fixture.PaneId, true, CancellationToken.None));
            refusal.Code.ShouldBe(HerdrPaneDisposalCodes.GuardUnavailable,
                "C801_IDENTITY_UNAVAILABLE_REFUSES");
            fixture.Methods.Count(m => m == "pane.close").ShouldBe(0,
                "C801_UNVERIFIED_PANE_NOT_CLOSED");
            if (!OperatingSystem.IsWindows())
                lookupCalls.ShouldBeGreaterThan(0, "C801_NATIVE_IDENTITY_LOOKUP_ATTEMPTED");
        }
        finally
        {
            HerdrPeerIdentity.NativePidOverride.Value = null;
            HerdrPeerIdentity.ForceUnavailable.Value = false;
        }
    }

    [Test]
    public async Task C801_RepeatedConnectDisposeReleasesHandles() =>
        await AssertRepeatedConnectDisposeReleasesHandlesAsync();

    [Test]
    public async Task C801_RepeatedConnectDisposeIgnoresUnrelatedHandles()
    {
        var unrelated = new List<FileStream>();
        try
        {
            await AssertRepeatedConnectDisposeReleasesHandlesAsync(() =>
            {
                if (unrelated.Count != 0) return;
                // Model another test opening files after this test's baseline. Keep them
                // alive through all connections: none belongs to the Herdr transport.
                for (var i = 0; i < 32; i++)
                    unrelated.Add(File.OpenRead(typeof(HerdrTransportTests).Assembly.Location));
            });
        }
        finally
        {
            foreach (var file in unrelated) file.Dispose();
        }
    }

    [Test]
    public async Task C801_ClientClosesEachRequestConnection()
    {
        await using var endpoint = new FakeHerdrEndpoint();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        for (var i = 0; i < 20; i++)
        {
            var server = ServeOneAsync(endpoint, async (reader, writer) =>
            {
                using var request = JsonDocument.Parse((await reader.ReadLineAsync(deadline.Token))!);
                var id = request.RootElement.GetProperty("id").GetString();
                await writer.WriteLineAsync(JsonSerializer.Serialize(new
                {
                    id, result = new { type = "pong", version = "0.8.2", protocol = 20 }
                }).AsMemory(), deadline.Token);
                // Keep the peer open: EOF must come from the client's disposal, rather
                // than the fake server closing its normal request connection first.
                (await reader.ReadAsync(new char[1], deadline.Token)).ShouldBe(0,
                    $"client must close its request connection, cycle={i}");
            });
            var client = Client(endpoint.Path).ConnectAndValidateAsync(deadline.Token);
            await Task.WhenAll(server, client);
            (await client).Protocol.ShouldBe(20);
        }
    }

    private static async Task AssertRepeatedConnectDisposeReleasesHandlesAsync(Action? duringConnections = null)
    {
        await using var fake = new FakeHerdrServer();
        fake.BeforeRequest = _ => duringConnections?.Invoke();
        fake.Start(); await fake.WaitUntilListeningAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        for (var i = 0; i < 20; i++)
        {
            await using var connection = await HerdrTransport.ConnectAsync(fake.EndpointPath, 500, deadline.Token);
            SafeHandle handle = connection.Stream switch
            {
                PipeStream pipe => pipe.SafePipeHandle,
                NetworkStream network => network.Socket.SafeHandle,
                _ => throw new InvalidOperationException("Expected a native Herdr pipe or socket.")
            };
            handle.IsClosed.ShouldBeFalse($"native handle must be open before disposal, cycle={i}");
            using (var reader = new StreamReader(connection.Stream, Encoding.UTF8, leaveOpen: true))
            using (var writer = new StreamWriter(connection.Stream, new UTF8Encoding(false), leaveOpen: true)
                   { AutoFlush = true })
            {
                await writer.WriteLineAsync("""{"id":"handle-check","method":"ping","params":{}}""".AsMemory(), deadline.Token);
                using var response = JsonDocument.Parse((await reader.ReadLineAsync(deadline.Token))!);
                response.RootElement.GetProperty("result").GetProperty("protocol").GetInt32().ShouldBe(20);
            }
            // Check this connection's handle immediately, while it is still strongly held.
            // Other tests and finalizers cannot satisfy or invalidate this assertion.
            await connection.DisposeAsync();
            handle.IsClosed.ShouldBeTrue($"native handle survived explicit disposal, cycle={i}");
        }
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try { await action(); return null; }
        catch (Exception ex) { return ex; }
    }

    private static async Task ServeOneAsync(FakeHerdrEndpoint endpoint,
        Func<StreamReader, StreamWriter, Task> handler)
    {
        await using var listener = new FakeHerdrTransport(endpoint);
        listener.Bind();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var stream = await listener.AcceptAsync(timeout.Token, () => { });
        using var reader = new StreamReader(stream, new UTF8Encoding(false), leaveOpen: true);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        await handler(reader, writer);
    }
}
