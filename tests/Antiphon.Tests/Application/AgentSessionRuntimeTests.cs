using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public class AgentSessionRuntimeTests
{
    [Test]
    public async Task SignalR_AgentTextDelta_routes_to_session_group_only_and_chunks_output()
    {
        var eventBus = new MockEventBus();
        var logPath = Path.Combine(Path.GetTempPath(), $"antiphon-runtime-tests-{Guid.NewGuid():N}");
        await using var provider = BuildProvider();
        var runtime = new AgentSessionRuntime(
            eventBus,
            Options.Create(new AgentSessionSettings
            {
                SignalRMaxChunkChars = 4,
                ReplayBufferMaxChars = 8,
                SessionLogPath = logPath
            }),
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            NullLogger<AgentSessionRuntime>.Instance);
        var sessionId = Guid.NewGuid();
        var otherSessionId = Guid.NewGuid();
        var adapter = new FakeAgentProtocolAdapter();
        runtime.Register(sessionId, adapter);
        runtime.Register(otherSessionId, new FakeAgentProtocolAdapter());

        adapter.Emit("ABCDEFGHIJ");

        await WaitUntilAsync(() => eventBus.PublishedEvents.Count >= 3);

        var events = eventBus.PublishedEvents;
        events.Count.ShouldBe(3);
        events.Select(e => e.Group).ShouldAllBe(g => g == AgentSessionGroups.Session(sessionId));
        events.Select(e => e.EventName).ShouldAllBe(e => e == "AgentTextDelta");
        runtime.GetBufferSnapshot(sessionId).Buffer.ShouldBe("ABCDEFGHIJ");
        runtime.GetBufferSnapshot(sessionId).LastSequence.ShouldBe(3);
        GetPayloadValue<long>(events[0].Payload, "sequence").ShouldBe(1);
        GetPayloadValue<long>(events[1].Payload, "sequence").ShouldBe(2);
        GetPayloadValue<long>(events[2].Payload, "sequence").ShouldBe(3);
        GetPayloadValue<string>(events[0].Payload, "text").ShouldBe("ABCD");
        GetPayloadValue<string>(events[2].Payload, "text").ShouldBe("IJ");

        Directory.Delete(logPath, recursive: true);
    }

    [Test]
    public async Task Replay_buffer_returns_full_session_log_after_runtime_restart()
    {
        var eventBus = new MockEventBus();
        var logPath = Path.Combine(Path.GetTempPath(), $"antiphon-runtime-tests-{Guid.NewGuid():N}");
        var sessionId = Guid.NewGuid();

        await using (var provider = BuildProvider())
        {
            var runtime = new AgentSessionRuntime(
                eventBus,
                Options.Create(new AgentSessionSettings
                {
                    SignalRMaxChunkChars = 16,
                    ReplayBufferMaxChars = 10,
                    SessionLogPath = logPath
                }),
                provider.GetRequiredService<IServiceScopeFactory>(),
                TimeProvider.System,
                NullLogger<AgentSessionRuntime>.Instance);
            var adapter = new FakeAgentProtocolAdapter();
            runtime.Register(sessionId, adapter);

            adapter.Emit("0123456789ABCDE");
            await WaitUntilAsync(() => eventBus.PublishedEvents.Count >= 1);
            await runtime.DisposeSessionAsync(sessionId);
        }

        await using (var provider = BuildProvider())
        {
            var restartedRuntime = new AgentSessionRuntime(
                eventBus,
                Options.Create(new AgentSessionSettings
                {
                    SignalRMaxChunkChars = 16,
                    ReplayBufferMaxChars = 10,
                    SessionLogPath = logPath
                }),
                provider.GetRequiredService<IServiceScopeFactory>(),
                TimeProvider.System,
                NullLogger<AgentSessionRuntime>.Instance);

            restartedRuntime.GetBufferSnapshot(sessionId).Buffer.ShouldBe("0123456789ABCDE");
            restartedRuntime.GetBufferSnapshot(sessionId).LastSequence.ShouldBe(0);
        }

        Directory.Delete(logPath, recursive: true);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
                return;

            await Task.Delay(25);
        }

        predicate().ShouldBeTrue();
    }

    private static T GetPayloadValue<T>(object payload, string propertyName)
    {
        var value = payload.GetType().GetProperty(propertyName)!.GetValue(payload);
        return value.ShouldBeOfType<T>();
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        return services.BuildServiceProvider();
    }
}
