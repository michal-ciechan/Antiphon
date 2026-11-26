using System.Text;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.Infrastructure;
using Shouldly;
using TUnit.Core;
using YamlDotNet.RepresentationModel;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1406 witness, unit 54 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the cases of the deleted
/// scripts/test-server2-postgres-compose.ps1, one test per driver case and in its order, in process. The compose files, the
/// docker-stack and testing docs and the runner composes are read from the checkout (LF, as on server2); the deploy cases run
/// the deploy-server2-postgres command on Server2PostgresWorld (a mock disk, a recording launcher), where the driver ran the
/// script under pwsh with POSIX sh stubs and real file modes, so they run on Windows and Linux alike. "No docker" is the
/// driver's empty marker: the run starts no tool. Two cases changed their mechanics: drill-render read the fields from
/// `docker compose config` and now reads them from the compose files' YAML (no docker), and drill-commands' quote probe ran sh
/// with a psql stub and now splits the line by POSIX sh's quoting rules in process.
/// </summary>
[Category("Unit")]
public sealed class Server2PostgresComposeTests
{
    private const string PostgresCpuset = "0,12";
    private const string PostgresCpuShares = "    cpu_shares: 2048";
    private const int HostCpuCount = 24;
    private const int SmtSiblingOffset = 12;
    private const string DeploySource = "scripts/lib/Antiphon.Nightly/Server2PostgresDeploy.cs";
    private const string ExitCodes = "Exit codes: 0 plan or ok, 2 refusal, 3 ambiguous-mode.";

    private static string Read(string relative) => DockerStackDocuments.Read(relative).Replace("\r\n", "\n");

    private static string Production => Server2PostgresWorld.ProductionText;

    private static string Drill => Server2PostgresWorld.DrillText;

    // Assert-NoDocker: no tool started, and the fixture password on neither stream.
    private static void NoDocker(Server2PostgresRun run, string name)
    {
        run.Tools.ShouldBeEmpty(name + " " + run);
        run.Out.ShouldNotContain(Server2PostgresWorld.Password, Case.Sensitive, name + " hides the fixture password");
        run.Error.ShouldNotContain(Server2PostgresWorld.Password, Case.Sensitive, name + " hides the fixture password on stderr");
    }

    private static Server2PostgresRun Refuses(Server2PostgresWorld world, string reason, params string[] arguments)
    {
        var run = world.Run(arguments);
        run.Exit.ShouldNotBe(0, run.ToString());
        run.Reason.ShouldBe(reason, run.ToString());
        NoDocker(run, reason);
        return run;
    }

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
    public void Render()
    {
        var bind = Server2PostgresWorld.Bind;
        var image = Server2PostgresWorld.Image;
        var rendered = Server2PostgresDeploy.Render(Production, bind, image, Server2PostgresWorld.Password);
        rendered.Ok.ShouldBeTrue("render accepts a digest pin and a tailnet address: " + rendered.Reason);
        rendered.Redacted.ShouldContain(bind + ":5432:5432", Case.Sensitive, "rendered port is the bind on 5432");
        rendered.Redacted.ShouldContain(image, Case.Sensitive, "rendered image is the digest pin");
        rendered.Redacted.ShouldNotContain("0.0.0.0", Case.Sensitive, "rendered port is not 0.0.0.0");
        rendered.Redacted.ShouldNotContain(Server2PostgresWorld.Password, Case.Insensitive, "rendered view redacts the password");
        Server2PostgresDeploy.Render(Production, "", image, Server2PostgresWorld.Password).ShouldBe(new Server2PostgresRender(false, "empty-bind", ""), "render refuses an empty bind");
        Server2PostgresDeploy.Render(Production, "0.0.0.0", image, Server2PostgresWorld.Password).ShouldBe(new Server2PostgresRender(false, "bind-any", ""), "render refuses 0.0.0.0");
        Server2PostgresDeploy.Render(Production, bind, "postgres:16", Server2PostgresWorld.Password).ShouldBe(new Server2PostgresRender(false, "image-floating", ""), "render refuses a floating tag");
        Server2PostgresDeploy.Render(Production, bind, "postgres:16-alpine@sha256:" + new string('b', 64), Server2PostgresWorld.Password)
            .ShouldBe(new Server2PostgresRender(false, "image-alpine", ""), "render refuses alpine");
    }

    [Test]
    public void Refuse_bind_any()
    {
        var world = new Server2PostgresWorld();
        Refuses(world, "bind-any", "--what-if", "--env-file", world.Env(bind: "0.0.0.0"));
    }

    [Test]
    public void Refuse_bind_empty()
    {
        var world = new Server2PostgresWorld();
        Refuses(world, "empty-bind", "--what-if", "--env-file", world.Env(emptyBind: true));
    }

    [Test]
    public void Refuse_bind_omitted()
    {
        var world = new Server2PostgresWorld();
        Refuses(world, "bind-omitted", "--what-if", "--env-file", world.Env(omitBind: true));
    }

    [Test]
    public void Refuse_volume_pgdata()
    {
        var world = new Server2PostgresWorld();
        var copy = world.Compose("compose.yml", Production.Replace("antiphon-server2-postgres_pgdata", "antiphon_pgdata"));
        Refuses(world, "volume-forbidden", "--what-if", "--env-file", world.Env(), "--compose-file", copy);
    }

    [Test]
    public void Refuse_image_floating()
    {
        var world = new Server2PostgresWorld();
        Refuses(world, "image-floating", "--what-if", "--env-file", world.Env(image: "postgres:16"));
    }

    [Test]
    public void Refuse_image_alpine()
    {
        var world = new Server2PostgresWorld();
        Refuses(world, "image-alpine", "--what-if", "--env-file", world.Env(image: "postgres:16-alpine@sha256:" + new string('b', 64)));
    }

    [Test]
    public void Whatif_no_docker()
    {
        var world = new Server2PostgresWorld();
        var result = world.Run("--what-if", "--env-file", world.Env());
        (result.Exit, result.Reason).ShouldBe((0, "ok"), "WhatIf exits 0: " + result);
        (result.Out.Contains("docker compose") && result.Out.Contains("-p antiphon-server2-postgres") && result.Out.Contains("up -d"))
            .ShouldBeTrue("WhatIf prints the host-daemon command");
        result.Out.ShouldContain("docker-compose.server2-postgres.yml", Case.Sensitive, "WhatIf names the compose file");
        NoDocker(result, "whatif");
        var plain = world.Run();
        (plain.Exit, plain.Reason).ShouldBe((0, "ok"), "default mode is WhatIf");
        NoDocker(plain, "default");
    }

    [Test]
    public void Whatif_drill()
    {
        var world = new Server2PostgresWorld();
        var result = world.Run("--what-if", "--restore-drill");
        (result.Exit, result.Reason).ShouldBe((0, "ok"), "drill WhatIf exits 0");
        foreach (var need in new[]
        {
            "antiphon-server2-postgres-drill", "antiphon-server2-postgres-drill_pgdata", "scratch-only", "unread: docker-ps", "unread: docker-volume-ls",
            "unread: docker-data-root-df", "unread: windmill-compose-parent", "unread: count-star", "unread: dump-elapsed-sec", "unread: restore-elapsed-sec",
            "unread: copy-elapsed-sec", "unread: dump-bytes", "phase3: user-table-count", "phase3: sequence-last-value", "phase3: extension-version",
            "phase3: ef-migrations-history", "phase3: key-table-count-min-max-pk",
        })
            result.Out.ShouldContain(need, Case.Sensitive, "drill plan contains " + need);
        Regex.IsMatch(result.Out, @"TranscriptEntries=\d", RegexOptions.IgnoreCase).ShouldBeFalse("drill plan does not print transcript rows");
        NoDocker(result, "drill");
    }

    [Test]
    public void Execute_off_host()
    {
        // The driver's stubs: docker info answers not-the-host, hostname fixture-host.
        var world = new Server2PostgresWorld();
        var result = world.Run("--execute", "--env-file", world.Env());
        result.Exit.ShouldNotBe(0);
        result.Reason.ShouldBe("not-server2-host", "Execute refuses off the server2 host");
        var marker = string.Join("\n", result.Tools);
        marker.ShouldNotContain("compose", Case.Insensitive, "Execute off-host does not compose up");
        marker.ShouldNotContain("volume", Case.Insensitive, "Execute off-host does not touch a volume");
        result.Out.ShouldNotContain(Server2PostgresWorld.Password, Case.Insensitive, "Execute off-host hides the fixture password");
    }

    [Test]
    public void Env_mode()
    {
        var world = new Server2PostgresWorld();
        Refuses(world, "env-mode", "--what-if", "--env-file", world.Env(ownerOnly: false));
    }

    [Test]
    public void Dev_password()
    {
        var world = new Server2PostgresWorld();
        var dev = Regex.Match(DockerStackDocuments.Read("docker-compose.dev.yml"), @"POSTGRES_PASSWORD:\s*(\S+)").Groups[1].Value;
        dev.ShouldNotBeNullOrWhiteSpace();
        var result = Refuses(world, "dev-password", "--what-if", "--env-file", world.Env(password: dev));
        result.Out.ShouldNotContain(dev, Case.Insensitive, "dev-password refusal does not print the password");
        result.Error.ShouldNotContain(dev, Case.Insensitive, "dev-password refusal does not print the password on stderr");
    }

    [Test]
    public void Drill_volume_guard()
    {
        foreach (var name in new[] { "antiphon_pgdata", "antiphon-server2-postgres_pgdata", "pgdata", "dind-data", "windmill_db" })
            Server2PostgresDeploy.DrillVolumeRefusal(name).ShouldBe("volume-forbidden", "drill refuses " + name);
        Server2PostgresDeploy.DrillVolumeRefusal("antiphon-server2-postgres-drill_pgdata").ShouldBe("", "drill allows its own scratch volume");
        var source = DockerStackDocuments.Read(DeploySource);
        source.ShouldNotContain("volume rm antiphon_pgdata", Case.Insensitive, "the command has no antiphon_pgdata volume rm");
        source.ShouldNotContain("volume rm antiphon-server2-postgres_pgdata", Case.Insensitive, "the command has no production volume rm");
    }

    [Test]
    public void Tailnet_guard()
    {
        Server2PostgresDeploy.Tailnet(Server2PostgresWorld.Bind, Server2PostgresWorld.Bind + "\n").ShouldBe("", "tailnet match is accepted");
        Server2PostgresDeploy.Tailnet("192.168.4.34", "100.64.0.2\n").ShouldBe("bind-not-tailnet", "a LAN address is not the tailnet address");
        Server2PostgresDeploy.Tailnet(Server2PostgresWorld.Bind, "").ShouldBe("bind-not-tailnet", "an empty tailscale reading is refused");
    }

    [Test]
    public void Ambiguous_mode()
    {
        var world = new Server2PostgresWorld();
        Refuses(world, "ambiguous-mode", "--what-if", "--execute");
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
    public void Network_block()
    {
        // The refusal needs the exact block and attachment, not a substring of them (the C# literals carry the line ends).
        var source = DockerStackDocuments.Read(DeploySource);
        source.ShouldContain("\"networks:\\n  antiphon-server2:\\n    name: antiphon-server2\"", Case.Sensitive, "deploy refusal requires the exact network block");
        source.ShouldContain("\"    networks:\\n      - antiphon-server2\"", Case.Sensitive, "deploy refusal requires the service network attachment");
        var world = new Server2PostgresWorld();
        var stripped = Production.Replace("    networks:\n      - antiphon-server2\n", "").Replace("networks:\n  antiphon-server2:\n    name: antiphon-server2\n\n", "");
        stripped.ShouldNotContain("networks:\n  antiphon-server2:\n    name: antiphon-server2", Case.Sensitive, "stripped compose has no network block");
        var result = world.Run("--what-if", "--env-file", world.Env(), "--compose-file", world.Compose("compose.yml", stripped));
        (result.Exit, result.Reason).ShouldBe((2, "compose-invalid"), "WhatIf refuses a compose with no network block");
        NoDocker(result, "network-block");
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

    // Get-RenderedFields' four fields, read from the file's YAML instead of `docker compose config` (no docker): the pinned volume
    // name (a volume with an explicit name keeps it under every project name), the publish's host_ip and published port, and the
    // network's name.
    private static (string Volume, string HostIp, string Published, string Network) Fields(string text)
    {
        var yaml = new YamlStream();
        yaml.Load(new StringReader(text));
        var root = (YamlMappingNode)yaml.Documents.Single().RootNode;
        static YamlNode Get(YamlNode node, string key) => ((YamlMappingNode)node).Children[new YamlScalarNode(key)];
        var volume = ((YamlScalarNode)Get(Get(Get(root, "volumes"), "pgdata"), "name")).Value ?? "";
        var network = ((YamlScalarNode)Get(((YamlMappingNode)Get(root, "networks")).Children.Single().Value, "name")).Value ?? "";
        var port = ((YamlScalarNode)((YamlSequenceNode)Get(Get(Get(root, "services"), "postgres"), "ports")).Children.Single()).Value ?? "";
        var parts = port.Split(':');
        return parts.Length == 3 ? (volume, parts[0], parts[1], network) : (volume, "", "", network);
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

    // Get-DrillCommandBodies.
    private static string[] Bodies(string text) =>
        text.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.StartsWith("drill-command:", StringComparison.Ordinal) && line.Contains(" :: ", StringComparison.Ordinal))
            .Select(line => line.Substring(line.IndexOf(" :: ", StringComparison.Ordinal) + 4)).ToArray();

    // Get-DocDrillFence: the first ```text fence after "### Scratch restore drill".
    private static string[] DocFence(string doc)
    {
        var at = doc.IndexOf("### Scratch restore drill", StringComparison.Ordinal);
        if (at < 0) return [];
        var fence = doc.IndexOf("```text\n", at, StringComparison.Ordinal);
        if (fence < 0) return [];
        var body = fence + "```text\n".Length;
        var end = doc.IndexOf("\n```", body, StringComparison.Ordinal);
        if (end < 0) return [];
        return doc.Substring(body, end - body).Split('\n').Where(line => line.Length > 0).ToArray();
    }

    // Get-DrillSection: from the heading to the next "## " heading.
    private static string DrillSection(string doc)
    {
        var at = doc.IndexOf("### Scratch restore drill", StringComparison.Ordinal);
        if (at < 0) return "";
        var next = doc.IndexOf("\n## ", at + 1, StringComparison.Ordinal);
        return next < 0 ? doc.Substring(at) : doc.Substring(at, next - at);
    }

    /// <summary>The words sh makes of a command line (blanks, single quotes, double quotes with POSIX backslash rules, backslash).</summary>
    internal static List<string> ShWords(string line)
    {
        var words = new List<string>();
        var word = new StringBuilder();
        var inWord = false;
        for (var i = 0; i < line.Length;)
        {
            var c = line[i];
            if (c == ' ' || c == '\t')
            {
                if (inWord) words.Add(word.ToString());
                word.Clear();
                inWord = false;
                i++;
                continue;
            }
            inWord = true;
            if (c == '\'')
            {
                var end = line.IndexOf('\'', i + 1);
                if (end < 0) throw new FormatException("unterminated single quote");
                word.Append(line, i + 1, end - i - 1);
                i = end + 1;
            }
            else if (c == '"')
            {
                i++;
                while (true)
                {
                    if (i >= line.Length) throw new FormatException("unterminated double quote");
                    if (line[i] == '"') break;
                    if (line[i] == '$' || line[i] == '`') throw new FormatException("an expansion inside double quotes");
                    if (line[i] == '\\' && i + 1 < line.Length && "$`\"\\".IndexOf(line[i + 1]) >= 0)
                    {
                        word.Append(line[i + 1]);
                        i += 2;
                        continue;
                    }
                    word.Append(line[i]);
                    i++;
                }
                i++;
            }
            else if (c == '\\' && i + 1 < line.Length)
            {
                word.Append(line[i + 1]);
                i += 2;
            }
            else
            {
                word.Append(c);
                i++;
            }
        }
        if (inWord) words.Add(word.ToString());
        return words;
    }

    [Test]
    public void Drill_commands()
    {
        var world = new Server2PostgresWorld();
        var result = world.Run("--what-if", "--restore-drill");
        (result.Exit, result.Reason).ShouldBe((0, "ok"), "drill command plan exits 0");
        foreach (var need in new[]
        {
            "docker-compose.server2-postgres-drill.yml", "-p antiphon-server2-postgres-drill up -d", "-p antiphon-server2-postgres-drill down",
            "docker volume create antiphon-server2-postgres-drill_pgdata", "docker volume rm antiphon-server2-postgres-drill_pgdata", "*-drill_pgdata",
            "postgres-snapshot.ps1 -Execute", ".dump.age", "age -d -i \"$AGE_IDENTITY\"", "scp \"$SNAPSHOT\"", "copy_start=$(date +%s)", "chmod 0600", "shred -u",
            "pg_restore -U antiphon -d antiphon --no-owner --no-acl", "antiphon-server2-postgres-drill-postgres-1", "pg_stat_user_tables", "__EFMigrationsHistory",
            "\\\"__EFMigrationsHistory\\\"", "pg_extension",
        })
            result.Out.ShouldContain(need, Case.Sensitive, "drill command contains " + need);
        result.Out.ShouldNotContain("pg_dump", Case.Insensitive, "drill plan does not print a plaintext pg_dump");
        result.Out.ShouldNotContain("AGE-SECRET-KEY-", Case.Insensitive, "drill plan does not print an age key");
        Regex.IsMatch(result.Out, @"(?m)>\s*\S+\.dump(?!\.age)", RegexOptions.IgnoreCase).ShouldBeFalse("no command writes a plaintext dump");
        Regex.IsMatch(result.Out, @"shred[^\n]*\.dump\.age", RegexOptions.IgnoreCase).ShouldBeFalse("shred does not delete the ciphertext");
        var doc = Read("docs/docker-stack.md");
        DrillSection(doc).ShouldContain("encrypted", Case.Sensitive, "drill section says encrypted");
        Regex.IsMatch(result.Out, @"docker-compose\.server2-postgres\.yml", RegexOptions.IgnoreCase).ShouldBeFalse("drill plan does not name the production compose");
        result.Out.ShouldNotContain("-p antiphon-server2-postgres up", Case.Insensitive, "drill plan does not start the production project");
        result.Out.ShouldNotContain("antiphon-server2-postgres_pgdata", Case.Insensitive, "drill plan does not name the production volume");
        result.Out.ShouldNotContain("antiphon_pgdata", Case.Insensitive, "drill plan does not name antiphon_pgdata");
        var lines = result.Lines.Where(line => line.Contains("drill-command:", StringComparison.OrdinalIgnoreCase)).ToArray();
        lines.Length.ShouldBeGreaterThanOrEqualTo(9, "drill prints one line per command");
        foreach (var line in lines)
        {
            (line.Contains("antiphon-server2-postgres-drill") && line.Contains("antiphon-server2-postgres-drill_pgdata")).ShouldBeTrue("drill command names the drill project and volume: " + line);
            line.ShouldNotContain("antiphon-server2-postgres_pgdata", Case.Insensitive, "drill command cannot reach the production volume");
        }

        var bodies = Bodies(result.Out);
        var docCommands = DocFence(doc);
        docCommands.Length.ShouldBeGreaterThan(0, "doc drill fence has commands");
        bodies.ShouldBe(docCommands, "printed drill commands equal the doc fence");

        var dump = lines.Where(line => Regex.IsMatch(line, "antiphon-postgres", RegexOptions.IgnoreCase)).ToArray();
        dump.Length.ShouldBe(1, "the desktop database is named once");
        (Regex.IsMatch(dump[0], @"postgres-snapshot\.ps1", RegexOptions.IgnoreCase) && Regex.IsMatch(dump[0], "-Execute", RegexOptions.IgnoreCase)
            && Regex.IsMatch(dump[0], "-RecipientFile \"\\$AGE_RECIPIENT\"", RegexOptions.IgnoreCase)
            && !Regex.IsMatch(dump[0], "pg_dump|>|psql|pg_restore|dropdb|createdb|DELETE|INSERT|UPDATE|DROP", RegexOptions.IgnoreCase))
            .ShouldBeTrue("the desktop step takes the encrypted snapshot: " + dump[0]);

        var migrations = bodies.Where(body => body.Contains("__EFMigrationsHistory", StringComparison.OrdinalIgnoreCase)).ToArray();
        migrations.Length.ShouldBe(1, "one migrations command");
        var docMigrations = docCommands.Where(body => body.Contains("__EFMigrationsHistory", StringComparison.OrdinalIgnoreCase)).ToArray();
        docMigrations.Length.ShouldBe(1, "printed migrations line equals the doc line");
        migrations[0].ShouldBe(docMigrations[0], "printed migrations line equals the doc line");
        var psql = Regex.Replace(migrations[0], @"^docker exec \S+ ", "");
        psql.ShouldStartWith("psql ", Case.Sensitive, "migrations line is a psql command");
        // sh keeps the migrations identifier quotes: the argument after -c, as sh hands it to psql.
        var words = ShWords(psql);
        words[words.IndexOf("-c") + 1].ShouldBe("select \"MigrationId\" from \"__EFMigrationsHistory\" order by \"MigrationId\"");
        NoDocker(result, "drill-commands");
    }

    [Test]
    public void Project_refusal()
    {
        var world = new Server2PostgresWorld();
        var wrongProject = world.Run("--what-if", "--project", "antiphon-server2-postgres-drill");
        (wrongProject.Exit, wrongProject.Reason).ShouldBe((2, "production-compose-wrong-project"), "WhatIf refuses the production compose under the drill project");
        NoDocker(wrongProject, "wrong-project");
        var wrongDrill = world.Run("--what-if", "--restore-drill", "--project", "antiphon-server2-postgres");
        (wrongDrill.Exit, wrongDrill.Reason).ShouldBe((2, "drill-compose-wrong-project"), "WhatIf refuses the drill under the production project");
        NoDocker(wrongDrill, "wrong-drill-project");
        var prodFile = world.Run("--what-if", "--restore-drill", "--compose-file", world.ProductionCompose);
        (prodFile.Exit, prodFile.Reason).ShouldBe((2, "production-compose-wrong-project"), "RestoreDrill refuses the production compose file");
        NoDocker(prodFile, "prod-file-drill");
        var drillFile = world.Run("--what-if", "--compose-file", world.DrillCompose);
        (drillFile.Exit, drillFile.Reason).ShouldBe((2, "drill-compose-wrong-project"), "a normal plan refuses the drill compose file");
        NoDocker(drillFile, "drill-file-prod");
    }

    [Test]
    public void Refuse_bind_cgnat()
    {
        Server2PostgresDeploy.Bind(true, "100.64.0.0").ShouldBe("", "CGNAT range bottom is accepted");
        Server2PostgresDeploy.Bind(true, "100.127.255.255").ShouldBe("", "CGNAT range top is accepted");
        Server2PostgresDeploy.Bind(true, "100.63.255.255").ShouldBe("bind-not-cgnat", "an address below CGNAT is refused");
        Server2PostgresDeploy.Bind(true, "100.128.0.1").ShouldBe("bind-not-cgnat", "an address above CGNAT is refused");
        Server2PostgresDeploy.Bind(true, "192.168.4.34").ShouldBe("bind-not-cgnat", "a LAN address is outside CGNAT");
        Server2PostgresDeploy.Bind(true, "8.8.8.8").ShouldBe("bind-not-cgnat", "a public address is outside CGNAT");
        var world = new Server2PostgresWorld();
        var result = world.Run("--what-if", "--env-file", world.Env(bind: "192.168.4.34"));
        (result.Exit, result.Reason).ShouldBe((2, "bind-not-cgnat"), "WhatIf refuses a LAN bind");
        NoDocker(result, "cgnat");
    }

    [Test]
    public void Image_repodigest()
    {
        var pin = "postgres@sha256:" + new string('c', 64);
        Server2PostgresDeploy.Image(pin).ShouldBe("", "RepoDigest postgres@sha256 is a digest pin");
        Server2PostgresDeploy.Image("postgres:latest").ShouldBe("image-floating", "a floating latest tag is refused");
        Server2PostgresDeploy.Image("postgres@sha256:" + new string('d', 12)).ShouldBe("image-floating", "a short digest is refused");
        var world = new Server2PostgresWorld();
        var result = world.Run("--what-if", "--env-file", world.Env(image: pin));
        (result.Exit, result.Reason).ShouldBe((0, "ok"), "WhatIf accepts a postgres RepoDigest pin");
        NoDocker(result, "repodigest");
    }

    [Test]
    public void Image_patch_pin()
    {
        // Runbook G-2: the env file may state the exact patch version beside the digest.
        var digest = "@sha256:" + new string('9', 64);
        foreach (var pin in new[] { "postgres:16.14" + digest, "postgres:16.14-trixie" + digest, "postgres:16.0" + digest })
            Server2PostgresDeploy.Image(pin).ShouldBe("", "patch pin accepted: " + pin.Split('@')[0]);
        foreach (var floating in new[]
        {
            "postgres:16.14", "postgres:16.14-trixie", "postgres:16", "postgres:latest", "postgres:latest" + digest, "postgres:16.14-bookworm" + digest,
            "postgres:17.2" + digest, "postgres:16.14.1" + digest, "postgres:16." + digest, "postgres:16.014" + digest, "postgres:16.14@sha256:" + new string('9', 12),
            "postgres:16.14@sha256:" + new string('A', 64), "library/postgres:16.14" + digest,
        })
            Server2PostgresDeploy.Image(floating).ShouldBe("image-floating", "still floating: " + floating.Split('@')[0]);
        var world = new Server2PostgresWorld();
        var result = world.Run("--what-if", "--env-file", world.Env(image: "postgres:16.14" + digest));
        (result.Exit, result.Reason).ShouldBe((0, "ok"), "WhatIf accepts postgres:16.14@sha256");
        NoDocker(result, "patch-pin");
        result = world.Run("--what-if", "--env-file", world.Env(image: "postgres:16.14"));
        result.Exit.ShouldNotBe(0);
        result.Reason.ShouldBe("image-floating", "WhatIf refuses a digest-less patch tag");
        NoDocker(result, "patch-no-digest");
    }

    // Get-ComposeServiceBlocks: each service's lines under the top-level services: key, in order.
    private static List<(string Name, List<string> Lines)> ServiceBlocks(string text)
    {
        var blocks = new List<(string Name, List<string> Lines)>();
        var inServices = false;
        List<string>? current = null;
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (Regex.IsMatch(raw, @"^[^\s#]"))
            {
                inServices = raw.TrimEnd() == "services:";
                current = null;
                continue;
            }
            if (!inServices) continue;
            var head = Regex.Match(raw, @"^  ([A-Za-z0-9._-]+):\s*$");
            if (head.Success)
            {
                current = [];
                blocks.Add((head.Groups[1].Value, current));
                continue;
            }
            current?.Add(raw);
        }
        return blocks;
    }

    // Get-ServiceCpusets: each cpuset line's value, or "malformed".
    private static string[] Cpusets(IEnumerable<string> lines) =>
        lines.Where(line => Regex.IsMatch(line, @"^\s*cpuset\s*:", RegexOptions.IgnoreCase))
            .Select(line => Regex.Match(line, "^    cpuset: \"([0-9,-]+)\"$", RegexOptions.IgnoreCase) is { Success: true } exact ? exact.Groups[1].Value : "malformed").ToArray();

    // ConvertFrom-Cpuset: the CPUs of a list of numbers and ranges below the host's count; null when it does not parse.
    private static SortedSet<int>? ParseCpuset(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return null;
        var set = new SortedSet<int>();
        foreach (var part in spec.Split(','))
        {
            var one = Regex.Match(part, "^([0-9]+)$");
            var range = Regex.Match(part, "^([0-9]+)-([0-9]+)$");
            int lo, hi;
            if (one.Success) lo = hi = int.Parse(one.Groups[1].Value);
            else if (range.Success)
            {
                lo = int.Parse(range.Groups[1].Value);
                hi = int.Parse(range.Groups[2].Value);
            }
            else return null;
            if (lo > hi || hi >= HostCpuCount) return null;
            for (var cpu = lo; cpu <= hi; cpu++) set.Add(cpu);
        }
        return set;
    }

    [Test]
    public void Cpu_isolation()
    {
        var pgSet = ParseCpuset(PostgresCpuset);
        pgSet.ShouldNotBeNull("declared Postgres cpuset parses");
        (pgSet!.Count == 2 && pgSet.Contains(pgSet.Min + SmtSiblingOffset)).ShouldBeTrue("Postgres cpuset is one physical core (a CPU and its SMT sibling)");
        (pgSet.Contains(0) && pgSet.Contains(12)).ShouldBeTrue("Postgres cpuset is CPU 0 and its sibling 12");
        ParseCpuset("3-24").ShouldBeNull("a cpuset past the host CPU count does not parse");
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
