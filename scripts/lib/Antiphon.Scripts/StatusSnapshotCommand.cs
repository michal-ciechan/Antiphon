// antiphon-scripts status-snapshot [--project-id <guid>] [--runner server2] [--json] [--rc-run-log-dir <dir>]
// [--rc-state-dir <dir>] [--rc-heartbeat-path <file>] [--repository <dir>]. Raw, read-only status facts
// (Antiphon.Nightly.StatusSnapshot.Run on the production host, HTTP transport with the 20 s deadline and git reader). It
// replaced scripts/status-snapshot.ps1 (deleted 2026-11-23, unit 7 of 2026-11-22-remaining-ps1-to-csharp.md). ANTIPHON_API
// (default http://localhost:17202) and ANTIPHON_TASK_TOKEN are read here; --repository defaults to the checkout this CLI was
// built in. Exit 0 every fact read; 2 partial facts (the problems in the output) or usage. ASCII only.
using System;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class StatusSnapshotCommand
    {
        internal const string Name = "status-snapshot";

        internal const string Usage = StatusSnapshotOptions.Usage;

        internal static int Run(string[] args)
        {
            var options = StatusSnapshotOptions.Parse(args.Skip(1).ToArray(), Environment.GetEnvironmentVariable("ANTIPHON_API"),
                Environment.GetEnvironmentVariable("ANTIPHON_TASK_TOKEN"), Checkout() ?? Environment.CurrentDirectory);
            return StatusSnapshot.Run(options, NightlyHost.Production(Console.Out, Console.Error), new FileSystem(),
                NightlyReport.HttpTransport(timeout: StatusSnapshot.Deadline), StatusSnapshot.ReadGit);
        }

        // The checkout this executable was built in (its output lives under it).
        private static string Checkout()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
                if (Directory.Exists(Path.Combine(directory.FullName, "scripts", "lib", "Antiphon.Scripts"))) return directory.FullName;
            return null;
        }
    }
}