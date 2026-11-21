using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// The channel-outbound probe's compiled AppDbContext model (ProbeModelGenerator in the probe). Each probe is a fresh
/// process that otherwise builds the whole model itself (about 7 of its 11-14 s on the HP laptop). The probe generates
/// the model from its own build, in about 25 s, and publishes it only when it matches the runtime model
/// (ProbeModelComparison), so it is never stale or different. It is generated once per probe build and cached
/// beside the staged probe (channel-outbound-probe-model/&lt;key&gt;, keyed by the MVIDs of the probe and its model
/// assemblies), so later test processes of the same build reuse it. When it cannot be generated this returns no
/// path and the probes build the model themselves, as before (slower, not different), and
/// ChannelOutboundProbeModelTests fails. Callers ask before a test's own deadline starts (each C519 class's
/// Before(Class)); the result is shared by every caller in the process.
/// </summary>
internal static class ProbeGeneratedModel
{
    public const string Label = "C519_PROBE_MODEL";
    private const string AssemblyName = "Antiphon.ChannelOutbound.Probe.GeneratedModel";
    private static readonly TimeSpan GenerateBound = TimeSpan.FromSeconds(300);
    private static readonly Lazy<Task<Result>> Generated = new(GenerateAsync);

    internal sealed record Result(string? Path, string Detail);

    public static Task<Result> ResultAsync() => Generated.Value;

    public static async Task<string?> PathAsync() => (await Generated.Value).Path;

    private static async Task<Result> GenerateAsync()
    {
        var watch = Stopwatch.StartNew();
        using var phase = TestTiming.BeginPhase(TestPhaseKind.FixtureSetup, "probe-generated-model");
        try
        {
            var probe = Path.Combine(AppContext.BaseDirectory, "channel-outbound-probe");
            var root = Path.Combine(AppContext.BaseDirectory, "channel-outbound-probe-model");
            var key = Key(probe);
            var published = Path.Combine(root, key);
            var dll = Path.Combine(published, AssemblyName + ".dll");
            if (File.Exists(dll)) return Report(new(dll, "reused key=" + key), watch);
            var work = Path.Combine(root, key + ".tmp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                var (exit, output) = await RunAsync(Path.Combine(probe, "Antiphon.ChannelOutbound.Probe.dll"), work);
                if (exit != 0 || !File.Exists(Path.Combine(work, AssemblyName + ".dll")))
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
                if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
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

    // The probe, its model assemblies and the provider: a rebuild of any of them is a new key, so a cached model is
    // always the one this probe build generated.
    private static string Key(string probe)
    {
        var mvids = new StringBuilder();
        foreach (var name in new[]
                 {
                     "Antiphon.ChannelOutbound.Probe.dll", "Antiphon.Server.dll", "Microsoft.EntityFrameworkCore.dll",
                     "Microsoft.EntityFrameworkCore.Relational.dll", "Npgsql.EntityFrameworkCore.PostgreSQL.dll",
                     "Npgsql.dll",
                 })
        {
            using var stream = File.OpenRead(Path.Combine(probe, name));
            using var pe = new PEReader(stream);
            var metadata = pe.GetMetadataReader();
            mvids.Append(name).Append('=').Append(metadata.GetGuid(metadata.GetModuleDefinition().Mvid)).Append('\n');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(mvids.ToString())))[..16].ToLowerInvariant();
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
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            await process.WaitForExitAsync();
            return (-1, "timed out after " + GenerateBound.TotalSeconds + " s " + await stderr);
        }
        return (process.ExitCode, ((await stdout) + (await stderr)).Trim());
    }
}
