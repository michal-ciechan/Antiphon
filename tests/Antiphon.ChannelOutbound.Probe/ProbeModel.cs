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
        Model() is { } model ? builder.UseModel(model) : builder;

    public static IModel Load(Assembly generated) =>
        (IModel)generated.GetType(ProbeModelGenerator.Namespace + ".AppDbContextModel", throwOnError: true)!
            .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

    // A model that does not load (its dll removed or damaged after the parent checked it) is reported once, and the
    // probe then builds the model itself.
    private static IModel? Model()
    {
        lock (Gate)
        {
            if (_model is not null || _path is null) return _model;
            try
            {
                return _model = Load(Assembly.LoadFrom(_path));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("probe model " + _path + " not loaded (" + ex.GetType().Name + ": " + ex.Message
                    + "); building the model");
                _path = null;
                return null;
            }
        }
    }
}
