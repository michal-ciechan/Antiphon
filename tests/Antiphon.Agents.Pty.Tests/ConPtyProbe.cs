namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// Runs <c>probes/stdin-probe.js</c> under a <see cref="ConPtyHost"/> — i.e. under a pseudoconsole
/// whose backend, window and pipe sizing are ours to choose — and returns what the child received.
///
/// <para>The report is read from the probe's own <c>PROBE_OUT</c> file rather than the pty's output:
/// the pty carries a RENDERED SCREEN, line-wrapped and interleaved with cursor sequences, and a
/// summary scraped off it is only as trustworthy as the de-wrapping.</para>
/// </summary>
internal static class ConPtyProbe
{
    public static async Task<ProbeResult> RunAsync(
        string? conptyDll,
        string payload,
        bool decset2004 = true,
        short cols = 120,
        short rows = 30,
        int inputPipeBytes = 0,
        int quietMs = 1200,
        int chunkBytes = 0,
        int gapMs = 0)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "TestOutput", "card-0030", "probe-runs");
        Directory.CreateDirectory(dir);
        var outFile = Path.Combine(dir, $"probe-{Guid.NewGuid():N}.txt");
        var env = new Dictionary<string, string>
        {
            ["PROBE_RAW"] = "1",
            ["PROBE_CHUNKLOG"] = "1",
            ["PROBE_OUT"] = outFile,
            ["PROBE_DECSET_2004"] = decset2004 ? "1" : "0",
            ["PROBE_QUIET_MS"] = quietMs.ToString(),
        };

        await using var host = ConPtyHost.Start(
            NodeStdinProbe.NodeExe, [NodeStdinProbe.ProbePath], AppContext.BaseDirectory, env,
            cols, rows, conptyDll, inputPipeBytes);

        if (!await WaitForFileAsync(outFile, "PROBE-READY", TimeSpan.FromSeconds(30)))
            throw new InvalidOperationException(
                FailureDetails(host, outFile, "probe never announced readiness"));

        if (chunkBytes <= 0)
        {
            host.Write(payload);
        }
        else
        {
            for (var i = 0; i < payload.Length; i += chunkBytes)
            {
                host.Write(payload.Substring(i, Math.Min(chunkBytes, payload.Length - i)));
                if (gapMs > 0) await Task.Delay(gapMs);
            }
        }

        if (!await WaitForFileAsync(outFile, "PROBE-SUMMARY ", TimeSpan.FromSeconds(30)))
            throw new InvalidOperationException(FailureDetails(host, outFile, "no PROBE-SUMMARY"));

        return NodeStdinProbe.ParseSummaryFile(outFile);
    }

    private static async Task<bool> WaitForFileAsync(string path, string needle, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (File.Exists(path) && File.ReadAllText(path).Contains(needle, StringComparison.Ordinal))
                    return true;
            }
            catch (IOException) { /* the probe is appending */ }
            await Task.Delay(50);
        }
        return false;
    }

    private static string FailureDetails(ConPtyHost host, string outFile, string problem) =>
        $"{problem} under {host.Backend}; pid={host.Pid}; exit={host.ExitCode?.ToString() ?? "running"}; "
        + $"da1Queries={host.Da1QueriesSeen}; da1Answered={host.Da1AnsweredAt?.ToString("O") ?? "never"}; "
        + $"host={ConPtyHost.Diagnostics}; probe file:\n{TailFile(outFile)}\npty said:\n{Tail(host.OutputText)}";

    private static string TailFile(string path)
    {
        try { return File.Exists(path) ? Tail(File.ReadAllText(path)) : "<not created>"; }
        catch (IOException) { return "<locked while reading diagnostics>"; }
    }

    private static string Tail(string text, int maxChars = 4_000) =>
        text.Length <= maxChars ? text : text[^maxChars..];
}
