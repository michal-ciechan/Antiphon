using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Antiphon.Nightly;
using Antiphon.Tests.Infrastructure;
using Antiphon.Tests.Scripts.Nightly;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0088 S2: the read-only bootstrap self-check (Antiphon.Nightly.BootstrapCheck, which replaced scripts/bootstrap-check.ps1 in
/// unit 38 of the 2026-11-22 plan; the script had no driver) in process on the test's Machine: a mock file system, the commands on
/// its PATH and their answers, the database port, the Scheduled Tasks, the projects read and the stack check. Every command the
/// check runs is recorded, so each case also shows that only reads run (the --no-stack network probe is the one create, and it is
/// removed again). Nothing reads or changes this machine.
/// </summary>
[Category("Unit")]
public sealed class BootstrapCheckTests
{
    private static readonly string Root = OperatingSystem.IsWindows() ? @"C:\fixture\antiphon" : "/fixture/antiphon";
    private static readonly string Local = Path.Combine(Root, "..", "profile", "AppData", "Local");
    private static readonly string Roaming = Path.Combine(Root, "..", "profile", "AppData", "Roaming");
    private static readonly string Profile = Path.Combine(Root, "..", "profile");
    private static readonly string Windir = Path.Combine(Root, "..", "Windows");
    private const string Secret = "sk-ant-never-printed";

    [Test]
    public void A_ready_desktop_passes_every_check_and_exits_0()
    {
        var machine = Ready();
        var run = Check(machine);

        run.Exit.ShouldBe(0, run.Output);
        run.Output.ShouldContain("Antiphon bootstrap check (Phase A + live stack)");
        foreach (var line in new[]
        {
            L("PASS", "agent context", "AGENTS.md byte budget and section report passed"),
            L("PASS", "toolchain / dotnet", "10.0.204 satisfies global.json 10.0.204 (latestMinor)"),
            L("PASS", "toolchain / node", "v22.11.0 (>= 20)"),
            L("PASS", "toolchain / npm", "10.9.0"),
            L("PASS", "toolchain / pwsh", "app-exec alias ") + Path.Combine(Local, @"Microsoft\WindowsApps\pwsh.exe"),
            L("PASS", "toolchain / docker", "daemon answering"),
            L("PASS", "toolchain / tui", "claude.exe"),
            L("PASS", "dirs", "C:\\Antiphon\\worktrees"),
            L("PASS", "docker / postgres", "antiphon-postgres running + healthy"),
            L("PASS", "docker / volume", "antiphon_pgdata exists"),
            L("PASS", "database", "TCP localhost:17280 open; pg_isready ok"),
            L("PASS", "scheduled tasks", "Antiphon AppHost + Antiphon Session Runner registered"),
            L("PASS", "secrets", "Llm provider ApiKey present via dotnet user-secrets (id antiphon-server)"),
            L("PASS", "claude / repo skills", ".claude/skills/ present"),
            L("PASS", "client bundle", "client/dist/index.html is current (2026-11-20 10:00:00Z)"),
            L("PASS", "config / DefaultBranch", "Git:DefaultBranch=master matches HEAD"),
            L("PASS", "config / WorkspacePath", "Git:WorkspacePath is empty"),
            "Phase B: verify-dev-stack --skip-browser",
            L("PASS", "card-files", "No project card-file setup warnings; publication still requires explicit opt-in."),
            "Antiphon dev stack verified - all checks passed.",
            L("PASS", "stack health", "verify-dev-stack --skip-browser exited 0"),
            "Summary: 25 PASS, 0 WARN, 0 FAIL",
            "Exit code: 0",
        })
            run.Lines.ShouldContain(line);
        run.Output.ShouldNotContain(Secret);
        run.Output.ShouldNotContain("[WARN]");
        machine.Gets.ShouldBe(["http://stub.invalid:9/api/projects"]);
        machine.StackRuns.ShouldBe(1);
        machine.TcpCalls.ShouldBe([("127.0.0.1", 17280, 2000)]);
        // Only reads: the version probes, docker's inspections, pg_isready, the user-secrets list and git's HEAD.
        machine.Runs.ShouldBe([
            "dotnet.exe --version @" + Root,
            "node.exe --version",
            "npm.cmd --version",
            "docker.exe version (12s)",
            "docker.exe inspect --format {{.State.Status}}|{{if .State.Health}}{{.State.Health.Status}}{{else}}no-health{{end}} antiphon-postgres",
            "docker.exe volume inspect antiphon_pgdata",
            "docker.exe compose -f " + Path.Combine(Root, "docker-compose.dev.yml") + " exec -T postgres pg_isready -U antiphon -d antiphon",
            "dotnet.exe user-secrets list --id antiphon-server",
            "git.exe -C " + Root + " rev-parse --abbrev-ref HEAD",
        ]);
        machine.Disk.AllFiles.Count().ShouldBe(Ready().Disk.AllFiles.Count(), "the check writes no file");
    }

    [Test]
    public void A_bare_machine_fails_each_missing_tool_and_the_exit_code_counts_them()
    {
        var machine = Ready();
        machine.OnPath.Clear();
        machine.Tasks.Clear();
        machine.Tcp = false;
        var run = Check(machine, "--no-stack");

        run.Exit.ShouldBe(7, run.Output);
        foreach (var line in new[]
        {
            L("FAIL", "toolchain / dotnet", "dotnet not on PATH"),
            "           Install the .NET SDK pinned by global.json, then reopen the shell.",
            L("FAIL", "toolchain / node", "node not on PATH"),
            L("FAIL", "toolchain / npm", "npm not on PATH"),
            L("FAIL", "toolchain / docker", "docker not on PATH"),
            L("WARN", "toolchain / tui", "neither claude.exe nor grok.exe on PATH"),
            L("FAIL", "docker / postgres", "cannot inspect antiphon-postgres (daemon down)"),
            "           Start Docker Desktop, then: docker compose -f docker-compose.dev.yml up -d",
            L("FAIL", "docker / volume", "cannot inspect antiphon_pgdata (daemon down)"),
            L("WARN", "docker / hns", "skipped (docker daemon down)"),
            L("FAIL", "database", "TCP localhost:17280 closed"),
            L("WARN", "scheduled tasks", "not registered: Antiphon AppHost, Antiphon Session Runner"),
            L("PASS", "secrets", "Llm provider ApiKey present via env Llm__Providers__anthropic__ApiKey"),
            L("WARN", "config / DefaultBranch", "could not read git rev-parse --abbrev-ref HEAD"),
            "Phase B skipped (--no-stack): stack health was not checked.",
            "Summary: 13 PASS, 4 WARN, 7 FAIL",
            "Exit code: 7",
        })
            run.Lines.ShouldContain(line);
        run.Output.ShouldNotContain(Secret);
        machine.Runs.ShouldBeEmpty("nothing is on PATH, so nothing runs");
        machine.Gets.ShouldBeEmpty();
        machine.StackRuns.ShouldBe(0);
    }

    [Test]
    [Arguments("10.0.204", "10.0.204", "latestMinor", true)]
    [Arguments("10.3.100", "10.0.204", "latestMinor", true)]
    [Arguments("10.0.103", "10.0.204", "latestMinor", false)]
    [Arguments("11.0.100", "10.0.204", "latestMinor", false)]
    [Arguments("11.0.100", "10.0.204", "latestMajor", true)]
    [Arguments("10.0.300", "10.0.204", "latestFeature", true)]
    [Arguments("10.1.100", "10.0.204", "latestFeature", false)]
    [Arguments("10.0.205", "10.0.204", "", true)]
    [Arguments("10.0.205", "10.0.204", "disable", false)]
    [Arguments("10.0.100-rc.1.25451.107", "10.0.100", "latestMinor", false)]
    [Arguments("", "10.0.204", "latestMinor", false)]
    public void Sdk_roll_forward_follows_global_json(string installed, string pinned, string policy, bool satisfies)
    {
        BootstrapCheck.SdkSatisfiesGlobalJson(installed, pinned, policy).ShouldBe(satisfies);
    }

    [Test]
    public void An_sdk_older_than_global_json_fails_with_both_versions()
    {
        var machine = Ready();
        machine.Answers["dotnet.exe --version"] = Ok("10.0.103");
        var run = Check(machine);
        run.Exit.ShouldBe(1, run.Output);
        run.Lines.ShouldContain(L("FAIL", "toolchain / dotnet", "10.0.103 does not satisfy global.json 10.0.204 (latestMinor)"));
        run.Lines.ShouldContain("           Install the SDK version named in global.json (rollForward: latestMinor).");
    }

    [Test]
    public void Docker_that_does_not_answer_in_time_fails_and_skips_the_container_checks()
    {
        var machine = Ready();
        machine.Answers["docker.exe version"] = new BootstrapCommandResult(true, true, -1, "");
        var run = Check(machine);

        run.Lines.ShouldContain(L("FAIL", "toolchain / docker", "docker version timed out (12s)"));
        run.Lines.ShouldContain(L("FAIL", "docker / postgres", "cannot inspect antiphon-postgres (daemon down)"));
        run.Lines.ShouldContain(L("FAIL", "docker / volume", "cannot inspect antiphon_pgdata (daemon down)"));
        run.Lines.ShouldContain(L("PASS", "database", "TCP localhost:17280 open"));
        machine.Runs.ShouldNotContain(call => call.StartsWith("docker.exe inspect") || call.StartsWith("docker.exe compose"));
        run.Exit.ShouldBe(3, run.Output);
    }

    [Test]
    public void No_stack_probes_network_create_and_removes_the_network_again()
    {
        var machine = Ready();
        var run = Check(machine, "--no-stack");

        run.Exit.ShouldBe(0, run.Output);
        run.Lines.ShouldContain("Antiphon bootstrap check (Phase A only; --no-stack)");
        run.Lines.ShouldContain(L("PASS", "docker / hns", "docker network create completed"));
        var create = machine.Runs.Single(call => call.StartsWith("docker.exe network create ", StringComparison.Ordinal));
        create.ShouldMatch(@"^docker\.exe network create bootstrap-hns-\d+ \(8s\)$");
        var network = create.Split(' ')[3];
        machine.Runs.ShouldContain("docker.exe network rm " + network);
        machine.Runs.IndexOf("docker.exe network rm " + network).ShouldBeGreaterThan(machine.Runs.IndexOf(create));
        machine.Gets.ShouldBeEmpty("Phase B reads nothing under --no-stack");
        machine.StackRuns.ShouldBe(0);

        machine.Answers["docker.exe network create"] = new BootstrapCommandResult(true, true, -1, "");
        var hung = Check(machine, "--no-stack");
        hung.Lines.ShouldContain(L("WARN", "docker / hns", "docker network create hung or failed"));
        hung.Exit.ShouldBe(0, "a WARN never counts");
    }

    [Test]
    [Arguments(false, true, "scripts\\antiphon.cmd install-autostart --apphost-only   (use --apphost-only so a live session-runner is not killed by re-register)")]
    [Arguments(true, false, "scripts\\antiphon.cmd install-autostart   (full install; --apphost-only leaves the runner task alone)")]
    [Arguments(false, false, "scripts\\antiphon.cmd install-autostart   (re-register kills a running session-runner; afterwards use --apphost-only to refresh only the AppHost task)")]
    public void A_missing_logon_task_warns_with_the_install_that_keeps_a_live_runner(bool appHost, bool runner, string remedy)
    {
        var machine = Ready();
        machine.Tasks.Clear();
        if (appHost) machine.Tasks.Add(BootstrapCheck.AppHostTask);
        if (runner) machine.Tasks.Add(BootstrapCheck.RunnerTask);
        var run = Check(machine);
        run.Lines.ShouldContain(run.Lines.Single(line => line.StartsWith(L("WARN", "scheduled tasks", "not registered: "), StringComparison.Ordinal)));
        run.Lines.ShouldContain("           " + remedy);
        run.Exit.ShouldBe(0, run.Output);
    }

    [Test]
    public void A_provider_key_is_found_in_user_secrets_then_the_overlays_then_the_environment_and_never_printed()
    {
        var overlay = Ready();
        overlay.Answers["dotnet.exe user-secrets list --id antiphon-server"] = Ok("GitHub:PersonalAccessToken = " + Secret);
        overlay.Disk.AddFile(Path.Combine(Root, "server", "appsettings.Development.json"),
            new MockFileData("{ // local\n \"Llm\": { \"Providers\": { \"openai\": { \"ApiKey\": \"" + Secret + "\" } } }, }"));
        Check(overlay).Lines.ShouldContain(L("PASS", "secrets", "Llm provider ApiKey present via gitignored appsettings.Development.json"));

        // Keys match exactly: a case-variant overlay key is not the server's key.
        var environment = Ready();
        environment.Answers["dotnet.exe user-secrets list --id antiphon-server"] = Ok("");
        environment.Variables.Remove("Llm__Providers__anthropic__ApiKey");
        environment.Disk.AddFile(Path.Combine(Root, "server", "appsettings.Production.json"),
            new MockFileData("{ \"llm\": { \"providers\": { \"anthropic\": { \"apiKey\": \"" + Secret + "\" } } } }"));
        environment.Variables["Llm__Providers__openai__ApiKey"] = Secret;
        Check(environment).Lines.ShouldContain(L("PASS", "secrets", "Llm provider ApiKey present via env Llm__Providers__openai__ApiKey"));

        var none = Ready();
        none.Answers["dotnet.exe user-secrets list --id antiphon-server"] = Ok("Llm:Providers:anthropic:ApiKey =   ");
        none.Variables.Remove("Llm__Providers__anthropic__ApiKey");
        var run = Check(none);
        run.Lines.ShouldContain(L("WARN", "secrets", "no Llm:Providers anthropic/openai ApiKey in user-secrets, overlay json, or env"));
        run.Exit.ShouldBe(0, run.Output);
        foreach (var output in new[] { Check(overlay).Output, Check(environment).Output, run.Output }) output.ShouldNotContain(Secret);
    }

    [Test]
    public void A_client_source_newer_than_the_bundle_is_stale_but_tests_and_stories_are_not()
    {
        var machine = Ready();
        AddAt(machine, Path.Combine(Root, "client", "src", "App.test.tsx"), "2026-11-21T09:00:00Z");
        AddAt(machine, Path.Combine(Root, "client", "src", "Card.stories.tsx"), "2026-11-21T09:00:00Z");
        Check(machine).Lines.ShouldContain(L("PASS", "client bundle", "client/dist/index.html is current (2026-11-20 10:00:00Z)"));

        AddAt(machine, Path.Combine(Root, "client", "src", "pages", "Board.tsx"), "2026-11-21T08:00:00Z");
        AddAt(machine, Path.Combine(Root, "client", "src", "api", "cards.ts"), "2026-11-21T07:00:00Z");
        var stale = Check(machine);
        stale.Lines.ShouldContain(L("WARN", "client bundle", "stale: dist 2026-11-20 10:00:00Z, but ") + Path.Combine("src", "pages", "Board.tsx")
            + " changed 2026-11-21 08:00:00Z");
        stale.Lines.ShouldContain("           Run npm run build in client/.");

        machine.Disk.RemoveFile(Path.Combine(Root, "client", "dist", "index.html"));
        Check(machine).Lines.ShouldContain(L("WARN", "client bundle", "client/dist/index.html missing"));
    }

    [Test]
    public void Tracked_config_warns_on_another_branch_and_a_missing_workspace_path()
    {
        var machine = Ready();
        machine.Answers["git.exe -C " + Root + " rev-parse --abbrev-ref HEAD"] = Ok("wip/next");
        var missing = Path.Combine(Root, "..", "gone");
        machine.Disk.AddFile(Path.Combine(Root, "server", "appsettings.json"),
            new MockFileData("{ \"Git\": { \"DefaultBranch\": \"master\", \"WorkspacePath\": " + System.Text.Json.JsonSerializer.Serialize(missing) + " } }"));
        var run = Check(machine);
        run.Lines.ShouldContain(L("WARN", "config / DefaultBranch", "Git:DefaultBranch=master != HEAD wip/next"));
        run.Lines.ShouldContain(L("WARN", "config / WorkspacePath", "Git:WorkspacePath does not exist: ") + missing);
        run.Exit.ShouldBe(0, run.Output);

        machine.Disk.RemoveFile(Path.Combine(Root, "server", "appsettings.json"));
        var unreadable = Check(machine);
        unreadable.Lines.ShouldContain(L("WARN", "config / DefaultBranch", "server/appsettings.json missing or unreadable"));
        unreadable.Lines.ShouldContain(L("WARN", "config / WorkspacePath", "server/appsettings.json missing or unreadable"));
    }

    [Test]
    public void A_failed_stack_check_fails_with_its_verdict_and_passes_its_lines_through()
    {
        var machine = Ready();
        machine.Stack = host =>
        {
            host.Out.WriteLine("=== Verifying Antiphon dev stack (Aspire) ===");
            host.Out.WriteLine("  Port 17202                   FAIL  closed");
            host.Out.WriteLine("");
            host.Out.WriteLine("Stack NOT healthy - 1 check(s) failed:");
            host.Out.Write("  - Port 17202: closed");
            return 1;
        };
        var run = Check(machine);

        run.Exit.ShouldBe(1, run.Output);
        var lines = run.Lines.ToList();
        lines.ShouldContain("=== Verifying Antiphon dev stack (Aspire) ===");
        lines.ShouldContain(L("FAIL", "stack health", "verify-dev-stack --skip-browser exited 1"));
        lines.IndexOf(L("FAIL", "stack health", "verify-dev-stack --skip-browser exited 1"))
            .ShouldBeGreaterThan(lines.IndexOf("  - Port 17202: closed"), "the stack's lines stream before the verdict");
        var verdict = lines.IndexOf(L("FAIL", "stack health", "verify-dev-stack --skip-browser exited 1"));
        lines[verdict + 1].ShouldBe("           Stack NOT healthy - 1 check(s) failed:");
        lines[verdict + 2].ShouldBe("             - Port 17202: closed");

        machine.Projects = new NightlyReportHttpResponse(503, "down");
        Check(machine).Lines.ShouldContain(L("WARN", "card-files", "Project card-file readiness could not be inspected."));
    }

    [Test]
    public void An_agent_context_over_its_budget_fails()
    {
        var machine = Ready();
        machine.Disk.AddFile(Path.Combine(Root, "AGENTS.md"), new MockFileData("# Agents\n\n## Rules\n" + new string('x', AgentContextCheck.HardCeilingBytes)));
        var run = Check(machine);
        run.Exit.ShouldBe(1, run.Output);
        run.Lines.ShouldContain(L("FAIL", "agent context", "AGENTS.md violated its byte budget or could not be read"));
        run.Lines.ShouldContain("           Run scripts\\antiphon.cmd check-agent-context (scripts/antiphon on sh) and restore the 24,576-byte target.");
    }

    [Test]
    public void C2064_Bootstrap_skips_local_Postgres_and_probes_AppHost_user_secrets()
    {
        var machine = ReadyWithoutLocalPostgres();
        machine.Variables.ContainsKey("ANTIPHON_SKIP_DEV_POSTGRES").ShouldBeFalse();
        machine.UserVariables["ANTIPHON_SKIP_DEV_POSTGRES"] = "1";
        SetAppHostDatabaseSecret(machine);
        machine.OpenRemoteTcpPort = 25432;

        var run = Check(machine, "--no-stack");

        run.Exit.ShouldBe(0, run.Output);
        run.Lines.ShouldContain(L("SKIP", "docker / postgres", "ANTIPHON_SKIP_DEV_POSTGRES=1"));
        run.Lines.ShouldContain(L("SKIP", "docker / volume", "ANTIPHON_SKIP_DEV_POSTGRES=1"));
        run.Lines.ShouldContain(L("SKIP", "database / localhost:17280", "ANTIPHON_SKIP_DEV_POSTGRES=1"));
        run.Lines.ShouldContain(L("PASS", "database", "TCP 127.0.0.1:25432 open"));
        machine.TcpCalls.ShouldBe([("127.0.0.1", 25432, 2000)]);
        machine.Runs.ShouldNotContain(call => call.StartsWith("docker.exe inspect") ||
            call.StartsWith("docker.exe volume") || call.StartsWith("docker.exe compose"));
        run.Output.ShouldNotContain("docker compose -f docker-compose.dev.yml up");
        run.Output.ShouldNotContain("fixture-password");
    }

    [Test]
    public void C2064_Bootstrap_fails_when_the_configured_database_is_unreachable()
    {
        var machine = ReadyWithoutLocalPostgres();
        machine.Variables["ANTIPHON_SKIP_DEV_POSTGRES"] = "1";
        SetAppHostDatabaseSecret(machine);

        var run = Check(machine, "--no-stack");

        run.Exit.ShouldBe(1, run.Output);
        run.Lines.ShouldContain(L("FAIL", "database", "TCP 127.0.0.1:25432 closed or unreachable"));
        machine.TcpCalls.ShouldBe([("127.0.0.1", 25432, 2000)]);
        run.Output.ShouldNotContain("docker compose -f docker-compose.dev.yml up");
        run.Output.ShouldNotContain("fixture-password");
    }

    [Test]
    public void C2064_Bootstrap_still_checks_the_local_container_and_volume_when_unset()
    {
        var machine = ReadyWithoutLocalPostgres();
        machine.Variables.ContainsKey("ANTIPHON_SKIP_DEV_POSTGRES").ShouldBeFalse();
        machine.UserVariables.ContainsKey("ANTIPHON_SKIP_DEV_POSTGRES").ShouldBeFalse();

        var run = Check(machine, "--no-stack");

        run.Exit.ShouldBe(3, run.Output);
        run.Lines.ShouldContain(L("FAIL", "docker / postgres", "antiphon-postgres not found"));
        run.Lines.ShouldContain(L("FAIL", "docker / volume", "antiphon_pgdata volume not found"));
        run.Lines.ShouldContain(L("FAIL", "database", "TCP localhost:17280 closed; pg_isready failed"));
        machine.TcpCalls.ShouldBe([("127.0.0.1", 17280, 2000)]);
        machine.Runs.ShouldContain(call => call.StartsWith("docker.exe compose") && call.Contains("pg_isready"));
    }

    private static Machine ReadyWithoutLocalPostgres()
    {
        var machine = Ready();
        var absent = new BootstrapCommandResult(true, false, 1, "");
        machine.Answers["docker.exe inspect"] = absent;
        machine.Answers["docker.exe volume"] = absent;
        machine.Answers["docker.exe compose"] = absent;
        machine.Tcp = false;
        return machine;
    }

    private static void SetAppHostDatabaseSecret(Machine machine)
    {
        machine.Variables["APPDATA"] = Roaming;
        machine.Disk.AddFile(Path.Combine(Roaming, "Microsoft", "UserSecrets", "aspire-antiphon-apphost", "secrets.json"),
            new MockFileData("""{ "ConnectionStrings:DefaultConnection": "Host=127.0.0.1;Port=25432;Password='fixture-password;Host=irrelevant'" }"""));
    }

    [Test]
    public void The_command_replaced_the_script_and_the_docs_name_it()
    {
        Should.Throw<NightlyUsageException>(() => BootstrapCheckOptions.Parse(["--NoStack"], Root));
        Should.Throw<NightlyUsageException>(() => BootstrapCheckOptions.Parse([], null!));
        BootstrapCheckOptions.Parse(["--no-stack"], Root).NoStack.ShouldBeTrue();
        var repo = DockerStackDocuments.RepoRoot;
        File.Exists(Path.Combine(repo, "scripts", "bootstrap-check.ps1")).ShouldBeFalse("the check is a command");
        File.ReadAllBytes(Path.Combine(repo, "scripts", "lib", "Antiphon.Nightly", "BootstrapCheck.cs")).ShouldAllBe(b => b < 128, "ASCII only");
        File.ReadAllText(Path.Combine(repo, "docs", "bootstrap.md")).ShouldContain("scripts\\antiphon.cmd bootstrap-check");
        File.ReadAllText(Path.Combine(repo, ".claude", "skills", "antiphon-run", "SKILL.md")).ShouldContain("bootstrap-check");
    }

    // ---- the machine ----

    // Write-Check's line.
    private static string L(string status, string name, string detail) => $"  [{status}] {name,-28} {detail}";

    private static (int Exit, string Output, string[] Lines) Check(Machine machine, params string[] args)
    {
        var output = new StringWriter();
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-11-24T08:00:00Z"));
        var host = new NightlyHost
        {
            Clock = clock,
            Git = new FakeGit().Run,
            Launcher = new RecordingLauncher(),
            Processes = new FakeProcessTable(),
            Probe = new FakeHostProbe(),
            Out = output,
            Error = output,
            Trace = TextWriter.Null,
            Variables = name => machine.Variables.TryGetValue(name, out var value) ? value : null,
            UserVariables = name => machine.UserVariables.TryGetValue(name, out var value) ? value : null,
            Sleep = span => clock.Advance(span),
        };
        var exit = BootstrapCheck.Run(BootstrapCheckOptions.Parse(args, Root), host, machine);
        var text = output.ToString().ReplaceLineEndings("\n");
        return (exit, text, text.Split('\n'));
    }

    // Everything a ready desktop answers.
    private static Machine Ready()
    {
        var machine = new Machine();
        var disk = machine.Disk;
        disk.AddFile(Path.Combine(Root, "AGENTS.md"), new MockFileData("# Agents\n\n## Rules\nKeep it short.\n"));
        disk.AddFile(Path.Combine(Root, "global.json"), new MockFileData("{ \"sdk\": { \"version\": \"10.0.204\", \"rollForward\": \"latestMinor\" } }"));
        disk.AddFile(Path.Combine(Root, "docker-compose.dev.yml"), new MockFileData("services: {}\n"));
        disk.AddDirectory(Path.Combine(Root, ".claude", "skills"));
        disk.AddFile(Path.Combine(Root, "server", "appsettings.json"), new MockFileData("{ \"Git\": { \"DefaultBranch\": \"master\", \"WorkspacePath\": \"\" } }"));
        AddAt(machine, Path.Combine(Root, "client", "dist", "index.html"), "2026-11-20T10:00:00Z");
        AddAt(machine, Path.Combine(Root, "client", "src", "main.tsx"), "2026-11-19T10:00:00Z");
        foreach (var directory in BootstrapCheck.ConventionDirectories) disk.AddDirectory(directory);
        disk.AddFile(Path.Combine(Local, @"Microsoft\WindowsApps\pwsh.exe"), new MockFileData(""));
        disk.AddDirectory(Path.Combine(Local, @"Antiphon\DataProtection-Keys"));
        disk.AddFile(Path.Combine(Profile, @".claude\CLAUDE.md"), new MockFileData("# user\n"));
        machine.Variables["LOCALAPPDATA"] = Local;
        machine.Variables["WINDIR"] = Windir;
        machine.Variables["USERPROFILE"] = Profile;
        machine.Variables["ANTIPHON_API"] = "http://stub.invalid:9/";
        machine.Variables["Llm__Providers__anthropic__ApiKey"] = Secret;
        foreach (var name in new[] { "dotnet.exe", "node.exe", "npm.cmd", "docker.exe", "claude.exe", "git.exe" }) machine.OnPath.Add(name);
        machine.Answers["dotnet.exe --version"] = Ok("10.0.204");
        machine.Answers["node.exe --version"] = Ok("v22.11.0");
        machine.Answers["npm.cmd --version"] = Ok("10.9.0");
        machine.Answers["docker.exe version"] = Ok("Client:\n Version: 28.0.1\n\nServer: Docker Desktop 4.39.0 (184744)\n Engine:");
        machine.Answers["docker.exe inspect"] = Ok("running|healthy");
        machine.Answers["docker.exe volume"] = Ok("[ { \"Name\": \"antiphon_pgdata\" } ]");
        machine.Answers["docker.exe compose"] = Ok("/var/run/postgresql:5432 - accepting connections");
        machine.Answers["docker.exe network create"] = Ok("f00d");
        machine.Answers["docker.exe network rm"] = Ok("");
        machine.Answers["dotnet.exe user-secrets list --id antiphon-server"] = Ok("Llm:Providers:anthropic:ApiKey = " + Secret);
        machine.Answers["git.exe -C " + Root + " rev-parse --abbrev-ref HEAD"] = Ok("master");
        return machine;
    }

    private static void AddAt(Machine machine, string path, string written)
    {
        machine.Disk.AddFile(path, new MockFileData("x") { LastWriteTime = DateTimeOffset.Parse(written) });
    }

    private static BootstrapCommandResult Ok(string output) => new(true, false, 0, output);

    private sealed class Machine : IBootstrapCheckPlatform
    {
        public MockFileSystem Disk { get; } = new();
        public IFileSystem Files => Disk;
        public HashSet<string> OnPath { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Variables { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> UserVariables { get; } = new(StringComparer.Ordinal);
        /// <summary>The answers by "name arguments", else by the longest prefix of it that has one.</summary>
        public Dictionary<string, BootstrapCommandResult> Answers { get; } = new(StringComparer.Ordinal);
        public List<string> Runs { get; } = [];
        public bool Tcp { get; set; } = true;
        public int? OpenRemoteTcpPort { get; set; }
        public List<(string Host, int Port, int TimeoutMs)> TcpCalls { get; } = [];
        public HashSet<string> Tasks { get; } = [BootstrapCheck.AppHostTask, BootstrapCheck.RunnerTask];
        public NightlyReportHttpResponse Projects { get; set; } = new(200, "[{\"cardFileWarnings\":[]},{\"cardFileWarnings\":[]}]");
        public List<string> Gets { get; } = [];
        public Func<NightlyHost, int> Stack { get; set; } = host =>
        {
            host.Out.WriteLine("Antiphon dev stack verified - all checks passed.");
            return 0;
        };
        public int StackRuns { get; private set; }

        public string? FindCommand(IReadOnlyList<string> names) => names.FirstOrDefault(OnPath.Contains);

        public BootstrapCommandResult Run(string file, IReadOnlyList<string> arguments, string? workingDirectory, int timeoutSec)
        {
            var call = file + " " + string.Join(" ", arguments);
            Runs.Add(call + (timeoutSec > 0 ? " (" + timeoutSec + "s)" : "") + (workingDirectory == null ? "" : " @" + workingDirectory));
            for (var key = call; key.Length > 0; key = key.Contains(' ') ? key[..key.LastIndexOf(' ')] : "")
                if (Answers.TryGetValue(key, out var answer)) return answer;
            return new BootstrapCommandResult(true, false, 1, "unexpected command " + call);
        }

        public bool TcpOpen(string host, int port, int timeoutMs)
        {
            TcpCalls.Add((host, port, timeoutMs));
            return host == "127.0.0.1" && (port == 17280 ? Tcp : port == OpenRemoteTcpPort);
        }

        public bool TaskRegistered(string name) => Tasks.Contains(name);

        public NightlyReportHttpResponse Get(string url, TimeSpan timeout)
        {
            timeout.ShouldBe(TimeSpan.FromSeconds(10));
            Gets.Add(url);
            return Projects;
        }

        public int VerifyDevStack(NightlyHost host)
        {
            StackRuns++;
            return Stack(host);
        }
    }
}
