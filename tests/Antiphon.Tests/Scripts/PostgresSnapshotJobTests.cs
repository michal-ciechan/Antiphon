using System.Security.Cryptography;
using System.Text;
using Antiphon.Nightly;
using Antiphon.Tests.Infrastructure;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// The daily plain snapshot's Windmill side, unit 53 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the job
/// (PostgresSnapshotJob.Run behind scripts/windmill/invoke-postgres-snapshot.ps1), the 08:00 stale check (PostgresSnapshotJob.Stale
/// behind invoke-postgres-snapshot-stale.ps1), Set-SnapshotTarget (WriteTarget) and the job-status library and script
/// (scripts/windmill/job-status.ps1, deleted). The Windows PowerShell child and the tg-send.cs sender are a recording launcher, so
/// no snapshot runs and nothing is posted. The lines, records and exits are the scripts', captured from them (dot-sourced, with the
/// child and the send replaced) before their bodies were replaced. The backup retarget plan's V-7..V-10 (the target word, the
/// source on the line, the reason from the child) and D-8 (the target in the records) are here.
/// </summary>
[Category("Unit")]
public sealed class PostgresSnapshotJobTests
{
    private const string PlainOk = """{"utc":"20261118T075000Z","exit":0,"bytes":750000000,"sha256":"abcdef123456","duration":240,"tables":142,"mode":"plain","encryption":"none","operatorDecision":"2026-11-10","oneDrive":"ok","oneDriveBytes":750000000,"oneDriveFolder":"C:\\Users\\lndco\\OneDrive\\Backups\\antiphon-postgres","target":"server2","serverVersion":"16.14"}""";
    private const string OkOut = "PLAIN UNENCRYPTED operator-decision=2026-11-10 encryption=none\nONEDRIVE COPY oneDrive=ok bytes=750000000\nSNAPSHOT ok exit=0 reason=ok bytes=750000000 kept=14 utc=20261118T075000Z\n";
    private static readonly string[] OkChildLines =
        ["PLAIN UNENCRYPTED operator-decision=2026-11-10 encryption=none", "ONEDRIVE COPY oneDrive=ok bytes=750000000", "SNAPSHOT ok exit=0 reason=ok bytes=750000000 kept=14 utc=20261118T075000Z", ""];

    private sealed class Runner : IDisposable
    {
        public Runner(string? target, bool pin = true)
        {
            Root = Path.Combine(Path.GetTempPath(), "card1831-job-" + Guid.NewGuid().ToString("N"));
            Dir = Path.Combine(Root, "runner");
            Directory.CreateDirectory(Dir);
            // The candidate is the real launcher: the resolver still finds its four source literals.
            Script = Path.Combine(Dir, "postgres-snapshot.ps1");
            File.Copy(Path.Combine(DockerStackDocuments.RepoRoot, "scripts", "postgres-snapshot.ps1"), Script);
            Sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Script))).ToLowerInvariant();
            if (target != null) File.WriteAllText(Path.Combine(Dir, "snapshot-target"), target, new ASCIIEncoding());
            if (pin) File.WriteAllText(Path.Combine(Dir, "SOURCE-SHA"), Sha, new ASCIIEncoding());
            Launcher = new RecordingLauncher(Answer);
        }

        public string Root { get; }
        public string Dir { get; }
        public string Script { get; }
        public string Sha { get; }
        public RecordingLauncher Launcher { get; }
        public int ChildExit { get; set; }
        public string ChildOut { get; set; } = OkOut;
        public string ChildErr { get; set; } = "";
        public int SendExit { get; set; }
        public string SendOut { get; set; } = "produced -> channels.outbound [[0]] @42 (broker localhost:19092)\n";
        public string SendErr { get; set; } = "";
        public string RecordDir => Path.Combine(Root, "job-status");
        public string StatusPath => Path.Combine(Root, "snapshot-status.json");
        public StringWriter Out { get; } = new();
        public StringWriter Error { get; } = new();
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 19, 8, 0, 0, TimeSpan.Zero));

        private NightlyLaunchResult Answer(NightlyLaunch launch)
        {
            if (Path.GetFileName(launch.FileName) == "powershell.exe")
                return new NightlyLaunchResult { ExitCode = ChildExit, StandardOutputText = ChildOut, StandardErrorText = ChildErr, LogText = ChildOut + ChildErr };
            Messages.Add(File.ReadAllText(launch.Arguments[^1]));
            return new NightlyLaunchResult { ExitCode = SendExit, StandardOutputText = SendOut, StandardErrorText = SendErr, LogText = SendOut + SendErr };
        }

        public NightlyHost Host => new()
        {
            Clock = Clock,
            Git = (_, _) => throw new InvalidOperationException("no git"),
            Launcher = Launcher,
            Processes = new NightlyProcessTable(),
            Probe = new NightlyHostProbe(),
            Out = Out,
            Error = Error,
            Trace = TextWriter.Null,
            Variables = name => name == "SystemRoot" ? @"C:\Windows" : null,
        };

        public PostgresSnapshotJobSeams Seams => new()
        {
            Candidates = [Path.Combine(Root, "missing", "postgres-snapshot.ps1"), Script],
            Resolve = name => name == "dotnet" ? @"C:\fake\dotnet.exe" : null,
            TempDirectory = () => Root,
            SenderRoot = Path.Combine(Root, "ClaudeBot"),
        };

        public (int Exit, string[] Lines) Run(string? status = null)
        {
            if (status != null) File.WriteAllText(StatusPath, status);
            var exit = PostgresSnapshotJob.Run(new PostgresSnapshotJobOptions { RunnerDir = Dir, RecordDir = RecordDir, StatusPath = StatusPath }, Host, Seams);
            return (exit, Out.ToString().Replace("\r\n", "\n").TrimEnd('\n').Split('\n'));
        }

        public string Record => File.ReadAllText(Path.Combine(RecordDir, "db-backup.json"));

        public List<string> Messages { get; } = [];

        public string[] Sent => Messages.ToArray();

        public void Dispose() => GitFixtureCleanup.Delete(Root);
    }

    private static string[] Head(Runner runner, string target) => ["snapshot-target=" + target, "snapshot-script=" + runner.Script, "source-sha=" + runner.Sha];

    [Test]
    public void Target_word_fails_closed_unless_it_is_exactly_desktop_or_server2()
    {
        foreach (var word in new string?[] { null, "Desktop", "server2\n\n", "", " desktop", "server3" })
        {
            using var runner = new Runner(word);
            var (exit, lines) = runner.Run(PlainOk);
            exit.ShouldBe(9, word);
            lines.ShouldBe(["SNAPSHOT fail exit=9 reason=target-invalid", "DB backup: FAIL exit 9 target-invalid src=none", "telegram=0"], word);
            runner.Record.ShouldBe("""{"job":"db-backup","icon":"FAIL","status":"fail","detail":"exit 9 target-invalid src=none"}""");
            runner.Launcher.Launches.Count(launch => Path.GetFileName(launch.FileName) == "powershell.exe").ShouldBe(0, "no database is picked");
        }
        foreach (var word in new[] { "desktop", "server2", "server2\n", "desktop\r\n" })
        {
            using var dir = new Runner(word);
            PostgresSnapshotJob.ReadTarget(dir.Dir).ShouldBe(word.TrimEnd('\r', '\n'));
        }
    }

    [Test]
    public void An_unpinned_or_mismatched_script_is_missing_script()
    {
        using (var noPin = new Runner("server2", pin: false))
        {
            var (exit, lines) = noPin.Run(PlainOk);
            exit.ShouldBe(1);
            lines.ShouldBe(["snapshot-target=server2", "SNAPSHOT fail exit=1 reason=missing-script", "DB backup: FAIL exit 1 missing-script src=server2", "telegram=0"]);
        }
        foreach (var pin in new[] { new string('A', 64), "not-a-sha", new string('0', 64) })
        {
            using var runner = new Runner("desktop\r\n");
            File.WriteAllText(Path.Combine(runner.Dir, "SOURCE-SHA"), pin);
            var (exit, lines) = runner.Run(PlainOk);
            exit.ShouldBe(1, pin);
            lines.ShouldBe(["snapshot-target=desktop", "SNAPSHOT fail exit=1 reason=missing-script", "DB backup: FAIL exit 1 missing-script src=desktop", "telegram=0"], pin);
        }

        // The pin is any-case hex with whitespace trimmed; a candidate without the four literals is skipped even when pinned.
        using var upper = new Runner("desktop");
        File.WriteAllText(Path.Combine(upper.Dir, "SOURCE-SHA"), "  " + upper.Sha.ToUpperInvariant() + "\r\n");
        PostgresSnapshotJob.ResolveScript([upper.Script], Path.Combine(upper.Dir, "SOURCE-SHA")).ShouldBe((upper.Script, upper.Sha));
        var bare = Path.Combine(upper.Root, "bare.ps1");
        File.WriteAllText(bare, "[switch]$Plain");
        var bareSha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(bare))).ToLowerInvariant();
        File.WriteAllText(Path.Combine(upper.Dir, "SOURCE-SHA"), bareSha);
        PostgresSnapshotJob.ResolveScript([bare, upper.Script], Path.Combine(upper.Dir, "SOURCE-SHA")).ShouldBe(("", ""));
        PostgresSnapshotJob.DefaultCandidates(upper.Dir).ShouldBe([@"C:\src\Antiphon\scripts\postgres-snapshot.ps1", Path.Combine(upper.Dir, "postgres-snapshot.ps1"),
            @"C:\Antiphon\worktrees\card-task-300d724e\scripts\postgres-snapshot.ps1"]);
    }

    [Test]
    public void A_passing_child_posts_its_size_cloud_state_and_source()
    {
        foreach (var (target, status, line) in new[]
                 {
                     ("server2", PlainOk, "DB backup: OK 0.7 GB +cloud src=server2"),
                     ("desktop\n", PlainOk.Replace("\"oneDrive\":\"ok\"", "\"oneDrive\":\"missing\""), "DB backup: WARN 0.7 GB no cloud copy src=desktop"),
                     ("desktop", """{"utc":"20261118T075000Z","exit":0,"bytes":104857600,"mode":"plain"}""", "DB backup: OK 0.1 GB src=desktop"),
                     ("desktop", """{"exit":0,"bytes":2048,"mode":"PLAIN","oneDrive":"failed"}""", "DB backup: WARN 2048 B no cloud copy src=desktop"),
                     ("desktop", """{"utc":"20261118T075000Z","exit":0,"bytes":1234}""", "DB backup: FAIL exit 0 src=desktop"),
                     ("desktop", "{not json", "DB backup: FAIL exit 0 src=desktop"),
                     ("desktop", """{"exit":"zero","bytes":5,"mode":"plain"}""", "DB backup: FAIL exit 0 src=desktop"),
                     ("desktop", """{"exit":"0","bytes":"5","mode":"plain","OneDrive":"ok"}""", "DB backup: OK 5 B +cloud src=desktop"),
                 })
        {
            using var runner = new Runner(target);
            var (exit, lines) = runner.Run(status);
            exit.ShouldBe(0, line);
            var word = target.TrimEnd('\n');
            lines.ShouldBe([.. Head(runner, word), .. OkChildLines, line, "telegram=0"], line);
            runner.Sent.ShouldBe([line], "the line is posted once");
        }

        using var plain = new Runner("server2");
        plain.Run(PlainOk);
        plain.Record.ShouldBe("""{"job":"db-backup","icon":"OK","status":"ok","detail":"0.7 GB +cloud src=server2"}""");
        var child = plain.Launcher.Launches.Single(launch => Path.GetFileName(launch.FileName) == "powershell.exe");
        child.FileName.ShouldBe(Path.Combine(@"C:\Windows", "System32", "WindowsPowerShell", "v1.0", "powershell.exe"));
        child.Arguments.ShouldBe(["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", plain.Script, "-Plain", "-Execute", "-OneDriveCopy", "-StagingPath",
            @"C:\Users\lndco\antiphon-snapshot-staging", "-Target", "server2"]);
        child.CaptureOutput.ShouldBeTrue();
        var send = plain.Launcher.Launches.Single(launch => Path.GetFileName(launch.FileName) == "dotnet.exe");
        send.Arguments.Take(6).ShouldBe(["run", Path.Combine(plain.Root, "ClaudeBot", "scripts", "tg-send.cs"), "--", "--to", "8738110514", "--text-file"]);
        send.WorkingDirectory.ShouldBe(Path.Combine(plain.Root, "ClaudeBot"));
        plain.Out.ToString().ShouldNotContain("produced ->", Case.Sensitive, "tg-send's receipt is not printed");
        Directory.GetDirectories(plain.Root, "tg-job-*").ShouldBeEmpty("the message file goes");
    }

    [Test]
    public void A_failing_child_posts_its_exit_and_the_reason_from_its_own_line()
    {
        using (var runner = new Runner("server2") { ChildExit = 9, ChildOut = "PLAIN UNENCRYPTED operator-decision=2026-11-10 encryption=none\nSNAPSHOT fail exit=9 reason=source-mismatch\n",
                   ChildErr = "child stderr one\npassword=hunter2\nchild stderr two\n" })
        {
            var (exit, lines) = runner.Run(PlainOk);
            exit.ShouldBe(9);
            lines.ShouldBe([.. Head(runner, "server2"), "PLAIN UNENCRYPTED operator-decision=2026-11-10 encryption=none", "SNAPSHOT fail exit=9 reason=source-mismatch", "",
                "child stderr one", "child stderr two", "", "DB backup: FAIL exit 9 source-mismatch src=server2", "telegram=0"]);
            runner.Record.ShouldBe("""{"job":"db-backup","icon":"FAIL","status":"fail","detail":"exit 9 source-mismatch src=server2"}""");
        }
        foreach (var (code, output, detail) in new[]
                 {
                     (9, "SNAPSHOT fail exit=9 reason=drill-refused guard=database\n", "exit 9 drill-refused src=server2"),
                     (4, "SNAPSHOT fail exit=9 reason=source-mismatch\n", "exit 4 src=server2"),
                     (1, "antiphon-cli: build failed\nSNAPSHOT fail later\nSNAPSHOT fail exit=1 reason=x\n", "exit 1 src=server2"),
                     (5, "SNAPSHOT fail exit=5 reason=verify-failed\n", "exit 5 verify-failed src=server2"),
                 })
        {
            PostgresSnapshotJob.FailDetail(code, output, "server2").ShouldBe(detail);
            using var runner = new Runner("server2") { ChildExit = code, ChildOut = output };
            var (exit, lines) = runner.Run();
            exit.ShouldBe(code);
            lines[^2..].ShouldBe(["DB backup: FAIL " + detail, "telegram=0"]);
        }
        PostgresSnapshotJob.FailDetail(9, "SNAPSHOT fail exit=9 reason=source-mismatch extra", "desktop").ShouldBe("exit 9 src=desktop", "the detail shape is key=value");
    }

    [Test]
    public void Child_lines_are_filtered_for_secrets_and_capped()
    {
        using (var runner = new Runner("desktop") { ChildOut = "a line\nHost=db postgres://u:p@h/db\nBearer abc\ntoken=xyz\nPassword = 1\nlast\n", ChildErr = "ignored on success" })
        {
            var (exit, lines) = runner.Run(PlainOk);
            exit.ShouldBe(0);
            lines.ShouldBe([.. Head(runner, "desktop"), "a line", "last", "", "DB backup: OK 0.7 GB +cloud src=desktop", "telegram=0"]);
        }
        using (var empty = new Runner("desktop") { ChildOut = "  \n" })
        {
            empty.Run(PlainOk).Lines.ShouldBe([.. Head(empty, "desktop"), "DB backup: OK 0.7 GB +cloud src=desktop", "telegram=0"]);
        }
        using var many = new Runner("desktop")
        {
            ChildExit = 3,
            ChildOut = string.Join("\n", Enumerable.Range(1, 85).Select(i => "line " + i)) + "\n",
            ChildErr = string.Join("\n", Enumerable.Range(1, 45).Select(i => "err " + i)),
        };
        var (code, all) = many.Run();
        code.ShouldBe(3);
        all.ShouldBe([.. Head(many, "desktop"), .. Enumerable.Range(1, 80).Select(i => "line " + i), .. Enumerable.Range(1, 40).Select(i => "err " + i),
            "DB backup: FAIL exit 3 src=desktop", "telegram=0"]);
    }

    [Test]
    public void A_failed_send_fails_a_passing_job_and_keeps_a_failing_childs_exit()
    {
        using (var runner = new Runner("server2") { SendExit = 1, SendErr = "produce failed: broker down\n" })
        {
            var (exit, lines) = runner.Run(PlainOk);
            exit.ShouldBe(1);
            lines[^2..].ShouldBe(["DB backup: OK 0.7 GB +cloud src=server2", "telegram=1"]);
            runner.Error.ToString().ShouldContain("produce failed: broker down", Case.Sensitive, "tg-send's stderr passes through");
        }
        using var failing = new Runner("server2") { ChildExit = 5, ChildOut = "SNAPSHOT fail exit=5 reason=verify-failed\n", SendExit = 1 };
        var (code, all) = failing.Run();
        code.ShouldBe(5);
        all[^2..].ShouldBe(["DB backup: FAIL exit 5 verify-failed src=server2", "telegram=1"]);
    }

    [Test]
    public void An_unexpected_failure_prints_no_job_lines_and_exits_1()
    {
        using var runner = new Runner("desktop");
        var seams = runner.Seams;
        var broken = new PostgresSnapshotJobSeams { Candidates = seams.Candidates, Resolve = seams.Resolve, TempDirectory = () => Path.Combine(runner.Root, "no", "such", "\0bad"), SenderRoot = seams.SenderRoot };
        var exit = PostgresSnapshotJob.Run(new PostgresSnapshotJobOptions { RunnerDir = runner.Dir, RecordDir = runner.RecordDir, StatusPath = runner.StatusPath }, runner.Host, broken);
        exit.ShouldBe(1);
        runner.Out.ToString().ShouldBeEmpty("the script printed its lines only once the job was done");
        runner.Error.ToString().ShouldStartWith("postgres-snapshot-job: ");
    }

    [Test]
    public void Set_snapshot_target_writes_one_ascii_word_and_reads_it_back()
    {
        using var runner = new Runner(null);
        var output = new StringWriter();
        string Set(string word, string? dir = null)
        {
            output.GetStringBuilder().Clear();
            return PostgresSnapshotJob.WriteTarget(word, dir ?? runner.Dir, output);
        }
        var path = Path.Combine(runner.Dir, "snapshot-target");
        Set("server2").ShouldBe("server2");
        output.ToString().ShouldBe("SNAPSHOT TARGET server2" + Environment.NewLine);
        File.ReadAllBytes(path).ShouldBe(Encoding.ASCII.GetBytes("server2"));
        Set("desktop").ShouldBe("desktop");
        File.ReadAllBytes(path).ShouldBe(Encoding.ASCII.GetBytes("desktop"), "ASCII, no BOM, no line break");
        Set("desktop").ShouldBe("desktop", "idempotent: the rollback runs whether or not the flip happened");
        foreach (var word in new[] { "Desktop", "server3", "" })
        {
            Set(word).ShouldBe("");
            output.ToString().ShouldBe("SNAPSHOT TARGET refused (desktop or server2)" + Environment.NewLine);
            File.ReadAllBytes(path).ShouldBe(Encoding.ASCII.GetBytes("desktop"), "a refused word leaves the file");
        }
        Set("desktop", Path.Combine(runner.Root, "no-such-dir")).ShouldBe("");
        output.ToString().ShouldBe("SNAPSHOT TARGET refused (runner directory missing)" + Environment.NewLine);
    }

    [Test]
    public void Job_status_records_lines_and_sizes_match_the_library()
    {
        foreach (var (bytes, text) in new (long, string)[]
                 {
                     (0, "0 B"), (1023, "1023 B"), (1048575, "1048575 B"), (1048576, "1.0 MB"), (1572864, "1.5 MB"), (104857599, "100.0 MB"), (104857600, "0.1 GB"),
                     (734003200, "0.7 GB"), (750000000, "0.7 GB"), (905456635, "0.8 GB"), (1073741824, "1.0 GB"), (5368709120, "5.0 GB"),
                 })
            JobStatus.FormatSize(bytes).ShouldBe(text, bytes.ToString());

        string Show(JobStatusRecord? record) => record == null ? "NULL" : JobStatus.FormatLine(record) + " | " + JobStatus.ToJson(record);
        Show(JobStatus.CloudRecord(750000000, "ok", "server2")).ShouldBe("DB backup: OK 0.7 GB +cloud src=server2 | {\"job\":\"db-backup\",\"icon\":\"OK\",\"status\":\"ok\",\"detail\":\"0.7 GB +cloud src=server2\"}");
        Show(JobStatus.CloudRecord(750000000, "ok")).ShouldBe("DB backup: OK 0.7 GB +cloud | {\"job\":\"db-backup\",\"icon\":\"OK\",\"status\":\"ok\",\"detail\":\"0.7 GB +cloud\"}");
        Show(JobStatus.CloudRecord(750000000, "missing", "desktop")).ShouldBe("DB backup: WARN 0.7 GB no cloud copy src=desktop | {\"job\":\"db-backup\",\"icon\":\"WARN\",\"status\":\"warn\",\"detail\":\"0.7 GB no cloud copy src=desktop\"}");
        Show(JobStatus.CloudRecord(750000000, "failed", "desktop")).ShouldStartWith("DB backup: WARN 0.7 GB no cloud copy src=desktop");
        Show(JobStatus.CloudRecord(750000000, "OK", "desktop")).ShouldStartWith("DB backup: OK 0.7 GB +cloud src=desktop");
        Show(JobStatus.CloudRecord(750000000, "", "desktop")).ShouldStartWith("DB backup: OK 0.7 GB src=desktop |");
        Show(JobStatus.CloudRecord(750000000, "ok", "Server2")).ShouldBe("NULL");
        Show(JobStatus.CloudRecord(750000000, "ok", "server2x0123456789")).ShouldBe("NULL");
        Show(JobStatus.NewRecord("db-backup", "FAIL", "fail", "exit 9 target-invalid src=none"))
            .ShouldBe("DB backup: FAIL exit 9 target-invalid src=none | {\"job\":\"db-backup\",\"icon\":\"FAIL\",\"status\":\"fail\",\"detail\":\"exit 9 target-invalid src=none\"}");
        Show(JobStatus.NewRecord("db-backup", "STALE", "stale")).ShouldBe("DB backup: STALE | {\"job\":\"db-backup\",\"icon\":\"STALE\",\"status\":\"stale\"}");
        Show(JobStatus.NewRecord("db-backup", "OK", "OK")).ShouldBe("DB backup: OK | {\"job\":\"db-backup\",\"icon\":\"OK\",\"status\":\"OK\"}");
        Show(JobStatus.NewRecord("other-job", "ok1", "warn", "   ")).ShouldBe("other-job: ok1 | {\"job\":\"other-job\",\"icon\":\"ok1\",\"status\":\"warn\"}");
        Show(JobStatus.NewRecord("Db-backup", "OK", "ok")).ShouldBe("DB backup: OK | {\"job\":\"Db-backup\",\"icon\":\"OK\",\"status\":\"ok\"}");
        Show(JobStatus.NewRecord("db-backup", "TOOLONGICON", "ok")).ShouldBe("NULL");
        Show(JobStatus.NewRecord("db-backup", "OK", "ok", "a\"b")).ShouldBe("NULL");
        Show(JobStatus.NewRecord("db-backup", "OK", "ok", "host = x")).ShouldBe("NULL");
        Show(JobStatus.NewRecord("db-backup", "OK", "ok", new string('x', 121))).ShouldBe("NULL");
        Show(JobStatus.NewRecord("db-backup", "OK", "ok", new string('x', 120))).ShouldBe("DB backup: OK " + new string('x', 120) + " | {\"job\":\"db-backup\",\"icon\":\"OK\",\"status\":\"ok\",\"detail\":\"" + new string('x', 120) + "\"}");
        Show(JobStatus.NewRecord("db-backup", "OK", "bogus")).ShouldBe("NULL");
    }

    [Test]
    public void Freshness_reads_the_status_and_the_newest_dump_as_the_script_did()
    {
        var root = Path.Combine(Path.GetTempPath(), "card1831-fresh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var now = new DateTime(2026, 11, 19, 8, 0, 0, DateTimeKind.Utc);
            bool Fresh(string json)
            {
                var path = Path.Combine(root, "status.json");
                File.WriteAllText(path, json);
                return JobStatus.StatusFresh(path, now);
            }
            Fresh("""{"utc":"20261118T075000Z","exit":0,"mode":"plain"}""").ShouldBeTrue("ok");
            Fresh("""{"utc":"20261118T060000Z","exit":0,"mode":"plain"}""").ShouldBeFalse("26 hours");
            Fresh("""{"utc":"20261118T060001Z","exit":0,"mode":"plain"}""").ShouldBeTrue("just under");
            Fresh("""{"utc":"20261119T090100Z","exit":0,"mode":"plain"}""").ShouldBeFalse("over an hour ahead");
            Fresh("""{"utc":"20261119T090000Z","exit":0,"mode":"plain"}""").ShouldBeTrue("an hour ahead");
            Fresh("""{"utc":"20261118T075000Z","exit":1,"mode":"plain"}""").ShouldBeFalse("exit 1");
            Fresh("""{"utc":"20261118T075000Z","mode":"plain"}""").ShouldBeTrue("[int] of an absent exit is 0");
            Fresh("""{"utc":"20261118T075000Z","exit":0}""").ShouldBeFalse("the age path");
            Fresh("""{"utc":"20261118T075000Z","exit":0,"mode":"PLAIN"}""").ShouldBeTrue("-ne ignores case");
            Fresh("""{"utc":"20261118T075000Z","exit":"0","mode":"plain"}""").ShouldBeTrue("a numeric string");
            Fresh("""{"utc":"20261118T075000Z","exit":"zero","mode":"plain"}""").ShouldBeFalse("not a number");
            Fresh("""{"utc":"2026-11-18T07:50:00Z","exit":0,"mode":"plain"}""").ShouldBeFalse("utc shape");
            Fresh("""{"utc":"20261341T075000Z","exit":0,"mode":"plain"}""").ShouldBeFalse("no such date");
            Fresh("{").ShouldBeFalse("not JSON");
            JobStatus.StatusFresh(Path.Combine(root, "nope.json"), now).ShouldBeFalse("missing");

            var dumps = Path.Combine(root, "dumps");
            Directory.CreateDirectory(dumps);
            JobStatus.DumpFresh(dumps, now).ShouldBeFalse("empty");
            var dump = Path.Combine(dumps, "antiphon-20261118T075000Z.dump");
            File.WriteAllText(dump, "x");
            File.SetLastWriteTimeUtc(dump, now.AddHours(-25));
            JobStatus.DumpFresh(dumps, now).ShouldBeTrue("25 hours");
            File.SetLastWriteTimeUtc(dump, now.AddHours(-26));
            JobStatus.DumpFresh(dumps, now).ShouldBeFalse("26 hours");
            var age = Path.Combine(dumps, "antiphon-20261118T075000Z.dump.age");
            File.WriteAllText(age, "x");
            File.SetLastWriteTimeUtc(age, now.AddHours(-1));
            JobStatus.DumpFresh(dumps, now).ShouldBeFalse("a ciphertext is not a plain dump");
            var upper = Path.Combine(dumps, "ANTIPHON-20261119T075000Z.DUMP");
            File.WriteAllText(upper, "x");
            File.SetLastWriteTimeUtc(upper, now.AddHours(-1));
            JobStatus.DumpFresh(dumps, now).ShouldBeTrue("-match ignores case");
            JobStatus.DumpFresh(Path.Combine(root, "nodir"), now).ShouldBeFalse();
        }
        finally { GitFixtureCleanup.Delete(root); }
    }

    [Test]
    public void Stale_check_is_quiet_when_fresh_and_posts_one_stale_line()
    {
        using var runner = new Runner("desktop");
        var dumps = Path.Combine(runner.Root, "dumps");
        Directory.CreateDirectory(dumps);
        var options = new PostgresSnapshotStaleOptions { StatusPath = runner.StatusPath, DumpDir = dumps, RecordDir = runner.RecordDir };
        File.WriteAllText(runner.StatusPath, """{"utc":"20261118T075000Z","exit":0,"mode":"plain"}""");
        PostgresSnapshotJob.Stale(options, runner.Host, runner.Seams).ShouldBe(0);
        runner.Out.ToString().ShouldBe("JOB STATUS fresh" + Environment.NewLine);
        runner.Launcher.Launches.ShouldBeEmpty("a fresh success sends nothing");

        File.WriteAllText(runner.StatusPath, """{"utc":"20261110T075000Z","exit":0,"mode":"plain"}""");
        runner.Out.GetStringBuilder().Clear();
        runner.SendExit = 0;
        PostgresSnapshotJob.Stale(options, runner.Host, runner.Seams).ShouldBe(0);
        runner.Out.ToString().ShouldBe("DB backup: STALE" + Environment.NewLine);
        File.ReadAllText(Path.Combine(runner.RecordDir, "db-backup-stale.json")).ShouldBe("""{"job":"db-backup","icon":"STALE","status":"stale"}""");
        runner.Sent.ShouldBe(["DB backup: STALE"]);
        runner.SendExit = 1;
        PostgresSnapshotJob.Stale(options, runner.Host, runner.Seams).ShouldBe(1, "the send's exit");

        PostgresSnapshotStaleOptions.Parse([]).ShouldSatisfyAllConditions(
            o => o.StatusPath.ShouldBe(@"C:\Users\lndco\antiphon-backups\snapshot-status.json"),
            o => o.DumpDir.ShouldBe(@"C:\Users\lndco\antiphon-backups"),
            o => o.RecordDir.ShouldBe(@"C:\Users\lndco\antiphon-backups\job-status"),
            o => o.MaxAgeHours.ShouldBe(26));
    }

    [Test]
    public void Job_status_script_mode_prints_the_line_writes_the_record_and_refuses_a_bad_one()
    {
        using var runner = new Runner("desktop");
        var record = Path.Combine(runner.Root, "rec", "db-backup.json");
        JobStatusOptions.Run(JobStatusOptions.Parse(["--job", "db-backup", "--icon", "OK", "--status", "ok", "--detail", "0.6 GB", "--record-path", record]), runner.Host, runner.Seams)
            .ShouldBe(0);
        runner.Out.ToString().ShouldBe("DB backup: OK 0.6 GB" + Environment.NewLine);
        File.ReadAllText(record).ShouldBe("""{"job":"db-backup","icon":"OK","status":"ok","detail":"0.6 GB"}""");
        runner.Launcher.Launches.ShouldBeEmpty("no --send");
        foreach (var arguments in new[] { new[] { "--job", "db-backup", "--icon", "OK", "--status", "bogus" }, Array.Empty<string>() })
        {
            runner.Out.GetStringBuilder().Clear();
            JobStatusOptions.Run(JobStatusOptions.Parse(arguments), runner.Host, runner.Seams).ShouldBe(2);
            runner.Out.ToString().ShouldBe("JOB STATUS bad-record" + Environment.NewLine);
        }
        runner.Out.GetStringBuilder().Clear();
        runner.SendExit = 7;
        JobStatusOptions.Run(JobStatusOptions.Parse(["--job", "db-backup", "--icon", "FAIL", "--status", "fail", "--send", "--chat-id", "1", "--sender-root", runner.Root]),
            runner.Host, runner.Seams).ShouldBe(7);
        runner.Launcher.Launches.Single().Arguments.ShouldContain("1");

        var defaults = JobStatusOptions.Parse([]);
        (defaults.ChatId, defaults.SenderRoot, defaults.MaxAgeHours, defaults.StatusFile, defaults.DumpDir)
            .ShouldBe(("8738110514", @"C:\src\ClaudeBot", 26, @"C:\Users\lndco\antiphon-backups\snapshot-status.json", @"C:\Users\lndco\antiphon-backups"));
    }

    [Test]
    public void The_sender_posts_once_and_refuses_a_blank_or_secret_line()
    {
        using var runner = new Runner("desktop");
        var sender = new JobStatusSender(runner.Host, runner.Seams.Resolve, () => runner.Root);
        sender.Send("   ").ShouldBe(1);
        sender.Send("DB backup: OK password=1").ShouldBe(1);
        sender.Send("Host = db").ShouldBe(1);
        runner.Launcher.Launches.ShouldBeEmpty();
        sender.Send("DB backup: OK", "42", runner.Root).ShouldBe(0);
        sender.Send("DB backup: OK", "42", runner.Root).ShouldBe(2, "a second call in one process");
        runner.Launcher.Launches.Count.ShouldBe(1);
    }
}
