namespace Antiphon.Server.Domain.Enums;

/// <summary>
/// Which lane hosts an agent's interactive child process (CARD-0160 / herdr S2).
///
/// <para><see cref="PtyHost"/> is 0 deliberately, and it is the migration default: every agent
/// that existed before this column keeps the only lane that existed yesterday. Herdr is a thing an
/// operator opts INTO — a visible, natively attachable pane in the operator's herdr instance —
/// never something a migration does to a working agent.</para>
///
/// <para>Hard constraints: spiked for <c>ClaudeCode</c>, <c>Grok</c>, and <c>Codex</c>
/// (CARD-0187). <c>OpenCode</c> / <c>Raw</c> are refused at create/PATCH, channel-bind, and
/// launch-time. Never silently remapped to pty-host. CARD-0186 lifted the AlwaysOn and
/// channel-bound refusals.</para>
/// </summary>
public enum SessionBackend
{
    /// <summary>Detached pty-host process — the existing default lane. Survives runner restarts.</summary>
    PtyHost = 0,

    /// <summary>
    /// Pane in the operator's herdr instance — visible and natively attachable, but it does not
    /// survive a herdr restart (Antiphon's own --resume path owns repopulation).
    /// </summary>
    Herdr = 1,
}
