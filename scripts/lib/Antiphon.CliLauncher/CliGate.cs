// N0 (unit 57): the check antiphon-scripts makes before it dispatches a command, when a launcher asked for it.
//
// scripts/antiphon, scripts/antiphon.cmd and scripts/lib/antiphon-cli.ps1 run the published build that
// bin-launcher/antiphon-scripts/current names, with ANTIPHON_CLI_CHECK set to the checkout: one dotnet process, no msbuild.
// Enter, first in Main:
//  - without the variable nothing is checked (a test, a child, a direct dotnet exec); the command runs here;
//  - with it, the variable is removed from this process (its children never inherit it) and the checkout's inputs are
//    compared with this build's manifest (CliIdentity.Matches). The same: the command runs here;
//  - different, or this is not a published build: CliPublisher.Ensure publishes the current inputs' build (or finds it)
//    under the build lock, and this process runs it as a child with the same arguments, console, directory and
//    environment, waits, and exits with its exit code. Interrupts are the child's: this process only waits.
// No exit code is reserved by the protocol, so none can collide with a command's own (build-slot passes its command's
// code through). Exit 9 with one stderr line is the launchers' own: the CLI could not be built or started. A process that
// runs from a published folder holds that folder's lease while it lives (CliCache.HoldLease). ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Antiphon.CliLauncher
{
    /// <summary>What Enter reads and does; Production is the real process.</summary>
    public sealed class CliGateContext
    {
        /// <summary>The variable's value: the checkout to check against; null or empty when no launcher asked.</summary>
        public string Request { get; init; }

        /// <summary>Removes the variable from this process.</summary>
        public Action ClearRequest { get; init; } = () => { };

        /// <summary>The folder this CLI runs from.</summary>
        public string BaseDirectory { get; init; } = "";

        /// <summary>How the current build is published.</summary>
        public CliPublishOptions Publish { get; init; } = new CliPublishOptions();

        /// <summary>Runs the current build's DLL with the arguments and returns its exit code.</summary>
        public Func<string, IReadOnlyList<string>, int> RunChild { get; init; } = CliGate.RunChild;

        /// <summary>Holds the lease of the folder this process runs from.</summary>
        public Action<string> HoldLease { get; init; } = CliCache.HoldLease;

        /// <summary>The real process: its environment, base directory, console and clock.</summary>
        public static CliGateContext Production() => new CliGateContext
        {
            Request = Environment.GetEnvironmentVariable(CliGate.Variable),
            ClearRequest = () => Environment.SetEnvironmentVariable(CliGate.Variable, null),
            BaseDirectory = AppContext.BaseDirectory,
        };
    }

    /// <summary>The launch check: run here when current, else publish and run the current build as a child.</summary>
    public static class CliGate
    {
        /// <summary>Set by the launchers to the checkout: check this build against it before any command runs.</summary>
        public const string Variable = "ANTIPHON_CLI_CHECK";

        /// <summary>The real process's check. Null: run the command here. A number: exit with it.</summary>
        public static int? Enter(string[] args) => Enter(args, CliGateContext.Production());

        /// <summary>The check against a context. Null: run the command here. A number: exit with it.</summary>
        public static int? Enter(string[] args, CliGateContext context)
        {
            if (string.IsNullOrEmpty(context.Request))
            {
                context.HoldLease(context.BaseDirectory);
                return null;
            }
            context.ClearRequest();
            var error = context.Publish.Error;
            string checkout;
            try { checkout = Checkout(context.Request); }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException)
            {
                error.WriteLine("antiphon-cli: " + CliGate.Variable + " names no checkout: " + context.Request);
                return CliPublisher.Unavailable;
            }
            try
            {
                var stamp = CliCache.Stamp(context.BaseDirectory);
                if (stamp != null && CliIdentity.Matches(CliInputs.Discover(checkout), stamp))
                {
                    context.HoldLease(context.BaseDirectory);
                    return null;
                }
            }
            catch (CliInputException e)
            {
                error.WriteLine("antiphon-cli: " + e.Message);
                return CliPublisher.Unavailable;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                error.WriteLine("antiphon-cli: cannot read the CLI's inputs in " + checkout + ": " + e.Message.Replace("\r", " ").Replace("\n", " "));
                return CliPublisher.Unavailable;
            }
            // This process keeps running from its folder while the current build runs.
            context.HoldLease(context.BaseDirectory);
            var dll = CliPublisher.Ensure(checkout, context.Publish);
            if (dll == null) return CliPublisher.Unavailable;
            try
            {
                return context.RunChild(dll, args);
            }
            catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is IOException)
            {
                error.WriteLine("antiphon-cli: cannot start the CLI (dotnet exec " + dll + "): " + e.Message.Replace("\r", " ").Replace("\n", " "));
                return CliPublisher.Unavailable;
            }
        }

        /// <summary>The variable's checkout as a full path; Git Bash's /c/... form becomes C:/... on Windows.</summary>
        public static string Checkout(string value)
        {
            if (OperatingSystem.IsWindows() && value.Length >= 2 && value[0] == '/' && char.IsAsciiLetter(value[1]) && (value.Length == 2 || value[2] == '/'))
                value = char.ToUpperInvariant(value[1]) + ":" + (value.Length > 2 ? value.Substring(2) : "/");
            return Path.GetFullPath(value);
        }

        /// <summary>
        /// Production's child: dotnet exec of the DLL with the arguments as given, on this console, in this directory and
        /// environment. This process ignores interrupts (the child gets them too) and, on Linux and macOS, passes SIGTERM on.
        /// </summary>
        public static int RunChild(string dll, IReadOnlyList<string> args)
        {
            var start = new ProcessStartInfo(CliPublisher.DotnetHost()) { UseShellExecute = false };
            start.ArgumentList.Add("exec");
            start.ArgumentList.Add(dll);
            foreach (var argument in args) start.ArgumentList.Add(argument);
            start.Environment.Remove(Variable);
            Process child = null;
            // Ctrl+C and Ctrl+Break (SIGINT and SIGQUIT) reach the child as well; this process waits for its exit code.
            ConsoleCancelEventHandler interrupt = (_, e) => e.Cancel = true;
            Console.CancelKeyPress += interrupt;
            using var terminate = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
            {
                context.Cancel = true;
                if (child != null) Forward(child.Id);
            });
            try
            {
                child = Process.Start(start) ?? throw new InvalidOperationException("dotnet did not start");
                using (child)
                {
                    child.WaitForExit();
                    return child.ExitCode;
                }
            }
            finally
            {
                Console.CancelKeyPress -= interrupt;
            }
        }

        // SIGTERM on to the child (kill -TERM); best effort.
        private static void Forward(int pid)
        {
            try
            {
                var kill = new ProcessStartInfo("kill") { UseShellExecute = false };
                kill.ArgumentList.Add("-TERM");
                kill.ArgumentList.Add(pid.ToString(System.Globalization.CultureInfo.InvariantCulture));
                using var process = Process.Start(kill);
                process?.WaitForExit(5000);
            }
            catch (Exception e) when (e is Win32Exception || e is InvalidOperationException) { }
        }
    }
}
