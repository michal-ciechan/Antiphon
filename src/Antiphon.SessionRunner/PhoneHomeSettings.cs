using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

public sealed class PhoneHomeSettings
{
    public bool Enabled { get; set; }
    public string RunnerId { get; set; } = "grok-linux";
    public string ServerOrigin { get; set; } = "";
    public string SecretPath { get; set; } = "/run/secrets/phone-home";
    public string StoreIdPath { get; set; } = "/state/runner-store-id";
    public string AllowedCwd { get; set; } = "/work";

    /// <summary>
    /// CARD-0604 D-15: the runner-side checkout mirror worktrees are created from. Fetches from it
    /// are anonymous HTTPS; only pushes use the deploy key.
    /// </summary>
    public string RunnerRepository { get; set; } = "/work/repos/antiphon";
    public string GrokHome { get; set; } = "/state/grok";

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
        if (Capacity < 1)
            throw new InvalidOperationException("PhoneHome:Capacity must be positive.");
        if (RawExeAllowList.Any(exe => string.IsNullOrWhiteSpace(exe) || !exe.StartsWith('/')))
            throw new InvalidOperationException("PhoneHome:RawExeAllowList entries must be POSIX absolute paths.");
        if (HeartbeatSeconds <= 0)
            throw new InvalidOperationException("PhoneHome:HeartbeatSeconds must be positive.");
        Limits.Validate("PhoneHome:Limits");
    }
}
