#pragma warning disable EF1001 // The design-time services dotnet ef dbcontext optimize uses (DesignTimeServicesBuilder).
using System.Diagnostics;
using System.Reflection;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Design.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.Extensions.DependencyInjection;

// `--generate-model <dir>`: writes <dir>/<AssemblyName>.dll, AppDbContext's compiled model. It is scaffolded from the
// model this process's own Antiphon.Server builds, by the scaffolder `dotnet ef dbcontext optimize` uses, and compiled
// against exactly the assemblies this process runs on, so it is never stale: each probe build makes its own. It is
// written only when it matches the runtime model (ProbeModelComparison); otherwise this exits 3 and writes no dll.
// The parent test process runs this once per probe build (ProbeGeneratedModel in Antiphon.Tests) and passes the dll to
// each probe (ProbeConfig.CompiledModelPath, ProbeModel), which then does not build the model itself.
internal static class ProbeModelGenerator
{
    public const string Namespace = ProbeModel.GeneratedNamespace;
    public const string AssemblyName = "Antiphon.ChannelOutbound.Probe.GeneratedModel";

    public static int Run(string outDir)
    {
        var total = Stopwatch.StartNew();
        var sources = Path.Combine(outDir, "src");
        Directory.CreateDirectory(sources);
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=unused").Options);
        var reporter = new OperationReporter(new OperationReportHandler(
            errorHandler: m => Console.Error.WriteLine("model error: " + m),
            warningHandler: m => Console.Error.WriteLine("model warning: " + m)));
        var server = typeof(AppDbContext).Assembly;
        var services = new DesignTimeServicesBuilder(server, server, reporter, []).Build(db);
        var files = services.GetRequiredService<ICompiledModelScaffolder>().ScaffoldModel(
            db.GetService<IDesignTimeModel>().Model, sources, new CompiledModelCodeGenerationOptions
            {
                ContextType = typeof(AppDbContext), ModelNamespace = Namespace, Language = "C#",
                UseNullableReferenceTypes = true,
            });
        var scaffoldMs = total.ElapsedMilliseconds;
        var parse = new CSharpParseOptions(LanguageVersion.Latest);
        var trees = files.Where(f => f.EndsWith(".cs", StringComparison.Ordinal))
            .Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), parse, f)).ToList();
        // The framework and the probe's own dependencies: the assemblies the probes load the model with.
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create(AssemblyName, trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release, nullableContextOptions: NullableContextOptions.Enable));
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        if (!emitted.Success)
        {
            foreach (var error in emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Take(20))
                Console.Error.WriteLine("model compile: " + error);
            return 1;
        }
        var compileMs = total.ElapsedMilliseconds - scaffoldMs;
        // Loaded from the bytes, so the file is written only once it is known to match.
        var model = ProbeModel.Load(Assembly.Load(image.ToArray()));
        using var compiled = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=unused")
            .UseModel(model).Options);
        var differences = ProbeModelComparison.Differences(db.Model, compiled.Model);
        if (differences.Count > 0)
        {
            foreach (var difference in differences) Console.Error.WriteLine("model differs: " + difference);
            return 3;
        }
        var dll = Path.Combine(outDir, AssemblyName + ".dll");
        File.WriteAllBytes(dll + ".tmp", image.ToArray());
        File.Move(dll + ".tmp", dll, overwrite: true);
        Directory.Delete(sources, recursive: true);
        Console.WriteLine($"probe model generated files={trees.Count} scaffoldMs={scaffoldMs} compileMs={compileMs} "
            + $"totalMs={total.ElapsedMilliseconds}");
        return 0;
    }
}
