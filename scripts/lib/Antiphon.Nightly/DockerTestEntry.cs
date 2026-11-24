// CARD-0590 foreground test entry and its in-image half (unit 47 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md,
// W8 slice B): the commands test-docker (scripts/test-docker.ps1) and test-docker-container (scripts/test-docker-container.ps1).
// Nothing started either script by path, so neither keeps a launcher. The CLI runs them as
//
//   antiphon-scripts test-docker [--group small|backend|all] [--manifest <path>] [--throwaway-stack]
//   antiphon-scripts test-docker-container (--validate-roster --roster <path> --discovery <path> | --checkpoint <name>) [--manifest <path>]
//
// test-docker --throwaway-stack without ANTIPHON_C590_STUB is the live session entry: it runs `bash scripts/c590-remote.sh` of
// this checkout with C590_CASE=throwaway-all (and C590_REEXEC=1 unless already set) on the inherited environment, sharing this
// console, and exits with its code. Otherwise the manifest is the boundary under test: the group must be small, backend or all
// (ignoring case), and the same ordered refusals as the script (TaskBindingUnavailable, SourcedTask, VerificationPath,
// OutsideCheckout, NestedDaemonUnavailable, SiblingDaemonRefused (CARD-0604 D-5), MissingSocket, SocketGroupMismatch,
// UnreachableDatabase, then after the recorded `checkpoint <group>` MissingReport, StaleResult, ZeroExecuted,
// "MissingClass <name>", UnexpectedSkip, ResultExitMismatch, DigestMismatch) each write the c590 result with exit 2. The export
// commands follow (docker cp, hash, commit-manifest, docker rm, in that order); a nonzero manifest childExit is then the exit
// with no result written, else the result is accepted with exit 0.
//
// test-docker-container --validate-roster checks a Linux class roster against a discovery list (Assert-LinuxTestRoster): one line
// per error (duplicate, unknown or stale class, MissingExclusionOwner, IncludedNative, IncludedSpawner), exit 2; else "roster-ok",
// exit 0. --checkpoint records the child environment (ANTIPHON_BROKER_TESTS=1, SessionRunner__BaseUrl=http://127.0.0.1:1), prints
// "checkpoint-launched", exit 0. --manifest is accepted and not read, as the script's -Manifest was. Commands go through
// C590Boundary. Usage errors (a missing manifest, neither mode, a missing roster or discovery path) are NightlyUsageException
// (exit 2); exit 1: a file could not be read or a value cast, or a command could not start (one stderr line).
// Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The test-docker command's options (test-docker.ps1's -Group, -Manifest and -ThrowawayStack).</summary>
    public sealed class DockerTestOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "test-docker [--group small|backend|all] [--manifest <path>] [--throwaway-stack]";

        /// <summary>The named group.</summary>
        public string Group { get; init; } = "";

        /// <summary>The manifest file (the stub boundary's input).</summary>
        public string Manifest { get; init; } = "";

        /// <summary>The live session entry: the throwaway stack through c590-remote.sh.</summary>
        public bool ThrowawayStack { get; init; }

        /// <summary>The checkout whose scripts/c590-remote.sh the live entry runs.</summary>
        public string Checkout { get; init; } = "";

        /// <summary>The environment the live entry's child inherits (the CLI's own).</summary>
        public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();

        /// <summary>The arguments after the command name; checkout and environment are the CLI's own.</summary>
        public static DockerTestOptions Parse(IReadOnlyList<string> arguments, string checkout, IReadOnlyDictionary<string, string> environment)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--group", "--manifest" }, new[] { "--throwaway-stack" });
            return new DockerTestOptions
            {
                Group = flags.Value("--group", ""),
                Manifest = flags.Value("--manifest", ""),
                ThrowawayStack = flags.Switch("--throwaway-stack"),
                Checkout = checkout ?? "",
                Environment = environment ?? new Dictionary<string, string>(),
            };
        }
    }

    /// <summary>The test-docker-container command's options (test-docker-container.ps1's parameters).</summary>
    public sealed class DockerTestContainerOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "test-docker-container (--validate-roster --roster <path> --discovery <path> | --checkpoint <name>) [--manifest <path>]";

        /// <summary>Validate the roster against the discovery list.</summary>
        public bool ValidateRoster { get; init; }

        /// <summary>The roster file ({"classes":[...]}).</summary>
        public string Roster { get; init; } = "";

        /// <summary>The discovery file (a JSON array of class names).</summary>
        public string Discovery { get; init; } = "";

        /// <summary>The checkpoint to launch.</summary>
        public string Checkpoint { get; init; } = "";

        /// <summary>Accepted and not read (the script's -Manifest).</summary>
        public string Manifest { get; init; } = "";

        /// <summary>The arguments after the command name.</summary>
        public static DockerTestContainerOptions Parse(IReadOnlyList<string> arguments)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--roster", "--discovery", "--checkpoint", "--manifest" }, new[] { "--validate-roster" });
            return new DockerTestContainerOptions
            {
                ValidateRoster = flags.Switch("--validate-roster"),
                Roster = flags.Value("--roster", ""),
                Discovery = flags.Value("--discovery", ""),
                Checkpoint = flags.Value("--checkpoint", ""),
                Manifest = flags.Value("--manifest", ""),
            };
        }
    }

    /// <summary>The CARD-0590 foreground test entry (test-docker.ps1).</summary>
    public static class DockerTestEntry
    {
        /// <summary>The named groups.</summary>
        public static readonly IReadOnlyList<string> Groups = new[] { "small", "backend", "all" };

        private static readonly Regex VerificationPath = new Regex(@"(^|[\\/])verification([\\/]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Runs the entry. Returns the exit code; usage errors throw NightlyUsageException.</summary>
        public static int Run(DockerTestOptions options, NightlyHost host, IFileSystem files)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            var c590 = new C590Boundary(host, files);
            // The live session entry. Stub command tests never set this switch, so their manifest checks stay the boundary under test.
            if (options.ThrowawayStack && !c590.Stubbed) return Throwaway(options, host);
            if (string.IsNullOrEmpty(options.Manifest) || !files.File.Exists(options.Manifest)) throw new NightlyUsageException("Manifest is required");
            try
            {
                return Check(options.Group ?? "", C590Manifest.Read(files, options.Manifest), c590, files);
            }
            catch (C590Failure e)
            {
                host.Error.WriteLine("test-docker: " + e.Message);
                return 1;
            }
        }

        /// <summary>The live entry's child environment: the inherited one, C590_CASE=throwaway-all, and C590_REEXEC=1 unless set.</summary>
        public static IReadOnlyDictionary<string, string> ThrowawayEnvironment(IReadOnlyDictionary<string, string> inherited)
        {
            var environment = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in inherited ?? new Dictionary<string, string>()) environment[pair.Key] = pair.Value;
            environment["C590_CASE"] = "throwaway-all";
            if (!environment.TryGetValue("C590_REEXEC", out var reexec) || string.IsNullOrEmpty(reexec)) environment["C590_REEXEC"] = "1";
            return environment;
        }

        private static int Throwaway(DockerTestOptions options, NightlyHost host)
        {
            var script = Path.Combine(options.Checkout ?? "", "scripts", "c590-remote.sh");
            try
            {
                return host.Launch(new NightlyLaunch("bash", new[] { script }, "", Environment: ThrowawayEnvironment(options.Environment))).ExitCode;
            }
            catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is IOException || e is UnauthorizedAccessException)
            {
                host.Error.WriteLine("test-docker: cannot start bash: " + e.Message);
                return 1;
            }
        }

        private static int Check(string group, JsonNode m, C590Boundary c590, IFileSystem files)
        {
            var root = C590Manifest.Str(m, "evidenceRoot");
            int Refuse(string diagnosis) => c590.Result(root, false, diagnosis, 2);

            if (!Groups.Contains(group, StringComparer.OrdinalIgnoreCase)) return Refuse("UnknownGroup");
            var task = C590Manifest.Field(m, "task");
            if (task == null || !C590Manifest.IsTrue(task, "readable")) return Refuse("TaskBindingUnavailable");
            if (C590Manifest.Field(task, "sourceLandingOperationId") != null && C590Manifest.Str(task, "sourceLandingOperationId").Length > 0) return Refuse("SourcedTask");

            var canonical = C590Manifest.Has(m, "canonicalPath") && C590Manifest.IsTrue(m, "canonicalPath") ? C590Manifest.Str(m, "canonicalPath") : "";
            var source = C590Manifest.Str(m, "sourceRoot");
            if (VerificationPath.IsMatch(source)) return Refuse("VerificationPath");
            if (!C590Manifest.Inside(files, source, C590Manifest.Str(m, "checkoutRoot"), canonical)) return Refuse("OutsideCheckout");

            // CARD-0604 D-5: the daemon must be the runner's OWN nested daemon. A sibling daemon puts every mapped port in another
            // network namespace; an unreachable one is the tracked-session case (D-18), told as such.
            var daemon = C590Manifest.Field(m, "daemon");
            if (daemon == null || !C590Manifest.IsTrue(daemon, "present")) return Refuse("NestedDaemonUnavailable");
            if (!C590Manifest.Eq(C590Manifest.Str(daemon, "name"), C590Manifest.Str(daemon, "hostname"))) return Refuse("SiblingDaemonRefused");

            var socket = C590Manifest.Field(m, "socket");
            if (!C590Manifest.IsTrue(socket, "present")) return Refuse("MissingSocket");
            var groups = C590Manifest.Items(C590Manifest.Field(socket, "groups")).Select(HealthJson.Str).ToArray();
            var gid = C590Manifest.Str(socket, "gid");
            if (!groups.Any(entry => C590Manifest.Eq(entry, gid))) return Refuse("SocketGroupMismatch");
            if (!C590Manifest.IsTrue(m, "dbProbeOk")) return Refuse("UnreachableDatabase");

            c590.Invoke("checkpoint", group);
            var exit = C590Manifest.Int(m, "childExit");
            if (!C590Manifest.IsTrue(m, "reportPresent")) return Refuse("MissingReport");
            var report = C590Manifest.Field(m, "report");
            if (!C590Manifest.Eq(C590Manifest.Str(report, "runId"), C590Manifest.Str(m, "runId"))) return Refuse("StaleResult");
            if (C590Manifest.Int(report, "executed") == 0) return Refuse("ZeroExecuted");
            var actual = C590Manifest.Items(C590Manifest.Field(report, "classes")).Select(HealthJson.Str).ToArray();
            foreach (var expected in C590Manifest.Items(C590Manifest.Field(m, "expectedClasses")).Select(HealthJson.Str))
                if (!actual.Any(name => C590Manifest.Eq(name, expected))) return Refuse("MissingClass " + expected);
            if (C590Manifest.Int(report, "skipped") > 0) return Refuse("UnexpectedSkip");
            if (C590Manifest.Int(report, "failed") > 0 && exit == 0) return Refuse("ResultExitMismatch");
            if (!C590Manifest.Eq(C590Manifest.Str(m, "artifactDigest"), C590Manifest.Str(m, "copiedDigest"))) return Refuse("DigestMismatch");

            c590.Invoke("docker", "cp", "export");
            c590.Invoke("hash", "artifacts");
            c590.Invoke("commit-manifest", "result-ready");
            c590.Invoke("docker", "rm", "container");
            if (exit != 0) return exit;
            return c590.Result(root, true, "", 0);
        }
    }

    /// <summary>The CARD-0590 in-image entry (test-docker-container.ps1): the roster validator and the checkpoint launch.</summary>
    public static class DockerTestContainer
    {
        /// <summary>The checkpoint child's environment, in order (the broker lane on, the application runner unreachable).</summary>
        public static readonly IReadOnlyList<KeyValuePair<string, string>> ChildEnvironment = new[]
        {
            new KeyValuePair<string, string>("ANTIPHON_BROKER_TESTS", "1"),
            new KeyValuePair<string, string>("SessionRunner__BaseUrl", "http://127.0.0.1:1"),
        };

        private static readonly Regex NativeBoundary = new Regex("native", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex NativeClass = new Regex("FakeGrokContractTests|LandDeliveryFixture|OutputDistillationApplyCanaryTests|WorktreeLockDiagnosticsWindowsTests",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex Spawner = new Regex("WorkspaceHookRunnerTests", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Runs the entry. Returns the exit code; usage errors throw NightlyUsageException.</summary>
        public static int Run(DockerTestContainerOptions options, NightlyHost host, IFileSystem files)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            try
            {
                if (options.ValidateRoster)
                {
                    if (string.IsNullOrEmpty(options.Roster)) throw new NightlyUsageException("--roster is required with --validate-roster");
                    if (string.IsNullOrEmpty(options.Discovery)) throw new NightlyUsageException("--discovery is required with --validate-roster");
                    var errors = Validate(C590Manifest.Read(files, options.Roster), C590Manifest.Read(files, options.Discovery));
                    foreach (var error in errors) host.Out.WriteLine(error);
                    if (errors.Count > 0) return 2;
                    host.Out.WriteLine("roster-ok");
                    return 0;
                }
                if (!string.IsNullOrEmpty(options.Checkpoint))
                {
                    new C590Boundary(host, files).Invoke("child-env", ChildEnvironment.Select(pair => pair.Key + "=" + pair.Value).ToArray());
                    host.Out.WriteLine("checkpoint-launched");
                    return 0;
                }
            }
            catch (C590Failure e)
            {
                host.Error.WriteLine("test-docker-container: " + e.Message);
                return 1;
            }
            throw new NightlyUsageException("Checkpoint or ValidateRoster is required");
        }

        /// <summary>Assert-LinuxTestRoster: the errors of a roster ({"classes":[...]}) against a discovery list, in the script's order.</summary>
        public static IReadOnlyList<string> Validate(JsonNode roster, JsonNode discovery)
        {
            var discovered = C590Manifest.Items(discovery).Select(HealthJson.Str).ToArray();
            var rows = C590Manifest.Items(C590Manifest.Field(roster, "classes"));
            var errors = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                var name = C590Manifest.Str(row, "class");
                if (!seen.Add(name)) errors.Add("duplicate class " + name);
                var included = C590Manifest.Eq(C590Manifest.Str(row, "disposition"), "include");
                var boundary = C590Manifest.Str(row, "boundary");
                if (!included && (string.IsNullOrWhiteSpace(C590Manifest.Str(row, "owner")) || string.IsNullOrWhiteSpace(C590Manifest.Str(row, "reason"))))
                    errors.Add("MissingExclusionOwner");
                if (included && (NativeBoundary.IsMatch(boundary) || NativeClass.IsMatch(name)))
                    errors.Add("IncludedNative " + name);
                var closure = C590Manifest.Items(C590Manifest.Field(row, "testHelperClosure")).Select(HealthJson.Str).Append(name);
                if (included && (closure.Any(Spawner.IsMatch) || C590Manifest.Eq(name, "WorkspaceHookRunnerTests")))
                    errors.Add("IncludedSpawner " + name);
            }
            var known = rows.Select(row => C590Manifest.Str(row, "class")).ToArray();
            foreach (var name in discovered)
                if (!known.Any(entry => C590Manifest.Eq(entry, name))) errors.Add("unknown class " + name);
            foreach (var name in known)
                if (!discovered.Any(entry => C590Manifest.Eq(entry, name))) errors.Add("stale class " + name);
            return errors;
        }
    }
}
