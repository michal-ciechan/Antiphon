using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class LandGraphEvaluationTests
{
    [Test]
    [Arguments("_SourceItemsToCopyToOutputDirectory")]
    [Arguments("_SourceItemsToCopyToOutputDirectoryAlways")]
    [Arguments("_SourceItemsToCopyToOutputDirectoryIfDifferent")]
    [Arguments("AllItemsFullPathWithTargetPath")]
    [Timeout(300_000)]
    public async Task Fdd9519d_SdkItemsAdmitOnlyReviewedRemoval(string item)
    {
        const string owner = "tests/Shared/FakeProviderOutput.targets";
        var condition = $"'%({item}.TargetPath)' == 'fakeclaude' or '%({item}.TargetPath)' == 'fakegrok'";
        var removal = $"<{item} Remove=\"@({item})\" Condition=\"{condition}\" />";
        foreach (var variant in new[] { "exact", "include", "remove", "condition", "metadata", "owner", "target", "evaluation", "output" })
        {
            var element = variant switch
            {
                "include" => removal.Replace("Remove=", "Include="),
                "remove" => removal.Replace($"@({item})", "**/*"),
                "condition" => removal.Replace("fakeclaude", "unreviewed"),
                "metadata" => removal.Replace(" />", " CopyToOutputDirectory=\"Always\" />"),
                _ => removal,
            };
            var target = variant == "target" ? "Unreviewed" : "ExcludeRootFakeProviderAppHosts";
            var body = variant == "evaluation" ? $"<ItemGroup>{element}</ItemGroup>"
                : variant == "output" ? $"<Target Name=\"{target}\"><MSBuild Projects=\"App.csproj\"><Output TaskParameter=\"TargetOutputs\" ItemName=\"{item}\" /></MSBuild></Target>"
                : $"<Target Name=\"{target}\"><ItemGroup>{element}</ItemGroup></Target>";
            await using var repo = await ScriptRepoAsync(new()
            {
                ["App/App.csproj"] = SdkProject(""),
                [variant == "owner" ? "other/Unreviewed.targets" : owner] = $"<Project>{body}</Project>",
            });
            var manifest = repo.Full("precheck-manifest.txt");
            var run = await RunScriptAsync(repo.Root, "-Update", "audit-land-verification-precheck.ps1", "-ManifestPath", manifest);
            run.ExitCode.ShouldBe(variant == "exact" ? 0 : 1, $"{item}/{variant}: {run.Text}");
            File.Exists(manifest).ShouldBe(variant == "exact", run.Text);
            if (variant != "exact") run.Text.ShouldContain(item);
        }
    }
}
