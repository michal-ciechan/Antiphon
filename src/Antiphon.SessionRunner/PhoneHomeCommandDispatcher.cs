using System.Text.Json;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

public interface IPhoneHomeRuntimeSurface
{
    RunnerCapabilitiesDto Capabilities();
    string Health();
    IReadOnlyList<RunnerSessionDto> List();
    Task<RunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct);
    Task<RunnerSessionDto> StartAsync(RunnerLaunchRequest request, CancellationToken ct);
    RunnerBufferDto GetBuffer(Guid sessionId);
    RunnerSnapshotDto GetSnapshot(Guid sessionId);
    RunnerTranscriptDto GetTranscript(Guid sessionId);
    Task SendInputAsync(Guid sessionId, string input, CancellationToken ct);
    Task<RunnerConditionalInputResult> SendConditionalInputAsync(Guid sessionId, RunnerConditionalInputRequest request, CancellationToken ct);
    Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct);
    Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct);
    Task<RunnerKillGenerationResult> KillGenerationAsync(Guid sessionId, DateTime expectedAcceptedStartedAt, CancellationToken ct);
    int OwnedSessionCount { get; }
}

public sealed class PhoneHomeCommandDispatcher
{
    private readonly IPhoneHomeRuntimeSurface _runtime;
    private readonly PhoneHomeSettings _settings;
    private readonly object _mutationGate = new();

    public PhoneHomeCommandDispatcher(IPhoneHomeRuntimeSurface runtime, PhoneHomeSettings settings)
    {
        _runtime = runtime;
        _settings = settings;
    }

    /// <summary>
    /// CARD-0604: the same DTO the Capabilities operation answers with, exposed so registration
    /// can carry it. One source, so a registration can never disagree with a later probe.
    /// </summary>
    public RunnerCapabilitiesDto Capabilities() => _runtime.Capabilities();

    public async Task<PhoneHomeFrame> DispatchAsync(PhoneHomeFrame request, CancellationToken ct)
    {
        if (request.Kind != PhoneHomeFrameKind.Request || request.Operation is null)
            return Error(request, PhoneHomeProblemTypes.UnsupportedOperation, "Frame is not a request.", 400);

        try
        {
            return request.Operation.Value switch
            {
                PhoneHomeOperation.Capabilities => Result(request, _runtime.Capabilities()),
                PhoneHomeOperation.Health => Result(request, new { status = _runtime.Health() }),
                PhoneHomeOperation.List => Result(request, _runtime.List()),
                PhoneHomeOperation.Get => Result(request, await _runtime.GetAsync(ReadSessionId(request), ct)),
                PhoneHomeOperation.Launch => await LaunchAsync(request, ct),
                PhoneHomeOperation.Buffer => Result(request, _runtime.GetBuffer(ReadSessionId(request))),
                PhoneHomeOperation.Snapshot => Result(request, _runtime.GetSnapshot(ReadSessionId(request))),
                PhoneHomeOperation.Transcript => Result(request, _runtime.GetTranscript(ReadSessionId(request))),
                PhoneHomeOperation.Input => await MutateAsync(request, async () =>
                {
                    var body = request.Payload?.Deserialize<RunnerInputRequest>(PhoneHomeFraming.Json)
                        ?? throw new ArgumentException("Input body is required.");
                    await _runtime.SendInputAsync(ReadSessionId(request), body.Input, ct);
                    return Result(request, new { ok = true });
                }),
                PhoneHomeOperation.ConditionalInput => await MutateAsync(request, async () =>
                {
                    var body = request.Payload?.Deserialize<RunnerConditionalInputRequest>(PhoneHomeFraming.Json)
                        ?? throw new ArgumentException("Conditional input body is required.");
                    return Result(request, await _runtime.SendConditionalInputAsync(ReadSessionId(request), body, ct));
                }),
                PhoneHomeOperation.ClearBuffer => await MutateAsync(request, async () =>
                {
                    await _runtime.ClearLiveBufferAsync(ReadSessionId(request), ct);
                    return Result(request, new { ok = true });
                }),
                PhoneHomeOperation.Resize => await MutateAsync(request, async () =>
                {
                    var body = request.Payload?.Deserialize<RunnerResizeRequest>(PhoneHomeFraming.Json)
                        ?? throw new ArgumentException("Resize body is required.");
                    await _runtime.ResizeAsync(ReadSessionId(request), body.Cols, body.Rows, ct);
                    return Result(request, new { ok = true });
                }),
                PhoneHomeOperation.KillGeneration => await MutateAsync(request, async () =>
                {
                    var body = request.Payload?.Deserialize<RunnerKillGenerationRequest>(PhoneHomeFraming.Json)
                        ?? throw new ArgumentException("Kill-generation body is required.");
                    return Result(request, await _runtime.KillGenerationAsync(ReadSessionId(request), body.ExpectedAcceptedStartedAt, ct));
                }),
                _ => Error(request, PhoneHomeProblemTypes.UnsupportedOperation, $"Operation '{request.Operation}' is not supported.", 400),
            };
        }
        catch (PhoneHomeAdmissionException ex)
        {
            return Error(request, ex.Code, ex.Message, ex.StatusCode);
        }
        catch (KeyNotFoundException ex)
        {
            return Error(request, "not_found", ex.Message, 404);
        }
        catch (VerificationCustodyException ex)
        {
            return Error(request, ex.Code, ex.Message, 409);
        }
        catch (GrokRulesLaunchException ex)
        {
            return Error(request, "grok_rules", ex.Message, 409);
        }
        catch (HerdrLaunchException ex)
        {
            return Error(request, PhoneHomeProblemTypes.UnsupportedTarget, ex.Message, 409);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Error(request, PhoneHomeProblemTypes.UnsupportedTarget, ex.Message, 400);
        }
    }

    private async Task<PhoneHomeFrame> LaunchAsync(PhoneHomeFrame request, CancellationToken ct)
    {
        var launch = request.Payload?.Deserialize<RunnerLaunchRequest>(PhoneHomeFraming.Json)
            ?? throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedTarget, "Launch body is required.", 400);
        RejectUnsupportedLaunch(launch);
        lock (_mutationGate)
        {
            if (_runtime.OwnedSessionCount >= _settings.Capacity)
                throw new PhoneHomeAdmissionException(
                    PhoneHomeProblemTypes.Capacity,
                    $"Phone-home capacity is {_settings.Capacity} session(s).",
                    409);
        }

        return await MutateAsync(request, async () => Result(request, await _runtime.StartAsync(launch, ct)));
    }

    internal void RejectUnsupportedLaunch(RunnerLaunchRequest launch)
    {
        // CARD-0604 D-2: grok, or an image-owned executable from the allow list. The runner keeps
        // its own copy of that list: it is the image's contract, and the server telling it to run
        // some other path is exactly what this refusal exists for.
        var isGrok = !string.IsNullOrWhiteSpace(launch.Exe)
            && (string.Equals(Path.GetFileName(launch.Exe), "grok", StringComparison.OrdinalIgnoreCase)
                || string.Equals(launch.Exe, "grok", StringComparison.OrdinalIgnoreCase)
                || launch.Exe.EndsWith("/grok", StringComparison.Ordinal));
        var isAllowedRaw = !string.IsNullOrWhiteSpace(launch.Exe)
            && _settings.RawExeAllowList.Any(allowed => string.Equals(allowed, launch.Exe, StringComparison.Ordinal));
        if (!isGrok && !isAllowedRaw)
            throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedTarget, "Only an image-owned executable may launch.", 409);

        // CARD-0604 D-15: the workspace root itself, or a mirror worktree directly under it. A
        // path that merely starts with the same characters ("/workspace") is not under "/work",
        // and "/work/worktrees/../.." must not escape it.
        if (!IsAdmittedCwd(launch.Cwd))
            throw new PhoneHomeAdmissionException(
                PhoneHomeProblemTypes.UnsupportedTarget,
                $"cwd must be '{_settings.AllowedCwd}' or a worktree directly under '{_settings.AllowedCwd}/worktrees/'.",
                409);
        if (!string.Equals(launch.Backend, SessionBackends.PtyHost, StringComparison.OrdinalIgnoreCase)
            && launch.Backend is not null)
            throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedTarget, "Herdr and unknown backends are refused.", 409);
        if (launch.Herdr is not null)
            throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedTarget, "Herdr launches are refused.", 409);
        if (launch.VerificationBinding is not null)
            throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedTarget, "Verification custody is refused.", 409);
        if (launch.MemoryLimitMb != 0)
            throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedTarget, "Memory limit must be zero.", 409);
        if (!string.Equals(launch.TranscriptFormat, TranscriptFormats.Grok, StringComparison.OrdinalIgnoreCase)
            && launch.TranscriptFormat is not null)
            throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedTarget, "Only the Grok transcript format is admitted.", 409);
    }

    /// <summary>
    /// CARD-0604 D-15/G-22. The allowed cwd, or a single-segment mirror directly under its
    /// <c>worktrees/</c> directory. Traversal, absolute escapes and sibling prefixes are refused.
    /// </summary>
    internal bool IsAdmittedCwd(string? cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd))
            return false;
        var root = _settings.AllowedCwd.TrimEnd('/');
        if (string.Equals(cwd, root, StringComparison.Ordinal))
            return true;
        var prefix = root + "/worktrees/";
        if (!cwd.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        var name = cwd[prefix.Length..];
        return name.Length > 0
            && !name.Contains('/', StringComparison.Ordinal)
            && name != "."
            && name != "..";
    }

    private async Task<PhoneHomeFrame> MutateAsync(PhoneHomeFrame request, Func<Task<PhoneHomeFrame>> action)
    {
        lock (_mutationGate)
        {
            // Serialize mutating commands for the single session without blocking the receive pump:
            // the caller awaits this task off the socket loop.
        }

        await MutationLock.WaitAsync();
        try
        {
            return await action();
        }
        finally
        {
            MutationLock.Release();
        }
    }

    private static Guid ReadSessionId(PhoneHomeFrame request)
    {
        if (request.Payload is { } payload && payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("sessionId", out var id))
            return id.GetGuid();
        throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedTarget, "sessionId is required.", 400);
    }

    private static PhoneHomeFrame Result(PhoneHomeFrame request, object payload) =>
        new(PhoneHomeFrameKind.Result, request.Epoch, request.RequestId, request.Operation,
            JsonSerializer.SerializeToElement(payload, PhoneHomeFraming.Json));

    private static PhoneHomeFrame Error(PhoneHomeFrame request, string code, string detail, int status) =>
        new(PhoneHomeFrameKind.Error, request.Epoch, request.RequestId, request.Operation,
            ErrorCode: code, ErrorDetail: detail, StatusCode: status);

    private readonly SemaphoreSlim MutationLock = new(1, 1);
}

public sealed class PhoneHomeAdmissionException(string code, string message, int statusCode) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}
