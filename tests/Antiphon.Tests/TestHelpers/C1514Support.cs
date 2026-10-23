using System.Diagnostics;
using System.Text.Json;
using Shouldly;

namespace Antiphon.Tests.TestHelpers;

internal static class C1514Support
{
    public const long Tick = 10_000;

    public static long Ms(int milliseconds) => milliseconds * Tick;

    public static string FreshRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "c1514-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static TestTimingSession Open(string root, int pid = 4242, string nonce = "nonce1")
    {
        var session = TestTimingSession.TryOpen(new TestTimingConfig
        {
            Root = root,
            RunId = "run-a",
            GroupId = "group-a",
            Attempt = "1",
            Suite = "suite-a",
            Pid = pid,
            Nonce = nonce,
            AssemblyId = "asm1",
            ManualClock = true,
        });
        session.Enabled.ShouldBeTrue(session.FailureCode ?? "recorder disabled");
        return session;
    }

    public static JsonElement Complete(string shard, string testId)
    {
        foreach (var line in ReadLiveLines(shard))
        {
            if (line.Length == 0)
                continue;
            using var doc = JsonDocument.Parse(line);
            if (!doc.RootElement.TryGetProperty("recordKind", out var kind) || kind.GetString() != "complete")
                continue;
            if (doc.RootElement.TryGetProperty("testId", out var id) && id.GetString() == testId)
                return doc.RootElement.Clone();
        }

        throw new InvalidOperationException("missing complete timing rows for " + testId);
    }

    // Windows sharing is reciprocal: a live reader must share the existing
    // writer's write access, even though the writer already shares read access.
    public static string ReadLiveText(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static IEnumerable<string> ReadLiveLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
            yield return line;
    }

    public static long Envelope(JsonElement complete)
    {
        var exclusive = complete.GetProperty("exclusive");
        long sum = complete.GetProperty("overlapMs").GetInt64() + complete.GetProperty("remainderMs").GetInt64();
        foreach (var name in new[] { "db", "host", "process", "fs", "network", "fixture" })
            sum += exclusive.GetProperty(name).GetInt64();
        return sum;
    }

    public static string RepoRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "scripts", "lib", "test-phase-evidence.ps1")))
                    return dir.FullName;
                dir = dir.Parent;
            }
        }

        throw new InvalidOperationException("scripts/lib/test-phase-evidence.ps1 not found");
    }

    public static string StagedProbe() =>
        Path.Combine(AppContext.BaseDirectory, "timing-probe", "Antiphon.TestTiming.Probe.dll");

    public static async Task<string> LaunchProbe(string root, bool enabled, CancellationToken cancellationToken)
    {
        var dll = StagedProbe();
        File.Exists(dll).ShouldBeTrue("timing probe must be staged by the parent build");
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = root };
        start.ArgumentList.Add(dll);
        start.ArgumentList.Add("--treenode-filter");
        start.ArgumentList.Add("/*/*/TimingProbeTests/*");
        start.ArgumentList.Add("--report-trx");
        start.ArgumentList.Add("--report-trx-filename");
        start.ArgumentList.Add("run.trx");
        start.ArgumentList.Add("--results-directory");
        start.ArgumentList.Add(root);
        start.Environment["TUNIT_MAX_PARALLEL_TESTS"] = "1";
        foreach (var name in TestTiming.PhaseVariableNames)
            start.Environment.Remove(name);
        if (enabled)
        {
            var timing = Path.Combine(root, "timing");
            Directory.CreateDirectory(timing);
            start.Environment[TestTiming.RootVariable] = timing;
            start.Environment[TestTiming.RunIdVariable] = "run-a";
            start.Environment[TestTiming.GroupIdVariable] = "group-a";
            start.Environment[TestTiming.AttemptVariable] = "1";
            start.Environment[TestTiming.SuiteVariable] = "suite-a";
        }

        var ran = await RunProcess(start, cancellationToken);
        File.Exists(Path.Combine(root, "run.trx")).ShouldBeTrue("staged child executes: " + ran.Text);
        return ran.Text;
    }

    public static List<string> NativeIds(string? text)
    {
        var ids = new List<string>();
        if (string.IsNullOrEmpty(text))
            return ids;
        const string marker = "C1514_NATIVE ";
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r');
            if (!line.StartsWith(marker, StringComparison.Ordinal))
                continue;
            var rest = line[marker.Length..];
            var space = rest.IndexOf(' ');
            if (space < 1)
                continue;
            var id = rest[(space + 1)..].Trim();
            if (id.Length > 0 && !ids.Contains(id, StringComparer.Ordinal))
                ids.Add(id);
        }

        return ids;
    }

    public static async Task<(int ExitCode, string Text)> RunProcess(
        ProcessStartInfo start, CancellationToken cancellationToken)
    {
        start.UseShellExecute = false;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        using var process = Process.Start(start)!;
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return (process.ExitCode, await stdout + "\n" + await stderr);
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            }
        }
    }
}
