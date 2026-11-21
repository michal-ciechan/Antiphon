using System.Reflection;
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
    private static IModel? _model;

    public static void Use(string? compiledModelPath)
    {
        if (compiledModelPath is null) return;
        try
        {
            _model = Load(Assembly.LoadFrom(compiledModelPath));
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

    public static IModel Load(Assembly generated) =>
        (IModel)generated.GetType(ProbeModelGenerator.Namespace + ".AppDbContextModel", throwOnError: true)!
            .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
}
