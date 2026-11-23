// CARD-1784: antiphon-scripts post-land-early-warning --range <base>..<tip> [--plan] [--force] [--repo <dir>]
// [--receipt-root <dir>] [--table <json>] [--file-list <txt>] [--run-id <32 hex>]. The Linux post-land early warning
// (Antiphon.Nightly.PostLandEarlyWarning). The POST_LAND_WARNING_* variables are its seams. Run mode detaches by
// relaunching this executable with the internal --child flag, under setsid where it exists; the lane writes its own
// stdout and stderr to <receipt-root>/<tip>/lane-<run-id>.log and .err. The build slot (scripts/build-slot.ps1) is still
// PowerShell and still starts under pwsh; the impacted-classes selector (ImpactedClasses) runs in this process.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class PostLandCommand
    {
        internal const string Name = "post-land-early-warning";

        internal const string Usage =
            "post-land-early-warning --range <base>..<tip> [--plan] [--force] [--repo <dir>] [--receipt-root <dir>] [--table <json>] [--file-list <txt>] [--run-id <id>]";

        internal static int Run(string[] args)
        {
            var flags = Program.Flags(args, new[] { "--range", "--receipt-root", "--repo", "--table", "--file-list", "--run-id" }, new[] { "--plan", "--force", "--child" });
            if (flags == null || !flags.ContainsKey("--range"))
            {
                Console.Error.WriteLine("usage: antiphon-scripts " + Usage);
                return Program.Usage;
            }
            string F(string name) => flags.TryGetValue(name, out var value) ? value : null;
            var options = new PostLandOptions
            {
                Range = F("--range"),
                ReceiptRoot = F("--receipt-root"),
                Repo = F("--repo"),
                Table = F("--table"),
                FileList = F("--file-list"),
                RunId = F("--run-id"),
                Plan = flags.ContainsKey("--plan"),
                Force = flags.ContainsKey("--force"),
                Child = flags.ContainsKey("--child"),
            };
            var productRoot = FindProductRoot() ?? Path.GetFullPath(string.IsNullOrWhiteSpace(options.Repo) ? Environment.CurrentDirectory : options.Repo);
            var settings = PostLandSettings.FromEnvironment(Environment.GetEnvironmentVariable, productRoot);
            settings.LaunchChild = Launch;
            var logs = options.Child ? PostLandEarlyWarning.LaneLogs(options) : null;
            if (logs == null) return PostLandEarlyWarning.Run(options, settings, Console.Out, Console.Error);
            Directory.CreateDirectory(Path.GetDirectoryName(logs.Value.Stdout));
            using var stdout = Appender(logs.Value.Stdout);
            using var stderr = Appender(logs.Value.Stderr);
            Console.SetOut(stdout);
            Console.SetError(stderr);
            return PostLandEarlyWarning.Run(options, settings, stdout, stderr);
        }

        private static StreamWriter Appender(string path) =>
            new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete), new UTF8Encoding(false)) { AutoFlush = true };

        // The checkout this executable was built in (a bin directory inside it), else the one the current directory is in.
        private static string FindProductRoot()
        {
            foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            {
                for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
                {
                    if (File.Exists(Path.Combine(directory.FullName, "tests", "post-land-unit-shards.json"))
                        && File.Exists(Path.Combine(directory.FullName, "scripts", "build-slot.ps1")))
                        return directory.FullName;
                }
            }
            return null;
        }

        private static string OnPath(string name)
        {
            var extensions = OperatingSystem.IsWindows() ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE").Split(';') : new[] { "" };
            foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory)) continue;
                foreach (var extension in extensions)
                {
                    var candidate = Path.Combine(directory.Trim(), name + extension);
                    if (File.Exists(candidate)) return candidate;
                }
            }
            return null;
        }

        // The same executable (dotnet plus this assembly when it runs under the dotnet host), detached from the caller's
        // standard handles: they are fresh pipes nobody reads, and the lane writes its own logs.
        private static void Launch(PostLandChildLaunch launch)
        {
            var executable = Environment.ProcessPath ?? "dotnet";
            var argv = new List<string> { executable };
            if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
                argv.Add(typeof(PostLandCommand).Assembly.Location);
            argv.Add(Name);
            argv.AddRange(launch.Arguments);
            var setsid = OnPath("setsid");
            var start = new ProcessStartInfo(setsid ?? argv[0])
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = launch.WorkingDirectory,
            };
            for (var i = setsid == null ? 1 : 0; i < argv.Count; i++) start.ArgumentList.Add(argv[i]);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("the lane did not start");
            process.StandardInput.Close();
        }
    }
}
