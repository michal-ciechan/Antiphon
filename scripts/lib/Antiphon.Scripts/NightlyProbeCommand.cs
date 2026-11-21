// CARD-1375: antiphon-scripts nightly-probe-capture --probe-directory <dir> --results-directory <dir>
// --probe-build-source-sha <40 hex>. Captures TimeoutProbe and CancelledProbe of a built c1375-probe directory
// (bin-c1375-probe, obj/project.assets.json) into a fresh results directory, one CAPTURE line per probe
// (Antiphon.Nightly.NightlyProbeSupervisor.Capture). Linux only. Exit 0 captured, 1 refused (one stderr line), 2 usage.
using System;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class NightlyProbeCommand
    {
        internal const string Name = "nightly-probe-capture";

        internal const string Usage =
            "nightly-probe-capture --probe-directory <dir> --results-directory <dir> --probe-build-source-sha <40 hex>";

        internal static int Run(string[] args)
        {
            var flags = Program.Flags(args, new[] { "--probe-directory", "--results-directory", "--probe-build-source-sha" }, new string[0]);
            string F(string name) => flags != null && flags.TryGetValue(name, out var value) && value.Length > 0 ? value : null;
            if (F("--probe-directory") == null || F("--results-directory") == null || F("--probe-build-source-sha") == null)
            {
                Console.Error.WriteLine("usage: antiphon-scripts " + Usage);
                return Program.Usage;
            }
            try
            {
                NightlyProbeSupervisor.Capture(F("--probe-directory"), F("--results-directory"), F("--probe-build-source-sha"), Console.Out);
                return Program.Handled;
            }
            catch (Exception e)
            {
                var message = e is NightlyProbeRefusal ? e.Message : e.GetType().Name + ": " + e.Message;
                Console.Error.WriteLine(message.Replace("\r", " ").Replace("\n", " "));
                return Program.Refused;
            }
        }
    }
}
