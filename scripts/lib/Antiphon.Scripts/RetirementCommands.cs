// The W10 retirement and cache-check entries (unit 67 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md, W10 part 2),
// with the scripts' parameters in kebab case: verify-card0849-caches (scripts/verify-card0849-caches.ps1, the CARD-0849 front door
// every rolling upgrade runs), retire-runner-volumes (scripts/retire-runner-volumes.ps1 with scripts/c1609-manual.ps1 and
// scripts/lib/runner-operator-token.ps1: the CARD-1609 audited Close and the manual Preview/Apply) and c1609-read-packet
// (scripts/c1609-read-packet.ps1, for the W10 node harnesses). The behaviour is Antiphon.Nightly's (C849CacheFrontDoor,
// RetireRunnerVolumes, C1609ReadPacket) on the production host, the real file system and RetireRunnerVolumesOs; the stack verifier
// runs in this process with the live server2 bridge (C590BridgeOs, C1609BoundedProcesses), as the verify-docker-stack command
// does. This file passes in the checkout the CLI was built in (the scripts' repository). Nothing starts these by path, so none
// keeps a launcher. ASCII only.
using System;
using System.IO.Abstractions;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class RetirementCommands
    {
        internal const string VerifyCaches = "verify-card0849-caches";
        internal const string RetireVolumes = "retire-runner-volumes";
        internal const string ReadPacket = "c1609-read-packet";

        internal const string Usage = C849CacheFrontDoorOptions.Usage + " | " + RetireRunnerVolumesOptions.Usage + " | " + C1609ReadPacketOptions.Usage;

        private static string Checkout => SourceCheckout.Root() ?? Environment.CurrentDirectory;

        private static (NightlyHost Host, FileSystem Files, RetireRunnerVolumesOs World) Production()
        {
            var host = NightlyHost.Production(Console.Out, Console.Error);
            var files = new FileSystem();
            var bridge = new C590BridgeSeams { World = new C590BridgeOs(Console.Out, Console.Error), Processes = new C1609BoundedProcesses(files) };
            return (host, files, new RetireRunnerVolumesOs(host, files, Checkout, bridge));
        }

        // CARD-0849: exit 0 the case's receipts accepted (its summary and C849_RECEIPT printed), 1 refused (one stderr line), 2 usage.
        internal static int RunVerifyCaches(string[] args)
        {
            var options = C849CacheFrontDoorOptions.Parse(args.Skip(1).ToArray(), Checkout);
            var (host, files, world) = Production();
            return C849CacheFrontDoor.Run(options, host, files, new C849CacheFrontDoorSeams
            {
                Verify = world.Verify,
                GitHead = repo => RetireRunnerVolumesOs.GitHeadLines(Console.Error, repo),
            });
        }

        // CARD-1609: exit 0 closed or the manual phase done, 2 refused (one TempRetirementCloseRefused or TempRetirementManualRefused line)
        // or usage, 1 a thrown error.
        internal static int RunRetireVolumes(string[] args)
        {
            var options = RetireRunnerVolumesOptions.Parse(args.Skip(1).ToArray(), Checkout);
            var (host, files, world) = Production();
            return RetireRunnerVolumes.Run(options, host, files, world);
        }

        // CARD-1609: exit 0 the packet (or with --send its answer) printed, 1 a refused packet, 2 a refused send or usage.
        internal static int RunReadPacket(string[] args)
        {
            var options = C1609ReadPacketOptions.Parse(args.Skip(1).ToArray(), Checkout);
            var (host, files, world) = Production();
            return C1609ReadPacket.Run(options, host, files, world);
        }
    }
}
