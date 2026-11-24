// The W8 read-only and static tools (unit 46 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md, W8 slice A):
// check-server-docker-inputs (it replaced scripts/check-server-docker-inputs.ps1 and its driver; the CI job runs
// scripts/antiphon check-server-docker-inputs), pg-qps (scripts/pg-qps.ps1) and postgres-jit-window
// (scripts/postgres-jit-window.ps1), with the scripts' parameters in kebab case. Nothing starts any of them by path, so none
// keeps a launcher. The behaviour is Antiphon.Nightly's (ServerDockerInputs, PgQps on DesktopPostgres, PostgresJitWindow) on the
// production host and the real file system. This file passes in the checkout the CLI was built in (the repository the guard
// reads by default, and the one pg-qps reads its SQL from) and binds pg-qps's psql child (PgQps.Psql). ASCII only.
using System;
using System.IO.Abstractions;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class InfrastructureToolCommands
    {
        internal const string CheckServerDockerInputs = "check-server-docker-inputs";
        internal const string PgQpsName = "pg-qps";
        internal const string PostgresJitWindowName = "postgres-jit-window";

        internal const string Usage = ServerDockerInputsOptions.Usage + " | " + PgQpsOptions.Usage + " | " + PostgresJitWindowOptions.Usage;

        private static string Checkout => SourceCheckout.Root() ?? Environment.CurrentDirectory;

        private static NightlyHost Host() => NightlyHost.Production(Console.Out, Console.Error);

        // CARD-1586: exit 0 every linked input copied; 1 an input missing; 2 an error or usage.
        internal static int RunCheckServerDockerInputs(string[] args) =>
            ServerDockerInputs.Run(ServerDockerInputsOptions.Parse(args.Skip(1).ToArray(), Checkout), Host(), new FileSystem());

        // CARD-1987: exit 0 done; 2 bad arguments or usage; 3 unreachable or no extension; 4 partial; 1 the signal file could not be written.
        internal static int RunPgQps(string[] args) =>
            PgQps.Run(PgQpsOptions.Parse(args.Skip(1).ToArray(), Checkout), Host(), new FileSystem(), new PgQpsSeams { Psql = PgQps.Psql });

        // CARD-1886: exit 0 the plan or the window written; 1 a query, a sample or a write failed; 2 usage.
        internal static int RunPostgresJitWindow(string[] args) =>
            PostgresJitWindow.Run(PostgresJitWindowOptions.Parse(args.Skip(1).ToArray()), Host(), new FileSystem());
    }
}
