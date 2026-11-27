using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

// The probe's AppDbContext model. Every probe is a fresh process, and building the AppDbContext model from scratch
// took about 7 s of each 11-14 s run on the HP laptop. When the parent passes the compiled model the probe generated
// from this same build (ProbeConfig.CompiledModelPath, ProbeModelGenerator), every context here uses it; without one
// the probe builds the model itself, as before. A given model that does not load fails the probe at its start (the
// top-level handler prints the exception type), never a silent rebuild that only makes every probe slower.
internal static class ProbeModel
{
    // The generated AppDbContextModel's namespace (ProbeModelGenerator writes it there).
    public const string GeneratedNamespace = "Antiphon.ChannelOutbound.Probe.GeneratedModel";
    internal const string AssemblyName = GeneratedNamespace;
    internal const string BuiltDirectory = "verified-model";
    internal const string IdentityFile = "model.identity";

    internal static string? FindBuiltArtifact(string probeDirectory, string key)
    {
        var directory = Path.Combine(probeDirectory, BuiltDirectory);
        var image = Path.Combine(directory, AssemblyName + ".dll");
        try
        {
            return File.Exists(image) && File.ReadAllText(Path.Combine(directory, IdentityFile)) == key
                ? image : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // A build or runtime cache is valid only for the probe, its model assemblies and its provider.
    internal static string BuildKey(string probeDirectory)
    {
        var mvids = new StringBuilder();
        foreach (var name in new[]
                 {
                     "Antiphon.ChannelOutbound.Probe.dll", "Antiphon.Server.dll", "Microsoft.EntityFrameworkCore.dll",
                     "Microsoft.EntityFrameworkCore.Relational.dll", "Npgsql.EntityFrameworkCore.PostgreSQL.dll",
                     "Npgsql.dll",
                 })
        {
            using var stream = File.OpenRead(Path.Combine(probeDirectory, name));
            using var pe = new PEReader(stream);
            var metadata = pe.GetMetadataReader();
            mvids.Append(name).Append('=').Append(metadata.GetGuid(metadata.GetModuleDefinition().Mvid)).Append('\n');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(mvids.ToString())))[..16].ToLowerInvariant();
    }

    private static IModel? _model;

    public static void Use(string? compiledModelPath)
    {
        if (compiledModelPath is null) return;
        try
        {
            _model = Load(System.Reflection.Assembly.LoadFrom(compiledModelPath));
        }
        catch (Exception ex)
        {
            // ProbeLog's contract: the exception type, never its message.
            ProbeLog.Phase("model-not-loaded " + Path.GetFileName(compiledModelPath) + " " + ex.GetType().Name);
            throw;
        }
    }

    public static DbContextOptionsBuilder<AppDbContext> WithProbeModel(this DbContextOptionsBuilder<AppDbContext> builder)
    {
        ((DbContextOptionsBuilder)builder).WithProbeModel();
        return builder;
    }

    public static DbContextOptionsBuilder WithProbeModel(this DbContextOptionsBuilder builder) =>
        _model is null ? builder : builder.UseModel(_model);

    public static IModel Load(System.Reflection.Assembly generated) =>
        (IModel)generated.GetType(GeneratedNamespace + ".AppDbContextModel", throwOnError: true)!
            .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
}
