using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1483: the land audit asks real MSBuild. Each test builds a throw-away git repo with the
/// real global.json, a tiny Antiphon.sln and SDK projects, then runs the staged
/// tools/Antiphon.LandGraphAudit (MSBuild API through MSBuildLocator) against it.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed partial class LandGraphEvaluationTests
{
    internal static string NativeLinkTarget(string target, bool windows) =>
        windows ? target.Replace('/', '\\') : target;

    [Test]
    public void Windows_link_targets_use_native_separators()
    {
        NativeLinkTarget("../docs", windows: true).ShouldBe(@"..\docs");
        NativeLinkTarget("../external.props", windows: true).ShouldBe(@"..\external.props");
        NativeLinkTarget("../docs", windows: false).ShouldBe("../docs");
    }

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

    // Review cb067299 D-1: a solution entry of a type the audit does not evaluate is still built by
    // `dotnet build Antiphon.sln`, so it denies (exit 2) instead of approving what it never read.
    [Test]
    [Timeout(300_000)]
    public async Task C1483_S1_UnevaluatedSolutionEntryDenies()
    {
        await using var repo = await AuditRepo.CreateAsync(new()
        {
            ["docs/guide.md"] = "# guide\n",
            ["App/Program.cs"] = "System.Console.WriteLine();\n",
            ["App/App.csproj"] = SdkProject(""),
            ["Other/Program.cs"] = "System.Console.WriteLine();\n",
            ["Other/Other.csproj"] = SdkProject("""
                <ItemGroup>
                  <EmbeddedResource Include="../docs/guide.md" />
                </ItemGroup>
                """),
        }, projectTypes: new() { ["Other/Other.csproj"] = "{11111111-2222-3333-4444-555555555555}" });

        var run = await repo.RunToolAsync();

        run.ExitCode.ShouldBe(2, run.Text);
        run.Lines.ShouldContain(l => l.StartsWith("evaluation-failed: ", StringComparison.Ordinal) && l.Contains("Other"), run.Text);
        run.Lines.ShouldNotContain("approved", run.Text);
        repo.AssertUntouched();
    }

    // Review cb067299 D-2: an item reaching docs/ or the root README through a tracked link refuses;
    // the build reads the link's target, not the lexical path MSBuild reports.
    [Test]
    [Timeout(300_000)]
    public async Task C1483_S1_LinkedSkippableItemRefuses()
    {
        await using var repo = await AuditRepo.CreateAsync(new()
        {
            ["README.md"] = "# root\n",
            ["docs/guide.md"] = "# guide\n",
            ["docs/sub/deep.md"] = "# deep\n",
            ["App/Program.cs"] = "System.Console.WriteLine();\n",
            ["App/App.csproj"] = SdkProject("""
                <ItemGroup>
                  <None Include="link/guide.md" />
                  <AntiphonCustomItem Include="link/../docs/sub/deep.md" />
                  <EmbeddedResource Include="top.md" />
                </ItemGroup>
                """),
        }, links: new() { ["App/link"] = "../docs", ["App/top.md"] = "../README.md" });

        var run = await repo.RunToolAsync();

        run.ExitCode.ShouldBe(1, run.Text);
        run.Lines.ShouldContain($"refused: App/App.csproj None {repo.Full("docs/guide.md")}", run.Text);
        run.Lines.ShouldContain($"refused: App/App.csproj AntiphonCustomItem {repo.Full("docs/sub/deep.md")}", run.Text);
        run.Lines.ShouldContain($"refused: App/App.csproj EmbeddedResource {repo.Full("README.md")}", run.Text);
        // The SDK's default None glob walks through both links too: link/guide.md, link/sub/deep.md, top.md.
        foreach (var target in new[] { "docs/guide.md", "docs/sub/deep.md", "README.md" })
        {
            run.Lines.ShouldContain(l => l.StartsWith("refused: ", StringComparison.Ordinal)
                                         && l.EndsWith($"Microsoft.NET.Sdk.DefaultItems.props None {repo.Full(target)}", StringComparison.Ordinal),
                run.Text);
        }
        run.Lines.Count(l => l.StartsWith("refused: ", StringComparison.Ordinal)
                             && !l.Contains(" glob ", StringComparison.Ordinal)).ShouldBe(6, run.Text);
        run.Lines.ShouldNotContain("approved", run.Text);
        repo.AssertUntouched();
    }

    // Review 8e9f68b4 D-1: on Linux FileInfo.LinkTarget is null when readlink fails (EACCES), the same as
    // for a plain path, so a link inside an unreadable directory must fail the evaluation, never approve.
    [Test]
    [Timeout(300_000)]
    public async Task C1483_S1_UnreadableLinkComponentDenies()
    {
        if (OperatingSystem.IsWindows())
            throw new SkipTestException("CARD-1483: Unix mode 000 has no Windows equivalent");
        await using var repo = await AuditRepo.CreateAsync(new()
        {
            ["docs/guide.md"] = "# guide\n",
            ["App/Program.cs"] = "System.Console.WriteLine();\n",
            ["App/App.csproj"] = SdkProject("""
                <ItemGroup>
                  <EmbeddedResource Include="locked/lk/guide.md" />
                </ItemGroup>
                """),
        }, links: new() { ["App/locked/lk"] = "../../docs" });
        var locked = repo.Full("App/locked");
        var mode = File.GetUnixFileMode(locked);
        File.SetUnixFileMode(locked, UnixFileMode.None);
        try
        {
            // Root (or CAP_DAC_OVERRIDE) reads a mode 000 directory anyway: a successful listing is the probe.
            try
            {
                _ = Directory.GetFileSystemEntries(locked);
                throw new SkipTestException("CARD-1483: this user reads a mode 000 directory (root), so readlink cannot fail");
            }
            catch (UnauthorizedAccessException)
            {
            }

            var run = await repo.RunToolAsync();

            run.ExitCode.ShouldBe(2, run.Text);
            run.Lines.ShouldContain(l => l.StartsWith("evaluation-failed: ", StringComparison.Ordinal)
                                         && l.Contains(Path.Combine(locked, "lk"), StringComparison.Ordinal), run.Text);
            run.Lines.ShouldNotContain("approved", run.Text);
        }
        finally
        {
            File.SetUnixFileMode(locked, mode);
        }

        repo.AssertUntouched();
    }

    // V-5: future documentation is unsafe even when a glob matches no files today; excludes
    // do not prove reachability safe. JSON-only and project-local source globs remain allowed.
    [Test]
    [Timeout(300_000)]
    public async Task C1483_S2_GlobReachabilityRefuses()
    {
        foreach (var (pattern, exclude, expected) in new[]
                 {
                     ("../docs/new/*.md", "", 1),
                     ("../docs/**/*.json", "", 0),
                     ("../*", "", 1),
                     ("../docs/**/*.m?", "", 1),
                     ("../docs/**/guide", "", 1),
                     ("../docs/**/*.MD", "../docs/**", 1),
                     ("../**/guide.md", "", 1),
                     ("Sub/*.txt;../docs/new/*.md", "", 1),
                     ("Sub/**/*.cs", "", 0),
                     ("../docs-sibling/*.md", "", 0),
                 })
        {
            await using var repo = await AuditRepo.CreateAsync(new()
            {
                ["App/App.csproj"] = SdkProject($"""
                    <ItemGroup><None Include="{pattern}" Exclude="{exclude}" /></ItemGroup>
                    """),
            });
            var run = await repo.RunToolAsync();
            run.ExitCode.ShouldBe(expected, $"{pattern}: {run.Text}");
            if (expected == 1)
            {
                run.Lines.ShouldContain(l => l.StartsWith("refused: App/App.csproj glob ", StringComparison.Ordinal)
                                             && l.Contains(pattern, StringComparison.Ordinal), run.Text);
                run.Lines.ShouldNotContain("approved", run.Text);
            }
            else
                run.Lines.ShouldContain("approved", run.Text);
            repo.AssertUntouched();
        }
    }

    // V-6: only the solution and its evaluated references belong to the land build. Reference
    // cycles terminate, and a conditionally absent reference must not pull in a docs project.
    [Test]
    [Timeout(300_000)]
    public async Task C1483_S2_SolutionClosureScopesProjects()
    {
        foreach (var reference in new[] { "none", "false-condition", "transitive-cycle" })
        {
            await using var repo = await AuditRepo.CreateAsync(new()
            {
                ["docs/guide.md"] = "# guide\n",
                ["docs/Hidden.csproj"] = SdkProject(""),
                ["App/App.csproj"] = SdkProject(reference == "none" ? "" : $"""
                    <ItemGroup><ProjectReference Include="../Library/Library.csproj" Condition="'{reference}' != 'false-condition'" /></ItemGroup>
                    """),
                ["Library/Library.csproj"] = SdkProject("""
                    <ItemGroup>
                      <ProjectReference Include="../docs/Hidden.csproj" />
                      <ProjectReference Include="../App/App.csproj" />
                    </ItemGroup>
                    """),
            }, solutionProjects: ["App/App.csproj"]);
            var run = await repo.RunToolAsync();
            if (reference == "transitive-cycle")
            {
                run.ExitCode.ShouldBe(1, run.Text);
                run.Text.ShouldContain(repo.Full("docs/guide.md"), Case.Sensitive, run.Text);
                run.Lines.ShouldNotContain("not-built: docs/Hidden.csproj", run.Text);
                run.Lines.ShouldNotContain("not-built: Library/Library.csproj", run.Text);
                run.Lines.ShouldNotContain("approved", run.Text);
            }
            else
            {
                run.ExitCode.ShouldBe(0, run.Text);
                run.Lines.ShouldContain("not-built: docs/Hidden.csproj", run.Text);
                run.Lines.ShouldContain("not-built: Library/Library.csproj", run.Text);
                run.Lines.ShouldNotContain("not-built: App/App.csproj", run.Text);
                run.Lines.ShouldContain("approved", run.Text);
            }
            repo.AssertUntouched();
        }
    }

    // V-7: actual resolved imports must be trusted definitions. A wildcard remains unsafe even
    // when its only current match is tracked; a linked tracked props cannot hide an untracked import.
    [Test]
    [Timeout(300_000)]
    public async Task C1483_S2_ImportOutsideTrackedSetRefuses()
    {
        foreach (var kind in new[] { "untracked", "docs", "wildcard", "wildcard-empty", "non-definition", "tracked", "duplicate", "linked" })
        {
            var path = kind switch
            {
                "docs" => "docs/import.md",
                "non-definition" => "build/import.xml",
                _ => "build/import.props",
            };
            var import = kind.StartsWith("wildcard", StringComparison.Ordinal) ? "build/*.props" : path;
            var files = new Dictionary<string, string>
            {
                ["App/App.csproj"] = SdkProject($"""
                    <Import Project="../{import}" />
                    {(kind == "duplicate" ? $"<Import Project=\"../{import}\" />" : "")}
                    """),
            };
            if (kind is not ("untracked" or "linked" or "wildcard-empty")) files[path] = "<Project />";
            await using var repo = await AuditRepo.CreateAsync(files,
                links: kind == "linked" ? new() { [path] = "../external.props" } : null,
                untrackedFiles: kind is "untracked" or "linked"
                    ? new() { [kind == "linked" ? "external.props" : path] = "<Project />" } : null);
            var run = await repo.RunToolAsync();
            var allowed = kind is "tracked" or "duplicate";
            run.ExitCode.ShouldBe(allowed ? 0 : 1, $"{kind}: {run.Text}");
            if (allowed)
                run.Lines.ShouldContain("approved", run.Text);
            else
            {
                run.Lines.ShouldContain(l => l.StartsWith("refused: App/App.csproj import ", StringComparison.Ordinal)
                                             && l.Contains(import, StringComparison.Ordinal), $"{kind}: {run.Text}");
                run.Lines.ShouldNotContain("approved", run.Text);
            }
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
        public string PathText => Text.Replace('\\', '/');

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

        /// <param name="projectTypes">Solution type GUID per project (default the C# GUID).</param>
        /// <param name="links">Link path to relative target, created as symbolic links (a junction on a Windows host that refuses them) and committed.</param>
        public static async Task<AuditRepo> CreateAsync(Dictionary<string, string> files,
            Dictionary<string, string>? projectTypes = null, Dictionary<string, string>? links = null,
            string[]? solutionProjects = null, Dictionary<string, string>? untrackedFiles = null)
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

            foreach (var (relative, target) in links ?? [])
                await CreateLinkAsync(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)), target);

            await File.WriteAllTextAsync(Path.Combine(root, "Antiphon.sln"),
                Solution(solutionProjects ?? files.Keys.Where(k => k.EndsWith(".csproj", StringComparison.Ordinal)).ToArray(), projectTypes ?? []));
            await GitAsync(root, "init", "-q");
            await GitAsync(root, "add", "-A");
            await GitAsync(root, "-c", "user.name=c1483", "-c", "user.email=c1483@antiphon.test",
                "commit", "-q", "-m", "fixture");
            foreach (var (relative, content) in untrackedFiles ?? [])
            {
                var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, content);
            }
            return new AuditRepo(root);
        }

        public async Task<ToolRun> RunToolAsync(params string[] extra)
            => await RunToolAtAsync(Root, extra);

        internal static async Task<ToolRun> RunToolAtAsync(string root, params string[] extra)
            => await RunToolWithEnvironmentAtAsync(root, null, extra);

        public Task<ToolRun> RunToolWithEnvironmentAsync(Dictionary<string, string?> environment)
            => RunToolWithEnvironmentAtAsync(Root, environment);

        private static async Task<ToolRun> RunToolWithEnvironmentAtAsync(string root,
            Dictionary<string, string?>? environment, params string[] extra)
        {
            var dll = ToolDll();
            File.Exists(dll).ShouldBeTrue($"the land graph audit tool must be staged at {dll}");
            var psi = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            // The test host may run under `dotnet run`; its MSBuild variables must not steer the tool.
            foreach (var key in psi.Environment.Keys.Where(k => k.StartsWith("MSBuild", StringComparison.OrdinalIgnoreCase)).ToArray())
                psi.Environment.Remove(key);
            foreach (var (key, value) in environment ?? [])
            {
                if (value is null) psi.Environment.Remove(key);
                else psi.Environment[key] = value;
            }
            psi.ArgumentList.Add(dll);
            foreach (var arg in extra)
                psi.ArgumentList.Add(arg);
            psi.ArgumentList.Add("--repo");
            psi.ArgumentList.Add(root);
            psi.ArgumentList.Add("--solution");
            psi.ArgumentList.Add("Antiphon.sln");

            var clock = Stopwatch.StartNew();
            using var process = Process.Start(psi)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            // Above the Windows two-pass ceiling (120s + 120s + copy + startup).
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(360));
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }

            var text = await stdout + await stderr;
            Console.WriteLine($"LAND_GRAPH_EVALUATION repo={root} elapsedMs={clock.ElapsedMilliseconds} exit={process.ExitCode}");
            return new ToolRun(process.ExitCode, text);
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

        private static async Task CreateLinkAsync(string path, string target)
        {
            target = NativeLinkTarget(target, OperatingSystem.IsWindows());
            var parent = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(parent);
            var isDirectory = Directory.Exists(Path.Combine(parent, target));
            try
            {
                if (isDirectory)
                    Directory.CreateSymbolicLink(path, target);
                else
                    File.CreateSymbolicLink(path, target);
            }
            catch (Exception ex) when (OperatingSystem.IsWindows() && isDirectory && ex is IOException or UnauthorizedAccessException)
            {
                // No symlink privilege: a junction (absolute target) is the same link class to the audit.
                var psi = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var arg in new[] { "/c", "mklink", "/J", path, Path.GetFullPath(Path.Combine(parent, target)) })
                    psi.ArgumentList.Add(arg);
                using var process = Process.Start(psi)!;
                await process.WaitForExitAsync();
                process.ExitCode.ShouldBe(0, $"mklink /J {path}: {await process.StandardError.ReadToEndAsync()}");
            }

            new FileInfo(path).LinkTarget.ShouldNotBeNull($"{path} must be a link");
        }

        private static string Solution(IEnumerable<string> projects, Dictionary<string, string> projectTypes)
        {
            var sb = new StringBuilder();
            sb.Append("\r\nMicrosoft Visual Studio Solution File, Format Version 12.00\r\n# Visual Studio Version 17\r\n");
            var ids = new List<string>();
            foreach (var project in projects)
            {
                var id = "{" + Guid.NewGuid().ToString().ToUpperInvariant() + "}";
                ids.Add(id);
                var type = projectTypes.GetValueOrDefault(project, "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}");
                sb.Append($"Project(\"{type}\") = \"{Path.GetFileNameWithoutExtension(project)}\", \"{project.Replace('/', '\\')}\", \"{id}\"\r\nEndProject\r\n");
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

        internal static async Task GitAsync(string cwd, params string[] args)
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
