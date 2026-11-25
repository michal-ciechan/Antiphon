// The CARD-1405 Postgres snapshot commands (unit 53 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md, W8 slice D),
// with the scripts' parameters in kebab case:
// - postgres-snapshot: the snapshot and the restore drill (Antiphon.Nightly's PostgresSnapshot). scripts/postgres-snapshot.ps1 is its
//   launcher: the Windmill job's runner resolves it by path and the cutover runbooks and deploy-server2-postgres.ps1 name it.
// Each runs on the production host (this process's console and environment) and the real file system. ASCII only.
using System;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class PostgresSnapshotCommands
    {
        internal const string SnapshotName = "postgres-snapshot";

        internal const string Usage = PostgresSnapshotOptions.Usage;

        private static NightlyHost Host => NightlyHost.Production(Console.Out, Console.Error);

        // The reason code of the result line (0 plan or ok, 2-9 a refusal or a failed step), 1 an unexpected failure; usage 2.
        internal static int RunSnapshot(string[] args) => PostgresSnapshot.Run(PostgresSnapshotOptions.Parse(args.Skip(1).ToArray()), Host);
    }
}