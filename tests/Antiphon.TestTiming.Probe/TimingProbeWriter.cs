using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Antiphon.Tests.TestHelpers;

internal static class TimingProbeWriter
{
    [ModuleInitializer]
    internal static void Run()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("C1514_WRITER")))
            WriteShard();
        else if (Environment.GetEnvironmentVariable("C1514_CHILD") == "1")
            LaunchOrdinaryChild();
    }

    private static void WriteShard()
    {
        var pidText = Environment.GetEnvironmentVariable("C1514_WRITER_PID");
        var nonce = Environment.GetEnvironmentVariable("C1514_WRITER_NONCE");
        if (!int.TryParse(pidText, out var pid) || string.IsNullOrWhiteSpace(nonce))
            Environment.Exit(2);
        using var session = TestTimingSession.TryOpen(new TestTimingConfig
        {
            Root = Environment.GetEnvironmentVariable(TestTiming.RootVariable),
            RunId = Environment.GetEnvironmentVariable(TestTiming.RunIdVariable),
            GroupId = Environment.GetEnvironmentVariable(TestTiming.GroupIdVariable),
            Attempt = Environment.GetEnvironmentVariable(TestTiming.AttemptVariable),
            Suite = Environment.GetEnvironmentVariable(TestTiming.SuiteVariable),
            Pid = pid,
            Nonce = nonce,
        });
        var enabled = session.Enabled;
        if (enabled)
        {
            var start = session.NowTicks();
            session.BeginCase("writer-case", "TimingProbe", "Writer", [], start);
            session.EndCase("writer-case", "Passed", session.NowTicks());
        }

        session.Dispose();
        Environment.Exit(enabled ? 0 : 3);
    }

    private static void LaunchOrdinaryChild()
    {
        var root = Environment.GetEnvironmentVariable(TestTiming.RootVariable) ?? "";
        var selfPath = Path.Combine(root, "self-env.txt");
        var childPath = Path.Combine(root, "child-env.txt");
        Directory.CreateDirectory(root);
        File.WriteAllText(selfPath, FormatPhaseEnvironment());
        var psi = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            psi.Environment[(string)entry.Key] = (string?)entry.Value ?? "";
        TestTiming.RemovePhaseVariables(psi);
        psi.Environment["C1514_CHILD_OUT"] = childPath;
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add("$names = @('ANTIPHON_TEST_PHASE_ROOT','ANTIPHON_TEST_PHASE_RUN_ID','ANTIPHON_TEST_PHASE_GROUP_ID','ANTIPHON_TEST_PHASE_ATTEMPT','ANTIPHON_TEST_PHASE_SUITE'); $lines = foreach ($n in $names) { $value = [Environment]::GetEnvironmentVariable($n); if ($null -ne $value) { $n + '=' + $value } }; [System.IO.File]::WriteAllText($env:C1514_CHILD_OUT, ($lines -join [char]10))");
        using var process = Process.Start(psi);
        process?.WaitForExit();
        Environment.Exit(process is { ExitCode: 0 } && File.Exists(childPath) ? 0 : 4);
    }

    private static string FormatPhaseEnvironment()
    {
        var lines = new List<string>();
        foreach (var name in TestTiming.PhaseVariableNames)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (value is not null)
                lines.Add(name + "=" + value);
        }

        return string.Join('\n', lines);
    }
}
