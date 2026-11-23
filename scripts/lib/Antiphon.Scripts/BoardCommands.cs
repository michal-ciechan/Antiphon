// The board API commands (unit 24 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md): capability,
// model-availability, specialist-routing, pipeline-definition, project, schedule, dispatch-concurrency and routing-pin, and
// card (unit 27). Each replaced the scripts/<name>.ps1 of the same name (deleted 2026-11-23), with the same flags in kebab
// case and the verb (and a schedule id, a project or a card) first, as the scripts took them. The behaviour is
// Antiphon.Nightly's (CapabilityCli, ModelAvailabilityCli, SpecialistRoutingCli, PipelineDefinitionCli, ProjectCli,
// ScheduleCli, DispatchConcurrencyCli, RoutingPinCli, CardCli) on the production host and an HTTP transport; this file reads
// the environment and passes it in: ANTIPHON_API (default http://localhost:17202), ANTIPHON_TASK_TOKEN, ANTIPHON_AGENT_ID
// (the schedule's default agent), ANTIPHON_CAPABILITY_STORE (else %LOCALAPPDATA%\Antiphon\capabilities) and, for card, the
// working directory. The capability store is written with DPAPI on Windows only. Tokens are sent as headers and never
// printed. ASCII only.
using System;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class BoardCommands
    {
        internal const string Capability = "capability";
        internal const string ModelAvailability = "model-availability";
        internal const string SpecialistRouting = "specialist-routing";
        internal const string PipelineDefinition = "pipeline-definition";
        internal const string Project = "project";
        internal const string Schedule = "schedule";
        internal const string DispatchConcurrency = "dispatch-concurrency";
        internal const string RoutingPin = "routing-pin";
        internal const string Card = "card";

        internal const string Usage = CapabilityCliOptions.Usage + " | " + ModelAvailabilityCliOptions.Usage + " | " + SpecialistRoutingCliOptions.Usage
            + " | " + PipelineDefinitionCliOptions.Usage + " | " + ProjectCliOptions.Usage + " | " + ScheduleCliOptions.Usage + " | " + DispatchConcurrencyCliOptions.Usage
            + " | " + RoutingPinCliOptions.Usage + " | " + CardCliOptions.Usage;

        private static string Api => Environment.GetEnvironmentVariable("ANTIPHON_API");

        private static string TaskToken => Environment.GetEnvironmentVariable("ANTIPHON_TASK_TOKEN");

        private static NightlyHost Host() => NightlyHost.Production(Console.Out, Console.Error);

        private static NightlyReportHttp Http() => NightlyReport.HttpTransport(timeout: BoardCli.Deadline);

        // CARD-0398: exit 0 done; 1 a failed request, an unknown or ambiguous name, no token, or issue/rotate off Windows.
        internal static int RunCapability(string[] args) =>
            CapabilityCli.Run(CapabilityCliOptions.Parse(args.Skip(1).ToArray(), Api, CapabilityStore()), Host(), new FileSystem(), Http(),
                OperatingSystem.IsWindows() ? new Func<byte[], byte[]>(CapabilityCli.ProtectCurrentUser) : null);

        // CARD-0309: exit 0 done; 1 a naive --until or a failed request.
        internal static int RunModelAvailability(string[] args) =>
            ModelAvailabilityCli.Run(ModelAvailabilityCliOptions.Parse(args.Skip(1).ToArray(), Api, TaskToken), Host(), Http());

        // CARD-0415: exit 0 done; 1 an invalid candidate, no declared chain or a failed request.
        internal static int RunSpecialistRouting(string[] args) =>
            SpecialistRoutingCli.Run(SpecialistRoutingCliOptions.Parse(args.Skip(1).ToArray(), Api, TaskToken), Host(), Http());

        // Exit 0 done; 1 a missing or malformed stages file or a failed request.
        internal static int RunPipelineDefinition(string[] args) =>
            PipelineDefinitionCli.Run(PipelineDefinitionCliOptions.Parse(args.Skip(1).ToArray(), Api, TaskToken), Host(), new FileSystem(), Http());

        // Exit 0 done (setup notes are warnings); 1 an unresolvable project or profile, a missing prompt file or a failed request.
        internal static int RunProject(string[] args) =>
            ProjectCli.Run(ProjectCliOptions.Parse(args.Skip(1).ToArray(), Api, TaskToken), Host(), new FileSystem(), Http());

        // CARD-0057: exit 0 done; 1 a refused value, an unacknowledged spend or a failed request.
        internal static int RunSchedule(string[] args) =>
            ScheduleCli.Run(ScheduleCliOptions.Parse(args.Skip(1).ToArray(), Api, TaskToken, Environment.GetEnvironmentVariable("ANTIPHON_AGENT_ID")), Host(),
                new FileSystem(), Http());

        // CARD-0505: exit 0 done; 1 a refused value or a failed request (a 409 is not retried).
        internal static int RunDispatchConcurrency(string[] args) =>
            DispatchConcurrencyCli.Run(DispatchConcurrencyCliOptions.Parse(args.Skip(1).ToArray(), Api, TaskToken), Host(), new FileSystem(), Http());

        // CARD-0305: exit 0 done; 1 a refused value or a failed request.
        internal static int RunRoutingPin(string[] args) =>
            RoutingPinCli.Run(RoutingPinCliOptions.Parse(args.Skip(1).ToArray(), Api, TaskToken), Host(), Http());

        // CARD-0051: exit 0 done; 1 a refused value, a missing file or a failed request (the server's detail on stderr).
        internal static int RunCard(string[] args) =>
            CardCli.Run(CardCliOptions.Parse(args.Skip(1).ToArray(), Api, TaskToken, Environment.CurrentDirectory), Host(), new FileSystem(), Http());

        // ANTIPHON_CAPABILITY_STORE, else where delegate.ps1 -Capability reads it: %LOCALAPPDATA%\Antiphon\capabilities.
        internal static string CapabilityStore()
        {
            var configured = Environment.GetEnvironmentVariable("ANTIPHON_CAPABILITY_STORE");
            if (!string.IsNullOrWhiteSpace(configured)) return configured;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Antiphon", "capabilities");
        }
    }
}
