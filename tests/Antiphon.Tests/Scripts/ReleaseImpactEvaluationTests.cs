using System.Text.Json;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReleaseImpactEvaluationTests
{
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
