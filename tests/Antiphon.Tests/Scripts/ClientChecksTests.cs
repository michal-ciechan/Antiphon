using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// The client gates and suite (Antiphon.Nightly.ClientLint, ClientTypecheck, ClientTests and ClientNpmInstall, which replaced
/// scripts/lint-client.ps1, scripts/typecheck-client.ps1, scripts/lib/client-npm-install.ps1 and the body of
/// scripts/test-client.ps1 in unit 32 of the 2026-11-22 plan) in process: each node and npm child is answered by a recording
/// runner, files are in memory (the npm lock's contention on the real file system, which enforces FileShare.None), and the
/// clock is fake. No case runs node or npm.
/// </summary>
[Category("Unit")]
public sealed class ClientChecksTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 9, 30, 0, TimeSpan.Zero);
    private const string Pass = "  (PASS)";
    private const string Fail = "  (FAIL - do not report this run as green)";

    /// <summary>A recording tool runner: each launch is kept, and answer gives its exit code and output lines.</summary>
    internal sealed class RecordingClientTools(Func<ClientToolLaunch, (int Exit, string[] Lines)> answer)
    {
        public List<ClientToolLaunch> Launches { get; } = [];

        public ClientToolRunner Runner => (launch, line) =>
        {
            Launches.Add(launch);
            var (exit, lines) = answer(launch);
            foreach (var text in lines) line(text);
            return exit;
        };
    }

    internal sealed class World
    {
        public World(Func<ClientToolLaunch, (int Exit, string[] Lines)>? answer = null, IFileSystem? files = null, string? root = null)
        {
            Root = root ?? Path.Combine(Path.GetTempPath(), "client-checks-world");
            Files = files ?? new MockFileSystem();
            Tools = new RecordingClientTools(answer ?? (_ => Answer(0)));
        }

        public string Root { get; }
        public IFileSystem Files { get; }
        public RecordingClientTools Tools { get; }
        public FakeTimeProvider Clock { get; } = new(Now);
        public StringWriter Out { get; } = new();
        public StringWriter Error { get; } = new();
        public Func<string, string?> Variables { get; set; } = _ => null;
        public Action<TimeSpan>? OnSleep { get; set; }

        public string Client(params string[] parts) => Path.Combine(new[] { Root, "client" }.Concat(parts).ToArray());
        public string Logs(string name) => Path.Combine(Root, "logs", name);
        public string Vitest => ClientTools.Vitest(Root);
        public string Tsc => ClientTools.Tsc(Root);
        public string Eslint => ClientTools.Eslint(Root);

        public NightlyHost Host() => new()
        {
            Clock = Clock, Git = new FakeGit().Run, Launcher = new RecordingLauncher(), Processes = new FakeProcessTable(), Probe = new FakeHostProbe(),
            Out = Out, Error = Error, Trace = TextWriter.Null, Variables = name => Variables(name)!,
            Sleep = span => { Clock.Advance(span); OnSleep?.Invoke(span); },
        };

        public void Write(string path, string text = "")
        {
            Files.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            Files.File.WriteAllText(path, text);
        }

        /// <summary>Installs the three binaries (empty files) and client/package-lock.json.</summary>
        public World Installed()
        {
            foreach (var file in new[] { Vitest, Tsc, Eslint, Client("package-lock.json") }) Write(file);
            return this;
        }

        public string[] Lines => Out.ToString().Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        public string Log(string name) => Files.File.ReadAllText(Logs(name)).Replace("\r\n", "\n");

        public int Lint(string path = "") => ClientLint.Run(new ClientLintOptions { Path = path, RepoRoot = Root }, Host(), Files, Tools.Runner);
        public int Typecheck(string project = "") => ClientTypecheck.Run(new ClientTypecheckOptions { Project = project, RepoRoot = Root }, Host(), Files, Tools.Runner);
        public int Test(params string[] args) => ClientTests.Run(ClientTestsOptions.Parse(args, Root), Host(), Files, Tools.Runner);
    }

    private static string Tool(ClientToolLaunch launch) =>
        launch.FileName == "node" ? Path.GetFileName(launch.Arguments[0]) : "npm";

    // ---- lint-client --------------------------------------------------------------------------------------------------

    [Test]
    public void Lint_runs_eslint_over_the_client_with_the_content_cache_and_tees_its_log()
    {
        var world = new World(_ => Answer(0, "clean")).Installed();

        world.Lint().ShouldBe(0);

        var launch = world.Tools.Launches.ShouldHaveSingleItem();
        launch.FileName.ShouldBe("node");
        launch.Arguments.ToArray().ShouldBe([world.Eslint, ".", "--max-warnings", "0", "--cache", "--cache-strategy", "content", "--cache-location", world.Client("node_modules", ".tmp", "eslintcache")]);
        launch.WorkingDirectory.ShouldBe(world.Client());
        world.Files.Directory.Exists(world.Client("node_modules", ".tmp")).ShouldBeTrue();
        world.Lines.ShouldBe(["clean", "", "CLIENT LINT EXIT CODE: 0" + Pass, "Full output: " + world.Logs("client-lint.log")]);
        world.Log("client-lint.log").ShouldBe("clean\n");

        var failing = new World(_ => Answer(1, "a.ts", "  1:1  error  no-unused-vars")).Installed();
        failing.Lint().ShouldBe(1);
        failing.Lines.ShouldBe(["a.ts", "  1:1  error  no-unused-vars", "", "CLIENT LINT EXIT CODE: 1" + Fail, "Full output: " + failing.Logs("client-lint.log")]);
        failing.Log("client-lint.log").ShouldBe("a.ts\n  1:1  error  no-unused-vars\n");
    }

    [Test]
    public void Lint_path_lints_one_file_from_its_directory_and_refuses_a_missing_one()
    {
        var world = new World().Installed();
        world.Write(world.Client("src", "features", "a.test.ts"));

        world.Lint("client/src/features/a.test.ts").ShouldBe(0);

        var launch = world.Tools.Launches.ShouldHaveSingleItem();
        launch.Arguments.ToArray().ShouldBe([world.Eslint, "a.test.ts", "--max-warnings", "0"]);
        launch.WorkingDirectory.ShouldBe(world.Client("src", "features"));

        var missing = new World().Installed();
        missing.Lint("client/src/gone.ts").ShouldBe(1);
        missing.Tools.Launches.ShouldBeEmpty();
        missing.Lines.ShouldBe(["lint target is missing at " + missing.Client("src", "gone.ts"), "", "CLIENT LINT EXIT CODE: 1" + Fail,
            "Full output: " + missing.Logs("client-lint.log")]);
    }

    [Test]
    public void Lint_installs_a_missing_eslint_once_and_fails_when_it_is_still_missing()
    {
        using var npm = NpmOnPath(out var variables);
        var world = new World(_ => Answer(0, "added 1 package")) { Variables = variables };
        world.Write(world.Client("package-lock.json"));

        world.Lint().ShouldBe(1);

        var install = world.Tools.Launches.ShouldHaveSingleItem();
        AssertNpm(install, npm, world.Client(), "ci");
        world.Lines.ShouldBe([
            "eslint is not installed at " + world.Eslint + " - installing client dependencies first.",
            "client dependencies are not installed - installing once under the npm ci lock.",
            "added 1 package",
            "eslint is still not installed at " + world.Eslint + "; run npm ci in client/ by hand and look at why it failed.",
            "",
            "CLIENT LINT EXIT CODE: 1" + Fail,
        ]);

        // The install that lands eslint lets the lint run.
        World? installing = null;
        installing = new World(launch => Tool(launch) == "npm" ? Installs(installing!, 0) : Answer(0)) { Variables = variables };
        installing.Lint().ShouldBe(0);
        installing.Tools.Launches.Select(Tool).ToArray().ShouldBe(["npm", "eslint.js"]);
        AssertNpm(installing.Tools.Launches[0], npm, installing.Client(), "install");
    }

    // ---- typecheck-client ---------------------------------------------------------------------------------------------

    [Test]
    public void Typecheck_checks_both_projects_incrementally_and_appends_the_second_to_the_log()
    {
        var world = new World(launch => Answer(0, "tsc " + launch.Arguments[2])).Installed();

        world.Typecheck().ShouldBe(0);

        world.Tools.Launches.Select(l => l.Arguments.ToArray()).ToArray().ShouldBe([
            [world.Tsc, "-p", "tsconfig.app.json", "--pretty", "false", "--noEmit", "--incremental", "--tsBuildInfoFile", "node_modules/.tmp/tsconfig.app.tsbuildinfo"],
            [world.Tsc, "-p", "tsconfig.node.json", "--pretty", "false", "--noEmit", "--incremental", "--tsBuildInfoFile", "node_modules/.tmp/tsconfig.node.tsbuildinfo"],
        ]);
        world.Tools.Launches.ShouldAllBe(l => l.FileName == "node" && l.WorkingDirectory == world.Client());
        world.Log("client-typecheck.log").ShouldBe("tsc tsconfig.app.json\ntsc tsconfig.node.json\n");
        world.Lines.ShouldBe(["tsc tsconfig.app.json", "tsc tsconfig.node.json", "", "CLIENT TYPECHECK EXIT CODE: 0" + Pass, "Full output: " + world.Logs("client-typecheck.log")]);
    }

    [Test]
    [Arguments(0, 2, "", 2)]
    [Arguments(2, 1, "", 2)]
    [Arguments(1, 3, "", 3)]
    [Arguments(1, 0, "src/App.tsx(12,5): error TS2322: Type 'string' is not assignable to type 'number'.", 2)]
    [Arguments(1, 0, "error TS5083: Cannot read file 'tsconfig.app.json'.", 1)]
    [Arguments(0, 1, "vite.config.ts(3,1): error TS2307: Cannot find module 'vite'.", 2)]
    public void Typecheck_keeps_the_worse_project_code_and_a_logged_source_diagnostic_is_2(int app, int node, string diagnostic, int expected)
    {
        var world = new World(launch => launch.Arguments[2] == "tsconfig.app.json" ? Answer(app, app == 0 ? Array.Empty<string>() : new[] { diagnostic }) : Answer(node, node == 0 ? Array.Empty<string>() : new[] { diagnostic })).Installed();

        world.Typecheck().ShouldBe(expected);

        world.Tools.Launches.Count.ShouldBe(2, "a failing project does not stop the other");
        world.Lines.Reverse().Skip(1).First().ShouldBe("CLIENT TYPECHECK EXIT CODE: " + expected + (expected == 0 ? Pass : Fail));
    }

    [Test]
    public void Typecheck_project_runs_one_tsconfig_from_the_checkout_root_without_the_diagnostic_remap()
    {
        var world = new World(_ => Answer(1, "client/src/a.ts(1,1): error TS1005: ';' expected.")).Installed();

        world.Typecheck("client/tsconfig.node.json").ShouldBe(1);

        var launch = world.Tools.Launches.ShouldHaveSingleItem();
        launch.Arguments.ToArray().ShouldBe([world.Tsc, "--pretty", "false", "--noEmit", "-p", "client/tsconfig.node.json"]);
        launch.WorkingDirectory.ShouldBe(world.Root);
    }

    [Test]
    public void Typecheck_without_typescript_fails_after_the_install()
    {
        using var npm = NpmOnPath(out var variables);
        var world = new World(_ => Answer(7, "npm ERR! network")) { Variables = variables };

        world.Typecheck().ShouldBe(1);

        world.Lines.ShouldBe([
            "typescript is not installed at " + world.Tsc + " - installing client dependencies first.",
            "client dependencies are not installed - installing once under the npm ci lock.",
            "npm ERR! network",
            "npm install exited with code 7.",
            "npm ci exited with code 7.",
            "typescript is still not installed at " + world.Tsc + "; run npm ci in client/ by hand and look at why it failed.",
            "",
            "CLIENT TYPECHECK EXIT CODE: 1" + Fail,
        ]);
    }

    // ---- test-client --------------------------------------------------------------------------------------------------

    [Test]
    public void Test_client_runs_the_typecheck_then_the_lint_then_vitest_with_the_forwarded_arguments()
    {
        var world = new World(launch => Answer(0, Tool(launch) + " ok")).Installed();
        world.Write(world.Client("src", "a.test.ts"));

        world.Test("--json-result-path", "out/client.json", "--typecheck-project", "client/tsconfig.node.json", "--lint-path", "client/src/a.test.ts",
            "attentionVisuals.test", "-t", "keeps a name with spaces").ShouldBe(0);

        world.Tools.Launches.Select(Tool).ToArray().ShouldBe(["tsc", "eslint.js", "vitest.mjs"]);
        var vitest = world.Tools.Launches[2];
        vitest.FileName.ShouldBe("node");
        vitest.Arguments.ToArray().ShouldBe([world.Vitest, "run", "--reporter=default", "--reporter=json", "--outputFile", "out/client.json", "attentionVisuals.test", "-t", "keeps a name with spaces"]);
        vitest.WorkingDirectory.ShouldBe(world.Client());
        world.Lines.TakeLast(3).ToArray().ShouldBe(["", "CLIENT TESTS EXIT CODE: 0" + Pass, "Full output: " + world.Logs("client-tests.log")]);
        world.Lines.ShouldContain("CLIENT TYPECHECK EXIT CODE: 0" + Pass);
        world.Lines.ShouldContain("CLIENT LINT EXIT CODE: 0" + Pass);
        world.Log("client-tests.log").ShouldBe("vitest.mjs ok\n", "the gates keep their own logs");
        world.Log("client-typecheck.log").ShouldBe("tsc ok\n");
        world.Log("client-lint.log").ShouldBe("eslint.js ok\n");

        var plain = new World().Installed();
        plain.Test().ShouldBe(0);
        plain.Tools.Launches.Last().Arguments.ToArray().ShouldBe([plain.Vitest, "run"]);
    }

    [Test]
    public void Test_client_stops_at_a_failing_gate_and_passes_vitests_code_through()
    {
        var typecheck = new World(launch => Tool(launch) == "tsc" ? Answer(2, "a.ts(1,1): error TS2322: no") : Answer(0)).Installed();
        typecheck.Test().ShouldBe(2);
        typecheck.Tools.Launches.Select(Tool).ToArray().ShouldBe(["tsc", "tsc"]);
        typecheck.Lines.TakeLast(2).ToArray().ShouldBe(["", "CLIENT TESTS EXIT CODE: 2  (FAIL - typecheck failed; vitest was not started)"]);

        var lint = new World(launch => Tool(launch) == "eslint.js" ? Answer(1) : Answer(0)).Installed();
        lint.Test().ShouldBe(1);
        lint.Tools.Launches.Select(Tool).ToArray().ShouldBe(["tsc", "tsc", "eslint.js"]);
        lint.Lines.TakeLast(2).ToArray().ShouldBe(["", "CLIENT TESTS EXIT CODE: 1  (FAIL - lint failed; vitest was not started)"]);

        var vitest = new World(launch => Tool(launch) == "vitest.mjs" ? Answer(1, " FAIL  src/a.test.ts > adds") : Answer(0)).Installed();
        vitest.Test("a.test").ShouldBe(1);
        vitest.Lines.TakeLast(3).ToArray().ShouldBe(["", "CLIENT TESTS EXIT CODE: 1" + Fail, "Full output: " + vitest.Logs("client-tests.log")]);
        vitest.Log("client-tests.log").ShouldBe(" FAIL  src/a.test.ts > adds\n");

        // A child that cannot start is one line and exit 1, as PowerShell's call operator left it.
        var gone = new World(launch => Tool(launch) == "vitest.mjs" ? throw new System.ComponentModel.Win32Exception("node: not found") : Answer(0)).Installed();
        gone.Test().ShouldBe(1);
        gone.Log("client-tests.log").ShouldBe("node: not found\n");
    }

    [Test]
    public void Test_client_installs_the_three_binaries_once_before_the_gates()
    {
        using var npm = NpmOnPath(out var variables);
        World? world = null;
        world = new World(launch => Tool(launch) == "npm" ? Installs(world!, 0) : Answer(0)) { Variables = variables };
        world.Write(world.Client("package-lock.json"));

        world.Test().ShouldBe(0);

        world.Tools.Launches.Select(Tool).ToArray().ShouldBe(["npm", "tsc", "tsc", "eslint.js", "vitest.mjs"]);
        world.Lines.Take(2).ToArray().ShouldBe([
            "client dependencies are not installed - installing once for vitest, tsc and eslint.",
            "client dependencies are not installed - installing once under the npm ci lock.",
        ]);

        var broken = new World(_ => Answer(1)) { Variables = variables };
        broken.Test().ShouldBe(1);
        broken.Tools.Launches.Select(Tool).ToArray().ShouldBe(["npm"]);
        broken.Lines.TakeLast(5).ToArray().ShouldBe([
            "npm install exited with code 1.",
            "npm ci exited with code 1.",
            "vitest is still not installed at " + broken.Vitest + "; run npm ci in client/ by hand and look at why it failed.",
            "",
            "CLIENT TESTS EXIT CODE: 1" + Fail,
        ]);
    }

    [Test]
    public void Test_client_options_forward_everything_but_their_own_flags()
    {
        var options = ClientTestsOptions.Parse(["BoardPage", "--reporter=verbose", "--lint-path", "x.ts", "-JsonResultPath", "y", "--repo-root", "/clone"], "cli");
        options.Forward.ToArray().ShouldBe(["BoardPage", "--reporter=verbose", "-JsonResultPath", "y"]);
        (options.LintPath, options.TypecheckProject, options.JsonResultPath, options.RepoRoot).ShouldBe(("x.ts", "", "", "/clone"));
        ClientTestsOptions.Parse(["--repo-root", " "], "cli").RepoRoot.ShouldBe("cli");
        ClientTestsOptions.Parse([], "cli").Forward.ShouldBeEmpty();

        Should.Throw<NightlyUsageException>(() => ClientTestsOptions.Parse(["--json-result-path"], "cli")).Message.ShouldBe("missing value for --json-result-path");
        Should.Throw<NightlyUsageException>(() => ClientTestsOptions.Parse(["--lint-path", "--typecheck-project", "p"], "cli")).Message.ShouldBe("missing value for --lint-path");
        Should.Throw<NightlyUsageException>(() => ClientTestsOptions.Parse(["--lint-path", "a", "--lint-path", "b"], "cli")).Message.ShouldBe("repeated flag --lint-path");
        Should.Throw<NightlyUsageException>(() => ClientTestsOptions.Parse(["--json-result-path=a.json"], "cli")).Message.ShouldBe("write --json-result-path <value>, not --json-result-path=<value>");

        ClientLintOptions.Parse(["--path", "a.ts"], "root").Path.ShouldBe("a.ts");
        Should.Throw<NightlyUsageException>(() => ClientLintOptions.Parse(["-Path", "a.ts"], "root"));
        ClientTypecheckOptions.Parse(["--project", "p.json"], "root").Project.ShouldBe("p.json");
        Should.Throw<NightlyUsageException>(() => ClientTypecheckOptions.Parse(["p.json"], "root"));
    }

    // ---- the shared npm install ---------------------------------------------------------------------------------------

    [Test]
    public void Npm_install_does_nothing_when_every_binary_is_there()
    {
        var world = new World(_ => throw new InvalidOperationException("no npm")).Installed();

        ClientNpmInstall.Install(world.Root, [world.Vitest, world.Tsc], world.Host(), world.Files, world.Tools.Runner).ShouldBe(0);

        world.Tools.Launches.ShouldBeEmpty();
        world.Files.File.Exists(world.Logs(ClientNpmInstall.LockName)).ShouldBeFalse("the lock is taken only to install");
        world.Out.ToString().ShouldBeEmpty();
    }

    [Test]
    public void Npm_install_waits_for_a_held_lock_and_gives_up_after_fifteen_minutes()
    {
        using var fixture = NightlyFixture.Create("client-npm-lock");
        var world = new World(_ => throw new InvalidOperationException("no npm while the lock is held"), new FileSystem(), fixture.Root);
        Directory.CreateDirectory(fixture.PathOf("logs"));
        var lockPath = fixture.PathOf("logs", ClientNpmInstall.LockName);
        using (new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            ClientNpmInstall.Install(world.Root, [world.Vitest], world.Host(), world.Files, world.Tools.Runner).ShouldBe(1);
        }

        world.Lines.ShouldBe(["timed out waiting for the client npm ci lock at " + lockPath]);
        world.Tools.Launches.ShouldBeEmpty();
        world.Clock.GetUtcNow().ShouldBe(Now + ClientNpmInstall.LockWait + ClientNpmInstall.LockRetry, "a 2 s retry until the deadline has passed");
    }

    [Test]
    public void Npm_install_looks_again_once_it_holds_the_lock_and_skips_npm_when_the_holder_installed()
    {
        using var fixture = NightlyFixture.Create("client-npm-lock");
        var world = new World(_ => throw new InvalidOperationException("the other holder installed"), new FileSystem(), fixture.Root);
        Directory.CreateDirectory(fixture.PathOf("logs"));
        using var held = new FileStream(fixture.PathOf("logs", ClientNpmInstall.LockName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        world.OnSleep = _ =>
        {
            // The holder finishes its install and exits while this caller waits.
            world.Write(world.Vitest);
            held.Dispose();
        };

        ClientNpmInstall.Install(world.Root, [world.Vitest], world.Host(), world.Files, world.Tools.Runner).ShouldBe(0);

        world.Tools.Launches.ShouldBeEmpty();
        world.Out.ToString().ShouldBeEmpty();
        world.Clock.GetUtcNow().ShouldBe(Now + ClientNpmInstall.LockRetry);
    }

    // ---- helpers ------------------------------------------------------------------------------------------------------

    /// <summary>A tool's answer: its exit code and output lines.</summary>
    private static (int Exit, string[] Lines) Answer(int exit, params string[] lines) => (exit, lines);

    /// <summary>The npm answer that lands the three binaries.</summary>
    private static (int, string[]) Installs(World world, int exit)
    {
        foreach (var file in new[] { world.Vitest, world.Tsc, world.Eslint }) world.Write(file);
        return Answer(exit, "added 3 packages");
    }

    /// <summary>A PATH directory holding an npm launcher (Windows resolves npm.cmd on PATH and runs it through ComSpec).</summary>
    private static NightlyFixture NpmOnPath(out Func<string, string?> variables)
    {
        var fixture = NightlyFixture.Create("client-npm-path");
        Directory.CreateDirectory(fixture.PathOf("bin"));
        File.WriteAllText(fixture.PathOf("bin", OperatingSystem.IsWindows() ? "npm.cmd" : "npm"), "");
        var path = fixture.PathOf("bin");
        variables = name => name switch { "PATH" => path, "ComSpec" => @"C:\Windows\system32\cmd.exe", _ => null };
        return fixture;
    }

    private static void AssertNpm(ClientToolLaunch launch, NightlyFixture npm, string clientDir, string verb)
    {
        launch.WorkingDirectory.ShouldBe(clientDir);
        if (OperatingSystem.IsWindows())
        {
            launch.FileName.ShouldBe(@"C:\Windows\system32\cmd.exe");
            launch.Arguments.ToArray().ShouldBe(["/c", npm.PathOf("bin", "npm.cmd"), verb]);
        }
        else
        {
            launch.FileName.ShouldBe("npm");
            launch.Arguments.ToArray().ShouldBe([verb]);
        }
    }
}
