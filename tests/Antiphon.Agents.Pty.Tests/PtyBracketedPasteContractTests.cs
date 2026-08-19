using System.Runtime.InteropServices;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-0030's CI-runnable half: the platform fact the whole diagnosis rests on, pinned against a
/// real JS peer on the real ConPTY path, with no Claude and no model turns.
///
/// <para><b>The fact.</b> A pseudoconsole is served by a conhost, and which conhost decides whether
/// <c>ESC[200~</c>/<c>ESC[201~</c> reach the child at all. The inbox
/// <c>%SystemRoot%\System32\conhost.exe</c> (10.0.19041.1 on this machine) <b>strips them</b>; the
/// redistributable ConPTY that ships with VS Code, the JetBrains IDEs and Windows Terminal
/// (conpty.dll + OpenConsole.exe) <b>delivers them</b>. Same write, same body, same peer.</para>
///
/// <para><b>Why it matters.</b> CARD-0027 concluded "bracketed paste is not the discriminator"
/// because a wrapped body and an unwrapped one lost identically — but both arms ran on the inbox
/// conhost, where the wrapped arm was silently unwrapped before the TUI saw it. It compared
/// unwrapped against unwrapped. With the markers actually delivered, real Claude takes 86 KB in one
/// write without losing a byte (<see cref="PtyPasteMarkerExperiments"/>, headed).</para>
///
/// <para><b>If the first test here goes RED</b>, the inbox conhost has learned to forward the
/// markers — which means the shipped spill-and-pointer ceilings are now costing us nothing but are
/// also no longer necessary, and the inline limits should be re-measured rather than left at 900
/// bytes. That is a good failure; read it, do not delete it.</para>
///
/// See <c>docs/investigations/2026-08-12-bracketed-paste-conhost-CARD-0030.md</c>.
/// </summary>
[NotInParallel("Headed")]
[Category("Pty")]
[ParallelLimiter<ProcessSpawnLimit>]
public class PtyBracketedPasteContractTests
{
    private static void SkipIfUnavailable()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new SkipTestException("ConPTY only on Windows");
        if (!NodeStdinProbe.NodeAvailable)
            throw new SkipTestException("no JS runtime (node.exe) on PATH");
        if (!File.Exists(NodeStdinProbe.ProbePath))
            throw new SkipTestException($"probe not staged at {NodeStdinProbe.ProbePath}");
    }

    /// <summary>
    /// The inbox path (Porta.Pty → kernel32 <c>CreatePseudoConsole</c> → <c>%SystemRoot%\System32\
    /// conhost.exe</c>): what we write as a paste reaches the child as plain typing. Asserted on the
    /// child's own first bytes, because a line count cannot tell a delivered marker from a stripped
    /// one.
    ///
    /// <para>Was <c>The_production_pty_delivers_no_bracketed_paste_markers</c>, and the rename is the
    /// point (CARD-0045): since CARD-0037 step 3 "production" on this deployment is the <em>modern</em>
    /// backend, which delivers the markers — so a test named for production, inheriting
    /// <c>ANTIPHON_PTY_BACKEND</c>, asserted the opposite of the thing it was measuring the moment
    /// anyone launched the suite from an Antiphon-spawned shell. It pins the fallback, and now says
    /// so and declares it.</para>
    /// </summary>
    [Test]
    public async Task The_inbox_conhost_delivers_no_bracketed_paste_markers()
    {
        SkipIfUnavailable();
        await using var probe = await NodeStdinProbe.StartAsync(
            chunkLog: false, decset2004: true, backend: "inbox");
        probe.Runner.Backend!.Backend.ShouldBe(
            PtyBackend.InboxConhost,
            "this test pins the marker-stripping fallback and must declare it, not inherit it");

        var result = await probe.DeliverAsync(NodeStdinProbe.MarkedBodyOfBytes(300));

        result.MissingCount.ShouldBe(0, "the body itself must still arrive whole");
        result.HasPasteStart.ShouldBeFalse(
            "the inbox conhost strips ESC[200~ from written input. If this is now FALSE-failing, the "
            + "platform has changed: re-measure the inline ceilings, they were sized for a TUI that "
            + "never sees a paste. Body head was: " + result.HeadHex);
        result.HasPasteEnd.ShouldBeFalse("ESC[201~ is stripped by the same parser");
        result.HeadHex.ShouldNotStartWith("1b5b3230307e", customMessage:
            "1b5b3230307e is ESC[200~ — it must not be at the head of what the child received");
    }

    /// <summary>
    /// The same write through a modern pseudoconsole: the markers arrive, unchanged, wrapping the
    /// body. This is the whole difference between our delivery and a terminal's paste, and it is
    /// one binary away — not a property of ConPTY, Windows, or the body.
    /// </summary>
    [Test]
    public async Task A_modern_conpty_delivers_the_markers_unchanged()
    {
        SkipIfUnavailable();
        var redist = ConPtyHost.FindRedistConPty();
        if (redist is null)
            throw new SkipTestException(
                "no redistributable conpty.dll on this machine (VS Code / JetBrains IDEs ship one "
                + "next to OpenConsole.exe)");

        var result = await ConPtyProbe.RunAsync(
            redist, PtyInputEncoding.EncodeBody(NodeStdinProbe.MarkedBodyOfBytes(300)));

        result.MissingCount.ShouldBe(0, "the body must arrive whole here too");
        result.HasPasteStart.ShouldBeTrue(
            "a modern OpenConsole forwards ESC[200~ to the client. Head was: " + result.HeadHex);
        result.HasPasteEnd.ShouldBeTrue("and ESC[201~ closes it");
        result.HeadHex.ShouldStartWith("1b5b3230307e", customMessage: "the delivery must START with ESC[200~");
        result.TailHex.ShouldEndWith("1b5b3230317e", customMessage: "and END with ESC[201~");
    }
}
