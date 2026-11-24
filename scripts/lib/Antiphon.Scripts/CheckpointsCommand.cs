// Unit 20 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: antiphon-scripts checkpoints <verb> [arguments...],
// the checkpoint tool (tools/Antiphon.Checkpoints) built from this checkout and run as a child with the arguments as given
// (Antiphon.Nightly.CheckpointTool). scripts/run-checkpoint.ps1 hands its row to `checkpoints row` and
// scripts/validate-checkpoint-receipt.ps1 its receipt to `checkpoints validate`. This file reads what the core may not: the
// checkout this executable was built in (else the current directory's), the current directory and the environment the
// tool's build starts from. Ctrl+C is the tool's to answer (exit 130 after it releases its slot), so this process waits
// for it instead of ending first. Exit: the tool's code; 9 the tool could not be built or started; 2 usage. ASCII only.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class CheckpointsCommand
    {
        internal const string Name = "checkpoints";

        internal const string Usage = CheckpointToolOptions.Usage;

        internal static int Run(string[] args)
        {
            var current = Environment.CurrentDirectory;
            var environment = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
                environment[(string)entry.Key] = (string)entry.Value;
            var options = CheckpointToolOptions.Parse(args.Skip(1).ToArray(), SourceCheckout.Root() ?? current, current, environment);
            Console.CancelKeyPress += (_, signal) => signal.Cancel = true;
            return CheckpointTool.Run(options, NightlyHost.Production(Console.Out, Console.Error), new FileSystem());
        }
    }
}
