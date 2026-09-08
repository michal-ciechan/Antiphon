using System.Diagnostics;
using System.Text.Json;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public class SpecialistRoutingScriptTests
{
    [Test]
    public async Task Card0415_V18_helper_inspects_sets_and_revalidates_with_fresh_tokens()
    {
        await using var h = await StandingSpecialistRoutingHttpTests.Harness.CreateAsync(network: true);
        var inspect = await RunAsync(h, "inspect");
        inspect.ExitCode.ShouldBe(0, inspect.Output);
        using (var json = JsonDocument.Parse(inspect.Output))
            json.RootElement.GetProperty("primaryModelAlias").GetString().ShouldBe("sonnet");
        var set = await RunAsync(h, "set", "-Candidates", "ClaudeCode/High,Codex/Low");
        set.ExitCode.ShouldBe(0, set.Output);
        var saved = await h.GetAsync();
        saved.Candidates.ShouldBe(h.Pairs);
        var revalidate = await RunAsync(h, "revalidate");
        revalidate.ExitCode.ShouldBe(0, revalidate.Output);
        var current = await h.GetAsync();
        current.ConcurrencyToken.ShouldNotBe(saved.ConcurrencyToken);
        current.CandidateStates.ShouldAllBe(c => c.Status != StandingSpecialistCandidateStatus.Qualified);
        var disabled = await RunAsync(h, "set", "-Disable");
        disabled.ExitCode.ShouldBe(0, disabled.Output);
        (await h.GetAsync()).Enabled.ShouldBe(false);
    }

    [Test]
    public async Task Card0415_V18_helper_rejects_incomplete_pairs_without_changing_configuration()
    {
        await using var h = await StandingSpecialistRoutingHttpTests.Harness.CreateAsync(network: true);
        var invalid = await RunAsync(h, "set", "-Candidates", "Codex");
        invalid.ExitCode.ShouldNotBe(0);
        invalid.Output.ShouldContain("Invalid candidate");
        (await h.GetAsync()).ConcurrencyToken.ShouldBeNull();
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(
        StandingSpecialistRoutingHttpTests.Harness h, params string[] arguments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "scripts", "specialist-routing.ps1")))
            directory = directory.Parent;
        directory.ShouldNotBeNull("the test needs this checkout's helper");
        var start = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = directory!.FullName,
        };
        foreach (var argument in new[] { "-NoProfile", "-File", Path.Combine(directory.FullName, "scripts", "specialist-routing.ps1") }
            .Concat(arguments).Concat(["-Agent", h.AgentId.ToString("D")])) start.ArgumentList.Add(argument);
        start.Environment["ANTIPHON_API"] = h.Client.BaseAddress!.ToString().TrimEnd('/');
        start.Environment.Remove("ANTIPHON_TASK_TOKEN");
        using var process = Process.Start(start)!;
        try
        {
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(stop.Token);
            return (process.ExitCode, await output + await error);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }
}
