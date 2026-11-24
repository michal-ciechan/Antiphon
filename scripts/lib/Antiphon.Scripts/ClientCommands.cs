// The client commands (unit 32 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md): client-mode (it replaced
// scripts/client-mode.ps1), lint-client and typecheck-client (scripts/lint-client.ps1, scripts/typecheck-client.ps1 and
// their scripts/lib/client-npm-install.ps1) and test-client (the body of scripts/test-client.ps1, which stays as the thin
// launcher scripts/c590-remote.sh runs by path; the nightly's client chunk runs this command). The behaviour is
// Antiphon.Nightly's ClientMode, ClientLint, ClientTypecheck and ClientTests on the production host, the real file system,
// the production tool runner (node and npm) and, for client-mode, an HTTP transport with a 5 s deadline. Each command's
// checkout (its client/ and logs/) is the one this executable was built in, else the current directory; test-client takes
// --repo-root. ASCII only.
using System;
using System.IO.Abstractions;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class ClientCommands
    {
        internal const string ClientModeCommand = "client-mode";
        internal const string LintClient = "lint-client";
        internal const string TypecheckClient = "typecheck-client";
        internal const string TestClient = "test-client";

        internal const string Usage = ClientModeOptions.Usage + " | " + ClientLintOptions.Usage + " | " + ClientTypecheckOptions.Usage + " | " + ClientTestsOptions.Usage;

        private static string Checkout => SourceCheckout.Root() ?? Environment.CurrentDirectory;

        private static NightlyHost Host() => NightlyHost.Production(Console.Out, Console.Error);

        // Exit 0 done; 1 the port did not show the mode in time (without --status) or the state file is not JSON; usage 2.
        internal static int RunClientMode(string[] args) =>
            ClientMode.Run(ClientModeOptions.Parse(args.Skip(1).ToArray(), Checkout), Host(), new FileSystem(), NightlyReport.HttpTransport(timeout: ClientMode.ProbeTimeout));

        // Exit eslint's code (1 when it is not installed or the file is missing); usage 2.
        internal static int RunLintClient(string[] args) =>
            ClientLint.Run(ClientLintOptions.Parse(args.Skip(1).ToArray(), Checkout), Host(), new FileSystem(), ClientTools.RunProcess);

        // Exit the gate code (2 a source diagnostic; 1 when tsc is not installed); usage 2.
        internal static int RunTypecheckClient(string[] args) =>
            ClientTypecheck.Run(ClientTypecheckOptions.Parse(args.Skip(1).ToArray(), Checkout), Host(), new FileSystem(), ClientTools.RunProcess);

        // Exit the first failing gate's code, else Vitest's (1 when vitest is not installed); usage 2.
        internal static int RunTestClient(string[] args) =>
            ClientTests.Run(ClientTestsOptions.Parse(args.Skip(1).ToArray(), Checkout), Host(), new FileSystem(), ClientTools.RunProcess);
    }
}
