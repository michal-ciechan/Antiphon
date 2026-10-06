using System.Diagnostics;
using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// CARD-0795 V-1..V-3. Evaluation of the real repo import chain: an isolated
/// <c>OutputPath</c> turns shared compilation off, a daemon build keeps the SDK
/// default, and an explicit environment value wins.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class DirectoryBuildPropsSharedCompilationTests
{
    private const int DeadlineMs = 120_000;

    [Test]
    [Timeout(DeadlineMs)]
    public async Task An_isolated_output_turns_shared_compilation_off()
    {
        var evaluated = await EvaluateAsync(null, "--property:OutputPath=bin-c795-eval/");
        evaluated.UseSharedCompilation.ShouldBe("false");
        evaluated.AntiphonIsolatedOutput.ShouldBe("true");
    }

    [Test]
    [Timeout(DeadlineMs)]
    public async Task A_daemon_output_keeps_the_sdk_default()
    {
        var evaluated = await EvaluateAsync(null);
        evaluated.UseSharedCompilation.ShouldNotBe("false");
        evaluated.AntiphonIsolatedOutput.ShouldBe(string.Empty);
    }

    [Test]
    [Timeout(DeadlineMs)]
    public async Task An_environment_value_wins_on_an_isolated_output()
    {
        var evaluated = await EvaluateAsync(
            new Dictionary<string, string> { ["UseSharedCompilation"] = "true" },
            "--property:OutputPath=bin-c795-eval/");
        evaluated.UseSharedCompilation.ShouldBe("true");
    }

    private static async Task<Evaluation> EvaluateAsync(
        IReadOnlyDictionary<string, string>? environment,
        params string[] properties)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = DockerStackDocuments.RepoRoot,
        };
        start.ArgumentList.Add("msbuild");
        start.ArgumentList.Add("src/Antiphon.SessionRunner.Contracts/Antiphon.SessionRunner.Contracts.csproj");
        start.ArgumentList.Add("-getProperty:UseSharedCompilation");
        start.ArgumentList.Add("-getProperty:AntiphonIsolatedOutput");
        start.ArgumentList.Add("-nologo");
        foreach (var property in properties)
            start.ArgumentList.Add(property);
        if (environment is not null)
        {
            foreach (var pair in environment)
                start.Environment[pair.Key] = pair.Value;
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("dotnet did not start");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(DeadlineMs);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // The process exited between the timeout and the kill.
            }

            var timedErr = await stderrTask;
            throw new TimeoutException($"dotnet msbuild exceeded 120s. stderr: {timedErr}");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        process.ExitCode.ShouldBe(0, $"stderr: {stderr}");
        return ReadEvaluation(stdout, stderr);
    }

    private static Evaluation ReadEvaluation(string stdout, string stderr)
    {
        try
        {
            using var document = JsonDocument.Parse(stdout);
            var props = document.RootElement.GetProperty("Properties");
            return new Evaluation(Read(props, "UseSharedCompilation"), Read(props, "AntiphonIsolatedOutput"));
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"dotnet msbuild did not return a Properties object. stderr: {stderr} stdout: {stdout}", ex);
        }
        catch (KeyNotFoundException ex)
        {
            throw new InvalidOperationException(
                $"dotnet msbuild JSON has no Properties object. stderr: {stderr} stdout: {stdout}", ex);
        }

        static string Read(JsonElement props, string name) =>
            props.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";
    }

    private sealed record Evaluation(string UseSharedCompilation, string AntiphonIsolatedOutput);
}
