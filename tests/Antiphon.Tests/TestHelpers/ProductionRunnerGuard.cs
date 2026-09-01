using System.Net.Sockets;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0204: no test host in this assembly may reach the always-on production session-runner.
///
/// <para>Every <c>WebApplicationFactory&lt;Program&gt;</c> boots the REAL <c>Program</c> — hosted
/// services included — on top of the real <c>server/appsettings.json</c>, which names
/// <c>SessionRunner:BaseUrl = http://localhost:17204</c> and
/// <c>Delegation:CheckInterpreterWorkingDirectory = C:\logs\antiphon\check-interpreter</c>.
/// <c>AgentTaskCheckHostedService</c> then calls <c>CheckInterpreterProvisioner.EnsureAsync</c> at
/// startup, which creates the AlwaysOn check-interpreter agent and starts it IMMEDIATELY through
/// that URL. With <c>AntiphonWebAppFactory</c>'s <c>test-raw</c> definition the exe is
/// <c>cmd.exe</c>, so each boot left a detached <c>Antiphon.PtyHost</c> holding an interactive
/// <c>cmd.exe</c> on the production runner — a child that never exits, so the host's 24 h linger
/// clock never starts — while the owning <c>AgentSessions</c> row lived in a throwaway test schema
/// and vanished at dispose. Measured 2026-08-25: 187 such hosts, ~4 GB resident, one per factory
/// boot, reproduced deterministically by a single <c>HealthEndpointTests</c> run.</para>
///
/// <para>This guard is the assembly-wide belt: environment variables outrank <c>appsettings.json</c>
/// in the default configuration order, so every <c>Program</c> boot in this process — including
/// hosts that build a bare <c>WebApplicationFactory&lt;Program&gt;</c> without going through
/// <c>AntiphonWebAppFactory</c> (<c>SmokeTests</c>) — sees a dead runner URL and a disabled check
/// interpreter unless it explicitly configures otherwise. <c>AntiphonWebAppFactory</c> adds the
/// braces: the same values in-memory plus a refusing <see cref="ISessionRunnerClient"/>, so a
/// launch attempt inside a factory host is a loud exception naming this card, never a process.</para>
///
/// <para>Deliberately NOT a change to production code: the production runner does nothing wrong by
/// launching what it is asked to launch, and the reconciler's CARD-0056 rule that "unclaimed never
/// implies kill" is the reason these were never reaped automatically. The fix is that the tests
/// stop asking.</para>
/// </summary>
public class ProductionRunnerGuard
{
    /// <summary>
    /// A loopback port nothing listens on. Port 1 is reserved (tcpmux) and refuses instantly on
    /// Windows, so a code path that ignores the fake client and builds its own HttpClient fails in
    /// milliseconds with a connection error rather than hanging for a request timeout.
    /// </summary>
    public const string DeadRunnerBaseUrl = "http://127.0.0.1:1";

    public const string BaseUrlEnvVar = "SessionRunner__BaseUrl";
    public const string CheckInterpreterEnvVar = "Delegation__CheckInterpreterEnabled";
    public const string HangfireServerEnabledEnvVar = "Hangfire__ServerEnabled";

    /// <summary>What this process inherited, kept so the log can name it.</summary>
    public static string? InheritedBaseUrl { get; private set; }

    [Before(Assembly)]
    public static void PointEveryProgramBootAwayFromTheProductionRunner()
    {
        InheritedBaseUrl = Environment.GetEnvironmentVariable(BaseUrlEnvVar);
        Environment.SetEnvironmentVariable(BaseUrlEnvVar, DeadRunnerBaseUrl);
        Environment.SetEnvironmentVariable(CheckInterpreterEnvVar, "false");
        Environment.SetEnvironmentVariable(HangfireServerEnabledEnvVar, "false");
        Console.WriteLine(
            $"[CARD-0204] {BaseUrlEnvVar}={DeadRunnerBaseUrl} and {CheckInterpreterEnvVar}=false for "
            + "every Program boot in this assembly (inherited "
            + $"{BaseUrlEnvVar}='{InheritedBaseUrl ?? "<unset>"}') - test hosts never launch on the "
            + "production session-runner.");
        Console.WriteLine(
            $"[CARD-0298] {HangfireServerEnabledEnvVar}=false for every Program boot in this assembly "
            + "- test hosts never start a Hangfire worker (no WMI census, no production-runner list).");
    }
}

/// <summary>
/// The <see cref="ISessionRunnerClient"/> a factory host gets instead of the HTTP client. Lists
/// nothing, streams nothing, and REFUSES to start a session — recording the attempt so a test can
/// assert that booting the host launched nothing anywhere.
/// </summary>
public sealed class RefusingSessionRunnerClient : ISessionRunnerClient
{
    private readonly List<(Guid SessionId, AgentLaunchSpec Spec)> _launchAttempts = [];

    /// <summary>Every <see cref="StartAsync"/> call this host made, in order.</summary>
    public IReadOnlyList<(Guid SessionId, AgentLaunchSpec Spec)> LaunchAttempts
    {
        get { lock (_launchAttempts) return _launchAttempts.ToList(); }
    }

    private int _listCalls;

    public int ListCalls
    {
        get { lock (_launchAttempts) return _listCalls; }
    }

    public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct)
    {
        lock (_launchAttempts) _listCalls++;
        return Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);
    }

    public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct)
    {
        lock (_launchAttempts)
            _launchAttempts.Add((sessionId, spec));
        throw new InvalidOperationException(
            $"CARD-0204: a test host tried to start session {sessionId} ({spec.Exe} in {spec.Cwd}) "
            + "on a session-runner. AntiphonWebAppFactory hosts never launch real processes; a test "
            + "that needs a live session uses DirectSessionRunnerClient or the E2E fixture's "
            + "isolated runner.");
    }

    public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
        throw new NotSupportedException(Reason);

    public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
        throw new NotSupportedException(Reason);

    public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
        throw new NotSupportedException(Reason);

    public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
        throw new NotSupportedException(Reason);

    public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) =>
        throw new NotSupportedException(Reason);

    public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) =>
        throw new NotSupportedException(Reason);

    public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) =>
        throw new NotSupportedException(Reason);

    public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) =>
        throw new NotSupportedException(Reason);

    public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        // An empty stream, not an exception: the event pump is switched off in the factory, but a
        // pump that does run must see "connected, nothing happening" rather than log an error a
        // second forever.
        await Task.CompletedTask;
        yield break;
    }

    private const string Reason =
        "CARD-0204: AntiphonWebAppFactory hosts have no session-runner; this host never started a session.";
}

/// <summary>
/// CARD-0298: factory hosts must not enumerate Win32_Process. A call is a loud exception so a
/// test that re-enables the Hangfire worker cannot silently WMI-scan the developer machine.
/// </summary>
public sealed class RefusingZombieProcessCensus : IZombieProcessCensus
{
    private int _calls;

    public int Calls => _calls;

    public Task<IReadOnlyList<ZombieOsProcess>> SnapshotAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        throw new InvalidOperationException(
            "CARD-0298: a test host tried to run the OS zombie census. AntiphonWebAppFactory "
            + "hosts never enumerate Win32_Process or call the production session-runner.");
    }
}

public class ProductionRunnerGuardTests
{
    /// <summary>The pin on the assembly guard: it has run before any test, and the port it chose is dead.</summary>
    [Test]
    public async Task Every_program_boot_in_this_assembly_is_pointed_at_a_dead_runner()
    {
        Environment.GetEnvironmentVariable(ProductionRunnerGuard.BaseUrlEnvVar)
            .ShouldBe(ProductionRunnerGuard.DeadRunnerBaseUrl,
                "the [Before(Assembly)] guard must override SessionRunner:BaseUrl before any Program boots "
                + $"(this process inherited '{ProductionRunnerGuard.InheritedBaseUrl ?? "<unset>"}')");
        Environment.GetEnvironmentVariable(ProductionRunnerGuard.CheckInterpreterEnvVar)
            .ShouldBe("false");
        Environment.GetEnvironmentVariable(ProductionRunnerGuard.HangfireServerEnabledEnvVar)
            .ShouldBe("false",
                "CARD-0298: AddHangfireServer must not run in a Program boot in this assembly");

        var uri = new Uri(ProductionRunnerGuard.DeadRunnerBaseUrl);
        using var socket = new TcpClient();
        var connect = socket.ConnectAsync(uri.Host, uri.Port);
        var finished = await Task.WhenAny(connect, Task.Delay(TimeSpan.FromSeconds(5)));
        finished.ShouldBeSameAs(connect, "a refused connection must fail fast, not hang");
        await Should.ThrowAsync<SocketException>(connect,
            $"nothing may be listening on {uri} - it is the address every test host is sent to");
    }

    /// <summary>A launch attempt through the fake is an exception that names the card, and is recorded.</summary>
    [Test]
    public async Task The_refusing_client_records_and_refuses_a_launch()
    {
        var client = new RefusingSessionRunnerClient();
        var id = Guid.NewGuid();
        var spec = new AgentLaunchSpec(
            DefinitionName: "test-raw", Kind: AgentKind.Raw, Exe: "cmd.exe", Args: [],
            Env: new Dictionary<string, string>(), Cwd: @"C:\nowhere", Cols: 120, Rows: 30);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => client.StartAsync(id, spec, CancellationToken.None));

        ex.Message.ShouldContain("CARD-0204");
        client.LaunchAttempts.ShouldHaveSingleItem().SessionId.ShouldBe(id);
        (await client.ListAsync(CancellationToken.None)).ShouldBeEmpty();
    }
}
