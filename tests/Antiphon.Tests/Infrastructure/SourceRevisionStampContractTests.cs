using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// Ordinary builds carry a constant SourceRevisionId so a new commit changes no compile input.
/// Only a build whose binary is deployed and compared to a SHA passes -p:SourceRevisionId.
/// </summary>
[Category("Unit")]
public sealed class SourceRevisionStampContractTests
{
    private const string ProbeSha = "0123456789abcdef0123456789abcdef01234567";

    private static readonly Regex DeployInvocation = new(
        @"dotnet\s+(build|pack|publish)\b(?<args>[^\r\n;]*)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    [Test]
    public void Ordinary_builds_never_read_git_for_the_stamp()
    {
        var props = XDocument.Parse(DockerStackDocuments.Read("Directory.Build.props"));
        props.Descendants("Target").ShouldBeEmpty("a props target would run in every project");
        props.Descendants("Exec").ShouldBeEmpty();
        var revision = props.Descendants("SourceRevisionId").ShouldHaveSingleItem();
        revision.Value.ShouldBe("unknown");
        ((string?)revision.Attribute("Condition")).ShouldBe("'$(SourceRevisionId)' == ''");
        // obj/<name>.sourcelink.json carries the HEAD SHA and is a compile input.
        var sourceLink = props.Descendants("EnableSourceLink").ShouldHaveSingleItem();
        sourceLink.Value.ShouldBe("false");
        sourceLink.Attribute("Condition").ShouldBeNull();
        XDocument.Parse(DockerStackDocuments.Read("Directory.Build.targets"))
            .Descendants("SourceRevisionId").ShouldBeEmpty();
    }

    [Test]
    [Arguments("dev-aspire.ps1", "-Label apphost-build -- dotnet build -p:SourceRevisionId=$sourceRevision")]
    [Arguments("scripts/run-daemon.ps1", "$buildArgs += \"-p:SourceRevisionId=$sourceRevision\"")]
    [Arguments("scripts/start-windows-phone-home-runner.ps1", "dotnet build $project -nologo -v:q \"-p:SourceRevisionId=$sourceRevision\"")]
    public void Deployed_builds_stamp_head(string script, string stampedBuild)
    {
        var text = DockerStackDocuments.Read(script);
        text.ShouldContain("rev-parse HEAD");
        text.ShouldContain("match '^[0-9a-f]{40}$'");
        text.ShouldContain(stampedBuild);
    }

    [Test]
    public void Deploy_surfaces_stamp_every_product_build()
    {
        var unstamped = new List<string>();
        foreach (var relative in DeploySurfaces())
        {
            var text = DockerStackDocuments.Read(relative);
            foreach (Match match in DeployInvocation.Matches(text))
            {
                if (match.Index > 0 && match.Value.Length > 0 && text[match.Index - 1] is '\'' or '"' or '`')
                    continue;
                var invocation = match.Value.Trim();
                // EchoGateway restores the already-published packages. It is not an Antiphon deploy binary.
                if (invocation.Contains("samples/EchoGateway", StringComparison.Ordinal))
                    continue;
                if (!invocation.Contains("SourceRevisionId", StringComparison.Ordinal))
                    unstamped.Add(relative + ": " + invocation);
            }

            if (text.Contains("dotnet @", StringComparison.Ordinal)
                && !text.Contains("-p:SourceRevisionId=", StringComparison.Ordinal))
                unstamped.Add(relative + ": splatted dotnet has no -p:SourceRevisionId=");
        }

        unstamped.ShouldBeEmpty("a deploy build without SourceRevisionId ships an unstamped binary");
    }

    [Test]
    public void Restart_apphost_refuses_an_unstamped_server()
    {
        var restart = DockerStackDocuments.Read("scripts/restart-apphost.ps1");
        restart.ShouldContain("dev-aspire.ps1");
        restart.ShouldContain("exit 5");
        restart.ShouldContain("server build unverified");
        var verified = restart.IndexOf("if ($dashUrl -and $backendOk -and $identityVerified)", StringComparison.Ordinal);
        var success = restart.IndexOf("exit 0", verified, StringComparison.Ordinal);
        verified.ShouldBeGreaterThanOrEqualTo(0);
        success.ShouldBeGreaterThan(verified);

        var common = DockerStackDocuments.Read("scripts/apphost-common.ps1");
        common.ShouldContain("function Resolve-AppHostLoadedIdentity");
        common.ShouldContain("Reason = 'malformed'");
        common.ShouldContain("'^[0-9a-fA-F]{40}$'");

        var aspire = DockerStackDocuments.Read("dev-aspire.ps1");
        aspire.ShouldContain("Set-Location '$appHostDir'");
        aspire.ShouldContain("-Label apphost-build -- dotnet build -p:SourceRevisionId=$sourceRevision");
        var appHost = DockerStackDocuments.Read("Antiphon.AppHost/Antiphon.AppHost.csproj");
        appHost.ShouldContain("ProjectReference");
        appHost.ShouldContain("Antiphon.Server.csproj");
    }

    [Test]
    public void Checkpoint_nightly_and_land_builds_are_unstamped()
    {
        var builders = new (string Path, string Token)[]
        {
            ("scripts/lib/nightly-core.ps1", "dotnet build"),
            ("scripts/lib/nightly-tests-impl.ps1", "dotnet build Antiphon.sln"),
            ("scripts/lib/land-audit-tool.ps1", "dotnet build"),
            ("server/Application/Services/AgentTaskLandService.cs", "\"build\", \"--artifacts-path\""),
            ("tools/Antiphon.Checkpoints/Execution/BuildStep.cs", "\"build\", project"),
        };
        foreach (var builder in builders)
        {
            var text = DockerStackDocuments.Read(builder.Path);
            text.ShouldContain(builder.Token, builder.Path);
            text.ShouldNotContain("SourceRevisionId", builder.Path);
        }
    }

    [Test]
    [NotInParallel("source-revision-builds")]
    [Timeout(300_000)]
    public void Two_heads_produce_identical_assembly_inputs()
    {
        var props = DockerStackDocuments.Read("Directory.Build.props");
        var root = Path.Combine(Path.GetTempPath(), "antiphon-sha-stamp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var left = WriteProbeRepo(Path.Combine(root, "left"), props, "left-head");
            var right = WriteProbeRepo(Path.Combine(root, "right"), props, "right-head");
            var leftInfo = BuildProbe(left, sourceRevision: null);
            var rightInfo = BuildProbe(right, sourceRevision: null);
            leftInfo.Entry.ShouldBe(rightInfo.Entry, "two HEADs must not change the entry AssemblyInfo");
            leftInfo.Referenced.ShouldBe(rightInfo.Referenced, "two HEADs must not change the referenced AssemblyInfo");
            leftInfo.Entry.ShouldContain("+unknown");
            leftInfo.Referenced.ShouldContain("+unknown");
            leftInfo.SourceLink.ShouldBe(rightInfo.SourceLink, "sourcelink inputs must not carry HEAD");

            var stamped = BuildProbe(left, ProbeSha);
            stamped.Referenced.ShouldContain("+" + ProbeSha, "a global -p:SourceRevisionId must reach the referenced project");
            stamped.Entry.ShouldContain("+" + ProbeSha);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static IEnumerable<string> DeploySurfaces()
    {
        var root = DockerStackDocuments.RepoRoot;
        yield return "dev-aspire.ps1";
        yield return "Dockerfile";
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "scripts"), "*.ps1"))
        {
            var name = Path.GetFileName(path);
            if (name.StartsWith("restart-", StringComparison.Ordinal)
                || name.StartsWith("deploy-", StringComparison.Ordinal)
                || name.StartsWith("publish-", StringComparison.Ordinal)
                || name.StartsWith("release-", StringComparison.Ordinal)
                || name.StartsWith("promote-", StringComparison.Ordinal)
                || name == "run-daemon.ps1"
                || (name.StartsWith("start-", StringComparison.Ordinal) && name.EndsWith("-runner.ps1", StringComparison.Ordinal)))
                yield return "scripts/" + name;
        }

        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "docker"), "Dockerfile", SearchOption.AllDirectories))
            yield return Path.GetRelativePath(root, path).Replace('\\', '/');

        var workflows = Path.Combine(root, ".github", "workflows");
        if (Directory.Exists(workflows))
        {
            foreach (var path in Directory.EnumerateFiles(workflows, "publish-*.yml"))
                yield return Path.GetRelativePath(root, path).Replace('\\', '/');
        }
    }

    private static string WriteProbeRepo(string directory, string props, string marker)
    {
        Directory.CreateDirectory(Path.Combine(directory, "app"));
        Directory.CreateDirectory(Path.Combine(directory, "svc"));
        File.WriteAllText(Path.Combine(directory, "Directory.Build.props"), props);
        File.WriteAllText(Path.Combine(directory, "marker.txt"), marker);
        File.WriteAllText(Path.Combine(directory, "svc", "Svc.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(directory, "svc", "Class1.cs"), """
            public static class StampProbe { public static int Value => 1; }
            """);
        File.WriteAllText(Path.Combine(directory, "app", "App.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\svc\Svc.csproj" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(directory, "app", "Program.cs"), """
            Console.WriteLine(StampProbe.Value);
            """);
        Run("git", "init", directory).Code.ShouldBe(0);
        Run("git", "add -A", directory).Code.ShouldBe(0);
        var commit = Run("git", "-c commit.gpgsign=false commit -m " + marker, directory);
        commit.Code.ShouldBe(0, commit.Text);
        return directory;
    }

    private static ProbeBuild BuildProbe(string directory, string? sourceRevision)
    {
        var args = "build app/App.csproj -p:UseAppHost=false --nologo -v:q";
        if (sourceRevision is not null)
            args += " -p:SourceRevisionId=" + sourceRevision;
        var built = Run("dotnet", args, directory);
        built.Code.ShouldBe(0, built.Text);
        return new ProbeBuild(
            ReadGenerated(directory, "app", "*.AssemblyInfo.cs"),
            ReadGenerated(directory, "svc", "*.AssemblyInfo.cs"),
            ReadGenerated(directory, "app", "*.sourcelink.json") + ReadGenerated(directory, "svc", "*.sourcelink.json"));
    }

    private static string ReadGenerated(string directory, string project, string pattern)
    {
        var obj = Path.Combine(directory, project, "obj");
        if (!Directory.Exists(obj))
            return "";
        var files = Directory.GetFiles(obj, pattern, SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        if (files.Length == 0)
            return "";
        return string.Join("\n", files.Select(File.ReadAllText));
    }

    private static (int Code, string Text) Run(string file, string args, string cwd)
    {
        var start = new ProcessStartInfo(file, args)
        {
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.Environment["ANTIPHON_LINUX_COMPILER_POLICY"] = "0";
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        start.Environment["NUGET_XMLDOC_MODE"] = "skip";
        start.Environment["GIT_AUTHOR_NAME"] = "stamp-probe";
        start.Environment["GIT_AUTHOR_EMAIL"] = "stamp-probe@antiphon.local";
        start.Environment["GIT_COMMITTER_NAME"] = "stamp-probe";
        start.Environment["GIT_COMMITTER_EMAIL"] = "stamp-probe@antiphon.local";
        using var process = Process.Start(start);
        process.ShouldNotBeNull();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(180_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return (124, "timeout\n" + stdout + stderr);
        }

        var text = stdout + stderr;
        if (text.Length > 4000)
            text = text[^4000..];
        return (process.ExitCode, text);
    }

    private readonly record struct ProbeBuild(string Entry, string Referenced, string SourceLink);
}
