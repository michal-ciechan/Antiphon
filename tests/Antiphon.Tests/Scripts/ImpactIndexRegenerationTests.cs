using System.IO.Abstractions.TestingHelpers;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1838: the regenerate-derived-impact-index command (Antiphon.Nightly.ImpactIndexRegeneration, which replaced
/// scripts/regenerate-derived-impact-index.ps1 in unit 41 of the 2026-11-22 plan; the script had no driver) on a recording
/// launcher: the driver build and the driver run as dotnet children in the checkout, sharing this process's streams, and
/// the script's exit codes. Nothing starts. DerivedImpactIndexTests.regenerate_command_reports_changed_then_unchanged runs
/// the real driver.
/// </summary>
[Category("Unit")]
public sealed class ImpactIndexRegenerationTests
{
    private static readonly string Checkout = OperatingSystem.IsWindows() ? @"C:\fixture\checkout" : "/fixture/checkout";
    private static readonly string Elsewhere = OperatingSystem.IsWindows() ? @"C:\fixture\elsewhere" : "/fixture/elsewhere";
    private static readonly string Project = Path.Combine(Checkout, "scripts", "regenerate-derived-impact-index", "Antiphon.DerivedImpactIndex.csproj");

    [Test]
    public void Builds_the_driver_then_runs_it_over_the_checkout()
    {
        var run = Regenerate([], maxCpuCount: "3");

        run.Exit.ShouldBe(0, run.Error);
        run.Launches.Select(Argv).ShouldBe([
            "dotnet build " + Project + " -p:UseAppHost=false --nologo -maxcpucount:3",
            "dotnet run --project " + Project + " --no-build -p:UseAppHost=false -- " + Checkout,
        ]);
        foreach (var launch in run.Launches)
        {
            launch.WorkingDirectory.ShouldBe(Checkout, "the checkout's global.json picks the SDK");
            launch.Custody.ShouldBe(NightlyCustody.Watched);
            launch.CaptureOutput.ShouldBeFalse("the driver's lines pass through unchanged");
            launch.Environment.ShouldBeNull("the children inherit this process's environment");
            launch.TimeoutMilliseconds.ShouldBe(0);
        }
        run.Error.ShouldBeEmpty();
    }

    [Test]
    public void No_build_runs_only_the_driver_and_a_blank_grant_adds_no_maxcpucount()
    {
        var repo = Path.Combine(Elsewhere, "repo");
        var run = Regenerate(["--repo", repo, "--no-build"], maxCpuCount: " ");

        run.Exit.ShouldBe(0, run.Error);
        run.Launches.Select(Argv).ShouldBe(["dotnet run --project " + Project + " --no-build -p:UseAppHost=false -- " + repo]);

        var built = Regenerate([], maxCpuCount: null);
        built.Launches[0].Arguments.ShouldBe(["build", Project, "-p:UseAppHost=false", "--nologo"]);
    }

    [Test]
    public void The_repo_is_the_checkout_when_blank_and_a_relative_one_resolves_against_the_current_directory()
    {
        ImpactIndexRegenerationOptions.Parse([], Checkout, Elsewhere, null).Repo.ShouldBe(Checkout);
        ImpactIndexRegenerationOptions.Parse(["--repo", ""], Checkout, Elsewhere, null).Repo.ShouldBe(Checkout);
        ImpactIndexRegenerationOptions.Parse(["--repo", "  "], Checkout, Elsewhere, null).Repo.ShouldBe(Checkout);
        ImpactIndexRegenerationOptions.Parse(["--repo", "land"], Checkout, Elsewhere, null).Repo.ShouldBe(Path.Combine(Elsewhere, "land"));
        var options = ImpactIndexRegenerationOptions.Parse(["--no-build"], Checkout, Elsewhere, "4");
        (options.Checkout, options.NoBuild, options.MaxCpuCount).ShouldBe((Checkout, true, "4"));
    }

    [Test]
    [Arguments("-NoBuild")]
    [Arguments("--repo")]
    [Arguments("--no-build=true")]
    [Arguments("positional")]
    public void A_malformed_line_is_a_usage_error(string argument)
    {
        Should.Throw<NightlyUsageException>(() => ImpactIndexRegenerationOptions.Parse([argument], Checkout, Elsewhere, null));
    }

    [Test]
    public void A_failed_build_exits_1_without_running_the_driver()
    {
        var run = Regenerate([], answer: launch => Exit(launch.Arguments[0] == "build" ? 1 : 0));

        run.Exit.ShouldBe(1);
        run.Launches.Select(launch => launch.Arguments[0]).ShouldBe(["build"]);
    }

    [Test]
    [Arguments(1)]
    [Arguments(3)]
    public void A_failed_driver_exits_1(int driverExit)
    {
        var run = Regenerate([], answer: launch => Exit(launch.Arguments[0] == "run" ? driverExit : 0));

        run.Exit.ShouldBe(1);
        run.Launches.Select(launch => launch.Arguments[0]).ShouldBe(["build", "run"]);
    }

    [Test]
    public void A_missing_driver_project_exits_1_before_any_child()
    {
        var run = Regenerate([], withProject: false);

        run.Exit.ShouldBe(1);
        run.Launches.ShouldBeEmpty();
        run.Error.ShouldBe("regenerate-derived-impact-index: missing " + Project + Environment.NewLine);
    }

    [Test]
    public void Dotnet_that_does_not_start_exits_1_with_one_line()
    {
        var run = Regenerate(["--no-build"], answer: _ => throw new System.ComponentModel.Win32Exception(2,"The system cannot find the file specified."));

        run.Exit.ShouldBe(1);
        run.Error.ShouldBe("regenerate-derived-impact-index: dotnet did not start: The system cannot find the file specified." + Environment.NewLine);
    }

    private static NightlyLaunchResult Exit(int code) => new() { ExitCode = code };

    private static string Argv(NightlyLaunch launch) => launch.FileName + " " + string.Join(" ", launch.Arguments);

    private static (int Exit, IReadOnlyList<NightlyLaunch> Launches, string Error) Regenerate(string[] arguments, string? maxCpuCount = null,
        Func<NightlyLaunch, NightlyLaunchResult>? answer = null, bool withProject = true)
    {
        var files = new MockFileSystem();
        files.AddDirectory(Checkout);
        if (withProject) files.AddFile(Project, new MockFileData("<Project Sdk=\"Microsoft.NET.Sdk\" />\n"));
        var launcher = new RecordingLauncher(answer ?? (_ => Exit(0)));
        var output = new StringWriter();
        var error = new StringWriter();
        var host = NightlyHost.Create(TimeProvider.System, launcher, output, error, TextWriter.Null, null, null, null);
        var exit = ImpactIndexRegeneration.Run(ImpactIndexRegenerationOptions.Parse(arguments, Checkout, Elsewhere, maxCpuCount), host, files);
        output.ToString().ShouldBeEmpty("the command itself prints nothing on stdout; the children do");
        return (exit, launcher.Launches, error.ToString());
    }
}
