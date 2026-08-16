using System.Text;
using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-0048 slice 1: the DA1 state machine, on its own, with no pty in sight.
///
/// <para>Two properties carry the whole fix and both are cheap to get wrong. <b>It must fire</b> —
/// including when <c>ESC[c</c> straddles a read-chunk boundary, which is the one case the
/// investigation could not provoke and therefore could not measure. <b>It must not fire on anything
/// else</b> — the reply is written into a live agent's stdin, so a false positive on
/// <c>ESC[?1004h</c> or <c>ESC[201~</c> would inject <c>ESC[?1;0c</c> into whatever the TUI is doing
/// at the time. Both directions are asserted here, deterministically, byte by byte.</para>
/// </summary>
[Category("Pty")]
public class Da1StartupResponderTests
{
    private const string Esc = "\u001b";

    /// <summary>Feeds <paramref name="text"/> as one chunk and returns how many replies were sent.</summary>
    private static (int Replies, Da1StartupResponder Responder) Feed(params string[] chunks)
    {
        var replies = 0;
        var responder = new Da1StartupResponder(() => replies++);
        foreach (var chunk in chunks)
            responder.Scan(Encoding.ASCII.GetBytes(chunk));
        return (replies, responder);
    }

    [Test]
    public void A_query_in_one_chunk_fires_exactly_once()
    {
        var (replies, responder) = Feed(Esc + "[1t" + Esc + "[c" + Esc + "[?1004h");

        replies.ShouldBe(1);
        responder.QueriesSeen.ShouldBe(1);
        responder.AnsweredAt.ShouldNotBeNull();
    }

    /// <summary>
    /// The zero-parameter form. <c>CSI 0 c</c> and <c>CSI c</c> are the same request; a host that
    /// sends the explicit 0 must not be left waiting out the full 3 s.
    /// </summary>
    [Test]
    public void The_explicit_zero_parameter_form_fires_too()
    {
        var (replies, responder) = Feed(Esc + "[0c");

        replies.ShouldBe(1);
        responder.QueriesSeen.ShouldBe(1);
    }

    /// <summary>
    /// The real init burst arrived as ONE read every time it was measured — which is exactly why a
    /// split has to be tested rather than assumed. Every interior boundary of both spellings, with
    /// output either side so the machine has to survive noise as well as the cut.
    /// </summary>
    [Test]
    [Arguments(Esc + "[c")]
    [Arguments(Esc + "[0c")]
    public void A_query_split_at_every_byte_boundary_still_fires_once(string query)
    {
        for (var cut = 1; cut < query.Length; cut++)
        {
            var (replies, responder) = Feed(
                "noisy-" + query[..cut],
                query[cut..] + Esc + "[?9001h trailing output");

            replies.ShouldBe(1, $"split after {cut} byte(s) of {Describe(query)} must still answer");
            responder.QueriesSeen.ShouldBe(1);
        }
    }

    /// <summary>One byte per <see cref="Da1StartupResponder.Scan"/> call — the worst possible split.</summary>
    [Test]
    public void A_query_delivered_one_byte_at_a_time_still_fires_once()
    {
        var stream = "junk " + Esc + "[c" + " more junk";
        var (replies, responder) = Feed(stream.Select(c => c.ToString()).ToArray());

        replies.ShouldBe(1);
        responder.QueriesSeen.ShouldBe(1);
    }

    /// <summary>
    /// The scope decision (spec §1): a second <c>ESC[c</c> could be a CHILD's query forwarded by
    /// OpenConsole, and our answer to that one would reach the child and change what the TUI
    /// negotiates. So it is counted as evidence and left unanswered.
    /// </summary>
    [Test]
    public void A_second_query_is_counted_but_never_answered()
    {
        var (replies, responder) = Feed(Esc + "[c", "output", Esc + "[0c", Esc + "[c");

        replies.ShouldBe(1, "exactly one reply per session, ever");
        responder.QueriesSeen.ShouldBe(3, "but every query is surfaced, so the field can disprove §1");
    }

    /// <summary>
    /// The false-positive suite. Everything here appears in a real init burst or a real TUI stream,
    /// and every one of them would put <c>ESC[?1;0c</c> into the child's stdin if the machine were
    /// matching loosely (say, on a trailing 'c', or on <c>[c</c> without the ESC).
    /// </summary>
    [Test]
    [Arguments(Esc + "[?1004h", "DECSET 1004 - focus reporting, in the measured init burst")]
    [Arguments(Esc + "[?9001h", "DECSET 9001 - win32-input-mode, in the measured init burst")]
    [Arguments(Esc + "[201~", "bracketed-paste END, the sequence this whole backend exists to deliver")]
    [Arguments(Esc + "[200~", "bracketed-paste START")]
    [Arguments(Esc + "[1t", "window manipulation, the FIRST thing OpenConsole emits")]
    [Arguments("[c", "no ESC - the accidental control in the investigation, which did NOT unblock")]
    [Arguments("plain text with a c in it", "ordinary child output")]
    [Arguments(Esc + "[?1;0c", "our own RESPONSE - a private-parameter DA1 reply, not a query")]
    [Arguments(Esc + "[>c", "DA2, secondary device attributes - a different request")]
    [Arguments(Esc + "[=c", "DA3, tertiary device attributes")]
    [Arguments(Esc + "[0;1c", "a two-parameter 'c' final - not DA1")]
    [Arguments(Esc + "[ c", "an intermediate byte before the final - not DA1")]
    [Arguments(Esc + "]0;title" + Esc + "\\", "an OSC title, which is not a CSI at all")]
    public void A_lookalike_never_fires(string sequence, string why)
    {
        var (replies, responder) = Feed("before " + sequence + " after");

        replies.ShouldBe(0, why);
        responder.QueriesSeen.ShouldBe(0, why);
        responder.AnsweredAt.ShouldBeNull();
    }

    /// <summary>A lookalike must not poison the machine for the real query that follows it.</summary>
    [Test]
    public void A_real_query_after_a_lookalike_still_fires()
    {
        var (replies, responder) = Feed(Esc + "[?1004h" + Esc + "[>c" + Esc, "[c");

        replies.ShouldBe(1);
        responder.QueriesSeen.ShouldBe(1);
    }

    /// <summary>
    /// The measured startup burst, verbatim from the investigation trace, split the way ConPTY
    /// actually hands bytes over (several reads, no respect for sequence boundaries).
    /// </summary>
    [Test]
    public void The_measured_init_burst_fires_once()
    {
        var (replies, responder) = Feed(
            Esc + "[1",
            "t",
            Esc + "[",
            "c" + Esc + "[?1004h" + Esc + "[?9001h");

        replies.ShouldBe(1);
        responder.QueriesSeen.ShouldBe(1);
    }

    /// <summary>Nothing at all is the common case for the inbox conhost, which never asks.</summary>
    [Test]
    public void No_query_means_no_reply_and_no_timestamp()
    {
        var (replies, responder) = Feed("", "just output", Esc + "[2J" + Esc + "[H");

        replies.ShouldBe(0);
        responder.QueriesSeen.ShouldBe(0);
        responder.AnsweredAt.ShouldBeNull();
    }

    private static string Describe(string sequence) => sequence.Replace(Esc, "ESC");
}
