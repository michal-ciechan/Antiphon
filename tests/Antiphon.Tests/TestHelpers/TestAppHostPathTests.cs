using Antiphon.TestSupport;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

[Category("Unit")]
public sealed class TestAppHostPathTests
{
    [Test]
    public void Windows_path_has_exe_suffix()
    {
        var path = TestAppHostPath.Resolve("fakegrok", Path.GetTempPath(), windows: true, siblingProducer: true);
        path.ShouldEndWith("fakegrok.exe");
    }

    [Test]
    public void Linux_path_has_no_exe_suffix()
    {
        var path = TestAppHostPath.Resolve("fakegrok", Path.GetTempPath(), windows: false, siblingProducer: true);
        Path.GetFileName(path).ShouldBe("fakegrok");
    }

    [Test]
    public void Sibling_producer_directory_is_used()
    {
        var root = Directory.CreateTempSubdirectory("c590-apphost-").FullName;
        var staged = Path.Combine(root, "fakegrok", "fakegrok");
        Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
        File.WriteAllText(staged, "x");
        TestAppHostPath.Resolve("fakegrok", root, windows: false, siblingProducer: true).ShouldBe(staged);
    }

    [Test]
    public void Missing_apphost_names_attempted_file()
    {
        var root = Directory.CreateTempSubdirectory("c590-missing-").FullName;
        var error = Should.Throw<FileNotFoundException>(() => TestAppHostPath.Require("fakegrok", root, windows: false));
        error.FileName.ShouldBe(Path.Combine(root, "fakegrok", "fakegrok"));
    }

    [Test]
    public void E2e_runner_uses_shared_resolution()
    {
        var source = File.ReadAllText(Path.Combine(FindRoot(), "tests", "Antiphon.E2E", "Fixtures", "IsolatedSessionRunner.cs"));
        source.ShouldContain("TestAppHostPath");
        source.ShouldContain("Attempted ");
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("repo root was not found");
    }
}
