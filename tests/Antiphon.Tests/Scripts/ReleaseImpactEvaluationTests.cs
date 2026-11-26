using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.ReleaseSelection;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReleaseImpactEvaluationTests
{
    [Test]
    [Timeout(30_000)]
    public async Task ProofRequiresBoundCompilerInputs(CancellationToken cancellationToken)
    {
        const string project = "tests/T/T.csproj";
        const string north = "tests/T/A.cs";
        const string south = "tests/T/B.cs";
        const string use = "tests/T/Use.cs";
        const string generated = "tests/T/Gen.g.cs";
        var files = new Dictionary<string, string>
        {
            [".gitignore"] = ".antiphon/selector-replay/\n",
            [project] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net9.0</TargetFramework><AssemblyName>T</AssemblyName>
                    <LangVersion>12</LangVersion><ImplicitUsings>disable</ImplicitUsings><Deterministic>true</Deterministic>
                  </PropertyGroup>
                </Project>
                """,
            [north] = "namespace North; public class Probe {}\n",
            [south] = "namespace South; public class Probe {}\n",
            [use] = "namespace Cases; public partial class SpecTests { public void Check() { _ = typeof(Chosen); } }\n",
        };
        await using var repo = await LandGraphEvaluationTests.AuditRepo.CreateAsync(files);
        var sha = Sha(repo);
        const string outputRoot = ".antiphon/selector-replay";
        var authority = repo.Full(outputRoot + "/authority");
        Directory.CreateDirectory(authority);
        var manifestPath = Path.Combine(authority, "manifest.json");
        var reference = File.ReadAllBytes(typeof(object).Assembly.Location);
        var aliases = new[] { "global", "Core" };
        var referenceDigest = Store(reference);
        var sources = new[] { north, south, use }.Select(path =>
        {
            var bytes = File.ReadAllBytes(repo.Full(path));
            var header = Encoding.ASCII.GetBytes(FormattableString.Invariant($"blob {bytes.Length}\0"));
            return new ReleaseCompilerSource(path, Store(bytes), Convert.ToHexStringLower(SHA1.HashData([..header, ..bytes])));
        }).ToArray();
        var gitBlobs = sources.ToDictionary(source => source.Path, source => source.GitBlob!, StringComparer.Ordinal);
        using (var git = new LibGit2Sharp.Repository(repo.Root))
            gitBlobs.Add(project, git.Head.Tip.Tree[project].Target.Id.Sha);
        var generatorOutput = Encoding.UTF8.GetBytes("global using Chosen = North.Probe;\n");
        var gen = new ReleaseCompilerSource(generated, Store(generatorOutput), null);
        var context = new ReleaseCompilerContext(project, "T", "windows", "x64", "net9.0", "win-x64", "Release", "AnyCPU",
            "12", ["FAST"], "disable", false, false, "release", "library",
            [..sources, gen], [new ReleaseCompilerReference(referenceDigest, aliases, false)]);
        var corePath = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var sdkRoot = Path.GetFullPath(Path.Combine(corePath, "..", "..", "..", "sdk"));
        var sdk = Directory.EnumerateDirectories(sdkRoot).Order(StringComparer.Ordinal).Last();
        var manifest = new ReleaseCompilerManifest("antiphon-release-compiler-inputs-v1", sha,
            Store(File.ReadAllBytes(Path.Combine(sdk, "dotnet.dll"))), HashFile(typeof(CSharpCompilation).Assembly.Location),
            HashFile(typeof(Compilation).Assembly.Location), [context]);
        var expected = Write(manifest);

        // Establish that these independently chosen inputs compile before the production reader sees them.
        var parse = new CSharpParseOptions(LanguageVersion.CSharp12, preprocessorSymbols: ["FAST"]);
        var control = CSharpCompilation.Create("T",
            sources.Select(source => CSharpSyntaxTree.ParseText(File.ReadAllText(repo.Full(source.Path)), parse, source.Path))
                .Append(CSharpSyntaxTree.ParseText(Encoding.UTF8.GetString(generatorOutput), parse, generated)),
            [MetadataReference.CreateFromImage(reference, new MetadataReferenceProperties(aliases: [..aliases]))],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release, deterministic: true));
        control.GetDiagnostics(cancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty("I.fixture");

        using var graph = await GraphAsync("on", manifestPath, expected);
        graph.RootElement.GetProperty("proofEligible").GetBoolean().ShouldBeTrue("I.bound");
        graph.RootElement.GetProperty("manifestDigest").GetString().ShouldBe(expected, "I.bound manifest");
        var binding = ReleaseCompilerInputs.Bind(sha, gitBlobs, manifestPath, expected);
        var compiled = binding.Contexts.Single().Compilation;
        var useTree = compiled.SyntaxTrees.Single(tree => tree.FilePath == use);
        var occurrence = useTree.GetRoot(cancellationToken).DescendantNodes().OfType<TypeOfExpressionSyntax>().Single().Type;
        var symbol = (INamedTypeSymbol)compiled.GetSemanticModel(useTree).GetSymbolInfo(occurrence, cancellationToken).Symbol!;
        symbol.ToDisplayString().ShouldBe("North.Probe", "I.bound symbol");
        symbol.DeclaringSyntaxReferences.Select(part => part.SyntaxTree.FilePath).ShouldBe([north], "I.bound declaration");
        using var off = await GraphAsync("off");
        off.RootElement.GetProperty("schema").GetString().ShouldBe("antiphon-release-graph-artifact-v1", "I.off");
        off.RootElement.TryGetProperty("proofEligible", out _).ShouldBeFalse("I.off");

        string Store(byte[] bytes)
        {
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            File.WriteAllBytes(Path.Combine(authority, hash), bytes);
            return hash;
        }

        static string HashFile(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

        string Write(ReleaseCompilerManifest value)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            File.WriteAllBytes(manifestPath, bytes);
            return Convert.ToHexStringLower(SHA256.HashData(bytes));
        }

        async Task<JsonDocument> GraphAsync(string name, string? inputs = null, string? digest = null)
        {
            var output = repo.Full(outputRoot + "/" + name + ".json");
            var driver = Path.Combine(AppContext.BaseDirectory, "derived-impact-index", "Antiphon.DerivedImpactIndex.dll");
            var start = new ProcessStartInfo("dotnet", [driver, "--release-graph", repo.Root, sha, output])
            {
                WorkingDirectory = repo.Root, RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false,
            };
            if (inputs is not null)
            {
                start.ArgumentList.Add("--proven-subtraction");
                start.ArgumentList.Add(inputs);
                start.ArgumentList.Add(digest!);
            }
            var clock = Stopwatch.StartNew();
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync(cancellationToken);
                await Task.WhenAll(stdout, stderr).WaitAsync(cancellationToken);
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }
                Console.WriteLine($"C2076 compiler-input graph case={name} elapsedMs={clock.ElapsedMilliseconds}");
            }
            process.ExitCode.ShouldBe(0, await stdout + await stderr);
            return JsonDocument.Parse(await File.ReadAllBytesAsync(output, cancellationToken));
        }
    }

    [Test]
    public async Task EvaluatedItemsRespectConditionsUpdateAndRemove()
    {
        await using var repo = await Fixture();
        var result = await Run(repo);
        result.ExitCode.ShouldBe(0, result.Text);
        using var json = Receipt(result.StandardOutput);
        var app = json.RootElement.GetProperty("projects").EnumerateArray()
            .Single(p => p.GetProperty("path").GetString() == "App/App.csproj");
        var items = app.GetProperty("items").EnumerateArray().ToArray();
        items.Where(i => i.GetProperty("kind").GetString() == "Compile").Select(i => i.GetProperty("path").GetString())
            .ShouldBe(new[] { "App/Code.cs" });
        items.Single(i => i.GetProperty("kind").GetString() == "Content").GetProperty("link").GetString().ShouldBe("settings.json");
        app.GetProperty("references").EnumerateArray().Select(x => x.GetString()).ShouldBe(new[] { "Product/Product.csproj" });
        app.GetProperty("imports").EnumerateArray().Select(x => x.GetProperty("path").GetString()).ShouldContain("shared.props");
        items.Single(i => i.GetProperty("kind").GetString() == "AdditionalFiles").GetProperty("path").GetString().ShouldBe("schema.json");
        var analyzers = items.Where(i => i.GetProperty("kind").GetString() == "Analyzer").ToArray();
        analyzers.ShouldNotBeEmpty("the real SDK contributes analyzer assemblies");
        analyzers.All(i => i.GetProperty("path").GetString()!.StartsWith("sdk:", StringComparison.Ordinal)
            && i.GetProperty("sha256").GetString()!.Length == 64).ShouldBeTrue();
        // Changing the requested framework must change actual evaluated reference membership.
        var alternate = await Run(repo, framework: "net8.0");
        alternate.ExitCode.ShouldBe(0, alternate.Text);
        using var other = Receipt(alternate.StandardOutput);
        other.RootElement.GetProperty("projects").EnumerateArray()
            .Single(p => p.GetProperty("path").GetString() == "App/App.csproj")
            .GetProperty("references").EnumerateArray().Select(x => x.GetString()).ShouldBe(new[] { "Other/Other.csproj" });
        repo.AssertUntouched();
    }

    [Test]
    public async Task ReceiptBindsContentSdkAndTargetProperties()
    {
        await using var repo = await Fixture();
        var result = await Run(repo);
        result.ExitCode.ShouldBe(0, result.Text);
        using var json = Receipt(result.StandardOutput);
        var root = json.RootElement;
        root.GetProperty("sourceSha").GetString().ShouldBe(Sha(repo));
        root.GetProperty("inputManifestDigest").GetString()!.Length.ShouldBe(64);
        root.GetProperty("sdkDigest").GetString()!.Length.ShouldBe(64);
        var target = root.GetProperty("target");
        target.GetProperty("framework").GetString().ShouldBe("net9.0");
        target.GetProperty("configuration").GetString().ShouldBe("Debug");
        target.GetProperty("platform").GetString().ShouldBe("AnyCPU");
        target.GetProperty("runtimeIdentifier").GetString().ShouldBe("");
        root.GetProperty("complete").GetBoolean().ShouldBeFalse();
        root.GetProperty("fallback").GetString().ShouldBe("Full");
        root.GetProperty("incompleteReasons").EnumerateArray().Select(x => x.GetString()).ShouldContain("runtime-closure-unavailable");
        await File.WriteAllTextAsync(repo.Full("unconsumed.txt"), "changed bytes\n");
        using (var git = new LibGit2Sharp.Repository(repo.Root))
        {
            LibGit2Sharp.Commands.Stage(git, "unconsumed.txt");
            var who = new LibGit2Sharp.Signature("fixture", "fixture@antiphon.test", DateTimeOffset.Now);
            git.Commit("input changed", who, who, new LibGit2Sharp.CommitOptions());
        }
        var second = await Run(repo);
        second.ExitCode.ShouldBe(0, second.Text);
        using var changed = Receipt(second.StandardOutput);
        changed.RootElement.GetProperty("inputManifestDigest").GetString().ShouldNotBe(root.GetProperty("inputManifestDigest").GetString());
        changed.RootElement.GetProperty("sourceSha").GetString().ShouldBe(Sha(repo));
    }

    [Test]
    public async Task DirtyOrMismatchedSourceRefuses()
    {
        await using var repo = await Fixture();
        var mismatch = await Run(repo, new string('0', 40));
        mismatch.ExitCode.ShouldBe(1, mismatch.Text);
        mismatch.Text.ShouldContain("source-mismatch");
        await File.WriteAllTextAsync(repo.Full("App/Code.cs"), "class Changed {}\n");
        var dirty = await Run(repo);
        dirty.ExitCode.ShouldBe(1, dirty.Text);
        dirty.Text.ShouldContain("source-drift");
        dirty.StandardOutput.ShouldNotContain("release-inputs-v1");
    }

    [Test]
    public async Task UntrackedEvaluatedInputRefusesAndMissingImportFails()
    {
        await using var repo = await Fixture();
        await File.WriteAllTextAsync(repo.Full("App/Hidden.cs"), "class Hidden {}\n");
        var hidden = await Run(repo);
        hidden.ExitCode.ShouldBe(1, hidden.Text);
        hidden.Text.ShouldContain("unbound-input:App/Hidden.cs");
        File.Delete(repo.Full("App/Hidden.cs"));
        File.Delete(repo.Full("shared.props"));
        var missing = await Run(repo);
        missing.ExitCode.ShouldBe(1, missing.Text);
        missing.Text.ShouldContain("source-drift");
    }

    private static Task<LandGraphEvaluationTests.AuditRepo> Fixture() => LandGraphEvaluationTests.AuditRepo.CreateAsync(new()
    {
        [".gitignore"] = "App/Hidden.cs\n",
        ["App/App.csproj"] = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup>
              <Import Project="../shared.props" />
              <ItemGroup>
                <Compile Remove="Removed.cs" />
                <Content Include="../settings.json" Link="old.json" />
                <Content Update="../settings.json" Link="settings.json" />
                <ProjectReference Include="../Product/Product.csproj" Condition="'$(TargetFramework)' == 'net9.0'" />
                <ProjectReference Include="../Other/Other.csproj" Condition="'$(TargetFramework)' == 'net8.0'" />
              </ItemGroup>
            </Project>
            """,
        ["App/Code.cs"] = "class Code {}\n",
        ["App/Removed.cs"] = "class Removed {}\n",
        ["Product/Product.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>",
        ["Other/Other.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>",
        ["shared.props"] = "<Project><ItemGroup><AdditionalFiles Include=\"../schema.json\" /></ItemGroup></Project>",
        ["schema.json"] = "{}\n",
        ["settings.json"] = "{}\n",
        ["unconsumed.txt"] = "original bytes\n",
    });

    private static string Sha(LandGraphEvaluationTests.AuditRepo repo)
    {
        using var git = new LibGit2Sharp.Repository(repo.Root);
        return git.Head.Tip.Sha;
    }
    private static Task<LandGraphEvaluationTests.ToolRun> Run(LandGraphEvaluationTests.AuditRepo repo, string? sha = null, string framework = "net9.0") =>
        repo.RunToolAsync("release-inputs", "--source", sha ?? Sha(repo), "--framework", framework, "--configuration", "Debug", "--platform", "AnyCPU", "--rid", "");
    private static JsonDocument Receipt(string stdout) => JsonDocument.Parse(stdout.Split('\n').Last(s => s.StartsWith('{')));
}
