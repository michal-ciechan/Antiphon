using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Antiphon.Agents.Pty;
using Porta.Pty;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-0048: the modern pseudoconsole's startup DA1 handshake, and the fix for it.
///
/// <para><b>The defect.</b> <c>OpenConsole.exe</c> writes <c>ESC[c</c> at startup and holds the
/// console CLIENT until a DA1 response arrives or ~3.0 s expires — so on the modern backend every
/// child did nothing at all for three seconds, with no input involved. That is a fixed tax on every
/// session launch, and it made everything that infers ready/done/idle from a quiet period shorter
/// than 3 s read the stall as quiet (investigation section 8: 8 tests plus CARD-0049's).
/// <see cref="ModernConPtyConnection"/> now answers it once per session.</para>
///
/// <para><b>Why these tests run in the normal suite.</b> CARD-0045's <see cref="PtyBackendEnvGuard"/>
/// clears the inherited environment variable, so the default backend here is inbox and an env-driven
/// modern sweep no longer exists — but the guard never touched the per-instance override. Every test
/// in this class names <c>"modern"</c> at the construction site, which outranks the environment, and
/// skips when the redistributable is genuinely absent. On any machine that builds the repo it is
/// staged into the test output, so in practice these RUN.</para>
/// </summary>
[NotInParallel("Headed")]
[Category("Pty")]
public class ModernPtyDa1Tests
{
    private static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    /// <summary>
    /// The healthy first-output latency is ~43 ms and the stall floor is ~3.0 s, so the bound sits
    /// in a wide gap in both directions: a load-induced red here is a visible false alarm (rerun in
    /// isolation), never a silent pass. It is also the empirical enforcement the spec puts in place
    /// of a settings validator — if a future ConPTY package bump introduces a new handshake, this
    /// goes red before any readiness quiet window silently becomes a coin flip.
    /// </summary>
    private static readonly TimeSpan MaxFirstOutputLatency = TimeSpan.FromSeconds(2.5);

    private static string RequireShippedDll()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new SkipTestException("ConPTY only on Windows");
        if (!ConPtyRedistributable.TryLocate(out var dll, out var why))
            throw new SkipTestException("no shipped conpty.dll: " + why);
        return dll!;
    }

    /// <summary>
    /// The fix's direct pin: a child on the modern backend produces its first output promptly.
    /// Without the responder this is ~3.05 s, measured nine times with a spread of 26 ms.
    /// </summary>
    [Test]
    public async Task Modern_child_first_output_arrives_without_the_da1_stall()
    {
        RequireShippedDll();

        await using var runner = new PtyAgentRunner("modern");
        var elapsed = Stopwatch.StartNew();
        await runner.StartAsync(Cmd, ["/d", "/c", "echo DA1-FIX-MARKER"], cwd: AppContext.BaseDirectory);

        var seen = await runner.WaitForOutputAsync(
            s => s.Contains("DA1-FIX-MARKER"), TimeSpan.FromSeconds(15));
        elapsed.Stop();

        runner.Backend!.Backend.ShouldBe(
            PtyBackend.ModernConPty, "a silent fallback would make this test prove nothing");
        seen.ShouldBeTrue("the child must run at all: " + runner.SnapshotText());
        elapsed.Elapsed.ShouldBeLessThan(
            MaxFirstOutputLatency,
            $"first child output took {elapsed.ElapsedMilliseconds} ms. The DA1 stall floor is "
            + "~3.0 s: either the handshake is unanswered again, or the ConPTY package changed what "
            + "it asks for at startup. Backend was " + runner.Backend);
    }

    /// <summary>
    /// The safety half of the fix. A WELL-FORMED DA1 response is consumed by the pty's input state
    /// machine and never reaches the child, which is why answering it cannot change what a TUI
    /// negotiates; the investigation established that by contrast with a MALFORMED one (an
    /// ESC-less reply leaked, and cmd answered <c>'[?1' is not recognized</c>). Both of those
    /// signatures are asserted absent here, on a live interactive shell.
    /// </summary>
    [Test]
    public async Task The_da1_reply_is_consumed_and_does_not_leak_to_the_child()
    {
        RequireShippedDll();

        await using var runner = new PtyAgentRunner("modern");
        // Bare /d /q /k, exactly as the investigation ran it: an extra "prompt" argument would be
        // quoted by the production command-line builder and come back as its own
        // "is not recognized" - which is the string this test is looking for.
        await runner.StartAsync(Cmd, ["/d", "/q", "/k"], cwd: AppContext.BaseDirectory);
        (await runner.WaitForScreenAsync(s => s.Contains('>'), TimeSpan.FromSeconds(15)))
            .ShouldBeTrue("the interactive shell must reach its prompt: " + runner.SnapshotText());

        await runner.SendLineAsync("echo leak-check-ok");

        // Twice: once as the console's echo of what we typed, once as the command's own output.
        // One occurrence would mean the shell heard us but never ran it.
        (await runner.WaitForScreenAsync(s => Occurrences(s, "leak-check-ok") >= 2, TimeSpan.FromSeconds(15)))
            .ShouldBeTrue("the child must execute the typed command: " + runner.SnapshotScreen());

        var screen = runner.SnapshotScreen();
        screen.ShouldNotContain(
            "?1;0c", customMessage:
            "the DA1 response reached the CHILD instead of the pty's input state machine — it is "
            + "being typed into whatever the agent is doing. Screen: " + screen);
        screen.ShouldNotContain(
            "is not recognized", customMessage:
            "the measured leak signature: cmd tried to run our reply as a command. Screen: " + screen);
    }

    /// <summary>
    /// Exactly one query, exactly one answer. The count is also the field probe for the spec's open
    /// question: the first-query-only scope is safe either way, but a session that ever reports more
    /// than one DA1 is the evidence that reopens it — a later query could be a CHILD's, forwarded,
    /// and answering that one would route our reply to the child.
    ///
    /// <para>Driven against the connection directly rather than through
    /// <see cref="PtyAgentRunner"/>, because the runner does not (and should not) surface a
    /// backend-specific counter — and because pumping the reader here is what makes the scan happen,
    /// so the test cannot accidentally assert on a responder nobody fed.</para>
    /// </summary>
    [Test]
    public async Task The_connection_answers_exactly_one_da1()
    {
        var dll = RequireShippedDll();

        using var connection = ModernConPtyConnection.Spawn(dll, new PtyOptions
        {
            Name = "antiphon-pty",
            Cols = 120,
            Rows = 30,
            Cwd = AppContext.BaseDirectory,
            App = Cmd,
            CommandLine = ["/d", "/c", "echo DA1-COUNT-MARKER"],
            Environment = new Dictionary<string, string>(),
        });

        var output = new StringBuilder();
        using var pumpCts = new CancellationTokenSource();
        var pump = Task.Run(async () =>
        {
            var buffer = new byte[4096];
            try
            {
                int read;
                while ((read = await connection.ReaderStream.ReadAsync(buffer, pumpCts.Token)) > 0)
                    lock (output) output.Append(Encoding.UTF8.GetString(buffer, 0, read));
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        });

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            lock (output)
            {
                if (output.ToString().Contains("DA1-COUNT-MARKER")) break;
            }

            await Task.Delay(50);
        }

        string text;
        lock (output) text = output.ToString();
        text.ShouldContain("DA1-COUNT-MARKER", customMessage: "the child must have run");

        connection.Da1AnsweredAt.ShouldNotBeNull("OpenConsole asked, so we must have answered");
        connection.Da1QueriesSeen.ShouldBe(
            1,
            "one DA1 per session is the assumption the first-query-only scope rests on; more than "
            + "one is the datum that reopens it (spec section 1). Saw: " + text.Length + " bytes of output");

        // The reader is a synchronous FileStream, so the token cannot interrupt a blocking read —
        // the connection's Dispose closes the pipe handle and does. Don't wait on it forever.
        await pumpCts.CancelAsync();
        await Task.WhenAny(pump, Task.Delay(TimeSpan.FromSeconds(2)));
    }

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal);
             i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
