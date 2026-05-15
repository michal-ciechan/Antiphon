using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.Pty;

namespace Antiphon.Tests.Agents;

[Category("Unit")]
public class AgentProtocolAdapterFactoryTests
{
    private static AgentProtocolAdapterFactory NewFactory()
        => new(Options.Create(new AgentRegistrySettings
        {
            DefaultDefinition = "claude",
            Definitions = { ["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "cl.bat" } },
        }));

    [Test]
    public async Task Create_returns_RawPtyAdapter_for_Raw()
    {
        var factory = NewFactory();
        await using var adapter = factory.Create(AgentKind.Raw);
        adapter.ShouldBeOfType<RawPtyAdapter>();
    }

    [Test]
    public async Task Create_returns_ClaudeAdapter_for_ClaudeCode()
    {
        var factory = NewFactory();
        await using var adapter = factory.Create(AgentKind.ClaudeCode);
        adapter.ShouldBeOfType<ClaudeAdapter>();
    }

    [Test]
    public void Create_throws_on_unmapped_kind()
    {
        var factory = NewFactory();
        Should.Throw<ArgumentOutOfRangeException>(() => factory.Create((AgentKind)999));
    }
}
