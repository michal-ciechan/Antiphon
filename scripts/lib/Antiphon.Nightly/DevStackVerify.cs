// CARD-0543, CARD-1305, CARD-1323: the one-shot health check of the local dev stack (unit 31 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md; it replaced the body of verify-dev-stack.ps1, which stays a thin
// launcher at the repository root because scripts/deploy-local.ps1 runs it by path; the restart-dev-stack command runs this
// class in process since unit 37, and bootstrap-check (BootstrapCheck, --skip-browser) since unit 38).
// The CLI runs it as
//
//   antiphon-scripts verify-dev-stack [--simple-mode] [--skip-browser] [--timeout-sec 10] [--probe-listening-ports]
//
// Aspire mode (default, Windows only: ports 1720x) checks Docker (daemon, network create, the antiphon-postgres container),
// the core listening ports, the HTTP health endpoints, the dashboard and an optional Playwright screenshot; --simple-mode
// checks the compose stack (API 17281, client 17282, runner 17204). Every check is reported before the verdict: exit 1 when a
// core check failed, 0 otherwise; a git index lock is reported but never fails the stack. --probe-listening-ports prints the
// listening TCP ports and exits 0 (portable: the Windows TCP table, or /proc/net on Linux). Full Aspire mode off Windows
// refuses before any external command (desktop-platform-required, exit 1). Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Antiphon.Nightly
{
    /// <summary>The verify-dev-stack command's options (verify-dev-stack.ps1 -SimpleMode -SkipBrowser -TimeoutSec -ProbeListeningPorts).</summary>
    public sealed class DevStackVerifyOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "verify-dev-stack [--simple-mode] [--skip-browser] [--timeout-sec 10] [--probe-listening-ports]";

        /// <summary>Check the docker-compose stack instead of Aspire.</summary>
        public bool SimpleMode { get; init; }

        /// <summary>Skip the Playwright browser smoke.</summary>
        public bool SkipBrowser { get; init; }

        /// <summary>The per-request HTTP timeout.</summary>
        public int TimeoutSec { get; init; } = 10;

        /// <summary>Print the listening ports and stop.</summary>
        public bool ProbeListeningPorts { get; init; }

        /// <summary>The checkout (the E2E Playwright script and the git index lock).</summary>
        public string RepoRoot { get; init; } = "";

        /// <summary>The flags after the command name; the checkout comes from the CLI.</summary>
        public static DevStackVerifyOptions Parse(IReadOnlyList<string> arguments, string repoRoot)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--timeout-sec" }, new[] { "--simple-mode", "--skip-browser", "--probe-listening-ports" });
            return new DevStackVerifyOptions
            {
                SimpleMode = flags.Switch("--simple-mode"),
                SkipBrowser = flags.Switch("--skip-browser"),
                TimeoutSec = flags.Int("--timeout-sec", 10),
                ProbeListeningPorts = flags.Switch("--probe-listening-ports"),
                RepoRoot = repoRoot ?? "",
            };
        }
    }

    /// <summary>The dev stack check (verify-dev-stack.ps1's body).</summary>
    public static class DevStackVerify
    {
        /// <summary>The checks' verdict; see the file comment for the exit codes.</summary>
        public static int Run(DevStackVerifyOptions options, NightlyHost host, IAppHostPlatform platform)
        {
            if (options.ProbeListeningPorts)
            {
                foreach (var port in platform.ListeningPorts()) host.Out.WriteLine(port.ToString(CultureInfo.InvariantCulture));
                return 0;
            }
            if (!options.SimpleMode && !platform.IsWindows)
            {
                host.Error.WriteLine("desktop-platform-required: Full Aspire dev-stack smoke requires Windows. Use --probe-listening-ports for a portable listener-only probe, or explicit --simple-mode for the compose stack.");
                return 1;
            }
            var simple = options.SimpleMode;
            var backend = simple ? "http://localhost:17281" : "http://localhost:17202";
            var frontend = simple ? "http://localhost:17282" : "http://localhost:17203";
            const string runner = "http://localhost:17204";
            var corePorts = simple ? new[] { 17280, 17281, 17282 } : new[] { 17200, 17202, 17203, 17204, 17205, 17206 };
            var dashboard = simple ? null : "http://localhost:17205";
            var results = new List<(string Check, bool Ok, string Detail)>();
            void Add(string name, bool ok, string detail)
            {
                results.Add((name, ok, detail));
                host.Out.WriteLine(string.Format(CultureInfo.InvariantCulture, "  {0,-28} {1,-5} {2}", name, ok ? "OK" : "FAIL", detail));
            }

            host.Out.WriteLine("");
            host.Out.WriteLine("=== Verifying Antiphon dev stack (" + (simple ? "simple/compose" : "Aspire") + ") ===");

            // 1. Docker daemon and network health (Aspire only: it needs containers).
            if (!simple)
            {
                var version = platform.Run("docker", new[] { "version" }, 12);
                var dockerUp = !version.TimedOut && version.Output.Split('\n').Any(line => line.Contains("Server:", StringComparison.OrdinalIgnoreCase));
                Add("Docker daemon", dockerUp, dockerUp ? "reachable" : "unreachable - run docker-desktop skill / restart.cmd");
                if (dockerUp)
                {
                    // HNS health: docker network create must not hang.
                    var network = "verify-hns-" + new Random().Next().ToString(CultureInfo.InvariantCulture);
                    var created = platform.Run("docker", new[] { "network", "create", network }, 8);
                    var hnsOk = !created.TimedOut;
                    if (hnsOk) platform.Run("docker", new[] { "network", "rm", network }, 0);
                    Add("Docker network (HNS)", hnsOk, hnsOk ? "create OK" : "HANGS - restart Docker Desktop (Windows HNS broken)");
                    // The always-on external container (docker-compose.dev.yml, restart: unless-stopped).
                    var postgres = platform.Run("docker", new[] { "ps", "--filter", "name=antiphon-postgres", "--format", "{{.Status}}" }, 0);
                    var up = AppHostWorktree.Lines(postgres.Output).Where(line => line.StartsWith("Up", StringComparison.OrdinalIgnoreCase)).ToArray();
                    Add("Postgres container", up.Length > 0, up.Length > 0 ? string.Join(" ", up) : "not Up - check 'docker ps -a --filter name=antiphon-postgres'");
                }
            }

            // 2. Listening ports.
            var listening = platform.ListeningPorts();
            foreach (var port in corePorts)
            {
                var open = listening.Contains(port);
                Add("Port " + port.ToString(CultureInfo.InvariantCulture), open, open ? "listening" : "closed");
            }

            // 3. HTTP endpoints.
            void Endpoint(string name, string uri, bool post = false, string expect = "")
            {
                var answer = post ? platform.Post(uri, "", options.TimeoutSec) : platform.Get(uri, options.TimeoutSec);
                if (answer.StatusCode is int status && status >= 200 && status < 300)
                {
                    var ok = string.IsNullOrEmpty(expect) || (answer.Body ?? "").IndexOf(expect, StringComparison.OrdinalIgnoreCase) >= 0;
                    Add(name, ok, "HTTP " + status.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    Add(name, false, answer.Error ?? ("HTTP " + answer.StatusCode?.ToString(CultureInfo.InvariantCulture)));
                }
            }
            Endpoint("Session runner health", runner + "/health", expect: "Healthy");
            Endpoint("Backend health", backend + "/health", expect: "Healthy");
            Endpoint("Frontend API proxy", frontend + "/api/projects");
            Endpoint("Frontend SignalR", frontend + "/hubs/antiphon/negotiate?negotiateVersion=1", post: true);
            if (dashboard != null) Endpoint("Aspire dashboard", dashboard);

            // 4. Optional browser smoke.
            if (!options.SkipBrowser)
            {
                var files = platform.Files;
                var script = files.Path.Combine(options.RepoRoot, "tests", "Antiphon.E2E", "bin", "Debug", "net9.0", "playwright.ps1");
                if (!files.File.Exists(script))
                    platform.Run("dotnet", new[] { "build", files.Path.Combine(options.RepoRoot, "tests", "Antiphon.E2E", "Antiphon.E2E.csproj"), "--nologo" }, 0);
                if (files.File.Exists(script))
                {
                    var shots = files.Path.Combine(options.RepoRoot, "tests", "Antiphon.E2E", "TestOutput", "Screenshots");
                    files.Directory.CreateDirectory(shots);
                    var shot = files.Path.Combine(shots, "dev-stack-smoke.png");
                    var run = platform.Run("pwsh", new[] { "-NoProfile", "-File", script, "screenshot", frontend, shot, "--wait-for-selector", "text=Workflows",
                        "--wait-for-timeout", "5000", "--viewport-size", "1280,720" }, 0);
                    Add("Browser smoke", run.ExitCode == 0, run.ExitCode == 0 ? shot : "playwright exit " + run.ExitCode.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    Add("Browser smoke", false, "playwright.ps1 not found");
                }
            }

            // CARD-0543: a present git index lock is reported; it never fails the stack.
            var indexLock = AppHostGitIndexLock.Read(platform, host, options.RepoRoot);
            if (indexLock == null) Add("Git index lock", true, "absent");
            else if (indexLock.Stale) Add("Git index lock", false, "stale (" + indexLock.AgeMinutes.ToString("N1", CultureInfo.InvariantCulture) + " min) at " + indexLock.Path + " - Remove-Item '" + indexLock.Path + "'");
            else Add("Git index lock", true, "fresh (" + indexLock.AgeMinutes.ToString("N1", CultureInfo.InvariantCulture) + " min)");

            var failed = results.Where(row => !row.Ok && row.Check != "Git index lock").ToList();
            host.Out.WriteLine("");
            if (failed.Count > 0)
            {
                host.Out.WriteLine("Stack NOT healthy - " + failed.Count.ToString(CultureInfo.InvariantCulture) + " check(s) failed:");
                foreach (var row in failed) host.Out.WriteLine("  - " + row.Check + ": " + row.Detail);
                return 1;
            }
            host.Out.WriteLine("Antiphon dev stack verified - all checks passed.");
            return 0;
        }
    }
}
