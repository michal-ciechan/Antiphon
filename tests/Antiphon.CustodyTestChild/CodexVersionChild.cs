using System.Diagnostics;
using System.Text.Json;

// Unit 40 of the 2026-11-22 plan: the owned fake `codex --version` child of CodexCliVersionTestFixture
// (SessionRunner.Tests) and RunnerCodexCliEvidenceTests' ProbeIo (Antiphon.Tests). It replaced
// tests/Antiphon.SessionRunner.Tests/Fixtures/CodexVersionChild.ps1, which ran under pwsh -File. Both suites stage this
// program as custody-child and start it as: dotnet <custody-child>/Antiphon.CustodyTestChild.dll codex-version <mode>
// <receipt-root>, with the environment the probe gave its child. The output is the script's: Write is the literal text,
// WriteLine ends with Environment.NewLine (as [Console]::Out.WriteLine did), and the exit codes are the script's (a
// throw was exit 1).
internal static class CodexVersionChild
{
    internal const string Command = "codex-version";
    private const string Banner = "codex-cli 0.160.0";
    private const string Notice = @"npm notice synthetic update; C:\Users\C1031\private; /home/C1031/private; C1031-token-canary";

    private static readonly HashSet<string> Modes = new(StringComparer.Ordinal)
    {
        "success", "nonzero", "stderr", "stdout-flood", "stderr-flood", "stdin", "timeout", "tree", "parent-exits", "leaf",
        "diagnostic", "notice", "notice-held", "stderr-4096", "stderr-4097", "stdout-notices", "outside-cap", "fragment",
        "lf-cap", "lf-cap-overflow", "crlf-cap", "crlf-split", "eof-cap", "eof-stderr-overflow", "both-floods",
        "nonzero-notice", "nonzero-stdout", "nonzero-stderr", "nonzero-both", "stderr-banner", "malformed-stdout", "empty",
        // The fixture's literal older records (it ran them as pwsh -Command): no receipt root is read or written.
        "version-old", "version-floor",
    };

    internal static int Run(string[] args)
    {
        if (args.Length != 3 || args[0] != Command || !Modes.Contains(args[1]))
        {
            Console.Error.WriteLine("usage: codex-version <mode> <receipt-root>");
            return 2;
        }
        try { return Execute(args[1], args[2]); }
        catch (InvalidOperationException failure)
        {
            Console.Error.WriteLine(failure.Message);
            return 1;
        }
    }

    private static int Execute(string mode, string receiptRoot)
    {
        if (mode is "version-old" or "version-floor")
        {
            Console.Out.WriteLine(mode == "version-old" ? "codex-cli 0.156.1" : "codex-cli 0.159.1");
            return 0;
        }
        if (!Directory.Exists(receiptRoot)) throw new InvalidOperationException("missing owned receipt root");
        WriteReceipt(receiptRoot, mode);
        if (mode == "notice-held")
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!File.Exists(Path.Combine(receiptRoot, "release")))
            {
                if (DateTime.UtcNow >= deadline) throw new InvalidOperationException("owned notice was not released");
                Thread.Sleep(10);
            }
        }
        if (mode is "leaf" or "timeout")
        {
            Thread.Sleep(20000);
            return 0;
        }
        if (mode is "tree" or "parent-exits") return Tree(mode, receiptRoot);
        if (mode == "stdout-flood") { Console.Out.Write(new string('A', 4097)); return 0; }
        if (mode == "stderr-flood") { Console.Error.Write(new string('E', 4097)); return 0; }
        if (mode == "stdin")
        {
            if (Console.In.ReadToEnd().Length != 0) throw new InvalidOperationException("probe stdin carried data");
            File.WriteAllText(Path.Combine(receiptRoot, "stdin-eof"), "eof");
        }
        switch (mode)
        {
            case "stderr-4096": Console.Out.WriteLine(Banner); Console.Error.Write(new string('E', 4096)); return 0;
            case "stderr-4097": Console.Out.WriteLine(Banner); Console.Error.Write(new string('E', 4097)); return 0;
            case "stdout-notices": Console.Out.Write(Banner + "\n" + new string('N', 4097)); Console.Error.Write("notice"); return 0;
            case "outside-cap": Console.Out.Write(new string('N', 4096) + "\n" + Banner + "\n"); return 0;
            case "fragment": Console.Out.Write(new string('N', 4096 - Banner.Length - 1) + "\n" + Banner + "-beta\n"); return 0;
            case "lf-cap": Console.Out.Write(new string('N', 4096 - Banner.Length - 2) + "\n" + Banner + "\n"); return 0;
            case "lf-cap-overflow": Console.Out.Write(new string('N', 4096 - Banner.Length - 2) + "\n" + Banner + "\nN"); return 0;
            case "crlf-cap": Console.Out.Write(new string('N', 4096 - Banner.Length - 3) + "\n" + Banner + "\r\n"); return 0;
            case "crlf-split": Console.Out.Write(new string('N', 4096 - Banner.Length - 2) + "\n" + Banner + "\r\n"); return 0;
            case "eof-cap": Console.Out.Write(new string('N', 4096 - Banner.Length - 1) + "\n" + Banner); return 0;
            case "eof-stderr-overflow": Console.Out.Write(Banner); Console.Error.Write(new string('E', 4097)); return 0;
            case "both-floods": Console.Out.Write(Banner + "\n" + new string('N', 1048576)); Console.Error.Write(new string('E', 1048576)); return 0;
            case "nonzero-notice": Console.Out.WriteLine(Banner); Console.Error.Write("notice"); return 1;
            case "nonzero-stdout": Console.Out.Write(Banner + "\n" + new string('N', 4097)); Console.Error.Write("notice"); return 1;
            case "nonzero-stderr": Console.Out.WriteLine(Banner); Console.Error.Write(new string('E', 4097)); return 1;
            case "nonzero-both": Console.Out.Write(Banner + "\n" + new string('N', 4097)); Console.Error.Write(new string('E', 4097)); return 1;
            case "stderr-banner": Console.Error.WriteLine(Banner); return 0;
            case "malformed-stdout": Console.Out.WriteLine("codex-cli banana"); Console.Error.WriteLine(Banner); return 0;
            case "empty": return 0;
        }
        Console.Out.WriteLine(Banner);
        if (mode == "stderr") Console.Error.Write("E");
        if (mode == "diagnostic") Console.Error.Write("C959-diagnostic-sentinel");
        if (mode is "notice" or "notice-held") Console.Error.Write(Notice);
        return mode == "nonzero" ? 1 : 0;
    }

    // <receipt-root>/<mode>.json: {pid, kernelStartTicks (field 22 of /proc/<pid>/stat on Linux, else null), startedUtc
    // (round-trip UTC), mode}. The fixture binds a pid to it by start time (Windows) or kernel start ticks (Linux). It is
    // published by rename, so a reader that sees the name sees the whole record.
    private static void WriteReceipt(string receiptRoot, string mode)
    {
        string? kernelStartTicks = null;
        if (OperatingSystem.IsLinux())
        {
            var stat = File.ReadAllText($"/proc/{Environment.ProcessId}/stat");
            kernelStartTicks = stat[(stat.LastIndexOf(')') + 2)..].Split(' ')[19];
        }
        using var self = Process.GetCurrentProcess();
        var receipt = JsonSerializer.Serialize(new
        {
            pid = Environment.ProcessId,
            kernelStartTicks,
            startedUtc = self.StartTime.ToUniversalTime().ToString("O"),
            mode,
        });
        var path = Path.Combine(receiptRoot, mode + ".json");
        File.WriteAllText(path + ".tmp", receipt);
        File.Move(path + ".tmp", path);
    }

    // tree and parent-exits start a leaf (this program on the same host, mode leaf) that inherits both standard writers,
    // wait at most 5 s for its receipt, write <receipt-root>/ready and the banner. tree then waits for the leaf (and kills
    // its tree if it is still running on the way out); parent-exits exits at once and leaves the leaf holding the pipes.
    private static int Tree(string mode, string receiptRoot)
    {
        var host = Environment.ProcessPath ?? throw new InvalidOperationException("no host executable");
        var self = typeof(CodexVersionChild).Assembly.Location;
        var start = new ProcessStartInfo(host) { UseShellExecute = false };
        // Under dotnet the program is its argument; this program's own apphost needs none.
        if (!Path.GetFileNameWithoutExtension(host).Equals(Path.GetFileNameWithoutExtension(self), StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(self);
        foreach (var item in new[] { Command, "leaf", receiptRoot }) start.ArgumentList.Add(item);
        var leaf = Process.Start(start) ?? throw new InvalidOperationException("leaf did not start");
        try
        {
            var readyPath = Path.Combine(receiptRoot, "leaf.json");
            var readyLimit = DateTime.UtcNow.AddSeconds(5);
            while (!File.Exists(readyPath) && DateTime.UtcNow < readyLimit) Thread.Sleep(10);
            if (!File.Exists(readyPath)) throw new InvalidOperationException("leaf did not become ready");
            File.WriteAllText(Path.Combine(receiptRoot, "ready"), "ready");
            Console.Out.WriteLine(Banner);
            if (mode != "parent-exits") leaf.WaitForExit();
        }
        finally
        {
            if (mode != "parent-exits")
            {
                if (!leaf.HasExited) leaf.Kill(true);
                leaf.WaitForExit();
            }
            leaf.Dispose();
        }
        return 0;
    }
}
