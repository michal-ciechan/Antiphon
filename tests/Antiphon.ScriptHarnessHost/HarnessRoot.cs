using System.Diagnostics;
using System.Globalization;

// The C# root of the C806 script harness fixtures (ScriptHarnessProcessTests, ScriptHarnessWindowsOwnershipTests,
// ScriptHarnessLinuxOwnershipTests): ScriptHarnessProcess runs a .dll script as `dotnet <dll> -Case <case> -ResultsDirectory
// <dir> [-HelperPath <dll>] [-Payload <text>] [-ObservationDirectory <dir> -Nonce <nonce>]`. It replaced
// tests/Antiphon.Tests/Scripts/Fixtures/script-harness-process.ps1 (unit 35 of docs/superpowers/plans/
// 2026-11-22-remaining-ps1-to-csharp.md) case for case: it publishes its identity (root), the tree cases start the
// fixture-child/fixture-grandchild pair holding the named streams, and the output cases print the C487 inventory lines.
// On Windows the external observer's nonce gates every file it writes. A failure prints one stderr line and exits 1, as the
// script's throw did. ASCII only.
internal static class HarnessRoot
{
    private static readonly string[] TreeCases = ["LiveRoot", "ExitedStdout", "ExitedStderr", "Cancellation", "Silent", "Race", "PassHold"];

    internal static bool Matches(string[] args) => args.Length >= 2 && args[0] == "-Case";

    internal static async Task<int> RunAsync(string[] args)
    {
        try
        {
            return await RunCaseAsync(Named(args));
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    // PowerShell's named parameters: -Name value pairs, names ignoring case; a name given without a value is empty.
    private static Dictionary<string, string> Named(string[] args)
    {
        var named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith('-')) throw new ArgumentException("Unexpected fixture argument: " + args[i]);
            named[args[i][1..]] = i + 1 < args.Length ? args[++i] : "";
        }
        return named;
    }

    private static bool Is(string value, params string[] names) => names.Contains(value, StringComparer.OrdinalIgnoreCase);

    private static async Task<int> RunCaseAsync(Dictionary<string, string> named)
    {
        string Arg(string name) => named.TryGetValue(name, out var value) ? value : "";
        var caseName = Arg("Case");
        var results = Arg("ResultsDirectory");
        var helper = Arg("HelperPath");
        var nonce = Arg("Nonce");
        Directory.CreateDirectory(results);
        using var self = Process.GetCurrentProcess();
        var nativeStart = OperatingSystem.IsLinux() ? ReadStart(Environment.ProcessId) : 0;
        var observer = results;
        var identityPrefix = "";
        if (OperatingSystem.IsWindows())
        {
            var observation = Arg("ObservationDirectory");
            if (observation.Length == 0 || nonce.Length == 0) throw new InvalidOperationException("Windows fixture requires an external observer and nonce.");
            observer = observation;
            if (File.ReadAllText(Path.Combine(observer, "nonce")) != nonce) throw new InvalidOperationException("Observer nonce mismatch.");
            identityPrefix = nonce + " ";
            Publish(observer, "first-instruction", nonce);
        }
        Publish(observer, "root", identityPrefix + Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + " "
            + self.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture) + " " + nativeStart.ToString(CultureInfo.InvariantCulture));

        if (OperatingSystem.IsWindows() && Is(caseName, "HandleProbe"))
        {
            // This process is the root, so the probe sees the root's own inherited handle table.
            WindowsScriptHandleProbe.WriteReceipt(observer, nonce);
            await Task.Delay(TimeSpan.FromSeconds(20));
            return 0;
        }

        if (Is(caseName, TreeCases))
        {
            var held = Is(caseName, "ExitedStdout", "PassHold") ? "stdout" : Is(caseName, "ExitedStderr") ? "stderr" : Is(caseName, "Silent") ? "none" : "both";
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false };
            foreach (var argument in new[] { helper.Length > 0 ? helper : typeof(HarnessRoot).Assembly.Location, "fixture-child", observer, held })
                start.ArgumentList.Add(argument);
            using (var child = Process.Start(start))
                if (child is null) throw new InvalidOperationException("Fixture child did not start.");
            var limit = Stopwatch.StartNew();
            while (!(File.Exists(Path.Combine(observer, "child")) && File.Exists(Path.Combine(observer, "grandchild"))))
            {
                if (limit.Elapsed.TotalSeconds > 4) throw new TimeoutException("Fixture child and grandchild readiness timed out.");
                await Task.Delay(20);
            }
            Publish(observer, "ready", OperatingSystem.IsWindows() ? nonce + " " + caseName : caseName);
            if (OperatingSystem.IsWindows())
            {
                while (!File.Exists(Path.Combine(observer, "observed")))
                {
                    if (limit.Elapsed.TotalSeconds > 4) throw new TimeoutException("Retained-handle observation barrier timed out.");
                    await Task.Delay(20);
                }
                if (File.ReadAllText(Path.Combine(observer, "observed")) != nonce) throw new InvalidOperationException("Release nonce mismatch.");
            }
            if (Is(caseName, "PassHold"))
            {
                // PASS is flushed, then the root exits while the child still owns stdout.
                Console.Out.WriteLine("PASS C806 C806 fixture passed");
                Console.Out.Flush();
                return 0;
            }
            if (Is(caseName, "LiveRoot", "Cancellation")) await Task.Delay(TimeSpan.FromSeconds(20));
            if (Is(caseName, "Race"))
            {
                while (!File.Exists(Path.Combine(observer, "release")))
                {
                    if (limit.Elapsed.TotalSeconds > 20) throw new TimeoutException("Race release timed out.");
                    await Task.Delay(20);
                }
            }
            if (!Is(caseName, "Silent")) return 0;
        }

        if (Is(caseName, "Nonzero"))
        {
            Console.Out.WriteLine("C806 stdout marker");
            Console.Error.WriteLine("C806 stderr marker");
            return 37;
        }
        if (Is(caseName, "HighVolume"))
        {
            Console.Out.Write(new string('A', 1048576));
            Console.Error.Write(new string('B', 1048576));
            Console.Out.WriteLine("C806 stdout end");
            Console.Error.WriteLine("C806 stderr end");
        }
        if (Is(caseName, "BadInventory"))
        {
            Console.Out.WriteLine("C487 HARNESS EXIT CODE: 0");
            Console.Out.WriteLine("C487: 0 passed, 0 failed, 0 rows");
            return 0;
        }
        if (Is(caseName, "Stdin")) Console.Out.WriteLine("STDIN:" + Console.In.ReadLine());
        var payload = Arg("Payload");
        if (Is(caseName, "Passing") && payload.Length > 0) File.WriteAllText(Path.Combine(results, "payload"), payload);
        Console.Out.WriteLine("PASS C806 C806 fixture passed");
        Console.Out.WriteLine("C487 HARNESS EXIT CODE: 0");
        Console.Out.WriteLine("C487: 1 passed, 0 failed, 1 rows");
        return 0;
    }

    // Written under a temporary name and moved, so an observer never reads a partial file.
    private static void Publish(string directory, string name, string text)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllText(path + ".tmp", text);
        File.Move(path + ".tmp", path);
    }

    private static long ReadStart(int pid)
    {
        var stat = File.ReadAllText($"/proc/{pid}/stat");
        var end = stat.LastIndexOf(") ", StringComparison.Ordinal);
        var fields = stat[(end + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return long.Parse(fields[19], CultureInfo.InvariantCulture);
    }
}
