// The stateful half of the CARD-1405 snapshot (unit 53; see PostgresSnapshot.cs): one run's docker context, remote-docker
// allowance, run directory and its lock, and the operations that touch the disk or start docker and age. Each method is the
// script function it names, in its order; the driver's dot-sourced cases call them directly. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>One snapshot run's state and the script's disk and docker operations.</summary>
    public sealed class PostgresSnapshotSession
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private readonly NightlyHost _host;
        private readonly PostgresSnapshotSeams _seams;
        private FileStream _runLock;

        /// <summary>A session on the host and seams (null seams: production) with the stale-run threshold.</summary>
        public PostgresSnapshotSession(NightlyHost host, PostgresSnapshotSeams seams = null, int staleRunMinutes = 360)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _seams = seams ?? new PostgresSnapshotSeams();
            StaleRunMinutes = staleRunMinutes;
            PlainRetention = UpdatePlainRetentionCore;
        }

        /// <summary>$script:SnapshotDockerContext: when set, every docker call names it (--target server2).</summary>
        public string DockerContext { get; set; } = "";

        /// <summary>$script:SnapshotRemoteDockerAllowed: a non-local daemon was allowed by --allow-remote-docker.</summary>
        public bool RemoteDockerAllowed { get; private set; }

        /// <summary>$script:SnapshotRunStaging: the run directory Initialize-SnapshotStaging opened ('' when none).</summary>
        public string RunStaging { get; private set; } = "";

        /// <summary>-StaleRunMinutes.</summary>
        public int StaleRunMinutes { get; }

        /// <summary>$script:SnapshotExit: the code of the last result line (1 until one is written).</summary>
        public int Exit { get; private set; } = 1;

        /// <summary>
        /// Update-SnapshotPlainRetention as the plain publish and the OneDrive copy call it. The driver's prune-order case replaced
        /// the function to observe when it ran; a test wraps this the same way. Defaults to the real retention.
        /// </summary>
        public Func<string, int, string, SnapshotRetention> PlainRetention { get; set; }

        private IPostgresSnapshotPlatform Platform => _seams.Platform;

        // ---- output ----

        /// <summary>Write-Output: one line on the host's stdout.</summary>
        public void Line(string text) => _host.Out.WriteLine(text);

        /// <summary>Write-SnapshotResult: the result line, and the exit code it names.</summary>
        public void Result(bool planOnly, string reason, string detail)
        {
            Exit = PostgresSnapshot.ReasonCode(reason);
            Line(PostgresSnapshot.ResultLine(planOnly, reason, detail));
        }

        // ---- natives ----

        /// <summary>Get-Command -CommandType Application: the tool's path, or null.</summary>
        public string Resolve(string name) => _seams.Resolve?.Invoke(name);

        /// <summary>
        /// Invoke-SnapshotNative: runs the command with its arguments (and, for docker with a context set, --context &lt;name&gt; first),
        /// stdout and stderr merged in arrival order, one string per line.
        /// </summary>
        public SnapshotNative Native(string command, IReadOnlyList<string> arguments)
        {
            var argv = new List<string>();
            if (!string.IsNullOrWhiteSpace(DockerContext) && IsDocker(command)) argv.AddRange(new[] { "--context", DockerContext });
            argv.AddRange(arguments ?? Array.Empty<string>());
            var result = _host.Launch(new NightlyLaunch(command, argv, "", CaptureOutput: true));
            return new SnapshotNative(result.ExitCode, Lines(result.LogText));
        }

        /// <summary>Test-SnapshotDockerCommand: the file name without extension is docker.</summary>
        public static bool IsDocker(string command) =>
            !string.IsNullOrWhiteSpace(command) && Path.GetFileNameWithoutExtension(command).Equals("docker", StringComparison.OrdinalIgnoreCase);

        private static IReadOnlyList<string> Lines(string text)
        {
            if (string.IsNullOrEmpty(text)) return Array.Empty<string>();
            var body = text.EndsWith("\n", StringComparison.Ordinal) ? text.Substring(0, text.Length - 1) : text;
            return body.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        }

        private static IReadOnlyList<string> NonBlank(IEnumerable<string> lines) => lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();

        // ---- the docker daemon ----

        /// <summary>
        /// Test-SnapshotDockerDaemon: '' when docker talks to an allowed daemon, else docker-host or docker-context. With a context
        /// (server2) any DOCKER_HOST refuses and the context's endpoint must be ssh://. Otherwise a non-local DOCKER_HOST or a
        /// non-local current context refuses unless allowRemote. The endpoint is never printed.
        /// </summary>
        public string DockerDaemon(string docker, bool allowRemote)
        {
            RemoteDockerAllowed = false;
            var hostVariable = _host.Variables("DOCKER_HOST");
            if (!string.IsNullOrWhiteSpace(DockerContext))
            {
                if (!string.IsNullOrWhiteSpace(hostVariable)) return "docker-host";
                var named = Native(docker, new[] { "context", "inspect", DockerContext, "--format", "{{.Endpoints.docker.Host}}" });
                var endpoints = NonBlank(named.Lines);
                if (named.Exit != 0 || endpoints.Count != 1 || !PostgresSnapshot.IsSshEndpoint(endpoints[0])) return "docker-context";
                return "";
            }
            if (!string.IsNullOrWhiteSpace(hostVariable) && !PostgresSnapshot.IsLocalEndpoint(hostVariable))
            {
                if (!allowRemote) return "docker-host";
                RemoteDockerAllowed = true;
                return "";
            }
            var shown = Native(docker, new[] { "context", "inspect", "--format", "{{.Endpoints.docker.Host}}" });
            var lines = NonBlank(shown.Lines);
            if (shown.Exit != 0 || lines.Count != 1 || !PostgresSnapshot.IsLocalEndpoint(lines[0]))
            {
                if (!allowRemote) return "docker-context";
                RemoteDockerAllowed = true;
            }
            return "";
        }

        /// <summary>Write-SnapshotDaemonWarning.</summary>
        public void DaemonWarning()
        {
            if (RemoteDockerAllowed) Line("warning: remote docker endpoint allowed by -AllowRemoteDocker");
        }

        // ---- the file system ----

        /// <summary>Test-Path -LiteralPath.</summary>
        public static bool Exists(string path) => !string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path));

        private static FileSystemInfo Item(string path)
        {
            if (Directory.Exists(path)) return new DirectoryInfo(path);
            if (File.Exists(path)) return new FileInfo(path);
            return null;
        }

        private static bool IsReparse(FileSystemInfo item) => item != null && (item.Attributes & FileAttributes.ReparsePoint) != 0;

        // Get-Item's LinkType as the plain path reads it: a symbolic link or a junction (both have a link target in .NET) is a
        // link; a cloud-filter placeholder or any other reparse point has none. Both kinds are refused alike, so one name serves.
        private static string LinkType(FileSystemInfo item)
        {
            if (item == null) return "";
            try { return item.LinkTarget == null ? "" : "SymbolicLink"; } catch (Exception) { return ""; }
        }

        private static IEnumerable<FileInfo> VisibleFiles(string directory)
        {
            // Get-ChildItem -File without -Force: hidden files are not listed.
            if (!Directory.Exists(directory)) return Array.Empty<FileInfo>();
            try
            {
                return new DirectoryInfo(directory).EnumerateFiles().Where(file => (file.Attributes & FileAttributes.Hidden) == 0).ToArray();
            }
            catch (Exception) { return Array.Empty<FileInfo>(); }
        }

        private static void RemoveQuietly(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
                else if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception) { /* Remove-Item -ErrorAction SilentlyContinue */ }
        }

        /// <summary>Test-SnapshotContainsLink: the path or any existing parent is a reparse point.</summary>
        public static bool ContainsLink(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var cursor = Path.GetFullPath(path);
            while (!string.IsNullOrWhiteSpace(cursor))
            {
                if (Exists(cursor) && IsReparse(Item(cursor))) return true;
                var parent = Path.GetDirectoryName(cursor);
                if (string.IsNullOrWhiteSpace(parent) || parent == cursor) break;
                cursor = parent;
            }
            return false;
        }

        /// <summary>Test-SnapshotOwnedDirectory: Windows strict ACL check; elsewhere the caller's and no group or other bit.</summary>
        public bool OwnedDirectory(string path)
        {
            if (Platform.IsWindows) return PostgresSnapshot.WindowsOwnedDirectory(path, Platform.CallerSid(), Platform.ReadSecurity, Platform.Elevated());
            var me = Platform.Uid();
            var info = Platform.Stat(path);
            if (me < 0 || info == null) return false;
            return info.Uid == me && (info.Mode & 63) == 0;
        }

        /// <summary>
        /// New-SnapshotPrivateDirectory: creates path owner-only in one step (never adopting an existing path) and re-reads it
        /// before trusting it.
        /// </summary>
        public bool NewPrivateDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || Exists(path)) return false;
            if (!Platform.CreatePrivateDirectory(path)) return false;
            if (ContainsLink(path)) return false;
            if (!Directory.Exists(path)) return false;
            return OwnedDirectory(path);
        }

        /// <summary>Set-SnapshotPlainDirectoryAcl: tightens an existing directory to the caller and re-checks it.</summary>
        public bool RestrictDirectory(string path)
        {
            if (!Platform.RestrictDirectory(path)) return false;
            return OwnedDirectory(path);
        }

        /// <summary>Test-SnapshotStagingRoot: '' or the refusal for the staging root (an absent root is created owner-only first).</summary>
        public string StagingRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "staging-owner";
            if (ContainsLink(path)) return "staging-link";
            if (!Exists(path))
            {
                var above = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrWhiteSpace(above) && !Exists(above))
                {
                    try { Directory.CreateDirectory(above); } catch (Exception) { return "staging-owner"; }
                }
                if (!NewPrivateDirectory(path)) return Exists(path) ? "staging-mode" : "staging-owner";
            }
            if (ContainsLink(path)) return "staging-link";
            if (!Directory.Exists(path)) return "staging-owner";
            if (Platform.IsWindows) return PostgresSnapshot.WindowsStagingRoot(path, Platform.CallerSid(), Platform.ReadSecurity, Platform.Elevated());
            var me = Platform.Uid();
            if (me < 0) return "staging-owner";
            var root = Platform.Stat(path);
            if (root == null) return "staging-owner";
            if (root.Uid != me) return "staging-owner";
            if ((root.Mode & 63) != 0) return "staging-mode";
            var cursor = Path.GetDirectoryName(Path.GetFullPath(path));
            while (!string.IsNullOrWhiteSpace(cursor))
            {
                var info = Platform.Stat(cursor);
                if (info == null) return "staging-parent";
                if (info.Uid != me && info.Uid != 0) return "staging-owner";
                // 18 is 0022 (group or other write); 512 is 01000 (sticky).
                if ((info.Mode & 18) != 0 && (info.Mode & 512) == 0) return "staging-parent";
                var parent = Path.GetDirectoryName(cursor);
                if (string.IsNullOrWhiteSpace(parent) || parent == cursor) break;
                cursor = parent;
            }
            return "";
        }

        /// <summary>Test-SnapshotRunLockFree: no process holds run.lock.</summary>
        public static bool RunLockFree(string lockPath)
        {
            if (!File.Exists(lockPath)) return true;
            try
            {
                using (File.Open(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                return true;
            }
            catch (Exception) { return false; }
        }

        private static readonly Regex OwnedFile = new Regex(@"^antiphon-[0-9]{8}T[0-9]{6}Z\.dump(\.age)?$|^antiphon-drill-plain\.dump$|^run\.lock$", PostgresSnapshot.Ci);
        private static readonly Regex RunName = new Regex("^run-[0-9a-f]{32}$", PostgresSnapshot.Ci);

        /// <summary>
        /// Clear-SnapshotOwnedFiles: sweeps run directories that are the caller's, older than the stale threshold and whose lock no
        /// process holds: their dumps and lock go, then the directory if it is empty. Anything else stays.
        /// </summary>
        public void ClearOwnedFiles(string root, int staleMinutes)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;
            if (IsReparse(new DirectoryInfo(root))) return;
            var cutoff = _host.UtcNow.AddMinutes(-1 * Math.Max(1, staleMinutes));
            DirectoryInfo[] directories;
            try { directories = new DirectoryInfo(root).GetDirectories(); } catch (Exception) { return; }
            foreach (var directory in directories)
            {
                if (!RunName.IsMatch(directory.Name)) continue;
                if (IsReparse(directory)) continue;
                if (directory.LastWriteTimeUtc > cutoff) continue;
                if (!OwnedDirectory(directory.FullName)) continue;
                if (!RunLockFree(Path.Combine(directory.FullName, "run.lock"))) continue;
                FileInfo[] files;
                try { files = directory.GetFiles(); } catch (Exception) { files = Array.Empty<FileInfo>(); }
                foreach (var file in files)
                {
                    if (IsReparse(file)) continue;
                    if (OwnedFile.IsMatch(file.Name)) RemoveQuietly(file.FullName);
                }
                bool empty;
                try { empty = !directory.EnumerateFileSystemInfos().Any(); } catch (Exception) { empty = false; }
                if (empty)
                {
                    try { directory.Delete(false); } catch (Exception) { /* Remove-Item -ErrorAction SilentlyContinue */ }
                }
            }
        }

        /// <summary>Get-SnapshotDefaultStaging: &lt;LocalApplicationData&gt;/antiphon/postgres-snapshot-staging (the temp path when unknown).</summary>
        public string DefaultStaging()
        {
            var local = _seams.LocalApplicationData?.Invoke();
            if (string.IsNullOrWhiteSpace(local)) local = Path.GetTempPath();
            return Path.Combine(Path.Combine(local, "antiphon"), "postgres-snapshot-staging");
        }

        /// <summary>
        /// Initialize-SnapshotStaging: the root (created if absent), the stale sweep, then this run's owner-only run-&lt;guid&gt;
        /// directory with run.lock held open exclusively until RemoveStaging.
        /// </summary>
        public bool InitializeStaging(string path)
        {
            RunStaging = "";
            ReleaseLock();
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (ContainsLink(path)) return false;
            if (!Exists(path)) Directory.CreateDirectory(path);
            if (ContainsLink(path)) return false;
            ClearOwnedFiles(path, StaleRunMinutes);
            var run = Path.Combine(path, "run-" + Guid.NewGuid().ToString("N"));
            if (!NewPrivateDirectory(run))
            {
                RemoveQuietly(run);
                return false;
            }
            try
            {
                _runLock = File.Open(Path.Combine(run, "run.lock"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            }
            catch (Exception)
            {
                _runLock = null;
                RemoveQuietly(run);
                return false;
            }
            RunStaging = run;
            return true;
        }

        /// <summary>Remove-SnapshotStaging: removes this run's directory (only a caller-owned run-&lt;guid&gt; that is not a link).</summary>
        public bool RemoveStaging(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Exists(path)) return true;
            if (ContainsLink(path)) return false;
            var item = Item(path);
            if (IsReparse(item)) return false;
            if (!RunName.IsMatch(item.Name)) return false;
            if (!OwnedDirectory(path)) return false;
            ReleaseLock();
            RemoveQuietly(path);
            return !Exists(path);
        }

        private void ReleaseLock()
        {
            if (_runLock == null) return;
            try { _runLock.Dispose(); } catch (Exception) { }
            _runLock = null;
        }

        /// <summary>Remove-SnapshotPartial.</summary>
        public static void RemovePartial(params string[] paths)
        {
            foreach (var path in paths ?? Array.Empty<string>())
                if (!string.IsNullOrWhiteSpace(path) && Exists(path)) RemoveQuietly(path);
        }

        /// <summary>Test-SnapshotOutsideFile: missing, the link reason, the inside reason, or ok.</summary>
        public static string OutsideFile(string path, string syncRoot, string destination, string insideReason, string linkReason)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return "missing";
            if (IsReparse(new FileInfo(path))) return linkReason;
            if (PostgresSnapshot.PathInside(syncRoot, path) || PostgresSnapshot.PathInside(destination, path)) return insideReason;
            return "ok";
        }

        // ---- retention ----

        /// <summary>Update-SnapshotRetention: the verified age pass over &lt;dir&gt;/antiphon-*.dump.age.</summary>
        public SnapshotRetention UpdateRetention(string destDir, int keep, string newName)
        {
            var pattern = new Regex(PostgresSnapshot.DumpPattern, PostgresSnapshot.Ci);
            var names = VisibleFiles(destDir).Select(file => file.Name).Where(name => pattern.IsMatch(name)).ToArray();
            var prune = PostgresSnapshot.PruneSet(names, keep, newName, true);
            if (!prune.Ok) return new SnapshotRetention(false, prune.Reason, 0);
            foreach (var name in prune.Drop)
            {
                if (!pattern.IsMatch(name) || name.Equals(newName, StringComparison.OrdinalIgnoreCase)) continue;
                var victim = Path.Combine(destDir, name);
                if (!PostgresSnapshot.PathInside(destDir, victim)) return new SnapshotRetention(false, "outside-destination", 0);
                Delete(victim);
            }
            var left = VisibleFiles(destDir).Where(file => pattern.IsMatch(file.Name)).ToArray();
            var still = left.Count(file => file.Name.Equals(newName, StringComparison.OrdinalIgnoreCase));
            if (left.Length < 1 || left.Length > keep || still != 1) return new SnapshotRetention(false, "prune-failed", left.Length);
            return new SnapshotRetention(true, "prune", left.Length);
        }

        /// <summary>Update-SnapshotPlainRetention through PlainRetention (the real one unless a test observes it).</summary>
        public SnapshotRetention UpdatePlainRetention(string destDir, int keep, string newName) => PlainRetention(destDir, keep, newName);

        /// <summary>The real Update-SnapshotPlainRetention: no victim may escape the folder, checked before and at each delete.</summary>
        public SnapshotRetention UpdatePlainRetentionCore(string destDir, int keep, string newName)
        {
            var pattern = new Regex(PostgresSnapshot.PlainDumpPattern, PostgresSnapshot.Ci);
            var names = VisibleFiles(destDir).Select(file => file.Name).Where(name => pattern.IsMatch(name)).ToArray();
            var prune = PostgresSnapshot.PruneSet(names, keep, newName, true, PostgresSnapshot.PlainDumpPattern);
            if (!prune.Ok) return new SnapshotRetention(false, prune.Reason, 0);
            var victims = new List<string>();
            foreach (var name in prune.Drop)
            {
                if (!pattern.IsMatch(name) || name.Equals(newName, StringComparison.OrdinalIgnoreCase)) continue;
                var victim = Path.Combine(destDir, name);
                if (PlainFileEscapes(destDir, victim) || !PostgresSnapshot.PathInside(destDir, victim)) return new SnapshotRetention(false, "outside-destination", 0);
                victims.Add(victim);
            }
            foreach (var victim in victims)
            {
                if (PlainFileEscapes(destDir, victim)) return new SnapshotRetention(false, "outside-destination", 0);
                Delete(victim);
            }
            var left = VisibleFiles(destDir).Where(file => pattern.IsMatch(file.Name)).ToArray();
            var still = left.Count(file => file.Name.Equals(newName, StringComparison.OrdinalIgnoreCase));
            if (left.Length < 1 || left.Length > keep || still != 1) return new SnapshotRetention(false, "prune-failed", left.Length);
            return new SnapshotRetention(true, "prune", left.Length);
        }

        // Remove-Item -Force without -ErrorAction: a failure is reported on stderr and the run goes on (the count check decides).
        private void Delete(string path)
        {
            try { File.Delete(path); }
            catch (Exception e) { _host.Error.WriteLine("postgres-snapshot: " + e.Message.Replace("\r", " ").Replace("\n", " ")); }
        }

        // ---- links and real paths (the plain path and OneDrive) ----

        /// <summary>Test-SnapshotPlainDirectoryLink: a symlink or junction at path.</summary>
        public static bool PlainDirectoryLink(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var item = LinkItem(path);
            if (item == null) return false;
            return PostgresSnapshot.ForbiddenLink(LinkType(item), IsReparse(item));
        }

        // Get-Item -Force: the item itself, a dangling link included.
        private static FileSystemInfo LinkItem(string path)
        {
            try
            {
                var directory = new DirectoryInfo(path);
                if (directory.Exists) return directory;
                var file = new FileInfo(path);
                if (file.Exists || file.LinkTarget != null) return file;
                return null;
            }
            catch (Exception) { return null; }
        }

        /// <summary>Resolve-SnapshotRealPath: each component's symlink or junction followed (8 hops at most); '' when unresolvable.</summary>
        public static string RealPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full);
            if (string.IsNullOrWhiteSpace(root)) return "";
            var parts = full.Substring(root.Length).Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            var cursor = root;
            foreach (var part in parts)
            {
                cursor = Path.Combine(cursor, part);
                var hops = 0;
                while (hops < 8)
                {
                    var item = LinkItem(cursor);
                    if (item == null) break;
                    if (!PlainDirectoryLink(cursor)) break;
                    var target = item.LinkTarget;
                    if (string.IsNullOrWhiteSpace(target)) return "";
                    if (!Path.IsPathRooted(target))
                    {
                        var parent = Path.GetDirectoryName(item.FullName);
                        if (string.IsNullOrWhiteSpace(parent)) return "";
                        target = Path.GetFullPath(Path.Combine(parent, target));
                    }
                    else
                    {
                        target = Path.GetFullPath(target);
                    }
                    if (target.Equals(cursor, StringComparison.OrdinalIgnoreCase)) return "";
                    cursor = target;
                    hops++;
                }
                if (hops >= 8) return "";
            }
            return cursor;
        }

        /// <summary>Test-SnapshotPlainChainLinks: ok, link (a link on the chain from the root to the folder) or outside.</summary>
        public static string PlainChainLinks(string rootPath, string folderPath)
        {
            if (PlainDirectoryLink(rootPath)) return "link";
            var rootFull = PostgresSnapshot.TrimmedFullPath(rootPath);
            var folderFull = PostgresSnapshot.TrimmedFullPath(folderPath);
            if (!PostgresSnapshot.PathInside(rootFull, folderFull)) return "outside";
            var sep = Path.DirectorySeparatorChar;
            var relative = folderFull.Substring(rootFull.Length).TrimStart(sep);
            var cursor = rootFull;
            if (!string.IsNullOrWhiteSpace(relative))
            {
                foreach (var part in relative.Split(new[] { sep }, StringSplitOptions.RemoveEmptyEntries))
                {
                    cursor = Path.Combine(cursor, part);
                    if (PlainDirectoryLink(cursor)) return "link";
                }
            }
            var realRoot = RealPath(rootFull);
            var realFolder = RealPath(folderFull);
            if (string.IsNullOrWhiteSpace(realRoot) || string.IsNullOrWhiteSpace(realFolder)) return "outside";
            if (!PostgresSnapshot.PathInside(realRoot, realFolder)) return "outside";
            var realTrim = PostgresSnapshot.TrimmedFullPath(realFolder);
            var folderTrim = PostgresSnapshot.TrimmedFullPath(folderFull);
            if (!realTrim.Equals(folderTrim, StringComparison.OrdinalIgnoreCase)) return "link";
            return "ok";
        }

        /// <summary>Test-SnapshotPlainFileEscapes: the file is missing, a link, or resolves outside the folder.</summary>
        public static bool PlainFileEscapes(string destDir, string filePath)
        {
            var item = LinkItem(filePath);
            if (item == null) return true;
            if (PostgresSnapshot.ForbiddenLink(LinkType(item), IsReparse(item))) return true;
            if (!PostgresSnapshot.PathInside(destDir, filePath)) return true;
            var realDir = RealPath(destDir);
            var realFile = RealPath(filePath);
            if (string.IsNullOrWhiteSpace(realDir) || string.IsNullOrWhiteSpace(realFile)) return true;
            return !PostgresSnapshot.PathInside(realDir, realFile);
        }

        // ---- hashes and the status files ----

        private static string Sha256(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        /// <summary>Get-SnapshotSha256Prefix: the first 12 hex digits of the file's SHA-256.</summary>
        public static string Sha256Prefix(string path)
        {
            var hash = Sha256(path);
            return hash.Length < 12 ? "" : hash.Substring(0, 12);
        }

        /// <summary>Write-SnapshotStatus: the age path's {"utc","exit","bytes"} record.</summary>
        public static void WriteStatus(string dir, string utc, int code, long bytes)
        {
            var json = "{\"utc\":\"" + utc + "\",\"exit\":" + PostgresSnapshot.Num(code) + ",\"bytes\":" + PostgresSnapshot.Num(bytes) + "}";
            File.WriteAllText(Path.Combine(dir, "snapshot-status.json"), json, Utf8NoBom);
        }

        /// <summary>Write-SnapshotPlainStatus: snapshot-status.json written through .partial and renamed; false when refused.</summary>
        public static bool WritePlainStatus(string dir, string utc, int code, long bytes, string sha256Prefix, int durationSec, int tables, string oneDrive = "",
            long oneDriveBytes = -1, string oneDriveFolder = "", string target = "desktop", string serverVersion = "")
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return false;
            var json = PostgresSnapshot.PlainStatusJson(utc, code, bytes, sha256Prefix, durationSec, tables, oneDrive, oneDriveBytes, oneDriveFolder, target, serverVersion);
            if (json == null) return false;
            var partial = Path.Combine(dir, "snapshot-status.json.partial");
            var final = Path.Combine(dir, "snapshot-status.json");
            File.WriteAllText(partial, json, Utf8NoBom);
            if (Exists(final)) File.Delete(final);
            File.Move(partial, final);
            return !Exists(partial) && Exists(final);
        }

        // ---- the OneDrive copy ----

        /// <summary>Confirm-SnapshotPlainOneDriveFile: the partial's SHA-256 equals the source's, then renamed and re-hashed.</summary>
        public static bool ConfirmOneDriveFile(string sourceFile, string partialFile, string finalFile)
        {
            if (string.IsNullOrWhiteSpace(sourceFile) || !File.Exists(sourceFile)) return false;
            if (string.IsNullOrWhiteSpace(partialFile) || !File.Exists(partialFile)) return false;
            if (string.IsNullOrWhiteSpace(finalFile)) return false;
            var sourceHash = Sha256(sourceFile);
            var partialHash = Sha256(partialFile);
            if (sourceHash != partialHash)
            {
                RemoveQuietly(partialFile);
                return false;
            }
            if (Exists(finalFile))
            {
                RemoveQuietly(partialFile);
                return false;
            }
            File.Move(partialFile, finalFile);
            if (!File.Exists(finalFile)) return false;
            if (Sha256(finalFile) != sourceHash)
            {
                RemoveQuietly(finalFile);
                return false;
            }
            return !Exists(partialFile);
        }

        /// <summary>
        /// Invoke-SnapshotPlainOneDriveCopy: the verified plain dump into &lt;sync&gt;\Backups\antiphon-postgres as .partial, renamed
        /// when its SHA-256 matches, then OneDrive's retention. A missing root or Backups is missing; a link anywhere is failed link.
        /// </summary>
        public SnapshotOneDriveResult OneDriveCopy(string localFile, string syncRoot, int keep = 7)
        {
            var folder = PostgresSnapshot.OneDriveFolder(syncRoot);
            if (string.IsNullOrWhiteSpace(syncRoot) || !Exists(syncRoot)) return PostgresSnapshot.OneDriveResult("missing", 0, folder);
            if (PlainDirectoryLink(syncRoot)) return PostgresSnapshot.OneDriveResult("failed", 0, folder, "link");
            if (!Directory.Exists(syncRoot)) return PostgresSnapshot.OneDriveResult("missing", 0, folder);
            var backups = Path.Combine(syncRoot, "Backups");
            if (PlainDirectoryLink(backups)) return PostgresSnapshot.OneDriveResult("failed", 0, folder, "link");
            if (!Directory.Exists(backups)) return PostgresSnapshot.OneDriveResult("missing", 0, folder);
            if (string.IsNullOrWhiteSpace(localFile) || !File.Exists(localFile)) return PostgresSnapshot.OneDriveResult("failed", 0, folder);
            var name = Path.GetFileName(localFile);
            if (!PostgresSnapshot.Matches(name, PostgresSnapshot.PlainDumpPattern) || keep < 1) return PostgresSnapshot.OneDriveResult("failed", 0, folder);
            var placement = PlainChainLinks(syncRoot, folder);
            if (placement != "ok") return PostgresSnapshot.OneDriveResult("failed", 0, folder, placement);
            if (Exists(folder))
            {
                // The script's condition parsed as (Test-SnapshotPlainDirectoryLink $folder) alone: its -or -not (Test-Path ...)
                // went to the function as extra arguments, so a file at the folder's name is not refused here.
                if (PlainDirectoryLink(folder)) return PostgresSnapshot.OneDriveResult("failed", 0, folder, "link");
                FileSystemInfo[] entries;
                try { entries = Directory.Exists(folder) ? new DirectoryInfo(folder).GetFileSystemInfos() : new FileSystemInfo[] { new FileInfo(folder) }; }
                catch (Exception) { entries = Array.Empty<FileSystemInfo>(); }
                foreach (var entry in entries)
                {
                    if (!PostgresSnapshot.Matches(entry.Name, PostgresSnapshot.PlainDumpPattern)) continue;
                    if (PlainFileEscapes(folder, entry.FullName)) return PostgresSnapshot.OneDriveResult("failed", 0, folder, "link");
                }
            }
            else
            {
                Directory.CreateDirectory(folder);
                if (PlainDirectoryLink(folder)) return PostgresSnapshot.OneDriveResult("failed", 0, folder, "link");
            }
            placement = PlainChainLinks(syncRoot, folder);
            if (placement != "ok") return PostgresSnapshot.OneDriveResult("failed", 0, folder, placement);
            if (!PostgresSnapshot.PathInside(backups, folder)) return PostgresSnapshot.OneDriveResult("failed", 0, folder);
            var final = Path.Combine(folder, name);
            if (!PostgresSnapshot.PathInside(folder, final)) return PostgresSnapshot.OneDriveResult("failed", 0, folder);
            if (File.Exists(final))
            {
                if (Sha256(final) != Sha256(localFile)) return PostgresSnapshot.OneDriveResult("failed", 0, folder);
                var sameBytes = new FileInfo(final).Length;
                var samePrune = UpdatePlainRetention(folder, keep, name);
                if (!samePrune.Ok) return PostgresSnapshot.OneDriveResult("failed", sameBytes, folder);
                return PostgresSnapshot.OneDriveResult("ok", sameBytes, folder);
            }
            var partial = final + ".partial";
            if (!PostgresSnapshot.PathInside(folder, partial)) return PostgresSnapshot.OneDriveResult("failed", 0, folder);
            if (Exists(partial)) File.Delete(partial);
            try
            {
                File.Copy(localFile, partial, true);
            }
            catch (Exception)
            {
                RemovePartial(partial);
                return PostgresSnapshot.OneDriveResult("failed", 0, folder);
            }
            if (!ConfirmOneDriveFile(localFile, partial, final)) return PostgresSnapshot.OneDriveResult("failed", 0, folder);
            var bytes = new FileInfo(final).Length;
            var prune = UpdatePlainRetention(folder, keep, name);
            if (!prune.Ok) return PostgresSnapshot.OneDriveResult("failed", bytes, folder);
            return PostgresSnapshot.OneDriveResult("ok", bytes, folder);
        }

        // ---- the clock ----

        /// <summary>[datetime]::UtcNow.ToString('yyyyMMddTHHmmssZ') on the host's clock.</summary>
        public string Stamp() => _host.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>A stopwatch on the host's clock.</summary>
        public Func<TimeSpan> StartWatch()
        {
            var started = _host.Clock.GetTimestamp();
            return () => _host.Clock.GetElapsedTime(started);
        }

        /// <summary>Start-Sleep -Seconds 1.</summary>
        public void SleepOneSecond() => _host.Sleep(TimeSpan.FromSeconds(1));

        /// <summary>[Environment]::GetEnvironmentVariable through the host.</summary>
        public string Variable(string name) => string.IsNullOrWhiteSpace(name) ? null : _host.Variables(name);

        // ---- docker facts ----

        /// <summary>Get-SnapshotContainerFact: docker inspect --format '{{json .}}' &lt;name&gt;, read as ConvertFrom-Json read it.</summary>
        public SnapshotContainerFact ContainerFact(string docker, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return SnapshotContainerFact.Missing;
            var result = Native(docker, new[] { "inspect", "--format", "{{json .}}", name });
            if (result.Exit != 0) return SnapshotContainerFact.Missing;
            var bad = new SnapshotContainerFact { Found = true, Parse = false };
            var info = SnapshotJson.Parse(string.Join("\n", result.Lines).Trim());
            if (info == null) return bad;
            var label = "";
            var project = "";
            var config = SnapshotJson.Get(info, "Config");
            var labels = SnapshotJson.Get(config, "Labels");
            if (SnapshotJson.Truthy(config) && SnapshotJson.Truthy(labels))
            {
                if (SnapshotJson.TryGet(labels, PostgresSnapshot.DrillLabel, out var drill)) label = SnapshotJson.Str(drill);
                if (SnapshotJson.TryGet(labels, "com.docker.compose.project", out var compose)) project = SnapshotJson.Str(compose);
            }
            var running = false;
            var state = SnapshotJson.Get(info, "State");
            if (SnapshotJson.Truthy(state) && SnapshotJson.TryGet(state, "Running", out var runningValue)) running = SnapshotJson.EqualsTrue(runningValue);
            var imageId = SnapshotJson.TryGet(info, "Image", out var image) ? SnapshotJson.Str(image) : "";
            var binds = 0;
            var mounts = new List<string>();
            var volumeMounts = new List<string>();
            var mountList = SnapshotJson.Get(info, "Mounts");
            if (SnapshotJson.Truthy(mountList))
            {
                foreach (var mount in SnapshotJson.Items(mountList))
                {
                    if (!SnapshotJson.Truthy(mount)) continue;
                    var kind = SnapshotJson.TryGet(mount, "Type", out var type) ? SnapshotJson.Str(type) : "";
                    var mountName = SnapshotJson.Get(mount, "Name");
                    if (!string.IsNullOrWhiteSpace(SnapshotJson.Str(mountName)) && (kind == "" || kind.Equals("volume", StringComparison.OrdinalIgnoreCase)))
                    {
                        mounts.Add(SnapshotJson.Str(mountName));
                        var destination = SnapshotJson.TryGet(mount, "Destination", out var dest) ? SnapshotJson.Str(dest) : "";
                        volumeMounts.Add(SnapshotJson.Str(mountName) + ":" + destination);
                    }
                    else
                    {
                        binds++;
                    }
                }
            }
            var aliases = new List<string>();
            var settings = SnapshotJson.Get(info, "NetworkSettings");
            var networks = SnapshotJson.Get(settings, "Networks");
            if (SnapshotJson.Truthy(settings) && SnapshotJson.Truthy(networks) && networks.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var network in networks.Value.EnumerateObject())
                {
                    var aliasList = SnapshotJson.Get(network.Value, "Aliases");
                    if (!SnapshotJson.Truthy(network.Value) || !SnapshotJson.Truthy(aliasList)) continue;
                    foreach (var alias in SnapshotJson.Items(aliasList))
                    {
                        var text = SnapshotJson.Str(alias);
                        if (!string.IsNullOrWhiteSpace(text)) aliases.Add(text);
                    }
                }
            }
            var env = new List<string>();
            var envList = SnapshotJson.Get(config, "Env");
            if (SnapshotJson.Truthy(config) && SnapshotJson.Truthy(envList))
            {
                foreach (var entry in SnapshotJson.Items(envList))
                {
                    var text = SnapshotJson.Str(entry);
                    if (!string.IsNullOrWhiteSpace(text)) env.Add(text);
                }
            }
            var networkMode = "";
            var hostConfig = SnapshotJson.Get(info, "HostConfig");
            if (SnapshotJson.Truthy(hostConfig) && SnapshotJson.TryGet(hostConfig, "NetworkMode", out var mode)) networkMode = SnapshotJson.Str(mode);
            var idValue = SnapshotJson.Get(info, "Id");
            var nameValue = SnapshotJson.Get(info, "Name");
            return new SnapshotContainerFact
            {
                Found = true,
                Parse = true,
                Id = SnapshotJson.Truthy(idValue) ? SnapshotJson.Str(idValue) : "",
                Name = SnapshotJson.Truthy(nameValue) ? PostgresSnapshot.BareName(SnapshotJson.Str(nameValue)) : "",
                Label = label,
                Mounts = mounts,
                Aliases = aliases,
                Env = env,
                Binds = binds,
                NetworkMode = networkMode,
                Running = running,
                ComposeProject = project,
                VolumeMounts = volumeMounts,
                ImageId = imageId,
            };
        }

        /// <summary>Get-SnapshotServerVersion: docker exec &lt;c&gt; postgres --version, parsed; unknown on a failure.</summary>
        public string ServerVersion(string docker, string sourceContainer)
        {
            var shown = Native(docker, new[] { "exec", sourceContainer, "postgres", "--version" });
            if (shown.Exit != 0) return "unknown";
            return PostgresSnapshot.ServerVersion(string.Join("\n", shown.Lines));
        }

        /// <summary>Get-SnapshotProductionIds: the source's id and every other production container's.</summary>
        public IReadOnlyList<string> ProductionIds(string docker, string sourceContainer, SnapshotContainerFact sourceFact)
        {
            var ids = new List<string>();
            if (sourceFact.Found && sourceFact.Parse && !string.IsNullOrWhiteSpace(sourceFact.Id)) ids.Add(sourceFact.Id);
            foreach (var name in PostgresSnapshot.ProductionContainers)
            {
                if (PostgresSnapshot.NameMatch(name, new[] { sourceContainer })) continue;
                var other = ContainerFact(docker, name);
                if (other.Found && other.Parse && !string.IsNullOrWhiteSpace(other.Id)) ids.Add(other.Id);
            }
            return ids;
        }

        /// <summary>Get-SnapshotProtectedMounts: the source's mounts and every other production container's.</summary>
        public IReadOnlyList<string> ProtectedMounts(string docker, SnapshotContainerFact sourceFact, string sourceContainer)
        {
            var mounts = new List<string>();
            if (sourceFact != null) mounts.AddRange(sourceFact.Mounts.Where(name => !string.IsNullOrWhiteSpace(name)));
            foreach (var name in PostgresSnapshot.ProductionContainers)
            {
                if (PostgresSnapshot.NameMatch(name, new[] { sourceContainer })) continue;
                var other = ContainerFact(docker, name);
                if (other.Found && other.Parse) mounts.AddRange(other.Mounts.Where(mount => !string.IsNullOrWhiteSpace(mount)));
            }
            return mounts;
        }

        /// <summary>Get-SnapshotVolumeFact.</summary>
        public SnapshotVolumeFact VolumeFact(string docker, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return new SnapshotVolumeFact(false, false, "", "");
            var result = Native(docker, new[] { "volume", "inspect", "--format", "{{json .}}", name });
            if (result.Exit != 0) return new SnapshotVolumeFact(false, false, "", "");
            var info = SnapshotJson.Parse(string.Join("\n", result.Lines).Trim());
            if (info == null) return new SnapshotVolumeFact(true, false, "", "");
            var label = "";
            var labels = SnapshotJson.Get(info, "Labels");
            if (SnapshotJson.Truthy(labels) && SnapshotJson.TryGet(labels, PostgresSnapshot.DrillLabel, out var value)) label = SnapshotJson.Str(value);
            var volumeName = name;
            var nameValue = SnapshotJson.Get(info, "Name");
            if (SnapshotJson.Truthy(nameValue)) volumeName = SnapshotJson.Str(nameValue);
            return new SnapshotVolumeFact(true, true, volumeName, label);
        }

        private static readonly Regex ContainerId = new Regex("^[0-9a-fA-F]{12,64}$", PostgresSnapshot.Ci);

        /// <summary>Get-SnapshotVolumeUsers: the containers (any state) that mount the volume; Ok false when docker ps failed.</summary>
        public (bool Ok, IReadOnlyList<string> Ids) VolumeUsers(string docker, string volume)
        {
            var result = Native(docker, new[] { "ps", "-a", "--no-trunc", "--filter", "volume=" + volume, "--format", "{{.ID}}" });
            if (result.Exit != 0) return (false, Array.Empty<string>());
            var ids = result.Lines.Select(line => (line ?? "").Trim()).Where(line => ContainerId.IsMatch(line)).ToArray();
            return (true, ids);
        }

        /// <summary>Test-SnapshotVolumeGate: ok, absent (when allowed) or the reason the scratch volume may not be used.</summary>
        public string VolumeGate(string docker, string volume, string sourceVolume, IReadOnlyList<string> sourceMounts, string allowedId, bool allowAbsent)
        {
            var denied = PostgresSnapshot.ProductionVolumes.Concat(new[] { sourceVolume }).ToArray();
            if (PostgresSnapshot.NameMatch(volume, denied)) return "production-volume";
            if (PostgresSnapshot.NameMatch(volume, sourceMounts)) return "source-mount";
            var users = VolumeUsers(docker, volume);
            if (!users.Ok) return "inspect";
            foreach (var user in users.Ids)
            {
                if (string.IsNullOrWhiteSpace(user)) continue;
                if (!string.IsNullOrWhiteSpace(allowedId) && PostgresSnapshot.SameId(user, allowedId)) continue;
                return "foreign-mount";
            }
            var fact = VolumeFact(docker, volume);
            if (!fact.Found) return allowAbsent ? "absent" : "volume-mismatch";
            if (!fact.Parse) return "inspect";
            if (fact.Label != "1") return "unlabelled-volume";
            return "ok";
        }
    }

    /// <summary>ConvertFrom-Json as the snapshot read docker's answers: property names ignore case, an array's first item is the object.</summary>
    internal static class SnapshotJson
    {
        /// <summary>The text as one JSON value (an array: its first item); null when it does not parse or is blank.</summary>
        public static JsonElement? Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            try
            {
                using var document = JsonDocument.Parse(text);
                var root = document.RootElement.Clone();
                if (root.ValueKind == JsonValueKind.Array)
                {
                    var first = root.EnumerateArray().FirstOrDefault();
                    return first.ValueKind == JsonValueKind.Undefined ? (JsonElement?)null : first;
                }
                return root;
            }
            catch (JsonException) { return null; }
        }

        public static bool TryGet(JsonElement? node, string name, out JsonElement value)
        {
            value = default;
            if (node == null || node.Value.ValueKind != JsonValueKind.Object) return false;
            foreach (var property in node.Value.EnumerateObject())
            {
                if (!property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                value = property.Value;
                return true;
            }
            return false;
        }

        public static JsonElement? Get(JsonElement? node, string name) => TryGet(node, name, out var value) ? value : (JsonElement?)null;

        // PowerShell truth: null, false, 0, '' and an empty array are false; an object is true.
        public static bool Truthy(JsonElement? node)
        {
            if (node == null) return false;
            var value = node.Value;
            switch (value.ValueKind)
            {
                case JsonValueKind.Null: case JsonValueKind.Undefined: case JsonValueKind.False: return false;
                case JsonValueKind.True: case JsonValueKind.Object: return true;
                case JsonValueKind.String: return value.GetString().Length > 0;
                case JsonValueKind.Number: return value.TryGetDouble(out var number) && number != 0;
                case JsonValueKind.Array: return value.GetArrayLength() > 1 || (value.GetArrayLength() == 1 && Truthy(value[0]));
                default: return false;
            }
        }

        public static IEnumerable<JsonElement> Items(JsonElement? node)
        {
            if (node == null) yield break;
            if (node.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in node.Value.EnumerateArray()) yield return item;
            }
            else
            {
                yield return node.Value;
            }
        }

        // [string] of a value: a string as itself, a number as written, booleans True/False, null empty.
        public static string Str(JsonElement? node)
        {
            if (node == null) return "";
            var value = node.Value;
            switch (value.ValueKind)
            {
                case JsonValueKind.String: return value.GetString();
                case JsonValueKind.Number: return value.GetRawText();
                case JsonValueKind.True: return "True";
                case JsonValueKind.False: return "False";
                case JsonValueKind.Array: return string.Join(" ", value.EnumerateArray().Select(item => Str(item)));
                case JsonValueKind.Null: case JsonValueKind.Undefined: return "";
                default: return value.GetRawText();
            }
        }

        // $x -eq $true.
        public static bool EqualsTrue(JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.True: return true;
                case JsonValueKind.String: return value.GetString().Equals("True", StringComparison.OrdinalIgnoreCase);
                case JsonValueKind.Number: return value.TryGetDouble(out var number) && number == 1;
                default: return false;
            }
        }
    }
}
