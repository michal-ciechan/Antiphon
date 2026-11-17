using Antiphon.Server.Application.Exceptions;
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

public partial class AgentSessionRuntimeTests
{
    // C1349 lost-wakeup: the fake launch and the receipt helper both Register this adapter.
    // The second call keeps a single OnTextDelta subscription. A different adapter still conflicts.
    [Test]
    public void Same_adapter_register_is_idempotent_and_a_different_adapter_conflicts()
    {
        var logPath = Path.Combine(Path.GetTempPath(), $"antiphon-runtime-register-{Guid.NewGuid():N}");
        using var provider = BuildProvider();
        var runtime = new AgentSessionRuntime(
            new MockEventBus(),
            Options.Create(new AgentSessionSettings
            {
                SignalRMaxChunkChars = 16,
                ReplayBufferMaxChars = 16,
                SessionLogPath = logPath,
            }),
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            NullLogger<AgentSessionRuntime>.Instance);
        var sessionId = Guid.NewGuid();
        var adapter = new FakeAgentProtocolAdapter();
        try
        {
            runtime.Register(sessionId, adapter);
            runtime.Register(sessionId, adapter);
            adapter.Emit("Z");
            var snapshot = runtime.GetBufferSnapshot(sessionId);
            snapshot.Buffer.ShouldBe("Z");
            snapshot.LastSequence.ShouldBe(1);

            var conflict = Should.Throw<ConflictException>(
                () => runtime.Register(sessionId, new FakeAgentProtocolAdapter()));
            conflict.Message.ShouldContain(sessionId.ToString());

            Exception? first = null;
            Exception? second = null;
            using var gate = new Barrier(2);
            var left = Task.Run(() =>
            {
                gate.SignalAndWait();
                try { runtime.Register(sessionId, adapter); }
                catch (Exception ex) { first = ex; }
            });
            var right = Task.Run(() =>
            {
                gate.SignalAndWait();
                try { runtime.Register(sessionId, adapter); }
                catch (Exception ex) { second = ex; }
            });
            Task.WaitAll(left, right);
            first.ShouldBeNull();
            second.ShouldBeNull();
            adapter.Emit("Z");
            snapshot = runtime.GetBufferSnapshot(sessionId);
            snapshot.Buffer.ShouldBe("ZZ");
            snapshot.LastSequence.ShouldBe(2);
        }
        finally
        {
            DeleteDirectoryBestEffort(logPath);
        }
    }
}
