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

    // CARD-0604 D-19 (Cut B). The custody surface the phone-home lane needs: which mechanism
    // this runner actually advertises, which custody store is its own, and a read/seal of a
    // tracked execution. Defaults keep every existing fake compiling AND refusing: a fake that
    // has not opted in advertises nothing, so no binding can be admitted through it.
    string? VerificationCustodyBackend => null;
    Guid RunnerStoreId => Guid.Empty;
    Task<VerificationCustodyStatus> ReadCustodyAsync(
        VerificationExecutionBinding binding, bool seal, CancellationToken ct) =>
        throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedTarget,
            "This runner cannot read verification custody.", 409);
}

public sealed class PhoneHomeCommandDispatcher
{
    private readonly IPhoneHomeRuntimeSurface _runtime;
    private readonly PhoneHomeSettings _settings;
    private readonly IProviderAuthProbe? _authProbe;
    private readonly object _mutationGate = new();
    private RunnerWorkspaceService? _workspace;

    public PhoneHomeCommandDispatcher(
        IPhoneHomeRuntimeSurface runtime, PhoneHomeSettings settings, IProviderAuthProbe? authProbe = null)
    {
        _runtime = runtime;
        _settings = settings;
        _authProbe = authProbe;
    }

    /// <summary>
    /// CARD-0604: the same DTO the Capabilities operation answers with, exposed so registration
    /// can carry it. One source, so a registration can never disagree with a later probe.
    /// </summary>
    public RunnerCapabilitiesDto Capabilities() => _runtime.Capabilities();

    private RunnerWorkspaceService Workspace() =>
        _workspace ??= new RunnerWorkspaceService(_settings.RunnerRepository, _settings.AllowedCwd);

    private async Task WriteSpillIfPresentAsync(PhoneHomeFrame request, CancellationToken ct)
    {
        if (request.Payload is not { } payload
            || payload.ValueKind != System.Text.Json.JsonValueKind.Object
            || !payload.TryGetProperty("spill", out var raw)
            || raw.ValueKind != System.Text.Json.JsonValueKind.Object)
            return;
        var spill = raw.Deserialize<PhoneHomeInputSpill>(PhoneHomeFraming.Json);
        if (spill is null || string.IsNullOrWhiteSpace(spill.RelativePath))
            return;
        if (!payload.TryGetProperty("runnerCwd", out var cwd) || cwd.ValueKind != System.Text.Json.JsonValueKind.String)
            throw new PhoneHomeAdmissionException(
                PhoneHomeProblemTypes.UnsupportedTarget, "A spill requires the session's runner cwd.", 409);
        await Workspace().WriteSpillAsync(cwd.GetString()!, spill, ct);
    }

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
                PhoneHomeOperation.ProviderAuth => Result(request, await ProviderAuthAsync(request, ct)),
                PhoneHomeOperation.Buffer => Result(request, _runtime.GetBuffer(ReadSessionId(request))),
                PhoneHomeOperation.Snapshot => Result(request, _runtime.GetSnapshot(ReadSessionId(request))),
                PhoneHomeOperation.Transcript => Result(request, _runtime.GetTranscript(ReadSessionId(request))),
                PhoneHomeOperation.Input => await MutateAsync(request, async () =>
                {
                    var body = request.Payload?.Deserialize<RunnerInputRequest>(PhoneHomeFraming.Json)
                        ?? throw new ArgumentException("Input body is required.");
                    // CARD-0604 G-21: a remote session's spilled body travels with the input and
                    // is written HERE, inside the session's own cwd. The desktop never writes it:
                    // its Cwd is a Windows path this process cannot see.
                    await WriteSpillIfPresentAsync(request, ct);
                    await _runtime.SendInputAsync(ReadSessionId(request), body.Input, ct);
                    return Result(request, new { ok = true });
                }),
                PhoneHomeOperation.WorkspaceMirror => await MutateAsync(request, async () =>
                {
                    var body = request.Payload?.Deserialize<PhoneHomeWorkspaceMirrorRequest>(PhoneHomeFraming.Json)
                        ?? throw new ArgumentException("Workspace mirror body is required.");
                    return Result(request, await Workspace().MirrorAsync(body, ct));
                }),
                PhoneHomeOperation.WorkspaceRemove => await MutateAsync(request, async () =>
                {
                    var body = request.Payload?.Deserialize<PhoneHomeWorkspaceRemoveRequest>(PhoneHomeFraming.Json)
                        ?? throw new ArgumentException("Workspace remove body is required.");
                    return Result(request, await Workspace().RemoveAsync(body, ct));
                }),
                // CARD-0604 D-19 (Cut B). Custody and the verification snapshot for a Mutation
                // bound to this runner. Each is a typed body; nothing here takes a shell string.
                PhoneHomeOperation.ReadCustody => await MutateAsync(request, async () =>
                {
                    var body = request.Payload?.Deserialize<PhoneHomeReadCustodyRequest>(PhoneHomeFraming.Json)
                        ?? throw new ArgumentException("Read-custody body is required.");
                    return Result(request, await _runtime.ReadCustodyAsync(body.Binding, body.Seal, ct));
                }),
                PhoneHomeOperation.VerificationWorkspaceCreate => await MutateAsync(request, async () =>
                {
                    var body = request.Payload?.Deserialize<PhoneHomeVerificationCreateRequest>(PhoneHomeFraming.Json)
                        ?? throw new ArgumentException("Verification create body is required.");
                    return Result(request, await Workspace().CreateVerificationAsync(body, ct));
                }),
                PhoneHomeOperation.VerificationWorkspaceValidate => await MutateAsync(request, async () =>
                {
                    var body = request.Payload?.Deserialize<PhoneHomeVerificationValidateRequest>(PhoneHomeFraming.Json)
                        ?? throw new ArgumentException("Verification validate body is required.");
                    return Result(request, await Workspace().ValidateVerificationAsync(body, ct));
                }),
                PhoneHomeOperation.VerificationWorkspaceInspect => Result(request,
                    await Workspace().InspectVerificationAsync(
                        request.Payload?.Deserialize<PhoneHomeVerificationInspectRequest>(PhoneHomeFraming.Json)
                        ?? throw new ArgumentException("Verification inspect body is required."), ct)),
                PhoneHomeOperation.VerificationWorkspaceReadRestoration => Result(request,
                    await Workspace().ReadVerificationRestorationAsync(
                        request.Payload?.Deserialize<PhoneHomeVerificationReadRestorationRequest>(PhoneHomeFraming.Json)
                        ?? throw new ArgumentException("Verification restoration body is required."), ct)),
                PhoneHomeOperation.VerificationWorkspaceRemove => await MutateAsync(request, async () =>
                {
                    var body = request.Payload?.Deserialize<PhoneHomeVerificationRemoveRequest>(PhoneHomeFraming.Json)
                        ?? throw new ArgumentException("Verification remove body is required.");
                    return Result(request, await Workspace().RemoveVerificationAsync(body, ct));
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

        await RejectSignedOutClaudeAsync(launch, ct);
        return await MutateAsync(request, async () => Result(request, await _runtime.StartAsync(launch, ct)));
    }

    /// <summary>
    /// CARD-0628 D-7. Read-only: the probe's DTO for a provider this runner can measure. An unknown
    /// provider is <see cref="PhoneHomeProblemTypes.UnsupportedTarget"/>; a runner built without a
    /// probe answers "cannot tell" rather than guessing.
    /// </summary>
    private async Task<RunnerProviderAuthDto> ProviderAuthAsync(PhoneHomeFrame request, CancellationToken ct)
    {
        var body = request.Payload?.Deserialize<PhoneHomeProviderAuthRequest>(PhoneHomeFraming.Json);
        if (body is null || !string.Equals(body.Provider, ClaudeAuthProbe.ProviderName, StringComparison.OrdinalIgnoreCase))
            throw new PhoneHomeAdmissionException(
                PhoneHomeProblemTypes.UnsupportedTarget,
                $"Provider '{body?.Provider}' has no auth probe on this runner; only '{ClaudeAuthProbe.ProviderName}' is measured.",
                400);
        if (_authProbe is null)
            return new RunnerProviderAuthDto(ClaudeAuthProbe.ProviderName, null, null, null, DateTimeOffset.UtcNow, "probe_unavailable");
        return await _authProbe.ProbeAsync(ClaudeAuthProbe.ProviderName, ct);
    }

    /// <summary>
    /// CARD-0628 D-7 backstop. The server's pre-flight should already have failed a task bound to
    /// a signed-out runner; this is the runner refusing to start Claude onto its sign-in screen
    /// when that pre-flight did not run (an older server, the setting off, a named agent). Only a
    /// definite "signed out" refuses: an unknown answer, or no probe, admits.
    /// </summary>
    private async Task RejectSignedOutClaudeAsync(RunnerLaunchRequest launch, CancellationToken ct)
    {
        if (!_settings.ClaudeAuthProbeEnabled || _authProbe is null || !IsClaudeExe(launch.Exe))
            return;
        var answer = await _authProbe.ProbeAsync(ClaudeAuthProbe.ProviderName, ct);
        if (answer.LoggedIn != false)
            return;
        throw new PhoneHomeAdmissionException(
            PhoneHomeProblemTypes.ProviderSignInRequired,
            $"Claude Code is not signed in on runner '{_settings.RunnerId}' (CLAUDE_CONFIG_DIR={_settings.ClaudeHome}). "
            + $"Provision {ClaudeAuthProbe.OAuthTokenVariable} (from `claude setup-token`) in the runner's environment, "
            + $"or run `claude auth login` as uid 1654 with CLAUDE_CONFIG_DIR={_settings.ClaudeHome}, then re-dispatch.",
            409);
    }

    /// <summary>
    /// CARD-0628 D-5: the image's own <c>claude</c>, by bare name or the exact path the runner
    /// Dockerfile installs it to. Any other path, even one ending in <c>/claude</c>
    /// (<c>/opt/evil/claude</c>, a <c>..</c> escape, a relative <c>./claude</c>), is not this image's.
    /// </summary>
    internal static bool IsClaudeExe(string? exe) =>
        string.Equals(exe, "claude", StringComparison.Ordinal)
        || string.Equals(exe, ImageClaudePath, StringComparison.Ordinal);

    /// <summary>Where <c>docker/session-runner-grok/Dockerfile</c> installs Claude Code.</summary>
    internal const string ImageClaudePath = "/usr/local/bin/claude";

    internal void RejectUnsupportedLaunch(RunnerLaunchRequest launch)
    {
        // CARD-0604 D-2 / CARD-0628 D-5: grok, claude, or an image-owned executable from the allow list. The runner keeps
        // its own copy of that list: it is the image's contract, and the server telling it to run
        // some other path is exactly what this refusal exists for.
        var isGrok = !string.IsNullOrWhiteSpace(launch.Exe)
            && (string.Equals(Path.GetFileName(launch.Exe), "grok", StringComparison.OrdinalIgnoreCase)
                || string.Equals(launch.Exe, "grok", StringComparison.OrdinalIgnoreCase)
                || launch.Exe.EndsWith("/grok", StringComparison.Ordinal));
        var isAllowedRaw = !string.IsNullOrWhiteSpace(launch.Exe)
            && _settings.RawExeAllowList.Any(allowed => string.Equals(allowed, launch.Exe, StringComparison.Ordinal));
        var isClaude = IsClaudeExe(launch.Exe);
        if (!isGrok && !isClaude && !isAllowedRaw)
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
        // CARD-0604 D-19 / G-37 (Cut B). A verification binding is admitted only when this
        // runner actually advertises a custody backend AND the binding names that exact backend
        // and this runner's own store. Cut A refused every binding outright because the runner
        // had no containment at all; refusing on capability rather than on principle is what
        // makes a Linux SourceLanding possible without ever fabricating a Windows receipt.
        if (launch.VerificationBinding is { } binding)
        {
            if (_runtime.VerificationCustodyBackend is not { } advertised)
                throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedTarget,
                    "This runner advertises no verification custody backend.", 409);
            if (!VerificationCustodyBackends.IsSupported(binding.Backend) || binding.Backend != advertised)
                throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedTarget,
                    "verification_custody_invalid_binding", 409);
            if (binding.RunnerStoreId != _runtime.RunnerStoreId)
                throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.StoreMismatch,
                    "verification_custody_invalid_binding", 409);
        }
        if (launch.MemoryLimitMb != 0)
            throw new PhoneHomeAdmissionException(PhoneHomeProblemTypes.UnsupportedTarget, "Memory limit must be zero.", 409);
        // CARD-0628 D-5: the two agents this image carries. Anything else (codex) has no tailer here.
        if (launch.TranscriptFormat is not null
            && !string.Equals(launch.TranscriptFormat, TranscriptFormats.Grok, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(launch.TranscriptFormat, TranscriptFormats.Claude, StringComparison.OrdinalIgnoreCase))
            throw new PhoneHomeAdmissionException(
                PhoneHomeProblemTypes.UnsupportedTarget, "Only the Grok and Claude transcript formats are admitted.", 409);
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
