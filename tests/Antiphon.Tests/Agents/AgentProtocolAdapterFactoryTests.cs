using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.Pty;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;

namespace Antiphon.Tests.Agents;

[Category("Unit")]
public class AgentProtocolAdapterFactoryTests
{
    private static AgentProtocolAdapterFactory NewFactory()
        => new(Options.Create(new AgentRegistrySettings
        {
            DefaultDefinition = "claude",
            Definitions = { ["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "cl.bat" } },
        }),
        new ThrowingSessionRunnerClient());

    [Test]
    public async Task Create_returns_RunnerRawAdapter_for_Raw()
    {
        var factory = NewFactory();
        await using var adapter = factory.Create(AgentKind.Raw);
        adapter.ShouldBeOfType<RunnerRawAdapter>();
    }

    [Test]
    public async Task Create_returns_RunnerClaudeAdapter_for_ClaudeCode()
    {
        var factory = NewFactory();
        await using var adapter = factory.Create(AgentKind.ClaudeCode);
        adapter.ShouldBeOfType<RunnerClaudeAdapter>();
    }

    [Test]
    public void Create_throws_on_unmapped_kind()
    {
        var factory = NewFactory();
        Should.Throw<ArgumentOutOfRangeException>(() => factory.Create((AgentKind)999));
    }

    private sealed class ThrowingSessionRunnerClient : ISessionRunnerClient
    {
        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);

        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
