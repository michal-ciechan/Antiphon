using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Time.Testing;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// Unit 53: the docker and age stand-ins of the deleted scripts/test-postgres-snapshot.ps1, in process. The driver's POSIX sh fakes
/// (Install-SnapshotFake for the desktop and age, Install-SnapshotServer2Fake for server2) answered by scenario through
/// environment variables and a state file; this answers the same scenarios through properties, so every case runs on Windows
/// and Linux alike. Nothing is started.
/// </summary>
internal sealed class SnapshotDockerFake
{
    public const string SourceId = "0123abcd9f0123456789abcdef0123456789abcd0123456789abcdef012345";
    public const string ScratchId = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
    public const string Server2ImageId = "sha256:8a4b045886180c64f518ca6a4b0791b15fb997f3b3b619bd5ee141914668db55";

    private const string SourceJson = """{"Id":"0123abcd9f0123456789abcdef0123456789abcd0123456789abcdef012345","Name":"/antiphon-postgres","Config":{"Labels":{}},"Mounts":[{"Name":"antiphon_pgdata","Destination":"/var/lib/postgresql/data"}],"NetworkSettings":{"Networks":{"bridge":{"Aliases":["antiphon-postgres"]}}}}""";
    private const string AliasJson = """{"Id":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","Name":"/snapshot-alias","Config":{"Labels":{"antiphon.snapshot-drill":"1"}},"Mounts":[{"Name":"antiphon-postgres-drill-pgdata","Destination":"/var/lib/postgresql/data"}],"NetworkSettings":{"Networks":{"bridge":{"Aliases":["antiphon-postgres","snapshot-alias"]}}}}""";
    private const string UnlabelledJson = """{"Id":"dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd","Name":"/antiphon-postgres-drill","Config":{"Labels":{}},"Mounts":[{"Name":"antiphon-postgres-drill-pgdata","Destination":"/var/lib/postgresql/data"}],"NetworkSettings":{"Networks":{"bridge":{"Aliases":["antiphon-postgres-drill"]}}}}""";
    private const string MountJson = """{"Id":"eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee","Name":"/antiphon-postgres-drill","Config":{"Labels":{"antiphon.snapshot-drill":"1"}},"Mounts":[{"Name":"antiphon_pgdata","Destination":"/var/lib/postgresql/data"}],"NetworkSettings":{"Networks":{"bridge":{"Aliases":["antiphon-postgres-drill"]}}}}""";
    // The official postgres image sets PG_MAJOR, PG_VERSION and PGDATA itself (CARD-1413 D-1).
    private const string ImageEnv = "\"Env\":[\"PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:/usr/lib/postgresql/16/bin\",\"GOSU_VERSION=1.17\",\"LANG=en_US.utf8\",\"PG_MAJOR=16\",\"PG_VERSION=16.4-1.pgdg120+1\",\"PGDATA=/var/lib/postgresql/data\",\"POSTGRES_HOST_AUTH_METHOD=trust\",\"POSTGRES_USER=antiphon\"]";
    private const string CleanJson = "{\"Id\":\"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc\",\"Name\":\"/antiphon-postgres-drill\",\"Config\":{\"Labels\":{\"antiphon.snapshot-drill\":\"1\"}," + ImageEnv + "},\"HostConfig\":{\"NetworkMode\":\"none\"},\"Mounts\":[{\"Type\":\"volume\",\"Name\":\"antiphon-postgres-drill-pgdata\",\"Destination\":\"/var/lib/postgresql/data\"}],\"NetworkSettings\":{\"Networks\":{}}}";
    private const string Server2Json = """{"Id":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","Name":"/antiphon-server2-postgres-postgres-1","Config":{"Labels":{}},"Mounts":[{"Name":"antiphon-server2-postgres_pgdata","Destination":"/var/lib/postgresql/data"}],"NetworkSettings":{"Networks":{"bridge":{"Aliases":["postgres"]}}}}""";
    private const string CustomSourceJson = """{"Id":"1212121212121212121212121212121212121212121212121212121212121212","Name":"/custom-pg","Config":{"Labels":{}},"Mounts":[{"Name":"custom_pgdata","Destination":"/var/lib/postgresql/data"}],"NetworkSettings":{"Networks":{"bridge":{"Aliases":["custom-pg"]}}}}""";
    private const string LabelledSourceJson = """{"Id":"3434343434343434343434343434343434343434343434343434343434343434","Name":"/antiphon-postgres-drill","Config":{"Labels":{"antiphon.snapshot-drill":"1"}},"Mounts":[{"Name":"custom_pgdata","Destination":"/var/lib/postgresql/data"}],"NetworkSettings":{"Networks":{"bridge":{"Aliases":["antiphon-postgres-drill"]}}}}""";
    private const string MismatchJson = """{"Id":"5656565656565656565656565656565656565656565656565656565656565656","Name":"/antiphon-postgres-drill","Config":{"Labels":{"antiphon.snapshot-drill":"1"}},"Mounts":[{"Name":"scratcha","Destination":"/var/lib/postgresql/data"}],"NetworkSettings":{"Networks":{"bridge":{"Aliases":["antiphon-postgres-drill"]}}}}""";
    private const string PgHostJson = """{"Id":"ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff","Name":"/antiphon-postgres-drill","Config":{"Labels":{"antiphon.snapshot-drill":"1"},"Env":["PGHOST=db.internal","PGPORT=5432"]},"Mounts":[{"Name":"antiphon-postgres-drill-pgdata","Destination":"/var/lib/postgresql/data"}],"NetworkSettings":{"Networks":{"bridge":{"Aliases":["antiphon-postgres-drill"]}}}}""";
    private const string OwnRunningJson = """{"Id":"7878787878787878787878787878787878787878787878787878787878787878","Name":"/antiphon-postgres-drill","Config":{"Labels":{"antiphon.snapshot-drill":"1"}},"Mounts":[{"Name":"custom_pgdata","Destination":"/var/lib/postgresql/data"}],"NetworkSettings":{"Networks":{"bridge":{"Aliases":["antiphon-postgres-drill"]}}}}""";
    private const string PgHostAddrJson = """{"Id":"9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a","Name":"/antiphon-postgres-drill","Config":{"Labels":{"antiphon.snapshot-drill":"1"},"Env":["PGHOSTADDR=10.0.0.5"]},"HostConfig":{"NetworkMode":"none"},"Mounts":[{"Type":"volume","Name":"antiphon-postgres-drill-pgdata","Destination":"/var/lib/postgresql/data"}],"NetworkSettings":{"Networks":{}}}""";
    private const string PgEnvJson = """{"Id":"9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b","Name":"/antiphon-postgres-drill","Config":{"Labels":{"antiphon.snapshot-drill":"1"},"Env":["PATH=/usr/bin","PG_MAJOR=16","PG_VERSION=16.4-1.pgdg120+1","PGDATA=/var/lib/postgresql/data","PGSERVICE=production"]},"HostConfig":{"NetworkMode":"none"},"Mounts":[{"Type":"volume","Name":"antiphon-postgres-drill-pgdata","Destination":"/var/lib/postgresql/data"}],"NetworkSettings":{"Networks":{}}}""";
    private const string PgPassFileJson = """{"Id":"9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f","Name":"/antiphon-postgres-drill","Config":{"Labels":{"antiphon.snapshot-drill":"1"},"Env":["PG_MAJOR=16","PG_VERSION=16.4-1.pgdg120+1","PGDATA=/var/lib/postgresql/data","PGPASSFILE=/run/secrets/pgpass"]},"HostConfig":{"NetworkMode":"none"},"Mounts":[{"Type":"volume","Name":"antiphon-postgres-drill-pgdata","Destination":"/var/lib/postgresql/data"}],"NetworkSettings":{"Networks":{}}}""";
    private const string PgSslJson = """{"Id":"8a8a8a8a8a8a8a8a8a8a8a8a8a8a8a8a8a8a8a8a8a8a8a8a8a8a8a8a8a8a8a8a","Name":"/antiphon-postgres-drill","Config":{"Labels":{"antiphon.snapshot-drill":"1"},"Env":["PG_MAJOR=16","PG_VERSION=16.4-1.pgdg120+1","PGDATA=/var/lib/postgresql/data","PGSSLMODE=verify-full","PGSSLROOTCERT=/certs/root.crt"]},"HostConfig":{"NetworkMode":"none"},"Mounts":[{"Type":"volume","Name":"antiphon-postgres-drill-pgdata","Destination":"/var/lib/postgresql/data"}],"NetworkSettings":{"Networks":{}}}""";
    private const string BindJson = """{"Id":"9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c","Name":"/antiphon-postgres-drill","Config":{"Labels":{"antiphon.snapshot-drill":"1"}},"HostConfig":{"NetworkMode":"none"},"Mounts":[{"Type":"volume","Name":"antiphon-postgres-drill-pgdata","Destination":"/var/lib/postgresql/data"},{"Type":"bind","Source":"/var/lib/docker/volumes/antiphon_pgdata/_data","Destination":"/mnt/live"}],"NetworkSettings":{"Networks":{}}}""";
    private const string HostNetJson = """{"Id":"9d9d9d9d9d9d9d9d9d9d9d9d9d9d9d9d9d9d9d9d9d9d9d9d9d9d9d9d9d9d9d9d","Name":"/antiphon-postgres-drill","Config":{"Labels":{"antiphon.snapshot-drill":"1"}},"HostConfig":{"NetworkMode":"host"},"Mounts":[{"Type":"volume","Name":"antiphon-postgres-drill-pgdata","Destination":"/var/lib/postgresql/data"}],"NetworkSettings":{"Networks":{"host":{"Aliases":[]}}}}""";
    private const string SharedNetJson = """{"Id":"9e9e9e9e9e9e9e9e9e9e9e9e9e9e9e9e9e9e9e9e9e9e9e9e9e9e9e9e9e9e9e9e","Name":"/antiphon-postgres-drill","Config":{"Labels":{"antiphon.snapshot-drill":"1"}},"HostConfig":{"NetworkMode":"container:0123abcd9f0123456789abcdef0123456789abcd0123456789abcdef012345"},"Mounts":[{"Type":"volume","Name":"antiphon-postgres-drill-pgdata","Destination":"/var/lib/postgresql/data"}],"NetworkSettings":{"Networks":{}}}""";
    private const string TableData = "3000; 0 12345 TABLE DATA public cards antiphon";

    /// <summary>The server2 fake (Install-SnapshotServer2Fake) instead of the desktop one.</summary>
    public bool Server2 { get; set; }

    /// <summary>CARD1405_SCENARIO (desktop) or CARD1831_S2_SCENARIO (server2: good, drill-project, drill-volume, wrong-dest, stopped).</summary>
    public string Scenario { get; set; } = "";

    /// <summary>CARD1405_DUMP_BYTES: wc -c's answer and the bytes a cp writes (desktop 0, server2 1048576 by default).</summary>
    public long? DumpBytes { get; set; }

    /// <summary>CARD1405_TABLE_DATA: pg_restore --list names TABLE DATA (desktop scenarios other than the drill guards).</summary>
    public bool HasTableData { get; set; }

    /// <summary>CARD1405_PLAIN_BYTES: what age -o writes.</summary>
    public long PlainBytes { get; set; } = 16;

    /// <summary>CARD1405_AGE_EXIT.</summary>
    public int AgeExit { get; set; }

    /// <summary>CARD1405_DOCKER_ENDPOINT: context inspect's answer; null is the fake's default (unix socket, or ssh for server2).</summary>
    public string? Endpoint { get; set; }

    /// <summary>The state file: docker run -d has started the scratch container.</summary>
    public bool State { get; set; }

    /// <summary>The state.volumes file: volumes that exist with the drill label.</summary>
    public HashSet<string> Volumes { get; } = new(StringComparer.Ordinal);

    /// <summary>The scratch container's id does not answer docker inspect (the drill cleanup then leaves it).</summary>
    public bool IdInspectFails { get; set; }

    /// <summary>A test's own answer for one docker argv (null falls through to the scenario).</summary>
    public Func<IReadOnlyList<string>, NightlyLaunchResult?>? Override { get; set; }

    public static NightlyLaunchResult Answer(int exit, params string[] lines)
    {
        var text = string.Concat(lines.Select(line => line + "\n"));
        return new NightlyLaunchResult { ExitCode = exit, LogText = text, StandardOutputText = text };
    }

    public NightlyLaunchResult Run(NightlyLaunch launch)
    {
        var name = Path.GetFileNameWithoutExtension(launch.FileName);
        var args = launch.Arguments.ToList();
        if (name == "age") return Age(args);
        if (Override?.Invoke(args) is { } answer) return answer;
        return Server2 ? Server2Docker(args) : Desktop(args);
    }

    private NightlyLaunchResult Age(List<string> args)
    {
        var index = args.IndexOf("-o");
        if (index >= 0 && index + 1 < args.Count) File.WriteAllBytes(args[index + 1], new byte[PlainBytes]);
        return Answer(AgeExit);
    }

    private static bool HostPath(string path) => System.Text.RegularExpressions.Regex.IsMatch(path, @"^[A-Za-z]:[\\/]") || !path.Contains(':');

    private string ListLine() =>
        new[] { "clean", "prefix", "alias", "unlabelled", "mount", "production-name", "database" }.Contains(Scenario) || HasTableData
            ? TableData : "; Archive created at 2026-10-21";

    private NightlyLaunchResult Desktop(List<string> args)
    {
        var cmd = args[0];
        var rest = args.Skip(1).ToList();
        var target = rest.Count > 0 ? rest[^1] : "";
        var bytes = (DumpBytes ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture);
        switch (cmd)
        {
            case "inspect":
                if (target == "antiphon-postgres") return Answer(0, SourceJson);
                if (target == "antiphon-server2-postgres-postgres-1") return Scenario == "production-name" ? Answer(0, Server2Json) : Answer(1);
                if (target == "custom-pg") return Answer(0, CustomSourceJson);
                if (IdInspectFails && target == ScratchId) return Answer(1);
                return Scenario switch
                {
                    "prefix" => Answer(0, SourceJson),
                    "alias" => Answer(0, AliasJson),
                    "unlabelled" or "database" => Answer(0, UnlabelledJson),
                    "mount" => Answer(0, MountJson),
                    "production-name" => Answer(0, Server2Json),
                    "clean" or "foreign-volume" or "plain-volume" => State ? Answer(0, CleanJson) : Answer(1),
                    "own-volume" => State ? Answer(0, OwnRunningJson) : Answer(1),
                    "labelled-source" => Answer(0, LabelledSourceJson),
                    "mismatch" => Answer(0, MismatchJson),
                    "pghostaddr" => Answer(0, PgHostAddrJson),
                    "pgenv" => Answer(0, PgEnvJson),
                    "pgpassfile" => Answer(0, PgPassFileJson),
                    "pgssl" => Answer(0, PgSslJson),
                    "reuse" => Answer(0, CleanJson),
                    "bind" => Answer(0, BindJson),
                    "hostnet" => Answer(0, HostNetJson),
                    "sharednet" => Answer(0, SharedNetJson),
                    "pghost" => Answer(0, PgHostJson),
                    _ => Answer(1),
                };
            case "run":
            {
                var joined = " " + string.Join(" ", rest);
                if (joined.Contains("--list")) return Answer(0, ListLine());
                if (joined.Contains(" -d ")) State = true;
                return Answer(0);
            }
            case "exec":
            {
                var joined = " " + string.Join(" ", rest);
                if (joined.Contains("pg_restore")) return Answer(0, ListLine());
                if (joined.Contains(" wc ") || joined.EndsWith(" wc", StringComparison.Ordinal)) return Answer(0, bytes + " /tmp/antiphon-snapshot.dump");
                return Answer(0);
            }
            case "cp":
                if (Scenario == "publish" && HostPath(target)) File.WriteAllBytes(target, new byte[DumpBytes ?? 0]);
                return Answer(0);
            case "volume":
                return Volume(rest, target, Scenario);
            case "context":
                return Answer(0, Endpoint ?? "unix:///var/run/docker.sock");
            case "ps":
            {
                var volume = rest.FirstOrDefault(arg => arg.StartsWith("volume=", StringComparison.Ordinal))?.Substring("volume=".Length) ?? "";
                return Scenario == "foreign-volume" && volume == "windmill_db" ? Answer(0, "abcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcd") : Answer(0);
            }
            default:
                return Answer(0);
        }
    }

    private NightlyLaunchResult Volume(List<string> rest, string target, string scenario)
    {
        switch (rest[0])
        {
            case "inspect":
                if ((scenario == "plain-volume" || scenario == "foreign-volume") && target == "windmill_db") return Answer(0, """{"Name":"windmill_db","Labels":{}}""");
                return Volumes.Contains(target) ? Answer(0, "{\"Name\":\"" + target + "\",\"Labels\":{\"antiphon.snapshot-drill\":\"1\"}}") : Answer(1);
            case "create":
                Volumes.Add(target);
                return Answer(0);
            default:
                return Answer(0);
        }
    }

    private NightlyLaunchResult Server2Docker(List<string> argv)
    {
        var args = argv[0] == "--context" ? argv.Skip(2).ToList() : argv;
        var cmd = args[0];
        var rest = args.Skip(1).ToList();
        var target = rest.Count > 0 ? rest[^1] : "";
        var bytes = DumpBytes ?? 1048576;
        string running = "true", project = "antiphon-server2-postgres", volume = "antiphon-server2-postgres_pgdata", destination = "/var/lib/postgresql/data";
        switch (Scenario)
        {
            case "drill-project": project = "antiphon-server2-postgres-drill"; break;
            case "drill-volume": volume = "antiphon-server2-postgres-drill_pgdata"; break;
            case "wrong-dest": destination = "/data"; break;
            case "stopped": running = "false"; break;
        }
        const string env = "\"Env\":[\"PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:/usr/lib/postgresql/16/bin\",\"LANG=en_US.utf8\",\"PG_MAJOR=16\",\"PG_VERSION=16.14-1.pgdg13+1\",\"PGDATA=/var/lib/postgresql/data\"]";
        var source = "{\"Id\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"Name\":\"/antiphon-server2-postgres-postgres-1\",\"Image\":\"" + Server2ImageId
            + "\",\"State\":{\"Running\":" + running + "},\"Config\":{\"Labels\":{\"com.docker.compose.project\":\"" + project + "\",\"com.docker.compose.service\":\"postgres\"},"
            + env + "},\"HostConfig\":{\"NetworkMode\":\"antiphon-server2-postgres_default\"},\"Mounts\":[{\"Type\":\"volume\",\"Name\":\"" + volume + "\",\"Destination\":\""
            + destination + "\"}],\"NetworkSettings\":{\"Networks\":{\"antiphon-server2-postgres_default\":{\"Aliases\":[\"postgres\"]}}}}";
        var scratch = "{\"Id\":\"" + ScratchId + "\",\"Name\":\"/antiphon-postgres-drill\",\"Image\":\"" + Server2ImageId
            + "\",\"State\":{\"Running\":true},\"Config\":{\"Labels\":{\"antiphon.snapshot-drill\":\"1\"}," + env
            + "},\"HostConfig\":{\"NetworkMode\":\"none\"},\"Mounts\":[{\"Type\":\"volume\",\"Name\":\"antiphon-postgres-drill-pgdata\",\"Destination\":\"/var/lib/postgresql/data\"}],\"NetworkSettings\":{\"Networks\":{}}}";
        switch (cmd)
        {
            case "context":
                return Answer(0, Endpoint ?? "ssh://fake@server2");
            case "inspect":
                if (target == "antiphon-server2-postgres-postgres-1") return Answer(0, source);
                if ((target == "antiphon-postgres-drill" || target == ScratchId) && State) return Answer(0, scratch);
                return Answer(1);
            case "run":
                if ((" " + string.Join(" ", rest)).Contains(" -d "))
                {
                    State = true;
                    return Answer(0, ScratchId);
                }
                return Answer(0);
            case "exec":
            {
                var joined = " " + string.Join(" ", rest);
                if (joined.Contains("--version")) return Answer(0, "postgres (PostgreSQL) 16.14 (Debian 16.14-1.pgdg13+1)");
                if (joined.Contains("pg_restore") && joined.IndexOf("--list", joined.IndexOf("pg_restore", StringComparison.Ordinal), StringComparison.Ordinal) >= 0)
                    return Answer(0, TableData, "3001; 0 12346 TABLE DATA public agents antiphon");
                if (joined.Contains(" wc ")) return Answer(0, bytes.ToString(System.Globalization.CultureInfo.InvariantCulture) + " /tmp/antiphon-snapshot.dump");
                if (joined.Contains(" psql ")) return Answer(0, "139");
                return Answer(0);
            }
            case "cp":
                if (HostPath(target)) File.WriteAllBytes(target, new byte[bytes]);
                return Answer(0);
            case "volume":
                return Volume(rest, target, "");
            default:
                return Answer(0);
        }
    }
}

/// <summary>
/// The ACL, owner and mode checks without the real operating system: every directory is the caller's and owner-only unless
/// named in Open (then it carries an inherited Users read entry, or mode 0755) or given its own POSIX stat. IsWindows picks the
/// rule set, so the Windows and the POSIX staging rules both run on either OS.
/// </summary>
internal sealed class FakeSnapshotPlatform : IPostgresSnapshotPlatform
{
    public const string Me = "S-1-5-21-1111111111-2222222222-3333333333-1001";
    public const string Users = "S-1-5-32-545";

    public bool IsWindows { get; set; } = OperatingSystem.IsWindows();

    public HashSet<string> Open { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, SnapshotUnixStat> Stats { get; } = new(StringComparer.Ordinal);

    public int UidValue { get; set; } = 1000;

    public List<string> Restricted { get; } = [];

    public List<string> Created { get; } = [];

    private static string Full(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public string CallerSid() => Me;

    public bool Elevated() => false;

    public SnapshotSecurity ReadSecurity(string path) => Open.Contains(Full(path))
        ? new SnapshotSecurity(Me, [new SnapshotAclRule(Me, "Allow", 2032127), new SnapshotAclRule(Users, "Allow", 1179817, Inherited: true)])
        : new SnapshotSecurity(Me, [new SnapshotAclRule(Me, "Allow", 2032127)]);

    public int Uid() => UidValue;

    public SnapshotUnixStat Stat(string path)
    {
        var full = Full(path);
        if (Stats.TryGetValue(full, out var stat)) return stat;
        return new SnapshotUnixStat(UidValue, Open.Contains(full) ? Convert.ToInt32("755", 8) : Convert.ToInt32("700", 8));
    }

    public bool CreatePrivateDirectory(string path)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(path));
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent) || Directory.Exists(path) || File.Exists(path)) return false;
        Directory.CreateDirectory(path);
        Created.Add(Full(path));
        Open.Remove(Full(path));
        return true;
    }

    public bool RestrictDirectory(string path)
    {
        Restricted.Add(Full(path));
        Open.Remove(Full(path));
        return true;
    }
}

/// <summary>
/// The driver's fixture (New-Fixture): a temporary root with sync, sync/antiphon-postgres (the destination), staging, the
/// recipient and identity canaries; and the run: the command in process on a recording launcher answered by the fake, docker
/// and age resolved to fixture paths (unless removed), the environment a dictionary, the clock fake, the sleep instant.
/// </summary>
internal sealed class SnapshotWorld : IDisposable
{
    public const string Canary = "CANARY-RECIPIENT-VALUE-1405";
    public const string IdentityCanary = "CANARY-IDENTITY-VALUE-1405";

    public SnapshotWorld(IPostgresSnapshotPlatform? platform = null)
    {
        Root = Path.Combine(Path.GetTempPath(), "card1405-" + Guid.NewGuid().ToString("N"));
        Sync = Path.Combine(Root, "sync");
        Dest = Path.Combine(Sync, "antiphon-postgres");
        Stage = Path.Combine(Root, "staging");
        foreach (var dir in new[] { Sync, Dest, Stage }) Directory.CreateDirectory(dir);
        Recipient = Path.Combine(Root, "recipient.txt");
        Identity = Path.Combine(Root, "identity.txt");
        File.WriteAllText(Recipient, Canary + System.Environment.NewLine);
        File.WriteAllText(Identity, IdentityCanary + System.Environment.NewLine);
        Platform = platform ?? new FakeSnapshotPlatform();
        Launcher = new RecordingLauncher(launch => Fake.Run(launch));
    }

    public string Root { get; }
    public string Sync { get; }
    public string Dest { get; }
    public string Stage { get; }
    public string Recipient { get; }
    public string Identity { get; }
    public IPostgresSnapshotPlatform Platform { get; }
    public FakeSnapshotPlatform? FakePlatform => Platform as FakeSnapshotPlatform;
    public SnapshotDockerFake Fake { get; set; } = new();
    public RecordingLauncher Launcher { get; private set; }
    public Dictionary<string, string> Environment { get; } = new(StringComparer.Ordinal);
    public bool NoAge { get; set; }
    public bool NoDocker { get; set; }
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 25, 2, 26, 22, TimeSpan.Zero));

    public string PathOf(params string[] parts) => Path.Combine(new[] { Root }.Concat(parts).ToArray());

    public NightlyHost Host(StringWriter output, StringWriter error) => new()
    {
        Clock = Clock,
        Git = (_, _) => throw new InvalidOperationException("the snapshot runs no git"),
        Launcher = Launcher,
        Processes = new NightlyProcessTable(),
        Probe = new NightlyHostProbe(),
        Out = output,
        Error = error,
        Trace = TextWriter.Null,
        Variables = name => Environment.TryGetValue(name, out var value) ? value : null,
        Sleep = _ => { },
    };

    public PostgresSnapshotSeams Seams => new()
    {
        Resolve = name => name switch
        {
            "docker" when !NoDocker => PathOf("bin", OperatingSystem.IsWindows() ? "docker.exe" : "docker"),
            "age" when !NoAge => PathOf("bin", OperatingSystem.IsWindows() ? "age.exe" : "age"),
            _ => null,
        },
        Platform = Platform,
        LocalApplicationData = () => PathOf("local"),
    };

    public PostgresSnapshotSession Session(StringWriter? output = null) => new(Host(output ?? new StringWriter(), new StringWriter()), Seams);

    /// <summary>One run with a fresh launcher record (the driver's tool marker was removed before each Invoke-Snapshot).</summary>
    public SnapshotRun Run(params string[] arguments)
    {
        Launcher = new RecordingLauncher(launch => Fake.Run(launch));
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = PostgresSnapshot.Run(PostgresSnapshotOptions.Parse(arguments), Host(output, error), Seams);
        var tools = Launcher.Launches.Select(launch => Path.GetFileNameWithoutExtension(launch.FileName) + " " + string.Join(" ", launch.Arguments)).ToArray();
        return new SnapshotRun(exit, output.ToString().Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries), error.ToString(), tools);
    }

    public string[] Common => ["--destination", Dest, "--sync-root", Sync, "--staging-path", Stage];

    public string[] Dumps(string dir, string pattern = "*.dump.age") => Directory.Exists(dir) ? Directory.GetFiles(dir, pattern).Select(Path.GetFileName).ToArray()! : [];

    public string[] RunDirectories(string? dir = null) =>
        Directory.Exists(dir ?? Stage) ? Directory.GetDirectories(dir ?? Stage).Select(Path.GetFileName).Where(name => name!.StartsWith("run-", StringComparison.Ordinal)).ToArray()! : [];

    public void Dispose() => GitFixtureCleanup.Delete(Root);
}

/// <summary>One run: the exit, stdout lines, stderr and the launches as "docker|age &lt;arguments&gt;".</summary>
internal sealed record SnapshotRun(int Exit, string[] Lines, string Error, string[] Tools)
{
    public string Out => string.Join("\n", Lines);

    public string ToolText => string.Join("\n", Tools);

    public override string ToString() => "exit=" + Exit + "\n" + Out + "\n" + Error + "\n" + ToolText;
}
