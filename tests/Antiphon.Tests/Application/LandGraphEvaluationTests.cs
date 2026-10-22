using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1483: the land audit asks real MSBuild. Each test builds a throw-away git repo with the
/// real global.json, a tiny Antiphon.sln and SDK projects, then runs the staged
/// tools/Antiphon.LandGraphAudit (MSBuild API through MSBuildLocator) against it.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LandGraphEvaluationTests
{
    // V-4: the plan's D-1 members exist on the SDK pinned by global.json, before anything relies on them.
    [Test]
    [Timeout(300_000)]
    public async Task C1483_S1_ApiSurfaceAvailable()
    {
        await using var repo = await AuditRepo.CreateAsync(new()
        {
            ["App/App.csproj"] = SdkProject(""),
        });

        var run = await repo.RunToolAsync("--api-probe");

        run.ExitCode.ShouldBe(0, run.Text);
        foreach (var member in new[]
                 {
                     "Microsoft.Build.Evaluation.Project.GetAllGlobs()",
                     "Microsoft.Build.Evaluation.Project.Imports",
                     "Microsoft.Build.Evaluation.Project.ImportsIncludingDuplicates",
                     "Microsoft.Build.Evaluation.ProjectProperty.IsEnvironmentProperty",
                     "Microsoft.Build.Evaluation.GlobResult.MsBuildGlob",
                     "Microsoft.Build.Globbing.MSBuildGlob.FixedDirectoryPart",
                     "Microsoft.Build.Globbing.MSBuildGlob.WildcardDirectoryPart",
                     "Microsoft.Build.Globbing.MSBuildGlob.FilenamePart",
                     "Microsoft.Build.FileSystem.MSBuildFileSystemBase",
                     "Microsoft.Build.Evaluation.Context.EvaluationContext.Create(SharingPolicy, MSBuildFileSystemBase)",
                     "Microsoft.Build.Definition.ProjectOptions.EvaluationContext",
                 })
        {
            run.Lines.ShouldContain($"api: {member} ok", run.Text);
        }
        run.Text.ShouldNotContain("api-missing:");

        // The MSBuild that answered is the SDK global.json resolves to, not a NuGet copy beside the tool.
        var pinned = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "global.json")))
            .RootElement.GetProperty("sdk").GetProperty("version").GetString()!;
        var majorMinor = string.Join('.', pinned.Split('.').Take(2));
        var sdkLine = run.Lines.Single(l => l.StartsWith("sdk: ", StringComparison.Ordinal));
        sdkLine.ShouldStartWith($"sdk: {majorMinor}.", Case.Sensitive, run.Text);
        var msbuildLine = run.Lines.Single(l => l.StartsWith("msbuild: ", StringComparison.Ordinal));
        var msbuildDir = Path.GetDirectoryName(msbuildLine["msbuild: ".Length..])!;
        Path.GetFileName(msbuildDir).ShouldStartWith($"{majorMinor}.", Case.Sensitive, run.Text);
        msbuildDir.ShouldNotStartWith(Path.GetDirectoryName(ToolDll())!, Case.Sensitive, run.Text);
        repo.AssertUntouched();
    }

    // V-1: evaluated items of any item type whose FullPath is docs/**/*.md or the root README refuse by name.
    [Test]
    [Timeout(300_000)]
    public async Task C1483_S1_EvaluatedSkippableItemRefuses()
    {
        await using var repo = await AuditRepo.CreateAsync(new()
        {
            ["README.md"] = "# root\n",
            ["docs/guide.md"] = "# guide\n",
            ["docs/sub/deep.md"] = "# deep\n",
            ["App/Program.cs"] = "System.Console.WriteLine();\n",
            ["App/App.csproj"] = SdkProject("""
                <ItemGroup>
                  <None Include="../docs/guide.md" />
                  <EmbeddedResource Include="../README.md" />
                  <AntiphonCustomItem Include="..\docs\sub\.\deep.md" />
                </ItemGroup>
                """),
        });

        var run = await repo.RunToolAsync();

        run.ExitCode.ShouldBe(1, run.Text);
        run.Lines.ShouldContain($"refused: App/App.csproj None {repo.Full("docs/guide.md")}", run.Text);
        run.Lines.ShouldContain($"refused: App/App.csproj EmbeddedResource {repo.Full("README.md")}", run.Text);
        run.Lines.ShouldContain($"refused: App/App.csproj AntiphonCustomItem {repo.Full("docs/sub/deep.md")}", run.Text);
        run.Lines.Count(l => l.StartsWith("refused: ", StringComparison.Ordinal)).ShouldBe(3, run.Text);
        run.Lines.ShouldNotContain("approved", run.Text);
        repo.AssertUntouched();
    }

    // V-2: a fixture with only harmless inputs, including a non-markdown docs file, approves.
    [Test]
    [Timeout(300_000)]
    public async Task C1483_S1_HarmlessFixtureApproves()
    {
        await using var repo = await AuditRepo.CreateAsync(new()
        {
            ["README.md"] = "# root\n",
            ["docs/x.json"] = "{}\n",
            ["docs/notes.md"] = "# not referenced\n",
            ["App/Program.cs"] = "System.Console.WriteLine();\n",
            ["App/Sub/Helper.cs"] = "class Helper { }\n",
            ["App/App.csproj"] = SdkProject("""
                <ItemGroup>
                  <Compile Include="**/*.cs" Exclude="@(Compile)" />
                  <EmbeddedResource Include="../docs/x.json" />
                </ItemGroup>
                """),
        });

        var run = await repo.RunToolAsync();

        run.ExitCode.ShouldBe(0, run.Text);
        run.Lines.ShouldContain("approved", run.Text);
        run.Text.ShouldNotContain("refused:");
        run.Text.ShouldNotContain("evaluation-failed:");
        repo.AssertUntouched();
    }

    // V-3: anything MSBuild cannot evaluate denies with exit 2 and never approves.
    [Test]
    [Timeout(300_000)]
    public async Task C1483_S1_EvaluationFailureDenies()
    {
        var failures = new Dictionary<string, string>
        {
            ["missing-import"] = SdkProject("""<Import Project="../build/does-not-exist.props" />"""),
            ["bad-sdk"] = """
                <Project Sdk="Antiphon.C1483.NoSuch.Sdk">
                  <PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup>
                </Project>
                """,
            ["malformed-xml"] = """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net9.0</TargetFramework></Project>""",
        };
        foreach (var (failure, project) in failures)
        {
            await using var repo = await AuditRepo.CreateAsync(new()
            {
                ["App/Program.cs"] = "System.Console.WriteLine();\n",
                ["App/App.csproj"] = project,
            });

            var run = await repo.RunToolAsync();

            run.ExitCode.ShouldBe(2, $"{failure}: {run.Text}");
            run.Lines.ShouldContain(l => l.StartsWith("evaluation-failed: ", StringComparison.Ordinal), $"{failure}: {run.Text}");
            run.Lines.ShouldNotContain("approved", $"{failure}: {run.Text}");
            repo.AssertUntouched();
        }
    }

    private static string SdkProject(string body) => $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net9.0</TargetFramework>
          </PropertyGroup>
          {body}
        </Project>
        """;

    internal static string ToolDll() =>
        Path.Combine(AppContext.BaseDirectory, "land-graph-audit", "Antiphon.LandGraphAudit.dll");

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
                return dir.FullName;
        }

        throw new InvalidOperationException("repo root was not found");
    }

    internal sealed record ToolRun(int ExitCode, string Text)
    {
        public IReadOnlyList<string> Lines { get; } =
            Text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToArray();
    }

    /// <summary>A temp git repo: real global.json, an Antiphon.sln naming every *.csproj, the given files.</summary>
    internal sealed class AuditRepo : IAsyncDisposable
    {
        private readonly string[] _baseline;

        private AuditRepo(string root)
        {
            Root = root;
            _baseline = Snapshot();
        }

        public string Root { get; }

        public string Full(string relative) =>
            Path.GetFullPath(Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar)));

        public static async Task<AuditRepo> CreateAsync(Dictionary<string, string> files)
        {
            var root = Path.Combine(Path.GetTempPath(), "c1483-" + Guid.NewGuid().ToString("N")[..12]);
            Directory.CreateDirectory(root);
            File.Copy(Path.Combine(RepoRoot(), "global.json"), Path.Combine(root, "global.json"));
            foreach (var (relative, content) in files)
            {
                var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, content);
            }

            await File.WriteAllTextAsync(Path.Combine(root, "Antiphon.sln"),
                Solution(files.Keys.Where(k => k.EndsWith(".csproj", StringComparison.Ordinal))));
            await GitAsync(root, "init", "-q");
            await GitAsync(root, "add", "-A");
            await GitAsync(root, "-c", "user.name=c1483", "-c", "user.email=c1483@antiphon.test",
                "commit", "-q", "-m", "fixture");
            return new AuditRepo(root);
        }

        public async Task<ToolRun> RunToolAsync(params string[] extra)
        {
            var dll = ToolDll();
            File.Exists(dll).ShouldBeTrue($"the land graph audit tool must be staged at {dll}");
            var psi = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = Root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            // The test host may run under `dotnet run`; its MSBuild variables must not steer the tool.
            foreach (var key in psi.Environment.Keys.Where(k => k.StartsWith("MSBuild", StringComparison.OrdinalIgnoreCase)).ToArray())
                psi.Environment.Remove(key);
            psi.ArgumentList.Add(dll);
            foreach (var arg in extra)
                psi.ArgumentList.Add(arg);
            psi.ArgumentList.Add("--repo");
            psi.ArgumentList.Add(Root);
            psi.ArgumentList.Add("--solution");
            psi.ArgumentList.Add("Antiphon.sln");

            using var process = Process.Start(psi)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(240));
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }

            return new ToolRun(process.ExitCode, await stdout + await stderr);
        }

        /// <summary>The tool writes nothing into the repo: same files, and no obj directory anywhere.</summary>
        public void AssertUntouched()
        {
            Snapshot().ShouldBe(_baseline, "the audit must not write into the repository");
            Directory.EnumerateDirectories(Root, "obj", SearchOption.AllDirectories)
                .Where(d => !IsGit(d)).ShouldBeEmpty("the audit must not create obj directories");
        }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(Root))
                StartRefGit.DeleteDirectory(Root);
            return ValueTask.CompletedTask;
        }

        private string[] Snapshot() =>
            Directory.EnumerateFileSystemEntries(Root, "*", SearchOption.AllDirectories)
                .Where(p => !IsGit(p))
                .Select(p => Path.GetRelativePath(Root, p) + (Directory.Exists(p) ? "/" : $" {new FileInfo(p).Length}"))
                .Order(StringComparer.Ordinal)
                .ToArray();

        private bool IsGit(string path)
        {
            var relative = Path.GetRelativePath(Root, path);
            return relative == ".git" || relative.StartsWith(".git" + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        }

        private static string Solution(IEnumerable<string> projects)
        {
            var sb = new StringBuilder();
            sb.Append("\r\nMicrosoft Visual Studio Solution File, Format Version 12.00\r\n# Visual Studio Version 17\r\n");
            var ids = new List<string>();
            foreach (var project in projects)
            {
                var id = "{" + Guid.NewGuid().ToString().ToUpperInvariant() + "}";
                ids.Add(id);
                sb.Append($"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"{Path.GetFileNameWithoutExtension(project)}\", \"{project.Replace('/', '\\')}\", \"{id}\"\r\nEndProject\r\n");
            }

            sb.Append("Global\r\n\tGlobalSection(SolutionConfigurationPlatforms) = preSolution\r\n\t\tDebug|Any CPU = Debug|Any CPU\r\n\tEndGlobalSection\r\n");
            sb.Append("\tGlobalSection(ProjectConfigurationPlatforms) = postSolution\r\n");
            foreach (var id in ids)
            {
                sb.Append($"\t\t{id}.Debug|Any CPU.ActiveCfg = Debug|Any CPU\r\n\t\t{id}.Debug|Any CPU.Build.0 = Debug|Any CPU\r\n");
            }

            sb.Append("\tEndGlobalSection\r\nEndGlobal\r\n");
            return sb.ToString();
        }

        private static async Task GitAsync(string cwd, params string[] args)
        {
            var psi = new ProcessStartInfo("git")
            {
                WorkingDirectory = cwd,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var arg in args)
                psi.ArgumentList.Add(arg);
            using var process = Process.Start(psi)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            process.ExitCode.ShouldBe(0, $"git {string.Join(' ', args)}: {await output}{await error}");
        }
    }
}
