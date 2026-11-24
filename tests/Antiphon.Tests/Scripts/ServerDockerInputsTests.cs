using System.IO.Abstractions;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.Scripts.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1586: the linked-input guard (Antiphon.Nightly.ServerDockerInputs, the check-server-docker-inputs command that replaced
/// scripts/check-server-docker-inputs.ps1 in unit 46 of the 2026-11-22 plan) in process on offline fixture trees. The twelve
/// cases of the deleted driver scripts/test-server-docker-inputs.ps1 keep their trees and exit codes; the rest pin the COPY
/// forms and refusals the guard reads. No Docker, network or application host.
/// </summary>
[Category("Unit")]
public sealed class ServerDockerInputsTests
{
    private const string ServerProject = """<Project><ItemGroup><Compile Include="../scripts/lib/Server.cs" Link="Server.cs" /></ItemGroup></Project>""";

    [Test]
    [Arguments("missing-copy", "", 1, "", "", "", false)]
    [Arguments("glob-copy", "COPY scripts/lib/*.cs scripts/lib/", 0, "", "", "", false)]
    [Arguments("directory-copy", "COPY scripts/lib/ scripts/lib/", 0, "", "", "", false)]
    [Arguments("json-copy", """COPY ["scripts/lib/Server.cs", "scripts/lib/Helper.cs", "scripts/lib/"]""", 0, "", "", "", false)]
    [Arguments("wrong-destination", "COPY scripts/lib/*.cs other/", 1, "", "", "", false)]
    [Arguments("wrong-stage", "", 1, "", "FROM sdk AS other\nWORKDIR /src\nCOPY scripts/lib/*.cs scripts/lib/", "", false)]
    [Arguments("after-publish", "", 1, "", "", "COPY scripts/lib/*.cs scripts/lib/", false)]
    [Arguments("ignored-source", "COPY scripts/lib/*.cs scripts/lib/", 1, "**/*.cs", "", "", false)]
    [Arguments("reincluded-source", "COPY scripts/lib/*.cs scripts/lib/", 0, "**/*.cs\n!scripts/lib/*.cs", "", "", false)]
    [Arguments("excluded-parent", "COPY scripts/lib/*.cs scripts/lib/", 1, "scripts/\n!scripts/lib/*.cs", "", "", false)]
    [Arguments("missing-source", "COPY scripts/lib/*.cs scripts/lib/", 1, "", "", "", true)]
    [Arguments("unsupported-copy-option", "COPY --exclude=*.cs scripts/lib/ scripts/lib/", 2, "", "", "", false)]
    public void Driver_cases_keep_their_exit_codes(string name, string copy, int expected, string ignore, string before, string after, bool missingSource)
    {
        WithFixture(root =>
        {
            Write(root, copy, ignore: ignore, before: before, after: after, missingSource: missingSource);
            var run = Check(root);
            run.Exit.ShouldBe(expected, name + "\n" + run.Output);
            if (expected == 1) run.Output.ShouldContain("DOCKER INPUT missing scripts/lib/");
            if (expected == 2) run.Lines.ShouldHaveSingleItem().ShouldStartWith("DOCKER INPUT error: ");
            else run.Lines.Last().ShouldStartWith("DOCKER INPUT projects=2 linked=2 missing=" + (run.Lines.Length - 1));
        });
    }

    [Test]
    public void Each_missing_input_names_its_reason_in_path_order()
    {
        WithFixture(root =>
        {
            Write(root, "COPY scripts/lib/Server.cs scripts/lib/", ignore: "scripts/lib/Server.cs", missingSource: true);
            var run = Check(root);
            run.Exit.ShouldBe(ServerDockerInputs.Missing);
            run.Lines.ShouldBe([
                "DOCKER INPUT missing scripts/lib/Helper.cs (source absent)",
                "DOCKER INPUT missing scripts/lib/Server.cs (dockerignore excludes input)",
                "DOCKER INPUT projects=2 linked=2 missing=2",
            ]);

            Write(root, "COPY scripts/lib/*.cs other/");
            Check(root).Lines.ShouldBe([
                "DOCKER INPUT missing scripts/lib/Helper.cs (no COPY to /src/scripts/lib/Helper.cs before publish)",
                "DOCKER INPUT missing scripts/lib/Server.cs (no COPY to /src/scripts/lib/Server.cs before publish)",
                "DOCKER INPUT projects=2 linked=2 missing=2",
            ]);
        });
    }

    [Test]
    [Arguments("COPY . .", "WORKDIR /src", 0)]
    [Arguments("COPY scripts/lib/*.cs scripts/lib/", "WORKDIR /\nWORKDIR src", 0)]
    [Arguments("COPY --chown=1000:1000 --chmod=644 scripts/lib/ scripts/lib/", "WORKDIR /src", 0)]
    [Arguments("COPY scripts/lib/Server.cs scripts/lib/Helper.cs scripts/lib", "WORKDIR /src", 0)]
    [Arguments("COPY scripts/lib/Server.cs \\\n  scripts/lib/Helper.cs \\\n  scripts/lib/", "WORKDIR /src", 0)]
    [Arguments("COPY \"scripts/lib/Server.cs\" \"scripts/lib/Helper.cs\" scripts/lib/", "WORKDIR /src", 0)]
    [Arguments("COPY scripts/**/*.cs scripts/lib/", "WORKDIR /src", 0)]
    [Arguments("COPY scripts/lib/?elper.cs scripts/lib/Server.cs scripts/lib/", "WORKDIR /src", 0)]
    [Arguments("COPY --from=other /x scripts/lib/", "WORKDIR /src", 1)]
    [Arguments("COPY scripts/lib/Server.cs scripts/lib/Server.cs", "WORKDIR /src", 1)]
    [Arguments("COPY $SRC scripts/lib/", "WORKDIR /src", 2)]
    [Arguments("COPY scripts/lib/", "WORKDIR /src", 2)]
    [Arguments("""COPY ["scripts/lib/Server.cs", 1, "scripts/lib/"]""", "WORKDIR /src", 2)]
    [Arguments("""COPY ["scripts/lib/Server.cs", "scripts/lib/" """, "WORKDIR /src", 2)]
    public void Copy_forms_resolve_against_the_workdir(string copy, string workdir, int expected)
    {
        WithFixture(root =>
        {
            Write(root, copy, workdir: workdir);
            var run = Check(root);
            run.Exit.ShouldBe(expected, copy + "\n" + run.Output);
        });
    }

    [Test]
    public void Stage_and_instructions_match_without_case_and_a_missing_stage_or_publish_fails_closed()
    {
        WithFixture(root =>
        {
            Write(root, "copy scripts/lib/*.cs scripts/lib/", stage: "from sdk as Server-Build");
            Check(root).Exit.ShouldBe(ServerDockerInputs.Complete);

            Write(root, "COPY scripts/lib/*.cs scripts/lib/", stage: "FROM sdk AS build");
            Check(root).Lines.ShouldBe(["DOCKER INPUT error: Missing server-build stage or dotnet publish"]);
        });
    }

    [Test]
    public void Dockerfile_dockerignore_wins_over_dockerignore_and_no_ignore_file_fails_closed()
    {
        WithFixture(root =>
        {
            Write(root, "COPY scripts/lib/*.cs scripts/lib/", ignore: "**/*.cs");
            File.WriteAllText(Path.Combine(root, "Dockerfile.dockerignore"), "# nothing excluded\n");
            Check(root).Exit.ShouldBe(ServerDockerInputs.Complete);

            File.WriteAllText(Path.Combine(root, "Dockerfile.dockerignore"), "**/*.cs\n");
            File.WriteAllText(Path.Combine(root, ".dockerignore"), "\n");
            Check(root).Exit.ShouldBe(ServerDockerInputs.Missing);

            File.Delete(Path.Combine(root, "Dockerfile.dockerignore"));
            File.Delete(Path.Combine(root, ".dockerignore"));
            var run = Check(root);
            run.Exit.ShouldBe(ServerDockerInputs.Error);
            run.Output.ShouldStartWith("DOCKER INPUT error: ");
        });
    }

    [Test]
    [Arguments("""<Compile Include="$(Root)/x.cs" />""", 2, "DOCKER INPUT error: Dynamic Compile Include needs guard support: $(Root)/x.cs")]
    [Arguments("""<Compile Include="../../../x.cs" />""", 2, "DOCKER INPUT error: Path escapes root: ../../x.cs")]
    [Arguments("""<Compile Include="../scripts/lib/Server.cs;../scripts/lib/Helper.cs" />""", 0, "DOCKER INPUT projects=2 linked=2 missing=0")]
    [Arguments("""<Compile Include="Local.cs" /><Compile Remove="../scripts/lib/Other.cs" />""", 0, "DOCKER INPUT projects=2 linked=1 missing=0")]
    [Arguments("<Compile", 2, "DOCKER INPUT error: ")]
    public void Linked_includes_are_read_from_every_project(string items, int expected, string line)
    {
        WithFixture(root =>
        {
            Write(root, "COPY scripts/lib/*.cs scripts/lib/", serverProject: "<Project><ItemGroup>" + items + "</ItemGroup></Project>");
            var run = Check(root);
            run.Exit.ShouldBe(expected, run.Output);
            run.Lines.Last().ShouldStartWith(line);
        });
    }

    [Test]
    public void A_repository_without_server_or_linked_inputs_fails_closed()
    {
        WithFixture(root =>
        {
            Check(root).Exit.ShouldBe(ServerDockerInputs.Error);

            Write(root, "COPY scripts/lib/*.cs scripts/lib/", serverProject: "<Project />");
            File.WriteAllText(Path.Combine(root, "src", "Helper", "Helper.csproj"), "<Project />\n");
            Check(root).Lines.ShouldBe(["DOCKER INPUT error: No projects or linked Compile inputs found"]);
        });
    }

    [Test]
    public void Ci_runs_the_command_after_the_dotnet_setup()
    {
        var workflow = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, ".github/workflows/ci.yml")).ReplaceLineEndings("\n");
        var job = workflow[workflow.IndexOf("\n  ci:\n", StringComparison.Ordinal)..workflow.IndexOf("\n  windows-native-tests:\n", StringComparison.Ordinal)];
        var setup = job.IndexOf("uses: actions/setup-dotnet@", StringComparison.Ordinal);
        var check = job.IndexOf("- name: Check server Docker linked inputs\n        shell: bash\n        run: ./scripts/antiphon check-server-docker-inputs\n",
            StringComparison.Ordinal);
        setup.ShouldBeGreaterThan(0);
        check.ShouldBeGreaterThan(setup, "the guard needs the .NET SDK the job sets up");
        workflow.ShouldNotContain("server-docker-inputs.ps1");
    }

    private static (int Exit, string Output, string[] Lines) Check(string repository)
    {
        using var output = new StringWriter();
        var host = new NightlyHost
        {
            Clock = TimeProvider.System,
            Git = new FakeGit().Run,
            Launcher = new RecordingLauncher(_ => throw new InvalidOperationException("the guard starts nothing")),
            Processes = new FakeProcessTable(),
            Probe = new FakeHostProbe(),
            Out = output,
            Error = output,
            Trace = TextWriter.Null,
            Variables = _ => null,
        };
        var exit = ServerDockerInputs.Run(ServerDockerInputsOptions.Parse(["--repository", repository], "unused"), host, new FileSystem());
        var text = output.ToString().ReplaceLineEndings("\n");
        return (exit, text, text.TrimEnd('\n').Split('\n'));
    }

    // The driver's tree: server/Server.csproj and src/Helper/Helper.csproj each link one scripts/lib source; the Dockerfile's
    // server-build stage copies both project directories, then the case's COPY, then publishes.
    private static void Write(string root, string copy, string ignore = "", string before = "", string after = "", bool missingSource = false,
        string workdir = "WORKDIR /src", string stage = "FROM sdk AS server-build", string serverProject = ServerProject)
    {
        foreach (var directory in new[] { "server", "src/Helper", "scripts/lib" })
            Directory.CreateDirectory(Path.Combine(root, directory));
        File.WriteAllText(Path.Combine(root, "server", "Server.csproj"), serverProject + "\n");
        File.WriteAllText(Path.Combine(root, "src", "Helper", "Helper.csproj"),
            """<Project><ItemGroup><Compile Include="../../scripts/lib/Helper.cs"><Link>Helper.cs</Link></Compile></ItemGroup></Project>""" + "\n");
        File.WriteAllText(Path.Combine(root, "scripts", "lib", "Server.cs"), "class Server {}\n");
        var helper = Path.Combine(root, "scripts", "lib", "Helper.cs");
        if (missingSource) File.Delete(helper);
        else File.WriteAllText(helper, "class Helper {}\n");
        File.WriteAllText(Path.Combine(root, ".dockerignore"), ignore + "\n");
        File.WriteAllText(Path.Combine(root, "Dockerfile"), string.Join("\n",
            before, stage, workdir, "COPY server/ server/", "COPY src/ src/", copy, "RUN dotnet publish server/Server.csproj", after) + "\n");
    }

    private static void WithFixture(Action<string> test)
    {
        var root = Directory.CreateTempSubdirectory("card1586-");
        try
        {
            test(root.FullName);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
