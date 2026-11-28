using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// The channel-outbound probe's compiled AppDbContext model (ProbeModelGenerator in the probe). Each probe is a fresh
/// process that otherwise builds the whole model itself (about 7 of its 11-14 s on the HP laptop). The probe generates
/// the model from its own build, in about 25 s, and publishes it only when it matches the runtime model
/// (ProbeModelComparison), so it is never stale or different. It is generated once per probe build and cached
/// beside the staged probe (channel-outbound-probe-model/&lt;key&gt;, keyed by the MVIDs of the probe and its model
/// assemblies), so later test processes of the same build reuse it. When it cannot be generated this returns no
/// path and the probes build the model themselves, as before (slower, not different), and
/// ChannelOutboundProbeModelTests fails. Callers ask before a test's own deadline starts (the Before(Class) of each
/// class that starts probes: the C519 classes and ChannelOutboundRecoveryTests); the result is shared by every caller
/// in the process. ChannelOutboundDeliveryTests' one restart probe is left on the runtime model: generating for it
/// alone would cost more than it saves.
/// </summary>
internal static class ProbeGeneratedModel
{
    public const string Label = "C519_PROBE_MODEL";
    // Measured 20-27 s on the HP laptop (5 runs, an otherwise idle host). About 3x rather than R5's 2x p95 because
    // it runs once per probe build, usually while other launch groups load the host, and a time-out costs every probe
    // of the run (about 6 s each) and turns ChannelOutboundProbeModelTests red. Well inside TUnit's 5-minute hook
    // timeout, so a stuck generation ends as "unavailable" rather than failing the C519 classes. Provisional (R5).
    private static readonly TimeSpan GenerateBound = TimeSpan.FromSeconds(90);
    // A reused model is touched (Reuse), so only an entry no test process has used for a day is pruned.
    private static readonly TimeSpan StaleAfter = TimeSpan.FromDays(1);
    private static readonly Regex CacheEntry = new(@"^[0-9a-f]{16}(\.tmp-[0-9a-f]{32})?$", RegexOptions.CultureInvariant);
    private static readonly Lazy<Task<Result>> Generated = new(GenerateAsync);

    internal sealed record Result(string? Path, string Detail);

    public static Task<Result> ResultAsync() => Generated.Value;

    public static async Task<string?> PathAsync() => (await Generated.Value).Path;

    // Build verification must fail if the stage is incomplete, even if runtime generation can recover.
    internal static string RequireStagedArtifact()
    {
        var probe = Path.Combine(AppContext.BaseDirectory, "channel-outbound-probe");
        var key = ProbeModel.BuildKey(probe);
        return ProbeModel.FindBuiltArtifact(probe, key)
            ?? throw new InvalidOperationException("Missing or mismatched staged verified probe model for build " + key);
    }

    internal static async Task<string> RequireSelectedStagedArtifactAsync()
    {
        var staged = RequireStagedArtifact();
        var result = await ResultAsync();
        if (!string.Equals(result.Path, staged, StringComparison.Ordinal))
            throw new InvalidOperationException("Probe model selection did not use the verified stage: " + result.Detail);
        return staged;
    }

    /// <summary>
    /// Adds the compiled model to a probe config a test wrote itself, once the model is ready (the caller's class asks
    /// for it in its Before(Class)); before that, or without one, the config is left as it is and the probe builds
    /// the model, as before.
    /// </summary>
    public static void AddTo(string configPath)
    {
        if (!Generated.IsValueCreated || !Generated.Value.IsCompletedSuccessfully)
        {
            // A caller without the Before(Class): said, so the slower probes are not silent.
            Console.WriteLine(Label + " not ready for " + Path.GetFileName(configPath) + "; the probe builds the model");
            return;
        }
        if (Generated.Value.Result.Path is not { } path) return;
        var config = JsonNode.Parse(File.ReadAllText(configPath))!.AsObject();
        config["CompiledModelPath"] = path;
        File.WriteAllText(configPath, config.ToJsonString());
    }

    private static async Task<Result> GenerateAsync()
    {
        var watch = Stopwatch.StartNew();
        using var phase = TestTiming.BeginPhase(TestPhaseKind.FixtureSetup, "probe-generated-model");
        try
        {
            var probe = Path.Combine(AppContext.BaseDirectory, "channel-outbound-probe");
            var root = Path.Combine(AppContext.BaseDirectory, "channel-outbound-probe-model");
            var key = ProbeModel.BuildKey(probe);
            if (ProbeModel.FindBuiltArtifact(probe, key) is { } built)
                return Report(new(built, "build artifact key=" + key), watch);
            var published = Path.Combine(root, key);
            var dll = Path.Combine(published, ProbeModel.AssemblyName + ".dll");
            if (File.Exists(dll)) return Report(new(Reuse(published, dll), "reused key=" + key), watch);
            Directory.CreateDirectory(root);
            Prune(root, key);
            var work = Path.Combine(root, key + ".tmp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                var (exit, output) = await RunAsync(Path.Combine(probe, "Antiphon.ChannelOutbound.Probe.dll"), work);
                if (exit != 0 || !File.Exists(Path.Combine(work, ProbeModel.AssemblyName + ".dll")))
                    return Report(new(null, "generation failed exit=" + exit + " " + output), watch);
                try
                {
                    Directory.Move(work, published);
                }
                catch (IOException) when (File.Exists(dll))
                {
                    // Another test process of the same build published it first: the same model.
                }
                return Report(new(File.Exists(dll) ? dll : null, "generated key=" + key + " " + output), watch);
            }
            finally
            {
                TryDelete(new DirectoryInfo(work));
            }
        }
        catch (Exception ex)
        {
            return Report(new(null, ex.GetType().Name + ": " + ex.Message), watch);
        }
    }

    private static Result Report(Result result, Stopwatch watch)
    {
        Console.WriteLine(Label + (result.Path is null ? " unavailable " : " ready ") + "ms=" + watch.ElapsedMilliseconds
            + " " + result.Detail.ReplaceLineEndings(" | ").Trim());
        return result;
    }

    private static string Reuse(string published, string dll)
    {
        try
        {
            Directory.SetLastWriteTimeUtc(published, DateTime.UtcNow);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        return dll;
    }

    // Earlier builds' models no test process has used for a day, and work directories a killed test process left
    // (a work directory of another process generating now is younger, and is kept). Only this cache's own entry
    // names. Best effort: nothing here may stop the generation.
    private static void Prune(string root, string key)
    {
        try
        {
            foreach (var entry in new DirectoryInfo(root).EnumerateDirectories())
            {
                try
                {
                    if (entry.Name == key || !CacheEntry.IsMatch(entry.Name)) continue;
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if (DateTime.UtcNow - entry.LastWriteTimeUtc < StaleAfter) continue;
                    if (!string.Equals(Path.GetFullPath(entry.FullName), Path.Combine(root, entry.Name), StringComparison.Ordinal)) continue;
                    TryDelete(entry);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // Best effort: a file another process still holds leaves the entry for a later prune.
    private static void TryDelete(DirectoryInfo directory)
    {
        try
        {
            if (directory.Exists) directory.Delete(recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static async Task<(int Exit, string Output)> RunAsync(string probeDll, string work)
    {
        var start = new ProcessStartInfo("dotnet")
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(probeDll);
        start.ArgumentList.Add("--generate-model");
        start.ArgumentList.Add(work);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("model generator did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var bound = new CancellationTokenSource(GenerateBound);
        try
        {
            await process.WaitForExitAsync(bound.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception)
            {
                // Already gone, or part of the tree could not be killed: the bounded wait below decides.
            }
            var timedOut = "timed out after " + GenerateBound.TotalSeconds + " s";
            using var exited = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await process.WaitForExitAsync(exited.Token);
            }
            catch (OperationCanceledException)
            {
                // Still running: its pipes stay open, so its output is not awaited (only observed).
                _ = stdout.ContinueWith(t => t.Exception, TaskScheduler.Default);
                _ = stderr.ContinueWith(t => t.Exception, TaskScheduler.Default);
                return (-1, timedOut + ", not exited after the kill");
            }
            // Exited: what it wrote shows where it was.
            var output = Task.WhenAll(stdout, stderr);
            if (await Task.WhenAny(output, Task.Delay(TimeSpan.FromSeconds(5))) != output)
            {
                _ = output.ContinueWith(t => t.Exception, TaskScheduler.Default);
                return (-1, timedOut);
            }
            return (-1, (timedOut + " " + (await stdout) + (await stderr)).Trim());
        }
        return (process.ExitCode, ((await stdout) + (await stderr)).Trim());
    }
}
