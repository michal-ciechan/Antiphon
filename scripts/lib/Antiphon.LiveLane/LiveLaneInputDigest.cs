// The live lane's protected-input pin (CARD-1545), moved from scripts/test-live-lane-run.ps1 (ProtectedInputs,
// Get-ProtectedInputDigest, Read-Census, the sentinels and $script:PinnedInputDigest; W2 slice S21, the driver is
// deleted). These inputs are the census the paid wrapper-only manifest was commissioned against. A change to any of them
// must be a deliberate, reviewed act: the same change sets Pinned to the value printed by
//   scripts/antiphon live-lane-input-digest          (scripts\antiphon.cmd live-lane-input-digest on Windows)
// LiveLaneRunnerTests.WrapperOnlyPaidManifest checks the pin (its full-inputs case). The digest is over
// newline-normalised text, so LF and CRLF checkouts agree. The policy reader and the census are compiled, so the pin
// carries their C# sources (NightlyPolicy.cs, ExecutionPolicy.cs, DiscoveryIndex.cs, DiagnosticLog.cs, DiagScan.cs and
// NightlyCore.cs: the class-name, exclusion-index and census code) and no PowerShell. The census itself is read by the
// caller's reader (the nightly core's policy and census, bound by the CLI): this project references no nightly code.
// Plain C#, framework only. ASCII only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Antiphon.LiveLane
{
    /// <summary>A discovery node the census read adds to the root's own discovery (the Read-Census sentinels).</summary>
    public sealed class LiveLaneSentinel
    {
        public LiveLaneSentinel(string uid, string className, string method, params string[] categories)
        {
            Uid = uid;
            ClassName = className;
            Method = method;
            Categories = categories ?? Array.Empty<string>();
        }

        public string Uid { get; }
        public string ClassName { get; }
        public string Method { get; }
        public IReadOnlyList<string> Categories { get; }
    }

    /// <summary>One census read: the policy hash, and the required and excluded uids (each sorted ordinally).</summary>
    public sealed class LiveLaneInputCensus
    {
        public LiveLaneInputCensus(string hash, IEnumerable<string> required, IEnumerable<string> excluded)
        {
            Hash = hash ?? "";
            Required = (required ?? Array.Empty<string>()).OrderBy(uid => uid, StringComparer.Ordinal).ToArray();
            Excluded = (excluded ?? Array.Empty<string>()).OrderBy(uid => uid, StringComparer.Ordinal).ToArray();
        }

        public string Hash { get; }
        public IReadOnlyList<string> Required { get; }
        public IReadOnlyList<string> Excluded { get; }

        /// <summary>The compact record {"Hash":..,"Required":[..],"Excluded":[..]}; two reads agree when these agree.</summary>
        public string ToJson()
        {
            var required = new JsonArray();
            foreach (var uid in Required) required.Add(uid);
            var excluded = new JsonArray();
            foreach (var uid in Excluded) excluded.Add(uid);
            return new JsonObject { ["Hash"] = Hash, ["Required"] = required, ["Excluded"] = excluded }.ToJsonString();
        }
    }

    /// <summary>
    /// Reads the census of a root's protected inputs (its execution policy and its probe discovery) with the reader's own
    /// compiled policy and census code, the sentinels added to the discovered nodes: the rc profile, suite antiphon, Linux.
    /// </summary>
    public delegate LiveLaneInputCensus LiveLaneCensusReader(string root, IReadOnlyList<LiveLaneSentinel> sentinels);

    /// <summary>A full check of a root's inputs: the digest against the pin and the census sentinels.</summary>
    public sealed class LiveLaneInputCheck
    {
        public string Digest { get; set; }
        public bool PinMatches { get; set; }
        public LiveLaneInputCensus Census { get; set; }
        public bool SentinelsHold { get; set; }
        public bool Ok => PinMatches && SentinelsHold;
    }

    /// <summary>The pin owner.</summary>
    public static class LiveLaneInputDigest
    {
        /// <summary>
        /// The deliberate pin. Never this commit's own blobs: a clean checkout always matches its own commit, so a change
        /// to an input is caught only against this value. Unit 62 of the 2026-11-22 remaining-ps1 plan (owner's approval of
        /// re-keying, 2026-11-26) moved it twice: f6d19136 to 214add01 when tests/test-execution-policy.json re-pinned the
        /// selection catalog (its imported-helper edge moved from the deleted lib/nightly-common.ps1 to lib/antiphon-cli.ps1),
        /// then to 4798d5a0 when NightlyCore.cs's provenance comment stopped naming the deleted nightly-core.ps1 loader. Unit 64
        /// (live-lane re-keys owner-approved) moved it from 4798d5a0 to c63422c3: tests/test-execution-policy.json lost the three
        /// Linux-only rows of LinuxServerRestartGateTests, Server2ProductionServerContractTests and C1744G1DesktopWinComposeTests,
        /// whose witnesses now run their commands in process, and its policy hash was resealed. Unit 65 moved it from 906e5c35
        /// to 214ae333: tests/test-execution-policy.json lost the Windows row of DeployServer2ApiDefaultTests (it runs the
        /// deploy-server2 command in process now) and tests/test-selection-policy.json's P-DOCKER set lost the deleted
        /// scripts/deploy-server2.ps1; both hashes were resealed. Unit 66 moved it from 214ae333 to 7276a6e6:
        /// tests/test-execution-policy.json lost the two Windows rows of VolumeRetirementHostProofTests (the retirement coordinator
        /// and the desktop origin proof run in process now, C1609Coordinator and C1609HostProof), and its policy hash was resealed.
        /// The server2 disk repair adds Windows exclusions for its two native Linux fixture classes and reseals the policy.
        /// </summary>
        public const string Pinned = "5b0655f3917372f70993e34d2de18a8388d8b10890d2daf8ac234629f17aa1aa";

        /// <summary>The policy the census reads, under a root.</summary>
        public const string PolicyPath = "tests/test-execution-policy.json";

        /// <summary>The probe discovery the census reads, under a root.</summary>
        public const string DiscoveryPath = "scripts/fixtures/nightly/c487-probe/discovery.diag";

        /// <summary>The protected inputs, in digest order (repository-relative, forward slashes).</summary>
        public static readonly IReadOnlyList<string> ProtectedInputs = new[]
        {
            "scripts/lib/Antiphon.Nightly/NightlyPolicy.cs", "scripts/lib/Antiphon.Nightly/ExecutionPolicy.cs",
            "scripts/lib/Antiphon.Nightly/DiscoveryIndex.cs", "scripts/lib/Antiphon.Nightly/DiagnosticLog.cs", "scripts/lib/Antiphon.Nightly/DiagScan.cs",
            "scripts/lib/Antiphon.Nightly/NightlyCore.cs",
            PolicyPath, DiscoveryPath
        };

        /// <summary>
        /// The census sentinels: the C680 live nudge canary and an ordinary class must be required; the live adapter
        /// round trip must be excluded. Membership in the right set, never a name anywhere in the census. Unit 60 moved
        /// the canary into tests/Antiphon.LiveClaude.Tests, which no rc suite runs; its sentinel keeps the rc policy from
        /// excluding the class by name, so a reviewer's C680 exclusion still fails the census as well as the pin.
        /// </summary>
        public static readonly IReadOnlyList<LiveLaneSentinel> Sentinels = new[]
        {
            new LiveLaneSentinel("sentinel-c680", "Antiphon.LiveClaude.Tests.ClaudeDialogWatchdogCanaryTests", "C680_LiveNudgeRefusesPermissionAndQuestionDialogs", "Integration"),
            new LiveLaneSentinel("sentinel-ordinary", "Antiphon.Tests.Ordinary", "Plain", "Unit"),
            new LiveLaneSentinel("sentinel-excluded", "Antiphon.Tests.Agents.ClaudeAdapterIntegrationTests", "RoundTrip", "Integration")
        };

        /// <summary>ConvertTo-LfText: CRLF and lone CR become LF; null is empty.</summary>
        public static string LfText(string text) => text == null ? "" : text.Replace("\r\n", "\n").Replace("\r", "\n");

        /// <summary>
        /// Get-ProtectedInputDigest: SHA-256 (lower-case hex) over each input's path, LF, its LF text and NUL, in order;
        /// "" when an input is missing under root.
        /// </summary>
        public static string Digest(string root)
        {
            var text = new StringBuilder();
            foreach (var path in ProtectedInputs)
            {
                var actual = Path.Combine(root ?? "", path);
                if (!File.Exists(actual)) return "";
                text.Append(path).Append('\n').Append(LfText(File.ReadAllText(actual))).Append('\0');
            }
            return LiveLaneValues.Sha256Hex(text.ToString());
        }

        /// <summary>Test-ProtectedInputs: the root's inputs are the pinned ones.</summary>
        public static bool MatchesPin(string root) => Digest(root) == Pinned;

        /// <summary>Test-CensusSentinels.</summary>
        public static bool SentinelsHold(LiveLaneInputCensus census)
        {
            if (census == null) return false;
            var required = census.Required;
            var excluded = census.Excluded;
            return required.Contains("sentinel-c680", StringComparer.Ordinal) && required.Contains("sentinel-ordinary", StringComparer.Ordinal)
                && excluded.Contains("sentinel-excluded", StringComparer.Ordinal) && !excluded.Contains("sentinel-c680", StringComparer.Ordinal)
                && !required.Contains("sentinel-excluded", StringComparer.Ordinal);
        }

        /// <summary>Read-Census: the root's census through the reader, with the sentinels.</summary>
        public static LiveLaneInputCensus Census(string root, LiveLaneCensusReader reader) => reader(root, Sentinels);

        /// <summary>The digest against the pin, then the census sentinels (read even when the pin differs).</summary>
        public static LiveLaneInputCheck Check(string root, LiveLaneCensusReader reader)
        {
            var digest = Digest(root);
            var census = digest.Length == 0 ? null : Census(root, reader);
            return new LiveLaneInputCheck { Digest = digest, PinMatches = digest == Pinned, Census = census, SentinelsHold = SentinelsHold(census) };
        }
    }

    /// <summary>live-lane-input-digest's flags.</summary>
    public sealed class LiveLaneInputDigestOptions
    {
        public const string Usage = "live-lane-input-digest [--root <dir>] [--check]";

        /// <summary>The checkout whose inputs are hashed (default: the CLI's own checkout).</summary>
        public string Root { get; private set; }

        /// <summary>Also compare with the pin and read the census sentinels; exit 1 when either fails.</summary>
        public bool Check { get; private set; }

        public static LiveLaneInputDigestOptions Parse(IReadOnlyList<string> arguments, string defaultRoot)
        {
            var values = LiveLaneArgs.Parse(arguments, new[] { "--root" }, new[] { "--check" });
            var root = values.Value("--root");
            if (root != null && root.Length == 0) throw new LiveLaneUsageException("missing value for --root");
            return new LiveLaneInputDigestOptions { Root = root ?? defaultRoot ?? Environment.CurrentDirectory, Check = values.Switch("--check") };
        }
    }

    /// <summary>
    /// antiphon-scripts live-lane-input-digest: the digest of the root's protected inputs on stdout, one line, as
    /// test-live-lane-run.ps1 -PrintInputDigest printed it (empty when an input is missing); exit 0. With --check it also
    /// compares the digest with LiveLaneInputDigest.Pinned and reads the census sentinels: exit 0 when both hold, 1
    /// otherwise with one stderr line saying which.
    /// </summary>
    public static class LiveLaneInputDigestCommand
    {
        public const string Name = "live-lane-input-digest";

        public static int Run(LiveLaneInputDigestOptions options, LiveLaneCensusReader reader, TextWriter output, TextWriter error)
        {
            if (!options.Check)
            {
                output.WriteLine(LiveLaneInputDigest.Digest(options.Root));
                return 0;
            }
            var check = LiveLaneInputDigest.Check(options.Root, reader);
            output.WriteLine(check.Digest);
            if (check.Ok) return 0;
            if (check.Digest.Length == 0)
                error.WriteLine("live-lane-input-digest: a protected input is missing under " + options.Root);
            else if (!check.PinMatches)
                error.WriteLine("live-lane-input-digest: " + check.Digest + " is not the pin " + LiveLaneInputDigest.Pinned
                    + "; if this census or policy input change is deliberate and reviewed, set LiveLaneInputDigest.Pinned in"
                    + " scripts/lib/Antiphon.LiveLane/LiveLaneInputDigest.cs to it");
            else
                error.WriteLine("live-lane-input-digest: the census sentinels do not hold (C680 and ordinary required, the live adapter excluded)");
            return 1;
        }
    }
}
