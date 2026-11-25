// CARD-1405 daily Postgres snapshot and scratch restore drill (unit 53 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md,
// W8 slice D; it replaced the body of scripts/postgres-snapshot.ps1, which stays as a thin launcher because the Windmill job's
// runner copy, the cutover runbooks and deploy-server2-postgres.ps1 name it). The CLI runs it as
//
//   antiphon-scripts postgres-snapshot [--what-if | --execute] [--plain] [--one-drive-copy] [--one-drive-retain 7] [--restore-drill]
//       [--container antiphon-postgres] [--database antiphon] [--staging-path <dir>] [--destination <dir>] [--sync-root <dir>]
//       [--recipient-file <file>] [--recipient-ref <VAR>] [--identity-file <file>] [--identity-ref <VAR>] [--retain 14]
//       [--minimum-dump-bytes 1048576] [--keep-scratch] [--scratch-container antiphon-postgres-drill] [--scratch-database antiphon_drill]
//       [--scratch-volume antiphon-postgres-drill-pgdata] [--source-volume antiphon_pgdata] [--scratch-image postgres:16]
//       [--allow-remote-docker] [--stale-run-minutes 360] [--target desktop|server2]
//
// The default and --what-if print the plan and run no docker, age or pg_restore. --execute dumps with pg_dump -Fc inside the source
// container, checks pg_restore --list for TABLE DATA and a minimum size, encrypts with age and moves the ciphertext into the
// snapshot directory; retention keeps 14 exact dailies and prunes only after the new file is verified. --plain (operator decision
// 2026-11-10) writes a compressed unencrypted dump to C:\Users\lndco\antiphon-backups instead, verifies it twice, writes
// snapshot-status.json and with --one-drive-copy copies the verified dump into OneDrive\Backups\antiphon-postgres (7 kept).
// --restore-drill restores the newest snapshot into a labelled scratch container. --target server2 (plain only) runs every docker
// call through the antiphon-server2 context (ssh:// only) against the CARD-1406 production container. Every step, refusal,
// reason code and line is the script's (captured from it before its body was replaced); the reason codes are the exit codes.
//
// docker and age are resolved on PATH as Get-Command -CommandType Application did and run on the host's launcher (stdout and
// stderr merged, one string per line, as `& tool @args 2>&1`); the Windows ACLs and the POSIX owner and mode checks go through
// IPostgresSnapshotPlatform (production PostgresSnapshotOs: the .NET ACL API on Windows, geteuid/statx and the .NET mode API
// elsewhere, so no coreutils are started); the clock, sleep and environment are the host's. Files are the real disk: the link,
// reparse-point and lock checks are about the real one. Plain C#: no PowerShell types. ASCII only. Never pass a password or a
// connection string.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The postgres-snapshot command's options (postgres-snapshot.ps1's parameters in kebab case, with its defaults).</summary>
    public sealed class PostgresSnapshotOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "postgres-snapshot [--what-if | --execute] [--plain] [--one-drive-copy] [--one-drive-retain 7] [--restore-drill] "
            + "[--container antiphon-postgres] [--database antiphon] [--staging-path <dir>] [--destination <dir>] [--sync-root <dir>] "
            + "[--recipient-file <file>] [--recipient-ref <VAR>] [--identity-file <file>] [--identity-ref <VAR>] [--retain 14] "
            + "[--minimum-dump-bytes 1048576] [--keep-scratch] [--scratch-container antiphon-postgres-drill] [--scratch-database antiphon_drill] "
            + "[--scratch-volume antiphon-postgres-drill-pgdata] [--source-volume antiphon_pgdata] [--scratch-image postgres:16] "
            + "[--allow-remote-docker] [--stale-run-minutes 360] [--target desktop|server2]";

        private static readonly string[] Valued =
        {
            "--one-drive-retain", "--container", "--database", "--staging-path", "--destination", "--sync-root", "--recipient-file", "--recipient-ref",
            "--identity-file", "--identity-ref", "--retain", "--minimum-dump-bytes", "--scratch-container", "--scratch-database", "--scratch-volume",
            "--source-volume", "--scratch-image", "--stale-run-minutes", "--target",
        };

        private static readonly string[] Switches = { "--what-if", "--execute", "--plain", "--one-drive-copy", "--restore-drill", "--keep-scratch", "--allow-remote-docker" };

        /// <summary>-WhatIf: the plan only.</summary>
        public bool WhatIf { get; init; }

        /// <summary>-Execute: dump (or drill) for real.</summary>
        public bool Execute { get; init; }

        /// <summary>-Plain: the unencrypted compressed dump (operator decision 2026-11-10).</summary>
        public bool Plain { get; init; }

        /// <summary>-OneDriveCopy: copy the verified plain dump into OneDrive.</summary>
        public bool OneDriveCopy { get; init; }

        /// <summary>-OneDriveRetain: plain dumps kept in OneDrive.</summary>
        public int OneDriveRetain { get; init; } = 7;

        /// <summary>-RestoreDrill: restore the newest snapshot into the scratch container.</summary>
        public bool RestoreDrill { get; init; }

        /// <summary>-Container: the source container.</summary>
        public string Container { get; init; } = "antiphon-postgres";

        /// <summary>True when --container was given (-Container bound): --target server2 refuses it.</summary>
        public bool ContainerGiven { get; init; }

        /// <summary>-Database: the source database.</summary>
        public string Database { get; init; } = "antiphon";

        /// <summary>-StagingPath (blank: &lt;LocalApplicationData&gt;/antiphon/postgres-snapshot-staging).</summary>
        public string StagingPath { get; init; } = "";

        /// <summary>-Destination (blank: &lt;sync&gt;/antiphon-postgres, or C:\Users\lndco\antiphon-backups with --plain).</summary>
        public string Destination { get; init; } = "";

        /// <summary>-SyncRoot (blank: C:\Users\lndco\OneDrive).</summary>
        public string SyncRoot { get; init; } = "";

        /// <summary>-RecipientFile.</summary>
        public string RecipientFile { get; init; } = "";

        /// <summary>-RecipientRef: an environment variable naming the recipient file.</summary>
        public string RecipientRef { get; init; } = "";

        /// <summary>-IdentityFile.</summary>
        public string IdentityFile { get; init; } = "";

        /// <summary>-IdentityRef: an environment variable naming the identity file.</summary>
        public string IdentityRef { get; init; } = "";

        /// <summary>-Retain: snapshots kept.</summary>
        public int Retain { get; init; } = 14;

        /// <summary>-MinimumDumpBytes.</summary>
        public long MinimumDumpBytes { get; init; } = 1048576;

        /// <summary>-KeepScratch: leave the scratch container and volume.</summary>
        public bool KeepScratch { get; init; }

        /// <summary>-ScratchContainer.</summary>
        public string ScratchContainer { get; init; } = "antiphon-postgres-drill";

        /// <summary>-ScratchDatabase.</summary>
        public string ScratchDatabase { get; init; } = "antiphon_drill";

        /// <summary>-ScratchVolume.</summary>
        public string ScratchVolume { get; init; } = "antiphon-postgres-drill-pgdata";

        /// <summary>-SourceVolume.</summary>
        public string SourceVolume { get; init; } = "antiphon_pgdata";

        /// <summary>-ScratchImage (the age drill's image).</summary>
        public string ScratchImage { get; init; } = "postgres:16";

        /// <summary>-AllowRemoteDocker.</summary>
        public bool AllowRemoteDocker { get; init; }

        /// <summary>-StaleRunMinutes: a run directory older than this, with no live lock, is swept.</summary>
        public int StaleRunMinutes { get; init; } = 360;

        /// <summary>-Target: desktop or server2 (exactly; the script's ValidateSet was case-sensitive).</summary>
        public string Target { get; init; } = "desktop";

        /// <summary>
        /// The flags after the command name. An unknown or valueless flag, a non-integer number or a --target other than exactly
        /// desktop or server2 is a usage error (the script's parameter binding refused those with exit 1). Empty values are kept
        /// as given (the launcher passes the script's strings verbatim): a blank path means the default, as in the script.
        /// </summary>
        public static PostgresSnapshotOptions Parse(IReadOnlyList<string> arguments)
        {
            var flags = NightlyArgs.Parse(arguments, Valued, Switches);
            var target = flags.Value("--target", "desktop");
            if (!(target == "desktop" || target == "server2"))
                throw new NightlyUsageException("--target must be desktop or server2, not '" + target + "'");
            return new PostgresSnapshotOptions
            {
                WhatIf = flags.Switch("--what-if"),
                Execute = flags.Switch("--execute"),
                Plain = flags.Switch("--plain"),
                OneDriveCopy = flags.Switch("--one-drive-copy"),
                OneDriveRetain = flags.Int("--one-drive-retain", 7),
                RestoreDrill = flags.Switch("--restore-drill"),
                Container = flags.Value("--container", "antiphon-postgres"),
                ContainerGiven = flags.Has("--container"),
                Database = flags.Value("--database", "antiphon"),
                StagingPath = flags.Value("--staging-path", ""),
                Destination = flags.Value("--destination", ""),
                SyncRoot = flags.Value("--sync-root", ""),
                RecipientFile = flags.Value("--recipient-file", ""),
                RecipientRef = flags.Value("--recipient-ref", ""),
                IdentityFile = flags.Value("--identity-file", ""),
                IdentityRef = flags.Value("--identity-ref", ""),
                Retain = flags.Int("--retain", 14),
                MinimumDumpBytes = Long(flags, "--minimum-dump-bytes", 1048576),
                KeepScratch = flags.Switch("--keep-scratch"),
                ScratchContainer = flags.Value("--scratch-container", "antiphon-postgres-drill"),
                ScratchDatabase = flags.Value("--scratch-database", "antiphon_drill"),
                ScratchVolume = flags.Value("--scratch-volume", "antiphon-postgres-drill-pgdata"),
                SourceVolume = flags.Value("--source-volume", "antiphon_pgdata"),
                ScratchImage = flags.Value("--scratch-image", "postgres:16"),
                AllowRemoteDocker = flags.Switch("--allow-remote-docker"),
                StaleRunMinutes = flags.Int("--stale-run-minutes", 360),
                Target = target,
            };
        }

        private static long Long(NightlyArgs flags, string flag, long fallback)
        {
            var value = flags.Value(flag);
            if (string.IsNullOrEmpty(value)) return fallback;
            if (!long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed))
                throw new NightlyUsageException(flag + " expects an integer, not '" + value + "'");
            return parsed;
        }
    }

    /// <summary>One access entry of a Windows DACL as plain values (Get-SnapshotWindowsSecurity's rule object).</summary>
    public sealed record SnapshotAclRule(string Sid, string Type, long Rights, bool InheritOnly = false, bool Inherited = false);

    /// <summary>A directory's owner SID and every access entry, inherited or explicit.</summary>
    public sealed record SnapshotSecurity(string Owner, IReadOnlyList<SnapshotAclRule> Rules);

    /// <summary>A POSIX owner and the permission bits (stat -c '%u %a', the bits as a number).</summary>
    public sealed record SnapshotUnixStat(int Uid, int Mode);

    /// <summary>The operating system under the staging, destination and run-directory checks.</summary>
    public interface IPostgresSnapshotPlatform
    {
        /// <summary>$env:OS -eq 'Windows_NT': the Windows ACL rules apply; otherwise the POSIX owner and mode rules.</summary>
        bool IsWindows { get; }

        /// <summary>The caller's SID; empty when it cannot be read.</summary>
        string CallerSid();

        /// <summary>True when the caller is an elevated Administrator.</summary>
        bool Elevated();

        /// <summary>The directory's owner and access entries; null when the security cannot be read.</summary>
        SnapshotSecurity ReadSecurity(string path);

        /// <summary>id -u: the caller's uid; -1 when unknown.</summary>
        int Uid();

        /// <summary>stat -c '%u %a' (not following a link); null when it cannot be read.</summary>
        SnapshotUnixStat Stat(string path);

        /// <summary>
        /// Creates path owner-only in one step (Windows: a protected DACL naming only the caller; elsewhere mkdir -m 0700, whose
        /// parent must exist). False when it could not be created that way.
        /// </summary>
        bool CreatePrivateDirectory(string path);

        /// <summary>Makes an existing directory owner-only (Windows: the DACL rewritten to the caller; elsewhere chmod 700). False on failure.</summary>
        bool RestrictDirectory(string path);
    }

    /// <summary>The production platform: the .NET ACL API on Windows; geteuid, statx and the .NET mode API elsewhere.</summary>
    public sealed class PostgresSnapshotOs : IPostgresSnapshotPlatform
    {
        /// <inheritdoc />
        public bool IsWindows => OperatingSystem.IsWindows();

        /// <inheritdoc />
        public string CallerSid()
        {
            if (!OperatingSystem.IsWindows()) return "";
            try { return WindowsIdentity.GetCurrent().User?.Value ?? ""; } catch (Exception) { return ""; }
        }

        /// <inheritdoc />
        public bool Elevated()
        {
            if (!OperatingSystem.IsWindows()) return false;
            try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); } catch (Exception) { return false; }
        }

        /// <inheritdoc />
        public SnapshotSecurity ReadSecurity(string path)
        {
            if (!OperatingSystem.IsWindows()) return null;
            try
            {
                var acl = new DirectoryInfo(path).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Group | AccessControlSections.Access);
                var owner = acl.GetOwner(typeof(SecurityIdentifier));
                var rules = new List<SnapshotAclRule>();
                foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
                {
                    rules.Add(new SnapshotAclRule(rule.IdentityReference.Value, rule.AccessControlType.ToString(), (int)rule.FileSystemRights,
                        (rule.PropagationFlags & PropagationFlags.InheritOnly) != 0, rule.IsInherited));
                }
                return new SnapshotSecurity(owner?.Value ?? "", rules);
            }
            catch (Exception) { return null; }
        }

        /// <inheritdoc />
        public int Uid()
        {
            if (OperatingSystem.IsWindows()) return -1;
            try { return (int)GetEffectiveUid(); } catch (Exception) { return -1; }
        }

        /// <inheritdoc />
        public SnapshotUnixStat Stat(string path)
        {
            if (OperatingSystem.IsWindows()) return null;
            try
            {
                // AT_FDCWD, AT_SYMLINK_NOFOLLOW (GNU stat does not follow), STATX_TYPE | STATX_MODE | STATX_UID.
                if (Statx(-100, path, 0x100, 0x1 | 0x2 | 0x8, out var stat) != 0) return null;
                return new SnapshotUnixStat((int)stat.Uid, stat.Mode & 0xFFF);
            }
            catch (Exception) { return null; }
        }

        /// <inheritdoc />
        public bool CreatePrivateDirectory(string path)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    var sid = CallerSid();
                    if (string.IsNullOrWhiteSpace(sid)) return false;
                    var security = new DirectorySecurity();
                    security.SetAccessRuleProtection(true, false);
                    security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid), FileSystemRights.FullControl,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                    new DirectoryInfo(path).Create(security);
                    return true;
                }
                // mkdir without -p: the parent must exist and the path must not.
                var parent = Path.GetDirectoryName(Path.GetFullPath(path));
                if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent) || Directory.Exists(path) || File.Exists(path)) return false;
                Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                return true;
            }
            catch (Exception) { return false; }
        }

        /// <inheritdoc />
        public bool RestrictDirectory(string path)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    var sid = CallerSid();
                    if (string.IsNullOrWhiteSpace(sid)) return false;
                    var info = new DirectoryInfo(path);
                    var acl = info.GetAccessControl(AccessControlSections.Access);
                    acl.SetAccessRuleProtection(true, false);
                    foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, false, typeof(SecurityIdentifier))) acl.RemoveAccessRuleSpecific(rule);
                    acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid), FileSystemRights.FullControl,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                    info.SetAccessControl(acl);
                    return true;
                }
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                return true;
            }
            catch (Exception) { return false; }
        }

        // statx(2): stx_uid at 20, stx_mode at 28 (struct statx, 256 bytes).
        [StructLayout(LayoutKind.Explicit, Size = 256)]
        private struct LinuxStatx
        {
            [FieldOffset(0)] public uint Mask;
            [FieldOffset(20)] public uint Uid;
            [FieldOffset(28)] public ushort Mode;
        }

        [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
        private static extern int Statx(int fd, string path, int flags, uint mask, out LinuxStatx stat);

        [DllImport("libc", EntryPoint = "geteuid")]
        private static extern uint GetEffectiveUid();
    }

    /// <summary>The snapshot's seams beyond the host.</summary>
    public sealed class PostgresSnapshotSeams
    {
        /// <summary>Get-Command -CommandType Application's first answer for docker and age: its path, null when there is none.</summary>
        public Func<string, string> Resolve { get; init; } = OwnedLauncher.FindApplication;

        /// <summary>The ACL, owner and mode checks (production PostgresSnapshotOs).</summary>
        public IPostgresSnapshotPlatform Platform { get; init; } = new PostgresSnapshotOs();

        /// <summary>[Environment]::GetFolderPath('LocalApplicationData'), the default staging's base.</summary>
        public Func<string> LocalApplicationData { get; init; } = () => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    }

    /// <summary>Get-SnapshotPruneSet's answer: whether to prune, the names to drop and the reason.</summary>
    public sealed record SnapshotPruneSet(bool Ok, IReadOnlyList<string> Drop, string Reason);

    /// <summary>A retention pass: whether it held, the reason and how many dumps were left.</summary>
    public sealed record SnapshotRetention(bool Ok, string Reason, int Kept);

    /// <summary>The OneDrive copy's answer: ok, missing or failed; its bytes, folder, the loud line and a reason (link, outside).</summary>
    public sealed record SnapshotOneDriveResult(string State, long Bytes, string Folder, string Loud, string Reason);

    /// <summary>One step's answer: the exit code, the reason and an optional detail and loud line (the script's result objects).</summary>
    public sealed record SnapshotStep(int Code, string Reason, string Detail = null, string Loud = null);

    /// <summary>One native command's answer: its exit code and its merged output lines.</summary>
    public sealed record SnapshotNative(int Exit, IReadOnlyList<string> Lines);

    /// <summary>Get-SnapshotContainerFact: what docker inspect said about one container.</summary>
    public sealed class SnapshotContainerFact
    {
        /// <summary>docker inspect answered.</summary>
        public bool Found { get; init; }

        /// <summary>The answer was one JSON object.</summary>
        public bool Parse { get; init; }

        /// <summary>The full id.</summary>
        public string Id { get; init; } = "";

        /// <summary>The bare name (no leading or trailing slash).</summary>
        public string Name { get; init; } = "";

        /// <summary>The antiphon.snapshot-drill label's value.</summary>
        public string Label { get; init; } = "";

        /// <summary>The named volume mounts.</summary>
        public IReadOnlyList<string> Mounts { get; init; } = Array.Empty<string>();

        /// <summary>Every network alias.</summary>
        public IReadOnlyList<string> Aliases { get; init; } = Array.Empty<string>();

        /// <summary>Config.Env.</summary>
        public IReadOnlyList<string> Env { get; init; } = Array.Empty<string>();

        /// <summary>Mounts that are not named volumes (binds and the rest).</summary>
        public int Binds { get; init; }

        /// <summary>HostConfig.NetworkMode.</summary>
        public string NetworkMode { get; init; } = "";

        /// <summary>State.Running.</summary>
        public bool Running { get; init; }

        /// <summary>The com.docker.compose.project label.</summary>
        public string ComposeProject { get; init; } = "";

        /// <summary>Each named volume as name:destination.</summary>
        public IReadOnlyList<string> VolumeMounts { get; init; } = Array.Empty<string>();

        /// <summary>The image id.</summary>
        public string ImageId { get; init; } = "";

        /// <summary>Nothing answered.</summary>
        public static SnapshotContainerFact Missing { get; } = new SnapshotContainerFact();
    }

    /// <summary>Get-SnapshotVolumeFact: what docker volume inspect said.</summary>
    public sealed record SnapshotVolumeFact(bool Found, bool Parse, string Name, string Label);

    /// <summary>The script's constants and its pure functions (the decisions the driver tested by dot-sourcing the script).</summary>
    public static partial class PostgresSnapshot
    {
        /// <summary>The label a drill may reuse a container or volume by.</summary>
        public const string DrillLabel = "antiphon.snapshot-drill";

        /// <summary>An age snapshot's name.</summary>
        public const string DumpPattern = @"^antiphon-[0-9]{8}T[0-9]{6}Z\.dump\.age$";

        /// <summary>A plain dump's name.</summary>
        public const string PlainDumpPattern = @"^antiphon-[0-9]{8}T[0-9]{6}Z\.dump$";

        /// <summary>The plain destination's default (outside OneDrive, user-only).</summary>
        public const string PlainDestination = @"C:\Users\lndco\antiphon-backups";

        /// <summary>The OneDrive root's default.</summary>
        public const string DefaultSyncRoot = @"C:\Users\lndco\OneDrive";

        /// <summary>The operator decision that allows the plain path.</summary>
        public const string PlainDecision = "2026-11-10";

        /// <summary>OneDrive's default retention.</summary>
        public const int OneDriveRetainDefault = 7;

        /// <summary>The server2 docker context.</summary>
        public const string Server2Context = "antiphon-server2";

        /// <summary>The server2 production container.</summary>
        public const string Server2Container = "antiphon-server2-postgres-postgres-1";

        /// <summary>The server2 compose project.</summary>
        public const string Server2Project = "antiphon-server2-postgres";

        /// <summary>The server2 data volume.</summary>
        public const string Server2Volume = "antiphon-server2-postgres_pgdata";

        /// <summary>Where the server2 volume is mounted.</summary>
        public const string Server2DataPath = "/var/lib/postgresql/data";

        /// <summary>The plain banner.</summary>
        public const string PlainBanner = "PLAIN UNENCRYPTED operator-decision=" + PlainDecision + " encryption=none";

        /// <summary>Container names a drill never reuses, whichever is the source.</summary>
        public static readonly IReadOnlyList<string> ProductionContainers = new[] { "antiphon-postgres", "antiphon-server2-postgres-postgres-1" };

        /// <summary>Volume names a drill never touches.</summary>
        public static readonly IReadOnlyList<string> ProductionVolumes = new[] { "antiphon_pgdata", "antiphon-server2-postgres_pgdata", "pgdata" };

        /// <summary>SYSTEM.</summary>
        public const string SystemSid = "S-1-5-18";

        /// <summary>BUILTIN\Administrators.</summary>
        public const string AdministratorsSid = "S-1-5-32-544";

        /// <summary>NT SERVICE\TrustedInstaller.</summary>
        public const string TrustedInstallerSid = "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";

        /// <summary>DELETE, FILE_DELETE_CHILD, WRITE_DAC, WRITE_OWNER, GENERIC_WRITE and GENERIC_ALL.</summary>
        public const long HijackMask = 0x10000 | 0x40 | 0x40000 | 0x80000 | 0x40000000 | 0x10000000;

        // libpq's environment names (CARD-1413 D-1); PG_MAJOR, PG_VERSION and PGDATA are server settings and stay allowed.
        private static readonly string[] LibpqEnvNames =
        {
            "PGHOST", "PGHOSTADDR", "PGPORT", "PGDATABASE", "PGUSER", "PGPASSWORD", "PGPASSFILE",
            "PGREQUIREAUTH", "PGCHANNELBINDING", "PGSERVICE", "PGSERVICEFILE", "PGOPTIONS", "PGAPPNAME",
            "PGREQUIRESSL", "PGREQUIREPEER", "PGKRBSRVNAME", "PGCONNECT_TIMEOUT", "PGCLIENTENCODING",
            "PGTARGETSESSIONATTRS", "PGLOADBALANCEHOSTS", "PGDATESTYLE", "PGTZ", "PGGEQO",
            "PGSYSCONFDIR", "PGLOCALEDIR", "PGMINPROTOCOLVERSION", "PGMAXPROTOCOLVERSION",
        };

        private static readonly string[] LibpqEnvPrefixes = { "PGSSL", "PGGSS", "PGOAUTH" };

        // PowerShell's -match ignores case; -cmatch does not.
        internal const RegexOptions Ci = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;
        internal const RegexOptions Cs = RegexOptions.CultureInvariant;
        private static readonly Regex Token = new Regex("^[A-Za-z0-9_.-]+$", Ci);
        private static readonly Regex ImageChars = new Regex("^[A-Za-z0-9_.:/-]+$", Ci);
        private static readonly Regex Alpine = new Regex("alpine", Ci);
        private static readonly Regex Hex4To64 = new Regex("^[0-9a-fA-F]{4,64}$", Ci);
        private static readonly Regex TableDataLine = new Regex("(^|[^A-Za-z])TABLE DATA([^A-Za-z]|$)", Ci);
        private static readonly Regex RemoteEnv = new Regex("^(PGHOST|PGPORT|PGHOSTADDR)=(.*)$", Ci);
        private static readonly Regex EnvEntry = new Regex("^([^=]+)=(.*)$", Ci);
        private static readonly Regex LocalEndpoint = new Regex("^(unix|npipe)://", Ci);
        private static readonly Regex SshEndpoint = new Regex(@"^ssh://[^\s]+$", Ci);
        private static readonly Regex VersionPrefix = new Regex(@"^postgres \(PostgreSQL\)\s+", Ci);
        private static readonly Regex VersionDigits = new Regex(@"^[0-9]{1,3}\.[0-9]{1,4}$", Ci);
        private static readonly Regex Secret = new Regex(@"password\s*=|postgres://|Host\s*=", Ci);

        /// <summary>The reason a step ends with, as its exit code (Get-SnapshotReasonCode).</summary>
        public static int ReasonCode(string reason)
        {
            switch (reason)
            {
                case "ok": return 0;
                case "ambiguous-mode": case "missing-recipient": case "missing-identity": case "bad-name": case "plain-drill-unsupported": return 2;
                case "recipient-inside-sync": case "identity-inside-sync": case "recipient-link": case "identity-link": case "missing-age":
                case "missing-sync-root": case "destination-inside-onedrive": case "destination-acl": case "destination-link":
                case "staging-inside-sync": case "staging-inside-destination": case "staging-link": case "staging-mode": case "staging-owner":
                case "staging-parent": case "staging-cleanup": return 3;
                case "missing-docker": case "docker-host": case "docker-context": case "dump-failed": return 4;
                case "verify-failed": return 5;
                case "encrypt-failed": return 6;
                case "copy-failed": return 7;
                case "prune-failed": return 8;
                case "drill-refused": case "drill-failed": case "missing-snapshot": case "source-mismatch": return 9;
                default: return 1;
            }
        }

        /// <summary>Write-SnapshotResult's line: SNAPSHOT plan|ok|fail exit=&lt;n&gt; reason=&lt;r&gt; [detail].</summary>
        public static string ResultLine(bool planOnly, string reason, string detail)
        {
            var code = ReasonCode(reason);
            var mode = code == 0 ? (planOnly ? "plan" : "ok") : "fail";
            var line = "SNAPSHOT " + mode + " exit=" + Num(code) + " reason=" + reason;
            if (!string.IsNullOrWhiteSpace(detail)) line += " " + detail;
            return line;
        }

        /// <summary>Get-SnapshotPruneSet: which names a verified retention pass drops (newest kept, ordinal name order).</summary>
        public static SnapshotPruneSet PruneSet(IEnumerable<string> names, int retain = 14, string newName = "", bool verified = false, string namePattern = "")
        {
            var pattern = new Regex(string.IsNullOrWhiteSpace(namePattern) ? DumpPattern : namePattern, Ci);
            var none = Array.Empty<string>();
            if (!verified) return new SnapshotPruneSet(true, none, "unverified");
            if (retain < 1) return new SnapshotPruneSet(false, none, "retain-below-one");
            var all = new List<string>();
            foreach (var name in names ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (!pattern.IsMatch(name)) continue;
                all.Add(name);
            }
            if (!string.IsNullOrWhiteSpace(newName))
            {
                if (!pattern.IsMatch(newName)) return new SnapshotPruneSet(false, none, "bad-name");
                if (!all.Contains(newName)) all.Add(newName);
            }
            var ordered = all.ToArray();
            Array.Sort(ordered, StringComparer.Ordinal);
            Array.Reverse(ordered);
            if (ordered.Length <= retain) return new SnapshotPruneSet(true, none, "within-retain");
            var keep = new List<string>();
            var drop = new List<string>();
            for (var i = 0; i < ordered.Length; i++)
            {
                if (i < retain) keep.Add(ordered[i]); else drop.Add(ordered[i]);
            }
            if (!string.IsNullOrWhiteSpace(newName) && !keep.Contains(newName)) return new SnapshotPruneSet(false, none, "would-drop-new");
            if (ordered.Length - drop.Count < 1) return new SnapshotPruneSet(false, none, "would-leave-zero");
            return new SnapshotPruneSet(true, drop.ToArray(), "prune");
        }

        /// <summary>Test-SnapshotToken.</summary>
        public static bool IsToken(string value) => Token.IsMatch(value ?? "");

        /// <summary>Test-SnapshotImage: allowed characters and never alpine.</summary>
        public static bool IsImage(string image) => ImageChars.IsMatch(image ?? "") && !Alpine.IsMatch(image ?? "");

        /// <summary>Test-SnapshotPathInside: child is strictly below parent (full paths, ignoring case).</summary>
        public static bool PathInside(string parent, string child)
        {
            if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(child)) return false;
            var sep = Path.DirectorySeparatorChar;
            var parentFull = Path.GetFullPath(parent).TrimEnd(sep) + sep;
            var childFull = Path.GetFullPath(child);
            return childFull.StartsWith(parentFull, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Get-SnapshotBareName: trimmed, without leading or trailing slashes.</summary>
        public static string BareName(string value) => string.IsNullOrWhiteSpace(value) ? "" : value.Trim().TrimStart('/').TrimEnd('/');

        /// <summary>Test-SnapshotNameMatch: the bare name equals one of the candidates' bare names, ignoring case.</summary>
        public static bool NameMatch(string value, IEnumerable<string> candidates)
        {
            var bare = BareName(value);
            if (string.IsNullOrWhiteSpace(bare)) return false;
            foreach (var candidate in candidates ?? Array.Empty<string>())
            {
                var other = BareName(candidate);
                if (string.IsNullOrWhiteSpace(other)) continue;
                if (bare.Equals(other, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>Test-SnapshotSameId.</summary>
        public static bool SameId(string left, string right) =>
            !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) && left.Equals(right, StringComparison.OrdinalIgnoreCase);

        /// <summary>Test-SnapshotIdPrefixHit: a 4-64 hex token that prefixes an id (or an id of 4+ characters that prefixes it).</summary>
        public static bool IdPrefixHit(string token, IEnumerable<string> ids)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;
            var trimmed = token.Trim();
            if (!Hex4To64.IsMatch(trimmed)) return false;
            foreach (var id in ids ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(id)) continue;
                if (id.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase)) return true;
                if (trimmed.Length > id.Length && trimmed.StartsWith(id, StringComparison.OrdinalIgnoreCase) && id.Length >= 4) return true;
            }
            return false;
        }

        /// <summary>Test-SnapshotTableData: some line names TABLE DATA.</summary>
        public static bool HasTableData(IEnumerable<string> lines) => (lines ?? Array.Empty<string>()).Any(line => TableDataLine.IsMatch(line ?? ""));

        /// <summary>Get-SnapshotPlainTableCount: the TABLE DATA lines.</summary>
        public static int PlainTableCount(IEnumerable<string> lines) => (lines ?? Array.Empty<string>()).Count(line => TableDataLine.IsMatch(line ?? ""));

        /// <summary>Test-SnapshotPlainVerified: at least the minimum bytes and a TABLE DATA line.</summary>
        public static bool PlainVerified(IEnumerable<string> lines, long bytes, long minimumBytes) => bytes >= minimumBytes && HasTableData(lines);

        /// <summary>Test-SnapshotDrillRefused: the static guard on the names (container, volume, database).</summary>
        public static string DrillRefused(string sourceContainer, string sourceDatabase, string drillContainer, string drillDatabase, string drillVolume,
            string liveVolume)
        {
            if (NameMatch(drillContainer, new[] { sourceContainer })) return "production-container";
            if (NameMatch(drillContainer, ProductionContainers)) return "production-name";
            if (NameMatch(drillVolume, new[] { liveVolume })) return "production-volume";
            if (NameMatch(drillVolume, ProductionVolumes)) return "production-volume";
            if (NameMatch(drillDatabase, new[] { sourceDatabase })) return "database";
            if (NameMatch(drillDatabase, new[] { "antiphon" })) return "database";
            return "";
        }

        /// <summary>Test-SnapshotLocalEndpoint: unix:// or npipe://.</summary>
        public static bool IsLocalEndpoint(string endpoint) => !string.IsNullOrWhiteSpace(endpoint) && LocalEndpoint.IsMatch(endpoint.Trim());

        /// <summary>Test-SnapshotSshEndpoint: ssh:// and no whitespace.</summary>
        public static bool IsSshEndpoint(string endpoint) => !string.IsNullOrWhiteSpace(endpoint) && SshEndpoint.IsMatch(endpoint.Trim());

        /// <summary>Test-SnapshotRemoteEnv: a non-blank PGHOST, PGPORT or PGHOSTADDR.</summary>
        public static bool HasRemoteEnv(IEnumerable<string> entries)
        {
            foreach (var entry in entries ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(entry)) continue;
                var match = RemoteEnv.Match(entry);
                if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[2].Value)) return true;
            }
            return false;
        }

        /// <summary>Test-SnapshotLibpqName.</summary>
        public static bool IsLibpqName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            var upper = name.Trim().ToUpperInvariant();
            if (LibpqEnvNames.Contains(upper, StringComparer.OrdinalIgnoreCase)) return true;
            return LibpqEnvPrefixes.Any(prefix => upper.StartsWith(prefix, StringComparison.Ordinal));
        }

        /// <summary>Test-SnapshotPgEnv: a non-blank libpq variable.</summary>
        public static bool HasPgEnv(IEnumerable<string> entries)
        {
            foreach (var entry in entries ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(entry)) continue;
                var match = EnvEntry.Match(entry);
                if (!match.Success) continue;
                if (IsLibpqName(match.Groups[1].Value) && !string.IsNullOrWhiteSpace(match.Groups[2].Value)) return true;
            }
            return false;
        }

        /// <summary>Test-SnapshotNetworkGuard: host-network, shared-network or ''.</summary>
        public static string NetworkGuard(string mode)
        {
            if (string.IsNullOrWhiteSpace(mode)) return "";
            var trimmed = mode.Trim();
            if (trimmed.Equals("host", StringComparison.OrdinalIgnoreCase)) return "host-network";
            if (trimmed.StartsWith("container:", StringComparison.OrdinalIgnoreCase)) return "shared-network";
            return "";
        }

        /// <summary>Test-SnapshotReuseGuard: ok, or the first reason a container may not be reused (or kept) as the scratch.</summary>
        public static string ReuseGuard(SnapshotContainerFact fact, string sourceId, string sourceName, string sourceVolume, IEnumerable<string> productionIds,
            IEnumerable<string> sourceMounts)
        {
            if (fact == null || !fact.Found || !fact.Parse) return "inspect";
            if (SameId(fact.Id, sourceId)) return "source-id";
            if (IdPrefixHit(fact.Id, productionIds)) return "id-prefix";
            var names = ProductionContainers.Concat(new[] { sourceName }).ToArray();
            if (NameMatch(fact.Name, names)) return "production-name";
            foreach (var alias in fact.Aliases)
            {
                if (NameMatch(alias, new[] { fact.Name })) continue;
                if (NameMatch(alias, names)) return "alias";
            }
            if (fact.Label != "1") return "unlabelled";
            var listed = ProductionVolumes.Concat(new[] { sourceVolume }).ToArray();
            var mounts = (sourceMounts ?? Array.Empty<string>()).ToArray();
            foreach (var mount in fact.Mounts)
            {
                if (NameMatch(mount, listed)) return "production-mount";
                if (NameMatch(mount, mounts)) return "source-mount";
            }
            if (HasRemoteEnv(fact.Env)) return "pghost";
            if (HasPgEnv(fact.Env)) return "pg-env";
            if (fact.Binds > 0) return "bind-mount";
            var network = NetworkGuard(fact.NetworkMode);
            return network != "" ? network : "ok";
        }

        /// <summary>Test-SnapshotServer2Source (plan D-5): the running CARD-1406 production container with its pinned volume at the data path.</summary>
        public static bool IsServer2Source(SnapshotContainerFact fact)
        {
            if (fact == null || !fact.Found || !fact.Parse) return false;
            if (!string.Equals(fact.Name, Server2Container, StringComparison.Ordinal)) return false;
            if (!fact.Running) return false;
            if (!string.Equals(fact.ComposeProject, Server2Project, StringComparison.Ordinal)) return false;
            var wanted = Server2Volume + ":" + Server2DataPath;
            return fact.VolumeMounts.Any(pair => string.Equals(pair, wanted, StringComparison.Ordinal));
        }

        /// <summary>ConvertTo-SnapshotServerVersion: the first line's major.minor, or unknown.</summary>
        public static string ServerVersion(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "unknown";
            var line = Regex.Split(text, "\r?\n").FirstOrDefault(entry => !string.IsNullOrWhiteSpace(entry)) ?? "";
            var trimmed = VersionPrefix.Replace(line.Trim(), "");
            var first = Regex.Split(trimmed, @"\s+")[0];
            return VersionDigits.IsMatch(first) ? first : "unknown";
        }

        /// <summary>Test-SnapshotPlainForbiddenLink: a symlink or junction (an empty link type on a reparse point is the cloud-filter placeholder).</summary>
        public static bool ForbiddenLink(string linkType, bool reparsePoint)
        {
            var kind = linkType ?? "";
            if (reparsePoint && string.IsNullOrWhiteSpace(kind)) return false;
            return kind == "SymbolicLink" || kind == "Junction";
        }

        /// <summary>Test-SnapshotPlainInsideOneDrive: the destination is the OneDrive root or below it.</summary>
        public static bool PlainInsideOneDrive(string destination, string oneDriveRoot)
        {
            if (string.IsNullOrWhiteSpace(destination) || string.IsNullOrWhiteSpace(oneDriveRoot)) return false;
            var sep = Path.DirectorySeparatorChar;
            var destFull = Path.GetFullPath(destination).TrimEnd(sep);
            var rootFull = Path.GetFullPath(oneDriveRoot).TrimEnd(sep);
            if (destFull.Equals(rootFull, StringComparison.OrdinalIgnoreCase)) return true;
            return PathInside(oneDriveRoot, destination);
        }

        /// <summary>Get-SnapshotPlainOneDriveFolder: &lt;sync&gt;\Backups\antiphon-postgres.</summary>
        public static string OneDriveFolder(string syncRoot) => string.IsNullOrWhiteSpace(syncRoot) ? "" : Path.Combine(Path.Combine(syncRoot, "Backups"), "antiphon-postgres");

        /// <summary>New-SnapshotOneDriveResult with its loud line.</summary>
        public static SnapshotOneDriveResult OneDriveResult(string state, long bytes, string folder, string reason = "")
        {
            var loud = "ONEDRIVE COPY oneDrive=" + state;
            if (state == "ok") loud += " bytes=" + Num(bytes);
            else if (!string.IsNullOrWhiteSpace(reason)) loud += " reason=" + reason;
            return new SnapshotOneDriveResult(state, bytes, folder, loud, reason ?? "");
        }

        /// <summary>The Windows principals a caller's private directory may name.</summary>
        public static IReadOnlyList<string> PermittedSids(string callerSid) => new[] { callerSid, SystemSid, AdministratorsSid };

        /// <summary>
        /// Test-SnapshotWindowsRules. Strict (the root and each run directory): no Allow entry names a SID other than the caller,
        /// SYSTEM and Administrators. A parent: no other SID holds a right that renames, deletes or re-permissions it; an
        /// inherit-only entry does not apply. A Deny entry grants nothing. Unreadable rules or an unknown caller refuse.
        /// </summary>
        public static bool WindowsRules(IEnumerable<SnapshotAclRule> rules, string callerSid, bool strict)
        {
            if (rules == null) return false;
            if (string.IsNullOrWhiteSpace(callerSid)) return false;
            var permitted = PermittedSids(callerSid);
            foreach (var rule in rules)
            {
                if (rule == null) continue;
                if (!string.Equals(rule.Type, "Allow", StringComparison.OrdinalIgnoreCase)) continue;
                var sid = rule.Sid ?? "";
                if (!string.IsNullOrWhiteSpace(sid) && permitted.Contains(sid, StringComparer.OrdinalIgnoreCase)) continue;
                if (strict) return false;
                if (rule.InheritOnly) continue;
                var mask = rule.Rights;
                if ((mask & HijackMask) != 0) return false;
            }
            return true;
        }

        /// <summary>Test-SnapshotWindowsDirectory: '' or the refusal for one directory (strict: the root or a run directory).</summary>
        public static string WindowsDirectory(string path, bool strict, string callerSid, Func<string, SnapshotSecurity> reader, bool elevated)
        {
            if (string.IsNullOrWhiteSpace(callerSid)) return "staging-owner";
            var security = reader(path);
            if (security == null) return "staging-owner";
            var owners = new List<string> { callerSid };
            if (elevated) owners.Add(AdministratorsSid);
            if (!strict) owners.AddRange(new[] { SystemSid, AdministratorsSid, TrustedInstallerSid });
            if (string.IsNullOrWhiteSpace(security.Owner) || !owners.Contains(security.Owner, StringComparer.OrdinalIgnoreCase)) return "staging-owner";
            if (!WindowsRules(security.Rules, callerSid, strict)) return strict ? "staging-mode" : "staging-parent";
            return "";
        }

        /// <summary>Test-SnapshotWindowsStagingRoot: the root strict, then every parent up to the drive root.</summary>
        public static string WindowsStagingRoot(string path, string callerSid, Func<string, SnapshotSecurity> reader, bool elevated)
        {
            var full = Path.GetFullPath(path);
            var state = WindowsDirectory(full, true, callerSid, reader, elevated);
            if (state != "") return state;
            var cursor = Path.GetDirectoryName(full);
            while (!string.IsNullOrWhiteSpace(cursor))
            {
                state = WindowsDirectory(cursor, false, callerSid, reader, elevated);
                if (state != "") return state;
                var parent = Path.GetDirectoryName(cursor);
                if (string.IsNullOrWhiteSpace(parent) || parent == cursor) break;
                cursor = parent;
            }
            return "";
        }

        /// <summary>Test-SnapshotWindowsOwnedDirectory: the directory passes the strict check.</summary>
        public static bool WindowsOwnedDirectory(string path, string callerSid, Func<string, SnapshotSecurity> reader, bool elevated) =>
            WindowsDirectory(Path.GetFullPath(path), true, callerSid, reader, elevated) == "";

        /// <summary>ConvertTo-SnapshotJsonString: backslash and quote escaped.</summary>
        public static string JsonString(string value) => string.IsNullOrEmpty(value) ? "" : value.Replace("\\", "\\\\").Replace("\"", "\\\"");

        /// <summary>The plain status JSON (Write-SnapshotPlainStatus's text); null when an argument refuses it.</summary>
        public static string PlainStatusJson(string utc, int code, long bytes, string sha256Prefix, int durationSec, int tables, string oneDrive = "",
            long oneDriveBytes = -1, string oneDriveFolder = "", string target = "desktop", string serverVersion = "")
        {
            if (!(string.Equals(target, "desktop", StringComparison.Ordinal) || string.Equals(target, "server2", StringComparison.Ordinal))) return null;
            var version = ServerVersion(serverVersion);
            if (!Regex.IsMatch(utc ?? "", "^[0-9]{8}T[0-9]{6}Z$", Ci)) return null;
            if (!Regex.IsMatch(sha256Prefix ?? "", "^[0-9a-f]{12}$", Ci)) sha256Prefix = "000000000000";
            if (durationSec < 0) durationSec = 0;
            if (tables < 0) tables = 0;
            var cloud = "";
            if (string.Equals(oneDrive, "ok", StringComparison.OrdinalIgnoreCase) || string.Equals(oneDrive, "missing", StringComparison.OrdinalIgnoreCase)
                || string.Equals(oneDrive, "failed", StringComparison.OrdinalIgnoreCase))
            {
                if (oneDriveBytes < 0) oneDriveBytes = 0;
                cloud = ",\"oneDrive\":\"" + oneDrive + "\",\"oneDriveBytes\":" + Num(oneDriveBytes) + ",\"oneDriveFolder\":\"" + JsonString(oneDriveFolder) + "\"";
            }
            var json = "{\"utc\":\"" + utc + "\",\"exit\":" + Num(code) + ",\"bytes\":" + Num(bytes) + ",\"sha256\":\"" + sha256Prefix + "\",\"duration\":"
                + Num(durationSec) + ",\"tables\":" + Num(tables) + ",\"mode\":\"plain\",\"encryption\":\"none\",\"operatorDecision\":\"" + PlainDecision + "\""
                + cloud + ",\"target\":\"" + target + "\",\"serverVersion\":\"" + version + "\"}";
            return Secret.IsMatch(json) ? null : json;
        }

        /// <summary>Get-SnapshotTrimmedFullPath: the full path without trailing separators (a root stays a root).</summary>
        public static string TrimmedFullPath(string path)
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full);
            var trimmed = full.TrimEnd(Path.DirectorySeparatorChar);
            return string.IsNullOrWhiteSpace(trimmed) ? root : trimmed;
        }

        internal static string Num(long value) => value.ToString(CultureInfo.InvariantCulture);

        // [int] of a double: PowerShell rounds half to even.
        internal static int Seconds(TimeSpan elapsed) => (int)Math.Round(elapsed.TotalSeconds, MidpointRounding.ToEven);

        internal static bool IsSecret(string text) => Secret.IsMatch(text ?? "");

        internal static bool Matches(string value, string pattern, bool caseSensitive = false) =>
            Regex.IsMatch(value ?? "", pattern, caseSensitive ? Cs : Ci);
    }
}
