// The session reapers (units 22 and 38 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md). cleanup-claude-sessions
// (Antiphon.Nightly ClaudeSessionCleanup) is what scripts/cleanup-claude-sessions.ps1 launches for the Windmill schedule
// u/lndcobra/claude_session_cleanup; reap-zombie-agents (ZombieAgentReaper) replaced scripts/reap-zombie-agents.ps1, which
// nothing scheduled; reap-orphaned-pty-hosts (PtyHostReaper, an operator tool: a dry run unless --execute, a kill only through
// the runner) replaced scripts/reap-orphaned-pty-hosts.ps1, which nothing started by path. This file reads the environment and passes it in: USERPROFILE (else the user profile folder) for
// ~/.claude, and the checkout this executable was built in (else the current directory) for the logs/ report directories.
// ASCII only.
using System;
using System.IO.Abstractions;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class SessionReaperCommands
    {
        internal const string ClaudeSessions = "cleanup-claude-sessions";
        internal const string ZombieAgents = "reap-zombie-agents";
        internal const string OrphanedPtyHosts = "reap-orphaned-pty-hosts";

        internal const string Usage = ClaudeSessionCleanupOptions.Usage + " | " + ZombieAgentReaperOptions.Usage + " | " + PtyHostReaperOptions.Usage;

        // Exit 0 done; 1 the list could not be read; 2 no or stale credentials (or usage); 3 truncated list; 4 over --max-per-run;
        // 5 a mutation failed.
        internal static int RunClaudeSessions(string[] args)
        {
            var options = ClaudeSessionCleanupOptions.Parse(args.Skip(1).ToArray(), UserProfile(), SourceCheckout.Root() ?? Environment.CurrentDirectory);
            var host = NightlyHost.Production(Console.Out, Console.Error);
            return ClaudeSessionCleanup.Run(options, host, new FileSystem(), NightlyReport.HttpTransport(), ClaudeSessionCleanup.ProcessName).ExitCode;
        }

        // Exit 0 no positives or every kill verified; 1 a leaf still alive; 2 a prerequisite did not answer (or usage); 3 a dry run
        // found positives.
        internal static int RunZombieAgents(string[] args)
        {
            var options = ZombieAgentReaperOptions.Parse(args.Skip(1).ToArray(), UserProfile(), SourceCheckout.Root() ?? Environment.CurrentDirectory);
            var host = NightlyHost.Production(Console.Out, Console.Error);
            return ZombieAgentReaper.Run(options, host, new FileSystem(), ZombieAgentReaper.Production(host)).ExitCode;
        }

        // CARD-0204: exit 0 a dry run, or every kill verified; 1 a kill request failed or a host is still alive; 2 a prerequisite
        // could not be read (or usage).
        internal static int RunOrphanedPtyHosts(string[] args)
        {
            var options = PtyHostReaperOptions.Parse(args.Skip(1).ToArray());
            var host = NightlyHost.Production(Console.Out, Console.Error);
            return PtyHostReaper.Run(options, host, new FileSystem(), PtyHostReaper.Production(host)).ExitCode;
        }

        private static string UserProfile()
        {
            var profile = Environment.GetEnvironmentVariable("USERPROFILE");
            return string.IsNullOrEmpty(profile) ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : profile;
        }
    }
}
