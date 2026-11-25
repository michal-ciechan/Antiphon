// Preview or register the plain Postgres snapshot's two Windmill jobs (unit 53 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md, W8 slice D; it replaced scripts/register-postgres-snapshot.ps1, which
// nothing started by path, so it keeps no launcher). The CLI runs it as
//
//   antiphon-scripts register-postgres-snapshot [--apply] [--enable-schedule] [--run-once] [--definition-root <dir>]
//
// Preview is the default and contacts nothing: it checks scripts/windmill's antiphon-postgres-snapshot(.schedule).json and
// antiphon-postgres-snapshot-stale(.schedule).json and prints one "preview path=... schedule=... timezone=... enabled=..." line
// each. --enable-schedule and --run-once require --apply (REFUSED, exit 3). --apply copies the four definitions,
// register-postgres-snapshot.py and a generated register-remote.sh (the script's here-string, byte for byte) to
// mc@server2:/tmp/antiphon-snapshot-reg with ssh and scp, then runs it there: it mints a two-hour Windmill token in the
// windmill_db container, runs the Python registration (mode keep or enable, run skip or run) and deletes the token. The bearer is
// minted and deleted on server2; this process never reads or writes it. The Windmill API is reached only through that remote
// script: ssh and scp run on the host's launcher (resolved on PATH, sharing this console), the temporary directory is a seam.
// Lines and exits are the script's (captured from it before its deletion). ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Antiphon.Nightly
{
    /// <summary>The register-postgres-snapshot command's options (register-postgres-snapshot.ps1's parameters in kebab case).</summary>
    public sealed class RegisterPostgresSnapshotOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "register-postgres-snapshot [--apply] [--enable-schedule] [--run-once] [--definition-root <dir>]";

        /// <summary>-Apply: register on server2.</summary>
        public bool Apply { get; init; }

        /// <summary>-EnableSchedule: enable the schedules (requires -Apply).</summary>
        public bool EnableSchedule { get; init; }

        /// <summary>-RunOnce: run the job once after registering (requires -Apply).</summary>
        public bool RunOnce { get; init; }

        /// <summary>-DefinitionRoot (blank: the checkout's scripts/windmill).</summary>
        public string DefinitionRoot { get; init; } = "";

        /// <summary>The flags; checkoutRoot supplies the default definition root.</summary>
        public static RegisterPostgresSnapshotOptions Parse(IReadOnlyList<string> arguments, string checkoutRoot)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--definition-root" }, new[] { "--apply", "--enable-schedule", "--run-once" });
            var root = flags.Value("--definition-root", "");
            return new RegisterPostgresSnapshotOptions
            {
                Apply = flags.Switch("--apply"),
                EnableSchedule = flags.Switch("--enable-schedule"),
                RunOnce = flags.Switch("--run-once"),
                DefinitionRoot = string.IsNullOrWhiteSpace(root) ? Path.Combine(checkoutRoot ?? "", "scripts", "windmill") : root,
            };
        }
    }

    /// <summary>The registration's seams beyond the host.</summary>
    public sealed class RegisterPostgresSnapshotSeams
    {
        /// <summary>Get-Command's application for ssh and scp: its path, null when there is none.</summary>
        public Func<string, string> Resolve { get; init; } = OwnedLauncher.FindApplication;

        /// <summary>[System.IO.Path]::GetTempPath().</summary>
        public Func<string> TempDirectory { get; init; } = Path.GetTempPath;

        /// <summary>A fresh 32-hex token for the local staging directory.</summary>
        public Func<string> Token { get; init; } = () => Guid.NewGuid().ToString("N");
    }

    /// <summary>register-postgres-snapshot.ps1's body.</summary>
    public static class RegisterPostgresSnapshot
    {
        /// <summary>The remote staging directory.</summary>
        public const string RemoteRoot = "/tmp/antiphon-snapshot-reg";

        /// <summary>The definition pairs, in order.</summary>
        public static readonly IReadOnlyList<(string Script, string Schedule)> Pairs = new[]
        {
            ("antiphon-postgres-snapshot.json", "antiphon-postgres-snapshot.schedule.json"),
            ("antiphon-postgres-snapshot-stale.json", "antiphon-postgres-snapshot-stale.schedule.json"),
        };

        /// <summary>The files uploaded beside register-remote.sh.</summary>
        public static readonly IReadOnlyList<string> Uploads = new[]
        {
            "antiphon-postgres-snapshot.json", "antiphon-postgres-snapshot.schedule.json", "antiphon-postgres-snapshot-stale.json",
            "antiphon-postgres-snapshot-stale.schedule.json", "register-postgres-snapshot.py",
        };

        // The script's here-string (CRLF folded to LF when written).
        private const string RemoteTemplate = "set -euo pipefail\n"
            + "ROOT=/tmp/antiphon-snapshot-reg\n"
            + "TOK=$(openssl rand -hex 20)\n"
            + "HASH=$(printf %s \"$TOK\" | sha256sum | cut -d' ' -f1)\n"
            + "PREFIX=${TOK:0:10}\n"
            + "cleanup() {\n"
            + "  docker exec windmill-windmill_db-1 psql -U windmill -d windmill -c \"DELETE FROM token WHERE label='snapshot-reg' AND token_prefix='${PREFIX}';\" >/dev/null 2>&1 || true\n"
            + "  unset TOK HASH WM_TOKEN\n"
            + "  rm -f /tmp/snapshot-reg.psql\n"
            + "  rm -rf \"$ROOT\"\n"
            + "}\n"
            + "trap cleanup EXIT\n"
            + "if ! docker exec windmill-windmill_db-1 psql -U windmill -d windmill -v ON_ERROR_STOP=1 -c \"INSERT INTO token (token,token_hash,token_prefix,email,super_admin,label,expiration) VALUES ('${TOK}','${HASH}','${PREFIX}','lndcobra@hotmail.com',true,'snapshot-reg',now()+interval '2 hours');\" >/tmp/snapshot-reg.psql 2>&1; then\n"
            + "  echo \"psql=fail\"\n"
            + "  exit 1\n"
            + "fi\n"
            + "echo \"psql=ok\"\n"
            + "export WM_TOKEN=\"$TOK\"\n"
            + "set +e\n"
            + "python3 \"$ROOT/register-postgres-snapshot.py\" \"$ROOT\" \"__MODE__\" \"__RUN__\"\n"
            + "status=$?\n"
            + "set -e\n"
            + "unset WM_TOKEN\n"
            + "exit \"$status\"";

        /// <summary>register-remote.sh for these switches: mode keep or enable, run skip or run.</summary>
        public static string RemoteScript(bool enableSchedule, bool runOnce) =>
            RemoteTemplate.Replace("__MODE__", enableSchedule ? "enable" : "keep").Replace("__RUN__", runOnce ? "run" : "skip");

        /// <summary>The command: the refusals, the preview, and with --apply the upload and the remote registration.</summary>
        public static int Run(RegisterPostgresSnapshotOptions options, NightlyHost host, RegisterPostgresSnapshotSeams seams = null)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            seams ??= new RegisterPostgresSnapshotSeams();
            try
            {
                return Register(options, host, seams);
            }
            catch (Exception e)
            {
                host.Error.WriteLine("register-postgres-snapshot: " + e.Message.Replace("\r", " ").Replace("\n", " "));
                return 1;
            }
        }

        private static int Register(RegisterPostgresSnapshotOptions options, NightlyHost host, RegisterPostgresSnapshotSeams seams)
        {
            var root = options.DefinitionRoot;
            if (options.EnableSchedule && !options.Apply)
            {
                host.Out.WriteLine("REFUSED: -EnableSchedule requires -Apply.");
                return 3;
            }
            if (options.RunOnce && !options.Apply)
            {
                host.Out.WriteLine("REFUSED: -RunOnce requires -Apply.");
                return 3;
            }
            foreach (var pair in Pairs)
            {
                var scriptPath = Path.Combine(root, pair.Script);
                var schedulePath = Path.Combine(root, pair.Schedule);
                if (!Exists(scriptPath) || !Exists(schedulePath))
                {
                    host.Out.WriteLine("REFUSED: missing definition " + pair.Script);
                    return 3;
                }
                var script = Read(scriptPath);
                var schedule = Read(schedulePath);
                var scheduleScript = SnapshotJson.Str(SnapshotJson.Get(schedule, "script_path"));
                var scriptName = SnapshotJson.Str(SnapshotJson.Get(script, "path"));
                if (!string.Equals(scheduleScript, scriptName, StringComparison.OrdinalIgnoreCase))
                {
                    host.Out.WriteLine("REFUSED: schedule does not name script " + scriptName);
                    return 3;
                }
                var enabled = SnapshotJson.Truthy(SnapshotJson.Get(schedule, "enabled")) ? "true" : "false";
                host.Out.WriteLine("preview path=" + SnapshotJson.Str(SnapshotJson.Get(schedule, "path")) + " schedule=" + SnapshotJson.Str(SnapshotJson.Get(schedule, "schedule"))
                    + " timezone=" + SnapshotJson.Str(SnapshotJson.Get(schedule, "timezone")) + " enabled=" + enabled);
            }
            if (!options.Apply) return 0;

            var local = Path.Combine(seams.TempDirectory(), "snapshot-reg-" + seams.Token());
            Directory.CreateDirectory(local);
            try
            {
                File.WriteAllText(Path.Combine(local, "register-remote.sh"), RemoteScript(options.EnableSchedule, options.RunOnce), new UTF8Encoding(false));
                foreach (var name in Uploads) File.Copy(Path.Combine(root, name), Path.Combine(local, name), true);
                if (Tool(host, seams, "ssh", new[] { "-o", "BatchMode=yes", "-o", "StrictHostKeyChecking=accept-new", "mc@server2",
                        "rm -rf " + RemoteRoot + " && mkdir -p " + RemoteRoot }) != 0)
                {
                    host.Out.WriteLine("REFUSED: ssh mkdir failed");
                    return 1;
                }
                var uploads = new DirectoryInfo(local).GetFiles().Select(file => file.FullName).OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase).ToList();
                var scp = new List<string> { "-o", "BatchMode=yes" };
                scp.AddRange(uploads);
                scp.Add("mc@server2:" + RemoteRoot + "/");
                if (Tool(host, seams, "scp", scp) != 0)
                {
                    host.Out.WriteLine("REFUSED: scp failed");
                    return 1;
                }
                var code = Tool(host, seams, "ssh", new[] { "-o", "BatchMode=yes", "mc@server2", "bash " + RemoteRoot + "/register-remote.sh" });
                if (code != 0)
                {
                    host.Out.WriteLine("REFUSED: remote register exit=" + code.ToString(CultureInfo.InvariantCulture));
                    return code;
                }
                return 0;
            }
            finally
            {
                try { Directory.Delete(local, true); } catch (Exception) { /* -ErrorAction SilentlyContinue */ }
            }
        }

        private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

        // Get-Content -Raw | ConvertFrom-Json: a blank file reads as nothing (every field empty); text that is not JSON throws.
        private static JsonElement? Read(string path)
        {
            var text = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(text)) return null;
            return SnapshotJson.Parse(text) ?? throw new InvalidDataException("not JSON: " + path);
        }

        // `& tool @arguments` on this console: the child's own output is shown as it comes.
        private static int Tool(NightlyHost host, RegisterPostgresSnapshotSeams seams, string tool, IReadOnlyList<string> arguments)
        {
            var file = seams.Resolve?.Invoke(tool) ?? tool;
            return host.Launch(new NightlyLaunch(file, arguments, "")).ExitCode;
        }
    }
}
