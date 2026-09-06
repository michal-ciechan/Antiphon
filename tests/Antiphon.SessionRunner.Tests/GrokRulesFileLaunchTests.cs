using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class GrokRulesFileLaunchTests
{
    [Test]
    [Arguments("nul")]
    [Arguments("unresolved_key")]
    [Arguments("too_large")]
    [Arguments("wrong_kind")]
    public async Task Invalid_payload_refuses_before_session_registration_or_disk_effects(string reason)
    {
        var root = Path.Combine(Path.GetTempPath(), "card0395", Guid.NewGuid().ToString("N"));
        var settings = new SessionRunnerSettings { SessionLogPath = root };
        await using var runtime = new SessionRunnerRuntime(Options.Create(settings), NullLogger<SessionRunnerRuntime>.Instance);
        var content = reason switch
        {
            "nul" => "content\0",
            "unresolved_key" => "{{key:NAME}}",
            "too_large" => new string('x', 262145),
            _ => "content",
        };
        var request = Request(root) with
        {
            TranscriptFormat = reason == "wrong_kind" ? TranscriptFormats.Claude : TranscriptFormats.Grok,
            GrokRulesPayload = new(content, 1, Guid.NewGuid()),
        };
        var error = await Should.ThrowAsync<GrokRulesTransportException>(() => runtime.StartAsync(request, CancellationToken.None));
        error.Code.ShouldBe("grok_rules_content_invalid");
        error.Reason.ShouldStartWith(reason);
        runtime.List().ShouldBeEmpty();
        Directory.Exists(root).ShouldBeFalse();
    }

    [Test]
    public async Task Explicit_rules_conflict_and_unsafe_source_precedence_have_no_effects()
    {
        var root = Path.Combine(Path.GetTempPath(), "card0395", Guid.NewGuid().ToString("N"));
        await using var runtime = new SessionRunnerRuntime(
            Options.Create(new SessionRunnerSettings { SessionLogPath = root, PtyHostSourceDir = Path.Combine(root, "missing-host") }), NullLogger<SessionRunnerRuntime>.Instance);
        var request = Request(root) with { Args = ["--rules", "@C:\\literal path\\rules.md"] };
        var error = await CaptureAsync(() => runtime.StartAsync(request, CancellationToken.None));
        Directory.Exists(root).ShouldBeFalse("source conflict must refuse before rules materialization");
        runtime.List().ShouldBeEmpty();
        error.ShouldBeOfType<GrokRulesTransportException>()
            .Code.ShouldBe("grok_rules_source_conflict");
        if (OperatingSystem.IsWindows())
            (await Should.ThrowAsync<GrokRulesLaunchException>(() => runtime.StartAsync(
                request with { Args = ["--rules", "unsafe\nbody"] }, CancellationToken.None)))
                .Code.ShouldBe("grok_rules_argv_unsafe");
        runtime.List().ShouldBeEmpty();
        Directory.Exists(root).ShouldBeFalse();
    }

    [Test]
    public async Task Actual_argv_budget_includes_generated_bootstrap_before_materialization()
    {
        var root = Path.Combine(Path.GetTempPath(), "card0395", Guid.NewGuid().ToString("N"));
        await using var runtime = new SessionRunnerRuntime(
            Options.Create(new SessionRunnerSettings { SessionLogPath = root, PtyHostSourceDir = Path.Combine(root, "missing-host") }), NullLogger<SessionRunnerRuntime>.Instance);
        var request = Request(root) with { CommandLineBudgetChars = 100 };
        var error = await CaptureAsync(() => runtime.StartAsync(request, CancellationToken.None));
        Directory.Exists(root).ShouldBeFalse("actual argv over budget must refuse before rules materialization");
        runtime.List().ShouldBeEmpty();
        error.ShouldBeOfType<GrokRulesTransportException>()
            .Reason.ShouldBe("command_line_budget");
        runtime.List().ShouldBeEmpty();
        Directory.Exists(root).ShouldBeFalse();
    }

    private static RunnerLaunchRequest Request(string root) => new(Guid.NewGuid(), "missing-executable", [],
        new Dictionary<string, string>(), root, 120, 30, TranscriptFormat: TranscriptFormats.Grok,
        GrokRulesPayload: new("full\r\nrules", 1, Guid.NewGuid()));

    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try { await action(); return null; }
        catch (Exception ex) { return ex; }
    }
}
