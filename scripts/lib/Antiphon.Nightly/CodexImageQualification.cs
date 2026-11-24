// CARD-0660 V-9 image qualification (Q-1 runtime, Q-2 session-testing; CP-10/11). Evidence tooling, not product. It replaced
// scripts/verify-card0660-codex-image.ps1 (unit 48 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md; the owner
// approved deleting the script's W10 fixture, scripts/fixtures/c913-image-wrapper.ps1, whose checks are C# tests now). The CLI
// runs it as
//
//   antiphon-scripts verify-card0660-codex-image --target runtime|session-testing --image <tag> --source-revision <full sha>
//       --results-root <fresh dir> [--skip-build]
//
// It builds ONE image target in the foreground (or, with --skip-build, checks a separately built CARD-0904 image), then grades
// eight Codex rows, the Grok and jq version rows and, for the SDK-bearing session-testing target, a net9 offline apphost row by
// running docker/session-runner-grok/verify-codex-image.sh inside throwaway containers of that image: phone-home disabled,
// --network none, no published port, no Docker socket, not privileged, and only this run's own volumes. session-testing's DinD
// entrypoint is overridden so no credential-dependent entrypoint boots. The production init-state script runs read-only from
// this checkout, mounted as state-init mounts it, and the runner-side rows observe the same volume at /state, with a throwaway
// volume standing in for the host Codex home at /codex-home (state-init) and /state/codex (runner). No external provider is
// contacted and no real CODEX_HOME is touched.
//
// Rows: (1) version (2) layout (3) install-readonly (4) no-baked-auth (5) fresh-home (6) trust (7) preserve (8) config-accepted
// (9) grok-version (10) jq-version; session-testing also grades (11) net9-offline. Lines: "C660 IMAGE NOTE: <reason>" (when
// there is one), "C660 IMAGE: target=... image=... imageId=... source=... rows=<ok>/<n> <row>=<grade> ...", "C660 IMAGE EXIT
// CODE: <n>"; the same lines go to <results-root>/result.txt once that directory is this run's. Exit 0 every applicable row
// ok, 1 one or more rows not ok, 2 setup failed (dirty or mismatched source, reused tag, non-fresh results root, no Docker,
// failed build or image revision mismatch). Every exit first removes the volumes this run created, and only those. docker
// and git run on the host's launcher (docker's stdout and stderr together, as the script's 2>&1 | Out-String; git's stdout
// alone, as its 2>$null); the volume owner token and the preserve nonce are CodexImageSeams; the clock is the host's, files
// IFileSystem's. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The verify-card0660-codex-image command's options (verify-card0660-codex-image.ps1's parameters).</summary>
    public sealed class CodexImageOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "verify-card0660-codex-image --target runtime|session-testing --image <tag> --source-revision <full sha> --results-root <fresh dir> [--skip-build]";

        /// <summary>The image targets the command qualifies (the script's ValidateSet).</summary>
        public static readonly IReadOnlyList<string> Targets = new[] { "runtime", "session-testing" };

        /// <summary>The Dockerfile target: runtime or session-testing.</summary>
        public string Target { get; init; } = "";

        /// <summary>The image tag: built here (unique), or with SkipBuild an existing one.</summary>
        public string Image { get; init; } = "";

        /// <summary>The full 40-hex commit the image must be built from (the checkout's HEAD).</summary>
        public string SourceRevision { get; init; } = "";

        /// <summary>A fresh directory for the evidence (relative: under the checkout).</summary>
        public string ResultsRoot { get; init; } = "";

        /// <summary>Check a separately built image instead of building one.</summary>
        public bool SkipBuild { get; init; }

        /// <summary>The checkout: the build context, the init and probe scripts, the git checks and a relative results root's base.</summary>
        public string Checkout { get; init; } = "";

        /// <summary>The arguments after the command name; checkout is the CLI's own. The target matches ignoring case, as ValidateSet did.</summary>
        public static CodexImageOptions Parse(IReadOnlyList<string> arguments, string checkout)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--target", "--image", "--source-revision", "--results-root" }, new[] { "--skip-build" });
            var given = flags.Required("--target");
            var target = Targets.FirstOrDefault(name => string.Equals(name, given, StringComparison.OrdinalIgnoreCase))
                ?? throw new NightlyUsageException("--target must be runtime or session-testing, not '" + given + "'");
            return new CodexImageOptions
            {
                Target = target,
                Image = flags.Required("--image"),
                SourceRevision = flags.Required("--source-revision"),
                ResultsRoot = flags.Required("--results-root"),
                SkipBuild = flags.Switch("--skip-build"),
                Checkout = checkout ?? "",
            };
        }
    }

    /// <summary>The qualification's seams beyond the host: the random token of the volume owner (its first six characters) and of the preserve nonce.</summary>
    public sealed class CodexImageSeams
    {
        /// <summary>A fresh 32-hex token (Guid "N").</summary>
        public Func<string> Token { get; init; } = () => Guid.NewGuid().ToString("N");
    }

    /// <summary>One probe container: the row the in-image probe grades and the uid:gid it runs as.</summary>
    public sealed record CodexImageProbe(string Row, string User);

    /// <summary>The CARD-0660 V-9 image qualification.</summary>
    public static class CodexImageQualification
    {
        /// <summary>The image's Dockerfile, relative to the checkout.</summary>
        public const string Dockerfile = "docker/session-runner-grok/Dockerfile";

        /// <summary>The production state initializer, bind-mounted read-only where state-init mounts it.</summary>
        public const string InitScript = "docker/stack/init-state.sh";

        /// <summary>The in-image probe, bind-mounted read-only at /c660/verify-codex-image.sh.</summary>
        public const string ProbeScript = "docker/session-runner-grok/verify-codex-image.sh";

        /// <summary>The docker inspect format of the image's source revision label.</summary>
        public const string RevisionFormat = "{{index .Config.Labels \"org.opencontainers.image.revision\"}}";

        /// <summary>The runner's app uid:gid.</summary>
        public const string AppUser = "1654:1654";

        /// <summary>
        /// session-testing only: as root in a throwaway container, the package and scratch volumes must be empty directories,
        /// then they become the app uid's with mode 0700. Nothing else is chowned.
        /// </summary>
        public const string CacheInitScript =
            "set -eu; for p in /home/app/.nuget/packages /var/cache/antiphon/nuget-scratch; do test -d \"$p\"; "
            + "test -z \"$(find \"$p\" -mindepth 1 -print -quit)\"; chown 1654:1654 \"$p\"; chmod 0700 \"$p\"; done";

        private static readonly string[] CodexRows =
        {
            "version", "layout", "install-readonly", "no-baked-auth", "fresh-home", "trust", "preserve", "config-accepted", "grok-version", "jq-version",
        };

        private static readonly CodexImageProbe[] FirstProbes =
        {
            new CodexImageProbe("version", AppUser),
            new CodexImageProbe("grok-version", AppUser),
            new CodexImageProbe("jq-version", AppUser),
            new CodexImageProbe("layout", AppUser),
            new CodexImageProbe("install-readonly", AppUser),
            new CodexImageProbe("no-baked-auth", "0:0"),
            new CodexImageProbe("fresh-home", AppUser),
            new CodexImageProbe("trust", AppUser),
            new CodexImageProbe("config-accepted", AppUser),
        };

        /// <summary>The graded rows of a target, in the summary's order: ten, and net9-offline for session-testing.</summary>
        public static IReadOnlyList<string> Rows(string target) =>
            IsTesting(target) ? CodexRows.Concat(new[] { "net9-offline" }).ToArray() : CodexRows.ToArray();

        /// <summary>
        /// The probes after the first init, in the order they run (net9-offline last, for session-testing). The preserve row runs
        /// after them: preserve-arm with the nonce, the second init, then preserve-check, each as the app uid.
        /// </summary>
        public static IReadOnlyList<CodexImageProbe> Probes(string target) =>
            IsTesting(target) ? FirstProbes.Concat(new[] { new CodexImageProbe("net9-offline", AppUser) }).ToArray() : FirstProbes.ToArray();

        /// <summary>Runs the qualification. Returns the exit code; usage errors throw NightlyUsageException.</summary>
        public static int Run(CodexImageOptions options, NightlyHost host, IFileSystem files, CodexImageSeams seams)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (seams?.Token == null) throw new ArgumentNullException(nameof(seams));
            if (!CodexImageOptions.Targets.Contains(options.Target)) throw new NightlyUsageException("--target must be runtime or session-testing, not '" + options.Target + "'");
            if (string.IsNullOrEmpty(options.Image)) throw new NightlyUsageException("missing --image");
            if (string.IsNullOrEmpty(options.SourceRevision)) throw new NightlyUsageException("missing --source-revision");
            if (string.IsNullOrEmpty(options.ResultsRoot)) throw new NightlyUsageException("missing --results-root");
            return new QualificationRun(options, host, files, seams).Execute();
        }

        private static bool IsTesting(string target) => string.Equals(target, "session-testing", StringComparison.Ordinal);

        private sealed record DockerResult(int ExitCode, string Output);

        private sealed class QualificationRun
        {
            private static readonly Regex FullSha = new Regex("^[0-9a-f]{40}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            private static readonly Regex ImageIdentity = new Regex("^sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant);
            private static readonly Regex RowLine = new Regex("^C660_ROW ", RegexOptions.CultureInvariant);

            private readonly CodexImageOptions _o;
            private readonly NightlyHost _host;
            private readonly IFileSystem _files;
            private readonly CodexImageSeams _seams;
            private readonly IReadOnlyList<string> _rows;
            private readonly Dictionary<string, string> _grades = new Dictionary<string, string>(StringComparer.Ordinal);
            private readonly List<string> _created = new List<string>();
            private readonly string _state;
            private readonly string _work;
            private readonly string _codex;
            private readonly string _packages;
            private readonly string _scratch;
            private readonly string _owner;
            private string _imageId = "none";
            private string _resultsPath;

            public QualificationRun(CodexImageOptions options, NightlyHost host, IFileSystem files, CodexImageSeams seams)
            {
                _o = options;
                _host = host;
                _files = files;
                _seams = seams;
                _rows = Rows(options.Target);
                foreach (var row in _rows) _grades[row] = "unknown";
                var stamp = host.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
                var token = seams.Token() ?? "";
                _owner = "c660q-" + stamp + "-" + (token.Length > 6 ? token.Substring(0, 6) : token);
                _state = _owner + "-state";
                _work = _owner + "-work";
                // CARD-0660 (amended): stands in for the server2 host directory RUNNER_CODEX_HOME_DIR, mounted where
                // docker-compose.server2-runner.yml binds it: /codex-home in state-init, /state/codex in the runner.
                _codex = _owner + "-codex";
                _packages = _owner + "-packages";
                _scratch = _owner + "-scratch";
            }

            private bool Testing => IsTesting(_o.Target);

            private string PackagesMount => "type=volume,source=" + _packages + ",target=/home/app/.nuget/packages,volume-nocopy";

            private string ScratchMount => "type=volume,source=" + _scratch + ",target=/var/cache/antiphon/nuget-scratch,volume-nocopy";

            private string CheckoutPath(string relative) => _files.Path.Combine(new[] { _o.Checkout }.Concat(relative.Split('/')).ToArray());

            public int Execute()
            {
                // --- setup gates ---
                var results = _files.Path.IsPathRooted(_o.ResultsRoot) ? _o.ResultsRoot : _files.Path.Combine(_o.Checkout, _o.ResultsRoot);
                if (_files.File.Exists(results) || (_files.Directory.Exists(results) && _files.Directory.EnumerateFileSystemEntries(results).Any()))
                    return Finish(2, "ResultsRoot must be a fresh directory.");
                try
                {
                    _files.Directory.CreateDirectory(results);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
                {
                    return Finish(2, "ResultsRoot cannot be created: " + e.Message);
                }
                _resultsPath = results;

                if (!FullSha.IsMatch(_o.SourceRevision)) return Finish(2, "SourceRevision must be a full 40-hex commit SHA.");
                var head = Git("rev-parse", "HEAD");
                var dirty = Git("status", "--porcelain", "--untracked-files=no");
                if (!string.Equals(head, _o.SourceRevision, StringComparison.OrdinalIgnoreCase))
                    return Finish(2, "SourceRevision " + _o.SourceRevision + " is not checkout HEAD " + head + ".");
                if (dirty.Length > 0) return Finish(2, "Tracked changes are uncommitted; the build context would not be SourceRevision.");

                if (Docker(new[] { "version", "--format", "{{.Server.Version}}" }, "docker-version.txt").ExitCode != 0)
                    return Finish(2, "Docker daemon is not reachable.");
                var existing = Docker(new[] { "image", "inspect", "--format", "{{.Id}}", _o.Image }, null);
                if (_o.SkipBuild)
                {
                    if (existing.ExitCode != 0) return Finish(2, "Image tag " + _o.Image + " does not exist for --skip-build.");
                    var labelled = Docker(new[] { "image", "inspect", "--format", RevisionFormat, _o.Image }, "image-revision.txt").Output.Trim();
                    if (!string.Equals(labelled, _o.SourceRevision, StringComparison.OrdinalIgnoreCase))
                        return Finish(2, "Image revision " + labelled + " does not match SourceRevision " + _o.SourceRevision + ".");
                }
                else
                {
                    if (existing.ExitCode == 0) return Finish(2, "Image tag " + _o.Image + " already exists; qualification needs a unique tag.");
                    // --- build (foreground, the one build this run owns) ---
                    var build = Docker(new[]
                    {
                        "build", "--file", CheckoutPath(Dockerfile), "--target", _o.Target, "--build-arg", "SOURCE_REVISION=" + _o.SourceRevision,
                        "--tag", _o.Image, _o.Checkout,
                    }, "build.log");
                    if (build.ExitCode != 0) return Finish(2, "docker build failed; see build.log.");
                }
                var identity = Docker(new[] { "image", "inspect", "--format", "{{.Id}}", _o.Image }, "image-id.txt");
                _imageId = identity.Output.Trim();
                if (identity.ExitCode != 0 || !ImageIdentity.IsMatch(_imageId)) return Finish(2, "Image identity is invalid.");
                var revision = Docker(new[] { "image", "inspect", "--format", RevisionFormat, _imageId }, "image-revision.txt");
                if (revision.ExitCode != 0 || !string.Equals(revision.Output.Trim(), _o.SourceRevision, StringComparison.Ordinal))
                    return Finish(2, "Image revision does not match SourceRevision.");

                // --- throwaway volumes and probes ---
                var volumes = new List<string> { _state, _work, _codex };
                if (Testing) volumes.AddRange(new[] { _packages, _scratch });
                foreach (var volume in volumes)
                {
                    if (Docker(new[] { "volume", "create", "--label", "antiphon.c660.owner=" + _owner, volume }, null).ExitCode != 0)
                        return Finish(2, "cannot create volume " + volume);
                    _created.Add(volume);
                }

                if (Testing)
                {
                    var caches = Docker(new[]
                    {
                        "run", "--rm", "--network", "none", "--user", "0:0", "--entrypoint", "/bin/sh",
                        "--mount", PackagesMount, "--mount", ScratchMount, _imageId, "-c", CacheInitScript,
                    }, "cache-init.txt");
                    if (caches.ExitCode != 0) return Finish(2, "Owned cache initialization failed.");
                }

                if (Init("init-1.txt") != 0) return Finish(2, "first init-state run failed; see init-1.txt.");
                foreach (var probe in Probes(_o.Target)) _grades[probe.Row] = Probe(probe.Row, probe.User, Array.Empty<string>());

                // Row 7: sentinel config and auth, a second init, then the same bytes, owner and mode.
                var nonce = _seams.Token() ?? "";
                var armed = Probe("preserve-arm", AppUser, new[] { nonce });
                _grades["preserve"] = armed == "ok" && Init("init-2.txt") == 0 ? Probe("preserve-check", AppUser, Array.Empty<string>()) : "fail";

                var failed = _rows.Count(row => _grades[row] != "ok");
                if (failed > 0) return Finish(1, failed.ToString(CultureInfo.InvariantCulture) + " row(s) not ok; see row-*.txt.");
                return Finish(0, "");
            }

            // The production init-state.sh as state-init runs it: root, no network, this run's work, state and Codex volumes.
            private int Init(string evidence) => Docker(new[]
            {
                "run", "--rm", "--network", "none", "--user", "0:0", "--entrypoint", "/bin/sh",
                "--env", "CODEX_HOME_REQUIRED=1",
                "--mount", "type=volume,source=" + _work + ",target=/work",
                "--mount", "type=volume,source=" + _state + ",target=/runner-state",
                "--mount", "type=volume,source=" + _codex + ",target=/codex-home",
                "--mount", "type=bind,source=" + CheckoutPath(InitScript) + ",target=/stack/init-state.sh,readonly",
                _imageId, "/stack/init-state.sh",
            }, evidence).ExitCode;

            // One row in its own throwaway container: ok only for exit 0 and exactly one C660_ROW line, naming this row as ok with a detail.
            private string Probe(string row, string user, string[] extra)
            {
                var mounts = row == "net9-offline"
                    ? new[] { "--mount", PackagesMount, "--mount", ScratchMount }
                    : new[]
                    {
                        "--mount", "type=volume,source=" + _work + ",target=/work",
                        "--mount", "type=volume,source=" + _state + ",target=/state",
                        "--mount", "type=volume,source=" + _codex + ",target=/state/codex",
                    };
                var arguments = new[] { "run", "--rm", "--network", "none", "--env", "PhoneHome__Enabled=false", "--entrypoint", "/bin/bash", "--user", user,
                        "--tmpfs", "/c660-home:exec,uid=1654,gid=1654,mode=0700" }
                    .Concat(mounts)
                    .Concat(new[] { "--mount", "type=bind,source=" + CheckoutPath(ProbeScript) + ",target=/c660/verify-codex-image.sh,readonly",
                        _imageId, "/c660/verify-codex-image.sh", row })
                    .Concat(extra)
                    .ToArray();
                var result = Docker(arguments, "row-" + row + ".txt");
                var lines = result.Output.Split('\n').Where(line => RowLine.IsMatch(line)).ToArray();
                var expected = new Regex("^C660_ROW " + Regex.Escape(row) + " ok .+\r?$", RegexOptions.CultureInvariant);
                return result.ExitCode == 0 && lines.Length == 1 && expected.IsMatch(lines[0]) ? "ok" : "fail";
            }

            // Exit-Qualification: this run's volumes are removed (and nothing else), then the summary is written and printed.
            private int Finish(int code, string reason)
            {
                foreach (var volume in _created) Launch("docker", new[] { "volume", "rm", "-f", volume });
                var ok = _rows.Count(row => _grades[row] == "ok");
                var summary = new List<string>();
                if (!string.IsNullOrEmpty(reason)) summary.Add("C660 IMAGE NOTE: " + reason);
                summary.Add(string.Format(CultureInfo.InvariantCulture, "C660 IMAGE: target={0} image={1} imageId={2} source={3} rows={4}/{5} ",
                        _o.Target, _o.Image, _imageId, _o.SourceRevision, ok, _rows.Count)
                    + string.Join(" ", _rows.Select(row => row + "=" + _grades[row])));
                summary.Add("C660 IMAGE EXIT CODE: " + code.ToString(CultureInfo.InvariantCulture));
                if (_resultsPath != null && _files.Directory.Exists(_resultsPath)) WriteEvidence("result.txt", string.Join("\n", summary));
                foreach (var line in summary) _host.Out.WriteLine(line);
                return code;
            }

            private void WriteEvidence(string name, string text) =>
                _files.File.WriteAllText(_files.Path.Combine(_resultsPath, name), text + "\n", Encoding.ASCII);

            // git in the checkout, its stdout alone and trimmed (the script's `& git ... 2>$null | Out-String`); empty when git cannot start.
            private string Git(params string[] arguments)
            {
                try
                {
                    var result = _host.Launch(new NightlyLaunch("git", arguments, _o.Checkout, CaptureOutput: true));
                    return (result.StandardOutputText ?? "").Trim();
                }
                catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is IOException || e is UnauthorizedAccessException)
                {
                    return "";
                }
            }

            // docker with its stdout and stderr lines together; with an evidence name, the command, its exit and its output go there.
            private DockerResult Docker(string[] arguments, string evidence)
            {
                var result = Launch("docker", arguments);
                if (!string.IsNullOrEmpty(evidence))
                    WriteEvidence(evidence, "docker " + string.Join(" ", arguments) + "\n--- exit " + result.ExitCode.ToString(CultureInfo.InvariantCulture) + " ---\n" + result.Output);
                return result;
            }

            // A tool that cannot start answers -1 with the reason.
            private DockerResult Launch(string fileName, string[] arguments)
            {
                try
                {
                    var result = _host.Launch(new NightlyLaunch(fileName, arguments, "", CaptureOutput: true));
                    return new DockerResult(result.ExitCode, result.LogText ?? "");
                }
                catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is IOException || e is UnauthorizedAccessException)
                {
                    return new DockerResult(-1, fileName + " could not start: " + e.Message + "\n");
                }
            }
        }
    }
}
