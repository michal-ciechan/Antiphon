using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1494: the crash-worker root never lives in the build output, so a checkpoint run's sealed
/// <c>bin-&lt;x&gt;/</c> cleanup has nothing of the child's to refuse, and the fixture deletes it.
/// </summary>
[Category("Unit")]
public class CheckCompactionFixtureTests
{
    [Test]
    public async Task CheckCompactionFixture_root_is_outside_the_build_output()
    {
        string path;
        await using (var root = CheckCompactionFixture.CreateRoot())
        {
            path = root.FullPath;
            Directory.Exists(path).ShouldBeTrue();
            IsUnder(path, AppContext.BaseDirectory).ShouldBeFalse($"{path} is under the build output {AppContext.BaseDirectory}");
            IsUnder(path, Environment.CurrentDirectory).ShouldBeFalse($"{path} is under the working directory {Environment.CurrentDirectory}");
        }

        Directory.Exists(path).ShouldBeFalse($"{path} outlived its test");
    }

    private static bool IsUnder(string path, string directory)
    {
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
