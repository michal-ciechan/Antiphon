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
    public string GrokHome { get; set; } = "/state/grok";
    public int Capacity { get; set; } = 1;
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
        if (Capacity != 1)
            throw new InvalidOperationException("PhoneHome:Capacity must be 1 for this slice.");
        if (HeartbeatSeconds <= 0)
            throw new InvalidOperationException("PhoneHome:HeartbeatSeconds must be positive.");
        Limits.Validate("PhoneHome:Limits");
    }
}
