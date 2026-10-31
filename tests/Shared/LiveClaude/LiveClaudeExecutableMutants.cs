using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Antiphon.TestSupport.LiveClaude;

// Cheap method-scoped positive controls compile only the owned in-process resolver/fixture.
// They never load a provider/native adapter or rewrite the checkout.
internal static class LiveClaudeExecutableMutants
{
    private sealed record Mutation(string Pc, string V, string From, string To, string Assertion);
    private static readonly Mutation[] mutations =
    [
        new("PC-1", "V-1", "new LiveClaudeExecutable(context.Image.Path, context)", "new LiveClaudeExecutable(legacy(), context)", "owned"),
        new("PC-8", "V-2", "if (!LiveClaudeCore.Contains(uid))", "if (request is null || !LiveClaudeCore.Contains(uid))", "missing-context"),
        new("PC-9", "V-5", "if (!LiveClaudeCore.Contains(uid))", "if (request is null || !LiveClaudeCore.Contains(uid))", "missing-context"),
        new("PC-10", "V-2", "if (request is null || context is null) throw new LiveClaudeRefusal(\"missing-context\");", "if (request is null || context is null) return new(legacy(), null);", "missing-context"),
        new("PC-11", "V-2", "if (string.IsNullOrWhiteSpace(request.Executable)) throw new LiveClaudeRefusal(\"missing-exe\");", "if (string.IsNullOrWhiteSpace(request.Executable)) request = request with { Executable = context.Image.Path };", "missing-exe"),
        new("PC-12", "V-2", "if (request.Binding.Uid != uid || context.Binding.Uid != uid)", "if (context.Binding.Uid != uid)", "context-UID"),
        new("PC-13", "V-2", "if (request.Binding.Run != context.Binding.Run)", "if (false)", "context-run"),
        new("PC-14", "V-2", "if (request.Binding.Attempt != context.Binding.Attempt)", "if (false)", "context-attempt"),
        new("PC-15", "V-2", "if (request.Binding.Source != context.Binding.Source)", "if (false)", "context-source-SHA"),
        new("PC-16", "V-2", "if (request.Binding.Profile != context.Binding.Profile)", "if (false)", "context-profile-identity"),
        new("PC-17", "V-2", "if (!current.Held)", "if (false)", "lock-released"),
        new("PC-18", "V-2", "if (string.IsNullOrWhiteSpace(context.LockOwner) || current.Owner != context.LockOwner)", "if (false)", "lock-replaced"),
        new("PC-22", "V-3", "if (!SamePath(observation.Image.Path, Path))", "if (false)", "image-path"),
        new("PC-23", "V-3", "if (observation.Image.Sha256 != context!.Image.Sha256)", "if (false)", "image-hash"),
        new("PC-24", "V-3", "if (observation.Process.Pid != process.Pid)", "if (false)", "image-PID"),
        new("PC-25", "V-3", "if (observation.Process.Started != process.Started)", "if (false)", "image-start-identity"),
        new("PC-26", "V-3", "if (observation.Process.Session != process.Session)", "if (false)", "image-session"),
        new("PC-27", "V-3", "if (observation is null) throw new LiveClaudeRefusal(\"executable-mismatch\");", "observation ??= new LiveClaudeObservation(process, context!.Image, context.Chain);", "post-launch-missing"),
        new("PC-28", "V-3", "Digest(image.Path) != image.Sha256", "image.Sha256 != image.Sha256", "post-drift"),
        new("PC-29", "V-3", "if (observation.Chain.Count != context.Chain.Count || !observation.Chain.SequenceEqual(context.Chain))", "if (false)", "unbound-chain")
    ];
    public static void Run(string v)
    {
        var root = Path.Combine(LiveClaudeExecutableAudit.Root(), "tests/Shared/LiveClaude");
        var source = File.ReadAllText(Path.Combine(root, "LiveClaudeExecutable.cs"));
        var support = new[] { "LiveClaudeLaunchContext.cs", "LiveClaudeExecutableCases.cs" }
            .Select(p => File.ReadAllText(Path.Combine(root, p))).ToArray();
        foreach (var m in mutations.Where(m => m.V == v))
        {
            var site = source.IndexOf(m.From, StringComparison.Ordinal);
            if (site < 0 || source.IndexOf(m.From, site + m.From.Length, StringComparison.Ordinal) >= 0)
                throw new InvalidOperationException($"C1545 {m.Pc} mutation site must be unique");
            var changed = source.Replace(m.From, m.To, StringComparison.Ordinal);
            var error = Execute(changed, support, v);
            if (error is null || !error.Message.StartsWith($"C1545 {v}/{m.Assertion}", StringComparison.Ordinal))
                throw new InvalidOperationException($"C1545 {m.Pc} expected assertion-red {m.Assertion}; got {error?.GetType().Name}: {error?.Message}");
            var restored = Execute(source, support, v);
            if (restored is not null) throw new InvalidOperationException($"C1545 {m.Pc} restored green failed", restored);
            Console.WriteLine($"C1545 {m.Pc} assertion-red {m.Assertion}; restored-green (compiled owned source copy)");
        }
    }
    private static Exception? Execute(string source, string[] support, string v)
    {
        const string entry = """
            namespace Antiphon.TestSupport.LiveClaude;
            public static class MutantEntry {
              public static void Run(string v) {
                using var f = new LiveClaudeExecutableCases(v == "V-5" ? LiveClaudeCore.C680 : LiveClaudeCore.Submit,
                  (u,r,c,l) => LiveClaudeExecutable.Resolve(u,r,c,l));
                if (v == "V-1") f.Selection(v);
                else if (v == "V-2" || v == "V-5") f.Admission(v);
                else f.Images(v);
              }
            }
            """;
        const string usings = "global using System; global using System.IO; global using System.Linq; global using System.Collections.Generic;\n";
        var trees = support.Append(source).Append(entry).Select(s => CSharpSyntaxTree.ParseText(usings + s)).ToArray();
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(p => MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create("C1545OwnedMutant" + Guid.NewGuid().ToString("N"), trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        using var bytes = new MemoryStream();
        var result = compilation.Emit(bytes);
        if (!result.Success) throw new InvalidOperationException("C1545 mutant compile error: "
            + string.Join(";", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        bytes.Position = 0;
        var load = new AssemblyLoadContext("C1545-owned-copy", isCollectible: true);
        try
        {
            var assembly = load.LoadFromStream(bytes);
            try { assembly.GetType("Antiphon.TestSupport.LiveClaude.MutantEntry")!.GetMethod("Run")!.Invoke(null, [v]); }
            catch (TargetInvocationException e) { return e.InnerException!; }
            return null;
        }
        finally { load.Unload(); }
    }
}
