// The dev stack's restart tools behind an external Aspire control endpoint (unit 37 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md): restart-dev-stack, restart-server, restart-client,
// stop-server and assert-dev-ports-clear. They replaced the bodies of the repository root's restart.ps1, restart-server.ps1,
// restart-client.ps1, stop-server.ps1 and assert-dev-ports-clear.ps1; restart.ps1, restart-server.ps1 and stop-server.ps1 stay
// thin launchers (the runbooks name them), restart-client.ps1 and assert-dev-ports-clear.ps1 (only restart*.ps1 ran them) are
// gone. The CLI runs them as
//
//   antiphon-scripts restart-dev-stack [--endpoint https://localhost:22025] [--stop-port-owners] [--restart-runner] [--skip-verify]
//   antiphon-scripts restart-server [--endpoint https://localhost:22025] [--stop-port-owners]
//   antiphon-scripts restart-client [--endpoint https://localhost:22025] [--stop-port-owners]
//   antiphon-scripts stop-server [--endpoint https://localhost:22025]
//   antiphon-scripts assert-dev-ports-clear [--ports 17202,17203,17209] [--stop-port-owners]
//
// The server and the client are stopped and started by Mikey.AspireCommand (`dotnet run --project
// D:\src\Mikeys.Tools\Mikey.AspireCommand -- antiphon-server|antiphon-client stop|start --endpoint <endpoint>`). restart-server
// first prints the toolchain floor warnings (CARD-1698: never refuses), refuses an unreadable HEAD, stops the server, waits 3 s,
// asserts 17281 is clear, builds server/Antiphon.Server.csproj stamped with HEAD (-p:SourceRevisionId) and starts it.
// restart-client stops the client, waits 2 s, asserts 17282 is clear and starts it. restart-dev-stack restarts the session
// runner first with --restart-runner (the restart-session-runner command, in process), then the server and the client, then
// verifies the stack (verify-dev-stack, in process) unless --skip-verify. assert-dev-ports-clear lists the listeners of the
// ports; with --stop-port-owners it stops them and looks again.
//
// Exit codes: 0 done (assert-dev-ports-clear: the ports are clear; stop-server: always); 1 a step failed (restart-dev-stack: any
// step; assert-dev-ports-clear: a listener remains), or off Windows (the CLI); usage 2 (an empty or non-numeric port among
// them). Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Antiphon.Nightly
{
    /// <summary>The restart-server, restart-client and stop-server commands' options (their scripts' -Endpoint -StopPortOwners).</summary>
    public sealed class DevServerRestartOptions
    {
        /// <summary>restart-server's usage line.</summary>
        public const string ServerUsage = "restart-server [--endpoint https://localhost:22025] [--stop-port-owners]";

        /// <summary>restart-client's usage line.</summary>
        public const string ClientUsage = "restart-client [--endpoint https://localhost:22025] [--stop-port-owners]";

        /// <summary>stop-server's usage line.</summary>
        public const string StopUsage = "stop-server [--endpoint https://localhost:22025]";

        /// <summary>The Aspire control endpoint Mikey.AspireCommand talks to.</summary>
        public string Endpoint { get; init; } = DevRestart.DefaultEndpoint;

        /// <summary>Stop the listeners of the dev port instead of refusing.</summary>
        public bool StopPortOwners { get; init; }

        /// <summary>The checkout.</summary>
        public string RepoRoot { get; init; } = "";

        /// <summary>The flags after the command name; stop-server takes no --stop-port-owners.</summary>
        public static DevServerRestartOptions Parse(IReadOnlyList<string> arguments, string repoRoot, bool stopOnly = false)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--endpoint" }, stopOnly ? Array.Empty<string>() : new[] { "--stop-port-owners" });
            return new DevServerRestartOptions
            {
                Endpoint = ParseEndpoint(flags),
                StopPortOwners = !stopOnly && flags.Switch("--stop-port-owners"),
                RepoRoot = repoRoot ?? "",
            };
        }

        internal static string ParseEndpoint(NightlyArgs flags)
        {
            var endpoint = flags.Value("--endpoint", DevRestart.DefaultEndpoint);
            if (string.IsNullOrWhiteSpace(endpoint)) throw new NightlyUsageException("--endpoint expects a URL");
            return endpoint;
        }
    }

    /// <summary>The restart-dev-stack command's options (restart.ps1 -Endpoint -StopPortOwners -RestartRunner -SkipVerify).</summary>
    public sealed class DevRestartOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "restart-dev-stack [--endpoint https://localhost:22025] [--stop-port-owners] [--restart-runner] [--skip-verify]";

        /// <summary>The Aspire control endpoint.</summary>
        public string Endpoint { get; init; } = DevRestart.DefaultEndpoint;

        /// <summary>Stop the listeners of the dev ports instead of refusing.</summary>
        public bool StopPortOwners { get; init; }

        /// <summary>Restart the session runner first.</summary>
        public bool RestartRunner { get; init; }

        /// <summary>Skip verify-dev-stack at the end.</summary>
        public bool SkipVerify { get; init; }

        /// <summary>The checkout.</summary>
        public string RepoRoot { get; init; } = "";

        /// <summary>The flags after the command name; the checkout comes from the CLI.</summary>
        public static DevRestartOptions Parse(IReadOnlyList<string> arguments, string repoRoot)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--endpoint" }, new[] { "--stop-port-owners", "--restart-runner", "--skip-verify" });
            return new DevRestartOptions
            {
                Endpoint = DevServerRestartOptions.ParseEndpoint(flags),
                StopPortOwners = flags.Switch("--stop-port-owners"),
                RestartRunner = flags.Switch("--restart-runner"),
                SkipVerify = flags.Switch("--skip-verify"),
                RepoRoot = repoRoot ?? "",
            };
        }
    }

    /// <summary>The assert-dev-ports-clear command's options (assert-dev-ports-clear.ps1 -Ports -StopPortOwners).</summary>
    public sealed class DevPortsOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "assert-dev-ports-clear [--ports 17202,17203,17209] [--stop-port-owners]";

        /// <summary>The ports checked.</summary>
        public IReadOnlyList<int> Ports { get; init; } = new[] { 17202, 17203, 17209 };

        /// <summary>Stop the listeners and look again instead of refusing.</summary>
        public bool StopPortOwners { get; init; }

        /// <summary>The flags after the command name: --ports is a comma list of port numbers.</summary>
        public static DevPortsOptions Parse(IReadOnlyList<string> arguments)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--ports" }, new[] { "--stop-port-owners" });
            var ports = new List<int> { 17202, 17203, 17209 };
            if (flags.Has("--ports"))
            {
                ports.Clear();
                foreach (var entry in flags.List("--ports"))
                {
                    if (!int.TryParse(entry, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port < 1 || port > 65535)
                        throw new NightlyUsageException("--ports expects port numbers, not '" + entry + "'");
                    ports.Add(port);
                }
                if (ports.Count == 0) throw new NightlyUsageException("at least one port must be provided");
            }
            return new DevPortsOptions { Ports = ports, StopPortOwners = flags.Switch("--stop-port-owners") };
        }
    }

    /// <summary>The steps of restart-dev-stack that are other commands, run in process on the host (a test binds its own).</summary>
    public sealed class DevRestartSteps
    {
        /// <summary>The restart-session-runner command with its defaults; its exit code.</summary>
        public Func<NightlyHost, int> RestartSessionRunner { get; init; }

        /// <summary>The verify-dev-stack command with its defaults; its exit code.</summary>
        public Func<NightlyHost, int> VerifyDevStack { get; init; }
    }

    /// <summary>The restart tools (see the file header).</summary>
    public static class DevRestart
    {
        /// <summary>The Aspire control endpoint the scripts defaulted to.</summary>
        public const string DefaultEndpoint = "https://localhost:22025";

        /// <summary>The Mikey.AspireCommand project that stops and starts the server and the client.</summary>
        public const string AspireCommand = @"D:\src\Mikeys.Tools\Mikey.AspireCommand";

        private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

        /// <summary>restart.ps1's body: the runner (optional), the server, the client and the verification.</summary>
        public static int Restart(DevRestartOptions options, NightlyHost host, IAppHostPlatform platform, IDevStackSeams seams, DevRestartSteps steps)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (steps == null) throw new ArgumentNullException(nameof(steps));
            void Say(string line) => host.Out.WriteLine(line);
            Say("=== Restarting Antiphon ===");
            if (options.RestartRunner)
            {
                if (steps.RestartSessionRunner(host) != 0) return 1;
            }
            else
            {
                Say("Leaving session runner running. Use --restart-runner to restart it too.");
            }
            var part = new DevServerRestartOptions { Endpoint = options.Endpoint, StopPortOwners = options.StopPortOwners, RepoRoot = options.RepoRoot };
            if (RestartServer(part, host, platform, seams) != 0) return 1;
            if (RestartClient(part, host, platform, seams) != 0) return 1;
            if (!options.SkipVerify && steps.VerifyDevStack(host) != 0) return 1;
            Say("=== Done. Open http://localhost:" + Num(DevStack.ClientPort) + ". ===");
            return 0;
        }

        /// <summary>restart-server.ps1's body (see the file header).</summary>
        public static int RestartServer(DevServerRestartOptions options, NightlyHost host, IAppHostPlatform platform, IDevStackSeams seams)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            var files = platform.Files;
            var root = options.RepoRoot;
            void Say(string line) => host.Out.WriteLine(line);
            var head = DevStack.Head(host, root);
            if (head == null)
            {
                host.Error.WriteLine("restart-server: git rev-parse HEAD failed; refusing an unstamped server build.");
                return 1;
            }
            // CARD-1698: a tool below its floor is a warning; it never refuses and never changes the exit code.
            ToolchainFloor.Run(new ToolchainFloorOptions { RepoRoot = root }, host);

            Say("Stopping server...");
            if (Aspire(seams, root, "antiphon-server", "stop", options.Endpoint, Say) != 0)
            {
                Say("Server stop failed! Aborting.");
                return 1;
            }
            Say("Waiting for file locks to release...");
            host.Sleep(TimeSpan.FromSeconds(3));

            Say("Checking backend dev port...");
            if (AssertPortsClear(new DevPortsOptions { Ports = new[] { DevStack.ServerPort }, StopPortOwners = options.StopPortOwners }, host, platform, seams) != 0)
            {
                Say("Backend dev port check failed! Aborting.");
                return 1;
            }

            Say("Building server...");
            var project = files.Path.Combine(root, "server", "Antiphon.Server.csproj");
            if (DevStack.Run(seams, new DevStackLaunch("dotnet", new[] { "build", project, "-p:SourceRevisionId=" + head }, root), Say) != 0)
            {
                Say("Build failed! Aborting.");
                return 1;
            }

            Say("Starting server...");
            if (Aspire(seams, root, "antiphon-server", "start", options.Endpoint, Say) != 0)
            {
                Say("Server start failed! Aborting.");
                return 1;
            }
            Say("Server restarted.");
            return 0;
        }

        /// <summary>restart-client.ps1's body (see the file header).</summary>
        public static int RestartClient(DevServerRestartOptions options, NightlyHost host, IAppHostPlatform platform, IDevStackSeams seams)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            var root = options.RepoRoot;
            void Say(string line) => host.Out.WriteLine(line);
            Say("Restarting client...");
            if (Aspire(seams, root, "antiphon-client", "stop", options.Endpoint, Say) != 0)
            {
                Say("Client stop failed! Aborting.");
                return 1;
            }
            Say("Waiting for client port to release...");
            host.Sleep(TimeSpan.FromSeconds(2));

            Say("Checking frontend dev port...");
            if (AssertPortsClear(new DevPortsOptions { Ports = new[] { DevStack.ClientPort }, StopPortOwners = options.StopPortOwners }, host, platform, seams) != 0)
            {
                Say("Frontend dev port check failed! Aborting.");
                return 1;
            }

            if (Aspire(seams, root, "antiphon-client", "start", options.Endpoint, Say) != 0)
            {
                Say("Client start failed! Aborting.");
                return 1;
            }
            Say("Client restarted.");
            return 0;
        }

        /// <summary>stop-server.ps1's body: the stop, then 3 s for the file locks. Always 0 (the stop's exit is not checked).</summary>
        public static int StopServer(DevServerRestartOptions options, NightlyHost host, IDevStackSeams seams)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            void Say(string line) => host.Out.WriteLine(line);
            Say("Stopping server...");
            Aspire(seams, options.RepoRoot, "antiphon-server", "stop", options.Endpoint, Say);
            Say("Waiting for file locks to release...");
            host.Sleep(TimeSpan.FromSeconds(3));
            Say("Server stopped.");
            return 0;
        }

        /// <summary>assert-dev-ports-clear.ps1's body: 0 when no listener holds a port (after stopping them with StopPortOwners), else 1.</summary>
        public static int AssertPortsClear(DevPortsOptions options, NightlyHost host, IAppHostPlatform platform, IDevStackSeams seams)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            void Say(string line) => host.Out.WriteLine(line);
            var ports = options.Ports ?? Array.Empty<int>();
            if (ports.Count == 0) throw new NightlyUsageException("at least one port must be provided");
            var clear = "Dev ports are clear: " + string.Join(", ", ports.Select(Num)) + ".";
            var owners = Owners(platform, seams, ports);
            if (owners.Count == 0)
            {
                Say(clear);
                return 0;
            }
            Say("Dev port owner check found listeners:");
            foreach (var line in Table(owners)) Say(line);
            if (!options.StopPortOwners)
            {
                Say("Aborting to avoid starting Antiphon against stale or mismatched processes.");
                Say("Stop these processes manually, or rerun with --stop-port-owners to stop the listed owners.");
                return 1;
            }
            foreach (var pid in owners.Where(row => row.Name != Exited).Select(row => row.Pid).Distinct())
            {
                Say("Stopping PID " + Num(pid) + "...");
                platform.StopProcess(pid);
            }
            host.Sleep(TimeSpan.FromSeconds(1));
            var remaining = Owners(platform, seams, ports);
            if (remaining.Count != 0)
            {
                Say("Some dev ports are still occupied after stopping owners:");
                foreach (var line in Table(remaining)) Say(line);
                return 1;
            }
            Say(clear);
            return 0;
        }

        private const string Exited = "<exited>";

        private sealed record Owner(int Port, int Pid, string Name, string Path);

        private static List<Owner> Owners(IAppHostPlatform platform, IDevStackSeams seams, IReadOnlyList<int> ports)
        {
            var rows = new List<Owner>();
            foreach (var port in ports.Distinct().OrderBy(port => port))
                foreach (var pid in platform.PortOwners(port).Distinct().OrderBy(pid => pid))
                {
                    var process = seams.Describe(pid);
                    rows.Add(new Owner(port, pid, process?.Name ?? Exited, process?.Path ?? ""));
                }
            return rows;
        }

        // Format-Table Port, ProcessId, ProcessName, Path: a header, a rule and one aligned row per owner.
        private static IEnumerable<string> Table(IReadOnlyList<Owner> rows)
        {
            var cells = new List<string[]> { new[] { "Port", "ProcessId", "ProcessName", "Path" }, new[] { "----", "---------", "-----------", "----" } };
            cells.AddRange(rows.Select(row => new[] { Num(row.Port), Num(row.Pid), row.Name, row.Path ?? "" }));
            var widths = Enumerable.Range(0, 3).Select(column => cells.Max(cell => cell[column].Length)).ToArray();
            yield return "";
            foreach (var cell in cells)
                yield return (cell[0].PadLeft(widths[0]) + " " + cell[1].PadLeft(widths[1]) + " " + cell[2].PadRight(widths[2]) + " " + cell[3]).TrimEnd();
            yield return "";
        }

        // dotnet run --project <Mikey.AspireCommand> -- <resource> <verb> --endpoint <endpoint>, its output on the host's.
        private static int Aspire(IDevStackSeams seams, string root, string resource, string verb, string endpoint, Action<string> line) =>
            DevStack.Run(seams, new DevStackLaunch("dotnet", new[] { "run", "--project", AspireCommand, "--", resource, verb, "--endpoint", endpoint }, root), line);
    }
}
