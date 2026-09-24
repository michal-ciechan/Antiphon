using System.Text.Json;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0660 (V-3). The daemon's own provider-auth composition (<c>AddProviderAuthProbes</c>, the
/// call <c>Program.cs</c> makes) with real probes over isolated Grok and Codex homes. Each home is
/// signed in while the other is signed out, and then the reverse, so routing Codex to Grok's probe,
/// or a Codex probe reading another provider's home, answers wrongly on one of the two passes.
/// Claude's probe spawns its CLI, so it is resolved here but never asked.
/// </summary>
[Category("Unit")]
public class CodexProviderAuthRoutingTests
{
    [Test]
    public async Task Composed_router_measures_codex_in_its_own_home_and_gates_the_launch()
    {
        var root = Path.Combine(Path.GetTempPath(), "codex-auth-routing-" + Guid.NewGuid().ToString("N"));
        var grokHome = Path.Combine(root, "grok");
        var codexHome = Path.Combine(root, "codex");
        Directory.CreateDirectory(grokHome);
        Directory.CreateDirectory(codexHome);
        var settings = new PhoneHomeSettings
        {
            Enabled = true,
            RunnerId = "routing-test",
            AllowedCwd = "/work",
            Capacity = 8,
            GrokHome = grokHome.Replace('\\', '/'),
            CodexHome = codexHome.Replace('\\', '/'),
            ClaudeHome = Path.Combine(root, "claude").Replace('\\', '/'),
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOptions<PhoneHomeSettings>>(Options.Create(settings));
        services.AddProviderAuthProbes();
        await using var provider = services.BuildServiceProvider();
        var probe = provider.GetRequiredService<IProviderAuthProbe>();
        provider.GetRequiredService<ClaudeAuthProbe>().ShouldNotBeNull();
        var runtime = new StartCountingRuntime();
        var dispatcher = new PhoneHomeCommandDispatcher(runtime, settings, probe);

        try
        {
            // Pass 1: Grok signed in, Codex signed out.
            await File.WriteAllTextAsync(Path.Combine(grokHome, "auth.json"), "{}");
            (await AuthAsync(dispatcher, "Codex")).LoggedIn.ShouldBe(false, "Codex's home has no auth.json");
            (await AuthAsync(dispatcher, "grok")).LoggedIn.ShouldBe(true, "Grok's home has one");

            var refused = await dispatcher.DispatchAsync(Launch("codex"), CancellationToken.None);
            refused.Kind.ShouldBe(PhoneHomeFrameKind.Error);
            refused.ErrorCode.ShouldBe(PhoneHomeProblemTypes.ProviderSignInRequired);
            refused.ErrorDetail.ShouldNotBeNull();
            refused.ErrorDetail.ShouldContain(settings.CodexHome);
            runtime.Starts.ShouldBe(0);

            // Pass 2: the reverse.
            File.Delete(Path.Combine(grokHome, "auth.json"));
            await File.WriteAllTextAsync(Path.Combine(codexHome, "auth.json"), "{}");
            var codex = await AuthAsync(dispatcher, "CODEX");
            codex.Provider.ShouldBe("codex");
            codex.LoggedIn.ShouldBe(true, "Codex's home now has auth.json");
            codex.AuthMethod.ShouldBe("auth_file");
            (await AuthAsync(dispatcher, "grok")).LoggedIn.ShouldBe(false, "Grok's home no longer has one");

            var admitted = await dispatcher.DispatchAsync(Launch("/usr/local/bin/codex"), CancellationToken.None);
            admitted.Kind.ShouldBe(PhoneHomeFrameKind.Result, admitted.ErrorDetail);
            runtime.Starts.ShouldBe(1);

            // A different unknown provider still has no probe.
            await Should.ThrowAsync<PhoneHomeAdmissionException>(() => probe.ProbeAsync("opencode", CancellationToken.None));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
        }
    }

    private static async Task<RunnerProviderAuthDto> AuthAsync(PhoneHomeCommandDispatcher dispatcher, string provider)
    {
        var reply = await dispatcher.DispatchAsync(
            new PhoneHomeFrame(PhoneHomeFrameKind.Request, 1, Guid.NewGuid(), PhoneHomeOperation.ProviderAuth,
                JsonSerializer.SerializeToElement(new PhoneHomeProviderAuthRequest(provider), PhoneHomeFraming.Json)),
            CancellationToken.None);
        reply.Kind.ShouldBe(PhoneHomeFrameKind.Result, $"{provider}: {reply.ErrorCode} {reply.ErrorDetail}");
        return reply.Payload!.Value.Deserialize<RunnerProviderAuthDto>(PhoneHomeFraming.Json)!;
    }

    private static PhoneHomeFrame Launch(string exe) =>
        new(PhoneHomeFrameKind.Request, 1, Guid.NewGuid(), PhoneHomeOperation.Launch,
            JsonSerializer.SerializeToElement(
                new RunnerLaunchRequest(Guid.NewGuid(), exe, [], new Dictionary<string, string>(), "/work", 80, 24)
                    with { TranscriptFormat = TranscriptFormats.Codex },
                PhoneHomeFraming.Json));

    private sealed class StartCountingRuntime : IPhoneHomeRuntimeSurface
    {
        public int Starts { get; private set; }
        public int OwnedSessionCount => Starts;
        public RunnerCapabilitiesDto Capabilities() => new("PtyHost", "inbox", "test", false, Features: []);
        public string Health() => "Healthy";
        public IReadOnlyList<RunnerSessionDto> List() => [];
        public Task<RunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) => throw new KeyNotFoundException();
        public Task<RunnerSessionDto> StartAsync(RunnerLaunchRequest request, CancellationToken ct)
        {
            Starts++;
            return Task.FromResult(new RunnerSessionDto(request.SessionId, 1, DateTime.UtcNow, "Running", null, "", 0));
        }
        public RunnerBufferDto GetBuffer(Guid sessionId) => new(sessionId, "", 0);
        public RunnerSnapshotDto GetSnapshot(Guid sessionId) => new(sessionId, "", "", 0, DateTime.UtcNow);
        public RunnerTranscriptDto GetTranscript(Guid sessionId) => new(sessionId, [], 0);
        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) => Task.CompletedTask;
        public Task<RunnerConditionalInputResult> SendConditionalInputAsync(Guid sessionId, RunnerConditionalInputRequest request, CancellationToken ct) =>
            Task.FromResult(new RunnerConditionalInputResult(sessionId, ConditionalInputOutcomes.Unsupported, null, null));
        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => Task.CompletedTask;
        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => Task.CompletedTask;
        public Task<RunnerKillGenerationResult> KillGenerationAsync(Guid sessionId, DateTime expectedAcceptedStartedAt, CancellationToken ct) =>
            Task.FromResult(new RunnerKillGenerationResult(sessionId, false, KillGenerationOutcomes.Missing, null));
    }
}
