using System.Reflection;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

// The probe's AppDbContext model. Every probe is a fresh process, and building the AppDbContext model from scratch
// took about 7 s of each 11-14 s run on the HP laptop. When the parent passes the compiled model the probe generated
// from this same build (ProbeConfig.CompiledModelPath, ProbeModelGenerator), every context here uses it; without one
// the probe builds the model itself, as before.
internal static class ProbeModel
{
    private static readonly object Gate = new();
    private static string? _path;
    private static IModel? _model;

    public static void Use(string? compiledModelPath) => _path = compiledModelPath;

    public static DbContextOptionsBuilder<AppDbContext> WithProbeModel(this DbContextOptionsBuilder<AppDbContext> builder)
    {
        ((DbContextOptionsBuilder)builder).WithProbeModel();
        return builder;
    }

    public static DbContextOptionsBuilder WithProbeModel(this DbContextOptionsBuilder builder) =>
        _path is null ? builder : builder.UseModel(Model());

    public static IModel Load(Assembly generated) =>
        (IModel)generated.GetType(ProbeModelGenerator.Namespace + ".AppDbContextModel", throwOnError: true)!
            .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

    private static IModel Model()
    {
        lock (Gate) return _model ??= Load(Assembly.LoadFrom(_path!));
    }
}
