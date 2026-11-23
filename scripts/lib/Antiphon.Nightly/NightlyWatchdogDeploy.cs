// CARD-0545: the preflight or deploy of the independent nightly watchdog to the host a deploy profile names,
// `antiphon-scripts deploy-nightly-watchdog [--profile <path>] [--deploy]`. It replaced scripts/deploy-nightly-watchdog.ps1
// and its driver scripts/test-deploy-nightly-watchdog.ps1 (deleted 2026-11-23, unit 10 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md); the cases run in process in
// NightlyVerificationContractTests (C545Deploy). The owner document is docs/nightly-watchdog.md.
//
// The target comes ONLY from an untracked deploy profile, looked up in this order: --profile, the
// ANTIPHON_WATCHDOG_DEPLOY_PROFILE value the caller passes in (the CLI reads the variable; this core reads no
// environment), the untracked default C:\Antiphon\nightly\watchdog-deploy.json, then exactly one gitignored
// scripts/nightly-watchdog/*.local.json. No profile, or a profile still holding a <placeholder> value, refuses (exit 3)
// before anything is published or contacted. There is no built-in target.
// - Preflight (the default, read-only on the host): publish the self-contained binary for the profile's rid, stamped
//   with HEAD (an unreadable HEAD refuses the publish), checksum it, render the systemd unit template and the
//   qualification config, print the profile's non-secret values and run the remote --self-check dry run over ssh.
// - Deploy (the explicit opt-in): create the state directories, upload the binary, unit and rendered config, append only
//   the absent non-secret env keys this command owns (ANTIPHON_WATCHDOG_SNAPSHOT_BIND, _STATE_DIR, _NAMESPACE) to
//   <remoteRoot>/env, install and enable the unit, and verify GET <snapshotUrl>.
// Secrets are never read, written or printed: the host's env file is read for its key names only (sed on the host), and
// no captured output of dotnet, ssh or scp is printed. dotnet, ssh and scp run on the host's launcher (Watched custody,
// output captured), git on its git seam, the snapshot GET on the HTTP transport, files on IFileSystem; a test binds
// fakes for all of them. Exit 0 done; 1 failed ("FAILED: <reason>" on stderr); 3 refused ("REFUSED: <reason>" on stderr,
// nothing contacted). Profile keys are matched exactly. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The deploy-nightly-watchdog command's options.</summary>
    public sealed class NightlyWatchdogDeployOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "deploy-nightly-watchdog [--profile <deploy-profile.json>] [--deploy]";

        /// <summary>The untracked default profile, looked up after --profile and the environment's profile.</summary>
        public const string DefaultProfile = @"C:\Antiphon\nightly\watchdog-deploy.json";

        private static readonly string[] Valued = { "--profile" };
        private static readonly string[] Switches = { "--deploy" };

        /// <summary>--profile: the deploy profile; empty looks further.</summary>
        public string ProfilePath { get; init; } = "";

        /// <summary>The ANTIPHON_WATCHDOG_DEPLOY_PROFILE value the caller read; empty looks further.</summary>
        public string EnvironmentProfilePath { get; init; } = "";

        /// <summary>The untracked default profile, used when the file exists.</summary>
        public string DefaultProfilePath { get; init; } = DefaultProfile;

        /// <summary>--deploy: install on the host; otherwise the read-only preflight.</summary>
        public bool Deploy { get; init; }

        /// <summary>The checkout whose watchdog project, templates and *.local.json profiles are used.</summary>
        public string RepoRoot { get; init; } = "";

        /// <summary>Where the binary is published and the unit and config are rendered; empty: a new temporary directory.</summary>
        public string WorkDir { get; init; } = "";

        /// <summary>The arguments after the command name; NightlyUsageException for a malformed line.</summary>
        public static NightlyWatchdogDeployOptions Parse(IReadOnlyList<string> arguments, string environmentProfilePath, string repoRoot)
        {
            var flags = NightlyArgs.Parse(arguments, Valued, Switches);
            return new NightlyWatchdogDeployOptions
            {
                ProfilePath = flags.Value("--profile", ""),
                EnvironmentProfilePath = environmentProfilePath ?? "",
                Deploy = flags.Switch("--deploy"),
                RepoRoot = repoRoot ?? "",
            };
        }
    }

    /// <summary>A checked deploy profile: every value as text, the defaults applied.</summary>
    public sealed class NightlyWatchdogProfile
    {
        /// <summary>The profile keys, in the order they are checked.</summary>
        public static readonly IReadOnlyList<string> Keys = Array.AsReadOnly(new[]
        {
            "sshTarget", "sshIdentityFile", "remoteRoot", "serviceUser", "rid", "runtime", "snapshotBind", "snapshotUrl", "qualSnapshotBind", "stubWindmillBind",
        });

        /// <summary>The keys printed by the preflight and the deploy (never sshTarget or sshIdentityFile).</summary>
        public static readonly IReadOnlyList<string> PrintedKeys = Array.AsReadOnly(new[]
        {
            "rid", "runtime", "remoteRoot", "serviceUser", "snapshotBind", "snapshotUrl", "qualSnapshotBind", "stubWindmillBind",
        });

        private readonly Dictionary<string, string> _values;

        internal NightlyWatchdogProfile(Dictionary<string, string> values)
        {
            _values = values;
        }

        /// <summary>A key's value ("" when absent).</summary>
        public string this[string key] => _values.TryGetValue(key, out var value) ? value : "";

        /// <summary>The ssh/scp target, &lt;user&gt;@&lt;host&gt;.</summary>
        public string SshTarget => this["sshTarget"];

        /// <summary>The private key ssh and scp use; empty for their default.</summary>
        public string SshIdentityFile => this["sshIdentityFile"];

        /// <summary>The absolute install directory on the host.</summary>
        public string RemoteRoot => this["remoteRoot"];

        /// <summary>The non-root user the unit runs as.</summary>
        public string ServiceUser => this["serviceUser"];

        /// <summary>The publish runtime identifier (default linux-x64).</summary>
        public string Rid => this["rid"];

        /// <summary>native or container (default native).</summary>
        public string Runtime => this["runtime"];

        /// <summary>The snapshot bind, host:port.</summary>
        public string SnapshotBind => this["snapshotBind"];

        /// <summary>The URL the deploy verifies.</summary>
        public string SnapshotUrl => this["snapshotUrl"];

        /// <summary>The qualification instance's bind (default: snapshotBind's host, port 17291).</summary>
        public string QualSnapshotBind => this["qualSnapshotBind"];

        /// <summary>The F-3 stub bind (default 127.0.0.1:17292).</summary>
        public string StubWindmillBind => this["stubWindmillBind"];
    }

    /// <summary>One profile check: the checked profile, or the refusal's reason (it names the key, never the value).</summary>
    public sealed record NightlyWatchdogProfileCheck(bool Ok, string Reason, NightlyWatchdogProfile Profile);

    /// <summary>What a preflight or deploy did.</summary>
    public sealed class NightlyWatchdogDeployResult
    {
        /// <summary>0 done, 1 failed, 3 refused.</summary>
        public int ExitCode { get; set; } = 1;

        /// <summary>The closing line (PREFLIGHT OK, DEPLOY OK, REFUSED: or FAILED:).</summary>
        public string Message { get; set; } = "";

        /// <summary>The profile used; empty when there was none.</summary>
        public string ProfilePath { get; set; } = "";

        /// <summary>The rendered systemd unit; empty before rendering.</summary>
        public string RenderedUnit { get; set; } = "";

        /// <summary>The rendered qualification config; empty before rendering.</summary>
        public string RenderedQual { get; set; } = "";

        /// <summary>The KEY=value lines the deploy appended to the host's env file.</summary>
        public IReadOnlyList<string> EnvFragment { get; set; } = Array.Empty<string>();

        /// <summary>The published binary's lower-case SHA-256; empty when the publish produced no Antiphon.NightlyWatchdog.</summary>
        public string Checksum { get; set; } = "";
    }

    /// <summary>The nightly watchdog's preflight and deploy (CARD-0545).</summary>
    public static class NightlyWatchdogDeploy
    {
        /// <summary>The systemd unit's name, on the host and in /etc/systemd/system.</summary>
        public const string UnitName = "antiphon-nightly-watchdog.service";

        /// <summary>The binary's name in the publish output and on the host.</summary>
        public const string BinaryName = "Antiphon.NightlyWatchdog";

        /// <summary>The env keys this command owns (non-secret), in the order they are appended.</summary>
        public static readonly IReadOnlyList<string> OwnedEnvKeys = Array.AsReadOnly(new[]
        {
            "ANTIPHON_WATCHDOG_SNAPSHOT_BIND", "ANTIPHON_WATCHDOG_STATE_DIR", "ANTIPHON_WATCHDOG_NAMESPACE",
        });

        private const string NoProfile =
            "REFUSED: no deploy profile (pass --profile, set ANTIPHON_WATCHDOG_DEPLOY_PROFILE, or place the untracked default). Nothing was contacted.";

        private static readonly Regex Placeholder = new Regex("[<>]", RegexOptions.CultureInvariant);
        private static readonly Regex SshTarget = new Regex("^[A-Za-z0-9_.-]+@[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant);
        private static readonly Regex ServiceUser = new Regex("^[a-z_][a-z0-9_-]*$", RegexOptions.CultureInvariant);
        private static readonly Regex QuoteOrSpace = new Regex(@"['""\s]", RegexOptions.CultureInvariant);
        private static readonly Regex BracketBind = new Regex(@"^\[(.+)\]:\d+$", RegexOptions.CultureInvariant);
        private static readonly Regex HostBind = new Regex(@"^(.+):\d+$", RegexOptions.CultureInvariant);
        private static readonly Regex Loopback = new Regex(@"^127\.", RegexOptions.CultureInvariant);
        private static readonly Regex BindPort = new Regex(@":\d+$", RegexOptions.CultureInvariant);
        private static readonly Regex EnvKey = new Regex(@"^\s*([A-Z0-9_]+)\s*=", RegexOptions.CultureInvariant);
        private static readonly Regex Sha = new Regex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant);

        /// <summary>
        /// The profile path: --profile, else the environment's profile, else the default when it exists, else the one
        /// scripts/nightly-watchdog/*.local.json of the checkout when there is exactly one; empty when none applies. Only
        /// the default's existence and the directory listing are observed; no profile is read here.
        /// </summary>
        public static string ResolveProfilePath(NightlyWatchdogDeployOptions options, IFileSystem files)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (!string.IsNullOrWhiteSpace(options.ProfilePath)) return options.ProfilePath;
            if (!string.IsNullOrWhiteSpace(options.EnvironmentProfilePath)) return options.EnvironmentProfilePath;
            if (!string.IsNullOrWhiteSpace(options.DefaultProfilePath) && files.File.Exists(options.DefaultProfilePath)) return options.DefaultProfilePath;
            if (!string.IsNullOrWhiteSpace(options.RepoRoot))
            {
                var directory = files.Path.Combine(options.RepoRoot, "scripts", "nightly-watchdog");
                if (files.Directory.Exists(directory))
                {
                    var local = files.Directory.EnumerateFiles(directory, "*.local.json", SearchOption.TopDirectoryOnly).Take(2).ToArray();
                    if (local.Length == 1) return files.Path.GetFullPath(local[0]);
                }
            }
            return "";
        }

        /// <summary>
        /// Test-NightlyWatchdogProfile: the profile text checked. Every refusal names the key, never the value. A value is
        /// its JSON string, or the JSON text of any other value; null and absent are empty.
        /// </summary>
        public static NightlyWatchdogProfileCheck Check(string profileText)
        {
            static NightlyWatchdogProfileCheck Fail(string reason) => new NightlyWatchdogProfileCheck(false, reason, null);
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                using var document = JsonDocument.Parse(profileText ?? "");
                if (document.RootElement.ValueKind != JsonValueKind.Object) return Fail("deploy profile is not a JSON object");
                foreach (var key in NightlyWatchdogProfile.Keys)
                    values[key] = document.RootElement.TryGetProperty(key, out var value) ? Text(value) : "";
            }
            catch (JsonException)
            {
                return Fail("deploy profile is not a JSON object");
            }
            foreach (var key in NightlyWatchdogProfile.Keys)
                if (Placeholder.IsMatch(values[key])) return Fail("placeholder value in " + key);
            foreach (var key in new[] { "sshTarget", "remoteRoot", "serviceUser", "snapshotBind", "snapshotUrl" })
                if (string.IsNullOrWhiteSpace(values[key])) return Fail("missing " + key);
            if (string.IsNullOrWhiteSpace(values["rid"])) values["rid"] = "linux-x64";
            if (string.IsNullOrWhiteSpace(values["runtime"])) values["runtime"] = "native";
            if (string.IsNullOrWhiteSpace(values["stubWindmillBind"])) values["stubWindmillBind"] = "127.0.0.1:17292";
            var runtime = values["runtime"];
            if (runtime != "native" && runtime != "container") return Fail("runtime must be native or container");
            if (!SshTarget.IsMatch(values["sshTarget"])) return Fail("sshTarget must be <user>@<host>");
            if (!ServiceUser.IsMatch(values["serviceUser"])) return Fail("serviceUser is not a valid user name");
            var remoteRoot = values["remoteRoot"];
            if (!remoteRoot.StartsWith('/') || QuoteOrSpace.IsMatch(remoteRoot)) return Fail("remoteRoot must be an absolute path without quotes or spaces");
            foreach (var bindKey in new[] { "snapshotBind", "qualSnapshotBind" })
            {
                var bind = values[bindKey];
                if (string.IsNullOrWhiteSpace(bind)) continue;
                var match = BracketBind.Match(bind);
                if (!match.Success) match = HostBind.Match(bind);
                if (!match.Success) return Fail(bindKey + " must be host:port");
                var host = match.Groups[1].Value;
                if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) || host == "::1" || Loopback.IsMatch(host))
                    return Fail(bindKey + " must be a private non-loopback address");
                if ((host == "0.0.0.0" || host == "::" || host == "*") && runtime != "container")
                    return Fail(bindKey + " must not be a wildcard for the native runtime");
            }
            if (string.IsNullOrWhiteSpace(values["qualSnapshotBind"])) values["qualSnapshotBind"] = BindPort.Replace(values["snapshotBind"], ":17291");
            return new NightlyWatchdogProfileCheck(true, "", new NightlyWatchdogProfile(values));
        }

        /// <summary>ConvertTo-NightlyWatchdogUnit: @@SERVICE_USER@@ and @@REMOTE_ROOT@@ (without a trailing slash) substituted.</summary>
        public static string RenderUnit(string templateText, NightlyWatchdogProfile profile) =>
            (templateText ?? "").Replace("@@SERVICE_USER@@", profile.ServiceUser, StringComparison.Ordinal)
                .Replace("@@REMOTE_ROOT@@", profile.RemoteRoot.TrimEnd('/'), StringComparison.Ordinal);

        /// <summary>ConvertTo-NightlyWatchdogQualConfig: &lt;qualSnapshotBind&gt;, &lt;stubWindmillBind&gt; and &lt;runtime&gt; substituted.</summary>
        public static string RenderQualConfig(string templateText, NightlyWatchdogProfile profile) =>
            (templateText ?? "").Replace("<qualSnapshotBind>", profile.QualSnapshotBind, StringComparison.Ordinal)
                .Replace("<stubWindmillBind>", profile.StubWindmillBind, StringComparison.Ordinal)
                .Replace("<runtime>", profile.Runtime, StringComparison.Ordinal);

        /// <summary>
        /// Get-NightlyWatchdogEnvFragment: the owned non-secret KEY=value lines whose key the existing env text (its key
        /// names) does not set, in OwnedEnvKeys order.
        /// </summary>
        public static IReadOnlyList<string> EnvFragment(string existingEnvText, NightlyWatchdogProfile profile)
        {
            var present = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in (existingEnvText ?? "").Split('\n'))
            {
                var match = EnvKey.Match(line);
                if (match.Success) present.Add(match.Groups[1].Value);
            }
            var wanted = new[]
            {
                ("ANTIPHON_WATCHDOG_SNAPSHOT_BIND", profile.SnapshotBind),
                ("ANTIPHON_WATCHDOG_STATE_DIR", "./state"),
                ("ANTIPHON_WATCHDOG_NAMESPACE", "mc"),
            };
            return wanted.Where(pair => OwnedEnvKeys.Contains(pair.Item1) && !present.Contains(pair.Item1)).Select(pair => pair.Item1 + "=" + pair.Item2).ToArray();
        }

        /// <summary>The remote --self-check dry run: the installed binary's self-check, or a skip line when none is installed.</summary>
        public static string SelfCheckCommand(string root) =>
            "if [ -x '" + root + "/" + BinaryName + "' ]; then cd '" + root + "' && ./" + BinaryName + " --self-check; else echo 'self-check skipped: no binary deployed yet'; fi";

        /// <summary>The state directories, created and closed to other users.</summary>
        public static string StateDirectoriesCommand(string root) =>
            "mkdir -p '" + root + "/state' '" + root + "/state-qual' && chmod 700 '" + root + "/state' '" + root + "/state-qual'";

        /// <summary>The env file's key names only (KEY=), created 0600 when absent. Its values never leave the host.</summary>
        public static string EnvKeyNamesCommand(string root) =>
            "touch '" + root + "/env' && chmod 600 '" + root + "/env' && sed -n 's/^\\([A-Z0-9_]*\\)=.*/\\1=/p' '" + root + "/env'";

        /// <summary>The env lines appended (base64 of the ASCII lines, each ending in a line feed).</summary>
        public static string AppendEnvCommand(string root, IReadOnlyList<string> fragment)
        {
            var payload = Convert.ToBase64String(Encoding.ASCII.GetBytes(string.Join("\n", fragment) + "\n"));
            return "echo '" + payload + "' | base64 -d >> '" + root + "/env' && chmod 600 '" + root + "/env'";
        }

        /// <summary>Ownership, the binary's mode, then the unit installed, the daemon reloaded and the unit enabled and started.</summary>
        public static string InstallCommand(string root, string serviceUser) =>
            "sudo chown -R '" + serviceUser + "' '" + root + "/state' '" + root + "/state-qual' '" + root + "/env' && chmod 755 '" + root + "/" + BinaryName + "' 2>/dev/null; "
            + "sudo install -m 0644 '" + root + "/" + UnitName + "' /etc/systemd/system/" + UnitName + " && sudo systemctl daemon-reload && sudo systemctl enable --now " + UnitName;

        /// <summary>
        /// Invoke-NightlyWatchdogDeployment: resolves and checks the profile (exit 3 without contacting anything), then
        /// publishes, renders and runs the preflight or, with Deploy, the deploy. Lines go to host.Out, the closing REFUSED
        /// or FAILED line to host.Error. http answers the snapshot GET (the deploy's only HTTP call).
        /// </summary>
        public static NightlyWatchdogDeployResult Run(NightlyWatchdogDeployOptions options, NightlyHost host, IFileSystem files, NightlyReportHttp http)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (http == null) throw new ArgumentNullException(nameof(http));
            var result = new NightlyWatchdogDeployResult();

            var resolved = ResolveProfilePath(options, files);
            if (string.IsNullOrWhiteSpace(resolved) || !files.File.Exists(resolved)) return Refuse(result, host, NoProfile);
            string text;
            try { text = files.File.ReadAllText(resolved); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { text = null; }
            var check = Check(text);
            if (!check.Ok) return Refuse(result, host, "REFUSED: invalid deploy profile: " + check.Reason);
            result.ProfilePath = resolved;
            var profile = check.Profile;
            var root = profile.RemoteRoot.TrimEnd('/');
            var repoRoot = options.RepoRoot ?? "";

            try
            {
                // A deployed watchdog binary stamps HEAD. An unreadable HEAD refuses the publish.
                var sourceRevision = (host.RunGit(repoRoot, "rev-parse", "HEAD").Output ?? "").Trim().ToLowerInvariant();
                if (!Sha.IsMatch(sourceRevision)) throw new DeployFailure("git rev-parse HEAD failed; refusing an unstamped watchdog publish.");

                var workDir = string.IsNullOrWhiteSpace(options.WorkDir)
                    ? files.Path.Combine(files.Path.GetTempPath(), "antiphon-watchdog-deploy-" + Guid.NewGuid().ToString("N"))
                    : options.WorkDir;
                files.Directory.CreateDirectory(workDir);

                host.Out.WriteLine("Deploy profile: " + resolved);
                foreach (var key in NightlyWatchdogProfile.PrintedKeys) host.Out.WriteLine("  " + key + ": " + profile[key]);

                var publishDir = files.Path.Combine(workDir, "publish");
                var project = files.Path.Combine(repoRoot, "src", BinaryName, BinaryName + ".csproj");
                Required(Launch(host, repoRoot, "dotnet", "publish", project, "-c", "Release", "-r", profile.Rid, "--self-contained", "-p:PublishSingleFile=true",
                    "-p:SourceRevisionId=" + sourceRevision, "-o", publishDir), "publish");
                var binary = files.Path.Combine(publishDir, BinaryName);
                var hasBinary = files.File.Exists(binary);
                if (hasBinary)
                {
                    using (var stream = files.File.OpenRead(binary)) result.Checksum = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
                    host.Out.WriteLine("Binary sha256: " + result.Checksum);
                }

                var assets = files.Path.Combine(repoRoot, "scripts", "nightly-watchdog");
                var unitPath = files.Path.Combine(workDir, UnitName);
                var qualPath = files.Path.Combine(workDir, "qual.json");
                result.RenderedUnit = RenderUnit(files.File.ReadAllText(files.Path.Combine(assets, UnitName + ".template")), profile);
                result.RenderedQual = RenderQualConfig(files.File.ReadAllText(files.Path.Combine(assets, "qual.example.json")), profile);
                if (result.RenderedUnit.Contains("@@", StringComparison.Ordinal) || Placeholder.IsMatch(result.RenderedQual))
                    throw new DeployFailure("template rendering left a placeholder");
                files.File.WriteAllText(unitPath, result.RenderedUnit.Replace("\r\n", "\n"), new UTF8Encoding(false));
                files.File.WriteAllText(qualPath, result.RenderedQual.Replace("\r\n", "\n"), new UTF8Encoding(false));

                if (!options.Deploy)
                {
                    var dry = Ssh(host, profile, SelfCheckCommand(root));
                    host.Out.WriteLine("Remote self-check exit: " + dry.ExitCode.ToString(CultureInfo.InvariantCulture));
                    result.ExitCode = 0;
                    result.Message = "PREFLIGHT OK (read-only; pass --deploy to install)";
                    host.Out.WriteLine(result.Message);
                    return result;
                }

                Required(Ssh(host, profile, StateDirectoriesCommand(root)), "create state directories");
                if (hasBinary) Required(Scp(host, profile, binary, root + "/" + BinaryName), "upload binary");
                Required(Scp(host, profile, unitPath, root + "/" + UnitName), "upload unit");
                Required(Scp(host, profile, qualPath, root + "/qual.json"), "upload qualification config");

                // Key names only: the existing env file is never printed.
                var keys = Required(Ssh(host, profile, EnvKeyNamesCommand(root)), "read env key names");
                var fragment = EnvFragment(keys.LogText, profile);
                result.EnvFragment = fragment;
                if (fragment.Count > 0)
                {
                    Required(Ssh(host, profile, AppendEnvCommand(root, fragment)), "append owned env keys");
                    host.Out.WriteLine("Env keys added: " + string.Join(", ", fragment.Select(line => line.Split('=')[0])));
                }

                Required(Ssh(host, profile, InstallCommand(root, profile.ServiceUser)), "install and enable unit");

                var snapshot = http("GET", profile.SnapshotUrl, new Dictionary<string, string>(), null);
                if (snapshot == null || snapshot.Status != 200) throw new DeployFailure("snapshot verification failed");
                result.ExitCode = 0;
                result.Message = "DEPLOY OK: unit enabled and snapshot answered";
                host.Out.WriteLine(result.Message);
                return result;
            }
            catch (Exception e) when (e is DeployFailure || e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException
                || e is Win32Exception || e is HttpRequestException || e is OperationCanceledException || e is ArgumentException || e is FormatException
                || e is NotSupportedException)
            {
                result.ExitCode = 1;
                result.Message = "FAILED: " + e.Message.Replace("\r", " ").Replace("\n", " ");
                host.Error.WriteLine(result.Message);
                return result;
            }
        }

        private static NightlyWatchdogDeployResult Refuse(NightlyWatchdogDeployResult result, NightlyHost host, string message)
        {
            result.ExitCode = 3;
            result.Message = message;
            host.Error.WriteLine(message);
            return result;
        }

        // ssh [-i <identity>] -o BatchMode=yes <target> <command>: the remote shell runs the command; the output is captured.
        private static NightlyLaunchResult Ssh(NightlyHost host, NightlyWatchdogProfile profile, string command)
        {
            var arguments = Identity(profile);
            arguments.AddRange(new[] { "-o", "BatchMode=yes", profile.SshTarget, command });
            return Launch(host, "", "ssh", arguments.ToArray());
        }

        // scp [-i <identity>] -o BatchMode=yes <source> <target>:<remotePath>.
        private static NightlyLaunchResult Scp(NightlyHost host, NightlyWatchdogProfile profile, string source, string remotePath)
        {
            var arguments = Identity(profile);
            arguments.AddRange(new[] { "-o", "BatchMode=yes", source, profile.SshTarget + ":" + remotePath });
            return Launch(host, "", "scp", arguments.ToArray());
        }

        private static List<string> Identity(NightlyWatchdogProfile profile) =>
            string.IsNullOrWhiteSpace(profile.SshIdentityFile) ? new List<string>() : new List<string> { "-i", profile.SshIdentityFile };

        private static NightlyLaunchResult Launch(NightlyHost host, string workingDirectory, string fileName, params string[] arguments) =>
            host.Launch(new NightlyLaunch(fileName, arguments, workingDirectory, CaptureOutput: true));

        private static NightlyLaunchResult Required(NightlyLaunchResult launched, string operation)
        {
            if (launched == null || launched.ExitCode != 0) throw new DeployFailure(operation + " failed");
            return launched;
        }

        private static string Text(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Null => "",
            _ => value.GetRawText(),
        };

        // One failed step: the message is the reason the FAILED line names.
        private sealed class DeployFailure : Exception
        {
            public DeployFailure(string message) : base(message) { }
        }
    }
}
