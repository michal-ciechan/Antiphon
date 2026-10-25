using System.Diagnostics;
using System.Text.Json;
using Antiphon.Checkpoints;
using Antiphon.Checkpoints.Coverage;
using Antiphon.HostCleanup.Checkpoints;

namespace Antiphon.Tests.Checkpoints;

// These fixtures passed on Linux while db2c2088 groups 62/63 failed on Windows.
// Capture before disposal, including on green bodies: the failure may be teardown itself.
// This is read-only evidence, never a second attempt at an assertion or cleanup.
internal static class CheckpointNativeDiagnostics
{
    internal static bool Enabled => OperatingSystem.IsWindows()
        || Environment.GetEnvironmentVariable("ANTIPHON_CHECKPOINT_NATIVE_DIAGNOSTICS") == "1";

    internal static void Capture(string fixture, IReadOnlyList<string> roots)
    {
        if (!Enabled || fixture is not (nameof(CheckpointLargeStampTests) or nameof(BaselineComparerTests)
            or nameof(CheckpointBuildBindingPathTests) or nameof(CheckpointTempUsageTests)
            or nameof(CheckpointAdmissionContractTests) or nameof(CheckpointAppTests)
            or nameof(CheckpointLargeStampScriptTests) or nameof(C1307CliCancellationTests)
            or nameof(RerunPolicyTests) or nameof(EvidenceFolderTests) or nameof(PlanCoverageBindingTests)
            or nameof(CheckpointRepeatBuildIsolationTests) or nameof(CheckpointToolSourceTests))) return;
        try
        {
            ThreadPool.GetAvailableThreads(out var workers, out var ports);
            Console.WriteLine($"CHECKPOINT_NATIVE fixture={fixture} pid={Environment.ProcessId} utc={DateTimeOffset.UtcNow:O} "
                + $"os={Environment.OSVersion} runtime={Environment.Version} workers={workers} ioThreads={ports} roots={roots.Count}");
            var entriesLeft = 96;
            var receiptsLeft = 8;
            // Later allocations are nearest a body failure. All omissions are explicit.
            foreach (var root in roots.Reverse().Take(8))
            {
                Console.WriteLine("CHECKPOINT_NATIVE root=" + root);
                if (!ContainedCleanup.SafeAncestors(root))
                {
                    Console.WriteLine("CHECKPOINT_NATIVE inventory refused: unsafe ancestors");
                    continue;
                }
                var pending = new Queue<(string Path, int Depth)>();
                pending.Enqueue((root, 0));
                while (pending.Count > 0 && entriesLeft > 0)
                {
                    var (path, depth) = pending.Dequeue();
                    entriesLeft--;
                    try
                    {
                        var attributes = File.GetAttributes(path);
                        Console.WriteLine($"CHECKPOINT_NATIVE path={path} attributes={attributes} mtime={File.GetLastWriteTimeUtc(path):O}");
                        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0) continue;
                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            if (Path.GetFileName(path) == ".git") continue;
                            if (depth == 6) { Console.WriteLine("CHECKPOINT_NATIVE depth limit=" + path); continue; }
                            foreach (var entry in Directory.EnumerateFileSystemEntries(path).Take(entriesLeft + 1))
                                pending.Enqueue((entry, depth + 1));
                        }
                        else
                        {
                            Console.WriteLine($"CHECKPOINT_NATIVE length={new FileInfo(path).Length} path={path}");
                            if (receiptsLeft > 0 && Path.GetFileName(path) is ("state.json" or "report.json"
                                or "checkpoint-build-source.json" or "ownership.json" or "executor-ownership.json"))
                            {
                                receiptsLeft--;
                                Receipt(root, path);
                            }
                        }
                    }
                    catch (Exception error) { Console.WriteLine("CHECKPOINT_NATIVE inspection " + path + ": " + error); }
                }
                if (pending.Count > 0) Console.WriteLine("CHECKPOINT_NATIVE inventory truncated=" + root);
            }
            if (roots.Count > 8 || receiptsLeft == 0) Console.WriteLine("CHECKPOINT_NATIVE root/receipt limit reached");
        }
        catch (Exception error) { Console.WriteLine("CHECKPOINT_NATIVE diagnostic failure: " + error); }
    }

    private static void Receipt(string root, string path)
    {
        var watch = Stopwatch.StartNew();
        Console.WriteLine("CHECKPOINT_NATIVE receipt begin=" + path);
        try
        {
            var context = CheckpointCleanupDefaults.Context;
            var request = context.Io.Platform == CheckpointPlatform.Windows
                ? new CheckpointReadOpen(0, 0x00200000 | 0x08000000)
                : new CheckpointReadOpen(0x80000 | 0x20000 | 0x800, 0);
            using (var handle = context.Io.OpenRead(path, request))
                Console.WriteLine("CHECKPOINT_NATIVE retained-handle=" + JsonSerializer.Serialize(handle.Inspect()));
            // Existing verified reader/deadline; a separate 32 KiB diagnostic cap.
            using var json = JsonDocument.Parse(ConfinedFileReader.Read(root, path, 32 * 1024));
            Fields(json.RootElement, "receipt");
            foreach (var property in json.RootElement.EnumerateObject())
                if (property.Name is "rows" or "builds" && property.Value.ValueKind == JsonValueKind.Array)
                    foreach (var item in property.Value.EnumerateArray().Take(8)) Fields(item, property.Name);
        }
        catch (Exception error) { Console.WriteLine("CHECKPOINT_NATIVE receipt " + path + ": " + error); }
        finally { Console.WriteLine($"CHECKPOINT_NATIVE receipt end={path} elapsedMs={watch.Elapsed.TotalMilliseconds:F1}"); }
    }

    private static void Fields(JsonElement value, string kind)
    {
        if (value.ValueKind != JsonValueKind.Object) return;
        string[] allowed = ["id", "runId", "phase", "state", "exitCode", "reason", "verdict", "slot", "slotReason",
            "terminalReason", "executed", "passed", "failed", "skipped", "repositoryRoot", "project", "outputPath",
            "sourceState", "commit", "fingerprint", "cleanupRefusals"];
        foreach (var field in value.EnumerateObject())
            if (allowed.Contains(field.Name, StringComparer.OrdinalIgnoreCase))
                Console.WriteLine($"CHECKPOINT_NATIVE {kind}.{field.Name}={Cap(field.Value.GetRawText())}");
    }

    internal static void Driver(DriverRequest request)
    {
        if (Enabled) Console.WriteLine("CHECKPOINT_NATIVE fake-driver=" + JsonSerializer.Serialize(new
            { request.FileName, request.Arguments, request.WorkingDirectory }));
    }

    internal static int Coverage(int exit, string root, StringWriter output)
    {
        if (exit == 0) return exit;
        try
        {
            Console.WriteLine("CHECKPOINT_NATIVE coverage=" + Cap(output.ToString(), 16 * 1024));
            using var json = JsonDocument.Parse(output.ToString());
            if (json.RootElement.TryGetProperty("diagnostics", out var diagnostics))
                foreach (var item in diagnostics.EnumerateArray().Take(3))
                    if (item.TryGetProperty("testPath", out var path) && path.ValueKind == JsonValueKind.String)
                    {
                        var full = Path.GetFullPath(Path.Combine(root, path.GetString()!));
                        if (CheckpointCleanupDefaults.Context.IsStrictlyInside(root, full)
                            && ContainedCleanup.SafeAncestors(full)) Receipt(root, full);
                    }
        }
        catch (Exception error) { Console.WriteLine("CHECKPOINT_NATIVE coverage diagnostic: " + error); }
        return exit;
    }

    private static string Cap(string text, int limit = 1024) => text.Length <= limit ? text : text[..limit] + " [truncated]";
}
