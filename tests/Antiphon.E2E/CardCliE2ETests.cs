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
/// <c>scripts/card.ps1</c> driven as a real process against a real server (CARD-0051 slice 4).
/// </summary>
/// <remarks>
/// The semantics all live behind the API and are covered by the slice 1-3 tests. What only this can
/// catch is the script's own plumbing: file reading, column-name resolution, the verb dispatch, and
/// whether the server's message survives to the caller. No browser and no agent session — the
/// fixture's Kestrel and its own Postgres are the whole cost, and every move here is deliberately
/// one the server will not spawn on.
///
/// <para>The precedent for a repo script with no harness is <c>delegate.ps1</c>, which is exercised
/// only through the delegation E2E suite. The reason not to follow it here is the round trip that
/// motivated the card: a description full of backticks and <c>$(...)</c> is exactly what a shell
/// mangles, and "it worked when I ran it by hand" is not a regression test.</para>
/// </remarks>
[NotInParallel]
public class CardCliE2ETests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly AntiphonAppFixture _appFixture = new();
    private readonly List<string> _tempFiles = [];

    [Before(Test)]
    public Task SetupAsync()
    {
        // No browser here, so no prebuilt frontend is needed and no stale-bundle gate applies.
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
    public async Task The_cli_creates_reads_edits_moves_and_closes_a_card_by_its_identifier()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var projectId = await CreateProjectAsync($"Card CLI Project {suffix}");
        var boardName = $"Card CLI Board {suffix}";
        await CreateBoardAsync(projectId, boardName);

        // The body a shell would eat: backticks, $(...), $vars, quotes, braces and real newlines.
        // It never touches a command line — the script reads it with Get-Content -Raw.
        var description = string.Join(
            "\n",
            "First line with `backticks` and $(Get-Date) and $env:PATH.",
            "A \"quoted\" phrase, a 'single-quoted' one, and a ${brace} form.",
            "",
            "    indented | piped > redirected & backgrounded % modulo # hash",
            "Last line.");
        var descriptionFile = NewTempFile(description);

        var created = RunCard("new", "-Board", boardName, "-Title", "Filed by the CLI",
            "-DescriptionFile", descriptionFile);
        created.ExitCode.ShouldBe(0, created.All);
        created.Stdout.ShouldContain("CARD-0001");
        created.Stdout.ShouldContain("Filed by the CLI");

        // Addressed by the SHORT form a human types, not by a guid anyone had to look up.
        var read = RunCard("get", "1", "-Json");
        read.ExitCode.ShouldBe(0, read.All);
        var card = JsonDocument.Parse(read.Stdout).RootElement;
        card.GetProperty("identifier").GetString().ShouldBe("CARD-0001");
        // Byte-identical, which is the whole claim of -DescriptionFile.
        card.GetProperty("description").GetString().ShouldBe(description);

        // A correction, with its reason also coming from a file.
        var reasonFile = NewTempFile("The original body was written before the `$(measurement)` landed.");
        var edited = RunCard("edit", "card-1", "-ReasonFile", reasonFile,
            "-Title", "Filed and corrected by the CLI", "-By", "cli-e2e");
        edited.ExitCode.ShouldBe(0, edited.All);
        edited.Stdout.ShouldContain("Filed and corrected by the CLI");

        // Into an ACTIVE column with no -Spawn: the card moves and NOTHING starts, out loud.
        var moved = RunCard("move", "CARD-0001", "-To", "In Progress", "-Reason", "Belongs here.");
        moved.ExitCode.ShouldBe(0, moved.All);
        moved.Stdout.ShouldContain("NO agent was started");
        moved.Stdout.ShouldContain("-Spawn");

        var review = RunCard("move", "CARD-0001", "-To", "review", "-Reason", "Ready to look at.");
        review.ExitCode.ShouldBe(0, review.All);
        review.Stdout.ShouldContain("moved to    Review");

        // close resolves the board's first terminal column itself — no column id in sight.
        var closed = RunCard("close", "CARD-0001", "-Reason", "Done, and this is why.");
        closed.ExitCode.ShouldBe(0, closed.All);
        closed.Stdout.ShouldContain("moved to    Done");

        var history = RunCard("history", "CARD-0001");
        history.ExitCode.ShouldBe(0, history.All);
        history.Stdout.ShouldContain("ContentEdit");
        history.Stdout.ShouldContain("Move");
        history.Stdout.ShouldContain("Done, and this is why.");

        var archived = RunCard("archive", "CARD-0001", "-Reason", "Filed away.", "-By", "cli-e2e");
        archived.ExitCode.ShouldBe(0, archived.All);
        archived.Stdout.ShouldContain("archived");

        var restored = RunCard("unarchive", "CARD-0001", "-Reason", "Wanted after all.");
        restored.ExitCode.ShouldBe(0, restored.All);
        restored.Stdout.ShouldContain("back on the board");

        // Nothing in this test asked for an agent, and nothing started one.
        var final = await _appFixture.HttpClient.GetFromJsonAsync<JsonElement>(
            "/api/cards/CARD-0001", JsonOptions);
        final.GetProperty("ownerSessionId").ValueKind.ShouldBe(JsonValueKind.Null);
        final.GetProperty("sessions").EnumerateArray().Count().ShouldBe(0);
    }

    [Test]
    public async Task The_cli_prints_the_limits_and_refuses_an_over_long_reason_before_sending_it()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var projectId = await CreateProjectAsync($"Card CLI Limits Project {suffix}");
        var boardName = $"Card CLI Limits Board {suffix}";
        await CreateBoardAsync(projectId, boardName);
        RunCard("new", "-Board", boardName, "-Title", "Has a ceiling").ExitCode.ShouldBe(0);

        var limits = RunCard("-Limits");
        limits.ExitCode.ShouldBe(0, limits.All);
        limits.Stdout.ShouldContain("title       300");
        limits.Stdout.ShouldContain("description 20000");
        limits.Stdout.ShouldContain("reason      4000");
        limits.Stdout.ShouldContain("actor       200");

        // Fails locally and deterministically, naming the ceiling — no round trip, no 422 after the
        // text was already assembled.
        var overLong = NewTempFile(new string('r', 4_001));
        var refused = RunCard("edit", "CARD-0001", "-ReasonFile", overLong, "-Title", "Never sent");
        refused.ExitCode.ShouldNotBe(0);
        refused.All.ShouldContain("4001");
        refused.All.ShouldContain("4000");

        var unchanged = RunCard("get", "CARD-0001", "-Json");
        JsonDocument.Parse(unchanged.Stdout).RootElement
            .GetProperty("title").GetString().ShouldBe("Has a ceiling");
    }

    [Test]
    public async Task An_unknown_card_and_an_unknown_column_both_fail_with_the_servers_own_words()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var projectId = await CreateProjectAsync($"Card CLI Errors Project {suffix}");
        var boardName = $"Card CLI Errors Board {suffix}";
        await CreateBoardAsync(projectId, boardName);
        RunCard("new", "-Board", boardName, "-Title", "The only card").ExitCode.ShouldBe(0);

        var missing = RunCard("get", "CARD-9998");
        missing.ExitCode.ShouldNotBe(0);
        missing.All.ShouldContain("CARD-9998");

        // Not identifier-shaped: a 422 that names the forms, not a bare 404.
        var junk = RunCard("get", "not-a-card");
        junk.ExitCode.ShouldNotBe(0);

        // The column is resolved client-side, so the error names the columns that DO exist.
        var wrongColumn = RunCard("move", "CARD-0001", "-To", "Nowhere");
        wrongColumn.ExitCode.ShouldNotBe(0);
        wrongColumn.All.ShouldContain("Backlog");
        wrongColumn.All.ShouldContain("Done");
    }

    private CliResult RunCard(params string[] arguments)
    {
        var scriptPath = Path.Combine(FindRepoRoot(), "scripts", "card.ps1");
        var startInfo = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = FindRepoRoot(),
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        // The script's only configuration: where the server is. Same variable delegate.ps1 reads.
        startInfo.Environment["ANTIPHON_API"] = _appFixture.BaseAddress;
        startInfo.Environment.Remove("ANTIPHON_TASK_TOKEN");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("pwsh did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit(120_000).ShouldBeTrue("card.ps1 did not exit within 120s");

        return new CliResult(process.ExitCode, stdout.Result, stderr.Result);
    }

    private sealed record CliResult(int ExitCode, string Stdout, string Stderr)
    {
        public string All => $"{Stdout}{Environment.NewLine}{Stderr}";
    }

    private string NewTempFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"antiphon-card-cli-{Guid.NewGuid():N}.txt");
        // No BOM and LF endings: the point of the round-trip assertion is that nothing rewrites the
        // bytes on the way through, so nothing may rewrite them on the way in either.
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        _tempFiles.Add(path);
        return path;
    }

    private async Task<Guid> CreateProjectAsync(string name)
    {
        var response = await _appFixture.HttpClient.PostAsJsonAsync(
            "/api/projects",
            new
            {
                name,
                gitRepositoryUrl = "https://github.com/example/card-cli-e2e.git",
                baseBranch = "main",
                gitHubIntegrationEnabled = false,
                notificationsEnabled = false
            },
            JsonOptions);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return body.GetProperty("id").GetGuid();
    }

    private async Task CreateBoardAsync(Guid projectId, string name)
    {
        var response = await _appFixture.HttpClient.PostAsJsonAsync(
            "/api/boards",
            new { projectId, name },
            JsonOptions);
        response.EnsureSuccessStatusCode();
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Antiphon.sln")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the Antiphon repository root.");
    }
}
