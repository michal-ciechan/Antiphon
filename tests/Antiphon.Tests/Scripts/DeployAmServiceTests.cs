using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.Scripts.Nightly;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// Unit 50 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the am-service gateway deploy (Antiphon.Nightly.AmServiceDeploy,
/// the deploy-am-service command that replaced scripts/deploy-am-service.ps1) in process. These are the cases of the deleted driver
/// scripts/test-deploy-am-service.ps1 (CARD-0270, CARD-0410, CARD-0496, CARD-1261): ssh, scp and tar are a recording launcher that
/// answers as the driver's fakes did (AmServiceWorld), the server's HTTP a fake, the clock fake and the tokens fixed; the manifest,
/// the hash and the migrations are this checkout's. The exact remote commands and lines are the ones the PowerShell script sent and
/// printed (captured from it before its deletion). Nothing reaches a host, the server or a network; the retry against real curl
/// and sh is AmServiceDeployReadinessTests.
/// </summary>
[Category("Unit")]
public sealed class DeployAmServiceTests
{
    private const string Target = AmServiceWorld.Target;
    private const string Secret = AmServiceWorld.Secret;
    private const string Required = "Configuration: --ssh-target is required; specify one user@host destination.";
    private const string Invalid = "Configuration: invalid --ssh-target; expected one user@host (SSH alias, DNS hostname or IPv4), without whitespace, options, paths or port suffixes.";
    private static string Root => DelegateScriptRunner.RepoRoot;

    // ---- the options ----

    [Test]
    public void Usage_and_flags()
    {
        var options = AmServiceDeployOptions.Parse([], "checkout");
        (options.SshTarget, options.Deploy, options.SkipRealTrafficCheck, options.WhatIf, options.Confirm, options.NoConfirm, options.TimeoutSec, options.RepoRoot)
            .ShouldBe(((string?)null, false, false, false, false, false, 180, "checkout"));
        options = AmServiceDeployOptions.Parse(["--ssh-target", Target, "--deploy", "--skip-real-traffic-check", "--what-if", "--no-confirm", "--timeout-sec", "3600"], "");
        (options.SshTarget, options.Deploy, options.SkipRealTrafficCheck, options.WhatIf, options.NoConfirm, options.TimeoutSec)
            .ShouldBe((Target, true, true, true, true, 3600));
        AmServiceDeployOptions.Parse(["--confirm", "--timeout-sec", "10"], "").Confirm.ShouldBeTrue();
        // The value is taken exactly; the target check, not the parser, refuses it.
        AmServiceDeployOptions.Parse(["--ssh-target", "-option"], "").SshTarget.ShouldBe("-option");
        AmServiceDeployOptions.Parse(["--ssh-target", ""], "").SshTarget.ShouldBe("");

        // ValidateRange(10, 3600), and the switches PowerShell could not combine, are usage errors.
        foreach (var bad in new[] { "9", "3601", "0" })
            Should.Throw<NightlyUsageException>(() => AmServiceDeployOptions.Parse(["--timeout-sec", bad], "")).Message
                .ShouldBe("--timeout-sec must be between 10 and 3600, not " + bad);
        Should.Throw<NightlyUsageException>(() => AmServiceDeployOptions.Parse(["--confirm", "--no-confirm"], "")).Message
            .ShouldBe("--confirm and --no-confirm exclude each other");
        Should.Throw<NightlyUsageException>(() => AmServiceDeployOptions.Parse(["-SshTarget", Target], "")).Message.ShouldBe("unexpected argument '-SshTarget'");
        Should.Throw<NightlyUsageException>(() => AmServiceDeployOptions.Parse(["--ssh-target"], "")).Message.ShouldBe("missing value for --ssh-target");
        AmServiceDeployOptions.Usage.ShouldBe(
            "deploy-am-service --ssh-target <user@host> [--deploy] [--what-if] [--confirm | --no-confirm] [--skip-real-traffic-check] [--timeout-sec 180]");
    }

    // ---- C496: the explicit target ----

    [Test]
    public void C496_TargetRequired()
    {
        using (var world = new AmServiceWorld())
        {
            var run = world.Cli();
            run.Exit.ShouldBe(1, run.Output);
            run.Lines.ShouldBe(["REMOTE DEPLOY VERDICT: failed " + Required], "CLI configuration refusal");
            world.Events.ShouldBeEmpty("CLI zero HTTP/SSH/SCP/archive");
        }

        // Both boundaries (the command and one deploy pass): missing, empty and whitespace refuse before all I/O.
        var probes = 0;
        foreach (var value in new string?[] { null, "", " ", "\t\r\n" })
        {
            using (var world = new AmServiceWorld())
            {
                var run = value == null ? world.Cli() : world.Cli("--ssh-target", value);
                run.Exit.ShouldBe(1);
                run.Lines.ShouldBe(["REMOTE DEPLOY VERDICT: failed " + Required]);
                world.Events.ShouldBeEmpty();
                probes++;
            }
            using (var world = new AmServiceWorld())
            {
                Should.Throw<InvalidOperationException>(() => world.Deploy(true, target: value)).Message.ShouldBe(Required);
                world.Lines.ShouldBe(["REMOTE DEPLOY VERDICT: failed " + Required]);
                world.Events.ShouldBeEmpty("configuration refusal before all I/O");
                probes++;
            }
        }
        probes.ShouldBe(8);
    }

    [Test]
    public void C496_TargetValidation()
    {
        string[] invalid = ["-option", "-u@host", "user@-host", "user@host-", "user@a..b", "user@.host", "user@host.", "user@@host", "user@host:22", "user@host/path",
            "user@host\\path", "user host", "user@host name", "user@host;echo", "@host", "user@", "user@host_name", "user@[::1]", "user@host ", " user@host", "user@host\n",
            "user@ho\tst", "user@ho\0st"];
        string[] valid = ["operator@gateway", "a@b", "User_1.Name-2@Gateway.Example.Invalid", "operator@192.0.2.10", "_user@gateway-2.example.invalid"];
        var probes = 0;
        foreach (var value in invalid)
        {
            using (var world = new AmServiceWorld())
            {
                var run = world.Cli("--ssh-target", value);
                run.Exit.ShouldBe(1, value);
                run.Lines.ShouldBe(["REMOTE DEPLOY VERDICT: failed " + Invalid], value);
                world.Events.ShouldBeEmpty("invalid value refused before I/O via the command: " + value);
                probes++;
            }
            using (var world = new AmServiceWorld())
            {
                Should.Throw<InvalidOperationException>(() => world.Deploy(true, target: value)).Message.ShouldBe(Invalid, value);
                world.Events.ShouldBeEmpty("invalid value refused before I/O via one pass: " + value);
                probes++;
            }
        }
        foreach (var value in valid)
        {
            using (var world = new AmServiceWorld())
            {
                var run = world.Cli("--ssh-target", value);
                run.Exit.ShouldBe(0, run.Output);
                run.Lines[0].ShouldBe("Remote target: " + value + ":/home/mc/antiphon-messaging");
                world.SshDestinations().ShouldNotBeEmpty();
                world.SshDestinations().ShouldAllBe(destination => destination == value, "exact valid value " + value + " via the command");
                probes++;
            }
            using (var world = new AmServiceWorld())
            {
                world.Deploy(false, target: value).SshTarget.ShouldBe(value);
                world.SshDestinations().ShouldNotBeEmpty();
                world.SshDestinations().ShouldAllBe(destination => destination == value, "exact valid value " + value + " via one pass");
                probes++;
            }
        }
        probes.ShouldBe(56, "46 invalid and 10 valid probes");
    }

    [Test]
    public void C496_TargetRouting()
    {
        foreach (var target in new[] { "operator@first.example.invalid", "other@second.example.invalid" })
        {
            using var world = new AmServiceWorld();
            var preview = world.Deploy(false, target: target);
            var deployment = world.Deploy(true, target: target);
            var ssh = world.Launcher.Launches.Where(launch => launch.FileName == "ssh").ToList();
            var scp = world.Launcher.Launches.Where(launch => launch.FileName == "scp").ToList();
            ssh.ShouldNotBeEmpty();
            ssh.ShouldAllBe(launch => launch.Arguments.Count == 2 && launch.Arguments[0] == target, "every native SSH argv selects " + target);
            scp.Count.ShouldBe(1);
            scp[0].Arguments.Count.ShouldBe(2);
            scp[0].Arguments[1].ShouldMatch("^" + Regex.Escape(target) + @":/tmp/am-service-src-[a-f0-9]{32}\.tgz$", "SCP destination selects " + target);
            (preview.SshTarget, deployment.SshTarget).ShouldBe((target, target));
            world.Text.ShouldContain("Remote target: " + target + ":/home/mc/antiphon-messaging");
            var commands = string.Join("\n", world.Commands);
            commands.ShouldContain("/health/inbound-unconsumed");
            commands.ShouldContain("rpk group describe");
            commands.ShouldContain("rm -f -- '/tmp/am-service-src-");
        }
    }

    [Test]
    public void C496_WriteGate()
    {
        const string gate = "reviewer@gate.example.invalid";
        foreach (var variant in new[] { "default", "whatif", "deploy" })
        {
            using var world = new AmServiceWorld();
            string[] flags = variant switch { "whatif" => ["--deploy", "--what-if"], "deploy" => ["--deploy", "--no-confirm"], _ => [] };
            var run = world.Cli(["--ssh-target", gate, .. flags]);
            run.Exit.ShouldBe(0, run.Output);
            world.Commands.ShouldNotBeEmpty(variant + " public entry preflight");
            AssertWrites(world, variant == "deploy", variant);
            run.Lines[^1].ShouldBe("REMOTE DEPLOY VERDICT: ok");
            if (variant == "default")
                run.Lines.ShouldBe([.. PreviewLines(gate), "REMOTE DEPLOY VERDICT: ok", "REMOTE DEPLOY VERDICT: ok"]);
            if (variant == "whatif")
                run.Lines.ShouldBe([.. PreviewLines(gate), "REMOTE DEPLOY VERDICT: ok",
                    "What if: Performing the operation \"replace build/src and recreate am-service\" on target \"" + gate + ":/home/mc/antiphon-messaging\".",
                    "Deployment not approved; read-only preflight completed.", "REMOTE DEPLOY VERDICT: ok"]);
            if (variant == "deploy")
            {
                run.Lines.Count(line => line == "REMOTE DEPLOY VERDICT: ok").ShouldBe(3, "the preview's, the deploy pass's and the command's");
                run.Lines.Count(line => line.StartsWith("Remote target: ", StringComparison.Ordinal)).ShouldBe(2);
                run.Lines[^2].ShouldBe("REMOTE DEPLOY VERDICT: ok");
                run.Lines[^3].ShouldBe(AmServiceDeploy.RequiredTrafficLine);
            }
        }
    }

    // ---- the confirmation (the script's ShouldProcess with ConfirmImpact High) ----

    [Test]
    public void Deploy_asks_first_and_an_unread_answer_writes_nothing()
    {
        const string question = "[Y] Yes  [A] Yes to All  [N] No  [L] No to All  [?] Help (default is \"Y\"): ";
        foreach (var (answers, approved) in new (string?[] Answers, bool Approved)[] { ([""], true), (["y"], true), (["Yes to All"], true), (["?", "a"], true),
                     (["maybe", "Y"], true), (["n"], false), (["L"], false), (["no"], false) })
        {
            using var world = new AmServiceWorld();
            foreach (var answer in answers) world.Answers.Enqueue(answer);
            var run = world.Cli("--ssh-target", Target, "--deploy", answers[0] == "?" ? "--confirm" : "--skip-real-traffic-check");
            run.Exit.ShouldBe(0, run.Output);
            world.Answers.ShouldBeEmpty();
            run.Output.ShouldContain("REMOTE DEPLOY VERDICT: ok\n\nConfirm\nAre you sure you want to perform this action?\nPerforming the operation \"replace build/src and recreate am-service\" on target \""
                + Target + ":/home/mc/antiphon-messaging\".\n" + question);
            Regex.Matches(run.Output, Regex.Escape(question)).Count.ShouldBe(answers.Length);
            AssertWrites(world, approved, string.Join("|", answers));
            if (answers[0] == "?") run.Output.ShouldContain("Y - Continue with only the next step of the operation.\nA - Continue with all the steps of the operation.\n");
            // The answer's echo is the console's, so the next text follows the question on a redirected input.
            if (!approved) run.Output.ShouldEndWith(question + AmServiceDeploy.NotApprovedLine + "\nREMOTE DEPLOY VERDICT: ok\n");
            else run.Output.ShouldContain(question + "Remote target: " + Target + ":/home/mc/antiphon-messaging\n");
        }

        // No answer to read (an agent's shell): the command fails and nothing is written, as -NonInteractive pwsh refused the prompt.
        using (var world = new AmServiceWorld())
        {
            var run = world.Cli("--ssh-target", Target, "--deploy");
            run.Exit.ShouldBe(1, run.Output);
            run.Lines[^1].ShouldBe("REMOTE DEPLOY VERDICT: failed The deployment confirmation could not be read (no interactive input); nothing was written. "
                + "Answer the question on a console, or pass --no-confirm only when the deployment is explicitly authorized.");
            AssertWrites(world, false, "unread answer");
        }

        // -WhatIf wins over -Confirm:$false; --confirm without --deploy asks nothing.
        using (var world = new AmServiceWorld())
        {
            var run = world.Cli("--ssh-target", Target, "--deploy", "--what-if", "--no-confirm");
            run.Exit.ShouldBe(0, run.Output);
            run.Output.ShouldNotContain("Confirm");
            run.Lines[^2].ShouldBe(AmServiceDeploy.NotApprovedLine);
            AssertWrites(world, false, "what-if wins");
        }
        using (var world = new AmServiceWorld())
        {
            var run = world.Cli("--ssh-target", Target, "--confirm", "--what-if");
            run.Exit.ShouldBe(0, run.Output);
            run.Lines.ShouldBe([.. PreviewLines(Target), "REMOTE DEPLOY VERDICT: ok", "REMOTE DEPLOY VERDICT: ok"]);
        }
    }

    [Test]
    public void C496_FixedLayout()
    {
        using (var world = new AmServiceWorld())
        {
            var result = world.Deploy(true);
            (result.RemoteContext, result.RemoteDockerfile, result.OverridePath)
                .ShouldBe(("/home/mc/antiphon-messaging/build/src", "Antiphon.Messaging.Service/Dockerfile", "/home/mc/antiphon-messaging/docker-compose.override.yml"));
            result.BackupPath.ShouldStartWith("/home/mc/antiphon-messaging/build/src.bak-");
            result.MigrationCount.ShouldBeGreaterThan(0);
            result.AdapterNames.ShouldBe(["telegram", "slack"]);
            result.RunningContainer.ShouldBe("bbbbbbbbbbbb sha256:bbbbbbbbbbbb running");
            result.PreviousContainer.ShouldBe("aaaaaaaaaaaa sha256:aaaaaaaaaaaa running");
            result.ComposeStatus.ShouldBe("running");
            world.Text.ShouldNotContain(Secret, Case.Sensitive, "safe redacted projections");
            AssertWrites(world, true, "fixed layout");
        }
        foreach (var variant in new[] { "wrong-context", "wrong-dockerfile" })
        {
            using var world = new AmServiceWorld { Variant = variant };
            Should.Throw<InvalidOperationException>(() => world.Deploy(true)).Message.ShouldBe("Remote Compose build contract changed.", variant);
            AssertWrites(world, false, variant);
        }
    }

    // ---- the repository, the parser and the archive (the driver's legacy block) ----

    [Test]
    public void Repository_profiles_agree_on_the_server_inbound_group()
    {
        using var server = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "server", "appsettings.json")));
        using var service = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "src", "Antiphon.Messaging.Service", "appsettings.json")));
        var group = server.RootElement.GetProperty("AntiphonMessaging").GetProperty("ConsumerGroup").GetString();
        var kafka = service.RootElement.GetProperty("Kafka");
        group.ShouldBe("antiphon-server-bridge");
        kafka.GetProperty("AntiphonConsumerGroup").GetString().ShouldBe(group);
        kafka.GetProperty("ExpectedAntiphonConsumerGroup").GetString().ShouldBe(group);
        kafka.GetProperty("ConsumerGroup").GetString().ShouldNotBe(group);
    }

    [Test]
    public void Json_output_helper_reads_the_object_through_rpk_banner_noise()
    {
        var noisy = string.Join("\n", "WARNING: This is a Redpanda Enterprise feature. Requires a license.", "CLUSTER", "=======",
            "{\"cluster_name\":\"" + AmServiceWorld.RealisticCluster + "\"}");
        AmServiceDeploy.ParseJsonOutput(noisy)!["cluster_name"]!.GetValue<string>().ShouldBe(AmServiceWorld.RealisticCluster);
        Should.Throw<InvalidOperationException>(() => AmServiceDeploy.ParseJsonOutput("WARNING: This is a Redpanda Enterprise feature. Requires a license."))
            .Message.ShouldBe("JSON output was not parseable.");
        Should.Throw<InvalidOperationException>(() => AmServiceDeploy.ParseJsonOutput("")).Message.ShouldBe("JSON output was empty.");
        Should.Throw<InvalidOperationException>(() => AmServiceDeploy.ParseJsonOutput(" \n ")).Message.ShouldBe("JSON output was empty.");
    }

    [Test]
    public void Broker_identity_execs_the_compose_service_and_refuses_garbage()
    {
        using (var world = new AmServiceWorld())
        {
            AmServiceDeploy.AssertBrokerIdentity(world.Host, world.Seams, Target, "am-redpanda:9092", "am-redpanda:9092");
            var commands = string.Join("\n", world.Commands);
            commands.ShouldContain("docker compose exec -T redpanda rpk cluster info");
            commands.ShouldNotContain("docker compose exec -T am-redpanda");
            commands.ShouldContain("2>/dev/null");
            world.Commands.ShouldBe([AmServiceDeploy.BrokerProbeCommand("am-redpanda:9092"), AmServiceDeploy.BrokerProbeCommand("am-redpanda:9092")]);
            world.Text.ShouldBe("Broker listener metadata identifies the same cluster.\n");
        }
        foreach (var (cluster, accepted) in new[] { ("...", false), ("redpanda.", false), ("c0410 cluster", false), ("c0410-cluster", true) })
        {
            using var world = new AmServiceWorld { Cluster = cluster };
            if (accepted) AmServiceDeploy.AssertBrokerIdentity(world.Host, world.Seams, Target, "am-redpanda:9092", "am-redpanda:9092");
            else Should.Throw<InvalidOperationException>(() => AmServiceDeploy.AssertBrokerIdentity(world.Host, world.Seams, Target, "am-redpanda:9092", "am-redpanda:9092"))
                .Message.ShouldBe("Broker metadata has no cluster identity.", cluster);
        }
    }

    [Test]
    public void Dockerfile_parser_handles_copy_comments_continuations_order_and_dedup()
    {
        using var fixture = new DockerfileFixture();
        fixture.Write("# comment\nCOPY one/ ./one/\nCOPY two/ \\\n three/ ./target/\nCOPY --from=build /app ./\nCOPY one/ ./again/");
        var manifest = AmServiceDeploy.Manifest(new FileSystem(), fixture.Dockerfile, fixture.Root);
        manifest.Select(entry => entry.Path).ShouldBe(["one", "two", "three"], "parser handles shell COPY, comments, continuations, order, and de-duplication");
        manifest.Select(entry => entry.Line).ShouldBe([2, 3, 3]);
        AmServiceDeploy.LogicalLines(new FileSystem(), fixture.Dockerfile).Select(line => (line.Line, line.Text))
            .ShouldBe([(2, "COPY one/ ./one/"), (3, "COPY two/ three/ ./target/"), (5, "COPY --from=build /app ./"), (6, "COPY one/ ./again/")]);
        // A lower-case keyword, CRLF lines, a trailing slash de-duplicated and an indented continuation.
        fixture.Write("copy one/ ./a/\r\nCOPY one ./b/\r\nCOPY two \\\r\n   three ./c/\r\n");
        var crlf = AmServiceDeploy.Manifest(new FileSystem(), fixture.Dockerfile, fixture.Root);
        crlf.Select(entry => (entry.Path, entry.Line)).ShouldBe([("one", 1), ("two", 3), ("three", 3)]);
        // A blank line ends a continuation: "COPY two" alone has no destination.
        fixture.Write("COPY two \\\n\nCOPY three ./c/\n");
        Should.Throw<InvalidOperationException>(() => AmServiceDeploy.Manifest(new FileSystem(), fixture.Dockerfile, fixture.Root))
            .Message.ShouldBe("Dockerfile COPY line 1 needs a source and destination.");
        fixture.Write("COPY one/ \\\n");
        Should.Throw<InvalidOperationException>(() => AmServiceDeploy.Manifest(new FileSystem(), fixture.Dockerfile, fixture.Root))
            .Message.ShouldBe("Dockerfile line 1 has an unterminated continuation.");
        fixture.Write("FROM scratch\nCOPY --from=build /app ./\n");
        Should.Throw<InvalidOperationException>(() => AmServiceDeploy.Manifest(new FileSystem(), fixture.Dockerfile, fixture.Root))
            .Message.ShouldBe("No local Dockerfile COPY sources were found.");
        Should.Throw<InvalidOperationException>(() => AmServiceDeploy.Manifest(new FileSystem(), Path.Combine(fixture.Root, "absent"), fixture.Root))
            .Message.ShouldBe("Dockerfile not found: " + Path.Combine(fixture.Root, "absent"));
    }

    [Test]
    [Arguments("json", "COPY [\"one\", \"./\"]", "Dockerfile COPY line 1 uses unsupported JSON-array syntax.")]
    [Arguments("variable", "COPY ${SOURCE} ./", "Dockerfile COPY line 1 has unsafe variable or glob '${SOURCE}'.")]
    [Arguments("glob", "COPY one/* ./", "Dockerfile COPY line 1 has unsafe variable or glob 'one/*'.")]
    [Arguments("absolute", "COPY /one ./", "Dockerfile COPY line 1 has absolute source '/one'.")]
    [Arguments("drive", "COPY C:one ./", "Dockerfile COPY line 1 has absolute source 'C:one'.")]
    [Arguments("parent", "COPY ../one ./", "Dockerfile COPY line 1 has parent traversal '../one'.")]
    [Arguments("option", "COPY --chown=1000 one ./", "Dockerfile COPY line 1 has unrecognised option '--chown=1000'.")]
    [Arguments("missing", "COPY absent ./", "Dockerfile COPY line 1 source does not exist: 'absent'.")]
    [Arguments("destination", "COPY one", "Dockerfile COPY line 1 needs a source and destination.")]
    public void Unsafe_copy_syntax_refuses_before_any_archive(string name, string dockerfile, string message)
    {
        using var fixture = new DockerfileFixture();
        fixture.Write(dockerfile);
        Should.Throw<InvalidOperationException>(() => AmServiceDeploy.Manifest(new FileSystem(), fixture.Dockerfile, fixture.Root)).Message.ShouldBe(message, name);
    }

    [Test]
    public void Real_dockerfile_derives_existing_sources()
    {
        var manifest = AmServiceDeploy.Manifest(new FileSystem(), Path.Combine(Root, "src", "Antiphon.Messaging.Service", "Dockerfile"), Path.Combine(Root, "src"));
        manifest.ShouldNotBeEmpty("real Dockerfile derives source entries");
        manifest.Where(entry => !File.Exists(Path.Combine(Root, "src", entry.Path)) && !Directory.Exists(Path.Combine(Root, "src", entry.Path)))
            .ShouldBeEmpty("every real Dockerfile-derived source exists");
        manifest.Select(entry => entry.Path).ShouldContain("Antiphon.Messaging.Service");
    }

    [Test]
    public void The_archive_is_listed_and_checked_and_a_bad_one_is_removed()
    {
        using var world = new AmServiceWorld();
        var manifest = world.Manifest;
        var archive = AmServiceDeploy.CreateArchive(world.Host, new FileSystem(), world.Seams, Path.Combine(Root, "src"), manifest);
        archive.Entries.ShouldBe(manifest.Select(entry => entry.Path));
        Path.GetDirectoryName(archive.Path).ShouldBe(world.Temp);
        File.Exists(archive.Path).ShouldBeTrue();
        var tar = world.Launcher.Launches.Where(launch => launch.FileName == "tar").ToList();
        tar[0].Arguments.ShouldBe(["-czf", Path.GetFileName(archive.Path), "--exclude=bin", "--exclude=*/bin", "--exclude=obj", "--exclude=*/obj", "--exclude=bin-*",
            "--exclude=*/bin-*", "-C", Path.Combine(Root, "src"), .. manifest.Select(entry => entry.Path)]);
        tar[1].Arguments.ShouldBe(["-tzf", Path.GetFileName(archive.Path)]);
        tar.ShouldAllBe(launch => launch.WorkingDirectory == world.Temp && launch.CaptureOutput, "a bare name in the temporary directory: no colon for GNU tar");

        // The listing's "./" prefixes are trimmed; a bin, obj or bin-* segment at any depth, any case, refuses; so does a missing source.
        foreach (var (listing, message) in new[] {
            (new[] { "./Messaging.Pack.props", "./Antiphon.Messaging/", "./Antiphon.Messaging/BIN/x.dll" }, "Deployment archive contains forbidden build output: Antiphon.Messaging/BIN/x.dll"),
            (new[] { "Antiphon.Messaging/obj/" }, "Deployment archive contains forbidden build output: Antiphon.Messaging/obj/"),
            (new[] { "Antiphon.Messaging/bin-release/a" }, "Deployment archive contains forbidden build output: Antiphon.Messaging/bin-release/a"),
            (manifest.Skip(1).Select(entry => "./" + entry.Path + "/").ToArray(), "Deployment archive is missing COPY source '" + manifest[0].Path + "' from line " + manifest[0].Line + ".") })
        {
            world.TarListing = listing;
            var before = Directory.GetFiles(world.Temp).Length;
            Should.Throw<InvalidOperationException>(() => AmServiceDeploy.CreateArchive(world.Host, new FileSystem(), world.Seams, Path.Combine(Root, "src"), manifest))
                .Message.ShouldBe(message);
            Directory.GetFiles(world.Temp).Length.ShouldBe(before, "the refused archive is removed");
        }
        world.TarListing = null;
        world.TarCreateExit = 2;
        Should.Throw<InvalidOperationException>(() => AmServiceDeploy.CreateArchive(world.Host, new FileSystem(), world.Seams, Path.Combine(Root, "src"), manifest))
            .Message.ShouldBe("Could not create deployment archive (tar exit 2).");
        world.TarCreateExit = 0;
        world.TarListExit = 3;
        Should.Throw<InvalidOperationException>(() => AmServiceDeploy.CreateArchive(world.Host, new FileSystem(), world.Seams, Path.Combine(Root, "src"), manifest))
            .Message.ShouldBe("Could not list deployment archive (tar exit 3).");
        Directory.GetFiles(world.Temp).ShouldBe([archive.Path]);
    }

    [Test]
    public void Migration_ids_skip_designers_snapshots_and_hidden_files()
    {
        using var fixture = new DockerfileFixture();
        var directory = Directory.CreateDirectory(Path.Combine(fixture.Root, "Migrations")).FullName;
        foreach (var name in new[] { "20260102000000_B.cs", "20260101000000_A.cs", "20260101000000_A.Designer.cs", "MessagingDbContextModelSnapshot.cs", "notes.txt" })
            File.WriteAllText(Path.Combine(directory, name), "");
        AmServiceDeploy.MigrationIds(new FileSystem(), directory).ShouldBe(["20260101000000_A", "20260102000000_B"]);
        Should.Throw<InvalidOperationException>(() => AmServiceDeploy.MigrationIds(new FileSystem(), Path.Combine(fixture.Root, "absent")))
            .Message.ShouldBe("Migration directory not found: " + Path.Combine(fixture.Root, "absent"));
    }

    // ---- the remote seam (the driver's preflight, deploy and refusal cases) ----

    [Test]
    public void Preflight_reads_without_writing_and_narrows_output()
    {
        using var world = new AmServiceWorld();
        var result = world.Deploy(false, skip: false);
        world.Launcher.Launches.ShouldNotContain(launch => launch.FileName == "scp", "remote seam makes no SCP call without --deploy");
        world.Launcher.Launches.ShouldNotContain(launch => launch.FileName == "tar");
        string.Join("\n", world.Commands).ShouldNotMatch("tar xzf|docker compose build|docker compose up|tee |cat >|printf .*> ", "remote seam makes no write command without --deploy");
        world.Text.ShouldNotContain(Secret, Case.Sensitive, "remote seam narrows Compose output before logging");
        foreach (var line in new[] { "repository default: antiphon-server-bridge", "server: antiphon-server-bridge", "gateway watched: antiphon-consumer",
                     "proposed: Kafka__AntiphonConsumerGroup=antiphon-server-bridge Kafka__ExpectedAntiphonConsumerGroup=antiphon-server-bridge",
                     "outbound group unchanged: antiphon-messaging-service" })
            world.Text.ShouldContain(line, Case.Sensitive, "preflight reads server identity and gateway settings without writing");
        world.Lines.ShouldBe([.. PreviewLines(Target), "REMOTE DEPLOY VERDICT: ok"]);
        (result.BackupPath, result.OverrideBackupPath, result.RunningContainer, result.MigrationCount).ShouldBe(((string?)null, (string?)null, (string?)null, 0));
        result.AdapterNames.ShouldBeEmpty();
        result.DockerfileHash.ShouldBe(AmServiceWorld.DockerfileHash);
        result.Manifest.ShouldBe(world.Manifest);
    }

    [Test]
    public void A_failed_build_keeps_the_backup_and_hides_remote_output()
    {
        using var world = new AmServiceWorld { FailBuild = true };
        var failure = Should.Throw<InvalidOperationException>(() => world.Deploy(true)).Message;
        world.Launcher.Launches.Count(launch => launch.FileName == "scp").ShouldBe(1, "remote seam uploads only with --deploy");
        failure.ShouldMatch(@"retained source backup: /home/mc/antiphon-messaging/build/src\.bak-", "remote seam retains backup evidence after remote failure");
        failure.ShouldBe("Remote replacement/build/recreate failed; retained source backup: /home/mc/antiphon-messaging/build/src.bak-20261125T001213Z-"
            + AmServiceWorld.Token(3)[..8] + "; staging path: /home/mc/antiphon-messaging/build/.src-stage-" + AmServiceWorld.Token(2)
            + ". remote archive replacement/build/recreate failed (exit 9).");
        failure.ShouldNotContain(Secret, Case.Sensitive, "remote seam failure diagnostics do not leak remote output");
        world.Text.ShouldNotContain(Secret, Case.Sensitive);
        world.Lines[^1].ShouldBe("REMOTE DEPLOY VERDICT: failed " + failure);
        world.Commands[^1].ShouldBe("rm -f -- '/tmp/am-service-src-" + AmServiceWorld.Token(1) + ".tgz'", "the transient upload is removed after a failure too");
        Directory.GetFiles(world.Temp).ShouldBeEmpty("the local archive is removed");
    }

    [Test]
    public void A_deploy_recreates_and_verifies()
    {
        using var world = new AmServiceWorld();
        var success = world.Deploy(true);
        success.AdapterNames.ShouldBe(["telegram", "slack"], "remote seam parses endpoint adapters after recreate");
        var all = string.Join("\n", world.Commands);
        all.ShouldContain("docker compose build messaging-service", Case.Sensitive, "remote seam issues fixed-target build and recreate sequence");
        all.ShouldContain("docker compose up -d --no-deps messaging-service");
        all.ShouldContain("rm -f -- '/tmp/am-service-src-", Case.Sensitive, "remote seam removes transient upload after handled deployment");
        world.Commands.Single(command => command.Contains("http://localhost:18090/api/channels", StringComparison.Ordinal))
            .ShouldContain("cd '/home/mc/antiphon-messaging'", Case.Sensitive, "technical verification uses the deployment Compose directory");
        var broker = string.Join("\n", world.Commands.Where(command => Regex.IsMatch(command, "rpk cluster info|rpk group describe")));
        broker.ShouldContain("docker compose exec -T redpanda rpk cluster info");
        broker.ShouldContain("docker compose exec -T redpanda rpk group describe");
        broker.ShouldNotContain("docker compose exec -T am-redpanda", Case.Sensitive, "deploy broker probes use Compose service name redpanda");
        Directory.GetFiles(world.Temp).ShouldBeEmpty("the local archive is removed");
        success.MigrationCount.ShouldBe(world.Migrations.Count);
        world.Text.ShouldContain("Migration comparison verified: " + world.Migrations.Count + " source, " + world.Migrations.Count + " recorded.");
    }

    [Test]
    public void Preflight_accepts_rpk_noise_and_an_undotted_cluster()
    {
        foreach (var @case in new[] { "rpk-noise", "legacy-cluster" })
        {
            using var world = new AmServiceWorld { Case = @case };
            world.Deploy(false, skip: false);
            world.Launcher.Launches.ShouldNotContain(launch => launch.FileName == "scp");
            world.Text.ShouldContain("Broker listener metadata identifies the same cluster.", Case.Sensitive, @case);
        }
    }

    [Test]
    [Arguments("404", "Server identity endpoint unavailable (requires HTTP 200).")]
    [Arguments("disabled", "Server identity reports disabled bridge.")]
    [Arguments("topic", "Inbound topic mismatch between server and gateway.")]
    [Arguments("rename", "Server identity change between preview and write refuses deployment.")]
    [Arguments("unmarked", "Unmarked existing docker-compose.override.yml refuses before any write.")]
    [Arguments("ambiguous-compose", "Compose base/override ambiguity or unreadable override refuses deployment.")]
    [Arguments("broker-mismatch", "Server/gateway broker cluster identity mismatch.")]
    [Arguments("invalid-cluster", "Broker metadata has no cluster identity.")]
    [Arguments("http-down", "Server identity endpoint unavailable.")]
    [Arguments("no-brokers", "Server identity has no broker addresses.")]
    [Arguments("bad-broker", "Server identity has invalid broker host/port.")]
    [Arguments("bad-group", "Server identity has invalid consumerGroup.")]
    [Arguments("same-group", "Server inbound group equals gateway outbound group.")]
    [Arguments("bad-listener", "Gateway broker listener is invalid.")]
    [Arguments("no-version", "Server version provenance unavailable.")]
    [Arguments("bad-facts", "Invalid container/image/status projection.")]
    public void Refusals_write_nothing(string @case, string message)
    {
        using var world = new AmServiceWorld { Case = @case };
        Should.Throw<InvalidOperationException>(() => world.Deploy(true)).Message.ShouldBe(message, @case);
        world.Launcher.Launches.ShouldNotContain(launch => launch.FileName == "scp" || launch.FileName == "tar", @case + " without writes");
        string.Join("\n", world.Commands).ShouldNotMatch("cat >|tar xzf|docker compose up", @case + " without writes");
        world.Text.ShouldNotContain(Secret, Case.Sensitive);
        world.Lines[^1].ShouldBe("REMOTE DEPLOY VERDICT: failed " + message);
    }

    [Test]
    public void A_server_override_is_shown_and_used()
    {
        using var world = new AmServiceWorld { Case = "override" };
        world.Deploy(false, skip: false);
        world.Text.ShouldContain("server: family-bridge-custom\nserver override (differs from repository default antiphon-server-bridge)\n");
        world.Text.ShouldContain("proposed: Kafka__AntiphonConsumerGroup=family-bridge-custom Kafka__ExpectedAntiphonConsumerGroup=family-bridge-custom");
    }

    [Test]
    public void The_deploy_writes_the_managed_override_and_verifies_the_merge_before_recreate()
    {
        using (var world = new AmServiceWorld())
        {
            var success = world.Deploy(true);
            var all = string.Join("\n", world.Commands);
            var written = world.Commands.Where(command => command.Contains("C0410_MANAGED", StringComparison.Ordinal)).ToList();
            var write = all.IndexOf("cat >", StringComparison.Ordinal);
            var merge = all.IndexOf("merged=\"merged\"", StringComparison.Ordinal);
            var up = all.IndexOf("docker compose up -d --no-deps messaging-service", StringComparison.Ordinal);
            written.Count.ShouldBe(1);
            write.ShouldBeGreaterThanOrEqualTo(0);
            merge.ShouldBeGreaterThan(write, "merged config after the persistent override write");
            up.ShouldBeGreaterThan(merge, "then the ordinary compose up");
            all.ShouldNotContain("docker compose -f");
            success.OverridePath.ShouldBe("/home/mc/antiphon-messaging/docker-compose.override.yml");
            success.OverrideBackupPath.ShouldBeNull();
            Regex.Matches(written[0], "Kafka__").Count.ShouldBe(2, "managed override has exactly the two environment keys");
            written[0].ShouldContain("# managed-by: scripts/deploy-am-service.ps1 CARD-0410 - do not hand-edit");
            written[0].ShouldNotContain("cp --");
        }
        using (var world = new AmServiceWorld { Case = "marked" })
        {
            var success = world.Deploy(true);
            var all = string.Join("\n", world.Commands);
            success.OverrideBackupPath.ShouldBe("/home/mc/antiphon-messaging/docker-compose.override.yml.bak-20261125T001213Z-" + AmServiceWorld.Token(5)[..8]);
            all.IndexOf("cp --", StringComparison.Ordinal).ShouldBeLessThan(all.IndexOf("cat >", StringComparison.Ordinal), "marked existing override is backed up first");
        }
    }

    [Test]
    [Arguments("degraded", "Monitor readiness requires HTTP 200 (missing/degraded route).")]
    [Arguments("readiness-404", "Monitor readiness requires HTTP 200 (missing/degraded route).")]
    [Arguments("wrong-group", "Monitor readiness group mismatch.")]
    [Arguments("stale", "Monitor readiness observation is stale or missing ageSeconds.")]
    [Arguments("no-commit", "Monitor readiness contains unknown offsets.")]
    [Arguments("missing-merged", "Merged docker compose config lacks matching watched/expected groups.")]
    [Arguments("no-numeric", "Broker group describe lacks numeric current offsets for a monitored partition.")]
    [Arguments("behind", "Broker sampled offset is behind monitor evidence.")]
    [Arguments("same-image", "Running image ID did not change from PreviousContainer.")]
    [Arguments("missing-migration", "Remote am-postgres migration history is missing: ")]
    [Arguments("verify-44", "HTTP readiness was not confirmed at /api/channels after five attempts (exit 44).")]
    [Arguments("verify-41", "remote technical verification failed (exit 41).")]
    [Arguments("bad-channels", "The am-service /api/channels response was not parseable channel JSON.")]
    [Arguments("rename-late", "Server identity changed during deployment verification.")]
    public void Postdeploy_verification_refuses(string @case, string message)
    {
        using var world = new AmServiceWorld { Case = @case };
        Should.Throw<InvalidOperationException>(() => world.Deploy(true)).Message.ShouldStartWith(message, Case.Sensitive, @case);
        string.Join("\n", world.Commands).ShouldNotMatch("rpk topic consume|--group |kafka-console-consumer", "verification never consumes from a group");
        // The merge is checked before the upload; after an upload the transient archive is always removed.
        if (world.Events.Contains("scp")) world.Commands[^1].ShouldStartWith("rm -f -- '/tmp/am-service-src-", Case.Sensitive, "the transient upload is removed");
        else world.Commands.ShouldNotContain(command => command.StartsWith("rm -f -- ", StringComparison.Ordinal));
        (@case == "missing-merged").ShouldBe(!world.Events.Contains("scp"), @case);
        Directory.GetFiles(world.Temp).ShouldBeEmpty();
    }

    [Test]
    public void A_failed_upload_cleanup_is_a_warning_before_the_verdict()
    {
        using var world = new AmServiceWorld { Case = "cleanup-fails" };
        world.Deploy(true);
        world.Lines[^2..].ShouldBe(["WARNING: remote transient archive cleanup could not be confirmed.", "REMOTE DEPLOY VERDICT: ok"]);
    }

    [Test]
    public void Nothing_secret_leaks_and_the_traffic_check_stays_manual()
    {
        using (var world = new AmServiceWorld())
        {
            world.Deploy(true, skip: true);
            var written = string.Join("\n", world.Commands.Where(command => command.Contains("C0410_MANAGED", StringComparison.Ordinal)));
            world.Text.ShouldNotContain(Secret);
            written.ShouldNotContain(Secret, Case.Sensitive, "identity, compose, and container output never leak");
            world.Lines.ShouldContain(AmServiceDeploy.SkippedTrafficLine);
            AmServiceDeploy.SkippedTrafficLine.ShouldBe("Real traffic check explicitly skipped; the round-trip check is manual and operator-initiated through the Family "
                + "conversation, and an agent never sends test traffic to it.");
            foreach (var retired in new[] { "Antiphon-Family", "test group", "test-group" }) world.Text.ShouldNotContain(retired, Case.Insensitive, "CARD-1261 skipped");
        }
        using (var world = new AmServiceWorld())
        {
            world.Deploy(true, skip: false);
            world.Lines.ShouldContain(AmServiceDeploy.RequiredTrafficLine);
            AmServiceDeploy.RequiredTrafficLine.ShouldBe("Real traffic check remains required: the round-trip check is manual and operator-initiated through the Family "
                + "conversation, and an agent never sends test traffic to it.");
            foreach (var retired in new[] { "Antiphon-Family", "test group", "test-group" }) world.Text.ShouldNotContain(retired, Case.Insensitive, "CARD-1261 required");
        }
    }

    [Test]
    public void The_deploy_source_stays_ascii_and_names_no_retired_destination()
    {
        foreach (var file in new[] { "scripts/lib/Antiphon.Nightly/AmServiceDeploy.cs", "scripts/lib/Antiphon.Scripts/DeployCommands.cs" })
        {
            var bytes = File.ReadAllBytes(Path.Combine(Root, file));
            bytes.ShouldAllBe(b => b < 128, file + " is ASCII only");
            var text = System.Text.Encoding.ASCII.GetString(bytes);
            text.ShouldNotContain("Antiphon-Family", Case.Sensitive, "CARD-1261: " + file);
            Regex.IsMatch(text, @"-?\d{10}").ShouldBeFalse("CARD-1261: no chat id in " + file);
        }
        File.Exists(Path.Combine(Root, "scripts", "deploy-am-service.ps1")).ShouldBeFalse();
        File.Exists(Path.Combine(Root, "scripts", "test-deploy-am-service.ps1")).ShouldBeFalse();
        File.Exists(Path.Combine(Root, "scripts", "test-deploy-am-service-readiness.ps1")).ShouldBeFalse();
    }

    [Test]
    public void Compose_yaml_uses_its_ordinary_override_name()
    {
        using var world = new AmServiceWorld { Case = "compose-yaml" };
        world.Deploy(false, skip: false).OverridePath.ShouldBe("/home/mc/antiphon-messaging/compose.override.yaml");
    }

    [Test]
    public void A_missing_local_tool_refuses_before_the_manifest()
    {
        foreach (var tool in new[] { "ssh", "scp", "tar" })
        {
            using var world = new AmServiceWorld { Missing = tool };
            Should.Throw<InvalidOperationException>(() => world.Deploy(true)).Message.ShouldBe("Required local command not found: " + tool);
            world.Commands.ShouldNotContain(AmServiceDeploy.ProjectionCommand);
            world.Text.ShouldNotContain("Dockerfile SHA-256");
        }
    }

    [Test]
    public void The_task_token_reaches_the_server_and_is_never_printed()
    {
        using (var world = new AmServiceWorld())
        {
            world.Variables["ANTIPHON_TASK_TOKEN"] = "fixture-task-token";
            world.Deploy(true);
            world.HttpCalls.Select(call => call.Url).ShouldBe(["http://localhost:17202/api/channels/consumer", "http://localhost:17202/api/version",
                "http://localhost:17202/api/channels/consumer", "http://localhost:17202/api/channels/consumer"]);
            world.HttpCalls.ShouldAllBe(call => call.Headers.Count == 1 && call.Headers["X-Antiphon-Task-Token"] == "fixture-task-token");
            world.Text.ShouldNotContain("fixture-task-token");
            world.Launcher.Launches.ShouldAllBe(launch => !string.Join(" ", launch.Arguments).Contains("fixture-task-token"));
        }
        using (var world = new AmServiceWorld())
        {
            world.Variables["ANTIPHON_API"] = "http://127.0.0.1:9/base/";
            world.Deploy(false);
            world.HttpCalls.Select(call => call.Url).ShouldBe(["http://127.0.0.1:9/base/api/channels/consumer", "http://127.0.0.1:9/base/api/version"]);
            world.HttpCalls.ShouldAllBe(call => call.Headers.Count == 0);
        }
    }

    // ---- parity: the exact commands and lines deploy-am-service.ps1 sent and printed (captured from it before its deletion) ----

    [Test]
    public void The_remote_commands_and_lines_are_the_scripts()
    {
        using var world = new AmServiceWorld { Case = "marked" };
        var result = world.Deploy(true);
        var checks = string.Join("\n", world.Manifest.Select(entry => "test -e '/home/mc/antiphon-messaging/build/.src-stage-{STAGE}/" + entry.Path + "'"));
        string Fill(string text) => text.ReplaceLineEndings("\n").Replace("{CHECKS}", checks).Replace("{UPLOAD}", AmServiceWorld.Token(1))
            .Replace("{STAGE}", AmServiceWorld.Token(2)).Replace("{BACKUP8}", AmServiceWorld.Token(3)[..8]).Replace("{OVERRIDE8}", AmServiceWorld.Token(5)[..8])
            .Replace("{STAMP}", "20261125T001213Z").Replace("{HASH}", AmServiceWorld.DockerfileHash);
        world.Commands.ShouldBe([.. new[] { PsGatewayEffective, PsOverride, PsBrokerProbe, PsBrokerProbe, PsProjection, PsFacts, PsPersist, PsGatewayMerged, PsReplace,
            PsVerify, PsMonitor, PsGroupDescribe, PsRunning, PsCleanup }.Select(Fill)]);
        world.Commands[0].ShouldBe(AmServiceDeploy.GatewaySettingsCommand(false));
        world.Commands[9].ShouldBe(AmServiceDeploy.VerifyScript(10));

        var archive = Path.Combine(world.Temp, "am-service-src-" + AmServiceWorld.Token(4) + ".tgz");
        world.Launcher.Launches.Where(launch => launch.FileName == "scp").Single().Arguments
            .ShouldBe([archive, Target + ":/tmp/am-service-src-" + AmServiceWorld.Token(1) + ".tgz"]);
        world.Launcher.Launches.Select(launch => launch.FileName).ShouldBe([.. Enumerable.Repeat("ssh", 6), "tar", "tar", "ssh", "ssh", "scp", .. Enumerable.Repeat("ssh", 6)]);

        var manifest = string.Join(", ", world.Manifest.Select(entry => entry.Path));
        world.Lines.ShouldBe([
            .. PreviewLines(Target, expected: ""),
            "Validated deployment archive: " + world.Manifest.Count + " paths; bin/obj/bin-* excluded.",
            "Managed override: /home/mc/antiphon-messaging/docker-compose.override.yml; retained override backup: /home/mc/antiphon-messaging/docker-compose.override.yml.bak-20261125T001213Z-"
                + AmServiceWorld.Token(5)[..8],
            "Retained source backup: /home/mc/antiphon-messaging/build/src.bak-20261125T001213Z-" + AmServiceWorld.Token(3)[..8],
            "Monitor build marker verified: readiness route Ready; sampled offsets numeric; ageSeconds=12.",
            "Running am-service container/image/status: bbbbbbbbbbbb sha256:bbbbbbbbbbbb running",
            "Endpoint verified: 2 registered adapter(s): telegram, slack",
            "Migration comparison verified: " + world.Migrations.Count + " source, " + world.Migrations.Count + " recorded.",
            "Startup-log scan verified: last 100 lines, no startup exception (redacted scan).",
            AmServiceDeploy.SkippedTrafficLine,
            "REMOTE DEPLOY VERDICT: ok"]);
        manifest.ShouldNotBeEmpty();
        result.BackupPath.ShouldBe("/home/mc/antiphon-messaging/build/src.bak-20261125T001213Z-" + AmServiceWorld.Token(3)[..8]);
    }

    // The preview pass's lines for the world's answers (gateway expected group unset before the managed override).
    private static string[] PreviewLines(string target, string expected = "") =>
    [
        "Remote target: " + target + ":/home/mc/antiphon-messaging",
        "Broker listener metadata identifies the same cluster.",
        "repository default: antiphon-server-bridge",
        "server: antiphon-server-bridge",
        "gateway watched: antiphon-consumer; expected: " + expected,
        "proposed: Kafka__AntiphonConsumerGroup=antiphon-server-bridge Kafka__ExpectedAntiphonConsumerGroup=antiphon-server-bridge",
        "outbound group unchanged: antiphon-messaging-service",
        "Server version provenance: 0123456789012345678901234567890123456789",
        "Dockerfile SHA-256: " + AmServiceWorld.DockerfileHash,
        "Dockerfile-derived source entries (" + AmServiceWorld.RepositoryManifest.Count + "): " + string.Join(", ", AmServiceWorld.RepositoryManifest.Select(entry => entry.Path)),
        "Remote Compose build projection: context=/home/mc/antiphon-messaging/build/src; dockerfile=Antiphon.Messaging.Service/Dockerfile",
        "Previous am-service container/image/status: aaaaaaaaaaaa sha256:aaaaaaaaaaaa running",
        "Previous Compose service status: running",
    ];

    // Assert-C496Writes: no upload, archive, override, extraction, build or recreate unless expected; when expected, the order
    // preflight facts, archive, override, merge, upload, build, recreate, with one upload and one archive create and list.
    private static void AssertWrites(AmServiceWorld world, bool expected, string name)
    {
        var events = world.Events;
        var writes = events.Where(e => e == "scp" || e.StartsWith("archive", StringComparison.Ordinal)
            || (e.StartsWith("ssh ", StringComparison.Ordinal) && Regex.IsMatch(e, "C0410_MANAGED|tar xzf|docker compose build|docker compose up"))).ToList();
        if (!expected)
        {
            writes.ShouldBeEmpty(name + " zero SCP/archive/override/build/recreate");
            return;
        }
        // Positions in the joined events, as the driver's IndexOf: the build and the recreate are one remote script.
        var joined = string.Join("\n", events);
        int At(string needle) => joined.IndexOf(needle, StringComparison.Ordinal);
        var facts = At("docker inspect");
        var archive = At("archive");
        var persist = At("C0410_MANAGED");
        var merge = At("merged=\"merged\"");
        var upload = At("scp");
        var build = At("docker compose build messaging-service");
        var up = At("docker compose up -d --no-deps messaging-service");
        (facts >= 0 && archive > facts && persist > archive && merge > persist && upload > merge && build > upload && up > build)
            .ShouldBeTrue(name + " preflight/archive/override/merge/SCP/build/recreate order: " + string.Join(" | ", events));
        events.Count(e => e == "scp").ShouldBe(1, name + " one upload");
        events.Count(e => e.StartsWith("archive", StringComparison.Ordinal)).ShouldBe(2, name + " archive create/list");
    }

    /// <summary>A scratch directory with three source folders and a Dockerfile, for the parser and the migrations.</summary>
    private sealed class DockerfileFixture : IDisposable
    {
        public DockerfileFixture()
        {
            Root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "am-service-deploy-" + Guid.NewGuid().ToString("N"))).FullName;
            foreach (var name in new[] { "one", "two", "three" })
            {
                Directory.CreateDirectory(Path.Combine(Root, name));
                File.WriteAllText(Path.Combine(Root, name, "file.txt"), name);
            }
        }

        public string Root { get; }

        public string Dockerfile => Path.Combine(Root, "Dockerfile");

        public void Write(string text) => File.WriteAllText(Dockerfile, text);

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    // ---- the remote commands as deploy-am-service.ps1 sent them (placeholders: the tokens, the stamp, the hash, the checks) ----

    private const string PsGatewayEffective = """
        cd /home/mc/antiphon-messaging && python3 - <<'C0410_GATEWAY_SETTINGS'
        import json,subprocess
        d=json.loads(subprocess.check_output(["docker","compose","config","--format","json"]))
        env=d["services"]["messaging-service"].get("environment",{})
        profile=json.loads(subprocess.check_output(["docker","exec","am-service","cat","/app/appsettings.json"])).get("Kafka",{})
        keys=["AntiphonConsumerGroup","ExpectedAntiphonConsumerGroup","InboundTopic","ConsumerGroup","BootstrapServers"]
        merged="effective" == "merged"
        print(json.dumps({k:env.get("Kafka__"+k,None if merged and k in keys[:2] else profile.get(k)) for k in keys}))
        C0410_GATEWAY_SETTINGS
        """;

    private const string PsOverride = """
        cd /home/mc/antiphon-messaging && python3 - <<'C0410_OVERRIDE'
        import json,pathlib
        bases=[p for p in ["compose.yaml","compose.yml","docker-compose.yaml","docker-compose.yml"] if pathlib.Path(p).is_file()]
        if len(bases)!=1: raise SystemExit(43)
        base=bases[0]; stem,ext=base.rsplit(".",1); override=stem+".override."+ext
        others=[p for p in ["compose.override.yaml","compose.override.yml","docker-compose.override.yaml","docker-compose.override.yml"] if p!=override and pathlib.Path(p).exists()]
        if others: raise SystemExit(43)
        p=pathlib.Path(override); exists=p.exists()
        marker="# managed-by: scripts/deploy-am-service.ps1 CARD-0410 - do not hand-edit"
        managed=not exists or p.read_text().splitlines()[0]==marker
        print(json.dumps({"name":override,"exists":exists,"managed":managed}))
        C0410_OVERRIDE
        """;

    private const string PsBrokerProbe = """
        cd /home/mc/antiphon-messaging && docker compose exec -T redpanda rpk cluster info -X brokers=am-redpanda:9092 --format json 2>/dev/null
        """;

    private const string PsProjection = """
        cd /home/mc/antiphon-messaging && docker compose config --no-interpolate --format json messaging-service | python3 -c 'import json, os, sys; d=json.load(sys.stdin); b=d["services"]["messaging-service"]["build"]; c=b["context"]; print(json.dumps({"context":os.path.normpath(c if os.path.isabs(c) else os.path.join("/home/mc/antiphon-messaging",c)),"dockerfile":b["dockerfile"]}))'
        """;

    private const string PsFacts = """
        docker inspect --format '{{.Id}} {{.Image}} {{.State.Status}}' am-service && cd /home/mc/antiphon-messaging && docker compose ps --format json messaging-service
        """;

    private const string PsPersist = """
        set -eu
        cp -- '/home/mc/antiphon-messaging/docker-compose.override.yml' '/home/mc/antiphon-messaging/docker-compose.override.yml.bak-{STAMP}-{OVERRIDE8}'
        cat > '/home/mc/antiphon-messaging/docker-compose.override.yml' <<'C0410_MANAGED'
        # managed-by: scripts/deploy-am-service.ps1 CARD-0410 - do not hand-edit
        services:
          messaging-service:
            environment:
              Kafka__AntiphonConsumerGroup: 'antiphon-server-bridge'
              Kafka__ExpectedAntiphonConsumerGroup: 'antiphon-server-bridge'
        C0410_MANAGED
        """;

    private const string PsGatewayMerged = """
        cd /home/mc/antiphon-messaging && python3 - <<'C0410_GATEWAY_SETTINGS'
        import json,subprocess
        d=json.loads(subprocess.check_output(["docker","compose","config","--format","json"]))
        env=d["services"]["messaging-service"].get("environment",{})
        profile=json.loads(subprocess.check_output(["docker","exec","am-service","cat","/app/appsettings.json"])).get("Kafka",{})
        keys=["AntiphonConsumerGroup","ExpectedAntiphonConsumerGroup","InboundTopic","ConsumerGroup","BootstrapServers"]
        merged="merged" == "merged"
        print(json.dumps({k:env.get("Kafka__"+k,None if merged and k in keys[:2] else profile.get(k)) for k in keys}))
        C0410_GATEWAY_SETTINGS
        """;

    private const string PsReplace = """
        set -eu
        archive='/tmp/am-service-src-{UPLOAD}.tgz'
        staging='/home/mc/antiphon-messaging/build/.src-stage-{STAGE}'
        current='/home/mc/antiphon-messaging/build/src'
        backup='/home/mc/antiphon-messaging/build/src.bak-{STAMP}-{BACKUP8}'
        cleanup() { rm -f -- "$archive"; }
        trap cleanup EXIT HUP INT TERM
        rm -rf -- "$staging"
        mkdir -p "$staging"
        tar xzf "$archive" -C "$staging"
        {CHECKS}
        actual_hash=$(sha256sum "$staging/Antiphon.Messaging.Service/Dockerfile" | awk '{print $1}')
        test "$actual_hash" = '{HASH}'
        test -d "$current"
        mv "$current" "$backup"
        mv "$staging" "$current"
        cd '/home/mc/antiphon-messaging'
        docker compose build messaging-service
        docker compose up -d --no-deps messaging-service
        """;

    private const string PsVerify = """
        set -eu
        cd '/home/mc/antiphon-messaging'
        deadline=$(($(date +%s) + 10))
        while [ "$(docker inspect --format '{{.State.Running}}' am-service 2>/dev/null || true)" != true ]; do [ "$(date +%s)" -lt "$deadline" ] || exit 41; sleep 2; done
        attempt=1
        while :; do
          if channels=$(curl --connect-timeout 2 --max-time 3 -fsS http://localhost:18090/api/channels 2>/dev/null); then
            printf '%s\n' "$channels"
            break
          fi
          [ "$attempt" -lt 5 ] || exit 44
          sleep 2
          attempt=$((attempt + 1))
        done
        docker compose exec -T am-postgres sh -c 'psql -X -At -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c '\''SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";'\'''
        if docker logs --since 5m --tail 100 am-service 2>&1 | grep -Eiq 'Unhandled exception|fail:|crit:'; then exit 42; fi
        """;

    private const string PsMonitor = """
        curl -sS --max-time 15 -w '\n%{http_code}' http://localhost:18090/health/inbound-unconsumed
        """;

    private const string PsGroupDescribe = """
        cd /home/mc/antiphon-messaging && docker compose exec -T redpanda rpk group describe antiphon-server-bridge 2>/dev/null
        """;

    private const string PsRunning = """
        docker inspect --format '{{.Id}} {{.Image}} {{.State.Status}}' am-service
        """;

    private const string PsCleanup = """
        rm -f -- '/tmp/am-service-src-{UPLOAD}.tgz'
        """;
}

/// <summary>
/// The fake remote of DeployAmServiceTests: the driver's Add-MonitorFake over its success runner and the C496 native shims. ssh, scp
/// and tar are a recording launcher (Events records "ssh &lt;command&gt;", "scp", "archive -czf", "archive -tzf" and "http &lt;url&gt;" in
/// order); the archive is a small file in a scratch directory. Case and Variant pick the driver's monitorCase and C496 variant.
/// </summary>
internal sealed class AmServiceWorld : IDisposable
{
    public const string Target = "operator@gateway.example.invalid";
    public const string Secret = "never-log-this";
    public const string RealisticCluster = "redpanda.66fe474a-d399-4ea1-8cde-c606e98614a8";

    private static string Root => DelegateScriptRunner.RepoRoot;

    public static readonly IReadOnlyList<AmServiceManifestEntry> RepositoryManifest =
        AmServiceDeploy.Manifest(new FileSystem(), Path.Combine(Root, "src", "Antiphon.Messaging.Service", "Dockerfile"), Path.Combine(Root, "src"));

    public static readonly string DockerfileHash =
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(Root, "src", "Antiphon.Messaging.Service", "Dockerfile")))).ToLowerInvariant();

    private int _identityReads;
    private int _brokerReads;
    private int _tokens;

    public AmServiceWorld()
    {
        Temp = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "am-service-deploy-" + Guid.NewGuid().ToString("N"))).FullName;
        Launcher = new RecordingLauncher(Answer);
        Migrations = AmServiceDeploy.MigrationIds(new FileSystem(), Path.Combine(Root, "src", "Antiphon.Messaging.Service", "Migrations"));
        Migrations.ShouldNotBeEmpty();
    }

    /// <summary>The 32-hex token number n (1-based) the world's seam hands out: distinct in its first eight characters.</summary>
    public static string Token(int n) => n.ToString("x8", System.Globalization.CultureInfo.InvariantCulture) + new string('c', 24);

    public string Case { get; set; } = "";

    public string Variant { get; set; } = "";

    public string? Cluster { get; set; }

    public bool FailBuild { get; set; }

    public string? Missing { get; set; }

    public string[]? TarListing { get; set; }

    public int TarCreateExit { get; set; }

    public int TarListExit { get; set; }

    public bool Persisted { get; private set; }

    public string Temp { get; }

    public RecordingLauncher Launcher { get; }

    public IReadOnlyList<string> Migrations { get; }

    public IReadOnlyList<AmServiceManifestEntry> Manifest => RepositoryManifest;

    public List<string> Events { get; } = [];

    public List<string> Commands { get; } = [];

    public List<(string Url, IReadOnlyDictionary<string, string> Headers)> HttpCalls { get; } = [];

    public Queue<string?> Answers { get; } = new();

    public Dictionary<string, string?> Variables { get; } = new(StringComparer.Ordinal);

    public StringWriter Out { get; } = new();

    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.Parse("2026-11-25T00:12:13Z", System.Globalization.CultureInfo.InvariantCulture));

    public NightlyHost Host => new()
    {
        Clock = Clock,
        Git = new FakeGit().Run,
        Launcher = Launcher,
        Processes = new FakeProcessTable(),
        Probe = new FakeHostProbe(),
        Out = Out,
        Error = Out,
        Trace = TextWriter.Null,
        // Never this process's environment: ANTIPHON_API and ANTIPHON_TASK_TOKEN are the test's.
        Variables = name => Variables.TryGetValue(name, out var value) ? value : null,
    };

    public AmServiceDeploySeams Seams => new()
    {
        Http = Http,
        Token = () => Token(++_tokens),
        TempDirectory = () => Temp,
        Resolve = name => name == Missing ? null : name,
        ReadLine = () => Answers.Count > 0 ? Answers.Dequeue() : null,
    };

    public string Text => Out.ToString().ReplaceLineEndings("\n");

    public string[] Lines => Text.TrimEnd('\n').Split('\n');

    public IReadOnlyList<string> SshDestinations() => Launcher.Launches.Where(launch => launch.FileName == "ssh").Select(launch => launch.Arguments[0]).ToList();

    public (int Exit, string Output, string[] Lines) Cli(params string[] arguments)
    {
        var exit = AmServiceDeploy.Run(AmServiceDeployOptions.Parse(arguments, Root), Host, new FileSystem(), Seams);
        return (exit, Text, Lines);
    }

    public AmServiceDeployResult Deploy(bool perform, bool skip = true, int poll = 10, string? target = Target) =>
        AmServiceDeploy.Deploy(Root, perform, skip, poll, target!, Host, new FileSystem(), Seams);

    public void Dispose() => Directory.Delete(Temp, recursive: true);

    private static NightlyLaunchResult Answer(int exit, params string[] lines) =>
        new() { ExitCode = exit, LogText = lines.Length == 0 ? "" : string.Join("\n", lines) + "\n" };

    private NightlyLaunchResult Answer(NightlyLaunch launch)
    {
        switch (launch.FileName)
        {
            case "ssh":
                launch.Arguments.Count.ShouldBe(2);
                return Ssh(launch.Arguments[1]);
            case "scp":
                Events.Add("scp");
                return Answer(0);
            case "tar":
                Events.Add("archive " + launch.Arguments[0]);
                if (launch.Arguments[0] == "-czf")
                {
                    launch.Arguments[1].ShouldMatch(@"^am-service-src-[a-f0-9]{32}\.tgz$");
                    File.WriteAllText(Path.Combine(launch.WorkingDirectory, launch.Arguments[1]), "owned fake archive");
                    return Answer(TarCreateExit);
                }
                launch.Arguments[0].ShouldBe("-tzf");
                return Answer(TarListExit, TarListing ?? Manifest.Select(entry => entry.Path).ToArray());
            default:
                throw new InvalidOperationException("Unexpected fixture native command: " + launch.FileName);
        }
    }

    private AmServiceHttpAnswer Http(string url, IReadOnlyDictionary<string, string> headers)
    {
        HttpCalls.Add((url, headers));
        Events.Add("http " + url);
        if (Case == "http-down") throw new HttpRequestException("connection refused");
        if (url.EndsWith("/api/version", StringComparison.Ordinal))
            return Case == "no-version" ? new(200, "{\"version\":\"main\"}") : new(200, "{\"version\":\"0123456789012345678901234567890123456789\"}");
        url.ShouldEndWith("/api/channels/consumer");
        _identityReads++;
        if (Case == "404") return new(404, Secret);
        var group = Case switch
        {
            "override" => "family-bridge-custom",
            "rename" when _identityReads > 1 => "renamed",
            "rename-late" when _identityReads > 2 => "renamed",
            "bad-group" => "bad group",
            "same-group" => "antiphon-messaging-service",
            _ => "antiphon-server-bridge",
        };
        object brokers = Case switch
        {
            "no-brokers" => Array.Empty<object>(),
            "bad-broker" => new[] { new { host = "am-redpanda", port = 0 } },
            _ => new[] { new { host = "am-redpanda", port = 9092 } },
        };
        return new(200, JsonSerializer.Serialize(new
        {
            consumerGroup = group,
            inboundTopic = Case == "topic" ? "wrong-topic" : "channels.inbound",
            enabled = Case != "disabled",
            brokers,
            secret = Secret,
        }));
    }

    private NightlyLaunchResult Ssh(string command)
    {
        Commands.Add(command);
        Events.Add("ssh " + command);
        var group = Case == "override" ? "family-bridge-custom" : "antiphon-server-bridge";
        if (command.Contains("C0410_GATEWAY_SETTINGS", StringComparison.Ordinal))
        {
            var expected = Persisted && Case != "missing-merged" ? group : null;
            return Answer(0, JsonSerializer.Serialize(new
            {
                AntiphonConsumerGroup = Persisted ? group : "antiphon-consumer",
                ExpectedAntiphonConsumerGroup = expected,
                InboundTopic = "channels.inbound",
                ConsumerGroup = "antiphon-messaging-service",
                BootstrapServers = Case == "bad-listener" ? "am redpanda" : "am-redpanda:9092",
                secret = Secret,
            }));
        }
        if (command.Contains("C0410_OVERRIDE", StringComparison.Ordinal))
        {
            if (Case == "ambiguous-compose") return Answer(43, Secret);
            return Answer(0, JsonSerializer.Serialize(new
            {
                name = Case == "compose-yaml" ? "compose.override.yaml" : "docker-compose.override.yml",
                exists = Case is "unmarked" or "marked",
                managed = Case != "unmarked",
            }));
        }
        if (command.Contains("rpk cluster info", StringComparison.Ordinal))
        {
            _brokerReads++;
            var cluster = Cluster ?? (Case == "broker-mismatch" && _brokerReads == 2 ? "different-cluster"
                : Case == "legacy-cluster" ? "c0410-cluster" : Case == "invalid-cluster" ? "..." : RealisticCluster);
            var json = JsonSerializer.Serialize(new { cluster_name = cluster });
            return Case == "rpk-noise"
                ? Answer(0, "WARNING: This is a Redpanda Enterprise feature. Requires a license.", "CLUSTER", "=======", json)
                : Answer(0, json);
        }
        if (command.Contains("C0410_MANAGED", StringComparison.Ordinal))
        {
            Persisted = true;
            return Answer(0);
        }
        if (command.Contains("/health/inbound-unconsumed", StringComparison.Ordinal))
        {
            var status = Case == "readiness-404" ? "404" : Case == "degraded" ? "503" : "200";
            return Answer(0, JsonSerializer.Serialize(new
            {
                state = "Ready",
                watchedGroup = Case == "wrong-group" ? "antiphon-consumer" : "antiphon-server-bridge",
                expectedGroup = "antiphon-server-bridge",
                topic = "channels.inbound",
                ageSeconds = Case == "stale" ? 400 : 12,
                partitions = new[] { new { partition = 0, status = Case == "no-commit" ? "NoCommit" : "CommittedOffset", committedNextOffset = 12 } },
                secret = Secret,
            }), status);
        }
        if (command.Contains("rpk group describe", StringComparison.Ordinal))
            return Answer(0, Case == "no-numeric" ? "channels.inbound 0 - - -" : Case == "behind" ? "channels.inbound 0 11 0 12 1" : "channels.inbound 0 12 0 12 0");
        if (command.StartsWith("docker inspect", StringComparison.Ordinal) && !command.Contains("&&", StringComparison.Ordinal))
            return Answer(0, Case == "same-image" ? "cccccccccccc sha256:aaaaaaaaaaaa running" : "bbbbbbbbbbbb sha256:bbbbbbbbbbbb running");
        if (command.Contains("docker compose config", StringComparison.Ordinal))
            return Answer(0, JsonSerializer.Serialize(new
            {
                context = Variant == "wrong-context" ? "/different/build/src" : "/home/mc/antiphon-messaging/build/src",
                dockerfile = Variant == "wrong-dockerfile" ? "different/Dockerfile" : "Antiphon.Messaging.Service/Dockerfile",
                secret = Secret,
            }));
        if (command.Contains("http://localhost:18090/api/channels", StringComparison.Ordinal))
        {
            if (Case == "verify-44") return Answer(44);
            if (Case == "verify-41") return Answer(41, Secret);
            var channels = Case == "bad-channels" ? "<html>" + Secret : "[{\"channel\":\"telegram\"},{\"channel\":\"slack\"}]";
            return Answer(0, [channels, .. Case == "missing-migration" ? Migrations.Skip(1) : Migrations]);
        }
        if (command.StartsWith("docker inspect", StringComparison.Ordinal))
            return Answer(0, Case == "bad-facts" ? "not-a-container" : "aaaaaaaaaaaa sha256:aaaaaaaaaaaa running", "{\"State\":\"running\",\"secret\":\"never-log-this\"}");
        if (command.Contains("tar xzf", StringComparison.Ordinal))
            return FailBuild ? Answer(9, "secret=" + Secret) : Answer(0);
        if (command.StartsWith("rm -f -- ", StringComparison.Ordinal))
            return Case == "cleanup-fails" ? Answer(255, Secret) : Answer(0);
        throw new InvalidOperationException("Unexpected fixture SSH command: " + command);
    }
}
