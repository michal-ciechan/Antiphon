using System.IO.Abstractions.TestingHelpers;
using System.Text;
using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.Review;
using Antiphon.Tests.Scripts.Nightly;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// The review engine's operator commands (Antiphon.Nightly ReviewStandalone, ReviewGitHubSetup and ReviewLiveGitHub, which
/// replaced scripts/review-standalone.ps1, review-github-setup.ps1 and review-live-github.ps1 in unit 42 of the 2026-11-22 plan;
/// the scripts had no driver) in process. Every observation is the test's Machine: docker, git, bw, gh and dotnet answers, the
/// detached starts, the process table, the tree stop and the build slot; the files are a mock file system and the clock is fake.
/// Nothing here reaches Bitwarden, GitHub, Docker or a real process: the vault item and its PEM key are fixtures.
/// </summary>
[Category("Unit")]
public sealed class ReviewEngineScriptsTests
{
    private static readonly bool Windows = OperatingSystem.IsWindows();
    private static readonly string Root = Windows ? @"C:\fixture\antiphon" : "/fixture/antiphon";
    private static readonly string Home = Windows ? @"C:\Users\op" : "/home/op";
    private static readonly string Temp = Windows ? @"C:\Users\op\AppData\Local\Temp" : "/tmp/op";
    private static readonly string Mirror = Windows ? @"C:\src\antiphon-review-mirror" : "/src/antiphon-review-mirror";
    private static readonly string LogDir = Path.Combine(Root, "logs", "review-standalone");
    private static readonly string PidFile = Path.Combine(LogDir, "pids.json");
    private const string Pem = "-----BEGIN RSA PRIVATE KEY-----\nMIIEfixture0only\n-----END RSA PRIVATE KEY-----\n";
    private const string Repo = "michal-ciechan/Antiphon";
    private const string VaultItem = "GitHub App - antiphon-review";

    // ---- review-standalone ----

    [Test]
    public void Standalone_runs_postgres_clones_the_mirror_and_starts_the_server_with_the_key_and_the_client_without_it()
    {
        var machine = new Machine { NotReadyFor = 2 };
        machine.Answers["docker inspect"] = new ReviewToolResult(1, "", "Error: No such object: antiphon-review-standalone-pg\n");
        var run = Standalone(machine, "--mirror-path", Mirror);

        run.Exit.ShouldBe(0, run.Error);
        run.Error.ShouldBe("", "docker inspect's stderr is discarded (2>$null)");
        machine.Calls.Select(c => c.CommandText).ShouldBe([
            "docker inspect -f {{.State.Running}} antiphon-review-standalone-pg",
            "docker run -d --name antiphon-review-standalone-pg -p 127.0.0.1:17290:5432 -e POSTGRES_DB=antiphon -e POSTGRES_USER=antiphon"
                + " -e POSTGRES_PASSWORD=antiphon_dev -v antiphon_review_standalone_pgdata:/var/lib/postgresql/data postgres:16",
            "docker exec antiphon-review-standalone-pg pg_isready -U antiphon -d antiphon",
            "docker exec antiphon-review-standalone-pg pg_isready -U antiphon -d antiphon",
            "docker exec antiphon-review-standalone-pg pg_isready -U antiphon -d antiphon",
            "git clone --quiet https://github.com/michal-ciechan/Antiphon.git " + Mirror,
            "bw get item GitHub App - antiphon-review --nointeraction"]);
        machine.Calls.Last().Arguments.ShouldBe(["get", "item", VaultItem, "--nointeraction"]);
        machine.Calls.Last().Environment!["BW_SESSION"].ShouldBe("session-from-file", "the trimmed ~/.bw-session");
        machine.Slept.ShouldBe(TimeSpan.FromSeconds(2));
        machine.Disk.Directory.Exists(Path.Combine(Temp, "antiphon-review-standalone")).ShouldBeTrue();

        machine.Starts.Count.ShouldBe(2);
        var server = machine.Starts[0];
        (server.FileName, server.WorkingDirectory).ShouldBe(("dotnet", Root));
        server.Arguments.ShouldBe(["run", "--project", Path.Combine(Root, "server"), "--no-launch-profile"]);
        (server.StandardOutput, server.StandardError).ShouldBe((Path.Combine(LogDir, "server.log"), Path.Combine(LogDir, "server.err.log")));
        var work = Path.Combine(Temp, "antiphon-review-standalone");
        var expected = new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["ASPNETCORE_URLS"] = "http://127.0.0.1:17262",
            ["ConnectionStrings__DefaultConnection"] = "Host=127.0.0.1;Port=17290;Database=antiphon;Username=antiphon;Password=antiphon_dev",
            ["SessionRunner__BaseUrl"] = "http://127.0.0.1:1",
            ["SessionRunner__Enabled"] = "false",
            ["Delegation__CheckInterpreterEnabled"] = "false",
            ["Delegation__DiagnoseEnabled"] = "false",
            ["Delegation__OutputDistillerEnabled"] = "false",
            ["Hangfire__ServerEnabled"] = "false",
            ["ZombieCensus__Enabled"] = "false",
            ["Schedules__Enabled"] = "false",
            ["CardFileSync__Enabled"] = "false",
            ["WorktreeResidue__Enabled"] = "false",
            ["WorktreeLegacyRelease__Enabled"] = "false",
            ["RunnerWorktreeRetention__Enabled"] = "false",
            ["AgentTui__ImportProfilesOnStartup"] = "false",
            ["GitHub__Enabled"] = "false",
            ["AntiphonMessaging__BootstrapServers"] = "127.0.0.1:1",
            ["Git__WorkspacePath"] = work,
            ["Git__WorktreeBasePath"] = Path.Combine(work, "worktrees"),
            ["Review__CodeHosts__Hosts__github__Kind"] = "GitHub",
            ["Review__CodeHosts__Hosts__github__App__AppId"] = "Iv23liFixture",
            ["Review__CodeHosts__Hosts__github__App__InstallationId"] = "424242",
            ["Review__Secrets__GitHubAppPrivateKey"] = Pem,
            ["Review__PublicBaseUrl"] = "http://localhost:17263",
            ["Review__DefaultViewerLogin"] = "michal-ciechan",
            ["Review__Policy__AuthorMayDecideRepos__0"] = Repo,
            ["Review__Policy__GatesOff__merge-gate__0"] = Repo,
            ["Review__Agents__Transport"] = "cli",
            ["Review__Agents__Repos__0__Repo"] = Repo,
            ["Review__Agents__Repos__0__LocalPath"] = Mirror,
            ["Review__Completions__Providers__haiku-cli__Dialect"] = "claude-cli",
            ["Review__Completions__Providers__haiku-cli__Model"] = "haiku",
            ["Review__Completions__Chains__completions-cheap__0__Provider"] = "haiku-cli",
            ["Review__Decisions__Providers__haiku-cli__Dialect"] = "claude-cli",
            ["Review__Decisions__Providers__haiku-cli__Model"] = "haiku",
            ["Review__Decisions__Chains__decisions-default__0__Provider"] = "haiku-cli",
            ["Review__Polling__Enabled"] = "true",
            ["Review__Polling__IntervalSeconds"] = "120",
            ["Review__Polling__Repos__0__Repo"] = Repo,
        };
        server.Environment.ShouldBe(expected, ignoreOrder: true);

        var client = machine.Starts[1];
        (client.FileName, client.WorkingDirectory).ShouldBe(("cmd.exe", Path.Combine(Root, "client")));
        client.Arguments.ShouldBe(["/c", "npx", "vite", "--host", "127.0.0.1"]);
        (client.StandardOutput, client.StandardError).ShouldBe((Path.Combine(LogDir, "client.log"), Path.Combine(LogDir, "client.err.log")));
        var clientExpected = new Dictionary<string, string?>(expected)
        {
            ["Review__Secrets__GitHubAppPrivateKey"] = null,
            ["Review__Secrets__GitHubOAuthClientSecret"] = null,
            ["services__server__http__0"] = "http://127.0.0.1:17262",
            ["VITE_PORT"] = "17263",
        };
        client.Environment.ShouldBe(clientExpected, ignoreOrder: true, "the client gets the settings without the key");

        Pids(machine).ShouldBe((4101, (int?)4102));
        run.Output.ShouldBe($"Server pid 4101 on http://127.0.0.1:17262 (log {Path.Combine(LogDir, "server.log")}).\n"
            + "Client pid 4102 on http://localhost:17263.\nWait for GET /health, then open /reviews. Stop with --stop.\n");
        run.Output.ShouldNotContain("MIIEfixture0only");
    }

    [Test]
    public void Standalone_options_trim_the_public_url_keep_the_merge_gate_and_skip_the_client_and_polling()
    {
        var machine = new Machine { Session = "session-from-env" };
        machine.Disk.RemoveFile(Path.Combine(Home, ".bw-session"));
        machine.Disk.AddDirectory(Path.Combine(Mirror, ".git"));
        var run = Standalone(machine, "--mirror-path", Mirror, "--no-auto-merge", "--no-client", "--no-polling", "--public-base-url", "https://review.laptop.codeperf.net/",
            "--port", "17300", "--client-port", "17301", "--postgres-port", "17302", "--poll-seconds", "30", "--repo", "owner/repo", "--viewer-login", "someone");

        run.Exit.ShouldBe(0, run.Error);
        machine.Calls.Select(c => c.CommandText).ShouldBe([
            "docker inspect -f {{.State.Running}} antiphon-review-standalone-pg",
            "docker exec antiphon-review-standalone-pg pg_isready -U antiphon -d antiphon",
            "git -C " + Mirror + " fetch --quiet --prune origin",
            "bw get item GitHub App - antiphon-review --nointeraction"], "a running container is neither run nor started; an existing mirror is fetched");
        machine.Calls.Last().Environment!["BW_SESSION"].ShouldBe("session-from-env");
        var server = machine.Starts.ShouldHaveSingleItem().Environment;
        server["Review__Policy__GatesOff__merge-gate__0"].ShouldBeNull("--no-auto-merge removes the variable");
        server["Review__PublicBaseUrl"].ShouldBe("https://review.laptop.codeperf.net");
        server["Review__Polling__Enabled"].ShouldBe("false");
        server["Review__Polling__IntervalSeconds"].ShouldBe("30");
        server["ASPNETCORE_URLS"].ShouldBe("http://127.0.0.1:17300");
        server["ConnectionStrings__DefaultConnection"].ShouldContain("Port=17302;");
        server["Review__DefaultViewerLogin"].ShouldBe("someone");
        server["Review__Agents__Repos__0__Repo"].ShouldBe("owner/repo");
        Pids(machine).ShouldBe((4101, (int?)null));
        run.Output.ShouldNotContain("Client pid");
    }

    [Test]
    public void Standalone_sign_in_gives_the_server_github_auth_and_keeps_the_client_secret_from_the_client()
    {
        var machine = new Machine { Item = Item(fields: [("Client ID", "Iv23liFixture"), ("Installation ID", "424242"), ("Client secret", "oauth-fixture-secret")]) };
        var run = Standalone(machine, "--mirror-path", Mirror, "--sign-in", "--public-base-url", "https://review.laptop.codeperf.net", "--viewer-login", "someone");

        run.Exit.ShouldBe(0, run.Error);
        machine.Starts.Count.ShouldBe(2);
        var server = machine.Starts[0].Environment;
        server["Review__Auth__Mode"].ShouldBe("GitHub");
        server["Review__Auth__ClientId"].ShouldBe("Iv23liFixture");
        server["Review__Secrets__GitHubOAuthClientSecret"].ShouldBe("oauth-fixture-secret");
        server["Review__Auth__AllowedLogins__0"].ShouldBe("someone");
        var client = machine.Starts[1].Environment;
        client["Review__Secrets__GitHubOAuthClientSecret"].ShouldBeNull("the client never gets the sign-in secret");
        client["Review__Secrets__GitHubAppPrivateKey"].ShouldBeNull();
        run.Output.ShouldNotContain("oauth-fixture-secret");
    }

    [Test]
    public void Standalone_sign_in_refuses_without_a_public_url_or_a_client_secret_and_starts_nothing()
    {
        var noUrl = new Machine();
        var run = Standalone(noUrl, "--mirror-path", Mirror, "--sign-in");
        (run.Exit, run.Error).ShouldBe((1, "--sign-in needs --public-base-url: GitHub returns the browser to <PublicBaseUrl>/api/review-auth/callback.\n"));
        noUrl.Starts.ShouldBeEmpty();

        var noSecret = new Machine();
        run = Standalone(noSecret, "--mirror-path", Mirror, "--sign-in", "--public-base-url", "https://review.laptop.codeperf.net");
        (run.Exit, run.Error).ShouldBe((1, $"Bitwarden item '{VaultItem}' has no 'Client secret' field.\n"));
        noSecret.Starts.ShouldBeEmpty();

        Standalone(new Machine(), "--mirror-path", Mirror).Exit.ShouldBe(0, "without --sign-in the item needs no client secret");
    }

    [Test]
    public void Standalone_starts_a_stopped_container()
    {
        var machine = new Machine();
        machine.Answers["docker inspect"] = new ReviewToolResult(0, "false\n", "");
        machine.Answers["docker start"] = new ReviewToolResult(0, "antiphon-review-standalone-pg\n", "");
        var run = Standalone(machine, "--mirror-path", Mirror);

        run.Exit.ShouldBe(0, run.Error);
        machine.Calls.Select(c => c.CommandText).Take(3).ShouldBe([
            "docker inspect -f {{.State.Running}} antiphon-review-standalone-pg",
            "docker start antiphon-review-standalone-pg",
            "docker exec antiphon-review-standalone-pg pg_isready -U antiphon -d antiphon"]);
        run.Output.ShouldNotContain("antiphon-review-standalone-pg\n", customMessage: "docker start's output is discarded");
    }

    [Test]
    [Arguments("--port", "17202", "Port 17202 belongs to the canonical stack (17202-17205).")]
    [Arguments("--client-port", "17205", "Port 17205 belongs to the canonical stack (17202-17205).")]
    [Arguments("--postgres-port", "17280", "Port 17280 is the dev stack Postgres.")]
    public void Standalone_refuses_the_canonical_and_dev_stack_ports_before_anything_runs(string flag, string port, string message)
    {
        var machine = new Machine();
        var run = Standalone(machine, flag, port);

        (run.Exit, run.Error).ShouldBe((1, message + "\n"));
        machine.Calls.ShouldBeEmpty();
        machine.Starts.ShouldBeEmpty();
    }

    [Test]
    public void Standalone_refuses_a_second_start_while_a_run_is_recorded()
    {
        var machine = new Machine();
        machine.Disk.AddFile(PidFile, new MockFileData("{\"server\": 1, \"client\": null}"));
        var run = Standalone(machine);

        (run.Exit, run.Error).ShouldBe((1, $"Already running (see {PidFile}); use --stop first.\n"));
        machine.Calls.ShouldBeEmpty();
    }

    [Test]
    public void Standalone_fails_when_postgres_never_answers_and_reads_no_vault()
    {
        var machine = new Machine { NotReadyFor = int.MaxValue };
        var run = Standalone(machine, "--mirror-path", Mirror);

        (run.Exit, run.Error).ShouldBe((1, "Postgres did not become ready in 60 s.\n"));
        machine.Slept.ShouldBeInRange(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(61));
        machine.Calls.ShouldNotContain(c => c.FileName == "bw" || c.FileName == "git");
        machine.Starts.ShouldBeEmpty();
    }

    [Test]
    public void Standalone_fails_on_a_failed_docker_run_or_clone_before_the_vault()
    {
        var docker = new Machine();
        docker.Answers["docker inspect"] = new ReviewToolResult(1, "", "");
        docker.Answers["docker run"] = new ReviewToolResult(125, "", "docker: port is already allocated.\n");
        var run = Standalone(docker, "--mirror-path", Mirror);
        (run.Exit, run.Error).ShouldBe((1, "docker: port is already allocated.\ndocker run for Postgres failed.\n"));
        docker.Calls.ShouldNotContain(c => c.FileName == "bw");

        var clone = new Machine();
        clone.Answers["git clone"] = new ReviewToolResult(128, "", "fatal: could not create work tree dir\n");
        run = Standalone(clone, "--mirror-path", Mirror);
        (run.Exit, run.Error).ShouldBe((1, $"fatal: could not create work tree dir\ngit clone into {Mirror} failed.\n"));
        clone.Calls.ShouldNotContain(c => c.FileName == "bw");
        clone.Starts.ShouldBeEmpty();
    }

    [Test]
    public void Standalone_starts_nothing_when_the_vault_is_locked_or_the_item_is_incomplete()
    {
        var locked = new Machine();
        locked.Disk.RemoveFile(Path.Combine(Home, ".bw-session"));
        var run = Standalone(locked, "--mirror-path", Mirror);
        (run.Exit, run.Error).ShouldBe((1, "Bitwarden is locked: no BW_SESSION and no ~/.bw-session.\n"));
        locked.Calls.ShouldNotContain(c => c.FileName == "bw");

        var missing = new Machine();
        missing.Answers["bw get"] = new ReviewToolResult(1, "", "Not found.\n");
        run = Standalone(missing, "--mirror-path", Mirror);
        (run.Exit, run.Error).ShouldBe((1, $"Not found.\nBitwarden item '{VaultItem}' not found or the vault is locked.\n"));

        var noInstallation = new Machine { Item = Item(fields: [("Client ID", "Iv23liFixture")]) };
        run = Standalone(noInstallation, "--mirror-path", Mirror);
        (run.Exit, run.Error).ShouldBe((1, $"Bitwarden item '{VaultItem}' has no 'Installation ID' field.\n"));

        var spelling = new Machine { Item = Item(fields: [("client id", "Iv23liFixture"), ("Installation ID", "424242")]) };
        run = Standalone(spelling, "--mirror-path", Mirror);
        (run.Exit, run.Error).ShouldBe((1, $"Bitwarden item '{VaultItem}' has no 'Client ID' field.\n"), "field names match exactly");

        var empty = new Machine { Item = Item(fields: [("Client ID", ""), ("Client ID", "later"), ("Installation ID", "424242")]) };
        run = Standalone(empty, "--mirror-path", Mirror);
        (run.Exit, run.Error).ShouldBe((1, $"Bitwarden item '{VaultItem}' has no 'Client ID' field.\n"), "the first field of the name decides");

        foreach (var machine in new[] { locked, missing, noInstallation, spelling, empty }) machine.Starts.ShouldBeEmpty();
    }

    [Test]
    public void Stop_stops_only_recorded_processes_that_are_still_ours_and_removes_the_record()
    {
        var machine = new Machine();
        machine.Disk.AddFile(PidFile, new MockFileData("{\r\n  \"server\": 4101,\r\n  \"client\": 4102\r\n}\r\n"));
        machine.CommandLines[4101] = "\"C:\\Program Files\\dotnet\\dotnet.exe\" run --project C:\\fixture\\antiphon\\server --no-launch-profile";
        machine.CommandLines[4102] = "notepad.exe C:\\notes.txt";
        var run = Standalone(machine, "--stop");

        run.Exit.ShouldBe(0, run.Error);
        machine.Stopped.ShouldBe([4101], "a reused pid whose command line is not ours is left alone");
        run.Output.ShouldBe("Stopped server (4101).\n");
        machine.Disk.File.Exists(PidFile).ShouldBeFalse();
        machine.Calls.ShouldBeEmpty();

        machine.Disk.AddFile(PidFile, new MockFileData("{\"server\": 4101, \"client\": null}"));
        machine.CommandLines.Remove(4101);
        run = Standalone(machine, "--stop");
        (run.Exit, run.Output).ShouldBe((0, ""));
        machine.Stopped.ShouldBe([4101], "a process that is gone is not stopped");
        machine.Disk.File.Exists(PidFile).ShouldBeFalse();

        machine.CommandLines[4102] = "C:\\Windows\\system32\\cmd.exe /c npx VITE --host 127.0.0.1";
        machine.Disk.AddFile(PidFile, new MockFileData("{\"client\": 4102}"));
        run = Standalone(machine, "--stop");
        (run.Exit, run.Output).ShouldBe((0, "Stopped client (4102).\n"), "-match is case-insensitive");

        run = Standalone(machine, "--stop");
        (run.Exit, run.Output).ShouldBe((0, "Nothing recorded as running.\n"));
    }

    [Test]
    public void Stop_keeps_a_record_that_is_not_json()
    {
        var machine = new Machine();
        machine.Disk.AddFile(PidFile, new MockFileData("server=4101"));
        var run = Standalone(machine, "--stop");

        run.Exit.ShouldBe(1);
        run.Error.ShouldStartWith(PidFile + " is not JSON: ");
        machine.Disk.File.Exists(PidFile).ShouldBeTrue();
        machine.Stopped.ShouldBeEmpty();
    }

    // ---- review-github-setup ----

    [Test]
    public void Setup_writes_every_setting_through_stdin_and_never_prints_the_key()
    {
        var machine = new Machine { Item = Item(installation: " 424242 ") };
        var run = Setup(machine);

        run.Exit.ShouldBe(0, run.Error);
        machine.Calls.Select(c => c.CommandText).ShouldBe(["bw get item GitHub App - antiphon-review --nointeraction", "dotnet user-secrets set --id antiphon-server"]);
        var write = machine.Calls[1];
        write.Arguments.ShouldNotContain(argument => argument.Contains("PRIVATE KEY"), "the key never reaches a command line");
        write.Input!.ShouldEndWith("}\n");
        var written = JsonSerializer.Deserialize<Dictionary<string, string>>(write.Input)!;
        written.ShouldBe(new Dictionary<string, string>
        {
            ["Review:CodeHosts:Hosts:github:Kind"] = "GitHub",
            ["Review:CodeHosts:Hosts:github:App:AppId"] = "Iv23liFixture",
            ["Review:CodeHosts:Hosts:github:App:InstallationId"] = "424242",
            ["Review:CodeHosts:Hosts:github:App:PrivateKeyConfigKey"] = "Review:Secrets:GitHubAppPrivateKey",
            ["Review:Secrets:GitHubAppPrivateKey"] = Pem,
            ["Review:PublicBaseUrl"] = "https://antiphon.desktop.codeperf.net",
            ["Review:DefaultViewerLogin"] = "michal-ciechan",
            ["Review:Agents:Repos:0:Repo"] = Repo,
            ["Review:Agents:Repos:0:Project"] = "Antiphon",
            ["Review:Agents:Runner"] = "local",
            ["Review:Completions:Chains:completions-cheap:0:Provider"] = "haiku-delegate",
            ["Review:Completions:Chains:completions-cheap:0:Transport"] = "Seat",
            ["Review:Polling:Enabled"] = "true",
            ["Review:Polling:IntervalSeconds"] = "120",
            ["Review:Polling:Repos:0:Repo"] = Repo,
            ["Review:Policy:AuthorMayDecideRepos:0"] = Repo,
            ["Review:Policy:GatesOff:merge-gate:0"] = Repo,
        });
        written.Keys.First().ShouldBe("Review:CodeHosts:Hosts:github:Kind");
        written.Keys.Last().ShouldBe("Review:Policy:GatesOff:merge-gate:0");
        run.Output.ShouldBe("Set 17 review settings in user-secrets 'antiphon-server': " + string.Join(", ", written.Keys) + "\n"
            + "Restart the AppHost from the main checkout to load them.\n");
        (run.Output + run.Error).ShouldNotContain("MIIEfixture0only");
    }

    [Test]
    public void Setup_dry_run_redacts_the_key_and_writes_nothing()
    {
        var machine = new Machine();
        var run = Setup(machine, "--dry-run", "--no-auto-merge", "--repo", "owner/repo", "--project", "Other", "--public-base-url", "https://x.example", "--viewer-login", "v",
            "--secrets-id", "other-secrets");

        run.Exit.ShouldBe(0, run.Error);
        machine.Calls.ShouldHaveSingleItem().FileName.ShouldBe("bw");
        run.Output.ShouldBe(string.Join("\n",
            "Review:CodeHosts:Hosts:github:Kind = GitHub",
            "Review:CodeHosts:Hosts:github:App:AppId = Iv23liFixture",
            "Review:CodeHosts:Hosts:github:App:InstallationId = 424242",
            "Review:CodeHosts:Hosts:github:App:PrivateKeyConfigKey = Review:Secrets:GitHubAppPrivateKey",
            $"Review:Secrets:GitHubAppPrivateKey = <PEM, {Pem.Length} chars>",
            "Review:PublicBaseUrl = https://x.example",
            "Review:DefaultViewerLogin = v",
            "Review:Agents:Repos:0:Repo = owner/repo",
            "Review:Agents:Repos:0:Project = Other",
            "Review:Agents:Runner = local",
            "Review:Completions:Chains:completions-cheap:0:Provider = haiku-delegate",
            "Review:Completions:Chains:completions-cheap:0:Transport = Seat",
            "Review:Polling:Enabled = true",
            "Review:Polling:IntervalSeconds = 120",
            "Review:Polling:Repos:0:Repo = owner/repo",
            "Review:Policy:AuthorMayDecideRepos:0 = owner/repo",
            "Dry run: nothing written.") + "\n", "--no-auto-merge leaves the merge gate's row out");
    }

    [Test]
    public void Setup_refuses_a_locked_vault_a_note_without_a_key_a_bad_installation_and_a_failed_write()
    {
        var locked = new Machine();
        locked.Disk.RemoveFile(Path.Combine(Home, ".bw-session"));
        var run = Setup(locked);
        (run.Exit, run.Error).ShouldBe((1, "Bitwarden is locked: no BW_SESSION and no ~/.bw-session. Unlock it first (bitwarden skill).\n"));
        locked.Calls.ShouldBeEmpty();

        var noKey = new Machine { Item = Item(notes: "a note, not a key") };
        run = Setup(noKey);
        (run.Exit, run.Error).ShouldBe((1, $"Bitwarden item '{VaultItem}' notes do not hold a PEM private key.\n"));
        noKey.Calls.ShouldNotContain(c => c.FileName == "dotnet");

        var lowerCase = new Machine { Item = Item(notes: "-----begin private key-----\nMIIE\n") };
        Setup(lowerCase, "--dry-run").Exit.ShouldBe(0, "-notmatch is case-insensitive");

        var installation = new Machine { Item = Item(installation: "four") };
        run = Setup(installation);
        (run.Exit, run.Error).ShouldBe((1, $"Bitwarden item '{VaultItem}' field 'Installation ID' is not a number.\n"));
        installation.Calls.ShouldNotContain(c => c.FileName == "dotnet");

        var failed = new Machine();
        failed.Answers["dotnet user-secrets"] = new ReviewToolResult(3, "", "Could not find the global property 'UserSecretsId'.\n");
        run = Setup(failed);
        (run.Exit, run.Error).ShouldBe((1, "Could not find the global property 'UserSecretsId'.\ndotnet user-secrets set failed (exit 3).\n"));
        run.Output.ShouldBe("");
    }

    // ---- review-live-github ----

    [Test]
    public void Live_check_runs_the_class_under_the_build_slot_with_the_app_in_its_environment_only()
    {
        var machine = new Machine { SlotExit = 3 };
        machine.Answers["gh pr view"] = new ReviewToolResult(0, "antiphon:skip\nAntiphon:Live-Check\n", "");
        var run = Live(machine, "--pr", "52");

        run.Exit.ShouldBe(3, "the test run's exit code");
        machine.Calls.Select(c => c.CommandText).ShouldBe([
            "gh pr view 52 --repo michal-ciechan/Antiphon --json labels --jq .labels[].name",
            "bw get item GitHub App - antiphon-review --nointeraction"]);
        var slot = machine.Slots.ShouldHaveSingleItem();
        slot.Label.ShouldBe("review-live-github");
        slot.WorkingDirectory.ShouldBe(Root);
        slot.Command.ShouldBe(["dotnet", "run", "--project", "tests/Antiphon.Tests", "--", "--treenode-filter", "/*/*/" + nameof(ReviewLiveGitHubTests) + "/*"]);
        slot.Environment.ShouldBe(new Dictionary<string, string>
        {
            [ReviewLiveGitHubTests.AppIdVariable] = "Iv23liFixture",
            [ReviewLiveGitHubTests.InstallationVariable] = "424242",
            [ReviewLiveGitHubTests.KeyVariable] = Pem,
            [ReviewLiveGitHubTests.RepoVariable] = Repo,
            [ReviewLiveGitHubTests.PrVariable] = "52",
        }, ignoreOrder: true);
        (run.Output + run.Error).ShouldNotContain("MIIEfixture0only");

        machine = new Machine();
        machine.Answers["gh pr view"] = new ReviewToolResult(0, "antiphon:live-check\n", "");
        Live(machine, "--pr", "52", "--no-build").Exit.ShouldBe(0);
        machine.Slots.ShouldHaveSingleItem().Command.ShouldBe(["dotnet", "run", "--project", "tests/Antiphon.Tests", "--no-build", "--", "--treenode-filter",
            "/*/*/ReviewLiveGitHubTests/*"]);
    }

    [Test]
    public void Live_check_names_the_variables_the_test_class_gates_on()
    {
        ReviewLiveGitHub.AppIdVariable.ShouldBe(ReviewLiveGitHubTests.AppIdVariable);
        ReviewLiveGitHub.InstallationVariable.ShouldBe(ReviewLiveGitHubTests.InstallationVariable);
        ReviewLiveGitHub.KeyVariable.ShouldBe(ReviewLiveGitHubTests.KeyVariable);
        ReviewLiveGitHub.RepoVariable.ShouldBe(ReviewLiveGitHubTests.RepoVariable);
        ReviewLiveGitHub.PrVariable.ShouldBe(ReviewLiveGitHubTests.PrVariable);
        ReviewLiveGitHub.TestClass.ShouldBe(nameof(ReviewLiveGitHubTests));
    }

    [Test]
    public void Live_check_refuses_an_unlabelled_or_unreadable_pr_before_the_vault()
    {
        var unlabelled = new Machine();
        unlabelled.Answers["gh pr view"] = new ReviewToolResult(0, "antiphon:skip\n", "");
        var run = Live(unlabelled, "--pr", "52");
        (run.Exit, run.Error).ShouldBe((1, "PR #52 is not labelled antiphon:live-check; the live check only posts on a scratch PR.\n"));
        unlabelled.Calls.ShouldHaveSingleItem().FileName.ShouldBe("gh");
        unlabelled.Slots.ShouldBeEmpty();

        var none = new Machine();
        none.Answers["gh pr view"] = new ReviewToolResult(0, "", "");
        run = Live(none, "--pr", "7", "--repo", "owner/repo", "--required-label", "scratch");
        (run.Exit, run.Error).ShouldBe((1, "PR #7 is not labelled scratch; the live check only posts on a scratch PR.\n"));

        var unreadable = new Machine();
        unreadable.Answers["gh pr view"] = new ReviewToolResult(1, "", "GraphQL: Could not resolve to a PullRequest with the number of 52.\n");
        run = Live(unreadable, "--pr", "52");
        (run.Exit, run.Error).ShouldBe((1, "GraphQL: Could not resolve to a PullRequest with the number of 52.\nCould not read PR #52 on michal-ciechan/Antiphon with gh.\n"));
        unreadable.Calls.ShouldHaveSingleItem();
        unreadable.Slots.ShouldBeEmpty();
    }

    [Test]
    public void Usage_errors_are_refused_before_anything_runs()
    {
        Should.Throw<NightlyUsageException>(() => ReviewLiveGitHubOptions.Parse([], Root, Home)).Message.ShouldBe("missing --pr");
        Should.Throw<NightlyUsageException>(() => ReviewLiveGitHubOptions.Parse(["--pr", "twelve"], Root, Home)).Message.ShouldBe("--pr expects a PR number, not 'twelve'");
        Should.Throw<NightlyUsageException>(() => ReviewLiveGitHubOptions.Parse(["-Pr", "12"], Root, Home));
        Should.Throw<NightlyUsageException>(() => ReviewStandaloneOptions.Parse(["--port", "x"], Root, Home, Temp));
        Should.Throw<NightlyUsageException>(() => ReviewStandaloneOptions.Parse(["--Stop"], Root, Home, Temp));
        Should.Throw<NightlyUsageException>(() => ReviewGitHubSetupOptions.Parse(["--dry-run", "--dry-run"], Home));
        var defaults = ReviewStandaloneOptions.Parse([], Root, Home, Temp);
        (defaults.Port, defaults.ClientPort, defaults.PostgresPort, defaults.PollSeconds).ShouldBe((17262, 17263, 17290, 120));
        (defaults.Repo, defaults.MirrorPath, defaults.VaultItem, defaults.ViewerLogin).ShouldBe((Repo, @"C:\src\antiphon-review-mirror", VaultItem, "michal-ciechan"));
        var setup = ReviewGitHubSetupOptions.Parse([], Home);
        (setup.Project, setup.PublicBaseUrl, setup.SecretsId).ShouldBe(("Antiphon", "https://antiphon.desktop.codeperf.net", "antiphon-server"));
        ReviewLiveGitHubOptions.Parse(["--pr", "12"], Root, Home).RequiredLabel.ShouldBe("antiphon:live-check");
    }

    // ---- the machine ----

    private static (int Exit, string Output, string Error) Standalone(Machine machine, params string[] args) =>
        Invoke(machine, host => ReviewStandalone.Run(ReviewStandaloneOptions.Parse(args, Root, Home, Temp), host, machine.Disk, machine));

    private static (int Exit, string Output, string Error) Setup(Machine machine, params string[] args) =>
        Invoke(machine, host => ReviewGitHubSetup.Run(ReviewGitHubSetupOptions.Parse(args, Home), host, machine.Disk, machine));

    private static (int Exit, string Output, string Error) Live(Machine machine, params string[] args) =>
        Invoke(machine, host => ReviewLiveGitHub.Run(ReviewLiveGitHubOptions.Parse(args, Root, Home), host, machine.Disk, machine));

    private static (int Exit, string Output, string Error) Invoke(Machine machine, Func<NightlyHost, int> run)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = new NightlyHost
        {
            Clock = machine.Clock,
            Git = new FakeGit().Run,
            Launcher = new RecordingLauncher(),
            Processes = new FakeProcessTable(),
            Probe = new FakeHostProbe(),
            Out = output,
            Error = error,
            Trace = TextWriter.Null,
            Sleep = span =>
            {
                machine.Slept += span;
                machine.Clock.Advance(span);
            },
            Variables = name => name == "BW_SESSION" ? machine.Session : null,
        };
        var exit = run(host);
        return (exit, output.ToString().ReplaceLineEndings("\n"), error.ToString().ReplaceLineEndings("\n"));
    }

    private static (int Server, int? Client) Pids(Machine machine)
    {
        using var document = JsonDocument.Parse(machine.Disk.File.ReadAllText(PidFile));
        var client = document.RootElement.GetProperty("client");
        return (document.RootElement.GetProperty("server").GetInt32(), client.ValueKind == JsonValueKind.Null ? null : client.GetInt32());
    }

    /// <summary>`bw get item`'s JSON for a fixture app: its fields (default Client ID and Installation ID) and its notes (default the fixture PEM).</summary>
    private static string Item(string installation = "424242", string notes = Pem, (string Name, string Value)[]? fields = null)
    {
        fields ??= [("Client ID", "Iv23liFixture"), ("Installation ID", installation)];
        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["object"] = "item",
            ["name"] = VaultItem,
            ["notes"] = notes,
            ["fields"] = fields.Select(f => new Dictionary<string, object> { ["name"] = f.Name, ["value"] = f.Value, ["type"] = 0 }).ToArray(),
        });
    }

    /// <summary>
    /// The machine: each tool's answer (by the start of its command text; bw answers Item, anything unlisted exit 0), pg_isready
    /// failing NotReadyFor times, the detached starts (pids 4101, 4102, ...), the process table's command lines, the tree stops and
    /// the build slot (answering SlotExit). A running container and a session file in ~ by default.
    /// </summary>
    private sealed class Machine : IReviewEngineSeams
    {
        public Machine()
        {
            Answers["docker inspect"] = new ReviewToolResult(0, "true\n", "");
            Disk.AddFile(Path.Combine(Home, ".bw-session"), new MockFileData("  session-from-file \r\n"));
            Disk.AddDirectory(Root);
        }

        public MockFileSystem Disk { get; } = new();
        public FakeTimeProvider Clock { get; } = new(DateTimeOffset.Parse("2026-11-24T12:00:00Z"));
        public TimeSpan Slept { get; set; }
        public string? Session { get; init; }
        public string Item { get; init; } = ReviewEngineScriptsTests.Item();
        public Dictionary<string, ReviewToolResult> Answers { get; } = new(StringComparer.Ordinal);
        public int NotReadyFor { get; set; }
        public List<ReviewToolCall> Calls { get; } = [];
        public List<ReviewDetachedStart> Starts { get; } = [];
        public Dictionary<int, string> CommandLines { get; } = [];
        public List<int> Stopped { get; } = [];
        public List<BuildSlotOptions> Slots { get; } = [];
        public int SlotExit { get; init; }

        public ReviewToolResult Run(ReviewToolCall call)
        {
            Calls.Add(call);
            if (call.CommandText.Contains(" pg_isready ", StringComparison.Ordinal))
            {
                if (NotReadyFor <= 0) return new ReviewToolResult(0, "accepting connections\n", "");
                NotReadyFor--;
                return new ReviewToolResult(2, "no response\n", "");
            }
            foreach (var (prefix, answer) in Answers)
                if (call.CommandText.StartsWith(prefix + " ", StringComparison.Ordinal)) return answer;
            return call.FileName == "bw" ? new ReviewToolResult(0, Item + "\n", "") : new ReviewToolResult(0, "", "");
        }

        public int Start(ReviewDetachedStart start)
        {
            Starts.Add(start);
            return 4100 + Starts.Count;
        }

        public string? CommandLine(int pid) => CommandLines.TryGetValue(pid, out var line) ? line : null;

        public void StopTree(int pid) => Stopped.Add(pid);

        public int BuildSlot(BuildSlotOptions options)
        {
            Slots.Add(options);
            return SlotExit;
        }
    }
}
