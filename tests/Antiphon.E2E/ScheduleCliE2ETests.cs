using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.E2E.Fixtures;
using Shouldly;
using TUnit.Core;

namespace Antiphon.E2E;

/// <summary>
/// <c>scripts/schedule.ps1</c> driven as a real process against a real server (CARD-0057 S3).
/// Every schedule it creates is a prompt, so the fixture never spawns.
/// </summary>
[NotInParallel]
public class ScheduleCliE2ETests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly AntiphonAppFixture _appFixture = new();
    private readonly List<string> _tempFiles = [];

    [Before(Test)]
    public Task SetupAsync()
    {
        _appFixture.UsePrebuiltFrontend = false;
        return _appFixture.InitializeAsync();
    }

    [After(Test)]
    public async Task TeardownAsync()
    {
        await _appFixture.DisposeAsync();
        foreach (var file in _tempFiles)
        {
            try { File.Delete(file); }
            catch (IOException) { /* best effort */ }
        }
    }

    [Test]
    public async Task The_cli_round_trips_new_list_preview_disable_remove()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var workspace = Path.Combine(Path.GetTempPath(), $"sched-cli-{suffix}");
        Directory.CreateDirectory(workspace);
        var agentResponse = await _appFixture.HttpClient.PostAsJsonAsync("/api/agents", new
        {
            name = $"Sched CLI {suffix}",
            workingDirectory = workspace,
            alwaysOn = true,
        }, JsonOptions);
        agentResponse.EnsureSuccessStatusCode();
        var agent = await agentResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var agentId = agent.GetProperty("id").GetGuid();

        var prompt = string.Join("\n", "First line with `backticks` and $(Get-Date).", "Second line.");
        var promptFile = NewTempFile(prompt);
        var fireAt = DateTime.UtcNow.AddHours(2).ToString("o");

        var created = RunSchedule(
            "new",
            "-Name", "Morning triage",
            "-Agent", agentId.ToString(),
            "-Repeat", "Once",
            "-FireAt", fireAt,
            "-PromptFile", promptFile,
            "-Json");
        created.ExitCode.ShouldBe(0, created.All);
        created.Stdout.ShouldContain("Morning triage");
        var createdJson = JsonDocument.Parse(ExtractJson(created.Stdout)).RootElement;
        var id = createdJson.GetProperty("id").GetGuid();
        createdJson.GetProperty("promptText").GetString().ShouldBe(prompt);

        var list = RunSchedule("list", "-Agent", agentId.ToString(), "-Json");
        list.ExitCode.ShouldBe(0, list.All);
        list.Stdout.ShouldContain(id.ToString());

        var preview = RunSchedule(
            "preview",
            "-Name", "Morning triage",
            "-Agent", agentId.ToString(),
            "-Repeat", "Once",
            "-FireAt", fireAt,
            "-PromptFile", promptFile);
        preview.ExitCode.ShouldBe(0, preview.All);
        preview.Stdout.ShouldContain("Preview");

        var disabled = RunSchedule("disable", id.ToString());
        disabled.ExitCode.ShouldBe(0, disabled.All);

        var got = RunSchedule("get", id.ToString(), "-Json");
        got.ExitCode.ShouldBe(0, got.All);
        JsonDocument.Parse(got.Stdout).RootElement.GetProperty("enabled").GetBoolean().ShouldBeFalse();

        var removed = RunSchedule("remove", id.ToString());
        removed.ExitCode.ShouldBe(0, removed.All);
        removed.Stdout.ShouldContain("removed");
    }

    private static string ExtractJson(string stdout)
    {
        var start = stdout.IndexOf('{');
        return start >= 0 ? stdout[start..] : stdout;
    }

    private CliResult RunSchedule(params string[] arguments)
    {
        var repoRoot = FindRepoRoot();
        var scriptPath = Path.Combine(repoRoot, "scripts", "schedule.ps1");
        var startInfo = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = repoRoot,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        startInfo.Environment["ANTIPHON_API"] = _appFixture.BaseAddress;
        startInfo.Environment["ANTIPHON_TASK_TOKEN"] = "";

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("pwsh did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit(120_000).ShouldBeTrue("schedule.ps1 did not exit within 120s");
        return new CliResult(process.ExitCode, stdout.Result, stderr.Result);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Antiphon.sln")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the Antiphon repository root.");
    }

    private string NewTempFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"antiphon-sched-cli-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        _tempFiles.Add(path);
        return path;
    }

    private sealed record CliResult(int ExitCode, string Stdout, string Stderr)
    {
        public string All => $"{Stdout}{Environment.NewLine}{Stderr}";
    }
}
