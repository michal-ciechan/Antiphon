using System.Net.Sockets;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

[Category("Integration")]
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
        Environment.GetEnvironmentVariable(ProductionRunnerGuard.DiagnoseEnvVar)
            .ShouldBe("false",
                "CARD-0352: DiagnoseProvisioner must not start antiphon-diagnose on a Program boot in this assembly");
        Environment.GetEnvironmentVariable(ProductionRunnerGuard.HangfireServerEnabledEnvVar)
            .ShouldBe("false",
                "CARD-0298: AddHangfireServer must not run in a Program boot in this assembly");
        Environment.GetEnvironmentVariable(ProductionRunnerGuard.ZombieCensusEnabledEnvVar)
            .ShouldBe("false",
                "CARD-1222: ZombieCensus:Enabled true off Windows fails startup validation");

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
