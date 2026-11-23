using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.E2E.Fixtures;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.E2E;

/// <summary>
/// The schedule command (Antiphon.Nightly.ScheduleCli, which replaced <c>scripts/schedule.ps1</c> in unit 24 of the
/// 2026-11-22 plan) run in process, as the CLI runs it, against a real server over HTTP (CARD-0057 S3). Every schedule it
/// creates is a prompt, so the fixture never spawns. The fake-transport cases are ScheduleCommandTests.
/// </summary>
[NotInParallel]
[Category("OptIn")]
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
            "--name", "Morning triage",
            "--agent", agentId.ToString(),
            "--repeat", "Once",
            "--fire-at", fireAt,
            "--prompt-file", promptFile,
            "--json");
        created.ExitCode.ShouldBe(0, created.All);
        created.Stdout.ShouldContain("Morning triage");
        var createdJson = JsonDocument.Parse(ExtractJson(created.Stdout)).RootElement;
        var id = createdJson.GetProperty("id").GetGuid();
        createdJson.GetProperty("promptText").GetString().ShouldBe(prompt);

        var list = RunSchedule("list", "--agent", agentId.ToString(), "--json");
        list.ExitCode.ShouldBe(0, list.All);
        list.Stdout.ShouldContain(id.ToString());

        var preview = RunSchedule(
            "preview",
            "--name", "Morning triage",
            "--agent", agentId.ToString(),
            "--repeat", "Once",
            "--fire-at", fireAt,
            "--prompt-file", promptFile);
        preview.ExitCode.ShouldBe(0, preview.All);
        preview.Stdout.ShouldContain("Preview");

        var disabled = RunSchedule("disable", id.ToString());
        disabled.ExitCode.ShouldBe(0, disabled.All);

        var got = RunSchedule("get", id.ToString(), "--json");
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

    // The command as the CLI runs it (ANTIPHON_API the fixture, no task token, no ANTIPHON_AGENT_ID), on the production
    // HTTP transport; a usage error is exit 2, as antiphon-scripts answers it.
    private CliResult RunSchedule(params string[] arguments)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int exit;
        try
        {
            exit = ScheduleCli.Run(ScheduleCliOptions.Parse(arguments, _appFixture.BaseAddress, "", null), NightlyHost.Production(stdout, stderr),
                new System.IO.Abstractions.FileSystem(), NightlyReport.HttpTransport(timeout: TimeSpan.FromSeconds(120)));
        }
        catch (NightlyUsageException e)
        {
            stderr.WriteLine("usage: antiphon-scripts schedule: " + e.Message);
            exit = 2;
        }
        return new CliResult(exit, stdout.ToString(), stderr.ToString());
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
