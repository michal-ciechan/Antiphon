using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Settings;

public sealed class PhoneHomeRunnerSettings
{
    public bool Enabled { get; set; }
    public string AllowedRunnerId { get; set; } = "grok-linux";
    public Guid StandingAgentId { get; set; }
    public string HostWorkspaceRoot { get; set; } = "";
    public string RunnerWorkspace { get; set; } = "/work";
    public string ChildGrokHome { get; set; } = "/state/grok";
    public string CallbackOrigin { get; set; } = "";
    public string SharedSecret { get; set; } = "";
    public int TicketTtlSeconds { get; set; } = 30;
    public int HeartbeatSeconds { get; set; } = PhoneHomeProtocol.DefaultHeartbeatSeconds;
    public int LeaseSeconds { get; set; } = PhoneHomeProtocol.DefaultLeaseSeconds;
    public PhoneHomeLimits Limits { get; set; } = new();
}

public static class PhoneHomeRunnerSettingsRules
{
    public static IReadOnlyList<string> Validate(PhoneHomeRunnerSettings options)
    {
        var failures = new List<string>();
        if (!options.Enabled)
            return failures;
        if (string.IsNullOrWhiteSpace(options.AllowedRunnerId) || options.AllowedRunnerId.Length > 64)
            failures.Add("PhoneHomeRunner:AllowedRunnerId must be a non-empty string of at most 64 characters.");
        if (options.StandingAgentId == Guid.Empty)
            failures.Add("PhoneHomeRunner:StandingAgentId must be set when enabled.");
        if (string.IsNullOrWhiteSpace(options.HostWorkspaceRoot))
            failures.Add("PhoneHomeRunner:HostWorkspaceRoot must be set when enabled.");
        if (string.IsNullOrWhiteSpace(options.RunnerWorkspace) || !options.RunnerWorkspace.StartsWith('/'))
            failures.Add("PhoneHomeRunner:RunnerWorkspace must be a POSIX absolute path.");
        if (string.IsNullOrWhiteSpace(options.ChildGrokHome) || !options.ChildGrokHome.StartsWith('/'))
            failures.Add("PhoneHomeRunner:ChildGrokHome must be a POSIX absolute path.");
        if (!Uri.TryCreate(options.CallbackOrigin, UriKind.Absolute, out var origin)
            || (origin.Scheme != Uri.UriSchemeHttp && origin.Scheme != Uri.UriSchemeHttps))
            failures.Add("PhoneHomeRunner:CallbackOrigin must be an absolute http(s) URI.");
        if (string.IsNullOrWhiteSpace(options.SharedSecret))
            failures.Add("PhoneHomeRunner:SharedSecret must be set when enabled.");
        if (options.TicketTtlSeconds <= 0)
            failures.Add("PhoneHomeRunner:TicketTtlSeconds must be positive.");
        if (options.HeartbeatSeconds <= 0)
            failures.Add("PhoneHomeRunner:HeartbeatSeconds must be positive.");
        if (options.LeaseSeconds <= 0)
            failures.Add("PhoneHomeRunner:LeaseSeconds must be positive.");
        try { options.Limits.Validate("PhoneHomeRunner:Limits"); }
        catch (InvalidOperationException ex) { failures.Add(ex.Message); }
        return failures;
    }
}
