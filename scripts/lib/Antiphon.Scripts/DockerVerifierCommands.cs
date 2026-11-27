// The W8 verifiers (unit 47 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md, W8 slice B), with the scripts'
// parameters in kebab case: verify-docker-stack (the stub boundaries and literal guards of scripts/verify-docker-stack.ps1 and,
// since unit 66, its head and the live server2 bridge of scripts/c590-real.ps1; the .ps1 is a thin launcher for the one-off that
// still starts it by path, c590-real.ps1's CARD-0590 entry), test-docker (scripts/test-docker.ps1), test-docker-container (scripts/test-docker-container.ps1)
// and verify-card0604-dind-runner (scripts/verify-card0604-dind-runner.ps1); nothing started the last three by path, so none keeps
// a launcher. Unit 48 added verify-card0660-codex-image (scripts/verify-card0660-codex-image.ps1, with the owner's approval to
// delete its fixture scripts/fixtures/c913-image-wrapper.ps1; nothing else started it by path, so no launcher either). The
// behaviour is Antiphon.Nightly's (DockerStackVerify, DockerTestEntry, DockerTestContainer, DindRunnerHarness,
// CodexImageQualification) on the production host and the real file system. This file passes in what the core may not read
// itself: the checkout the CLI was built in (the e2e-staged-apphost consumer, c590-remote.sh, the harnesses' build context and
// evidence root, the image qualification's init and probe scripts), the environment the live throwaway entry's child inherits,
// and the harness's HTTP. ANTIPHON_C590_STUB and ANTIPHON_C590_SINK are read through the host's variables. ASCII only.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class DockerVerifierCommands
    {
        internal const string VerifyDockerStack = "verify-docker-stack";
        internal const string TestDocker = "test-docker";
        internal const string TestDockerContainer = "test-docker-container";
        internal const string VerifyDindRunner = "verify-card0604-dind-runner";
        internal const string VerifyCodexImage = "verify-card0660-codex-image";
        internal const string HostProof = "c1609-host-proof";

        internal const string Usage = DockerStackVerifyOptions.Usage + " | " + DockerTestOptions.Usage + " | " + DockerTestContainerOptions.Usage
            + " | " + DindRunnerOptions.Usage + " | " + CodexImageOptions.Usage + " | " + C1609HostProofOptions.Usage;

        private static string Checkout => SourceCheckout.Root() ?? Environment.CurrentDirectory;

        private static NightlyHost Host() => NightlyHost.Production(Console.Out, Console.Error);

        // CARD-0590: exit 0 accepted, 2 refused (the c590 result written) or usage, the manifest's runExit for failure-summary or the
        // remote result's exit for a live case, 1 the head refused, the manifest could not be read or a command could not start.
        // Unit 66: a live case (no ANTIPHON_C590_STUB) runs the server2 bridge here, on this console (C590BridgeOs) and the bounded
        // children of its retirement coordinator (C1609BoundedProcesses).
        internal static int RunVerifyDockerStack(string[] args)
        {
            var files = new FileSystem();
            return DockerStackVerify.Run(DockerStackVerifyOptions.Parse(args.Skip(1).ToArray(), Checkout), Host(), files, new C590BridgeSeams
            {
                World = new C590BridgeOs(Console.Out, Console.Error),
                Processes = new C1609BoundedProcesses(files),
            });
        }

        // CARD-0590: exit 0 accepted, 2 refused or usage, the manifest's childExit, or c590-remote.sh's code with --throwaway-stack.
        internal static int RunTestDocker(string[] args)
        {
            var environment = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables()) environment[(string)entry.Key] = (string)entry.Value;
            return DockerTestEntry.Run(DockerTestOptions.Parse(args.Skip(1).ToArray(), Checkout, environment), Host(), new FileSystem());
        }

        // CARD-0590: exit 0 roster-ok or checkpoint-launched, 2 roster errors or usage, 1 a file could not be read.
        internal static int RunTestDockerContainer(string[] args) =>
            DockerTestContainer.Run(DockerTestContainerOptions.Parse(args.Skip(1).ToArray()), Host(), new FileSystem());

        // CARD-0604: exit 0 every observation ok, 1 one or more not, 2 setup failed or usage.
        internal static int RunVerifyDindRunner(string[] args) =>
            DindRunnerHarness.Run(DindRunnerOptions.Parse(args.Skip(1).ToArray(), Checkout), Host(), new FileSystem(),
                new DindRunnerSeams { Http = DindRunnerHarness.Http() });

        // CARD-1609 (unit 66): the desktop origin proof's write and live-check verbs for the native retirement harness (exit 0 proven, 1
        // refused with one stderr line, 2 usage), on the production children (git found on PATH) and the real file system.
        internal static int RunHostProof(string[] args)
        {
            var host = Host();
            return C1609HostProofCommand.Run(C1609HostProofOptions.Parse(args.Skip(1).ToArray()), host, C1609HostProofCommand.Production(host, new FileSystem()));
        }

        // CARD-0660 V-9: exit 0 every applicable row ok, 1 one or more not, 2 setup failed or usage.
        internal static int RunVerifyCodexImage(string[] args) =>
            CodexImageQualification.Run(CodexImageOptions.Parse(args.Skip(1).ToArray(), Checkout), Host(), new FileSystem(), new CodexImageSeams());
    }
}
