// CARD-1406 server2 host Postgres service and scratch restore drill (unit 54 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md, W8 slice D). It replaced scripts/deploy-server2-postgres.ps1,
// which nothing started by path (docs/docker-stack.md and the CARD-1406 runbooks told the operator to run it on the server2 host
// shell). The CLI runs it as
//
//   antiphon-scripts deploy-server2-postgres [--what-if | --execute] [--restore-drill] [--project antiphon-server2-postgres]
//       [--env-file <file>] [--compose-file <file>] [--repo-root <dir>]
//
// (scripts/antiphon deploy-server2-postgres on the server2 host, scripts\antiphon.cmd deploy-server2-postgres on Windows).
//
// Default is --what-if. It prints the host-daemon command and, with --restore-drill, the scratch restore commands. --what-if does
// not call docker. --execute starts the service only when this process is on the server2 host daemon (not inside the runner
// container, not a sibling daemon). The env file is untracked, mode 0600, and outside the repo. It holds
// ANTIPHON_POSTGRES_BIND_ADDRESS, ANTIPHON_POSTGRES_IMAGE, and POSTGRES_PASSWORD. This command never prints the password.
// --what-if accepts only a bind inside 100.64.0.0/10. --execute still requires that bind to equal the single line of
// tailscale ip -4. The image must be postgres:16@sha256:<64 hex>, postgres@sha256:<64 hex>, or an exact 16.N patch tag with a
// digest: postgres:16.14@sha256:<64 hex> (or postgres:16.14-trixie@sha256:<64 hex>). The production compose runs only as project
// antiphon-server2-postgres. The drill compose runs only as project antiphon-server2-postgres-drill. The drill commands take or
// locate the encrypted B1 snapshot, time the copy to server2, decrypt it with age under the drill scratch directory mode 0600,
// restore into the scratch volume, shred the plaintext, and remove only that scratch project. CARD-1406 final layout: Postgres is
// pinned to one physical core (CPU 0 and its SMT sibling 12), not exclusively, and wins a shared CPU by cpu_shares 2048; Cpuset
// is the one value to change with the compose files (two physical cores is "0-1,12-13").
// Exit codes: 0 plan or ok, 2 refusal, 3 ambiguous-mode.
//
// Every step, refusal, reason and line is the script's, in its order (captured from it before its deletion), with PowerShell's
// semantics where they decide: -match is a case-insensitive regex and -cmatch a case-sensitive one, -eq and -ne compare strings
// ignoring case, the env file's keys ignore case (a PowerShell hashtable) and a later key wins, a file that exists but cannot be
// read reads as empty (the script's .NET call failed with an error record and went on), and the POSIX mode check refuses
// whenever the OS variable is Windows_NT. docker, hostname and tailscale are found as Get-Command -CommandType Application found
// them (Server2PostgresDeploySeams.Resolve; the CLI binds OperatorToolOs.Find) and run on the host's launcher with their output
// captured (stderr discarded, as 2>$null). Files are IFileSystem's. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The deploy-server2-postgres command's options (deploy-server2-postgres.ps1's parameters in kebab case).</summary>
    public sealed class Server2PostgresDeployOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "deploy-server2-postgres [--what-if | --execute] [--restore-drill] [--project " + Server2PostgresDeploy.Project + "]"
            + " [--env-file <file>] [--compose-file <file>] [--repo-root <dir>]";

        /// <summary>--what-if: the plan (also the default without --execute).</summary>
        public bool WhatIf { get; init; }

        /// <summary>--execute: start the service on the server2 host daemon.</summary>
        public bool Execute { get; init; }

        /// <summary>--restore-drill: the scratch restore drill's commands instead of the service.</summary>
        public bool RestoreDrill { get; init; }

        /// <summary>--project: the compose project (blank: the mode's own).</summary>
        public string Project { get; init; } = "";

        /// <summary>--env-file: the untracked mode-0600 env file outside the repo.</summary>
        public string EnvFile { get; init; } = "";

        /// <summary>--compose-file (blank: the repo's docker-compose.server2-postgres[-drill].yml).</summary>
        public string ComposeFile { get; init; } = "";

        /// <summary>--repo-root (blank: Checkout).</summary>
        public string RepoRoot { get; init; } = "";

        /// <summary>The checkout this command belongs to (the script's parent directory); the default repo root.</summary>
        public string Checkout { get; init; } = "";

        /// <summary>The flags after the command name; an unknown or valueless flag is a usage error. Values are kept as given.</summary>
        public static Server2PostgresDeployOptions Parse(IReadOnlyList<string> arguments, string checkout)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--project", "--env-file", "--compose-file", "--repo-root" },
                new[] { "--what-if", "--execute", "--restore-drill" });
            return new Server2PostgresDeployOptions
            {
                WhatIf = flags.Switch("--what-if"),
                Execute = flags.Switch("--execute"),
                RestoreDrill = flags.Switch("--restore-drill"),
                Project = flags.Value("--project", ""),
                EnvFile = flags.Value("--env-file", ""),
                ComposeFile = flags.Value("--compose-file", ""),
                RepoRoot = flags.Value("--repo-root", ""),
                Checkout = checkout ?? "",
            };
        }
    }

    /// <summary>What the command observes outside its files and launcher.</summary>
    public sealed class Server2PostgresDeploySeams
    {
        /// <summary>
        /// Get-Command &lt;name&gt; -CommandType Application for docker, hostname and tailscale: the path, null when there is none.
        /// Null: OperatorToolOs.Find over the host's PATH and PATHEXT.
        /// </summary>
        public Func<string, string> Resolve { get; init; }

        /// <summary>
        /// [System.IO.File]::GetUnixFileMode(path) equals UserRead|UserWrite (asked only when the OS variable is not Windows_NT); a
        /// mode that cannot be read is false. Default: this machine's.
        /// </summary>
        public Func<string, bool> OwnerReadWriteOnly { get; init; } = Server2PostgresDeploy.IsOwnerReadWriteOnly;
    }

    /// <summary>The env file as the script read it (a PowerShell 7 hashtable): keys ignore case, ordinally, and a later line wins.</summary>
    public sealed class Server2PostgresEnv
    {
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Read-Server2PostgresEnv: each trimmed line that is not blank or a comment and has '=' after its first character.</summary>
        public static Server2PostgresEnv Parse(IEnumerable<string> lines)
        {
            var env = new Server2PostgresEnv();
            foreach (var line in lines ?? Array.Empty<string>())
            {
                var trim = (line ?? "").Trim();
                if (trim.Length == 0 || trim.StartsWith("#", StringComparison.CurrentCulture)) continue;
                var eq = trim.IndexOf('=', StringComparison.Ordinal);
                if (eq < 1) continue;
                env._values[trim.Substring(0, eq).Trim()] = trim.Substring(eq + 1);
            }
            return env;
        }

        /// <summary>The key was written (with any value).</summary>
        public bool Present(string key) => _values.ContainsKey(key ?? "");

        /// <summary>The key's value; empty when it was not written.</summary>
        public string Value(string key) => _values.TryGetValue(key ?? "", out var value) ? value : "";

        /// <summary>The distinct keys written.</summary>
        public int Count => _values.Count;
    }

    /// <summary>Expand-Server2PostgresCompose's answer: whether the values passed, the reason and the rendered text (password redacted).</summary>
    public sealed record Server2PostgresRender(bool Ok, string Reason, string Redacted);

    /// <summary>The CARD-1406 server2 host Postgres deploy (deploy-server2-postgres.ps1; see the file comment).</summary>
    public static class Server2PostgresDeploy
    {
        /// <summary>The production compose project.</summary>
        public const string Project = "antiphon-server2-postgres";

        /// <summary>The scratch drill's compose project.</summary>
        public const string DrillProject = "antiphon-server2-postgres-drill";

        /// <summary>The scratch drill's pinned volume.</summary>
        public const string DrillVolume = "antiphon-server2-postgres-drill_pgdata";

        /// <summary>CARD-1406 final layout: CPU 0 and its SMT sibling 12, with cpu_shares 2048 (one value to change with the compose files).</summary>
        public const string Cpuset = "0,12";

        /// <summary>The production compose file's name in the repo.</summary>
        public const string ComposeName = "docker-compose.server2-postgres.yml";

        /// <summary>The drill compose file's name in the repo.</summary>
        public const string DrillComposeName = "docker-compose.server2-postgres-drill.yml";

        /// <summary>The env file's bind key.</summary>
        public const string BindKey = "ANTIPHON_POSTGRES_BIND_ADDRESS";

        /// <summary>The env file's image key.</summary>
        public const string ImageKey = "ANTIPHON_POSTGRES_IMAGE";

        /// <summary>The env file's password key.</summary>
        public const string PasswordKey = "POSTGRES_PASSWORD";

        private const string DockerEnv = "/.dockerenv";

        // PowerShell's -match is a case-insensitive regex in the current culture; [regex]::IsMatch and -cmatch are case-sensitive.
        private static readonly Regex Alpine = new Regex("(?i)alpine", RegexOptions.IgnoreCase);
        private static readonly Regex Ipv4 = new Regex(@"^(?:[0-9]{1,3}\.){3}[0-9]{1,3}$", RegexOptions.IgnoreCase);
        private static readonly Regex DigestTag = new Regex("^postgres:16@sha256:[0-9a-f]{64}$", RegexOptions.IgnoreCase);
        private static readonly Regex RepoDigest = new Regex("^postgres@sha256:[0-9a-f]{64}$", RegexOptions.IgnoreCase);
        private static readonly Regex PatchTag = new Regex("^postgres:16\\.(0|[1-9][0-9]*)(-trixie)?@sha256:[0-9a-f]{64}$");
        private static readonly Regex Windmill = new Regex("(?i)windmill", RegexOptions.IgnoreCase);
        private static readonly Regex AnyAddress = new Regex(@"0\.0\.0\.0", RegexOptions.IgnoreCase);
        private static readonly Regex Port5433 = new Regex("5433", RegexOptions.IgnoreCase);
        private static readonly Regex AntiphonPgdata = new Regex("antiphon_pgdata", RegexOptions.IgnoreCase);
        private static readonly Regex CpusetKey = new Regex(@"(?m)^\s*cpuset\s*:");
        private static readonly Regex CpuSharesKey = new Regex(@"(?m)^\s*cpu_shares\s*:");
        private static readonly Regex CpusKey = new Regex(@"(?m)^\s*cpus\s*:", RegexOptions.IgnoreCase);
        private static readonly Regex Lines = new Regex("\r?\n");
        private static readonly Regex DevPasswordLine = new Regex(@"POSTGRES_PASSWORD:\s*(\S+)");

        private static readonly string[] ComposeNeeds =
        {
            "name: antiphon-server2-postgres",
            "ANTIPHON_POSTGRES_BIND_ADDRESS:?",
            "ANTIPHON_POSTGRES_IMAGE:?",
            "POSTGRES_PASSWORD:?",
            ":5432:5432",
            "mem_limit: 8g",
            "shm_size: 1gb",
            "    blkio_config:\n      weight: 800",
            "restart: unless-stopped",
            "pg_isready -U antiphon -d antiphon",
            "shared_preload_libraries=pg_stat_statements",
            "pg_stat_statements.track=top",
            "pg_stat_statements.max=5000",
            "networks:\n  antiphon-server2:\n    name: antiphon-server2",
            "    networks:\n      - antiphon-server2",
            "pgdata:/var/lib/postgresql/data",
            "name: antiphon-server2-postgres_pgdata",
        };

        private static readonly string[] DrillComposeNeeds =
        {
            "name: antiphon-server2-postgres-drill",
            "ANTIPHON_POSTGRES_IMAGE:?",
            "POSTGRES_PASSWORD:?",
            "127.0.0.1:5434:5432",
            "mem_limit: 8g",
            "shm_size: 1gb",
            "    blkio_config:\n      weight: 800",
            "restart: unless-stopped",
            "pg_isready -U antiphon -d antiphon",
            "shared_preload_libraries=pg_stat_statements",
            "pgdata:/var/lib/postgresql/data",
            "name: antiphon-server2-postgres-drill_pgdata",
            "networks:\n  antiphon-server2-postgres-drill:\n    name: antiphon-server2-postgres-drill",
            "    networks:\n      - antiphon-server2-postgres-drill",
        };

        private sealed class DeployStop : Exception
        {
            public DeployStop(int code) => Code = code;

            public int Code { get; }
        }

        // ---- the checks (the script's functions) ----

        /// <summary>Test-Server2PostgresBind: '' for an IPv4 address inside 100.64.0.0/10, else the refusal.</summary>
        public static string Bind(bool provided, string address)
        {
            if (!provided) return "bind-omitted";
            if (string.IsNullOrWhiteSpace(address)) return "empty-bind";
            var trimmed = address.Trim();
            if (trimmed == "0.0.0.0" || trimmed == "::" || trimmed == "*") return "bind-any";
            if (!Ipv4.IsMatch(trimmed)) return "bind-not-ipv4";
            var octets = trimmed.Split('.');
            foreach (var octet in octets)
            {
                if (!int.TryParse(octet, NumberStyles.Integer, CultureInfo.CurrentCulture, out var n) || n > 255) return "bind-not-ipv4";
            }
            // 100.64.0.0/10. --execute still compares the surviving address with tailscale ip -4.
            int.TryParse(octets[1], NumberStyles.Integer, CultureInfo.CurrentCulture, out var second);
            var first = int.Parse(octets[0], NumberStyles.Integer, CultureInfo.InvariantCulture);
            if (first != 100 || second < 64 || second > 127) return "bind-not-cgnat";
            return "";
        }

        /// <summary>Test-Server2PostgresImage: '' for a digest pin, else empty-image, image-alpine or image-floating.</summary>
        public static string Image(string image)
        {
            if (string.IsNullOrWhiteSpace(image)) return "empty-image";
            if (Alpine.IsMatch(image)) return "image-alpine";
            if (DigestTag.IsMatch(image)) return "";
            if (RepoDigest.IsMatch(image)) return "";
            // Runbook G-2: an exact 16.N patch tag (Debian trixie default or -trixie) may sit beside the digest.
            if (PatchTag.IsMatch(image)) return "";
            return "image-floating";
        }

        /// <summary>Test-Server2PostgresDrillVolume: '' only for the drill's own scratch volume.</summary>
        public static string DrillVolumeRefusal(string name)
        {
            foreach (var forbidden in new[] { "antiphon_pgdata", "antiphon-server2-postgres_pgdata", "pgdata", "dind-data" })
                if (PsEquals(name, forbidden)) return "volume-forbidden";
            if (Windmill.IsMatch(name ?? "")) return "volume-forbidden";
            if (!string.Equals(name, DrillVolume, StringComparison.Ordinal)) return "volume-forbidden";
            return "";
        }

        /// <summary>Test-Server2PostgresDrillTeardown: the name must end with -drill_pgdata and be the drill's own volume.</summary>
        public static string DrillTeardown(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || !name.EndsWith("-drill_pgdata", StringComparison.Ordinal)) return "volume-forbidden";
            return DrillVolumeRefusal(name);
        }

        /// <summary>Test-Server2PostgresExactLine: the line, exactly, is one of the text's lines (CRLF read as LF).</summary>
        public static bool ExactLine(string text, string line)
        {
            var norm = (text ?? "").Replace("\r\n", "\n");
            return Regex.IsMatch(norm, "(?m)^" + Regex.Escape(line ?? "") + "$");
        }

        /// <summary>Get-Server2PostgresComposeClass: drill, production or unknown, by the project and pinned volume lines.</summary>
        public static string ComposeClass(string text)
        {
            var drill = ExactLine(text, "name: antiphon-server2-postgres-drill") && ExactLine(text, "    name: antiphon-server2-postgres-drill_pgdata");
            var prodVolume = ExactLine(text, "    name: antiphon-server2-postgres_pgdata");
            var prod = ExactLine(text, "name: antiphon-server2-postgres") && prodVolume;
            if (drill && !prodVolume) return "drill";
            if (prod && !drill) return "production";
            return "unknown";
        }

        /// <summary>Test-Server2PostgresTailnet: '' when tailscale ip -4 printed exactly one non-blank line, equal to the bind.</summary>
        public static string Tailnet(string bind, string tailscaleText)
        {
            var lines = Lines.Split(tailscaleText ?? "").Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
            if (lines.Count != 1) return "bind-not-tailnet";
            if (!string.Equals(lines[0], bind, StringComparison.Ordinal)) return "bind-not-tailnet";
            return "";
        }

        /// <summary>Get-Server2PostgresCpuRefusal: one cpuset line with the pin, one cpu_shares 2048 line and no cpus quota.</summary>
        public static string CpuRefusal(string text)
        {
            var norm = (text ?? "").Replace("\r\n", "\n");
            if (CpusetKey.Matches(norm).Count != 1) return "compose-invalid";
            if (!ExactLine(norm, "    cpuset: \"" + Cpuset + "\"")) return "compose-invalid";
            if (CpuSharesKey.Matches(norm).Count != 1) return "compose-invalid";
            if (!ExactLine(norm, "    cpu_shares: 2048")) return "compose-invalid";
            if (CpusKey.IsMatch(norm)) return "compose-invalid";
            return "";
        }

        /// <summary>Get-Server2PostgresComposeRefusal: the production compose's literals (the text as read; a CRLF file is compose-invalid).</summary>
        public static string ComposeRefusal(string text)
        {
            text ??= "";
            if (AntiphonPgdata.IsMatch(text)) return "volume-forbidden";
            if (Alpine.IsMatch(text)) return "image-alpine";
            if (AnyAddress.IsMatch(text)) return "bind-any";
            if (Port5433.IsMatch(text)) return "port-5433";
            foreach (var need in ComposeNeeds)
                if (!text.Contains(need, StringComparison.Ordinal)) return "compose-invalid";
            return CpuRefusal(text);
        }

        /// <summary>Get-Server2PostgresDrillComposeRefusal: the drill compose's literals (CRLF read as LF).</summary>
        public static string DrillComposeRefusal(string text)
        {
            var norm = (text ?? "").Replace("\r\n", "\n");
            if (norm.Contains("antiphon_pgdata", StringComparison.Ordinal)) return "volume-forbidden";
            if (norm.Contains("name: antiphon-server2-postgres_pgdata", StringComparison.Ordinal)) return "volume-forbidden";
            if (Port5433.IsMatch(norm)) return "port-5433";
            if (Alpine.IsMatch(norm)) return "image-alpine";
            if (AnyAddress.IsMatch(norm)) return "bind-any";
            if (norm.Contains(":5432:5432", StringComparison.Ordinal)) return "port-production";
            if (norm.Contains("ANTIPHON_POSTGRES_BIND_ADDRESS", StringComparison.Ordinal)) return "bind-not-loopback";
            if (ExactLine(norm, "      - antiphon-server2")) return "network-forbidden";
            if (norm.Contains("networks:\n  antiphon-server2:\n    name: antiphon-server2", StringComparison.Ordinal)) return "network-forbidden";
            foreach (var need in DrillComposeNeeds)
                if (!norm.Contains(need, StringComparison.Ordinal)) return "compose-invalid";
            return CpuRefusal(norm);
        }

        /// <summary>
        /// Expand-Server2PostgresCompose: the compose with the bind and image substituted and the password variable shown as
        /// &lt;redacted&gt;, after the bind, image and password checks (the first refusal, and no text, when one fails).
        /// </summary>
        public static Server2PostgresRender Render(string text, string bind, string image, string password)
        {
            var bindReason = Bind(true, bind);
            if (bindReason.Length > 0) return new Server2PostgresRender(false, bindReason, "");
            var imageReason = Image(image);
            if (imageReason.Length > 0) return new Server2PostgresRender(false, imageReason, "");
            if (string.IsNullOrWhiteSpace(password)) return new Server2PostgresRender(false, "empty-password", "");
            var redacted = text ?? "";
            redacted = Regex.Replace(redacted, @"\$\{ANTIPHON_POSTGRES_BIND_ADDRESS:\?[^}]*\}", bind);
            redacted = Regex.Replace(redacted, @"\$\{ANTIPHON_POSTGRES_IMAGE:\?[^}]*\}", image);
            redacted = Regex.Replace(redacted, @"\$\{POSTGRES_PASSWORD:\?[^}]*\}", "<redacted>");
            return new Server2PostgresRender(true, "ok", redacted);
        }

        /// <summary>Get-Server2PostgresHostCommand: the host-daemon command the plan prints.</summary>
        public static string HostCommand(string envPath, string composePath)
        {
            var envArg = string.IsNullOrWhiteSpace(envPath) ? "<env-file>" : envPath;
            return "docker compose --env-file " + envArg + " -f " + composePath + " -p " + Project + " up -d";
        }

        /// <summary>Write-Server2PostgresDrillPlan: the drill's header lines, then one drill-command line per command.</summary>
        public static IReadOnlyList<string> DrillPlan(string envPath, string composePath)
        {
            var envArg = string.IsNullOrWhiteSpace(envPath) ? "<env-file>" : envPath;
            const string project = DrillProject;
            const string volume = DrillVolume;
            const string container = project + "-postgres-1";
            var composeArg = Path.GetFileName(composePath ?? "");
            const string scratch = "/var/tmp/" + project;
            const string plain = scratch + "/antiphon-drill-plain.dump";
            const string cipher = scratch + "/antiphon-drill.dump.age";
            const string prefix = "drill-command: project=" + project + " volume=" + volume + " :: ";
            var lines = new List<string>
            {
                "drill-project: " + project,
                "drill-volume: " + volume,
                "drill-remove: scratch-only",
                "unread: docker-ps",
                "unread: docker-volume-ls",
                "unread: docker-data-root-df",
                "unread: windmill-compose-parent",
                "unread: count-star",
                "unread: dump-elapsed-sec",
                "unread: restore-elapsed-sec",
                "unread: copy-elapsed-sec",
                "unread: dump-bytes",
                "phase3: user-table-count",
                "phase3: sequence-last-value",
                "phase3: extension-version",
                "phase3: ef-migrations-history",
                "phase3: key-table-count-min-max-pk",
                "phase3: key tables are TranscriptEntries, AgentTasks, SessionQueuedMessages, AgentTaskLandNotifications, AgentTaskEvents",
                "phase3: count, minimum primary key, and maximum primary key; no row body",
            };
            const string migrations = "select \\\"MigrationId\\\" from \\\"__EFMigrationsHistory\\\" order by \\\"MigrationId\\\"";
            var commands = new[]
            {
                "pwsh -NoProfile -File scripts/postgres-snapshot.ps1 -Execute -Container antiphon-postgres -RecipientFile \"$AGE_RECIPIENT\"",
                "SNAPSHOT=$(ls -1t \"$SNAPSHOT_DIR\"/antiphon-*.dump.age | head -n 1)",
                "mkdir -p -m 0700 " + scratch,
                "docker volume create " + volume,
                "docker compose --env-file " + envArg + " -f " + composeArg + " -p " + project + " up -d",
                "copy_start=$(date +%s); scp \"$SNAPSHOT\" mc@server2:" + cipher + "; copy_end=$(date +%s); echo copy-elapsed-sec=$((copy_end - copy_start))",
                "umask 077; age -d -i \"$AGE_IDENTITY\" -o " + plain + " " + cipher + "; chmod 0600 " + plain,
                "docker exec -i " + container + " pg_restore -U antiphon -d antiphon --no-owner --no-acl < " + plain,
                "docker exec " + container + " psql -U antiphon -d antiphon -At -c \"select relname, n_live_tup from pg_stat_user_tables order by relname\"",
                "docker exec " + container + " psql -U antiphon -d antiphon -At -c \"" + migrations + "\"",
                "docker exec " + container + " psql -U antiphon -d antiphon -At -c \"select extname, extversion from pg_extension order by extname\"",
                "shred -u " + plain,
                "docker compose --env-file " + envArg + " -f " + composeArg + " -p " + project + " down",
                "if case " + volume + " in *-drill_pgdata) true ;; *) echo 'SERVER2-POSTGRES fail exit=2 reason=volume-forbidden'; exit 2 ;; esac; then docker volume rm " + volume + "; fi",
            };
            lines.AddRange(commands.Select(command => prefix + command));
            return lines;
        }

        /// <summary>Resolve-Server2PostgresLane: none (no daemon name), sibling (another host's daemon), nested (inside a container) or host.</summary>
        public static string Lane(string daemonName, string hostname, bool inContainer)
        {
            if (string.IsNullOrWhiteSpace(daemonName)) return "none";
            if (!PsEquals(daemonName, hostname)) return "sibling";
            if (inContainer) return "nested";
            return "host";
        }

        /// <summary>Test-Server2PostgresInsideRepo: the full path lies under the root (ordinal, after the root's trailing separators).</summary>
        public static bool InsideRepo(IFileSystem files, string path, string root)
        {
            if (files == null) throw new ArgumentNullException(nameof(files));
            var full = files.Path.GetFullPath(path);
            var rootFull = files.Path.GetFullPath(root).TrimEnd('/', '\\');
            return full.StartsWith(rootFull + files.Path.DirectorySeparatorChar, StringComparison.Ordinal);
        }

        /// <summary>The production mode check: the file's POSIX permission bits are exactly 0600; false on Windows or when unreadable.</summary>
        public static bool IsOwnerReadWriteOnly(string path)
        {
            if (OperatingSystem.IsWindows()) return false;
            try
            {
                return File.GetUnixFileMode(path) == (UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
            {
                return false;
            }
        }

        // PowerShell's -eq and -ne on strings: a case-insensitive comparison in the invariant culture.
        private static bool PsEquals(string left, string right) =>
            CultureInfo.InvariantCulture.CompareInfo.Compare(left ?? "", right ?? "", CompareOptions.IgnoreCase) == 0;

        // ---- the run ----

        /// <summary>
        /// Runs the command: the plan (the default and --what-if), or --execute on the server2 host daemon. Every outcome is one
        /// "SERVER2-POSTGRES &lt;plan|ok|fail&gt; exit=&lt;code&gt; reason=&lt;reason&gt;" line on host.Out, after the plan lines; the exit code is
        /// the line's. An I/O failure the script did not survive is one host.Error line, exit 1.
        /// </summary>
        public static int Run(Server2PostgresDeployOptions options, NightlyHost host, IFileSystem files, Server2PostgresDeploySeams seams = null)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            seams ??= new Server2PostgresDeploySeams();
            var resolve = seams.Resolve ?? new OperatorToolOs(host.Variables).Find;
            try
            {
                return Deploy(options, host, files, seams, resolve);
            }
            catch (DeployStop stop)
            {
                return stop.Code;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException
                || e is System.Security.SecurityException)
            {
                host.Error.WriteLine(e.Message);
                return 1;
            }
        }

        private static int Result(NightlyHost host, string kind, int code, string reason)
        {
            host.Out.WriteLine("SERVER2-POSTGRES " + kind + " exit=" + code.ToString(CultureInfo.InvariantCulture) + " reason=" + reason);
            return code;
        }

        private static void Fail(NightlyHost host, string reason, int code = 2)
        {
            Result(host, "fail", code, reason);
            throw new DeployStop(code);
        }

        private static int Deploy(Server2PostgresDeployOptions options, NightlyHost host, IFileSystem files, Server2PostgresDeploySeams seams, Func<string, string> resolve)
        {
            if (options.WhatIf && options.Execute) Fail(host, "ambiguous-mode", 3);
            var planOnly = !options.Execute;
            if (options.WhatIf) planOnly = true;
            var drillMode = options.RestoreDrill;
            var selectedProject = drillMode ? DrillProject : Project;
            if (!string.IsNullOrWhiteSpace(options.Project)) selectedProject = options.Project.Trim();
            if (drillMode)
            {
                if (!string.Equals(selectedProject, DrillProject, StringComparison.Ordinal)) Fail(host, "drill-compose-wrong-project");
            }
            else if (!string.Equals(selectedProject, Project, StringComparison.Ordinal)) Fail(host, "production-compose-wrong-project");

            var root = string.IsNullOrWhiteSpace(options.RepoRoot) ? options.Checkout ?? "" : options.RepoRoot;
            var composePath = options.ComposeFile ?? "";
            if (string.IsNullOrWhiteSpace(composePath)) composePath = files.Path.Combine(root, drillMode ? DrillComposeName : ComposeName);
            if (!Exists(files, composePath)) Fail(host, "missing-compose");
            var composeText = ReadText(files, host, composePath);
            var composeClass = ComposeClass(composeText);
            if (drillMode && composeClass != "drill") Fail(host, "production-compose-wrong-project");
            if (!drillMode && composeClass == "drill") Fail(host, "drill-compose-wrong-project");
            var composeReason = drillMode ? DrillComposeRefusal(composeText) : ComposeRefusal(composeText);
            if (composeReason.Length > 0) Fail(host, composeReason);
            var drillReason = DrillTeardown(DrillVolume);
            if (drillReason.Length > 0) Fail(host, drillReason);

            var envFile = options.EnvFile ?? "";
            var bind = "";
            if (!string.IsNullOrWhiteSpace(envFile))
            {
                if (!Exists(files, envFile)) Fail(host, "env-missing");
                if (InsideRepo(files, envFile, root)) Fail(host, "env-inside-repo");
                if (!Mode0600(host, seams, envFile)) Fail(host, "env-mode");
                var env = Server2PostgresEnv.Parse(ReadLines(files, host, envFile));
                var bindReason = Bind(env.Present(BindKey), env.Value(BindKey));
                if (bindReason.Length > 0) Fail(host, bindReason);
                var imageReason = Image(env.Value(ImageKey));
                if (imageReason.Length > 0) Fail(host, imageReason);
                var password = env.Value(PasswordKey);
                if (string.IsNullOrWhiteSpace(password)) Fail(host, "empty-password");
                var dev = DevPassword(files, host, root);
                if (!string.IsNullOrWhiteSpace(dev) && string.Equals(password, dev, StringComparison.Ordinal)) Fail(host, "dev-password");
                bind = env.Value(BindKey);
            }
            else if (!planOnly) Fail(host, "env-missing");

            if (planOnly)
            {
                if (drillMode)
                {
                    foreach (var line in DrillPlan(envFile, composePath)) host.Out.WriteLine(line);
                }
                else host.Out.WriteLine(HostCommand(envFile, composePath));
                return Result(host, "plan", 0, "ok");
            }

            if (ObservedLane(host, files, resolve) != "host") Fail(host, "not-server2-host");
            var tailscale = resolve("tailscale");
            if (tailscale == null) Fail(host, "bind-not-tailnet");
            var tailnetReason = Tailnet(bind, Capture(host, tailscale, "ip", "-4"));
            if (tailnetReason.Length > 0) Fail(host, tailnetReason);
            if (options.RestoreDrill)
            {
                // The scratch restore is the operator rehearsal. This switch prints it. It does not start a second project and it
                // does not remove a volume.
                foreach (var line in DrillPlan(envFile, composePath)) host.Out.WriteLine(line);
                return Result(host, "plan", 0, "ok");
            }
            var up = Up(host, resolve, envFile, composePath);
            if (up.Length > 0) Fail(host, up);
            return Result(host, "ok", 0, "ok");
        }

        // Test-Path -LiteralPath: a file or a directory.
        private static bool Exists(IFileSystem files, string path) => files.File.Exists(path) || files.Directory.Exists(path);

        // Test-Server2PostgresMode0600: never on Windows ($env:OS -eq 'Windows_NT'), else the POSIX bits are exactly 0600.
        private static bool Mode0600(NightlyHost host, Server2PostgresDeploySeams seams, string path)
        {
            if (PsEquals(host.Variables("OS"), "Windows_NT")) return false;
            return (seams.OwnerReadWriteOnly ?? IsOwnerReadWriteOnly)(path);
        }

        // [System.IO.File]::ReadAllText: a file the script could not read failed with an error record and read as empty.
        private static string ReadText(IFileSystem files, NightlyHost host, string path)
        {
            try
            {
                return files.File.ReadAllText(path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                host.Error.WriteLine(e.Message);
                return "";
            }
        }

        // Read-Server2PostgresEnv's [System.IO.File]::ReadAllLines, with ReadText's failure rule.
        private static IReadOnlyList<string> ReadLines(IFileSystem files, NightlyHost host, string path)
        {
            try
            {
                return files.File.ReadAllLines(path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                host.Error.WriteLine(e.Message);
                return Array.Empty<string>();
            }
        }

        // Get-Server2PostgresDevPassword: docker-compose.dev.yml's first POSTGRES_PASSWORD value under the root; '' without one.
        private static string DevPassword(IFileSystem files, NightlyHost host, string root)
        {
            var path = files.Path.Combine(root, "docker-compose.dev.yml");
            if (!Exists(files, path)) return "";
            var match = DevPasswordLine.Match(ReadText(files, host, path));
            return match.Success ? match.Groups[1].Value : "";
        }

        // Get-Server2PostgresObservedLane: refuses inside a container before any docker call; then docker info's daemon name and
        // hostname, both read before the lane is decided.
        private static string ObservedLane(NightlyHost host, IFileSystem files, Func<string, string> resolve)
        {
            if (Exists(files, DockerEnv)) return "nested";
            var docker = resolve("docker");
            if (docker == null) return "none";
            var daemon = Capture(host, docker, "info", "--format", "{{.Name}}").Trim();
            var hostname = resolve("hostname");
            string own;
            if (hostname == null)
            {
                // The call operator's error record (the script went on with an empty name).
                host.Error.WriteLine("The term 'hostname' is not recognized as a name of a cmdlet, function, script file, or executable program.");
                own = "";
            }
            else own = Capture(host, hostname).Trim();
            return Lane(daemon, own, Exists(files, DockerEnv));
        }

        // `& tool @arguments 2>$null | Out-String`: the tool's stdout (stderr discarded); a tool that cannot start is its error line and empty output.
        private static string Capture(NightlyHost host, string tool, params string[] arguments)
        {
            try
            {
                return host.Launch(new NightlyLaunch(tool, arguments, "", CaptureOutput: true)).StandardOutputText ?? "";
            }
            catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is FileNotFoundException)
            {
                host.Error.WriteLine(e.Message);
                return "";
            }
        }

        // Invoke-Server2PostgresUp: docker compose up -d for the production project, its output redirected and not printed.
        private static string Up(NightlyHost host, Func<string, string> resolve, string envPath, string composePath)
        {
            var docker = resolve("docker");
            if (docker == null) return "missing-docker";
            NightlyLaunchResult result;
            try
            {
                result = host.Launch(new NightlyLaunch(docker, new[] { "compose", "--env-file", envPath, "-f", composePath, "-p", Project, "up", "-d" }, "",
                    CaptureOutput: true));
            }
            catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is FileNotFoundException)
            {
                host.Error.WriteLine(e.Message);
                return "compose-up-failed";
            }
            return result.ExitCode != 0 ? "compose-up-failed" : "";
        }
    }
}
