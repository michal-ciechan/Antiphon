using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

public sealed class PhoneHomeSettings
{
    public bool Enabled { get; set; }
    public string RunnerId { get; set; } = "grok-linux";
    public string ServerOrigin { get; set; } = "";
    public string SecretPath { get; set; } = "/run/secrets/phone-home";
    public string StoreIdPath { get; set; } = "/state/runner-store-id";

    /// <summary>
    /// CARD-0679 R5 repair 2: where the accepted-launch-generation watermarks live (one file per
    /// session id), so a re-sent Launch is refused after a slot release or a runner restart. An enabled
    /// runner left unset uses <c>launch-generations</c> beside <see cref="StoreIdPath"/>, on the same state
    /// volume. With no path at all (a dispatcher built in a test) the watermark is in memory and
    /// survives a release only.
    /// </summary>
    public string? LaunchGenerationsPath { get; set; }

    /// <summary>The configured watermark directory, or the state-volume default beside the store id.</summary>
    public string ResolvedLaunchGenerationsPath() =>
        !string.IsNullOrWhiteSpace(LaunchGenerationsPath)
            ? LaunchGenerationsPath
            : Path.Combine(Path.GetDirectoryName(StoreIdPath) ?? ".", "launch-generations");

    public string AllowedCwd { get; set; } = "/work";

    /// <summary>
    /// CARD-0604 D-15: the runner-side checkout mirror worktrees are created from. Fetches from it
    /// are anonymous HTTPS; only pushes use the deploy key.
    /// </summary>
    public string RunnerRepository { get; set; } = "/work/repos/antiphon";
    public string GrokHome { get; set; } = "/state/grok";

    /// <summary>
    /// CARD-0628 D-6: Claude Code's <c>CLAUDE_CONFIG_DIR</c> on the runner, the store the auth probe
    /// inspects. The same value as the server's <c>PhoneHomeRunner:ChildClaudeHome</c> and the
    /// compose <c>CLAUDE_CONFIG_DIR</c>.
    /// </summary>
    public string ClaudeHome { get; set; } = "/state/claude";

    /// <summary>
    /// CARD-0628 D-7: refuse a <c>claude</c> launch with <c>provider_sign_in_required</c> when the
    /// probe says Claude is signed out. An unknown answer always admits.
    /// </summary>
    public bool ClaudeAuthProbeEnabled { get; set; } = true;

    /// <summary>
    /// CARD-0647: refuse a <c>grok</c> launch with <c>provider_sign_in_required</c> when
    /// <c>GROK_HOME/auth.json</c> is absent. An unknown answer always admits.
    /// </summary>
    public bool GrokAuthProbeEnabled { get; set; } = true;

    /// <summary>
    /// CARD-0660 D-3: Codex's <c>CODEX_HOME</c> on the runner, the store the auth probe inspects.
    /// The same value as the compose <c>CODEX_HOME</c> and the server's
    /// <c>PhoneHomeRunner:ChildCodexHome</c>.
    /// </summary>
    public string CodexHome { get; set; } = "/state/codex";

    /// <summary>
    /// CARD-0660 D-9: refuse a <c>codex</c> launch with <c>provider_sign_in_required</c> when
    /// <c>CODEX_HOME/auth.json</c> is absent. An unknown answer always admits.
    /// </summary>
    public bool CodexAuthProbeEnabled { get; set; } = true;

    /// <summary>
    /// CARD-0604 D-14: how many concurrent sessions this runner will hold. The server bounds it
    /// again at registration (<c>PhoneHomeRunner:MaxCapacity</c>), so a runner cannot enlarge
    /// itself past what the control plane allows.
    /// </summary>
    public int Capacity { get; set; } = 1;

    /// <summary>
    /// Image-owned executables a projected Raw launch may name, alongside "grok". Anything else,
    /// including a host path that happens to end in the same file name, is refused.
    /// </summary>
    public IReadOnlyList<string> RawExeAllowList { get; set; } = ["/bin/sh", "/bin/bash", "/usr/local/bin/pwsh"];
    public int HeartbeatSeconds { get; set; } = PhoneHomeProtocol.DefaultHeartbeatSeconds;
    public int ReconnectBackoffMs { get; set; } = 1000;
    public int ReconnectBackoffMaxMs { get; set; } = 15_000;
    public PhoneHomeLimits Limits { get; set; } = new();

    public void Validate()
    {
        if (!Enabled)
            return;
        if (string.IsNullOrWhiteSpace(RunnerId) || RunnerId.Length > 64)
            throw new InvalidOperationException("PhoneHome:RunnerId must be a non-empty string of at most 64 characters.");
        if (!Uri.TryCreate(ServerOrigin, UriKind.Absolute, out var origin)
            || (origin.Scheme != Uri.UriSchemeHttp && origin.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("PhoneHome:ServerOrigin must be an absolute http(s) URI.");
        if (string.IsNullOrWhiteSpace(SecretPath))
            throw new InvalidOperationException("PhoneHome:SecretPath must not be empty.");
        if (string.IsNullOrWhiteSpace(StoreIdPath))
            throw new InvalidOperationException("PhoneHome:StoreIdPath must not be empty.");
        if (string.IsNullOrWhiteSpace(AllowedCwd) || !AllowedCwd.StartsWith('/'))
            throw new InvalidOperationException("PhoneHome:AllowedCwd must be a POSIX absolute path.");
        if (string.IsNullOrWhiteSpace(RunnerRepository) || !RunnerRepository.StartsWith('/'))
            throw new InvalidOperationException("PhoneHome:RunnerRepository must be a POSIX absolute path.");
        if (string.IsNullOrWhiteSpace(ClaudeHome) || !ClaudeHome.StartsWith('/'))
            throw new InvalidOperationException("PhoneHome:ClaudeHome must be a POSIX absolute path.");
        if (string.IsNullOrWhiteSpace(CodexHome) || !CodexHome.StartsWith('/'))
            throw new InvalidOperationException("PhoneHome:CodexHome must be a POSIX absolute path.");
        if (Capacity < 1)
            throw new InvalidOperationException("PhoneHome:Capacity must be positive.");
        if (RawExeAllowList.Any(exe => string.IsNullOrWhiteSpace(exe) || !exe.StartsWith('/')))
            throw new InvalidOperationException("PhoneHome:RawExeAllowList entries must be POSIX absolute paths.");
        if (HeartbeatSeconds <= 0)
            throw new InvalidOperationException("PhoneHome:HeartbeatSeconds must be positive.");
        Limits.Validate("PhoneHome:Limits");
    }
}
