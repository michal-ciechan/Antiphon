using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

public sealed class SessionRunnerHttpClient : ISessionRunnerClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    public SessionRunnerHttpClient(
        HttpClient httpClient,
        IOptions<SessionRunnerSettings> settings)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(settings.Value.BaseUrl.TrimEnd('/') + "/");
    }

    public async Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct)
    {
        var request = new RunnerLaunchRequest(
            sessionId,
            spec.Exe,
            spec.Args,
            spec.Env,
            spec.Cwd,
            spec.Cols,
            spec.Rows,
            spec.MemoryLimitMb);
        var response = await _httpClient.PostAsJsonAsync("sessions", request, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
        return Map(await response.Content.ReadFromJsonAsync<RunnerSessionDto>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Session runner returned an empty start response."));
    }

    public async Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct)
    {
        var sessions = await _httpClient.GetFromJsonAsync<IReadOnlyList<RunnerSessionDto>>("sessions", JsonOptions, ct)
            ?? [];
        return sessions.Select(Map).ToList();
    }

    public async Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
        Map(await _httpClient.GetFromJsonAsync<RunnerSessionDto>($"sessions/{sessionId:D}", JsonOptions, ct)
            ?? throw new InvalidOperationException("Session runner returned an empty session response."));

    public async Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct)
    {
        var buffer = await _httpClient.GetFromJsonAsync<RunnerBufferDto>($"sessions/{sessionId:D}/buffer", JsonOptions, ct)
            ?? throw new InvalidOperationException("Session runner returned an empty buffer response.");
        return new SessionRunnerBufferDto(buffer.SessionId, buffer.Buffer, buffer.LastSequence);
    }

    public async Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct)
    {
        var snapshot = await _httpClient.GetFromJsonAsync<RunnerSnapshotDto>($"sessions/{sessionId:D}/snapshot", JsonOptions, ct)
            ?? throw new InvalidOperationException("Session runner returned an empty snapshot response.");
        return new SessionRunnerSnapshotDto(
            snapshot.SessionId,
            snapshot.RawOutput,
            snapshot.RenderedScreen,
            snapshot.LastSequence,
            snapshot.StartedAt);
    }

    public async Task SendInputAsync(Guid sessionId, string input, CancellationToken ct)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"sessions/{sessionId:D}/input",
            new RunnerInputRequest(input),
            JsonOptions,
            ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct)
    {
        var response = await _httpClient.PostAsync($"sessions/{sessionId:D}/clear-live-buffer", null, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"sessions/{sessionId:D}/resize",
            new RunnerResizeRequest(cols, rows),
            JsonOptions,
            ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct)
    {
        var response = await _httpClient.PostAsync($"sessions/{sessionId:D}/kill", null, ct);
        response.EnsureSuccessStatusCode();
        return Map(await response.Content.ReadFromJsonAsync<RunnerSessionDto>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Session runner returned an empty kill response."));
    }

    public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "events");
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        string? eventName = null;
        var data = new System.Text.StringBuilder();

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null)
                break;

            if (line.Length == 0)
            {
                if (!string.IsNullOrWhiteSpace(eventName) && data.Length > 0)
                {
                    var parsed = ParseEvent(eventName, data.ToString());
                    if (parsed is not null)
                        yield return parsed;
                }

                eventName = null;
                data.Clear();
                continue;
            }

            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                eventName = line["event: ".Length..].Trim();
                continue;
            }

            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                if (data.Length > 0)
                    data.Append('\n');
                data.Append(line["data: ".Length..]);
            }
        }
    }

    private static SessionRunnerEvent? ParseEvent(string eventName, string json)
    {
        if (eventName == SessionRunnerEventNames.SessionOutput)
        {
            var output = JsonSerializer.Deserialize<RunnerOutputEvent>(json, JsonOptions);
            return output is null
                ? null
                : new SessionRunnerEvent(
                    eventName,
                    output.SessionId,
                    Output: new SessionRunnerOutputEvent(output.SessionId, output.Sequence, output.Text));
        }

        if (eventName == SessionRunnerEventNames.SessionExited)
        {
            var exited = JsonSerializer.Deserialize<RunnerSessionExitedEvent>(json, JsonOptions);
            return exited is null
                ? null
                : new SessionRunnerEvent(
                    eventName,
                    exited.SessionId,
                    Exited: new SessionRunnerExitedEvent(
                        exited.SessionId,
                        exited.ExitCode,
                        MapExitReason(exited.ExitReason),
                        exited.LastSequence));
        }

        if (eventName == SessionRunnerEventNames.SessionStarted)
        {
            var started = JsonSerializer.Deserialize<RunnerSessionStartedEvent>(json, JsonOptions);
            return started is null
                ? null
                : new SessionRunnerEvent(eventName, started.SessionId);
        }

        return null;
    }

    private static SessionRunnerSessionDto Map(RunnerSessionDto dto) =>
        new(
            dto.SessionId,
            dto.Pid,
            dto.StartedAt,
            dto.Status,
            dto.ExitCode,
            MapExitReason(dto.ExitReason),
            dto.LastSequence);

    private static AgentExitReason MapExitReason(string reason) =>
        Enum.TryParse<AgentExitReason>(reason, ignoreCase: true, out var parsed)
            ? parsed
            : AgentExitReason.Unknown;
}
