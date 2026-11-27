// CARD-0590 stack verifier: the literal checkpoints and guards of scripts/verify-docker-stack.ps1 (unit 47 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md, W8 slice B). The CLI runs it as
//
//   antiphon-scripts verify-docker-stack --case <name> --manifest <path>
//
// scripts/verify-docker-stack.ps1 stays at its path with its parameters (-Case, -Manifest, -RefreshClaudeToken): the owner-gated
// deploy-server2 command (Server2RollingOs.HostCase, unit 65) and scripts c590-real.ps1, c1609-manual.ps1 and verify-card0849-caches.ps1 start it by path. It keeps its head
// unchanged: the manifest checks and context guards of scripts/c590-real.ps1 (C994, C1008) and, without ANTIPHON_C590_STUB, the
// live server2 bridge (Invoke-C590LiveCase) for every case on c590-real.ps1's roster, which always ends the script. Whatever
// reaches past that head (stub mode, or a case the bridge does not route) is handed to this command.
//
// Each case answers as the script's switch did: a refusal is accepted=false, its diagnosis and exit 2; acceptance exit 0 (the
// failure-summary row exits the manifest's runExit). The commands a case records go through C590Boundary (recorded in the sink;
// started only without the stub). Cases match ignoring case, as the PowerShell switch did; manifest keys match exactly. A case
// with no arm is UnknownCase in stub mode, else "RealCasePending <case>". Run directly without the stub, a case on the live
// roster (LiveCases, kept equal to c590-real.ps1's by DockerStackVerifyTests) is a usage error: only the launcher's bridge runs
// it. Exit 1: the manifest could not be read, a value could not be cast, or a command could not start (one stderr line).
// Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The verify-docker-stack command's options (verify-docker-stack.ps1's -Case and -Manifest).</summary>
    public sealed class DockerStackVerifyOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "verify-docker-stack --case <name> --manifest <path>";

        /// <summary>The checkpoint or guard.</summary>
        public string Case { get; init; } = "";

        /// <summary>The manifest file.</summary>
        public string Manifest { get; init; } = "";

        /// <summary>The checkout the e2e-staged-apphost case reads its consumer from.</summary>
        public string Checkout { get; init; } = "";

        /// <summary>The arguments after the command name; checkout is the CLI's own.</summary>
        public static DockerStackVerifyOptions Parse(IReadOnlyList<string> arguments, string checkout)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--case", "--manifest" }, Array.Empty<string>());
            return new DockerStackVerifyOptions { Case = flags.Required("--case"), Manifest = flags.Required("--manifest"), Checkout = checkout ?? "" };
        }
    }

    /// <summary>The stub boundaries and literal guards of the CARD-0590 stack verifier.</summary>
    public static class DockerStackVerify
    {
        /// <summary>
        /// The cases scripts/c590-real.ps1 routes to server2 ($script:C590LiveCases, in its order). Without the stub the launcher
        /// sends these to the live bridge, so this command never answers one outside stub mode.
        /// </summary>
        public static readonly IReadOnlyList<string> LiveCases = new[]
        {
            "deploy-parent", "deploy-temp-runner", "retire-temp-runner", "retire-temp-containers", "runner-cache-inventory",
            "runner-cache-fixture", "runner-cache-seed", "runner-cache-reset", "verify-runner-caches", "verify-runner-caches-retired",
            "runner-cache-prune-preview", "runner-cache-prune", "nested-residue", "persistent-restart", "server2-independent-handoff",
            "git-credential-smoke", "baseline-build-failure", "server-image-payload", "runner-image-payload", "testing-runner-payload",
            "custody-containment", "parent-native-and-command-session", "child-server-image-payload", "child-runner-image-payload",
            "test-image-context-and-tools", "receipt-runner-image-payload", "fixture-image-payload", "deployment-state", "client-lint",
            "client-tests", "messaging-tests", "dotnet-filter", "session-result-export-and-child-cleanup", "session-denied-socket",
            "interrupted-export-cleanup", "stock-idle", "stock-busy", "insert-refused", "insert-committed-idle", "insert-committed-busy",
            "attempt-committed", "body-before-enter", "recipient-before-ingestion", "transcript-save-fails-release",
            "transcript-save-fails-restart", "receipt-before-verdict", "response-before-client", "receipt-before-manifest",
            "changed-generation", "failure-summary", "runtime-context-engine", "test-context-engine", "throwaway-all",
        };

        /// <summary>The e2e-staged-apphost consumer, relative to the checkout.</summary>
        public static readonly string[] StagedApphostConsumer = { "tests", "Antiphon.E2E", "Fixtures", "IsolatedSessionRunner.cs" };

        /// <summary>Runs one case. Returns the exit code; usage errors throw NightlyUsageException.</summary>
        public static int Run(DockerStackVerifyOptions options, NightlyHost host, IFileSystem files)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            var c590 = new C590Boundary(host, files);
            if (!c590.Stubbed && LiveCases.Contains(options.Case, StringComparer.OrdinalIgnoreCase))
                throw new NightlyUsageException(options.Case + " is a live case: scripts/verify-docker-stack.ps1 runs it through the c590-real.ps1 bridge");
            if (!files.File.Exists(options.Manifest)) throw new NightlyUsageException("Manifest is required (no file " + options.Manifest + ")");
            try
            {
                var manifest = C590Manifest.Read(files, options.Manifest);
                return Verify(options.Case, manifest, options.Checkout, c590, files);
            }
            catch (C590Failure e)
            {
                host.Error.WriteLine("verify-docker-stack: " + e.Message);
                return 1;
            }
        }

        private static int Verify(string name, JsonNode m, string checkout, C590Boundary c590, IFileSystem files)
        {
            var root = C590Manifest.Str(m, "evidenceRoot");
            int Accept(JsonObject extra = null) => c590.Result(root, true, "", 0, extra);
            int Refuse(string diagnosis, JsonObject extra = null) => c590.Result(root, false, diagnosis, 2, extra);
            string S(string field) => C590Manifest.Str(m, field);
            bool B(string field) => C590Manifest.IsTrue(m, field);
            int I(string field) => C590Manifest.Int(m, field);
            bool Ne(string first, string second) => !C590Manifest.Eq(first, second);
            bool Match(string text, string pattern) => Regex.IsMatch(text ?? "", pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            switch ((name ?? "").ToLowerInvariant())
            {
                case "version-check":
                    if (Ne(S("observedRevision"), S("sourceSha"))) return Refuse("RevisionMismatch");
                    return Accept();
                case "foreign-state":
                    if (B("foreignOwner")) return Refuse("ForeignStateOwner");
                    c590.Invoke("chown", "1654:1654");
                    c590.Invoke("docker", "compose", "up");
                    return Accept();
                case "managed-secrets":
                    var ready = C590Manifest.Eq(S("keyProtection"), "X509Certificate") && B("certificatePresent");
                    return Accept(new JsonObject { ["managedSecretReady"] = ready });
                case "parent-child":
                    if (C590Manifest.Eq(S("childProject"), S("parentProject"))) return Refuse("ParentAsChild");
                    c590.Invoke("docker", "create");
                    c590.Invoke("docker", "rm");
                    return Accept();
                case "manifest-order":
                    c590.Invoke("write-manifest", "initial");
                    c590.Invoke("docker", "create");
                    return Accept();
                case "source-sha":
                    if (Ne(S("contextSha"), S("sourceSha"))) return Refuse("SourceMismatch");
                    c590.Invoke("docker", "build");
                    return Accept();
                case "bind-source":
                    if (C590Manifest.Eq(S("bindSource"), "/work/test-evidence")) return Refuse("RunnerLocalBind");
                    return Accept();
                case "foreign-id":
                    if (Ne(S("inspectedId"), S("ownedId"))) return Refuse("ForeignResource");
                    c590.Invoke("docker", "rm", S("ownedId"));
                    return Accept();
                case "foreign-label":
                    if (Ne(S("inspectedLabel"), S("ownedLabel"))) return Refuse("ForeignLabel");
                    c590.Invoke("docker", "rm", S("ownedId"));
                    return Accept();
                case "normal-down":
                    c590.Invoke("docker", "compose", "down", "--remove-orphans");
                    return Accept();
                case "global-cleanup":
                    c590.Invoke("docker", "compose", "down");
                    return Accept();
                case "raw-challenge":
                    if (!B("rawMarker")) return Refuse("RawChallengeMissing");
                    return Accept();
                case "raw-exit":
                    if (!B("rawExit")) return Refuse("RawExitMissing");
                    return Accept();
                case "session-origin":
                    if (S("sessionId").Length == 0) return Refuse("SessionOriginMissing");
                    return Accept();
                case "child-probe":
                    return Accept();
                case "linux-custody":
                    // CARD-0604 D-17 (Cut B), V-29/R-5: the persistent runner REQUIRES verificationCustodyV1 with backend
                    // linux-cgroup-v1 and a store id; a Linux runner advertising windows-job-v1 is refused outright.
                    if (!Match(S("capabilities"), "VerificationCustodyV1")) return Refuse("CustodyNotAdvertised");
                    if (C590Manifest.Eq(S("verificationCustodyBackend"), "windows-job-v1")) return Refuse("WindowsBackendAdvertisedOnLinux");
                    if (Ne(S("verificationCustodyBackend"), "linux-cgroup-v1")) return Refuse("UnsupportedCustodyAdvertised");
                    if (S("runnerStoreId").Length == 0) return Refuse("RunnerStoreIdMissing");
                    return Accept();
                case "custody-containment":
                    // CARD-0604 S9, CP-15 / V-28 on cgroup v1: the remote's own ordered refusals before it runs the probe.
                    if (!B("runnerRunning")) return Refuse("RunnerNotRunning");
                    if (Ne(S("verificationCustodyBackend"), "linux-cgroup-v1")) return Refuse("CustodyNotAdvertised");
                    if (B("windowsBackendAdvertised")) return Refuse("WindowsBackendAdvertisedOnLinux");
                    if (!B("custodyHelpersRootOwned")) return Refuse("CustodyHelpersNotRootOwned");
                    if (Ne(S("containment"), "ok")) return Refuse("ContainmentFailed");
                    // server2 is kernel 4.15: a v2 reading means the freezer path went unmeasured.
                    if (Ne(S("cgroupVersion"), "v1")) return Refuse("ExpectedCgroupV1");
                    if (I("custodyResidue") != 0) return Refuse("CustodyResidue");
                    return Accept();
                // CARD-0604 S4 boundaries: each is the refusal the live case makes before any command.
                case "deploy-parent":
                    if (!B("deployKeyPresent")) return Refuse("DeployKeyMissing");
                    if (!B("phoneHomeSecretPresent")) return Refuse("PhoneHomeSecretMissing");
                    if (Ne(S("restartPolicy"), "unless-stopped")) return Refuse("NotPersistent");
                    if (Ne(S("daemonName"), S("runnerHostname"))) return Refuse("SiblingDaemonRefused");
                    return Accept();
                case "deploy-temp-runner":
                    if (!B("deployKeyPresent")) return Refuse("DeployKeyMissing");
                    if (!B("phoneHomeSecretPresent")) return Refuse("PhoneHomeSecretMissing");
                    if (C590Manifest.Double(m, "nestedStoreFreeGb") < 20) return Refuse("NestedStoreDiskLow");
                    if (Ne(S("daemonName"), S("runnerHostname"))) return Refuse("SiblingDaemonRefused");
                    return Accept();
                case "retire-temp-runner":
                    if (S("tempRetiredAt").Length == 0) return Refuse("TempRunnerNotRetired");
                    return Accept();
                case "session-nested-stack":
                    // The session cases launch through the production server: a runner that is not dispatch-eligible refuses here.
                    if (!B("dispatchEligible")) return Refuse("RunnerNotDispatchEligible");
                    if (S("sessionOrigin").Length == 0) return Refuse("SessionOriginMissing");
                    if (S("hostResidue").Length > 0) return Refuse("HostResidue");
                    if (S("nestedResidue").Length > 0) return Refuse("NestedResidue");
                    return Accept();
                case "await-nested-stack":
                    // A running session is not a result, and a dead session with no marker is incomplete.
                    if (C590Manifest.Eq(S("sessionState"), "Working")) return Refuse("SessionStillRunning");
                    if (!B("markerSeen")) return Refuse("SessionIncomplete");
                    foreach (var sub in C590Manifest.Items(C590Manifest.Field(m, "subordinates")))
                        if (!C590Manifest.IsTrue(sub, "accepted")) return Refuse("SubordinateFailed " + C590Manifest.Str(sub, "name"));
                    return Accept();
                case "persistent-restart":
                    if (S("storeIdBefore").Length == 0) return Refuse("LostNestedStore");
                    if (Ne(S("storeIdAfter"), S("storeIdBefore"))) return Refuse("ChangedStoreId");
                    if (!B("imagesRetained")) return Refuse("NestedImagesLost");
                    return Accept();
                case "nested-residue":
                    if (S("hostResidue").Length > 0) return Refuse("HostResidue");
                    if (S("nestedResidue").Length > 0) return Refuse("NestedResidue");
                    return Accept();
                case "session-git-smoke":
                    if (S("pushedSha").Length != 40) return Refuse("GitPushShaMissing");
                    if (!B("branchDeleted")) return Refuse("SmokeBranchNotDeleted");
                    return Accept();
                case "cleanup-residue":
                    if (S("survivingId").Length > 0) return Refuse("CleanupIncomplete", new JsonObject { ["surviving"] = S("survivingId") });
                    return Accept();
                case "interrupted-cleanup":
                    if (S("replacementId").Length > 0 && Ne(S("replacementId"), S("ownedId"))) return Refuse("IdentityChanged");
                    c590.Invoke("docker", "rm", S("ownedId"));
                    return Accept();
                case "expectation-before-post":
                    c590.Invoke("write-expectation", S("bodySha"));
                    c590.Invoke("http", "POST", "WhenIdle");
                    return Accept();
                case "unknown-ack":
                    c590.Invoke("http", "POST", "WhenIdle");
                    return Refuse("UnknownAck");
                case "live-absence":
                    if (B("requestOwnerAlive")) return Refuse("Incomplete");
                    c590.Invoke("http", "POST", "WhenIdle");
                    return Accept();
                case "refused-retry":
                    if (B("nativePrompt") || B("bodyWritten") || B("enterWritten")) return Refuse("AbsenceRequired");
                    c590.Invoke("http", "POST", "WhenIdle");
                    return Accept();
                case "crash-export":
                    c590.Invoke("export-reached", "digest");
                    c590.Invoke("docker", "kill", S("fixtureServerId"));
                    return Accept();
                case "crash-target":
                    c590.Invoke("docker", "kill", S("fixtureServerId"));
                    return Accept();
                case "crash-intent":
                    c590.Invoke("write-intent", "kill");
                    c590.Invoke("docker", "kill", S("fixtureServerId"));
                    return Accept();
                case "await-exit":
                    c590.Invoke("observe-exited", S("fixtureServerId"));
                    c590.Invoke("docker", "start", S("fixtureServerId"));
                    return Accept();
                case "restart-identity":
                    if (Ne(S("runnerIdAfter"), S("runnerIdBefore"))) return Refuse("RestartIdentity");
                    return Accept();
                case "retype-charge":
                    if (I("attempts") != 2 || !B("floor1") || !B("floor2")) return Refuse("RetypeCharge");
                    return Accept();
                case "enter-only-body":
                    if (I("extraBody") > 0) return Refuse("SecondBody");
                    return Accept();
                case "zero-writes":
                    if (I("additionalBody") != 0 || I("additionalEnter") != 0) return Refuse("ExtraWrite");
                    return Accept();
                case "resume-manifest":
                    return Accept(new JsonObject { ["post"] = 0 });
                case "failure-summary":
                    var runExit = I("runExit");
                    return c590.Result(root, true, "", runExit, new JsonObject { ["delivery"] = "confirmed", ["runExit"] = runExit });
                case "missing-cut":
                    if (!B("reached")) return Refuse("CutNotReached");
                    return Accept();
                case "stock-gates":
                    if (!B("stockIdle") || !B("stockBusy")) return Refuse("StockGateMissing");
                    return Accept();
                case "interrupted-export":
                    if (!B("exportComplete")) return Refuse("ExportIncomplete");
                    c590.Invoke("commit-manifest", "result-ready");
                    return Accept();
                case "when-idle":
                    c590.Invoke("http", "POST", "WhenIdle");
                    return Accept();
                case "enter-only-tuple":
                    if (I("attempts") != 1 || Ne(S("floor"), S("originalFloor")) || Ne(S("generation"), S("originalGeneration"))) return Refuse("TupleChanged");
                    return Accept();
                case "retype-second-tuple":
                    if (!B("attempt2Snapshot")) return Refuse("MissingSecondTuple");
                    return Accept();
                case "foreign-resume":
                    var canonical = C590Manifest.Has(m, "canonicalResume") && B("canonicalResume") ? S("canonicalResume") : "";
                    if (!C590Manifest.Inside(files, S("resumePath"), S("evidenceRoot"), canonical)) return Refuse("ForeignResume");
                    return Accept();
                case "private-credential":
                    return Accept(new JsonObject { ["credential"] = "redacted" });
                case "e2e-staged-apphost":
                    var consumer = files.Path.Combine(new[] { checkout ?? "" }.Concat(StagedApphostConsumer).ToArray());
                    string source;
                    try
                    {
                        source = files.File.ReadAllText(consumer);
                    }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
                    {
                        throw new C590Failure("cannot read " + consumer + ": " + e.Message);
                    }
                    if (!Match(source, "TestAppHostPath")) return Refuse("MissingConsumer");
                    var staged = S("stagedApphost");
                    if (staged.Length == 0 || !(files.File.Exists(staged) || files.Directory.Exists(staged))) return Refuse("MissingApphost");
                    return Accept();
                default:
                    return c590.Stubbed ? Refuse("UnknownCase") : Refuse("RealCasePending " + name);
            }
        }
    }
}
