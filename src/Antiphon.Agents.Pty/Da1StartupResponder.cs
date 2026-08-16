using System.Text;

namespace Antiphon.Agents.Pty;

/// <summary>
/// Answers the ONE device-attributes query the modern console host asks at startup (CARD-0048).
///
/// <para><b>Why this exists.</b> <c>OpenConsole.exe</c> — the console host behind the shipped
/// <c>conpty.dll</c> — writes <c>ESC[c</c> (DA1, primary device attributes) into the pty the moment
/// it comes up, and <b>holds the console client</b> until either a DA1 response arrives on the input
/// pipe or ~3.0 s elapses. Nothing in our stack answered, so every child on the modern backend was
/// frozen for ~3.0 s before executing its first instruction; the inbox conhost never asks and never
/// waits. Proven, with controls, in
/// <c>docs/investigations/2026-08-16-modern-conpty-da1-stall-CARD-0048.md</c>: the child unblocks
/// 16 ms after the reply whenever it is sent, an arbitrary byte or a CPR reply does NOT unblock it,
/// and no <c>CreatePseudoConsole</c> flag removes the handshake.</para>
///
/// <para><b>Why <c>ESC[?1;0c</c> and not something richer</b> (spec §1,
/// <c>docs/superpowers/specs/2026-08-16-card-0048-da1-answer.md</c>). It is the only string measured
/// to unblock the client (43 ms vs 3061 ms). It is also <i>accurate</i>: a DA1 response describes the
/// hosting terminal, and our hosting terminal is <see cref="PtyAgentRunner"/>'s scraper
/// (<see cref="TerminalScreen"/> + string detectors) — genuinely a VT101 with no options: no sixel,
/// no soft fonts, no rectangular editing, and it ignores the <c>ESC[?9001h</c> win32-input-mode
/// request. The risk is asymmetric — claiming too much invites OpenConsole to emit sequences
/// <see cref="TerminalScreen"/> cannot parse, silently degrading every snapshot-based detector.
/// <b>Never claim sixel (<c>4</c>).</b> If the marker-passthrough gate ever fails on this string, the
/// documented escalation is to claim what a real Windows Terminal claims — captured empirically or
/// read out of the <c>microsoft/terminal</c> source at
/// <see cref="ConPtyRedistributable.PackageVersion"/> — never guessed.</para>
///
/// <para><b>Why only the FIRST query.</b> The startup query is guaranteed to be the first
/// <c>ESC[c</c> on the pipe <i>because of the defect itself</i> — the child is frozen until it is
/// answered, so nothing else can have written output yet. That one was measured to be consumed by
/// the pty's input state machine and to never reach the child, so it provably cannot change what a
/// TUI negotiates. A <i>later</i> <c>ESC[c</c> could in principle be a child's own query forwarded
/// by OpenConsole, and answering that one WOULD route our reply to the child. So later queries are
/// counted in <see cref="QueriesSeen"/> and never answered; if a counter ever shows more than one,
/// that datum reopens the scope decision with evidence.</para>
///
/// <para>Pure and I/O-free on purpose: it scans bytes and calls back. <see cref="Scan"/> is driven
/// from the read path of <c>ModernConPtyConnection</c>, one call per read chunk, and carries its
/// state ACROSS chunk boundaries — the init burst arrived as a single read in every measurement, but
/// a split query must not defeat the fix.</para>
/// </summary>
internal sealed class Da1StartupResponder
{
    /// <summary>DA1 response "VT101 with no options" — see the type doc for why this exact string.</summary>
    public const string Response = "\u001b[?1;0c";

    /// <summary><see cref="Response"/> as the bytes to write. ASCII, so one byte per char.</summary>
    public static readonly byte[] ResponseBytes = Encoding.ASCII.GetBytes(Response);

    private const byte Esc = 0x1b;

    /// <summary>Longest parameter run we will remember; anything longer cannot be a DA1 query.</summary>
    private const int MaxParams = 32;

    private readonly Action _reply;
    private readonly byte[] _parameters = new byte[MaxParams];

    private State _state = State.Ground;
    private int _parameterLength;
    private bool _parametersUsable = true;
    private bool _fired;

    private int _queriesSeen;
    private long _answeredAtTicks;

    /// <param name="reply">
    /// Sends the DA1 response. Invoked at most once, from whichever thread is reading the pty, and
    /// it owns its own error handling — an exception escaping here would kill the read loop.
    /// </param>
    public Da1StartupResponder(Action reply) => _reply = reply;

    /// <summary>
    /// Every complete DA1 query seen on this pty, answered or not. Expected to be exactly 1; see the
    /// type doc for what a larger number would mean.
    /// </summary>
    public int QueriesSeen => Volatile.Read(ref _queriesSeen);

    /// <summary>When the single reply was sent, or null if no DA1 query has arrived.</summary>
    public DateTimeOffset? AnsweredAt
    {
        get
        {
            var ticks = Volatile.Read(ref _answeredAtTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    /// <summary>
    /// Feeds one chunk of pty output through the state machine. Read-only: the caller's bytes are
    /// untouched and every one of them still reaches the snapshot, the screen and the audit.
    /// </summary>
    public void Scan(ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
        {
            switch (_state)
            {
                case State.Ground:
                    if (b == Esc) _state = State.Escape;
                    break;

                case State.Escape:
                    if (b == (byte)'[')
                    {
                        _state = State.Csi;
                        _parameterLength = 0;
                        _parametersUsable = true;
                    }
                    else if (b != Esc)
                    {
                        _state = State.Ground;
                    }

                    break;

                case State.Csi:
                    // Parameter bytes 0x30-0x3F: digits, ';', ':' and the private markers <=>? .
                    if (b >= 0x30 && b <= 0x3f)
                    {
                        if (_parameterLength < MaxParams) _parameters[_parameterLength++] = b;
                        else _parametersUsable = false;
                    }
                    // Intermediate bytes 0x20-0x2F: DA1 has none, so this is a different sequence.
                    else if (b >= 0x20 && b <= 0x2f)
                    {
                        _parametersUsable = false;
                    }
                    // Final byte 0x40-0x7E ends the sequence, whatever it turned out to be.
                    else if (b >= 0x40 && b <= 0x7e)
                    {
                        if (b == (byte)'c' && _parametersUsable && IsDa1Query()) OnQuery();
                        _state = State.Ground;
                    }
                    else if (b == Esc)
                    {
                        // Abandoned mid-sequence; the new ESC starts the next one.
                        _state = State.Escape;
                    }
                    else
                    {
                        // A C0 control or an 8-bit byte inside a CSI. Rather than model the full
                        // "execute and continue" rule, abandon: the cost is a missed query we have
                        // never observed, the alternative is a false fire, and a false fire writes
                        // bytes into a live agent's stdin.
                        _state = State.Ground;
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// DA1 is <c>CSI c</c> or <c>CSI 0 c</c> — nothing else. This is what keeps the responder off
    /// <c>ESC[?1;0c</c> (a DA1 <i>response</i>) and <c>ESC[&gt;c</c> (DA2, secondary attributes),
    /// both of which also end in 'c'.
    /// </summary>
    private bool IsDa1Query() =>
        _parameterLength == 0 || (_parameterLength == 1 && _parameters[0] == (byte)'0');

    private void OnQuery()
    {
        Interlocked.Increment(ref _queriesSeen);
        if (_fired) return;

        _fired = true;
        Volatile.Write(ref _answeredAtTicks, DateTimeOffset.UtcNow.UtcTicks);
        _reply();
    }

    private enum State
    {
        Ground,
        Escape,
        Csi,
    }
}
