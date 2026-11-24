// The logon auto-start of the desktop Aspire AppHost (server 17202, client 17203, dashboard 17205, control API 17207; unit 31 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md; it replaced the body of scripts/autostart-apphost.ps1, which
// stays a thin launcher because the per-user Scheduled Task "Antiphon AppHost" (scripts/install-autostart.ps1) starts it by
// path). The CLI runs it as
//
//   antiphon-scripts autostart-apphost [--no-build] [--docker-timeout-sec 300] [--health-timeout-sec 180]
//
// Unlike running dev-aspire.ps1 at logon, it waits for Docker Desktop (still starting at logon; dev-aspire.ps1 hard-errors
// without it), no-ops when port 17202 already listens (a manual dev-aspire.ps1 is never clobbered), brings up the dev compose
// services (runbook G-1: ANTIPHON_SKIP_DEV_POSTGRES=1 leaves the desktop Postgres stopped), passes -NoBrowser, and logs to
// logs/autostart-apphost.log (dev-aspire.ps1's own output too). The AppHost is launched detached by dev-aspire.ps1 and
// survives this command. Exit 0 once /health answers 200 (or already up); 1 when Docker or health did not come up.
// Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The autostart-apphost command's options (autostart-apphost.ps1 -NoBuild -DockerTimeoutSec -HealthTimeoutSec).</summary>
    public sealed class AppHostAutostartOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "autostart-apphost [--no-build] [--docker-timeout-sec 300] [--health-timeout-sec 180]";

        /// <summary>Skip dotnet restore and npm install (dev-aspire.ps1 -NoBuild).</summary>
        public bool NoBuild { get; init; }

        /// <summary>How long to wait for Docker Desktop.</summary>
        public int DockerTimeoutSec { get; init; } = 300;

        /// <summary>How long to wait for /health after the launch.</summary>
        public int HealthTimeoutSec { get; init; } = 180;

        /// <summary>The checkout.</summary>
        public string RepoRoot { get; init; } = "";

        /// <summary>The flags after the command name; the checkout comes from the CLI.</summary>
        public static AppHostAutostartOptions Parse(IReadOnlyList<string> arguments, string repoRoot)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--docker-timeout-sec", "--health-timeout-sec" }, new[] { "--no-build" });
            return new AppHostAutostartOptions
            {
                NoBuild = flags.Switch("--no-build"),
                DockerTimeoutSec = flags.Int("--docker-timeout-sec", 300),
                HealthTimeoutSec = flags.Int("--health-timeout-sec", 180),
                RepoRoot = repoRoot ?? "",
            };
        }
    }

    /// <summary>The logon auto-start (autostart-apphost.ps1's body).</summary>
    public static class AppHostAutostart
    {
        private static readonly Regex Healthy = new Regex("healthy", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex Up = new Regex("^Up", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex Health = new Regex("health", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>Brings the stack up at logon; exit 0 healthy or already up, 1 otherwise.</summary>
        public static int Run(AppHostAutostartOptions options, NightlyHost host, IAppHostPlatform platform)
        {
            var files = platform.Files;
            var root = options.RepoRoot;
            var logDir = AppHost.LogDir(files, root);
            files.Directory.CreateDirectory(logDir);
            var logFile = files.Path.Combine(logDir, "autostart-apphost.log");
            void Log(string message)
            {
                var line = host.Clock.GetLocalNow().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " [autostart-apphost] " + message;
                host.Out.WriteLine(line);
                try { files.File.AppendAllText(logFile, line + Environment.NewLine, new UTF8Encoding(false)); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
            }
            var port = AppHost.ServerPort.ToString(CultureInfo.InvariantCulture);

            Log("logon auto-start beginning (PID " + platform.CurrentPid.ToString(CultureInfo.InvariantCulture) + ")");
            // 1. Already running: never clobber a manual dev-aspire.ps1.
            if (platform.PortOwners(AppHost.ServerPort).Count > 0)
            {
                Log("port " + port + " already listening - AppHost is up, nothing to do.");
                return 0;
            }

            // 2. Docker Desktop.
            Log("waiting for Docker Desktop (up to " + options.DockerTimeoutSec.ToString(CultureInfo.InvariantCulture) + "s)...");
            var dockerDeadline = host.UtcNow.AddSeconds(options.DockerTimeoutSec);
            var dockerOk = false;
            var timing = new LiveStartupPhase(host, "docker-info");
            try
            {
                while (host.UtcNow < dockerDeadline)
                {
                    if (platform.Run("docker", new[] { "info" }, 0).ExitCode == 0) { dockerOk = true; break; }
                    host.Sleep(TimeSpan.FromSeconds(10));
                }
            }
            finally
            {
                timing.Stop(dockerOk);
            }
            if (!dockerOk)
            {
                Log("ERROR: Docker Desktop did not become responsive within " + options.DockerTimeoutSec.ToString(CultureInfo.InvariantCulture) + "s - aborting.");
                Log("Fix: start Docker Desktop, then run dev-aspire.ps1 (or Start-ScheduledTask -TaskName 'Antiphon AppHost').");
                return 1;
            }
            Log("Docker is responsive.");

            // 3. The dev compose services (idempotent; restart: unless-stopped usually has them up).
            var skipValue = host.Variables(DevCompose.SkipVariable);
            var skipped = DevCompose.Skipped(skipValue);
            var compose = DevCompose.Up(platform, files.Path.Combine(root, "docker-compose.dev.yml"), skipValue);
            if (skipped) Log(compose.Message);
            if (compose.Exit != 0) Log("WARNING: 'docker compose up -d' failed; continuing (AppHost pre-flight retries it).");
            else if (!skipped) Log("Postgres container ensured.");
            if (skipped)
            {
                Log("Postgres status: desktop container not checked (ANTIPHON_SKIP_DEV_POSTGRES=1).");
            }
            else
            {
                // Healthy before the server races its EF migrations.
                var postgresDeadline = host.UtcNow.AddSeconds(120);
                while (host.UtcNow < postgresDeadline)
                {
                    var status = PostgresStatus(platform);
                    if (Healthy.IsMatch(status) || (Up.IsMatch(status) && !Health.IsMatch(status))) break;
                    host.Sleep(TimeSpan.FromSeconds(5));
                }
                Log("Postgres status: " + PostgresStatus(platform));
            }

            // 4. The stack: dev-aspire.ps1 backgrounds the AppHost and exits once the dashboard is up.
            var devArguments = new List<string> { "-NonInteractive", "-NoLogo", "-ExecutionPolicy", "Bypass", "-File", files.Path.Combine(root, "dev-aspire.ps1"), "-NoBrowser" };
            if (options.NoBuild) devArguments.Add("-NoBuild");
            Log("launching dev-aspire.ps1 -NoBrowser" + (options.NoBuild ? " -NoBuild" : "") + "...");
            platform.RunAppending("pwsh", devArguments, logFile);

            // 5. Health.
            var healthDeadline = host.UtcNow.AddSeconds(options.HealthTimeoutSec);
            var healthy = false;
            while (host.UtcNow < healthDeadline)
            {
                if (platform.Get("http://localhost:" + port + "/health", 5).StatusCode == 200) { healthy = true; break; }
                host.Sleep(TimeSpan.FromSeconds(5));
            }
            if (healthy)
            {
                var urlFile = files.Path.Combine(logDir, "apphost-dashboard-url.txt");
                var url = files.File.Exists(urlFile) ? files.File.ReadAllText(urlFile).Trim() : "http://localhost:17205";
                Log("OK - backend healthy on :" + port + ", dashboard " + url);
                return 0;
            }
            Log("ERROR: backend not healthy within " + options.HealthTimeoutSec.ToString(CultureInfo.InvariantCulture) + "s - check logs/apphost.log");
            return 1;
        }

        private static string PostgresStatus(IAppHostPlatform platform) =>
            platform.Run("docker", new[] { "ps", "--filter", "name=antiphon-postgres", "--format", "{{.Status}}" }, 0).Output.Trim();
    }

    /// <summary>One compose bring-up: its exit, whether Postgres was skipped and ran at all, the arguments and the skip message.</summary>
    public sealed record DevComposeUp(int Exit, bool SkipPostgres, bool Ran, IReadOnlyList<string> Arguments, string Message);

    /// <summary>
    /// Runbook G-1: the docker-compose.dev.yml bring-up (scripts/lib/dev-postgres-compose.ps1, which dev-aspire.ps1 still loads).
    /// ANTIPHON_SKIP_DEV_POSTGRES=1 (the database moved to server2) brings up every other service with --no-deps and never
    /// the postgres service, so a restart cannot revive the stopped desktop copy.
    /// </summary>
    public static class DevCompose
    {
        /// <summary>The opt-out variable (process environment only).</summary>
        public const string SkipVariable = "ANTIPHON_SKIP_DEV_POSTGRES";

        private static readonly Regex Services = new Regex(@"^services:\s*$", RegexOptions.CultureInvariant);
        private static readonly Regex TopLevel = new Regex(@"^[^\s#]", RegexOptions.CultureInvariant);
        private static readonly Regex Service = new Regex(@"^  ([A-Za-z0-9][A-Za-z0-9_.-]*):\s*$", RegexOptions.CultureInvariant);

        /// <summary>Test-DevPostgresSkipped: the value trimmed is 1.</summary>
        public static bool Skipped(string value) => value != null && value.Trim() == "1";

        /// <summary>Get-DevComposeServices: the two-space keys under services:.</summary>
        public static IReadOnlyList<string> ServiceNames(string composeText)
        {
            var names = new List<string>();
            var inServices = false;
            foreach (var line in Regex.Split(composeText ?? "", "\r?\n"))
            {
                if (Services.IsMatch(line)) { inServices = true; continue; }
                if (TopLevel.IsMatch(line)) { inServices = false; continue; }
                var match = Service.Match(line);
                if (inServices && match.Success) names.Add(match.Groups[1].Value);
            }
            return names;
        }

        /// <summary>Get-DevComposeUpArguments: compose -f file up -d, or with the skip every other service --no-deps (none: no run).</summary>
        public static IReadOnlyList<string> UpArguments(string composeFile, string composeText, bool skipPostgres)
        {
            var arguments = new List<string> { "compose", "-f", composeFile, "up", "-d" };
            if (!skipPostgres) return arguments;
            var others = ServiceNames(composeText).Where(name => name != "postgres").ToList();
            if (others.Count == 0) return Array.Empty<string>();
            arguments.Add("--no-deps");
            arguments.AddRange(others);
            return arguments;
        }

        /// <summary>Invoke-DevComposeUp: runs docker with the arguments (nothing to run is exit 0).</summary>
        public static DevComposeUp Up(IAppHostPlatform platform, string composeFile, string skipValue)
        {
            var skip = Skipped(skipValue);
            var text = skip && platform.Files.File.Exists(composeFile) ? platform.Files.File.ReadAllText(composeFile) : "";
            var arguments = UpArguments(composeFile, text, skip);
            var message = "";
            if (skip)
            {
                var started = arguments.Skip(6).ToList();
                message = SkipVariable + "=1: desktop Postgres bring-up skipped (database moved off-host); compose up -d --no-deps "
                    + (started.Count > 0 ? string.Join(", ", started) + " only" : "no other service");
            }
            if (arguments.Count == 0) return new DevComposeUp(0, skip, false, arguments, message);
            return new DevComposeUp(platform.Run("docker", arguments, 0).ExitCode, skip, true, arguments, message);
        }
    }
}
