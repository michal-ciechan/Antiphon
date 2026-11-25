// The CARD-1405 Postgres snapshot commands (unit 53 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md, W8 slice D),
// with the scripts' parameters in kebab case:
// - postgres-snapshot: the snapshot and the restore drill (Antiphon.Nightly's PostgresSnapshot). scripts/postgres-snapshot.ps1 is its
//   launcher: the Windmill job's runner resolves it by path and the cutover runbooks and deploy-server2-postgres.ps1 name it.
// - postgres-snapshot-job and postgres-snapshot-stale: the Windmill jobs u/lndcobra/antiphon_postgres_snapshot and _stale
//   (PostgresSnapshotJob), started by the runner copies of scripts/windmill/invoke-postgres-snapshot.ps1 and
//   invoke-postgres-snapshot-stale.ps1.
// - postgres-snapshot-target: Set-SnapshotTarget, the only writer of the runner's snapshot-target word.
// - job-status: scripts/windmill/job-status.ps1 run as a script.
// Each runs on the production host (this process's console and environment) and the real file system. ASCII only.
using System;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class PostgresSnapshotCommands
    {
        internal const string SnapshotName = "postgres-snapshot";

        internal const string JobName = "postgres-snapshot-job";

        internal const string StaleName = "postgres-snapshot-stale";

        internal const string TargetName = "postgres-snapshot-target";

        internal const string JobStatusName = "job-status";

        internal const string TargetUsage = "postgres-snapshot-target --word <desktop|server2> [--runner-dir <dir>]";

        internal const string Usage = PostgresSnapshotOptions.Usage + " | " + PostgresSnapshotJobOptions.Usage + " | " + PostgresSnapshotStaleOptions.Usage + " | " + TargetUsage
            + " | " + JobStatusOptions.Usage;

        private static NightlyHost Host => NightlyHost.Production(Console.Out, Console.Error);

        // The reason code of the result line (0 plan or ok, 2-9 a refusal or a failed step), 1 an unexpected failure; usage 2.
        internal static int RunSnapshot(string[] args) => PostgresSnapshot.Run(PostgresSnapshotOptions.Parse(args.Skip(1).ToArray()), Host);

        // The child's exit, 9 target-invalid, 1 missing-script, or the send's exit when the child passed; usage 2.
        internal static int RunJob(string[] args) => PostgresSnapshotJob.Run(PostgresSnapshotJobOptions.Parse(args.Skip(1).ToArray(), Environment.CurrentDirectory), Host);

        // 0 fresh, else the send's exit after the STALE line; usage 2.
        internal static int RunStale(string[] args) => PostgresSnapshotJob.Stale(PostgresSnapshotStaleOptions.Parse(args.Skip(1).ToArray()), Host);

        // 0 written and read back (SNAPSHOT TARGET <word>), 1 refused or a mismatch; usage 2.
        internal static int RunTarget(string[] args)
        {
            var flags = NightlyArgs.Parse(args.Skip(1).ToArray(), new[] { "--word", "--runner-dir" }, Array.Empty<string>());
            var dir = flags.Value("--runner-dir", "");
            if (string.IsNullOrWhiteSpace(dir)) dir = Environment.CurrentDirectory;
            return PostgresSnapshotJob.WriteTarget(flags.Value("--word", ""), dir, Console.Out) == "" ? 1 : 0;
        }

        // 0 the line (or fresh), 2 bad-record, or the send's exit with --send; usage 2.
        internal static int RunJobStatus(string[] args) => JobStatusOptions.Run(JobStatusOptions.Parse(args.Skip(1).ToArray()), Host);

    }
}
