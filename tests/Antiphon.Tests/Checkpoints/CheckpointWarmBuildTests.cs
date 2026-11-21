using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class CheckpointWarmBuildTests : CheckpointTestBase
{
    [Test]
    public void intermediate_cache_is_shared_by_matching_builds_and_survives_bin_cleanup()
    {
        var root = InitGitRepo();
        const string project = "App/App.csproj";
        Directory.CreateDirectory(Path.Combine(root, "App"));

        (string Owned, string Cache) Plan(string name, string useAppHost, string entry = project)
        {
            var properties = BuildStep.PropertyArguments(
                new Dictionary<string, string> { ["UseAppHost"] = useAppHost }, OperatingSystem.IsWindows()).ToList();
            var layout = BuildStep.AppendSharedLayout(properties, root, entry, name);
            var arguments = BuildStep.BuildArguments(entry, name, properties, 1);
            const string cacheProperty = "--property:AntiphonCheckpointIntermediateRoot=";
            var cache = arguments.SingleOrDefault(arg => arg.StartsWith(cacheProperty, StringComparison.Ordinal));
            cache.ShouldNotBeNull("build arguments must select a persistent intermediate root");
            return (layout.Owned.TrimEnd('/'), cache![cacheProperty.Length..]);
        }

        var first = Plan("bin-warm-first/", "false");
        var second = Plan("bin-warm-second/", "false");
        var changed = Plan("bin-warm-second/", "true");
        var otherProject = Plan("bin-warm-second/", "false", "App/Other.csproj");
        Path.GetRelativePath(root, first.Cache).Replace('\\', '/').ShouldStartWith(".antiphon/checkpoint-obj/obj/");
        first.Cache.ShouldBe(second.Cache);
        first.Cache.ShouldNotBe(changed.Cache);
        first.Cache.ShouldNotBe(otherProject.Cache);

        Directory.CreateDirectory(first.Cache);
        var assets = Path.Combine(first.Cache, "project.assets.json");
        File.WriteAllText(assets, "{}");
        SafeWrite.Require(SafeWrite.CreateFreshDirectory(first.Owned, SafeWrite.Under(root)));
        var binary = Path.Combine(first.Owned, "App.dll");
        SafeWrite.Require(SafeWrite.Text(binary, "build output", SafeWrite.Under(first.Owned)));
        SafeWrite.Require(OwnedOutputLedger.Seal(first.Owned));

        var cleanup = OutputCleanup.CleanOwnedOutputs(root, ["bin-warm-first"], 0, false, false,
            createdPaths: [first.Owned]);
        cleanup.Refusals.ShouldBeEmpty();
        cleanup.Deleted.ShouldContain(first.Owned);
        Directory.Exists(first.Owned).ShouldBeFalse();
        File.ReadAllText(assets).ShouldBe("{}");
    }
}
