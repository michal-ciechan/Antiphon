using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Settings;

public sealed class PhoneHomeRunnerSettings
{
    public bool Enabled { get; set; }
    public string AllowedRunnerId { get; set; } = "grok-linux";

    /// <summary>
    /// CARD-0604 D-2/D-14: when true the runner is a bounded pool rather than one pinned agent,
    /// so <see cref="StandingAgentId"/> becomes optional and delegated Worktree tasks may be
    /// routed to it. Card-backed starts, OnAgent, Shared, ReadOnly and pins stay refused.
    /// </summary>
    public bool AllowDelegatedTasks { get; set; }

    public Guid StandingAgentId { get; set; }
    public string HostWorkspaceRoot { get; set; } = "";
    public string RunnerWorkspace { get; set; } = "/work";

    /// <summary>The runner-side checkout the mirror worktrees are created from (CARD-0604 D-15).</summary>
    public string RunnerRepository { get; set; } = "/work/repos/antiphon";

    /// <summary>
    /// Image-owned executables a runner-bound Raw agent may project to. Anything outside this list
    /// (or "grok") is refused: the runner must never be told to run an arbitrary host path.
    /// </summary>
    public IReadOnlyList<string> RawExeAllowList { get; set; } = ["/bin/sh", "/bin/bash", "/usr/local/bin/pwsh"];

    /// <summary>Upper bound on a registration's declared capacity (CARD-0604 D-14).</summary>
    public int MaxCapacity { get; set; } = 10;

    public string ChildGrokHome { get; set; } = "/state/grok";
    public string ChildClaudeHome { get; set; } = "/state/claude";
    public bool ClaudeAuthProbeEnabled { get; set; } = true;

    /// <summary>
    /// CARD-0660 D-3: the runner-only Codex home, projected as <c>CODEX_HOME</c> on every
    /// runner-bound Codex launch. It must agree with the runner's compose <c>CODEX_HOME</c> and
    /// <c>PhoneHome__CodexHome</c>; a desktop or <c>/tmp</c> home is refused.
    /// </summary>
    public string ChildCodexHome { get; set; } = "/state/codex";

    /// <summary>
    /// CARD-0660 D-9: a runner-bound Codex create/retry asks THAT runner whether its Codex home has
    /// a login. Only a definite "no" refuses; the answer is a presence hint, never token validity.
    /// </summary>
    public bool CodexAuthProbeEnabled { get; set; } = true;
    public string CallbackOrigin { get; set; } = "";
    public string SharedSecret { get; set; } = "";
    public int TicketTtlSeconds { get; set; } = 30;
    public int HeartbeatSeconds { get; set; } = PhoneHomeProtocol.DefaultHeartbeatSeconds;
    public int LeaseSeconds { get; set; } = PhoneHomeProtocol.DefaultLeaseSeconds;

    /// <summary>
    /// CARD-0633 D-2: after a catch-up List fails or times out, the recovery pump waits this long
    /// (on the live connection's clock) before asking again. The runner stays dispatch-ineligible
    /// meanwhile; without the delay a fast-failing List would be re-sent every 50 ms.
    /// </summary>
    public int CatchUpRetrySeconds { get; set; } = 5;

    /// <summary>
    /// CARD-0679 D-10: while a connection is recovered, the recovery pump re-reads the runner's
    /// List this often to refresh the cached inventory <c>ListLiveSessions</c> reads. Launch acks,
    /// exits and kills update it between refreshes. <c>0</c> or less disables the refresh.
    /// </summary>
    public int InventoryRefreshSeconds { get; set; } = 30;

    /// <summary>
    /// CARD-0679 D-10 (review 57fa2e6a): a cached inventory entry that no refresh List, launch ack
    /// or event has confirmed for this many <see cref="InventoryRefreshSeconds"/> intervals no longer
    /// counts as live, so refreshes that keep failing while heartbeats hold the lease cannot keep a
    /// lost session "live" forever. The pump logs a Warning when this many refreshes in a row fail.
    /// <c>0</c> or less (or a disabled refresh) removes the bound.
    /// </summary>
    public int InventoryStaleAfterRefreshes { get; set; } = 3;

    /// <summary>The age past which a cached inventory entry stops counting as live, or null for no bound.</summary>
    public TimeSpan? InventoryMaxAge =>
        InventoryRefreshSeconds > 0 && InventoryStaleAfterRefreshes > 0
            ? TimeSpan.FromSeconds((double)InventoryRefreshSeconds * InventoryStaleAfterRefreshes)
            : null;

    /// <summary>
    /// CARD-0679 D-3: how long the recovery pump trusts a "not ours" owner lookup for one session on
    /// one connection before reading the binding again (a match is trusted for the connection's
    /// life). Short, so a session row committed just after its first event is still picked up;
    /// <c>0</c> re-reads every miss.
    /// </summary>
    public int OwnerCacheNegativeSeconds { get; set; } = 5;

    /// <summary>
    /// CARD-0679 D-8: how many times a remote launch retries its Start-to-ready segment after the
    /// phone-home connection is lost under it (re-attach after the runner acknowledged the Launch,
    /// re-launch before). <c>0</c> fails on the first loss.
    /// </summary>
    public int LaunchTransportRetries { get; set; } = 2;

    /// <summary>
    /// CARD-0679 D-8: how long one retry waits for the runner to be dispatch-eligible again before
    /// the launch fails. Defaults to the lease; the row stays Starting while it waits.
    /// </summary>
    public int LaunchReattachWaitSeconds { get; set; } = PhoneHomeProtocol.DefaultLeaseSeconds;

    /// <summary>
    /// CARD-0653: the owner-only file holding the operator credential for every operator surface.
    /// Empty means <c>%LOCALAPPDATA%\Antiphon\operator-token</c> (else the XDG data home). Set
    /// from <c>Operator:TokenPath</c> first, this legacy key second (CARD-0676 F-1; Program.cs).
    /// </summary>
    public string OperatorTokenPath { get; set; } = "";

    /// <summary>CARD-0653: how often pending slot-release intents are finished (audit only).</summary>
    public string SlotReconcileCron { get; set; } = "*/2 * * * *";

    public PhoneHomeLimits Limits { get; set; } = new();

    /// <summary>
    /// CARD-0710 D-6. When this map is non-empty it is the only remote configuration: the singleton
    /// keys above are ignored. An empty map normalizes those keys into one entry.
    /// </summary>
    public Dictionary<string, PhoneHomeRunnerEntry> Runners { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>One configured phone-home runner. Secrets stay on the entry; nothing here is logged.</summary>
public sealed class PhoneHomeRunnerEntry
{
    public bool Enabled { get; set; } = true;
    public string DisplayName { get; set; } = "";
    public bool AllowDelegatedTasks { get; set; }
    public Guid StandingAgentId { get; set; }
    public string HostWorkspaceRoot { get; set; } = "";
    public string RunnerWorkspace { get; set; } = "/work";
    public string RunnerRepository { get; set; } = "/work/repos/antiphon";
    public IReadOnlyList<string> RawExeAllowList { get; set; } = ["/bin/sh", "/bin/bash", "/usr/local/bin/pwsh"];
    public int MaxCapacity { get; set; } = 10;
    public string ChildGrokHome { get; set; } = "/state/grok";
    public string ChildClaudeHome { get; set; } = "/state/claude";
    public bool ClaudeAuthProbeEnabled { get; set; } = true;
    public string ChildCodexHome { get; set; } = "/state/codex";
    public bool CodexAuthProbeEnabled { get; set; } = true;
    public string CallbackOrigin { get; set; } = "";
    public string SharedSecret { get; set; } = "";
}

public sealed record ResolvedPhoneHomeRunner(string Id, PhoneHomeRunnerEntry Entry);

public static class PhoneHomeRunnerCatalog
{
    public static bool UsesMap(PhoneHomeRunnerSettings settings) => settings.Runners is { Count: > 0 };

    /// <summary>Enabled remotes. Empty when phone-home is off. Map entries win over the singleton.</summary>
    public static IReadOnlyList<ResolvedPhoneHomeRunner> Resolve(PhoneHomeRunnerSettings settings)
    {
        if (!settings.Enabled)
            return [];
        if (UsesMap(settings))
        {
            var list = new List<ResolvedPhoneHomeRunner>();
            foreach (var (key, entry) in settings.Runners)
            {
                if (string.IsNullOrWhiteSpace(key) || entry is not { Enabled: true })
                    continue;
                list.Add(new ResolvedPhoneHomeRunner(key.Trim(), entry));
            }

            return list;
        }

        if (string.IsNullOrWhiteSpace(settings.AllowedRunnerId))
            return [];
        return [new ResolvedPhoneHomeRunner(settings.AllowedRunnerId.Trim(), FromLegacy(settings))];
    }

    /// <summary>Every map entry, including disabled ones. Legacy mode is the one normalized entry.</summary>
    public static IReadOnlyList<ResolvedPhoneHomeRunner> Configured(PhoneHomeRunnerSettings settings)
    {
        if (UsesMap(settings))
        {
            var list = new List<ResolvedPhoneHomeRunner>();
            foreach (var (key, entry) in settings.Runners)
            {
                if (string.IsNullOrWhiteSpace(key) || entry is null)
                    continue;
                list.Add(new ResolvedPhoneHomeRunner(key.Trim(), entry));
            }

            return list;
        }

        return Resolve(settings);
    }

    public static PhoneHomeRunnerEntry FromLegacy(PhoneHomeRunnerSettings settings) => new()
    {
        Enabled = true,
        DisplayName = settings.AllowedRunnerId.Trim(),
        AllowDelegatedTasks = settings.AllowDelegatedTasks,
        StandingAgentId = settings.StandingAgentId,
        HostWorkspaceRoot = settings.HostWorkspaceRoot,
        RunnerWorkspace = settings.RunnerWorkspace,
        RunnerRepository = settings.RunnerRepository,
        RawExeAllowList = settings.RawExeAllowList,
        MaxCapacity = settings.MaxCapacity,
        ChildGrokHome = settings.ChildGrokHome,
        ChildClaudeHome = settings.ChildClaudeHome,
        ClaudeAuthProbeEnabled = settings.ClaudeAuthProbeEnabled,
        ChildCodexHome = settings.ChildCodexHome,
        CodexAuthProbeEnabled = settings.CodexAuthProbeEnabled,
        CallbackOrigin = settings.CallbackOrigin,
        SharedSecret = settings.SharedSecret,
    };
}

public static class PhoneHomeRunnerSettingsRules
{
    public static IReadOnlyList<string> Validate(PhoneHomeRunnerSettings options)
    {
        var failures = new List<string>();
        if (!options.Enabled)
            return failures;
        if (PhoneHomeRunnerCatalog.UsesMap(options))
        {
            ValidateMapped(options, failures);
            ValidateGlobal(options, failures);
            return failures;
        }

        if (string.IsNullOrWhiteSpace(options.AllowedRunnerId) || options.AllowedRunnerId.Length > 64)
            failures.Add("PhoneHomeRunner:AllowedRunnerId must be a non-empty string of at most 64 characters.");
        // CARD-0659 D-1: "local" is the reserved desktop token in task creation and the directory.
        else if (options.AllowedRunnerId.Trim().Equals(PhoneHomeProtocol.LocalRunnerId, StringComparison.OrdinalIgnoreCase)
            || options.AllowedRunnerId.Trim().Equals(RunnerPlatformWire.DesktopId, StringComparison.OrdinalIgnoreCase))
            failures.Add($"PhoneHomeRunner:AllowedRunnerId must not be the reserved desktop id '{PhoneHomeProtocol.LocalRunnerId}' or '{RunnerPlatformWire.DesktopId}'.");
        // CARD-0604 D-14: a pinned standing agent is still required when the runner is NOT a pool.
        // With AllowDelegatedTasks the dispatcher creates its own pool delegates, so pinning one
        // named agent would be a second, contradictory shape rather than a safety net.
        if (options.StandingAgentId == Guid.Empty && !options.AllowDelegatedTasks)
            failures.Add("PhoneHomeRunner:StandingAgentId must be set when enabled unless AllowDelegatedTasks is true.");
        if (string.IsNullOrWhiteSpace(options.HostWorkspaceRoot))
            failures.Add("PhoneHomeRunner:HostWorkspaceRoot must be set when enabled.");
        if (string.IsNullOrWhiteSpace(options.RunnerWorkspace) || !options.RunnerWorkspace.StartsWith('/'))
            failures.Add("PhoneHomeRunner:RunnerWorkspace must be a POSIX absolute path.");
        if (string.IsNullOrWhiteSpace(options.RunnerRepository) || !options.RunnerRepository.StartsWith('/'))
            failures.Add("PhoneHomeRunner:RunnerRepository must be a POSIX absolute path.");
        if (options.MaxCapacity <= 0)
            failures.Add("PhoneHomeRunner:MaxCapacity must be positive.");
        if (options.RawExeAllowList.Any(exe => string.IsNullOrWhiteSpace(exe) || !exe.StartsWith('/')))
            failures.Add("PhoneHomeRunner:RawExeAllowList entries must be POSIX absolute paths.");
        if (string.IsNullOrWhiteSpace(options.ChildGrokHome) || !options.ChildGrokHome.StartsWith('/'))
            failures.Add("PhoneHomeRunner:ChildGrokHome must be a POSIX absolute path.");
        if (string.IsNullOrWhiteSpace(options.ChildClaudeHome) || !options.ChildClaudeHome.StartsWith('/'))
            failures.Add("PhoneHomeRunner:ChildClaudeHome must be a POSIX absolute path.");
        // CARD-0660 D-3: the persistent runner-state home, never a temporary one.
        if (string.IsNullOrWhiteSpace(options.ChildCodexHome) || !options.ChildCodexHome.StartsWith('/')
            || options.ChildCodexHome.Split('/').Contains(".."))
            failures.Add("PhoneHomeRunner:ChildCodexHome must be a POSIX absolute path.");
        else if (options.ChildCodexHome.TrimEnd('/') is "/tmp" or "" || options.ChildCodexHome.StartsWith("/tmp/", StringComparison.Ordinal))
            failures.Add("PhoneHomeRunner:ChildCodexHome must be a persistent path, not /tmp or the filesystem root.");
        if (!Uri.TryCreate(options.CallbackOrigin, UriKind.Absolute, out var origin)
            || (origin.Scheme != Uri.UriSchemeHttp && origin.Scheme != Uri.UriSchemeHttps))
            failures.Add("PhoneHomeRunner:CallbackOrigin must be an absolute http(s) URI.");
        if (string.IsNullOrWhiteSpace(options.SharedSecret))
            failures.Add("PhoneHomeRunner:SharedSecret must be set when enabled.");
        ValidateGlobal(options, failures);
        return failures;
    }

    private static void ValidateGlobal(PhoneHomeRunnerSettings options, List<string> failures)
    {
        if (options.TicketTtlSeconds <= 0)
            failures.Add("PhoneHomeRunner:TicketTtlSeconds must be positive.");
        if (options.HeartbeatSeconds <= 0)
            failures.Add("PhoneHomeRunner:HeartbeatSeconds must be positive.");
        if (options.LeaseSeconds <= 0)
            failures.Add("PhoneHomeRunner:LeaseSeconds must be positive.");
        if (options.CatchUpRetrySeconds < 1)
            failures.Add("PhoneHomeRunner:CatchUpRetrySeconds must be at least 1.");
        if (options.OwnerCacheNegativeSeconds < 0)
            failures.Add("PhoneHomeRunner:OwnerCacheNegativeSeconds must not be negative.");
        if (options.LaunchTransportRetries < 0)
            failures.Add("PhoneHomeRunner:LaunchTransportRetries must not be negative.");
        if (options.LaunchReattachWaitSeconds < 1)
            failures.Add("PhoneHomeRunner:LaunchReattachWaitSeconds must be at least 1.");
        if (!string.IsNullOrWhiteSpace(options.OperatorTokenPath) && !Path.IsPathFullyQualified(options.OperatorTokenPath))
            failures.Add("Operator:TokenPath (alias PhoneHomeRunner:OperatorTokenPath) must be an absolute path.");
        if (string.IsNullOrWhiteSpace(options.SlotReconcileCron))
            failures.Add("PhoneHomeRunner:SlotReconcileCron must be set when enabled.");
        try { options.Limits.Validate("PhoneHomeRunner:Limits"); }
        catch (InvalidOperationException ex) { failures.Add(ex.Message); }
    }

    private static void ValidateMapped(PhoneHomeRunnerSettings options, List<string> failures)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pins = new HashSet<Guid>();
        foreach (var (key, entry) in options.Runners)
        {
            if (string.IsNullOrWhiteSpace(key) || entry is null)
            {
                failures.Add("PhoneHomeRunner:Runners keys must be non-empty runner ids.");
                continue;
            }

            var id = key.Trim();
            if (id.Length > 64 || id.Any(char.IsControl))
                failures.Add($"PhoneHomeRunner:Runners:{id} must be at most 64 characters and contain no controls.");
            else if (id.Equals(PhoneHomeProtocol.LocalRunnerId, StringComparison.OrdinalIgnoreCase)
                || id.Equals(RunnerPlatformWire.DesktopId, StringComparison.OrdinalIgnoreCase))
                failures.Add($"PhoneHomeRunner:Runners:{id} is reserved for the desktop.");
            if (!seen.Add(id))
                failures.Add($"PhoneHomeRunner:Runners id '{id}' is duplicated.");
            if (!entry.Enabled)
                continue;
            if (entry.StandingAgentId != Guid.Empty && !pins.Add(entry.StandingAgentId))
                failures.Add("PhoneHomeRunner:Runners standing agent ids must be unique.");
            if (entry.StandingAgentId == Guid.Empty && !entry.AllowDelegatedTasks)
                failures.Add($"PhoneHomeRunner:Runners:{id}:StandingAgentId must be set unless AllowDelegatedTasks is true.");
            if (string.IsNullOrWhiteSpace(entry.HostWorkspaceRoot))
                failures.Add($"PhoneHomeRunner:Runners:{id}:HostWorkspaceRoot must be set.");
            if (string.IsNullOrWhiteSpace(entry.RunnerWorkspace) || !entry.RunnerWorkspace.StartsWith('/'))
                failures.Add($"PhoneHomeRunner:Runners:{id}:RunnerWorkspace must be a POSIX absolute path.");
            if (string.IsNullOrWhiteSpace(entry.RunnerRepository) || !entry.RunnerRepository.StartsWith('/'))
                failures.Add($"PhoneHomeRunner:Runners:{id}:RunnerRepository must be a POSIX absolute path.");
            if (entry.MaxCapacity <= 0)
                failures.Add($"PhoneHomeRunner:Runners:{id}:MaxCapacity must be positive.");
            if (entry.RawExeAllowList.Any(exe => string.IsNullOrWhiteSpace(exe) || !exe.StartsWith('/')))
                failures.Add($"PhoneHomeRunner:Runners:{id}:RawExeAllowList entries must be POSIX absolute paths.");
            if (string.IsNullOrWhiteSpace(entry.ChildGrokHome) || !entry.ChildGrokHome.StartsWith('/'))
                failures.Add($"PhoneHomeRunner:Runners:{id}:ChildGrokHome must be a POSIX absolute path.");
            if (string.IsNullOrWhiteSpace(entry.ChildClaudeHome) || !entry.ChildClaudeHome.StartsWith('/'))
                failures.Add($"PhoneHomeRunner:Runners:{id}:ChildClaudeHome must be a POSIX absolute path.");
            if (string.IsNullOrWhiteSpace(entry.ChildCodexHome) || !entry.ChildCodexHome.StartsWith('/')
                || entry.ChildCodexHome.Split('/').Contains("..")
                || entry.ChildCodexHome.TrimEnd('/') is "/tmp"
                || entry.ChildCodexHome.StartsWith("/tmp/", StringComparison.Ordinal))
                failures.Add($"PhoneHomeRunner:Runners:{id}:ChildCodexHome must be a persistent POSIX path.");
            if (!Uri.TryCreate(entry.CallbackOrigin, UriKind.Absolute, out var origin)
                || (origin.Scheme != Uri.UriSchemeHttp && origin.Scheme != Uri.UriSchemeHttps))
                failures.Add($"PhoneHomeRunner:Runners:{id}:CallbackOrigin must be an absolute http(s) URI.");
            if (string.IsNullOrWhiteSpace(entry.SharedSecret))
                failures.Add($"PhoneHomeRunner:Runners:{id}:SharedSecret must be set.");
        }
    }
}
