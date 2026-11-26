using System.Text;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.Infrastructure;
using Shouldly;
using TUnit.Core;
using YamlDotNet.RepresentationModel;
using Antiphon.Tests.Scripts;
using static Antiphon.Tests.Scripts.Server2PostgresComposeTests;

namespace Antiphon.Tests.Invariants;

[Category("Unit")]
[Category("RepositoryInvariant")]
public sealed class Server2PostgresComposeInvariantTests
{
    [Test]
    public void Compose_contract()
    {
        File.Exists(Path.Combine(DockerStackDocuments.RepoRoot, Server2PostgresDeploy.ComposeName)).ShouldBeTrue("compose file exists");
        var text = Production;
        foreach (var need in new[]
        {
            "name: antiphon-server2-postgres", "ANTIPHON_POSTGRES_BIND_ADDRESS:?", "ANTIPHON_POSTGRES_IMAGE:?", "POSTGRES_PASSWORD:?", ":5432:5432",
            "cpuset: \"0,12\"", "cpu_shares: 2048", "mem_limit: 8g", "shm_size: 1gb", "    blkio_config:\n      weight: 800", "restart: unless-stopped",
            "pg_isready -U antiphon -d antiphon", "shared_preload_libraries=pg_stat_statements", "pg_stat_statements.track=top", "pg_stat_statements.max=5000",
            "networks:\n  antiphon-server2:\n    name: antiphon-server2", "    networks:\n      - antiphon-server2", "pgdata:/var/lib/postgresql/data",
            "name: antiphon-server2-postgres_pgdata", "interval: 5s", "timeout: 5s", "retries: 12", "start_period: 10s",
        })
            text.ShouldContain(need, Case.Sensitive, "compose contains " + need);
        text.ShouldNotContain("5433", Case.Sensitive, "compose omits port 5433");
        text.ShouldNotContain("alpine", Case.Insensitive, "compose omits alpine");
        text.ShouldNotContain("0.0.0.0", Case.Sensitive, "compose omits 0.0.0.0");
        text.ShouldNotContain("antiphon_pgdata", Case.Insensitive, "compose omits antiphon_pgdata");
        text.ShouldNotContain("privileged:", Case.Insensitive, "compose is not privileged");
        text.ShouldNotContain("docker.sock", Case.Insensitive, "compose mounts no docker socket");
    }

    [Test]
    public void Doc_sentence()
    {
        var text = DockerStackDocuments.Read("docs/docker-stack.md");
        text.ShouldContain("docker-compose.server2-postgres.yml", Case.Sensitive, "doc names the host compose file");
        text.ShouldContain("antiphon-server2-postgres", Case.Sensitive, "doc names the compose project");
        text.ShouldContain("exists beside the runner", Case.Sensitive, "doc says the service exists beside the runner");
        text.ShouldContain("not the nested", Case.Sensitive, "doc says the service is not the nested daemon");
        text.ShouldContain("not Windmill", Case.Sensitive, "doc says the service is not Windmill");
        text.ShouldContain("antiphon-server2-postgres-drill", Case.Sensitive, "doc names the scratch project");
        text.ShouldNotContain("no server, no Postgres", Case.Insensitive, "doc no longer says server2 has no Postgres");
        var runner = DockerStackDocuments.Read("docker-compose.server2-runner.yml");
        runner.ShouldNotContain("no server, no Postgres", Case.Insensitive, "runner compose no longer says there is no Postgres");
        runner.ShouldContain("CARD-1406", Case.Sensitive, "runner compose points at the host Postgres project");
        text.ShouldContain(ExitCodes, Case.Sensitive, "doc names the deploy exit codes");
        DockerStackDocuments.Read(DeploySource).ShouldContain(ExitCodes, Case.Sensitive, "the deploy command documents exit codes");
        var guide = DockerStackDocuments.Read("docs/testing-and-build.md");
        Regex.IsMatch(guide, @"no server\s+and no Postgres", RegexOptions.IgnoreCase).ShouldBeFalse("testing guide no longer says server2 has no Postgres");
        guide.ShouldContain("not yet the control plane", Case.Sensitive, "testing guide says the host Postgres is not yet the control plane");
    }

    [Test]
    public void Ascii()
    {
        foreach (var path in new[]
        {
            DeploySource, "scripts/lib/Antiphon.Scripts/DeployCommands.cs", Server2PostgresDeploy.ComposeName, Server2PostgresDeploy.DrillComposeName,
            "tests/Antiphon.Tests/Scripts/Server2PostgresComposeTests.cs", "tests/Antiphon.Tests/Scripts/Server2PostgresDeployTests.cs",
            "tests/Antiphon.Tests/Scripts/Server2PostgresDeployWorld.cs",
        })
        {
            var bytes = File.ReadAllBytes(Path.Combine(DockerStackDocuments.RepoRoot, path.Replace('/', Path.DirectorySeparatorChar)));
            bytes.Count(b => b > 127).ShouldBe(0, "ascii " + Path.GetFileName(path));
        }
    }

    [Test]
    public void Drill_compose()
    {
        var text = Drill;
        text.Length.ShouldBeGreaterThan(0);
        foreach (var need in new[]
        {
            "name: antiphon-server2-postgres-drill", "name: antiphon-server2-postgres-drill_pgdata", "127.0.0.1:5434:5432",
            "networks:\n  antiphon-server2-postgres-drill:\n    name: antiphon-server2-postgres-drill", "    networks:\n      - antiphon-server2-postgres-drill",
            "ANTIPHON_POSTGRES_IMAGE:?", "POSTGRES_PASSWORD:?", "cpuset: \"0,12\"", "cpu_shares: 2048", "mem_limit: 8g", "shm_size: 1gb",
            "    blkio_config:\n      weight: 800",
        })
            text.ShouldContain(need, Case.Sensitive, "drill compose contains " + need);
        text.ShouldNotContain("name: antiphon-server2-postgres_pgdata", Case.Insensitive, "drill compose does not pin the production volume");
        text.ShouldNotContain("antiphon_pgdata", Case.Insensitive, "drill compose omits antiphon_pgdata");
        text.ShouldNotContain(":5432:5432", Case.Sensitive, "drill compose does not publish the production port");
        text.ShouldNotContain("0.0.0.0", Case.Sensitive, "drill compose omits 0.0.0.0");
        text.ShouldNotContain("5433", Case.Sensitive, "drill compose omits port 5433");
        text.ShouldNotContain("ANTIPHON_POSTGRES_BIND_ADDRESS", Case.Sensitive, "drill bind is not the production tailnet variable");
        text.ShouldNotContain("networks:\n  antiphon-server2:\n    name: antiphon-server2", Case.Sensitive, "drill compose does not define the production network");
    }

    [Test]
    public void Drill_render()
    {
        var prod = Fields(Production);
        prod.Volume.ShouldBe("antiphon-server2-postgres_pgdata", "production compose under the drill project mounts the production volume");
        File.Exists(Path.Combine(DockerStackDocuments.RepoRoot, Server2PostgresDeploy.DrillComposeName)).ShouldBeTrue("scratch drill compose exists for render");
        var drill = Fields(Drill);
        drill.Volume.ShouldBe("antiphon-server2-postgres-drill_pgdata", "drill compose under the drill project mounts the scratch volume");
        (drill.Volume != "antiphon-server2-postgres_pgdata" && drill.Volume != "antiphon_pgdata").ShouldBeTrue("drill render does not mount a production volume");
        (drill.HostIp + ":" + drill.Published).ShouldBe("127.0.0.1:5434", "drill publish is loopback port 5434");
        drill.Network.ShouldBe("antiphon-server2-postgres-drill", "drill network is the scratch network");
        // Under the production project the pinned name is still the scratch volume.
        Regex.IsMatch(Drill, "(?m)^volumes:\n  pgdata:\n    name: antiphon-server2-postgres-drill_pgdata$").ShouldBeTrue("drill compose under the production project still mounts the scratch volume");
    }

    [Test]
    public void Rendered_production_compose_has_the_shipped_port_and_image()
    {
        var bind = Server2PostgresWorld.Bind;
        var image = Server2PostgresWorld.Image;
        var rendered = Server2PostgresDeploy.Render(Production, bind, image, Server2PostgresWorld.Password);
        rendered.Ok.ShouldBeTrue("render accepts a digest pin and a tailnet address: " + rendered.Reason);
        rendered.Redacted.ShouldContain(bind + ":5432:5432", Case.Sensitive, "rendered port is the bind on 5432");
        rendered.Redacted.ShouldContain(image, Case.Sensitive, "rendered image is the digest pin");
        rendered.Redacted.ShouldNotContain("0.0.0.0", Case.Sensitive, "rendered port is not 0.0.0.0");
    }

    [Test]
    public void Development_compose_declares_a_db_password()
    {
        var dev = Regex.Match(DockerStackDocuments.Read("docker-compose.dev.yml"), @"POSTGRES_PASSWORD:\s*(\S+)").Groups[1].Value;
        dev.ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public void Deploy_source_has_no_production_volume_removal()
    {
        var source = DockerStackDocuments.Read(DeploySource);
        source.ShouldNotContain("volume rm antiphon_pgdata", Case.Insensitive, "the command has no antiphon_pgdata volume rm");
        source.ShouldNotContain("volume rm antiphon-server2-postgres_pgdata", Case.Insensitive, "the command has no production volume rm");
    }

    [Test]
    public void Deploy_requires_the_shipped_network_block()
    {
        var source = DockerStackDocuments.Read(DeploySource);
        source.ShouldContain("\"networks:\\n  antiphon-server2:\\n    name: antiphon-server2\"", Case.Sensitive, "deploy refusal requires the exact network block");
        source.ShouldContain("\"    networks:\\n      - antiphon-server2\"", Case.Sensitive, "deploy refusal requires the service network attachment");
    }

    [Test]
    public void Drill_plan_agrees_with_the_operator_runbook()
    {
        var world = new Server2PostgresWorld();
        var result = world.Run("--what-if", "--restore-drill");
        (result.Exit, result.Reason).ShouldBe((0, "ok"), "drill command plan exits 0");
        var doc = Read("docs/docker-stack.md");
        DrillSection(doc).ShouldContain("encrypted", Case.Sensitive, "drill section says encrypted");
        var bodies = Bodies(result.Out);
        var docCommands = DocFence(doc);
        docCommands.Length.ShouldBeGreaterThan(0, "doc drill fence has commands");
        bodies.ShouldBe(docCommands, "printed drill commands equal the doc fence");
        var migrations = bodies.Where(body => body.Contains("__EFMigrationsHistory", StringComparison.OrdinalIgnoreCase)).ToArray();
        migrations.Length.ShouldBe(1, "one migrations command");
        var docMigrations = docCommands.Where(body => body.Contains("__EFMigrationsHistory", StringComparison.OrdinalIgnoreCase)).ToArray();
        docMigrations.Length.ShouldBe(1, "printed migrations line equals the doc line");
        migrations[0].ShouldBe(docMigrations[0], "printed migrations line equals the doc line");
    }

    [Test]
    public void Committed_postgres_and_runner_cpu_budgets_are_isolated()
    {
        Server2PostgresDeploy.Cpuset.ShouldBe(PostgresCpuset, "the command's pin is the witness's");
        var pgLiteral = "cpuset: \"" + PostgresCpuset + "\"";
        var postgresFiles = new (string Label, string Text, Func<string, string> Refusal, bool Drill)[]
        {
            ("production", Production, Server2PostgresDeploy.ComposeRefusal, false),
            ("drill", Drill, Server2PostgresDeploy.DrillComposeRefusal, true),
        };
        foreach (var file in postgresFiles)
        {
            var blocks = ServiceBlocks(file.Text);
            blocks.Any(block => block.Name == "postgres").ShouldBeTrue(file.Label + " compose has the postgres service");
            var postgres = blocks.First(block => block.Name == "postgres").Lines;
            var body = "\n" + string.Join("\n", postgres) + "\n";
            Cpusets(postgres).ShouldBe([PostgresCpuset], file.Label + " postgres " + pgLiteral);
            foreach (var need in new[] { PostgresCpuShares, "    mem_limit: 8g", "    shm_size: 1gb", "    blkio_config:\n      weight: 800" })
                body.ShouldContain("\n" + need + "\n", Case.Sensitive, file.Label + " postgres has " + need.Trim().Replace("\n", " "));
            postgres.Count(line => Regex.IsMatch(line, @"^\s*cpu_shares\s*:", RegexOptions.IgnoreCase)).ShouldBe(1, file.Label + " postgres sets cpu_shares exactly once");
            Regex.IsMatch(file.Text, @"(?m)^\s*cpus\s*:", RegexOptions.IgnoreCase).ShouldBeFalse(file.Label + " compose sets no cpus quota (the cpuset is the cap)");
            foreach (var old in new[] { "mem_limit: 4g", "cpuset: \"0-2,12-14\"" })
                file.Text.ShouldNotContain(old, Case.Sensitive, file.Label + " compose omits " + old);
        }
        var runnerText = Read("docker-compose.server2-runner.yml");
        var runnerBlocks = ServiceBlocks(runnerText);
        foreach (var service in new[] { "state-init", "session-runner", "build-slots" })
            runnerBlocks.Any(block => block.Name == service).ShouldBeTrue("runner compose has " + service);
        foreach (var (service, lines) in runnerBlocks)
            Cpusets(lines).ShouldBeEmpty("runner " + service + " carries no cpuset and runs on every CPU");
        runnerText.Length.ShouldBeGreaterThan(0);
        Regex.IsMatch(runnerText, @"(?m)^\s*cpuset\s*:", RegexOptions.IgnoreCase).ShouldBeFalse("runner compose sets no cpuset anywhere");
        Regex.IsMatch(runnerText, @"(?m)^\s*cpu_shares\s*:", RegexOptions.IgnoreCase).ShouldBeFalse("runner compose keeps the default cpu_shares (1024)");
        Regex.IsMatch(runnerText, @"(?m)^\s*mem_limit\s*:", RegexOptions.IgnoreCase).ShouldBeFalse("runner compose sets no memory cap");
        var tempText = Read("docker-compose.server2-runner.temp.yml");
        tempText.Length.ShouldBeGreaterThan(0);
        Regex.IsMatch(tempText, @"(?m)^\s*cpuset\s*:", RegexOptions.IgnoreCase).ShouldBeFalse("temp runner overlay sets no cpuset");
        Regex.IsMatch(tempText, @"(?m)^\s*cpu_shares\s*:", RegexOptions.IgnoreCase).ShouldBeFalse("temp runner overlay sets no cpu_shares");
        var docText = DockerStackDocuments.Read("docs/docker-stack.md");
        var sentence = "Postgres cpuset is " + PostgresCpuset + ", one physical core, and the runner services set no cpuset";
        docText.ShouldContain(sentence, Case.Sensitive, "doc says " + sentence);
        foreach (var field in new[] { "HostConfig.CpusetCpus", "HostConfig.CpuShares", "HostConfig.Memory", "HostConfig.ShmSize" })
            docText.ShouldContain(field, Case.Sensitive, "doc names the on-host " + field + " check");
        docText.ShouldContain("Postgres reads `cpuset=[0,12] shares=2048 8589934592 1073741824 800`", Case.Sensitive, "doc V-4 expects the Postgres reading");
        docText.ShouldContain("both runner containers read `cpuset=[] shares=0`", Case.Sensitive, "doc V-4 expects the runner reading");
        foreach (var old in new[] { "Postgres cpuset is 0-2,12-14", "The runner cpuset is", "cpuset: \"3-11,15-23\"", "cpuset: \"1-11,13-23\"" })
            docText.ShouldNotContain(old, Case.Sensitive, "doc drops " + old);
        var shares = PostgresCpuShares + "\n";
        foreach (var file in postgresFiles)
        {
            file.Refusal(file.Text).ShouldBe("", file.Label + " refusal accepts the one-core compose");
            foreach (var (name, from, to) in new[]
            {
                ("without the cpuset", "    " + pgLiteral + "\n", ""),
                ("with an unpinned 0-23 cpuset", pgLiteral, "cpuset: \"0-23\""),
                ("with the old 0-2,12-14 cpuset", pgLiteral, "cpuset: \"0-2,12-14\""),
                ("with a 0,12,13 cpuset", pgLiteral, "cpuset: \"0,12,13\""),
                ("with a second cpuset line", shares, shares + "    cpuset: \"1,13\"\n"),
                ("without cpu_shares 2048", shares, ""),
                ("with cpu_shares 1024", shares, "    cpu_shares: 1024\n"),
                ("with cpu_shares 20480", shares, "    cpu_shares: 20480\n"),
                ("with a cpus 6.0 quota", shares, shares + "    cpus: 6.0\n"),
                ("with a cpus 2.0 quota", shares, shares + "    cpus: 2.0\n"),
                ("without mem_limit 8g", "    mem_limit: 8g\n", ""),
                ("with mem_limit 4g", "mem_limit: 8g", "mem_limit: 4g"),
                ("without shm_size 1gb", "    shm_size: 1gb\n", ""),
                ("without blkio weight 800", "      weight: 800\n", ""),
            })
            {
                var changed = file.Text.Replace(from, to);
                (changed != file.Text, file.Refusal(changed)).ShouldBe((true, "compose-invalid"), file.Label + " refusal is compose-invalid " + name);
            }
            foreach (var (name, from) in new[] { ("the cpuset", "    " + pgLiteral + "\n"), ("cpu_shares 2048", shares) })
            {
                var world = new Server2PostgresWorld();
                var stripped = file.Text.Replace(from, "");
                var copy = world.Compose("compose.yml", stripped);
                var result = file.Drill
                    ? world.Run("--what-if", "--restore-drill", "--compose-file", copy)
                    : world.Run("--what-if", "--compose-file", copy, "--env-file", world.Env());
                (stripped != file.Text, result.Exit, result.Reason).ShouldBe((true, 2, "compose-invalid"), file.Label + " WhatIf refuses a compose without " + name);
                NoDocker(result, file.Label + " cpu-isolation");
            }
        }
    }
}
