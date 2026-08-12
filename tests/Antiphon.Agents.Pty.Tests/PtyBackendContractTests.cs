using System.Diagnostics;
using System.Runtime.InteropServices;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-0037 step 1: the shipped modern pseudoconsole, and the flag in front of it.
///
/// <para>Four things have to hold, and each of them fails silently otherwise — a session on the
/// wrong backend looks exactly like a session on the right one until a body over ~1 KB gets clipped:
/// the flag DEFAULTS OFF; the binary we ship is the one we say we ship; asking for it actually loads
/// it (the child's console host is OUR OpenConsole.exe, not <c>conhost.exe</c>); and going through
/// the real production write path with it on delivers the bracketed-paste markers that the inbox
/// conhost eats.</para>
///
/// <para>No Claude, no model turns — the peer is the Node probe, the same one CARD-0027/0030 used.
/// The paired negative (production default strips the markers) lives in
/// <see cref="PtyBracketedPasteContractTests"/> and must stay green alongside these.</para>
/// </summary>
[NotInParallel("Headed")]
[Category("Pty")]
public class PtyBackendContractTests
{
    private static void SkipIfNotWindows()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new SkipTestException("ConPTY only on Windows");
    }

    private static string RequireShippedDll()
    {
        SkipIfNotWindows();
        if (!ConPtyRedistributable.TryLocate(out var dll, out var why))
            throw new SkipTestException("no shipped conpty.dll: " + why);
        return dll!;
    }

    /// <summary>
    /// The default. An unset flag — and anything unrecognised in it — is the inbox conhost, i.e.
    /// precisely the behaviour that shipped before this card, ceilings and all. Raising
    /// <c>BriefInlineMaxBytes</c> is gated on measurements taken with the flag ON; if the default
    /// ever drifts to modern, those ceilings would go stale in the other direction on any machine
    /// that lacks the redistributable.
    /// </summary>
    [Test]
    [Arguments("")]
    [Arguments("inbox")]
    [Arguments("0")]
    [Arguments("off")]
    [Arguments("something-nobody-defined")]
    public async Task The_flag_defaults_off(string requested)
    {
        var decision = PtyBackendPolicy.Resolve(requested);

        decision.Backend.ShouldBe(PtyBackend.InboxConhost);
        decision.ConPtyDllPath.ShouldBeNull();
        decision.FellBack.ShouldBeFalse("this is the default, not a failure to honour a request");
        decision.Reason.ShouldNotBeNullOrWhiteSpace();
        await Task.CompletedTask;
    }

    /// <summary>
    /// The point of shipping a KNOWN binary is lost if nothing checks which binary shipped. Both
    /// files are pinned by SHA-256 against the Microsoft-signed NuGet package recorded in
    /// <see cref="ConPtyRedistributable"/>; a package bump that changes them must be a deliberate
    /// re-measurement of the delivery envelope, not a silent build-time swap.
    /// </summary>
    [Test]
    public async Task The_shipped_binaries_are_the_ones_with_recorded_provenance()
    {
        var dll = RequireShippedDll();

        var (ok, detail) = ConPtyRedistributable.VerifyShippedHashes(dll);

        ok.ShouldBeTrue(
            $"the staged pseudoconsole is not {ConPtyRedistributable.PackageId} "
            + $"{ConPtyRedistributable.PackageVersion}. Got {detail}. If the package was bumped on "
            + "purpose, re-run PtyPasteMarkerExperiments.Real_claude_delivery_envelope and update "
            + "both the hashes and the recorded numbers — the ceilings are coupled to this binary.");
        Path.GetDirectoryName(dll)!.ShouldEndWith(ConPtyRedistributable.RelativeDirectory);
        await Task.CompletedTask;
    }

    /// <summary>
    /// A conpty.dll that cannot find its OpenConsole.exe falls back to the inbox conhost <b>without
    /// telling anyone</b> — the pty still works, it just quietly strips markers again. So the
    /// locator treats a lone DLL as a MISS, and a modern request over it falls back explicitly, with
    /// the ceilings still in force. This is the missing-redistributable case the card asks for.
    /// </summary>
    [Test]
    public async Task A_modern_request_falls_back_to_the_inbox_conhost_when_the_pair_is_incomplete()
    {
        SkipIfNotWindows();
        var dir = Path.Combine(Path.GetTempPath(), "antiphon-conpty-probe", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var previous = Environment.GetEnvironmentVariable(ConPtyRedistributable.DirectoryEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(ConPtyRedistributable.DirectoryEnvVar, dir);

            // Nothing there at all: no DLL to load.
            var missing = PtyBackendPolicy.Resolve("modern");
            missing.Backend.ShouldBe(PtyBackend.InboxConhost);
            missing.FellBack.ShouldBeTrue();

            // A DLL with no console host beside it — the dangerous case, because it LOADS.
            File.WriteAllText(Path.Combine(dir, ConPtyRedistributable.DllName), "not really a dll");
            var halfStaged = PtyBackendPolicy.Resolve("modern");
            halfStaged.Backend.ShouldBe(PtyBackend.InboxConhost);
            halfStaged.FellBack.ShouldBeTrue();
            halfStaged.Reason.ShouldContain(ConPtyRedistributable.ConsoleHostName);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ConPtyRedistributable.DirectoryEnvVar, previous);
            try { Directory.Delete(dir, recursive: true); } catch { /* temp */ }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Asking for the modern backend must actually get it. The proof is the console host process:
    /// under the inbox path the pty is served by <c>%SystemRoot%\System32\conhost.exe</c>, under
    /// ours by the <c>OpenConsole.exe</c> we staged. Asserted on the process tree rather than on
    /// behaviour, because a fallback is behaviourally invisible right up until a body is clipped.
    /// </summary>
    [Test]
    public async Task Asking_for_the_modern_backend_runs_the_child_under_our_own_OpenConsole()
    {
        var dll = RequireShippedDll();
        var expected = Path.Combine(Path.GetDirectoryName(dll)!, ConPtyRedistributable.ConsoleHostName);

        await using var runner = new PtyAgentRunner("modern");
        await runner.StartAsync("cmd.exe", ["/k", "echo BACKEND-READY"], cwd: AppContext.BaseDirectory);
        (await runner.WaitForOutputAsync(s => s.Contains("BACKEND-READY"), TimeSpan.FromSeconds(30)))
            .ShouldBeTrue("the child must actually come up: " + runner.SnapshotText());

        runner.Backend!.Backend.ShouldBe(PtyBackend.ModernConPty);
        runner.Backend.ConPtyDllPath.ShouldBe(dll);

        var hosts = ConsoleHostsOf(Environment.ProcessId);
        hosts.ShouldContain(
            h => string.Equals(h, expected, StringComparison.OrdinalIgnoreCase),
            $"the pseudoconsole must be served by {expected}; saw [{string.Join("; ", hosts)}]");
    }

    /// <summary>
    /// The whole point, end to end through the SHIPPED path: <see cref="PtyAgentRunner"/>, the
    /// production encoding, one write — and the child receives <c>ESC[200~</c> … <c>ESC[201~</c>
    /// intact. On the default backend the identical call arrives with the markers eaten
    /// (<see cref="PtyBracketedPasteContractTests.The_production_pty_delivers_no_bracketed_paste_markers"/>),
    /// which is the difference between the TUI's paste path and its typing path, and therefore
    /// between a 43 KB body landing whole and landing clipped at 1 KB.
    /// </summary>
    [Test]
    public async Task The_production_write_path_delivers_the_markers_on_the_modern_backend()
    {
        RequireShippedDll();
        if (!NodeStdinProbe.NodeAvailable)
            throw new SkipTestException("no JS runtime (node.exe) on PATH");
        if (!File.Exists(NodeStdinProbe.ProbePath))
            throw new SkipTestException($"probe not staged at {NodeStdinProbe.ProbePath}");

        await using var probe = await NodeStdinProbe.StartAsync(chunkLog: false, decset2004: true, backend: "modern");
        probe.Runner.Backend!.Backend.ShouldBe(PtyBackend.ModernConPty);

        var result = await probe.DeliverAsync(NodeStdinProbe.MarkedBodyOfBytes(300));

        result.MissingCount.ShouldBe(0, "the body itself must arrive whole: " + result);
        result.HasPasteStart.ShouldBeTrue(
            "the shipped conpty.dll must forward ESC[200~ through the production runner. Head was: "
            + result.HeadHex);
        result.HasPasteEnd.ShouldBeTrue("and ESC[201~ must close it");
        result.HeadHex.ShouldStartWith("1b5b3230307e", customMessage: "delivery must START with ESC[200~");
        // Not ShouldEndWith: DeliverAsync sends the production trailing CR and then the probe's own
        // report sentinel, both of which land after the closing marker.
        result.TailHex.ShouldContain("1b5b3230317e", customMessage:
            "ESC[201~ must be in the delivered tail, before the trailing CR");
    }

    /// <summary>Console-host executables currently running as children of this process.</summary>
    private static List<string> ConsoleHostsOf(int parentPid)
    {
        var found = new List<string>();
        var psi = new ProcessStartInfo(
            "powershell",
            "-NoProfile -Command \"Get-CimInstance Win32_Process | Where-Object { $_.ParentProcessId -eq "
            + parentPid + " } | ForEach-Object { $_.ExecutablePath }\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(20_000);
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.EndsWith("OpenConsole.exe", StringComparison.OrdinalIgnoreCase)
                || trimmed.EndsWith("conhost.exe", StringComparison.OrdinalIgnoreCase))
            {
                found.Add(trimmed);
            }
        }

        return found;
    }
}
