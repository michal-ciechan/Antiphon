using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class GrokRulesStoreFailureTests
{
    [Test]
    [Arguments(false, "temp_write")]
    [Arguments(true, "temp_write")]
    [Arguments(false, "replace")]
    [Arguments(true, "replace")]
    [Arguments(false, "metadata")]
    [Arguments(true, "metadata")]
    public async Task Storage_failure_refuses_before_any_child_or_pane_effect(bool herdr, string operation)
    {
        var root = TestSessionLogRoot.Create("card0395-write-fault");
        var settings = new SessionRunnerSettings { SessionLogPath = root, PtyHostSourceDir = Path.Combine(root, "missing-host") };
        var id = Guid.NewGuid();
        var file = new GrokRulesFileStore(root, new()).PathFor(id);
        if (operation == "temp_write")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetDirectoryName(file)!)!);
            await File.WriteAllTextAsync(Path.GetDirectoryName(file)!, "block directory creation");
        }
        else if (operation == "replace") Directory.CreateDirectory(file);
        else Directory.CreateDirectory(herdr ? HerdrPaneSidecar.PathFor(root, id) : PtyHostManifest.PathFor(settings.PtyHostManifestDir, id));
        await using var fake = new FakeHerdrServer { LaunchScriptAgentKind = HerdrAgentKinds.Grok };
        fake.Start();
        await fake.WaitUntilListeningAsync();
        await using var runtime = new SessionRunnerRuntime(Options.Create(settings), NullLogger<SessionRunnerRuntime>.Instance,
            new HerdrClient(new HerdrSettings { Enabled = true, Session = fake.Session }), new SystemProcessLivenessProbe());
        var request = new RunnerLaunchRequest(id, "grok", [], new Dictionary<string,string>(), root, 120, 30,
            TranscriptFormat: TranscriptFormats.Grok, GrokRulesPayload: new("private body sentinel\r\nlast line", 1, Guid.NewGuid()),
            Backend: herdr ? SessionBackends.Herdr : null,
            Herdr: herdr ? new HerdrLaunchOptions("card0395-" + id.ToString("N"), "rules", root, "rules", AgentKind: HerdrAgentKinds.Grok) : null);
        Exception? failure = null;
        try { await runtime.StartAsync(request, CancellationToken.None); }
        catch (Exception ex) { failure = ex; }
        try
        {
            fake.Requests.ShouldBeEmpty("storage failures must precede allocation, detection, and typing");
            if (!herdr && operation != "metadata")
                File.Exists(PtyHostManifest.PathFor(settings.PtyHostManifestDir, id)).ShouldBeFalse("the host launch boundary must not be reached");
            runtime.List().ShouldBeEmpty();
            var error = failure.ShouldBeOfType<GrokRulesTransportException>();
            error.Code.ShouldBe("grok_rules_file_write_failed");
            error.Reason.ShouldBe(operation);
            error.Message.ShouldNotContain("private body sentinel");
            Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories).ShouldBeEmpty();
        }
        finally
        {
            if (runtime.List().Any(s => s.SessionId == id)) await runtime.KillAsync(id, TimeSpan.FromSeconds(2), CancellationToken.None);
        }
    }
}
