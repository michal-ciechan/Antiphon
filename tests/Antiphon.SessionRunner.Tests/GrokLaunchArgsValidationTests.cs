using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class GrokLaunchArgsValidationTests
{
    [Test]
    [Arguments("null_args", "ArgumentException", "Args must not be null or contain null elements. (Parameter 'request')")]
    [Arguments("null_element", "ArgumentException", "Args must not be null or contain null elements. (Parameter 'request')")]
    [Arguments("empty_args", "GrokRulesTransportException", "grok_rules_argv_unsafe: command_line_budget")]
    [Arguments("normal_args", "GrokRulesTransportException", "grok_rules_argv_unsafe: command_line_budget")]
    public async Task Positive_dimensions_validate_grok_argv(string variant, string expectedType, string expectedMessage)
    {
        var root = Root();
        await using var runtime = Runtime(root);
        IReadOnlyList<string> args = variant switch
        {
            "null_args" => null!,
            "null_element" => ["before", null!, "after"],
            "empty_args" => [],
            "normal_args" => ["--always-approve", "a b", "\"quoted\""],
            _ => throw new ArgumentOutOfRangeException(nameof(variant)),
        };
        var request = Launch(root) with { Args = args };
        request.Cols.ShouldBeGreaterThan(0);
        request.Rows.ShouldBeGreaterThan(0);
        var error = await CaptureAsync(() => runtime.StartAsync(request, CancellationToken.None));

        AssertNoEffects(runtime, root);
        error.ShouldNotBeNull().GetType().Name.ShouldBe(expectedType, "positive-size-grok-validation-type");
        error.Message.ShouldBe(expectedMessage, "positive-size-grok-validation-message");
    }

    [Test]
    [Arguments("invalid_dimensions", "Terminal size must be positive. (Parameter 'request')")]
    [Arguments("unknown_backend", "Unsupported session backend 'unknown'. Supported: pty-host, herdr. (Parameter 'request')")]
    public async Task Earlier_validation_keeps_its_precedence(string variant, string expectedMessage)
    {
        var root = Root();
        await using var runtime = Runtime(root);
        var request = Launch(root) with { Args = null! };
        request = variant switch
        {
            "invalid_dimensions" => request with { Cols = 0 },
            "unknown_backend" => request with { Backend = "unknown" },
            _ => throw new ArgumentOutOfRangeException(nameof(variant)),
        };
        var error = await CaptureAsync(() => runtime.StartAsync(request, CancellationToken.None));

        AssertNoEffects(runtime, root);
        error.ShouldNotBeNull().GetType().ShouldBe(typeof(ArgumentException), "grok-validation-precedence-type");
        error.Message.ShouldBe(expectedMessage, "grok-validation-precedence-message");
    }

    private static RunnerLaunchRequest Launch(string root) => new(Guid.NewGuid(), "grok", [],
        new Dictionary<string, string>(), root, 80, 24,
        TranscriptFormat: TranscriptFormats.Grok,
        GrokRulesPayload: new GrokRulesPayload("standing instructions", GrokRulesTransport.Version, Guid.NewGuid()),
        CommandLineBudgetChars: 1);

    private static SessionRunnerRuntime Runtime(string root) => new(
        Options.Create(new SessionRunnerSettings { SessionLogPath = root, CpuWatchdogEnabled = false }),
        NullLogger<SessionRunnerRuntime>.Instance);

    private static string Root() => Path.Combine(Path.GetTempPath(), "c1206-grok-" + Guid.NewGuid().ToString("N"));

    private static void AssertNoEffects(SessionRunnerRuntime runtime, string root)
    {
        runtime.StartCoreSessionRegistrations.ShouldBe(0, "grok-malformed-no-registration");
        runtime.List().ShouldBeEmpty();
        Directory.Exists(root).ShouldBeFalse("grok-malformed-no-disk-effects");
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try { await action(); return null; }
        catch (Exception error) { return error; }
    }
}
