using System.Security.Cryptography;
using Antiphon.Nightly;
using Antiphon.Tests.Infrastructure;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// The plain snapshot's Windmill registration, unit 53 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md:
/// RegisterPostgresSnapshot (the register-postgres-snapshot command that replaced scripts/register-postgres-snapshot.ps1) in
/// process. The preview reads the checked-in definitions and contacts nothing; --apply's ssh and scp are a recording launcher
/// that keeps what would have been uploaded, so nothing reaches server2 and no Windmill token is minted. The lines, exits, the
/// ssh and scp command lines and the bytes of register-remote.sh are the script's, captured from it (ssh and scp shadowed) before
/// its deletion. The checked-in definitions are read, never written.
/// </summary>
[Category("Unit")]
public sealed class RegisterPostgresSnapshotTests
{
    internal static readonly string Windmill = Path.Combine(DockerStackDocuments.RepoRoot, "scripts", "windmill");
    private const string Preview = "preview path=u/lndcobra/antiphon_postgres_snapshot schedule=0 50 7 * * * timezone=Europe/London enabled=true";
    private const string PreviewStale = "preview path=u/lndcobra/antiphon_postgres_snapshot_stale schedule=0 0 8 * * * timezone=Europe/London enabled=true";

    private sealed class World : IDisposable
    {
        public World()
        {
            Root = Path.Combine(Path.GetTempPath(), "card1831-reg-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Launcher = new RecordingLauncher(Answer);
        }

        public string Root { get; }
        public RecordingLauncher Launcher { get; }
        public int MkdirExit { get; set; }
        public int ScpExit { get; set; }
        public int RemoteExit { get; set; }
        public Dictionary<string, byte[]> Uploaded { get; } = new(StringComparer.Ordinal);
        public StringWriter Out { get; } = new();
        public StringWriter Error { get; } = new();

        private NightlyLaunchResult Answer(NightlyLaunch launch)
        {
            var tool = Path.GetFileNameWithoutExtension(launch.FileName);
            if (tool == "scp")
            {
                foreach (var argument in launch.Arguments.Where(File.Exists)) Uploaded[Path.GetFileName(argument)] = File.ReadAllBytes(argument);
                return new NightlyLaunchResult { ExitCode = ScpExit };
            }
            var joined = string.Join(" ", launch.Arguments);
            return new NightlyLaunchResult { ExitCode = joined.Contains("mkdir") ? MkdirExit : RemoteExit };
        }

        public (int Exit, string[] Lines) Run(params string[] arguments)
        {
            var host = new NightlyHost
            {
                Clock = new FakeTimeProvider(),
                Git = (_, _) => throw new InvalidOperationException("no git"),
                Launcher = Launcher,
                Processes = new NightlyProcessTable(),
                Probe = new NightlyHostProbe(),
                Out = Out,
                Error = Error,
                Trace = TextWriter.Null,
                Variables = _ => null,
            };
            var seams = new RegisterPostgresSnapshotSeams { Resolve = name => Path.Combine(Root, "bin", name + ".exe"), TempDirectory = () => Root, Token = () => "0123456789abcdef0123456789abcdef" };
            var exit = RegisterPostgresSnapshot.Run(RegisterPostgresSnapshotOptions.Parse(arguments, DockerStackDocuments.RepoRoot), host, seams);
            return (exit, Out.ToString().Replace("\r\n", "\n").TrimEnd('\n').Split('\n'));
        }

        public string Copy(string name, params (string Old, string New)[] edits)
        {
            var dir = Path.Combine(Root, name);
            Directory.CreateDirectory(dir);
            foreach (var file in RegisterPostgresSnapshot.Uploads)
            {
                var text = File.ReadAllText(Path.Combine(Windmill, file));
                foreach (var (old, @new) in edits) text = text.Replace(old, @new);
                File.WriteAllText(Path.Combine(dir, file), text);
            }
            return dir;
        }

        public void Dispose() => GitFixtureCleanup.Delete(Root);
    }

    private static void Expect((int Exit, string[] Lines) run, int exit, params string[] lines)
    {
        run.Exit.ShouldBe(exit, string.Join("\n", run.Lines));
        run.Lines.ShouldBe(lines);
    }

    [Test]
    public void Preview_reads_both_definitions_and_contacts_nothing()
    {
        using var world = new World();
        var (exit, lines) = world.Run();
        exit.ShouldBe(0);
        lines.ShouldBe([Preview, PreviewStale]);
        world.Launcher.Launches.ShouldBeEmpty("the preview contacts nothing");
        Should.Throw<NightlyUsageException>(() => RegisterPostgresSnapshotOptions.Parse(["-Apply"], "x")).Message.ShouldBe("unexpected argument '-Apply'");
    }

    [Test]
    public void Enable_and_run_once_require_apply_and_definitions_must_agree()
    {
        using var world = new World();
        Expect(world.Run("--enable-schedule"), 3, "REFUSED: -EnableSchedule requires -Apply.");
        world.Out.GetStringBuilder().Clear();
        Expect(world.Run("--run-once"), 3, "REFUSED: -RunOnce requires -Apply.");
        world.Out.GetStringBuilder().Clear();
        Expect(world.Run("--enable-schedule", "--run-once"), 3, "REFUSED: -EnableSchedule requires -Apply.");
        world.Out.GetStringBuilder().Clear();
        Expect(world.Run("--definition-root", Path.Combine(world.Root, "nowhere")), 3, "REFUSED: missing definition antiphon-postgres-snapshot.json");
        world.Out.GetStringBuilder().Clear();
        var mismatch = world.Copy("mismatch", ("\"script_path\": \"u/lndcobra/antiphon_postgres_snapshot_stale\"", "\"script_path\": \"u/lndcobra/other\""));
        Expect(world.Run("--definition-root", mismatch), 3, Preview, "REFUSED: schedule does not name script u/lndcobra/antiphon_postgres_snapshot_stale");
        world.Out.GetStringBuilder().Clear();
        var disabled = world.Copy("disabled", ("\"enabled\": true", "\"enabled\": false"));
        Expect(world.Run("--definition-root", disabled), 0, Preview.Replace("enabled=true", "enabled=false"), PreviewStale.Replace("enabled=true", "enabled=false"));
        world.Launcher.Launches.ShouldBeEmpty();
    }

    [Test]
    public void Apply_uploads_the_definitions_and_the_remote_script_then_runs_it()
    {
        foreach (var (arguments, sha) in new[]
                 {
                     (new[] { "--apply" }, "959f981bff8ee69e491f645df809a5161ef2a1f544783f0e6cb49d67deda2dee"),
                     (new[] { "--apply", "--enable-schedule", "--run-once" }, "d77239170b2bffca53a62705145826629e91aa95c1bbd9cbe1056581bcc4bbee"),
                 })
        {
            using var world = new World();
            var (exit, lines) = world.Run(arguments);
            exit.ShouldBe(0, world.Error.ToString());
            lines.ShouldBe([Preview, PreviewStale]);
            var local = Path.Combine(world.Root, "snapshot-reg-0123456789abcdef0123456789abcdef");
            var launches = world.Launcher.Launches;
            launches.Count.ShouldBe(3);
            launches[0].FileName.ShouldBe(Path.Combine(world.Root, "bin", "ssh.exe"));
            launches[0].Arguments.ShouldBe(["-o", "BatchMode=yes", "-o", "StrictHostKeyChecking=accept-new", "mc@server2", "rm -rf /tmp/antiphon-snapshot-reg && mkdir -p /tmp/antiphon-snapshot-reg"]);
            launches[1].Arguments.ShouldBe(["-o", "BatchMode=yes",
                .. new[] { "antiphon-postgres-snapshot-stale.json", "antiphon-postgres-snapshot-stale.schedule.json", "antiphon-postgres-snapshot.json",
                    "antiphon-postgres-snapshot.schedule.json", "register-postgres-snapshot.py", "register-remote.sh" }.Select(name => Path.Combine(local, name)),
                "mc@server2:/tmp/antiphon-snapshot-reg/"]);
            launches[2].Arguments.ShouldBe(["-o", "BatchMode=yes", "mc@server2", "bash /tmp/antiphon-snapshot-reg/register-remote.sh"]);
            launches.ShouldAllBe(launch => !launch.CaptureOutput, "ssh and scp share this console");
            Convert.ToHexString(SHA256.HashData(world.Uploaded["register-remote.sh"])).ToLowerInvariant().ShouldBe(sha, "register-remote.sh is the script's here-string");
            foreach (var name in RegisterPostgresSnapshot.Uploads) world.Uploaded[name].ShouldBe(File.ReadAllBytes(Path.Combine(Windmill, name)), name);
            Directory.Exists(local).ShouldBeFalse("the local staging goes");
        }
        RegisterPostgresSnapshot.RemoteScript(false, false).ShouldContain("python3 \"$ROOT/register-postgres-snapshot.py\" \"$ROOT\" \"keep\" \"skip\"");
        RegisterPostgresSnapshot.RemoteScript(true, true).ShouldContain("\"enable\" \"run\"");
        RegisterPostgresSnapshot.RemoteScript(false, false).ShouldNotContain("\r");
    }

    [Test]
    public void Apply_refuses_on_each_failed_step()
    {
        using (var world = new World { MkdirExit = 255 })
        {
            Expect(world.Run("--apply"), 1, Preview, PreviewStale, "REFUSED: ssh mkdir failed");
            world.Launcher.Launches.Count.ShouldBe(1);
        }
        using (var world = new World { ScpExit = 1 })
        {
            Expect(world.Run("--apply"), 1, Preview, PreviewStale, "REFUSED: scp failed");
            world.Launcher.Launches.Count.ShouldBe(2);
        }
        using (var world = new World { RemoteExit = 7 })
        {
            Expect(world.Run("--apply"), 7, Preview, PreviewStale, "REFUSED: remote register exit=7");
        }
        using (var world = new World())
        {
            var noPy = Path.Combine(world.Root, "nopy");
            Directory.CreateDirectory(noPy);
            foreach (var name in RegisterPostgresSnapshot.Uploads.Where(name => name.EndsWith(".json", StringComparison.Ordinal))) File.Copy(Path.Combine(Windmill, name), Path.Combine(noPy, name));
            var (exit, lines) = world.Run("--apply", "--definition-root", noPy);
            exit.ShouldBe(1);
            lines.ShouldBe([Preview, PreviewStale]);
            world.Error.ToString().ShouldStartWith("register-postgres-snapshot: ");
            world.Launcher.Launches.ShouldBeEmpty("nothing is contacted before every file is staged");
        }
    }
}
