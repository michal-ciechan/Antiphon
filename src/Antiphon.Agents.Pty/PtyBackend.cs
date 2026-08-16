namespace Antiphon.Agents.Pty;

/// <summary>Which pseudoconsole implementation serves a pty.</summary>
public enum PtyBackend
{
    /// <summary>
    /// <c>kernel32!CreatePseudoConsole</c> → <c>%SystemRoot%\System32\conhost.exe</c>, via Porta.Pty.
    /// The historical path. Strips bracketed-paste markers, so every delivery arrives as typing and
    /// the byte ceilings (<c>BriefInlineMaxBytes</c>, spill-and-pointer) are load-bearing.
    /// </summary>
    InboxConhost,

    /// <summary>
    /// The shipped redistributable <c>conpty.dll</c> → its sibling <c>OpenConsole.exe</c>
    /// (<see cref="ConPtyRedistributable"/>). Delivers the markers, so the TUI takes its paste path.
    /// </summary>
    ModernConPty,
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
    /// <summary>True when the modern backend was asked for and could not be given.</summary>
    public bool FellBack =>
        Backend == PtyBackend.InboxConhost
        && !PtyBackendPolicy.IsInboxRequest(Requested);

    public override string ToString() =>
        $"{Backend} (requested '{Requested}'): {Reason}";
}

/// <summary>
/// The flag. <b>Defaults OFF</b> — an unset or unrecognised value is the inbox conhost, i.e. exactly
/// the behaviour that shipped before CARD-0037, with the existing ceilings still in force.
///
/// <para><b>Scope: one switch, process-wide, inherited.</b> The session runner, the detached
/// pty-hosts it spawns and the server-side adapters all move together, because they all deliver
/// bodies that were sized against ONE set of ceilings — a per-session backend would make
/// <c>BriefInlineMaxBytes</c> correct for some sessions and a data-loss bug for others. Propagation
/// is free: <c>PtyHostLauncher</c> starts hosts with <c>UseShellExecute=false</c> and no environment
/// override, so the host inherits the runner's environment block, and the runner exports this
/// variable from <c>SessionRunner:PtyBackend</c> at startup when one is configured.</para>
///
/// <para><b>Tests move independently</b>, by construction: they need to pin BOTH backends at once
/// (the inbox one strips the markers, the modern one delivers them — that pair IS the contract), so
/// <see cref="PtyAgentRunner"/> takes a per-instance override rather than reading only the
/// environment.</para>
/// </summary>
public static class PtyBackendPolicy
{
    /// <summary>Accepted values: <c>inbox</c> (default), <c>modern</c>. Also 0/off/false and 1/on/true.</summary>
    public const string EnvVar = "ANTIPHON_PTY_BACKEND";

    /// <summary>The configuration key the session runner reads and exports into <see cref="EnvVar"/>.</summary>
    public const string ConfigKey = "SessionRunner:PtyBackend";

    public static bool IsInboxRequest(string requested) => Parse(requested) == PtyBackend.InboxConhost;

    /// <summary>
    /// Resolves the backend for one spawn. <paramref name="requested"/> overrides the environment;
    /// null means "read <see cref="EnvVar"/>".
    /// </summary>
    public static PtyBackendDecision Resolve(string? requested = null)
    {
        var raw = requested
                  ?? Environment.GetEnvironmentVariable(EnvVar)
                  ?? string.Empty;

        if (Parse(raw) == PtyBackend.InboxConhost)
            return new PtyBackendDecision(
                PtyBackend.InboxConhost, null, raw,
                raw.Length == 0
                    ? $"{EnvVar} unset — default"
                    : $"{EnvVar}='{raw}'");

        if (ConPtyRedistributable.TryLocate(out var dll, out var why))
            return new PtyBackendDecision(PtyBackend.ModernConPty, dll, raw, why);

        // The card's explicit requirement: a machine without the redistributable keeps working, on
        // the old binary, with the old ceilings. Which is also why the ceilings cannot simply be
        // deleted once the modern path ships.
        return new PtyBackendDecision(
            PtyBackend.InboxConhost, null, raw,
            $"modern backend requested but unavailable ({why}) — falling back to the inbox conhost; "
            + "the paste ceilings still apply");
    }

    private static PtyBackend Parse(string raw) => raw.Trim().ToLowerInvariant() switch
    {
        "modern" or "conpty" or "1" or "on" or "true" or "yes" => PtyBackend.ModernConPty,
        _ => PtyBackend.InboxConhost,
    };
}
