// Observe the remote master ref with git ls-remote; never fetch or change a checkout (the rc qualification skill's master
// watcher). It replaced scripts/orchestration/watch-master.ps1 (unit 25 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md); the CLI runs it as `antiphon-scripts watch-master` from a
// Monitor task (.claude/skills/antiphon-rc-qualification/SKILL.md).
//
//   watch-master --from <40-hex sha> [--repository <dir>] [--remote origin] [--poll-seconds 30] [--max-minutes 28] [--once]
//
// --from is the previously observed full master SHA, not a claim that a later movement contains a particular fix. Each poll
// runs `git ls-remote <remote> refs/heads/master` in --repository (the CLI's default: the checkout it was built in) on the
// host's git seam, which reads stdout and stderr together, so a warning on stderr is not fatal: git is judged by its exit
// code and the ref line. A different SHA (compared ignoring case) prints "MASTER MOVED <sha>" and ends the watch; a failed
// call or an answer without a master ref prints "MASTER-WATCH-ERROR <reason>" and the watch goes on. Between polls it
// sleeps (the host's Sleep, clamped to the window) until --max-minutes have passed, or stops after one poll with --once,
// and always ends with "master watch done; re-arm if still waiting". Exit 0. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The watch-master command's options (watch-master.ps1 -From -Repository -Remote -PollSeconds -MaxMinutes -Once).</summary>
    public sealed class WatchMasterOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "watch-master --from <sha> [--repository <dir>] [--remote origin] [--poll-seconds 30] [--max-minutes 28] [--once]";

        private static readonly Regex FullSha = new Regex("^[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant);

        /// <summary>The previously observed full master SHA.</summary>
        public string From { get; init; } = "";

        /// <summary>The checkout git runs in.</summary>
        public string Repository { get; init; } = "";

        /// <summary>The remote whose master is read.</summary>
        public string Remote { get; init; } = "origin";

        /// <summary>Seconds between polls, 1 to 300.</summary>
        public int PollSeconds { get; init; } = 30;

        /// <summary>The watch window in minutes, 1 to 29 (under the Monitor's 30-minute timeout).</summary>
        public int MaxMinutes { get; init; } = 28;

        /// <summary>One poll, then stop.</summary>
        public bool Once { get; init; }

        /// <summary>
        /// The arguments after the command name; defaultRepository is the CLI's checkout (null: none). A --from that is not 40
        /// hex digits, a value out of range, or no repository is a NightlyUsageException.
        /// </summary>
        public static WatchMasterOptions Parse(IReadOnlyList<string> arguments, string defaultRepository)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--from", "--repository", "--remote", "--poll-seconds", "--max-minutes" }, new[] { "--once" });
            var from = flags.Required("--from");
            if (!FullSha.IsMatch(from)) throw new NightlyUsageException("--from expects a full 40-hex SHA, not '" + from + "'");
            var repository = flags.Value("--repository", "");
            if (string.IsNullOrEmpty(repository)) repository = defaultRepository ?? "";
            if (repository.Length == 0) throw new NightlyUsageException("missing --repository (no checkout found to default to)");
            var remote = flags.Value("--remote", "");
            return new WatchMasterOptions
            {
                From = from,
                Repository = repository,
                Remote = remote.Length == 0 ? "origin" : remote,
                PollSeconds = Ranged(flags, "--poll-seconds", 30, 1, 300),
                MaxMinutes = Ranged(flags, "--max-minutes", 28, 1, 29),
                Once = flags.Switch("--once"),
            };
        }

        internal static int Ranged(NightlyArgs flags, string flag, int fallback, int minimum, int maximum)
        {
            var value = flags.Int(flag, fallback);
            if (value < minimum || value > maximum)
                throw new NightlyUsageException(flag + " expects " + minimum.ToString(CultureInfo.InvariantCulture) + " to "
                    + maximum.ToString(CultureInfo.InvariantCulture) + ", not " + value.ToString(CultureInfo.InvariantCulture));
            return value;
        }
    }

    /// <summary>The remote master watcher (watch-master.ps1).</summary>
    public static class WatchMaster
    {
        private static readonly Regex MasterRef = new Regex(@"^([0-9a-fA-F]{40})\s+refs/heads/master$", RegexOptions.CultureInvariant);

        /// <summary>Polls the remote master on host.Out until it moves, the window ends or --once; always 0.</summary>
        public static int Run(WatchMasterOptions options, NightlyHost host)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            var deadline = host.UtcNow.AddMinutes(options.MaxMinutes);
            do
            {
                var moved = Poll(options, host);
                if (moved != null)
                {
                    host.Out.WriteLine("MASTER MOVED " + moved);
                    break;
                }
                if (options.Once || host.UtcNow >= deadline) break;
                var remaining = Math.Max(0.0, Math.Floor((deadline - host.UtcNow).TotalMilliseconds));
                if (remaining > 0) host.Sleep(TimeSpan.FromMilliseconds(Math.Min(options.PollSeconds * 1000.0, remaining)));
            }
            while (host.UtcNow < deadline);
            host.Out.WriteLine("master watch done; re-arm if still waiting");
            return 0;
        }

        // One ls-remote: the new SHA when master moved, null when it did not or the poll failed (its error line printed).
        private static string Poll(WatchMasterOptions options, NightlyHost host)
        {
            NightlyGitResult result;
            try
            {
                result = host.RunGit(options.Repository, "ls-remote", options.Remote, "refs/heads/master");
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is InvalidOperationException || e is System.IO.IOException)
            {
                host.Out.WriteLine("MASTER-WATCH-ERROR " + e.Message);
                return null;
            }
            if (result.ExitCode != 0)
            {
                host.Out.WriteLine("MASTER-WATCH-ERROR git ls-remote failed.");
                return null;
            }
            string sha = null;
            foreach (var line in result.Output.Split('\n'))
            {
                var match = MasterRef.Match(line.TrimEnd('\r'));
                if (match.Success) sha = match.Groups[1].Value;
            }
            if (sha == null)
            {
                host.Out.WriteLine("MASTER-WATCH-ERROR Remote returned no valid master ref.");
                return null;
            }
            return string.Equals(sha, options.From, StringComparison.OrdinalIgnoreCase) ? null : sha;
        }
    }
}
