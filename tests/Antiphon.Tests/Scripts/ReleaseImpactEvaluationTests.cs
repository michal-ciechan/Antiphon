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
            [".gitignore"] = ".antiphon/selector-replay/\nobj/\n",
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
        var corePath = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var dotnetRoot = Path.GetFullPath(Path.Combine(corePath, "..", "..", ".."));
        var referencePack = LatestVersion(Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref"), 9);
        var reference = File.ReadAllBytes(Path.Combine(referencePack, "ref", "net9.0", "System.Runtime.dll"));
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
        var sdk = LatestVersion(Path.Combine(dotnetRoot, "sdk"), 10);
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
        graph.RootElement.GetProperty("proofEligible").GetBoolean()
            .ShouldBeTrue("I.bound: " + graph.RootElement.GetProperty("reason").GetString());
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
        using var offWithInputs = await GraphAsync("off-with-inputs", manifestPath, expected, enabled: false);
        offWithInputs.RootElement.GetRawText().ShouldBe(off.RootElement.GetRawText(), "I.off with a valid compiler pack");

        var unboundAlias = Store(Encoding.UTF8.GetBytes("global using Chosen = South.Probe;\n"));
        var validButDifferent = manifest with { Contexts = [context with { Sources = [..sources, gen with { Sha256 = unboundAlias }] }] };
        var resealed = Write(validButDifferent);
        var alternative = ReleaseCompilerInputs.Bind(sha, gitBlobs, manifestPath, resealed);
        BoundAlias(alternative).ShouldBe("South.Probe", "I.authority alternative fixture must really bind");
        using var untrustedGraph = await GraphAsync("untrusted", manifestPath, expected);
        untrustedGraph.RootElement.GetProperty("proofEligible").GetBoolean().ShouldBeFalse("I.authority");
        untrustedGraph.RootElement.GetProperty("reason").GetString().ShouldBe("compiler-manifest-digest-mismatch");
        untrustedGraph.RootElement.GetProperty("graph").GetProperty("fallback").GetString().ShouldBe("Full");

        (string Label, ReleaseCompilerManifest Manifest)[] proposals =
        [
            ("generated-receipt", manifest with { Contexts = [context with { Sources = sources }] }),
            ("owner", manifest with { Contexts = [context with { Project = "tests/T/Missing.csproj" }] }),
            ("reference-alias", manifest with { Contexts = [context with { References = [new(referenceDigest, ["global", "Alternative"], false)] }] }),
            ("define", manifest with { Contexts = [context with { Defines = ["OTHER"] }] }),
            ("language", manifest with { Contexts = [context with { LanguageVersion = "13" }] }),
            ("sdk", manifest with { SdkSha256 = unboundAlias }),
            ("compiler", manifest with { CompilerSha256 = unboundAlias }),
            ("rid", manifest with { Contexts = [context with { RuntimeIdentifier = "linux-x64" }] }),
            ("framework", manifest with { Contexts = [context with { Framework = "net10.0" }] }),
            ("os", manifest with { Contexts = [context with { OperatingSystem = "linux" }] }),
            ("architecture", manifest with { Contexts = [context with { Architecture = "arm64" }] }),
            ("configuration", manifest with { Contexts = [context with { Configuration = "Debug" }] }),
            ("platform", manifest with { Contexts = [context with { Platform = "x64" }] }),
        ];
        foreach (var (label, proposal) in proposals)
        {
            Write(proposal).ShouldNotBe(expected, $"I.context {label} fixture");
            Denied(sha, expected, "compiler-manifest-digest-mismatch", $"I.context {label}");
        }

        Write(manifest).ShouldBe(expected);
        var obj = repo.Full("tests/T/obj/Gen.g.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(obj)!);
        await File.WriteAllBytesAsync(obj, generatorOutput, cancellationToken);
        var payload = Path.Combine(authority, gen.Sha256);
        File.Delete(payload);
        Denied(sha, expected, "compiler-generated-source-unbound", "I.generated");
        await File.WriteAllBytesAsync(payload, Encoding.UTF8.GetBytes("global using Chosen = South.Probe;\n"), cancellationToken);
        Denied(sha, expected, "compiler-generated-source-unbound", "I.generated conflicting bytes");
        Store(generatorOutput);
        var referencePath = Path.Combine(authority, referenceDigest);
        await File.WriteAllBytesAsync(referencePath, generatorOutput, cancellationToken);
        Denied(sha, expected, "compiler-reference-unbound", "I.reference substituted bytes");
        Store(reference);

        var auditDirectory = Path.Combine(AppContext.BaseDirectory, "land-graph-audit");
        // The net10.0 evaluator and net9.0 index driver resolve different binaries
        // from the same Roslyn package. Pin the effective compiler for each capture.
        var auditManifest = manifest with
        {
            CompilerSha256 = HashFile(Path.Combine(auditDirectory, "Microsoft.CodeAnalysis.CSharp.dll")),
            CodeAnalysisSha256 = HashFile(Path.Combine(auditDirectory, "Microsoft.CodeAnalysis.dll")),
        };
        string[] evaluation = ["release-inputs", "--source", sha, "--framework", "net9.0",
            "--configuration", "Release", "--platform", "AnyCPU", "--rid", "win-x64"];
        var audit = await repo.RunToolAsync([..evaluation, "--compiler-manifest", manifestPath,
            "--expected-compiler-manifest-sha256", Write(auditManifest)]);
        audit.ExitCode.ShouldBe(0, audit.Text);
        using var auditReceipt = Receipt(audit.StandardOutput);
        auditReceipt.RootElement.GetProperty("schema").GetString().ShouldBe("release-inputs-v2");
        auditReceipt.RootElement.GetProperty("proofEligible").GetBoolean().ShouldBeTrue("I.inputs");
        auditReceipt.RootElement.GetProperty("fallback").GetString().ShouldBe("Full");
        auditReceipt.RootElement.GetProperty("evaluation").GetProperty("sourceSha").GetString().ShouldBe(sha);
        var legacyBytes = Encoding.UTF8.GetBytes(auditReceipt.RootElement.GetProperty("evaluation").GetRawText());
        await File.WriteAllBytesAsync(manifestPath, legacyBytes, cancellationToken);
        ReleaseCompilerInputs.Bind(sha, gitBlobs, manifestPath, Convert.ToHexStringLower(SHA256.HashData(legacyBytes)))
            .Eligible.ShouldBeFalse("I.v1");

        // Use distinct pinned commits and independently pinned manifest bytes. A
        // valid request on either side cannot mask the mismatched twin.
        using var repository = new LibGit2Sharp.Repository(repo.Root);
        var marker = repo.Full("endpoint.txt");
        await File.WriteAllTextAsync(marker, "candidate\n", cancellationToken);
        LibGit2Sharp.Commands.Stage(repository, "endpoint.txt");
        var who = new LibGit2Sharp.Signature("fixture", "fixture@antiphon.test", DateTimeOffset.UnixEpoch);
        var candidate = repository.Commit("candidate", who, who, new LibGit2Sharp.CommitOptions()).Sha;
        Write(manifest).ShouldBe(expected);
        Denied(candidate, expected, "compiler-source-mismatch", "I.context wrong A");
        var candidateDigest = Write(validButDifferent with { SourceSha = candidate });
        BoundAlias(ReleaseCompilerInputs.Bind(candidate, gitBlobs, manifestPath, candidateDigest))
            .ShouldBe("South.Probe", "I.context valid A");
        Denied(sha, candidateDigest, "compiler-source-mismatch", "I.context wrong B");

        void Denied(string endpoint, string pin, string reason, string label)
        {
            var refusal = ReleaseCompilerInputs.Bind(endpoint, gitBlobs, manifestPath, pin);
            refusal.Eligible.ShouldBeFalse(label);
            refusal.Reason.ShouldBe(reason, label);
        }

        string BoundAlias(ReleaseCompilerBinding verified)
        {
            verified.Eligible.ShouldBeTrue(verified.Reason);
            var compilation = verified.Contexts.Single().Compilation;
            var tree = compilation.SyntaxTrees.Single(item => item.FilePath == use);
            var node = tree.GetRoot(cancellationToken).DescendantNodes().OfType<TypeOfExpressionSyntax>().Single().Type;
            return compilation.GetSemanticModel(tree).GetSymbolInfo(node, cancellationToken).Symbol!.ToDisplayString();
        }

        static string LatestVersion(string path, int major) => Directory.EnumerateDirectories(path)
            .Select(directory => (Directory: directory, Version: Version.TryParse(Path.GetFileName(directory), out var version) ? version : null))
            .Where(installed => installed.Version?.Major == major)
            .MaxBy(installed => installed.Version).Directory;

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

        async Task<JsonDocument> GraphAsync(string name, string? inputs = null, string? digest = null, bool enabled = true)
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
                if (enabled) start.ArgumentList.Add("--proven-subtraction");
                start.ArgumentList.Add("--compiler-manifest");
                start.ArgumentList.Add(inputs);
                start.ArgumentList.Add("--expected-compiler-manifest-sha256");
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
