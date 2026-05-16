using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.E2E.Fixtures;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Shouldly;
using TUnit.Core;

namespace Antiphon.E2E;

[NotInParallel]
public sealed class ChannelE2ETests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly AntiphonAppFixture _appFixture = new();
    private readonly List<string> _tempRepoRoots = [];

    [Before(Test)]
    public async Task SetupAsync()
    {
        await _appFixture.InitializeAsync();
    }

    [After(Test)]
    public async Task TeardownAsync()
    {
        await _appFixture.DisposeAsync();
        foreach (var repoRoot in _tempRepoRoots)
            await DeleteDirectoryBestEffortAsync(repoRoot);
    }

    [Test]
    public async Task SignalR_channel_send_routes_to_card_group_for_live_session()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var repoPath = await CreateLocalGitRepositoryAsync();
        var projectId = await CreateProjectAsync($"E11 Channel Project {suffix}", repoPath);
        var boardId = await CreateBoardAsync(projectId, $"E11 Channel Board {suffix}");
        var cardId = await CreateCardAsync(boardId, $"E11 Channel Card {suffix}");
        var sessionId = await SpawnCardAsync(cardId);
        await WaitForSessionRunningAsync(boardId, sessionId);

        var channelMessage = $"E11 hub message {suffix}";
        var received = new TaskCompletionSource<ChannelMessagePayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = new HubConnectionBuilder()
            .WithUrl($"{_appFixture.BaseAddress}/hubs/antiphon", options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => new SocketsHttpHandler { UseProxy = false };
            })
            .Build();
        connection.On<ChannelMessagePayload>("ChannelMessage", payload => received.TrySetResult(payload));

        await connection.StartAsync();
        await connection.InvokeAsync("JoinCard", cardId);
        await connection.InvokeAsync("SendAsync", sessionId, new { message = channelMessage });

        var payload = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        payload.TargetSessionId.ShouldBe(sessionId);
        payload.TargetCardId.ShouldBe(cardId);
        payload.Message.ShouldBe(channelMessage);
        payload.RoutedByMention.ShouldBeFalse();
    }

    private async Task<Guid> CreateProjectAsync(string name, string localRepositoryPath)
    {
        var response = await _appFixture.HttpClient.PostAsJsonAsync(
            "/api/projects",
            new
            {
                name,
                gitRepositoryUrl = "https://github.com/example/e11-channel-e2e.git",
                localRepositoryPath,
                baseBranch = "main",
                constitutionPath = (string?)null,
                gitHubIntegrationEnabled = false,
                notificationsEnabled = false
            },
            JsonOptions);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return body.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateBoardAsync(Guid projectId, string name)
    {
        var response = await _appFixture.HttpClient.PostAsJsonAsync(
            "/api/boards",
            new
            {
                projectId,
                name
            },
            JsonOptions);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return body.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateCardAsync(Guid boardId, string title)
    {
        var response = await _appFixture.HttpClient.PostAsJsonAsync(
            $"/api/boards/{boardId}/cards",
            new
            {
                boardColumnId = (Guid?)null,
                title,
                description = "SignalR channel E2E target.",
                labels = new[] { "e2e", "channel" }
            },
            JsonOptions);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return body.GetProperty("id").GetGuid();
    }

    private async Task<Guid> SpawnCardAsync(Guid cardId)
    {
        var response = await _appFixture.HttpClient.PostAsJsonAsync(
            $"/api/cards/{cardId}/spawn",
            new
            {
                prompt = "echo E11 channel target ready",
                cols = 100,
                rows = 24
            },
            JsonOptions);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return body.GetProperty("sessionId").GetGuid();
    }

    private async Task WaitForSessionRunningAsync(Guid boardId, Guid sessionId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var board = await GetBoardAsync(boardId);
            foreach (var column in board.GetProperty("columns").EnumerateArray())
            {
                foreach (var card in column.GetProperty("cards").EnumerateArray())
                {
                    foreach (var session in card.GetProperty("sessions").EnumerateArray())
                    {
                        if (session.GetProperty("id").GetGuid() != sessionId)
                            continue;

                        var status = session.GetProperty("status").GetString();
                        if (string.Equals(status, "Running", StringComparison.OrdinalIgnoreCase))
                            return;
                    }
                }
            }

            await Task.Delay(250);
        }

        throw new TimeoutException($"Session {sessionId} did not reach Running status.");
    }

    private async Task<JsonElement> GetBoardAsync(Guid boardId)
    {
        var response = await _appFixture.HttpClient.GetAsync($"/api/boards/{boardId}");
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    private async Task<string> CreateLocalGitRepositoryAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "antiphon-e2e-channel-repos", Guid.NewGuid().ToString("N"));
        var repoPath = Path.Combine(root, "work");
        Directory.CreateDirectory(repoPath);
        _tempRepoRoots.Add(root);

        await RunGitAsync(repoPath, "init");
        await RunGitAsync(repoPath, "config", "user.email", "e2e@example.test");
        await RunGitAsync(repoPath, "config", "user.name", "Antiphon E2E");
        await File.WriteAllTextAsync(Path.Combine(repoPath, "README.md"), "# E2E channel repository\n");
        await RunGitAsync(repoPath, "add", "README.md");
        await RunGitAsync(repoPath, "commit", "-m", "Initial commit");
        await RunGitAsync(repoPath, "branch", "-M", "main");

        return repoPath;
    }

    private static async Task RunGitAsync(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start git.");
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} failed with exit code {process.ExitCode}: {stdout}{stderr}");
    }

    private static async Task DeleteDirectoryBestEffortAsync(string path)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (!Directory.Exists(path))
                    return;

                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);

                Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100 * (attempt + 1)));
            }
        }
    }

    private sealed record ChannelMessagePayload(
        Guid? SourceSessionId,
        Guid TargetSessionId,
        Guid TargetCardId,
        string Message,
        bool RoutedByMention);
}
