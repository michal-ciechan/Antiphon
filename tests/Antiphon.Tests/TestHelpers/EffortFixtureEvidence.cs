using System.Text;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

internal static class EffortFixtureEvidence
{
    internal static async Task<string> CaptureAsync(string requested, Guid session, string token, TimeSpan elapsed,
        Func<string> raw, Func<string> rendered, string trace, string logger)
    {
        static string Observe(Func<string> read)
        {
            try { return read(); } catch (Exception ex) { return "unreadable: " + ex; }
        }
        string capturedTrace;
        try { capturedTrace = await EffortFixtureCompletion.ReadTraceAsync(trace); }
        catch (Exception ex) { capturedTrace = "missing/unreadable trace: " + ex; }
        return $"requested={requested}\nsession={session}\ntoken={token}\nelapsed={elapsed}\n" +
            $"raw:\n{Observe(raw)}\nrendered:\n{Observe(rendered)}\ntrace:\n{capturedTrace}\nlogger:\n{logger}\n";
    }

    internal static async Task<string> PublishAsync(string snapshot, Exception? primary)
    {
        var args = Environment.GetCommandLineArgs();
        var at = Array.IndexOf(args, "--results-directory");
        var directory = at >= 0 && at + 1 < args.Length ? Path.GetFullPath(args[at + 1]) : FallbackDirectory();
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "c1516-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(path, snapshot + $"\nprimary:\n{primary}\ncleanup: pending\n", Encoding.UTF8);
        return path;
    }

    internal static async Task CompleteAsync(string path, Exception? primary, Exception? cleanup)
    {
        if (primary is null && cleanup is null) { File.Delete(path); return; }
        await File.AppendAllTextAsync(path, "\ncleanup: " + (cleanup is null ? "complete" : cleanup.ToString()));
        TestContext.Current?.Output.AttachArtifact(path, Path.GetFileName(path), "effort fixture before terminal teardown and scratch removal");
    }

    private static string FallbackDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Antiphon.sln"))) current = current.Parent;
        return Path.Combine(current?.FullName ?? throw new DirectoryNotFoundException("Antiphon.sln not found"), ".antiphon", "test-evidence");
    }
}
