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
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql.EntityFrameworkCore.PostgreSQL.Design.Internal;

// `--generate-model <dir>`: writes <dir>/<AssemblyName>.dll, AppDbContext's compiled model. It is scaffolded from the
// model this process's own Antiphon.Server builds, by the scaffolder `dotnet ef dbcontext optimize` uses, and compiled
// against exactly the assemblies this process runs on, so it is never stale: each probe build makes its own. It is
// written only when it matches the runtime model (ProbeModelComparison); otherwise this exits 3 and writes no dll.
// The parent test process runs this once per probe build (ProbeGeneratedModel in Antiphon.Tests) and passes the dll to
// each probe (ProbeConfig.CompiledModelPath, ProbeModel), which then does not build the model itself.
public static class ProbeModelGenerator
{
    public const string Namespace = ProbeModel.GeneratedNamespace;
    public const string AssemblyName = "Antiphon.ChannelOutbound.Probe.GeneratedModel";

    private static readonly Lazy<IRelationalTypeMappingSource> ElementMappings = new(() =>
    {
        using var db = new DbContext(new DbContextOptionsBuilder().UseNpgsql("Host=unused").Options);
        return db.GetService<IRelationalTypeMappingSource>();
    });

    // The generated assembly calls this during initialization. Npgsql supplies its conventional
    // mapping and comparer without building AppDbContext's model or opening a connection.
    public static RelationalTypeMapping ResolveElementMapping(Type clrType, string storeType) =>
        ElementMappings.Value.FindMapping(clrType, storeType)
        ?? throw new InvalidOperationException($"No Npgsql mapping for {clrType} as {storeType}.");

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
        using var services = new DesignTimeServicesBuilder(server, server, reporter, []).CreateServiceCollection(db)
            .AddSingleton<ICSharpRuntimeAnnotationCodeGenerator, PrimitiveCollectionCodeGenerator>()
            .BuildServiceProvider();
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

    // EF 9 scaffolds the property without its primitive collection element metadata. Supply that metadata
    // for conventional arrays and let Npgsql resolve the mappings with its original comparers.
    private sealed class PrimitiveCollectionCodeGenerator(
        CSharpRuntimeAnnotationCodeGeneratorDependencies dependencies,
        RelationalCSharpRuntimeAnnotationCodeGeneratorDependencies relationalDependencies)
        : NpgsqlCSharpRuntimeAnnotationCodeGenerator(dependencies, relationalDependencies)
    {
        public override void Generate(IProperty property, CSharpRuntimeAnnotationCodeGeneratorParameters parameters)
        {
            base.Generate(property, parameters);
            if (parameters.IsRuntime || !property.IsPrimitiveCollection || property.GetElementType() is not { } element)
                return;

            // Leave additional element configuration to the parity check, which refuses missing metadata.
            if (element.GetAnnotations().Any()
                || element.FindTypeMapping() is not RelationalTypeMapping mapping
                || !ReferenceEquals(mapping, property.GetTypeMapping().ElementTypeMapping))
                return;

            var code = Dependencies.CSharpHelper;
            parameters.MainBuilder.AppendLine(
                $"{parameters.TargetName}.SetElementType({code.Literal(element.ClrType, fullName: true)}, "
                + $"nullable: {code.Literal(element.IsNullable)}, "
                + $"typeMapping: global::ProbeModelGenerator.ResolveElementMapping("
                + $"{code.Literal(mapping.ClrType, fullName: true)}, {code.Literal(mapping.StoreType)}), "
                + "primitiveCollection: true);");
        }
    }
}
