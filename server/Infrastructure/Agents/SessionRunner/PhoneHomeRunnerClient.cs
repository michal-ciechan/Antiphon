using System.Runtime.CompilerServices;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

public sealed class PhoneHomeRunnerClient : ISessionRunnerClient
{
    private readonly PhoneHomeLiveConnection _connection;
    private readonly RunnerContractMapper _mapper = new();
    private readonly Antiphon.Server.Application.Services.RemoteSpillCourier? _spills;

    public PhoneHomeRunnerClient(
        PhoneHomeLiveConnection connection,
        Antiphon.Server.Application.Services.RemoteSpillCourier? spills = null)
    {
        _connection = connection;
        _spills = spills;
    }

    public async Task<RunnerCapabilitiesDto?> GetCapabilitiesAsync(CancellationToken ct)
    {
        var frame = await _connection.RequestAsync(PhoneHomeOperation.Capabilities, null, ct);
        return Read<RunnerCapabilitiesDto>(frame);
    }

    public async Task<string?> GetHealthAsync(CancellationToken ct)
    {
        var frame = await _connection.RequestAsync(PhoneHomeOperation.Health, null, ct);
        if (frame.Kind == PhoneHomeFrameKind.Error)
            return null;
        return frame.Payload?.GetRawText();
    }

    public async Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct)
    {
        var frame = await _connection.RequestAsync(PhoneHomeOperation.List, null, ct);
        var dtos = Read<IReadOnlyList<RunnerSessionDto>>(frame) ?? [];
        return dtos.Select(_mapper.Map).ToArray();
    }

    public async Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct)
    {
        var frame = await _connection.RequestAsync(PhoneHomeOperation.Get, new { sessionId }, ct);
        return _mapper.Map(Read<RunnerSessionDto>(frame) ?? throw Missing("get"));
    }

    public async Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct)
    {
        var request = _mapper.ToLaunchRequest(sessionId, spec);
        var frame = await _connection.RequestAsync(PhoneHomeOperation.Launch, request, ct);
        var started = _mapper.Map(Read<RunnerSessionDto>(frame) ?? throw Missing("launch"));
        if (spec.AcceptedStartedAt is { } expected
            && !SessionGeneration.Equal(expected, started.AcceptedStartedAt))
        {
            throw new ConflictException(
                "The session runner did not echo the accepted launch generation.",
                SessionGeneration.NotEchoed);
        }

        return started;
    }

    public async Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct)
    {
        var frame = await _connection.RequestAsync(PhoneHomeOperation.Buffer, new { sessionId }, ct);
        var dto = Read<RunnerBufferDto>(frame) ?? throw Missing("buffer");
        return new SessionRunnerBufferDto(dto.SessionId, dto.Buffer, dto.LastSequence);
    }

    public async Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct)
    {
        var frame = await _connection.RequestAsync(PhoneHomeOperation.Snapshot, new { sessionId }, ct);
        var dto = Read<RunnerSnapshotDto>(frame) ?? throw Missing("snapshot");
        return new SessionRunnerSnapshotDto(dto.SessionId, dto.RawOutput, dto.RenderedScreen, dto.LastSequence, dto.StartedAt);
    }

    public async Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct)
    {
        var frame = await _connection.RequestAsync(PhoneHomeOperation.Transcript, new { sessionId }, ct);
        return _mapper.MapTranscript(Read<RunnerTranscriptDto>(frame) ?? throw Missing("transcript"));
    }

    public async Task SendInputAsync(Guid sessionId, string input, CancellationToken ct)
    {
        // CARD-0604 G-21. A body spilled for this session travels HERE, in the Input payload, and
        // the runner writes it inside the session's own cwd. Taking it clears it, so a retyped
        // pointer after a composer-evidence retry does not rewrite the file.
        if (_spills is not null && _spills.TryTake(sessionId, out var staged))
        {
            await SendInputWithSpillAsync(sessionId, input, staged.RunnerCwd, staged.Spill, ct);
            return;
        }

        var frame = await _connection.RequestAsync(
            PhoneHomeOperation.Input, new { sessionId, input }, ct);
        ThrowIfError(frame);
    }

    /// <summary>
    /// CARD-0604 G-21. Input plus the body of a spilled file, written by the RUNNER inside
    /// <paramref name="runnerCwd"/>. The desktop never writes it: the session cannot see a Windows
    /// path, and a desktop write leaves a file no one reads behind a prompt pointing at nothing.
    /// </summary>
    public async Task SendInputWithSpillAsync(
        Guid sessionId, string input, string runnerCwd, PhoneHomeInputSpill spill, CancellationToken ct)
    {
        var frame = await _connection.RequestAsync(
            PhoneHomeOperation.Input, new { sessionId, input, runnerCwd, spill }, ct);
        ThrowIfError(frame);
    }

    /// <summary>CARD-0604 D-15: mirror an already-pushed task branch on the runner.</summary>
    public async Task<PhoneHomeWorkspaceMirrorResponse> MirrorWorkspaceAsync(
        PhoneHomeWorkspaceMirrorRequest request, CancellationToken ct)
    {
        var frame = await _connection.RequestAsync(PhoneHomeOperation.WorkspaceMirror, request, ct);
        return Read<PhoneHomeWorkspaceMirrorResponse>(frame) ?? throw Missing("workspace-mirror");
    }

    /// <summary>CARD-0604 D-15: remove a mirror at retirement. Residue is reported, never forced away.</summary>
    public async Task<PhoneHomeWorkspaceRemoveResponse> RemoveWorkspaceAsync(
        PhoneHomeWorkspaceRemoveRequest request, CancellationToken ct)
    {
        var frame = await _connection.RequestAsync(PhoneHomeOperation.WorkspaceRemove, request, ct);
        return Read<PhoneHomeWorkspaceRemoveResponse>(frame) ?? throw Missing("workspace-remove");
    }

    public async Task<RunnerConditionalInputResult> SendConditionalInputAsync(
        Guid sessionId, RunnerConditionalInputRequest request, CancellationToken ct)
    {
        var frame = await _connection.RequestAsync(
            PhoneHomeOperation.ConditionalInput,
            new
            {
                sessionId,
                expectedAcceptedStartedAt = request.ExpectedAcceptedStartedAt,
                expectedLastSequence = request.ExpectedLastSequence,
                input = request.Input,
            },
            ct);
        if (frame.Kind == PhoneHomeFrameKind.Error)
            return new RunnerConditionalInputResult(sessionId, ConditionalInputOutcomes.Unknown, request.ExpectedAcceptedStartedAt, request.ExpectedLastSequence);
        return Read<RunnerConditionalInputResult>(frame)
            ?? new RunnerConditionalInputResult(sessionId, ConditionalInputOutcomes.Unknown, request.ExpectedAcceptedStartedAt, request.ExpectedLastSequence);
    }

    public async Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct)
    {
        ThrowIfError(await _connection.RequestAsync(PhoneHomeOperation.ClearBuffer, new { sessionId }, ct));
    }

    public async Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct)
    {
        ThrowIfError(await _connection.RequestAsync(
            PhoneHomeOperation.Resize, new { sessionId, cols, rows }, ct));
    }

    public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) =>
        throw new ConflictException("Unconditional kill is not supported on the phone-home runner.", PhoneHomeProblemTypes.UnsupportedOperation);

    public async Task<RunnerKillGenerationResult> KillGenerationAsync(
        Guid sessionId, DateTime expectedAcceptedStartedAt, CancellationToken ct)
    {
        var frame = await _connection.RequestAsync(
            PhoneHomeOperation.KillGeneration,
            new { sessionId, expectedAcceptedStartedAt },
            ct);
        return Read<RunnerKillGenerationResult>(frame) ?? throw Missing("kill-generation");
    }

    public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var frame in _connection.Events.ReadAllAsync(ct))
        {
            if (frame.EventName is null || frame.Payload is null)
                continue;
            var parsed = RunnerContractMapper.ParseEvent(frame.EventName, frame.Payload.Value.GetRawText());
            if (parsed is not null)
                yield return parsed;
        }
    }

    private static T? Read<T>(PhoneHomeFrame frame)
    {
        ThrowIfError(frame);
        if (frame.Payload is null)
            return default;
        return frame.Payload.Value.Deserialize<T>(PhoneHomeFraming.Json);
    }

    private static void ThrowIfError(PhoneHomeFrame frame)
    {
        if (frame.Kind != PhoneHomeFrameKind.Error)
            return;
        var code = frame.ErrorCode ?? "phone_home_error";
        var detail = frame.ErrorDetail ?? "Phone-home command failed.";
        throw frame.StatusCode switch
        {
            404 => new RunnerProblemException(404, detail, code),
            409 => new ConflictException(detail, code),
            503 => new ServiceUnavailableException(detail, code),
            _ => new ConflictException(detail, code),
        };
    }

    private static InvalidOperationException Missing(string op) =>
        new($"Phone-home {op} returned an empty payload.");
}
