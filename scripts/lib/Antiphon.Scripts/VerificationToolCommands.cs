// The verification tools (unit 33 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md, wave W10):
// test-duration-tripwire (it replaced scripts/test-duration-tripwire.ps1), mechanical-pc (the five check snippets of
// docs/mechanical-pc-contract.md), measure-session-state-cache (scripts/measure-session-state-cache.ps1) and
// verify-agent-tui-profile (scripts/verify-agent-tui-profile.ps1), with the scripts' parameters in kebab case, and (unit 35)
// check-os-witnesses (scripts/check-os-witnesses.ps1). The behaviour is Antiphon.Nightly's (DurationTripwire, MechanicalPc,
// SessionStateCacheMeasure, AgentTuiProfileSmoke, OsWitnessCheck on its native git) on the production host and the real file
// system. This file reads the environment and passes it in: ANTIPHON_API (default
// http://localhost:17202) and ANTIPHON_TASK_TOKEN for the session-state reads, the system temp path for the smoke's evidence,
// and the checkout the CLI was built in. It binds the outside reads: an HTTP transport (15 s per read for the measurement,
// 100 s for the smoke), a process's CPU and start time, a process's command line (the qualification seams' reader) and the
// platform. Tokens are sent as headers and never printed. ASCII only.
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class VerificationToolCommands
    {
        internal const string DurationTripwire = "test-duration-tripwire";
        internal const string MechanicalPc = "mechanical-pc";
        internal const string MeasureSessionStateCache = "measure-session-state-cache";
        internal const string VerifyAgentTuiProfile = "verify-agent-tui-profile";
        internal const string CheckOsWitnesses = "check-os-witnesses";

        internal const string Usage = DurationTripwireOptions.Usage + " | " + MechanicalPcOptions.Usage + " | " + SessionStateCacheOptions.Usage
            + " | " + AgentTuiProfileSmokeOptions.Usage + " | " + OsWitnessCheckOptions.Usage;

        private static string Checkout => SourceCheckout.Root() ?? Environment.CurrentDirectory;

        private static NightlyHost Host() => NightlyHost.Production(Console.Out, Console.Error);

        // CARD-0475: exit 0 clean; 1 an unlisted slow or unresolved row; 2 invalid input or usage.
        internal static int RunDurationTripwire(string[] args) =>
            Antiphon.Nightly.DurationTripwire.Run(DurationTripwireOptions.Parse(args.Skip(1).ToArray(), Checkout), Host(), new FileSystem());

        // CARD-0479: exit 0 C479 ACCEPT; 2 C479 REFUSE <reason> or usage; 1 an input that could not be read.
        internal static int RunMechanicalPc(string[] args) =>
            Antiphon.Nightly.MechanicalPc.Run(MechanicalPcOptions.Parse(args.Skip(1).ToArray()), Host(), new FileSystem());

        // CARD-0701: exit 0 captured or both gates passed; 1 a gate failed or the run was refused; 2 usage.
        internal static int RunMeasureSessionStateCache(string[] args) =>
            SessionStateCacheMeasure.Run(SessionStateCacheOptions.Parse(args.Skip(1).ToArray(), Environment.GetEnvironmentVariable("ANTIPHON_API"),
                    Environment.GetEnvironmentVariable("ANTIPHON_TASK_TOKEN"), Checkout), Host(), new FileSystem(),
                new SessionStateCacheSeams
                {
                    Http = NightlyReport.HttpTransport(timeout: TimeSpan.FromSeconds(15)),
                    Process = ProcessSample,
                    IsWindows = OperatingSystem.IsWindows(),
                });

        // Exit 0 verified; 1 failed; 2 usage.
        internal static int RunVerifyAgentTuiProfile(string[] args)
        {
            var host = Host();
            return AgentTuiProfileSmoke.Run(AgentTuiProfileSmokeOptions.Parse(args.Skip(1).ToArray(), Path.GetTempPath()), host, new FileSystem(),
                new AgentTuiProfileSmokeSeams
                {
                    Http = NightlyReport.HttpTransport(timeout: TimeSpan.FromSeconds(100)),
                    CommandLine = pid => host.Qualification?.CommandLine(pid),
                    IsWindows = OperatingSystem.IsWindows(),
                });
        }

        // CARD-1526: exit 0 complete syntax/inventory; 1 missing or contradictory obligations; 2 invalid or unreadable input, or usage.
        internal static int RunCheckOsWitnesses(string[] args) =>
            OsWitnessCheck.Run(OsWitnessCheckOptions.Parse(args.Skip(1).ToArray()), Host(), new FileSystem(), OsWitnessCheck.NativeGit);

        // Get-Process -Id: the CPU time, start and working set; null when there is no such process.
        private static SessionStateProcessSample ProcessSample(int pid)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                return new SessionStateProcessSample(process.TotalProcessorTime.TotalSeconds, process.StartTime.ToUniversalTime(), process.WorkingSet64);
            }
            catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is System.ComponentModel.Win32Exception)
            {
                return null;
            }
        }
    }
}
