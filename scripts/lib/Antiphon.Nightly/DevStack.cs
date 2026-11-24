// The desktop dev stack's simple (compose) mode and its database tools (unit 37 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md): dev-start, dev-stop, dev-fresh, dev-backup, dev-restore and
// dev-upgrade. Each replaced the body of the repository root's script of the same name, which stays a thin launcher so the
// runbooks' `.\dev-start.ps1` still works. The CLI runs them as
//
//   antiphon-scripts dev-start [--no-client] [--no-browser] [--server-only]
//   antiphon-scripts dev-stop [--include-postgres]
//   antiphon-scripts dev-fresh [--yes]
//   antiphon-scripts dev-backup [--backup-dir <dir>] [--retain 14]
//   antiphon-scripts dev-restore [--backup-file <file>] [--backup-dir <dir>] [--yes]
//   antiphon-scripts dev-upgrade [--no-browser]
//
// dev-start: Postgres from docker-compose.dev.yml (pg_isready, 90 s), then the server (dotnet run, stamped with HEAD, port
// 17281) and the Vite client (npm run dev, 17282) each in a Windows Terminal tab (or a console window), then the browser.
// dev-stop: stops the listeners of 17281 and 17282, and with --include-postgres `docker compose down`. dev-fresh: after
// "yes", dev-stop, `docker compose down -v` (the volume), deletes C:\Antiphon\worktrees and runs dev-start. dev-backup: a
// pg_dump of the antiphon database zipped into backups/antiphon-<local yyyyMMdd-HHmmss>.sql.zip, keeping the newest 14; the
// zip's path is the last line. dev-restore: after "yes", dev-stop, compose up, drop and recreate the database and psql the
// backup (the newest zip by default) into it. dev-upgrade: dev-backup, dev-stop, git pull, a stamped Release build of the
// server, npm install, dev-start.
//
// Exit codes: 0 done (also an answer other than "yes", which aborts); 1 failed (one stderr line), or off Windows (the CLI);
// usage 2. Every observation of the machine is the platform's (IAppHostPlatform: files, listeners, bounded commands) or the
// seams' (IDevStackSeams: streamed children, the build slot, the browser, terminal tabs, the prompt, a process's name and
// path); git, the clock and the sleep are the host's. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Antiphon.Nightly
{
    /// <summary>
    /// One child of a dev stack command: the program, its argv, where it runs, and optionally its stdin from a file and its
    /// stdout into a file (created; the raw bytes).
    /// </summary>
    public sealed record DevStackLaunch(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory, string InputFile = null, string OutputFile = null)
    {
        /// <summary>The program and the arguments, space separated.</summary>
        public string CommandText => FileName + " " + string.Join(" ", Arguments ?? Array.Empty<string>());
    }

    /// <summary>A running process as assert-dev-ports-clear names it: its image name and executable path (empty when unreadable).</summary>
    public sealed record DevStackProcess(string Name, string Path);

    /// <summary>What the dev stack commands do to the machine beyond IAppHostPlatform. A test binds a fake.</summary>
    public interface IDevStackSeams
    {
        /// <summary>
        /// Runs the child to its exit: each output line (stdout unless OutputFile takes it, and stderr, in arrival order) goes to
        /// line; returns the exit code. Throws when the child cannot start.
        /// </summary>
        int Run(DevStackLaunch launch, Action<string> line);

        /// <summary>The build slot of an in-process caller (BuildSlotRunner): the BUILD SLOT lines and the command's output go to capture.</summary>
        int BuildSlot(BuildSlotOptions options, TextWriter capture);

        /// <summary>Start-Process &lt;url&gt;: the default browser. Throws when it cannot start.</summary>
        void OpenBrowser(string url);

        /// <summary>A new Windows Terminal tab (or a console window) titled title that runs the command in the directory and stays open. Throws when it cannot start.</summary>
        void OpenTab(string title, string workingDirectory, IReadOnlyList<string> command);

        /// <summary>Read-Host: writes the prompt and returns the line typed, or null at the end of the input.</summary>
        string Prompt(string text);

        /// <summary>Get-Process -Id: the process's name and path, or null when it has exited.</summary>
        DevStackProcess Describe(int pid);
    }

    /// <summary>The dev-start command's options (dev-start.ps1 -NoClient -NoBrowser -ServerOnly).</summary>
    public sealed class DevStartOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "dev-start [--no-client] [--no-browser] [--server-only]";

        /// <summary>Skip the React client tab.</summary>
        public bool NoClient { get; init; }

        /// <summary>Do not open the browser.</summary>
        public bool NoBrowser { get; init; }

        /// <summary>Postgres and the server only (no client, no browser).</summary>
        public bool ServerOnly { get; init; }

        /// <summary>The checkout.</summary>
        public string RepoRoot { get; init; } = "";

        /// <summary>Where git worktrees live (outside the checkout); created when missing.</summary>
        public string WorktreeRoot { get; init; } = DevStack.WorktreeRoot;

        /// <summary>The flags after the command name; the checkout comes from the CLI.</summary>
        public static DevStartOptions Parse(IReadOnlyList<string> arguments, string repoRoot)
        {
            var flags = NightlyArgs.Parse(arguments, Array.Empty<string>(), new[] { "--no-client", "--no-browser", "--server-only" });
            return new DevStartOptions
            {
                NoClient = flags.Switch("--no-client"),
                NoBrowser = flags.Switch("--no-browser"),
                ServerOnly = flags.Switch("--server-only"),
                RepoRoot = repoRoot ?? "",
            };
        }
    }

    /// <summary>The dev-stop command's options (dev-stop.ps1 -IncludePostgres).</summary>
    public sealed class DevStopOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "dev-stop [--include-postgres]";

        /// <summary>Also `docker compose down` (Postgres).</summary>
        public bool IncludePostgres { get; init; }

        /// <summary>The checkout.</summary>
        public string RepoRoot { get; init; } = "";

        /// <summary>The flags after the command name; the checkout comes from the CLI.</summary>
        public static DevStopOptions Parse(IReadOnlyList<string> arguments, string repoRoot)
        {
            var flags = NightlyArgs.Parse(arguments, Array.Empty<string>(), new[] { "--include-postgres" });
            return new DevStopOptions { IncludePostgres = flags.Switch("--include-postgres"), RepoRoot = repoRoot ?? "" };
        }
    }

    /// <summary>The dev-fresh command's options (dev-fresh.ps1 -Yes).</summary>
    public sealed class DevFreshOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "dev-fresh [--yes]";

        /// <summary>Skip the confirmation prompt.</summary>
        public bool Yes { get; init; }

        /// <summary>The checkout.</summary>
        public string RepoRoot { get; init; } = "";

        /// <summary>The worktrees deleted (and created again by dev-start).</summary>
        public string WorktreeRoot { get; init; } = DevStack.WorktreeRoot;

        /// <summary>The flags after the command name; the checkout comes from the CLI.</summary>
        public static DevFreshOptions Parse(IReadOnlyList<string> arguments, string repoRoot)
        {
            var flags = NightlyArgs.Parse(arguments, Array.Empty<string>(), new[] { "--yes" });
            return new DevFreshOptions { Yes = flags.Switch("--yes"), RepoRoot = repoRoot ?? "" };
        }
    }

    /// <summary>The dev-backup command's options (dev-backup.ps1 -BackupDir -Retain).</summary>
    public sealed class DevBackupOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "dev-backup [--backup-dir <dir>] [--retain 14]";

        /// <summary>Where the zip goes; empty: the checkout's backups/.</summary>
        public string BackupDir { get; init; } = "";

        /// <summary>How many *.sql.zip files to keep (the newest by write time).</summary>
        public int Retain { get; init; } = 14;

        /// <summary>The checkout.</summary>
        public string RepoRoot { get; init; } = "";

        /// <summary>The flags after the command name; the checkout comes from the CLI.</summary>
        public static DevBackupOptions Parse(IReadOnlyList<string> arguments, string repoRoot)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--backup-dir", "--retain" }, Array.Empty<string>());
            return new DevBackupOptions { BackupDir = flags.Value("--backup-dir", "") ?? "", Retain = flags.Int("--retain", 14), RepoRoot = repoRoot ?? "" };
        }
    }

    /// <summary>The dev-restore command's options (dev-restore.ps1 -BackupFile -BackupDir -Yes).</summary>
    public sealed class DevRestoreOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "dev-restore [--backup-file <file>] [--backup-dir <dir>] [--yes]";

        /// <summary>A .sql or .sql.zip file; empty: the newest *.sql.zip in BackupDir.</summary>
        public string BackupFile { get; init; } = "";

        /// <summary>Where the newest backup is looked for; empty: the checkout's backups/.</summary>
        public string BackupDir { get; init; } = "";

        /// <summary>Skip the confirmation prompt.</summary>
        public bool Yes { get; init; }

        /// <summary>The checkout.</summary>
        public string RepoRoot { get; init; } = "";

        /// <summary>The flags after the command name; the checkout comes from the CLI.</summary>
        public static DevRestoreOptions Parse(IReadOnlyList<string> arguments, string repoRoot)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--backup-file", "--backup-dir" }, new[] { "--yes" });
            return new DevRestoreOptions
            {
                BackupFile = flags.Value("--backup-file", "") ?? "",
                BackupDir = flags.Value("--backup-dir", "") ?? "",
                Yes = flags.Switch("--yes"),
                RepoRoot = repoRoot ?? "",
            };
        }
    }

    /// <summary>The dev-upgrade command's options (dev-upgrade.ps1 -NoBrowser).</summary>
    public sealed class DevUpgradeOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "dev-upgrade [--no-browser]";

        /// <summary>Passed to dev-start.</summary>
        public bool NoBrowser { get; init; }

        /// <summary>The checkout.</summary>
        public string RepoRoot { get; init; } = "";

        /// <summary>The worktrees root dev-start creates.</summary>
        public string WorktreeRoot { get; init; } = DevStack.WorktreeRoot;

        /// <summary>The flags after the command name; the checkout comes from the CLI.</summary>
        public static DevUpgradeOptions Parse(IReadOnlyList<string> arguments, string repoRoot)
        {
            var flags = NightlyArgs.Parse(arguments, Array.Empty<string>(), new[] { "--no-browser" });
            return new DevUpgradeOptions { NoBrowser = flags.Switch("--no-browser"), RepoRoot = repoRoot ?? "" };
        }
    }

    /// <summary>The simple-mode dev stack commands and the pieces the dev stack commands share.</summary>
    public static class DevStack
    {
        /// <summary>Where the git worktrees live: outside the checkout (a worktree cannot live inside the repository).</summary>
        public const string WorktreeRoot = @"C:\Antiphon\worktrees";

        /// <summary>The simple mode's API port.</summary>
        public const int ServerPort = 17281;

        /// <summary>The simple mode's Vite client port.</summary>
        public const int ClientPort = 17282;

        /// <summary>The checkout's docker-compose.dev.yml.</summary>
        public static string ComposeFile(IFileSystem files, string root) => files.Path.Combine(root, "docker-compose.dev.yml");

        /// <summary>The checkout's HEAD as 40 lower-case hex characters, or null (a deployed build is stamped with it, never "unknown").</summary>
        public static string Head(NightlyHost host, string root)
        {
            var head = AppHostProbes.SourceHead(host, root);
            return head != null && head.Length == 40 ? head : null;
        }

        /// <summary>Runs the child on the seams; a child that cannot start is one line (its message) and exit 1.</summary>
        public static int Run(IDevStackSeams seams, DevStackLaunch launch, Action<string> line)
        {
            try
            {
                return seams.Run(launch, line);
            }
            catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is IOException || e is NightlyRefusal)
            {
                line(e.Message);
                return 1;
            }
        }

        /// <summary>npm with the arguments in the directory (ClientTools.Npm: the absolute npm launcher under ComSpec /c on Windows).</summary>
        public static int Npm(NightlyHost host, IDevStackSeams seams, string directory, Action<string> line, params string[] arguments)
        {
            ClientToolLaunch npm;
            try { npm = ClientTools.Npm(host, directory, arguments); }
            catch (NightlyRefusal e)
            {
                line(e.Message);
                return 1;
            }
            return Run(seams, new DevStackLaunch(npm.FileName, npm.Arguments, npm.WorkingDirectory), line);
        }

        /// <summary>The docker compose argv of the checkout's dev compose file: compose -f &lt;file&gt; then the rest.</summary>
        public static string[] Compose(IFileSystem files, string root, params string[] rest) =>
            new[] { "compose", "-f", ComposeFile(files, root) }.Concat(rest).ToArray();

        /// <summary>pg_isready against the compose postgres service answered 0.</summary>
        public static bool PostgresReady(IAppHostPlatform platform, string root) =>
            platform.Run("docker", Compose(platform.Files, root, "exec", "-T", "postgres", "pg_isready", "-U", "antiphon", "-d", "antiphon"), 0).ExitCode == 0;

        /// <summary>"yes", ignoring case (PowerShell's -ne), was typed at the prompt.</summary>
        public static bool Confirmed(IDevStackSeams seams, string prompt) =>
            string.Equals(seams.Prompt(prompt), "yes", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Remove-Item -Recurse -Force: deletes the directory tree, read-only files included. A junction or symbolic link inside is
        /// removed as a link; its target is never entered.
        /// </summary>
        public static void DeleteTree(IFileSystem files, string path)
        {
            var directory = files.DirectoryInfo.New(path);
            if (!directory.Exists) return;
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                if ((entry.Attributes & FileAttributes.ReadOnly) != 0) entry.Attributes &= ~FileAttributes.ReadOnly;
                if (entry is IDirectoryInfo child)
                {
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) files.Directory.Delete(child.FullName, false);
                    else DeleteTree(files, child.FullName);
                }
                else
                {
                    files.File.Delete(entry.FullName);
                }
            }
            if ((directory.Attributes & FileAttributes.ReadOnly) != 0) directory.Attributes &= ~FileAttributes.ReadOnly;
            files.Directory.Delete(path, false);
        }

        /// <summary>A copy of the host whose output and error are writer (the clock, git, sleep and variables are the host's).</summary>
        public static NightlyHost WithOutput(NightlyHost host, TextWriter writer) => new NightlyHost
        {
            Clock = host.Clock,
            Git = host.Git,
            Launcher = host.Launcher,
            Processes = host.Processes,
            Probe = host.Probe,
            Out = writer,
            Error = writer,
            Trace = host.Trace,
            Variables = host.Variables,
            Sleep = host.Sleep,
            Qualification = host.Qualification,
        };

        private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

        // ---- dev-start ----

        /// <summary>Starts the compose stack, the server and the client (see the file header).</summary>
        public static int Start(DevStartOptions options, NightlyHost host, IAppHostPlatform platform, IDevStackSeams seams)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            var files = platform.Files;
            var root = options.RepoRoot;
            void Say(string line) => host.Out.WriteLine(line);
            var logDir = files.Path.Combine(root, "server", "logs");
            var head = Head(host, root);
            if (head == null)
            {
                host.Error.WriteLine("dev-start: git rev-parse HEAD failed; refusing an unstamped server launch.");
                return 1;
            }
            foreach (var directory in new[] { logDir, files.Path.Combine(logDir, "sessions"), files.Path.Combine(root, "backups"), options.WorktreeRoot })
                files.Directory.CreateDirectory(directory);
            if (!files.File.Exists(files.Path.Combine(root, "server", "appsettings.json")))
            {
                host.Error.WriteLine("server\\appsettings.json not found. Copy appsettings.json.example and configure it.");
                return 1;
            }
            if (platform.Run("docker", new[] { "info" }, 0).ExitCode != 0)
            {
                host.Error.WriteLine("Docker Desktop is not running. Start it, wait for the tray icon, then re-run.");
                return 1;
            }

            Say("");
            Say("> Starting PostgreSQL...");
            if (Run(seams, new DevStackLaunch("docker", Compose(files, root, "up", "-d"), root), Say) != 0)
            {
                host.Error.WriteLine("docker compose up failed.");
                return 1;
            }
            Say("  Waiting for postgres...");
            var elapsed = 0;
            var ready = false;
            do
            {
                host.Sleep(TimeSpan.FromSeconds(3));
                elapsed += 3;
                ready = PostgresReady(platform, root);
                if (ready) break;
                Say("  ...(" + Num(elapsed) + " s)");
            }
            while (elapsed < 90);
            Say(ready ? "  Postgres ready." : "WARNING: Postgres not yet healthy - continuing.");

            try
            {
                Say("> Launching .NET server (port " + Num(ServerPort) + ")...");
                seams.OpenTab("Antiphon Server", files.Path.Combine(root, "server"),
                    new[] { "dotnet", "run", "-p:SourceRevisionId=" + head, "--urls", "http://localhost:" + Num(ServerPort) });
                if (!options.NoClient && !options.ServerOnly)
                {
                    Say("> Launching React client (port " + Num(ClientPort) + ")...");
                    seams.OpenTab("Antiphon Client", files.Path.Combine(root, "client"), new[] { "npm", "run", "dev" });
                }
            }
            catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is IOException)
            {
                host.Error.WriteLine("dev-start: could not open a terminal: " + e.Message);
                return 1;
            }

            Say("");
            Say("Antiphon starting up (first run ~15s):");
            Say("  http://localhost:" + Num(ClientPort) + "  <- app");
            Say("  http://localhost:" + Num(ServerPort) + "  <- API");
            Say("  localhost:17280         <- PostgreSQL");
            Say("");
            Say("  Logs  : " + logDir + files.Path.DirectorySeparatorChar);
            Say("  Backup: .\\dev-backup.ps1");
            Say("  Aspire: .\\dev-aspire.ps1  (dashboard + daemon mode)");
            if (!options.NoBrowser && !options.ServerOnly)
            {
                host.Sleep(TimeSpan.FromSeconds(15));
                Browse(host, seams, "http://localhost:" + Num(ClientPort));
            }
            return 0;
        }

        /// <summary>Opens the browser; a browser that cannot start is a warning (the stack is already up).</summary>
        public static void Browse(NightlyHost host, IDevStackSeams seams, string url)
        {
            try { seams.OpenBrowser(url); }
            catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is IOException)
            {
                host.Out.WriteLine("WARNING: could not open " + url + ": " + e.Message);
            }
        }

        // ---- dev-stop ----

        /// <summary>Stops the simple mode's server and client listeners, and with IncludePostgres the compose stack. Always 0.</summary>
        public static int Stop(DevStopOptions options, NightlyHost host, IAppHostPlatform platform)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            void Say(string line) => host.Out.WriteLine(line);
            Say("");
            Say("Stopping Antiphon services...");
            StopOnPort(host, platform, ServerPort, ".NET server");
            StopOnPort(host, platform, ClientPort, "React client");
            if (options.IncludePostgres)
            {
                Say("  Stopping PostgreSQL...");
                platform.Run("docker", Compose(platform.Files, options.RepoRoot, "down"), 0);
                Say("  PostgreSQL stopped.");
            }
            else
            {
                Say("  PostgreSQL left running (pass --include-postgres to stop it).");
            }
            Say("");
            return 0;
        }

        private static void StopOnPort(NightlyHost host, IAppHostPlatform platform, int port, string label)
        {
            var owners = platform.PortOwners(port);
            if (owners.Count == 0)
            {
                host.Out.WriteLine("  " + label + " not listening on :" + Num(port));
                return;
            }
            foreach (var pid in owners) platform.StopProcess(pid);
            host.Out.WriteLine("  Stopped " + label + " (PID " + string.Join(" ", owners.Select(Num)) + ")");
        }

        // ---- dev-fresh ----

        /// <summary>The nuclear reset (see the file header).</summary>
        public static int Fresh(DevFreshOptions options, NightlyHost host, IAppHostPlatform platform, IDevStackSeams seams)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            var files = platform.Files;
            var root = options.RepoRoot;
            void Say(string line) => host.Out.WriteLine(line);
            Say("");
            Say("Antiphon FRESH START");
            Say("This will:");
            Say("  - Stop the server and client");
            Say("  - Destroy the postgres volume (ALL DATABASE DATA LOST)");
            Say("  - Delete all worktrees under " + options.WorktreeRoot + " (outside repo)");
            Say("  - Re-start everything from scratch");
            Say("");
            if (!options.Yes && !Confirmed(seams, "Type 'yes' to continue, anything else to abort"))
            {
                Say("Aborted.");
                return 0;
            }

            Say("");
            Say("> Stopping services...");
            Stop(new DevStopOptions { RepoRoot = root }, host, platform);
            Say("> Dropping postgres volume...");
            var down = Run(seams, new DevStackLaunch("docker", Compose(files, root, "down", "-v"), root), Say);
            if (down != 0) Say("WARNING: docker compose down -v returned " + Num(down) + " - continuing.");
            if (files.Directory.Exists(options.WorktreeRoot))
            {
                Say("> Wiping worktrees...");
                DeleteTree(files, options.WorktreeRoot);
            }
            Say("");
            Say("> Starting fresh...");
            return Start(new DevStartOptions { RepoRoot = root, WorktreeRoot = options.WorktreeRoot }, host, platform, seams);
        }

        // ---- dev-backup ----

        /// <summary>The pg_dump zip (see the file header); its path is the last line of output. 1 when Postgres is down or pg_dump fails.</summary>
        public static int Backup(DevBackupOptions options, NightlyHost host, IAppHostPlatform platform, IDevStackSeams seams)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            var files = platform.Files;
            var root = options.RepoRoot;
            void Say(string line) => host.Out.WriteLine(line);
            var backupDir = string.IsNullOrWhiteSpace(options.BackupDir) ? files.Path.Combine(root, "backups") : options.BackupDir;
            files.Directory.CreateDirectory(backupDir);
            if (!PostgresReady(platform, root))
            {
                host.Error.WriteLine("Postgres is not running. Start it first: docker compose -f docker-compose.dev.yml up -d");
                return 1;
            }
            var stamp = host.Clock.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var dump = files.Path.Combine(backupDir, "antiphon-" + stamp + ".sql");
            var zip = dump + ".zip";
            Say("Backing up -> " + zip + " ...");
            var code = Run(seams, new DevStackLaunch("docker", Compose(files, root, "exec", "-T", "postgres", "pg_dump", "-U", "antiphon", "-d", "antiphon", "--no-owner", "--no-acl"),
                root, OutputFile: dump), Say);
            if (code != 0)
            {
                try { files.File.Delete(dump); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
                host.Error.WriteLine("pg_dump failed (exit " + Num(code) + ").");
                return 1;
            }
            Zip(files, dump, zip);
            files.File.Delete(dump);
            var size = Math.Round(files.FileInfo.New(zip).Length / 1024.0, 1);
            Say("  Created: " + zip + "  (" + size.ToString("0.#", CultureInfo.InvariantCulture) + " KB)");

            var all = files.Directory.EnumerateFiles(backupDir, "*.sql.zip").Select(path => files.FileInfo.New(path)).OrderByDescending(info => info.LastWriteTimeUtc).ToList();
            if (all.Count > options.Retain)
            {
                var prune = all.Skip(Math.Max(0, options.Retain)).ToList();
                foreach (var old in prune) files.File.Delete(old.FullName);
                Say("  Pruned " + Num(prune.Count) + " old backup(s), kept " + Num(options.Retain) + ".");
            }
            Say(zip);
            return 0;
        }

        /// <summary>Compress-Archive -Force: a zip at zip (replaced) holding file under its own name.</summary>
        public static void Zip(IFileSystem files, string file, string zip)
        {
            using var target = files.FileStream.New(zip, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            using var archive = new ZipArchive(target, ZipArchiveMode.Create);
            var entry = archive.CreateEntry(files.Path.GetFileName(file), CompressionLevel.Optimal);
            using var input = files.FileStream.New(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var output = entry.Open();
            input.CopyTo(output);
        }

        /// <summary>Expand-Archive -Force: every entry of zip under directory (a name that would leave it refuses).</summary>
        public static void Unzip(IFileSystem files, string zip, string directory)
        {
            var rootPath = files.Path.GetFullPath(directory).TrimEnd('\\', '/') + files.Path.DirectorySeparatorChar;
            using var source = files.FileStream.New(zip, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = new ZipArchive(source, ZipArchiveMode.Read);
            foreach (var entry in archive.Entries)
            {
                var target = files.Path.GetFullPath(files.Path.Combine(directory, entry.FullName));
                if (!target.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase)) throw new IOException("the archive entry " + entry.FullName + " leaves " + directory);
                if (entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.FullName.EndsWith("\\", StringComparison.Ordinal))
                {
                    files.Directory.CreateDirectory(target);
                    continue;
                }
                files.Directory.CreateDirectory(files.Path.GetDirectoryName(target));
                using var input = entry.Open();
                using var output = files.FileStream.New(target, FileMode.Create, FileAccess.Write, FileShare.None);
                input.CopyTo(output);
            }
        }

        // ---- dev-restore ----

        /// <summary>Drops, recreates and restores the antiphon database (see the file header).</summary>
        public static int Restore(DevRestoreOptions options, NightlyHost host, IAppHostPlatform platform, IDevStackSeams seams)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            var files = platform.Files;
            var root = options.RepoRoot;
            void Say(string line) => host.Out.WriteLine(line);
            var backupDir = string.IsNullOrWhiteSpace(options.BackupDir) ? files.Path.Combine(root, "backups") : options.BackupDir;
            var backup = options.BackupFile;
            if (string.IsNullOrWhiteSpace(backup))
            {
                backup = files.Directory.Exists(backupDir)
                    ? files.Directory.EnumerateFiles(backupDir, "*.sql.zip").Select(path => files.FileInfo.New(path)).OrderByDescending(info => info.LastWriteTimeUtc).FirstOrDefault()?.FullName
                    : null;
                if (backup == null)
                {
                    host.Error.WriteLine("No backups found in " + backupDir + ". Run .\\dev-backup.ps1 first.");
                    return 1;
                }
                Say("Using latest backup: " + backup);
            }
            if (!files.File.Exists(backup))
            {
                host.Error.WriteLine("File not found: " + backup);
                return 1;
            }

            Say("");
            Say("This will DROP and recreate the 'antiphon' database, then restore from backup.");
            if (!options.Yes && !Confirmed(seams, "Type 'yes' to continue"))
            {
                Say("Aborted.");
                return 0;
            }

            Say("");
            Say("> Stopping .NET server...");
            Stop(new DevStopOptions { RepoRoot = root }, host, platform);
            Say("> Ensuring PostgreSQL is running...");
            Run(seams, new DevStackLaunch("docker", Compose(files, root, "up", "-d"), root), Say);
            host.Sleep(TimeSpan.FromSeconds(5));

            string extracted = null;
            try
            {
                var sql = backup;
                if (backup.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    var temp = host.Variables("TEMP");
                    if (string.IsNullOrWhiteSpace(temp)) temp = files.Path.GetTempPath();
                    extracted = files.Path.Combine(temp, "antiphon-restore-" + host.Clock.GetLocalNow().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture));
                    files.Directory.CreateDirectory(extracted);
                    Unzip(files, backup, extracted);
                    sql = files.Directory.EnumerateFiles(extracted, "*.sql").OrderBy(path => path, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                    if (sql == null)
                    {
                        host.Error.WriteLine("No .sql file found inside " + backup);
                        return 1;
                    }
                }

                Say("> Recreating database...");
                foreach (var statement in new[]
                {
                    "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname='antiphon';",
                    "DROP DATABASE IF EXISTS antiphon;",
                    "CREATE DATABASE antiphon OWNER antiphon;",
                })
                    platform.Run("docker", Compose(files, root, "exec", "-T", "postgres", "psql", "-U", "antiphon", "-d", "postgres", "-c", statement), 0);

                Say("> Restoring from " + backup + " ...");
                var code = Run(seams, new DevStackLaunch("docker", Compose(files, root, "exec", "-T", "postgres", "psql", "-U", "antiphon", "-d", "antiphon"), root, InputFile: sql), _ => { });
                if (code != 0)
                {
                    host.Error.WriteLine("psql restore failed (exit " + Num(code) + ").");
                    return 1;
                }
            }
            finally
            {
                if (extracted != null)
                {
                    try { DeleteTree(files, extracted); }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
                }
            }
            Say("");
            Say("Restore complete.");
            Say("Run .\\dev-start.ps1 to restart the server.");
            return 0;
        }

        // ---- dev-upgrade ----

        /// <summary>Backup, stop, pull, build, npm install, start (see the file header).</summary>
        public static int Upgrade(DevUpgradeOptions options, NightlyHost host, IAppHostPlatform platform, IDevStackSeams seams)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            var files = platform.Files;
            var root = options.RepoRoot;
            void Say(string line) => host.Out.WriteLine(line);
            Say("");
            Say("Antiphon upgrade");

            Say("");
            Say("> Backing up database...");
            var backup = Backup(new DevBackupOptions { RepoRoot = root }, host, platform, seams);
            if (backup != 0) return backup;

            Say("");
            Say("> Stopping server and client...");
            Stop(new DevStopOptions { RepoRoot = root }, host, platform);

            Say("");
            Say("> git pull...");
            var pull = host.RunGit(root, "pull");
            foreach (var line in AppHostWorktree.Lines(pull.Output)) Say(line);
            if (pull.ExitCode != 0)
            {
                host.Error.WriteLine("git pull failed - resolve conflicts and retry.");
                return 1;
            }

            Say("");
            Say("> Building .NET server...");
            var head = Head(host, root);
            if (head == null)
            {
                host.Error.WriteLine("dev-upgrade: git rev-parse HEAD failed; refusing an unstamped server build.");
                return 1;
            }
            var server = files.Path.Combine(root, "server");
            var build = Run(seams, new DevStackLaunch("dotnet", new[] { "build", server, "-p:SourceRevisionId=" + head, "--configuration", "Release", "--no-restore" }, root), Say);
            if (build != 0)
            {
                build = Run(seams, new DevStackLaunch("dotnet", new[] { "build", server, "-p:SourceRevisionId=" + head, "--configuration", "Release" }, root), Say);
                if (build != 0)
                {
                    host.Error.WriteLine("dotnet build failed.");
                    return 1;
                }
            }

            Say("");
            Say("> npm install...");
            if (Npm(host, seams, files.Path.Combine(root, "client"), Say, "install") != 0)
            {
                host.Error.WriteLine("npm install failed.");
                return 1;
            }

            Say("");
            Say("> Starting services...");
            var start = Start(new DevStartOptions { RepoRoot = root, NoBrowser = options.NoBrowser, WorktreeRoot = options.WorktreeRoot }, host, platform, seams);
            if (start != 0) return start;
            Say("");
            Say("Upgrade complete.");
            return 0;
        }
    }

    /// <summary>A writer that appends each line to a log file (UTF-8, no byte-order mark), as RunAppending did for a child.</summary>
    public sealed class DevStackLogWriter : TextWriter
    {
        private readonly IFileSystem _files;
        private readonly string _path;
        private readonly StringBuilder _pending = new StringBuilder();

        /// <summary>Appends to path on files.</summary>
        public DevStackLogWriter(IFileSystem files, string path)
        {
            _files = files ?? throw new ArgumentNullException(nameof(files));
            _path = path;
        }

        /// <inheritdoc />
        public override Encoding Encoding => new UTF8Encoding(false);

        /// <inheritdoc />
        public override void Write(char value)
        {
            lock (_pending)
            {
                if (value == '\r') return;
                if (value != '\n')
                {
                    _pending.Append(value);
                    return;
                }
                var line = _pending.ToString();
                _pending.Clear();
                Append(line);
            }
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            lock (_pending)
            {
                if (_pending.Length > 0) Append(_pending.ToString());
                _pending.Clear();
            }
            base.Dispose(disposing);
        }

        private void Append(string line)
        {
            try { _files.File.AppendAllText(_path, line + Environment.NewLine, new UTF8Encoding(false)); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
        }
    }
}
