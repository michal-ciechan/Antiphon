namespace Antiphon.Agents.Pty;

/// <summary>Which pseudoconsole implementation serves a pty.</summary>
public enum PtyBackend
{
    /// <summary>
    /// <c>kernel32!CreatePseudoConsole</c> → <c>%SystemRoot%\System32\conhost.exe</c>, via Porta.Pty.
    /// The historical path. Strips bracketed-paste markers, so every delivery arrives as typing and
    /// the byte ceilings (<c>BriefInlineMaxBytes</c>, spill-and-pointer) are load-bearing.
    /// </summary>
    InboxConhost = 0,

    /// <summary>
    /// The shipped redistributable <c>conpty.dll</c> → its sibling <c>OpenConsole.exe</c>
    /// (<see cref="ConPtyRedistributable"/>). Delivers the markers, so the TUI takes its paste path.
    /// </summary>
    ModernConPty = 1,

    /// <summary>Porta's Unix PTY transport; no Windows console host is involved.</summary>
    UnixPty = 2,
}

/// <summary>What was asked for, what was resolved, and why — kept so the log and the tests can both
/// see whether a "modern" request actually got the modern binary or quietly fell back.</summary>
/// <param name="Backend">The backend that will be (or was) used.</param>
/// <param name="ConPtyDllPath">The conpty.dll to load, or null for the inbox path.</param>
/// <param name="Requested">The raw flag value that was resolved.</param>
/// <param name="Reason">Human-readable explanation, always populated.</param>
public sealed record PtyBackendDecision(
    PtyBackend Backend,
    string? ConPtyDllPath,
    string Requested,
    string Reason)
{
    public bool Deprecated => Backend == PtyBackend.InboxConhost;

    /// <summary>Unknown Windows selectors remain accepted during release A, with a warning.</summary>
    public bool UnrecognisedRequest => Backend != PtyBackend.UnixPty
        && !string.IsNullOrWhiteSpace(Requested) && !PtyBackendPolicy.IsKnownRequest(Requested);

    public bool RequiresWarning => Deprecated || UnrecognisedRequest;

    /// <summary>True when the modern backend was asked for and could not be given.</summary>
    public bool FellBack =>
        Backend == PtyBackend.InboxConhost
        && !PtyBackendPolicy.IsInboxRequest(Requested);

    public override string ToString() =>
        $"{Backend} (requested '{Requested}'): {Reason}";
}

/// <summary>
/// CARD-1022 release A: Windows defaults to the shipped modern host. Explicit legacy selectors
/// and missing-pair fallback remain deprecated transitions until releases B/C. Unix uses Porta
/// without probing Windows files. Explicit instance requests, including empty, override ambient input.
/// </summary>
public static class PtyBackendPolicy
{
    /// <summary>Windows defaults to modern; inbox/0/off/false/no are deprecated selectors.</summary>
    public const string EnvVar = "ANTIPHON_PTY_BACKEND";

    /// <summary>The configuration key the daemon composes with its environment once at startup.</summary>
    public const string ConfigKey = "SessionRunner:PtyBackend";

    public static bool IsInboxRequest(string requested) => requested.Trim().ToLowerInvariant()
        is "inbox" or "0" or "off" or "false" or "no";

    internal static bool IsKnownRequest(string requested) => IsInboxRequest(requested)
        || requested.Trim().ToLowerInvariant() is "modern" or "conpty" or "1" or "on" or "true" or "yes";

    /// <summary>
    /// Resolves the backend for one spawn. <paramref name="requested"/> overrides the environment;
    /// null means "read <see cref="EnvVar"/>".
    /// </summary>
    public static PtyBackendDecision Resolve(string? requested = null) =>
        Resolve(requested, Environment.GetEnvironmentVariable(EnvVar), OperatingSystem.IsWindows(),
            () =>
            {
                ConPtyRedistributable.TryLocate(out var dll, out var reason);
                return (dll, reason);
            });

    internal static PtyBackendDecision Resolve(string? requested, string? environment, bool isWindows,
        Func<(string? DllPath, string Reason)> locate)
    {
        var raw = requested
                  ?? environment
                  ?? string.Empty;

        if (!isWindows)
            return new PtyBackendDecision(PtyBackend.UnixPty, null, raw,
                "Unix PTY via Porta; Windows backend selectors do not apply");

        if (IsInboxRequest(raw))
            return new PtyBackendDecision(
                PtyBackend.InboxConhost, null, raw,
                $"{EnvVar}='{raw}' selects the deprecated inbox conhost; migrate to modern");

        var selection = string.IsNullOrWhiteSpace(raw) ? "default modern; "
            : !IsKnownRequest(raw) ? $"unrecognised {EnvVar}='{raw}'; choosing modern; " : "";
        var (dll, why) = locate();
        if (dll is not null)
            return new PtyBackendDecision(PtyBackend.ModernConPty, dll, raw, selection + why);

        // The card's explicit requirement: a machine without the redistributable keeps working, on
        // the old binary, with the old ceilings. Which is also why the ceilings cannot simply be
        // deleted once the modern path ships.
        return new PtyBackendDecision(
            PtyBackend.InboxConhost, null, raw,
            selection + $"modern backend requested but unavailable ({why}) — falling back to the deprecated inbox conhost; "
            + "the paste ceilings still apply");
    }
}
