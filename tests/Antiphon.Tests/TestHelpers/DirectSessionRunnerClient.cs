using System.Runtime.CompilerServices;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Antiphon.Tests.TestHelpers;

internal sealed class DirectSessionRunnerClient : ISessionRunnerClient, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SessionRunnerRuntime _runtime;

    public DirectSessionRunnerClient(string sessionLogPath)
    {
        _runtime = new SessionRunnerRuntime(
            Options.Create(new Antiphon.SessionRunner.SessionRunnerSettings
            {
                SessionLogPath = sessionLogPath
            }),
            NullLogger<SessionRunnerRuntime>.Instance);
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

        return Map(await _runtime.StartAsync(request, ct));
    }

    public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>(_runtime.List().Select(Map).ToList());
    }

    public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Map(_runtime.Get(sessionId)));
    }

    public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var buffer = _runtime.GetBuffer(sessionId);
        return Task.FromResult(new SessionRunnerBufferDto(buffer.SessionId, buffer.Buffer, buffer.LastSequence));
    }

    public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var snapshot = _runtime.GetSnapshot(sessionId);
        return Task.FromResult(new SessionRunnerSnapshotDto(
            snapshot.SessionId,
            snapshot.RawOutput,
            snapshot.RenderedScreen,
            snapshot.LastSequence,
            snapshot.StartedAt));
    }

    public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var transcript = _runtime.GetTranscript(sessionId);
        return Task.FromResult(new SessionRunnerTranscriptDto(
            transcript.SessionId,
            transcript.Entries.Select(MapTranscript).ToList(),
            transcript.LastSequence));
    }

    public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) =>
        _runtime.SendInputAsync(sessionId, input, ct);

    public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) =>
        _runtime.ClearLiveBufferAsync(sessionId, ct);

    public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) =>
        _runtime.ResizeAsync(sessionId, cols, rows, ct);

    public async Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) =>
        Map(await _runtime.KillAsync(sessionId, TimeSpan.FromSeconds(5), ct));

    public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var reader = _runtime.Subscribe(ct);
        await foreach (var evt in reader.ReadAllAsync(ct))
        {
            var parsed = ParseEvent(evt.EventName, evt.Json);
            if (parsed is not null)
                yield return parsed;
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in _runtime.List())
        {
            if (session.Status is "Running" or "Starting")
                await _runtime.KillAsync(session.SessionId, TimeSpan.FromSeconds(2), CancellationToken.None);
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

        if (eventName == SessionRunnerEventNames.SessionTranscript)
        {
            var entry = JsonSerializer.Deserialize<RunnerTranscriptEvent>(json, JsonOptions);
            return entry is null
                ? null
                : new SessionRunnerEvent(eventName, entry.SessionId, Transcript: MapTranscript(entry));
        }

        return null;
    }

    private static SessionRunnerTranscriptEvent MapTranscript(RunnerTranscriptEvent e) =>
        new(
            e.SessionId, e.Sequence, e.Kind, e.Uuid, e.ParentUuid, e.Timestamp,
            e.Role, e.Text, e.ToolName, e.ToolInput, e.ToolUseId, e.ToolIsError, e.StopReason);

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
