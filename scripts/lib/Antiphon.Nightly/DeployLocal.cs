// The desktop production deploy (unit 49 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md, W8 slice C; it
// replaced scripts/deploy-local.ps1, which nothing started by path: the orchestrator's deploy step, docs/orchestration-loop.md,
// runs scripts\antiphon.cmd deploy-local). The CLI runs it as
//
//   antiphon-scripts deploy-local [--no-build] [--timeout-sec 150] [--allow-worktree] [--report] [--pg-container <name>]
//       [--pg-user antiphon] [--pg-database antiphon]
//
// It is the orchestrator-triggered machine-global canonical-stack deploy, never an isolated linked-worktree validation, and it
// composes restart-apphost instead of reproducing its lock, teardown or health wait. In order, as the script:
//
// 1. Without --report, the worktree guard (AppHostWorktree, CARD-0273) before anything else: a linked or unverifiable worktree
//    is "DEPLOY VERDICT: refused <the guard's lines on one line>" and exit 3 (no Docker call, restart, verification or query);
//    --allow-worktree lets a linked worktree through after the guard's warning lines.
// 2. The PostgreSQL target (DesktopPostgres): the AppHost user secret ConnectionStrings:DefaultConnection pinned to the running
//    server2 container that publishes it, or --pg-container (a local or test container in the current Docker context, with
//    --pg-user and --pg-database; the script's name check first).
// 3. Without --report, the restart-apphost command (--no-build, --timeout-sec, --allow-worktree), then the verify-dev-stack
//    command (--skip-browser --timeout-sec 30), each in this process (DeployLocalSteps); a non-zero exit stops the deploy.
// 4. Every source migration (server/Migrations/*.cs except AppDbContextModelSnapshot.cs and *.Designer.cs, not hidden) must be
//    in the database's __EFMigrationsHistory and every recorded one in the source (read-only query); then "EF migrations
//    verified: <n> source and database history entries.".
//
// The last line is always the deploy result: "DEPLOY VERDICT: ok" (exit 0), "DEPLOY VERDICT: failed <detail>" (exit 1: the
// first failure's message, whitespace runs collapsed) or "DEPLOY VERDICT: refused <detail>" (exit 3). Usage 2 (the CLI). git,
// docker and the lines are the host's, the migrations IFileSystem's. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The deploy-local command's options (deploy-local.ps1's parameters in kebab case).</summary>
    public sealed class DeployLocalOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "deploy-local [--no-build] [--timeout-sec 150] [--allow-worktree] [--report] [--pg-container <name>] [--pg-user antiphon] "
            + "[--pg-database antiphon]";

        private static readonly string[] Valued = { "--timeout-sec", "--pg-container", "--pg-user", "--pg-database" };
        private static readonly string[] Switches = { "--no-build", "--allow-worktree", "--report" };

        /// <summary>Pass --no-build to restart-apphost.</summary>
        public bool NoBuild { get; init; }

        /// <summary>restart-apphost's health wait (its --timeout-sec).</summary>
        public int TimeoutSec { get; init; } = 150;

        /// <summary>Intentionally allow a linked worktree to control the shared local stack.</summary>
        public bool AllowWorktree { get; init; }

        /// <summary>Compare the source migrations with the database without the guard, the restart or the verification.</summary>
        public bool Report { get; init; }

        /// <summary>An explicit local or test Docker container instead of the AppHost database; empty: the AppHost database.</summary>
        public string PgContainer { get; init; } = "";

        /// <summary>The PostgreSQL user in PgContainer.</summary>
        public string PgUser { get; init; } = "antiphon";

        /// <summary>The PostgreSQL database in PgContainer.</summary>
        public string PgDatabase { get; init; } = "antiphon";

        /// <summary>The checkout (its worktree, its migrations, its stack).</summary>
        public string RepoRoot { get; init; } = "";

        /// <summary>The flags after the command name; the checkout comes from the CLI.</summary>
        public static DeployLocalOptions Parse(IReadOnlyList<string> arguments, string repoRoot)
        {
            var flags = NightlyArgs.Parse(arguments, Valued, Switches);
            return new DeployLocalOptions
            {
                NoBuild = flags.Switch("--no-build"),
                TimeoutSec = flags.Int("--timeout-sec", 150),
                AllowWorktree = flags.Switch("--allow-worktree"),
                Report = flags.Switch("--report"),
                PgContainer = flags.Value("--pg-container", "") ?? "",
                PgUser = flags.Value("--pg-user", "antiphon") ?? "",
                PgDatabase = flags.Value("--pg-database", "antiphon") ?? "",
                RepoRoot = repoRoot ?? "",
            };
        }
    }

    /// <summary>The deploy's steps that are other commands, run in process on the host (the CLI binds the production AppHost platform).</summary>
    public sealed class DeployLocalSteps
    {
        /// <summary>The restart-apphost command with these options; its exit code.</summary>
        public Func<AppHostRestartOptions, NightlyHost, int> RestartAppHost { get; init; }

        /// <summary>The verify-dev-stack command with these options; its exit code.</summary>
        public Func<DevStackVerifyOptions, NightlyHost, int> VerifyDevStack { get; init; }
    }

    /// <summary>The desktop deploy (deploy-local.ps1's body; see the file comment).</summary>
    public static class DeployLocal
    {
        /// <summary>Deployed: restarted, verified and every migration recorded (or, with --report, compared).</summary>
        public const int Ok = 0;

        /// <summary>A step failed.</summary>
        public const int Failed = 1;

        /// <summary>A linked or unverifiable worktree without --allow-worktree; nothing was touched.</summary>
        public const int Refused = 3;

        /// <summary>The prefix of the last line.</summary>
        public const string VerdictPrefix = "DEPLOY VERDICT: ";

        /// <summary>The read-only history query.</summary>
        public const string HistorySql = "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\";";

        /// <summary>verify-dev-stack's flags: no browser smoke, and a readiness probe longer than its interactive default.</summary>
        public static readonly IReadOnlyList<string> VerifyArguments = new[] { "--skip-browser", "--timeout-sec", "30" };

        // Resolve-DesktopPostgresTarget's name check of an explicit container.
        private static readonly Regex ContainerName = new Regex("^[A-Za-z0-9][A-Za-z0-9_.-]*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.CultureInvariant);

        /// <summary>restart-apphost's flags, as deploy-local.ps1 passed them to restart-apphost.ps1 (-TimeoutSec always).</summary>
        public static IReadOnlyList<string> RestartArguments(DeployLocalOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            var arguments = new List<string>();
            if (options.NoBuild) arguments.Add("--no-build");
            arguments.Add("--timeout-sec");
            arguments.Add(options.TimeoutSec.ToString(CultureInfo.InvariantCulture));
            if (options.AllowWorktree) arguments.Add("--allow-worktree");
            return arguments;
        }

        /// <summary>The deploy; the exit code (see the file comment). Its last line is the verdict.</summary>
        public static int Run(DeployLocalOptions options, NightlyHost host, IFileSystem files, DeployLocalSteps steps)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (steps == null) throw new ArgumentNullException(nameof(steps));
            var root = options.RepoRoot;

            // CARD-0273: refuse before any side effect.
            if (!options.Report)
            {
                var worktree = AppHostWorktree.Classify(host, files, root);
                if (!worktree.Verified || (!worktree.IsMainWorktree && !options.AllowWorktree))
                {
                    host.Out.WriteLine(VerdictPrefix + "refused " + string.Join(" ", worktree.GuardMessage(false)));
                    return Refused;
                }
                if (!worktree.IsMainWorktree)
                    foreach (var line in worktree.GuardMessage(true)) host.Out.WriteLine(line);
            }

            string failure = null;
            try
            {
                var target = Resolve(host, options);
                if (!options.Report)
                {
                    Step("restart-apphost", steps.RestartAppHost(AppHostRestartOptions.Parse(RestartArguments(options), root), host));
                    // The built client can still be finishing its first post-restart build after restart-apphost has confirmed
                    // the backend, so the readiness probe gets longer than verify-dev-stack's interactive default.
                    Step("verify-dev-stack", steps.VerifyDevStack(DevStackVerifyOptions.Parse(VerifyArguments, root), host));
                }

                var source = SourceMigrationIds(files, files.Path.Combine(root, "server", "Migrations"));
                var recorded = RecordedMigrationIds(target.Query(HistorySql));
                var pending = source.Where(id => !recorded.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList();
                var unknown = recorded.Where(id => !source.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList();
                if (pending.Count > 0 || unknown.Count > 0)
                {
                    var parts = new List<string>();
                    if (pending.Count > 0) parts.Add("pending migrations: " + string.Join(", ", pending));
                    if (unknown.Count > 0) parts.Add("database-only migrations: " + string.Join(", ", unknown));
                    throw new InvalidOperationException(string.Join("; ", parts));
                }

                host.Out.WriteLine("EF migrations verified: " + source.Count.ToString(CultureInfo.InvariantCulture) + " source and database history entries.");
            }
            catch (Exception e)
            {
                // The script caught every failure in its try: the last line is always the verdict.
                failure = Detail(e);
            }

            if (failure != null)
            {
                host.Out.WriteLine(VerdictPrefix + "failed " + failure);
                return Failed;
            }
            host.Out.WriteLine(VerdictPrefix + "ok");
            return Ok;
        }

        // Resolve-DesktopPostgresTarget: an explicit container must be a plain name or ID; otherwise the AppHost database.
        private static DesktopPostgres Resolve(NightlyHost host, DeployLocalOptions options)
        {
            var container = options.PgContainer ?? "";
            if (container.Length > 0 && !ContainerName.IsMatch(container))
                throw new InvalidOperationException("Specify a PostgreSQL container name or ID.");
            return DesktopPostgres.Resolve(host, container, options.PgUser, options.PgDatabase);
        }

        // Invoke-ChildPowerShell's verdict on a step: a non-zero exit stops the deploy.
        private static void Step(string name, int exitCode)
        {
            if (exitCode != 0) throw new InvalidOperationException(name + " exited " + exitCode.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Get-SourceMigrationIds: the base names of the directory's *.cs files that are not hidden, without
        /// AppDbContextModelSnapshot.cs and *.Designer.cs, sorted and unique ignoring case (Sort-Object -Unique).
        /// </summary>
        public static IReadOnlyList<string> SourceMigrationIds(IFileSystem files, string directory)
        {
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (!files.Directory.Exists(directory)) throw new InvalidOperationException("Migration directory not found: " + directory);
            var comparer = StringComparer.CurrentCultureIgnoreCase;
            return files.Directory.EnumerateFiles(directory, "*.cs")
                .Where(path => (files.File.GetAttributes(path) & FileAttributes.Hidden) == 0)
                .Select(path => files.Path.GetFileName(path))
                .Where(name => !string.Equals(name, "AppDbContextModelSnapshot.cs", StringComparison.OrdinalIgnoreCase)
                    && !name.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase))
                .Select(name => files.Path.GetFileNameWithoutExtension(name))
                .Distinct(comparer)
                .OrderBy(id => id, comparer)
                .ToList();
        }

        /// <summary>Get-RecordedMigrationIds: the query's lines, trimmed, without empty ones, in the database's order.</summary>
        public static IReadOnlyList<string> RecordedMigrationIds(string rows) =>
            Regex.Split(rows ?? "", "\r?\n").Select(row => row.Trim()).Where(row => row.Length > 0).ToList();

        /// <summary>Format-FailureDetail: the message on one line; "unknown error" when it is blank.</summary>
        public static string Detail(Exception error)
        {
            var detail = Whitespace.Replace(error?.Message ?? "", " ");
            return string.IsNullOrWhiteSpace(detail) ? "unknown error" : detail.Trim();
        }
    }
}
