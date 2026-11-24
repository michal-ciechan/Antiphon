// CARD-1744 S4, CARD-0716, CARD-1221: the Windows phone-home runner's configuration and environment rules (unit 36 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md; it replaced scripts/lib/windows-phone-home-env.ps1's
// functions). The rules are pure: no file IO and no process lookup. The callers (the autostart-session-runner and
// start-windows-phone-home-runner commands, and restart-apphost's launch refusal) read the configuration file, test the
// secret path and resolve pwsh and the agent executables.
//
//   ParseConfig      ConvertFrom-WindowsPhoneHomeConfig: the phone-home.json text, refused by its first broken rule
//   RunnerEnvironment New-WindowsPhoneHomeRunnerEnv: the runner's variables, in their order
//   AutostartPlan    Resolve-SessionRunnerAutostartPlan: direct, phone-home or refused
//   LaunchRefusal    Get-WindowsPhoneHomeLaunchRefusal: the desktop AppHost launch refusal while a phase 2 file exists
//
// The configuration is one JSON object read by System.Text.Json (comments and trailing commas allowed, as pwsh 7's
// ConvertFrom-Json read them; a single-quoted string is malformed). Keys match exactly; two keys that differ only in case
// are malformed (ConvertFrom-Json refused them), an exact repeat keeps the last value. Plain C#: no PowerShell types. ASCII
// only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>A parsed phone-home.json: Ok with the values, or the refusal reason (the values unset).</summary>
    public sealed record WindowsPhoneHomeConfig(bool Ok, string Reason, string RunnerId, string ServerOrigin, int Capacity, string Root, int Port)
    {
        /// <summary>A refusal.</summary>
        public static WindowsPhoneHomeConfig Refused(string reason) => new WindowsPhoneHomeConfig(false, reason, null, null, 0, null, 0);
    }

    /// <summary>The runner a phone-home environment describes (New-WindowsPhoneHomeRunnerEnv's parameters).</summary>
    public sealed record WindowsPhoneHomeRunner(string RunnerId, string Root, int Port, int Capacity, string ServerOrigin, string ClaudeHome,
        string CodexHome, string GrokHome, string PwshPath, IReadOnlyList<KeyValuePair<string, string>> Executables);

    /// <summary>The autostart plan's mode.</summary>
    public enum SessionRunnerAutostartMode
    {
        /// <summary>No configuration: the direct runner on 17204.</summary>
        Direct,

        /// <summary>A valid configuration with its secret: the phone-home runner.</summary>
        PhoneHome,

        /// <summary>A configuration that is present but refused: nothing starts.</summary>
        Refused,
    }

    /// <summary>The autostart plan: the mode, the refusal reason, the runner's added variables (in order) and its arguments.</summary>
    public sealed record SessionRunnerAutostartPlan(SessionRunnerAutostartMode Mode, string Reason, IReadOnlyList<KeyValuePair<string, string>> Environment, string ExeArgs)
    {
        /// <summary>The mode as the plan named it: direct, phone-home or refused.</summary>
        public string ModeName => Mode == SessionRunnerAutostartMode.PhoneHome ? "phone-home" : Mode == SessionRunnerAutostartMode.Refused ? "refused" : "direct";
    }

    /// <summary>The Windows phone-home runner's configuration and environment.</summary>
    public static class WindowsPhoneHome
    {
        /// <summary>The configuration file a phone-home desktop runner reads (phase 2).</summary>
        public const string DefaultConfigPath = @"C:\Antiphon\runner\phone-home.json";

        /// <summary>The runner root when the configuration names none.</summary>
        public const string DefaultRoot = @"C:\Antiphon\runner";

        /// <summary>The broker port: the desktop runner's, and the only one a default-path configuration may name.</summary>
        public const int BrokerPort = 17204;

        /// <summary>The second runner's port (phase 1, a laptop).</summary>
        public const int SecondPort = 17214;

        /// <summary>The secret file under the runner root.</summary>
        public const string SecretChild = @"secrets\phone-home";

        private static readonly Regex RunnerIdPattern = new Regex("^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant);
        private static readonly Regex DriveRooted = new Regex(@"^[A-Za-z]:[\\/]", RegexOptions.CultureInvariant);
        private static readonly Regex DriveLetter = new Regex("^[A-Za-z]:", RegexOptions.CultureInvariant);
        private static readonly string[] Keys = { "runnerId", "serverOrigin", "capacity", "root", "port" };
        private static readonly string[] Required = { "runnerId", "serverOrigin", "capacity" };
        private static readonly JsonDocumentOptions Reading = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

        /// <summary>
        /// The refusal of a desktop AppHost launch while a phone-home configuration is present (the Linux server serves, so the
        /// desktop stack must not start against it), or null when there is none. A blank path names the default file.
        /// </summary>
        public static string LaunchRefusal(bool configPresent, string configPath)
        {
            if (!configPresent) return null;
            if (string.IsNullOrWhiteSpace(configPath)) configPath = DefaultConfigPath;
            return "REFUSED: phone-home-config-present" + Environment.NewLine
                + "If the Linux server is serving, this refusal is correct: do NOT delete the file. Only during ROLLBACK, after the Linux server is stopped and pg_stat_activity is clear: delete "
                + configPath + ", run scripts\\antiphon.cmd restart-session-runner --hard, then retry.";
        }

        /// <summary>
        /// ConvertTo-DriveAbsolute: a drive-rooted path with its dot segments resolved and backslashes (C:\a\b); "" for a blank
        /// path; any other path through Path.GetFullPath.
        /// </summary>
        public static string DriveAbsolute(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            if (!DriveRooted.IsMatch(path)) return Path.GetFullPath(path);
            var slash = path.Replace('/', '\\');
            var drive = slash.Substring(0, 2);
            var parts = new List<string>();
            foreach (var segment in slash.Substring(2).Split('\\'))
            {
                if (segment.Length == 0 || segment == ".") continue;
                if (segment == "..")
                {
                    if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
                    continue;
                }
                parts.Add(segment);
            }
            return parts.Count == 0 ? drive + "\\" : drive + "\\" + string.Join("\\", parts);
        }

        /// <summary>ConvertTo-PhoneHomeSlash: forward slashes, no trailing slash ("" for null).</summary>
        public static string Slash(string path) => path == null ? "" : path.Replace('\\', '/').TrimEnd('/');

        /// <summary>
        /// Join-PhoneHomeChild: right under left, in left's separator (a drive-lettered or backslashed left joins with a
        /// backslash, anything else with a slash); a blank left is right alone. Pure: the same text on every platform.
        /// </summary>
        public static string ChildPath(string left, string right)
        {
            var rightNorm = (right ?? "").TrimStart('\\', '/');
            if (string.IsNullOrWhiteSpace(left)) return rightNorm;
            var leftTrim = left.TrimEnd('\\', '/');
            if (DriveLetter.IsMatch(leftTrim) || leftTrim.Contains('\\')) return leftTrim + "\\" + rightNorm.Replace('/', '\\');
            return leftTrim + "/" + rightNorm.Replace('\\', '/');
        }

        /// <summary>
        /// New-WindowsPhoneHomeRunnerEnv: the runner's variables in order. Every runner path lives under the root in
        /// drive-rooted, forward-slash form (the spelling the runner reports back); the secret path keeps the root's own form.
        /// </summary>
        public static IReadOnlyList<KeyValuePair<string, string>> RunnerEnvironment(WindowsPhoneHomeRunner runner)
        {
            if (runner == null) throw new ArgumentNullException(nameof(runner));
            var rootSlash = Slash(DriveAbsolute(runner.Root));
            var work = rootSlash + "/work";
            var state = rootSlash + "/state";
            var list = new List<KeyValuePair<string, string>>();
            void Set(string key, string value)
            {
                var at = list.FindIndex(pair => pair.Key == key);
                if (at >= 0) list[at] = new KeyValuePair<string, string>(key, value);
                else list.Add(new KeyValuePair<string, string>(key, value));
            }
            Set("ASPNETCORE_URLS", "http://localhost:" + runner.Port.ToString(CultureInfo.InvariantCulture));
            Set("PhoneHome__Enabled", "true");
            Set("PhoneHome__RunnerId", runner.RunnerId ?? "");
            Set("PhoneHome__ServerOrigin", runner.ServerOrigin ?? "");
            Set("PhoneHome__SecretPath", ChildPath(runner.Root, SecretChild));
            Set("PhoneHome__StoreIdPath", state + "/runner-store-id");
            Set("PhoneHome__CapacityStatePath", state + "/runner-capacity");
            Set("PhoneHome__AllowedCwd", work);
            Set("PhoneHome__RunnerRepository", work + "/repos/antiphon");
            Set("PhoneHome__Capacity", runner.Capacity.ToString(CultureInfo.InvariantCulture));
            Set("PhoneHome__ClaudeHome", runner.ClaudeHome ?? "");
            Set("PhoneHome__CodexHome", runner.CodexHome ?? "");
            Set("PhoneHome__GrokHome", runner.GrokHome ?? "");
            Set("PhoneHome__RawExeAllowList__0", runner.PwshPath ?? "");
            Set("SessionRunner__SessionLogPath", state + "/session-runner");
            Set("SessionRunner__PtyHostDir", state + "/pty-hosts");
            Set("SessionRunner__Herdr__Enabled", "false");
            Set("SessionRunner__HostStats__HistoryEnabled", "false");
            Set("SessionRunner__HostStats__Volumes__0", work);
            Set("Serilog__LogPath", state + "/logs");
            foreach (var executable in runner.Executables ?? Array.Empty<KeyValuePair<string, string>>())
                Set("PhoneHome__Executables__" + executable.Key, executable.Value ?? "");
            return list;
        }

        /// <summary>Get-PhoneHomeRootRefusal: root-device, root-unc or root-not-drive-rooted; null for a drive-rooted root.</summary>
        public static string RootRefusal(string root)
        {
            if (string.IsNullOrWhiteSpace(root)) return "root-not-drive-rooted";
            var text = root.Trim();
            if (text.StartsWith(@"\\?\", StringComparison.Ordinal) || text.StartsWith(@"\\.\", StringComparison.Ordinal)
                || text.StartsWith("//?/", StringComparison.Ordinal) || text.StartsWith("//./", StringComparison.Ordinal))
                return "root-device";
            if (text.StartsWith(@"\\", StringComparison.Ordinal) || text.StartsWith("//", StringComparison.Ordinal)) return "root-unc";
            return DriveRooted.IsMatch(text) ? null : "root-not-drive-rooted";
        }

        /// <summary>
        /// ConvertFrom-WindowsPhoneHomeConfig: the configuration text, checked in order: ASCII JSON object (config-malformed),
        /// only the five keys (unknown-key:&lt;name&gt;), runnerId, serverOrigin and capacity present (missing-key:&lt;name&gt;), the
        /// runner id (runner-id-reserved for local or desktop in any case, runner-id-invalid), the origin (origin-invalid,
        /// origin-not-https, origin-userinfo, origin-query, origin-fragment, origin-path), the capacity (an integer,
        /// capacity-invalid; 1 or 2, capacity-out-of-range), the root (default C:\Antiphon\runner; a string that is drive-rooted,
        /// not UNC and not a device path) and the port (default 17204; 17204 or 17214, port-invalid). requirePort, when not 0,
        /// refuses any other port (port-not-broker).
        /// </summary>
        public static WindowsPhoneHomeConfig ParseConfig(string text, int requirePort = 0)
        {
            if (text == null || text.Any(ch => ch > 127)) return WindowsPhoneHomeConfig.Refused("config-malformed");
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(text, Reading);
            }
            catch (JsonException)
            {
                return WindowsPhoneHomeConfig.Refused("config-malformed");
            }
            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return WindowsPhoneHomeConfig.Refused("config-malformed");
                var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in root.EnumerateObject())
                {
                    if (seen.TryGetValue(property.Name, out var earlier) && earlier != property.Name) return WindowsPhoneHomeConfig.Refused("config-malformed");
                    seen[property.Name] = property.Name;
                }
                var present = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                foreach (var property in root.EnumerateObject())
                {
                    if (!Keys.Contains(property.Name, StringComparer.Ordinal)) return WindowsPhoneHomeConfig.Refused("unknown-key:" + property.Name);
                    present[property.Name] = property.Value.Clone();
                }
                foreach (var name in Required)
                    if (!present.ContainsKey(name)) return WindowsPhoneHomeConfig.Refused("missing-key:" + name);

                var runnerId = Text(present["runnerId"]);
                if (runnerId == null) return WindowsPhoneHomeConfig.Refused("runner-id-invalid");
                if (runnerId.Equals("local", StringComparison.OrdinalIgnoreCase) || runnerId.Equals("desktop", StringComparison.OrdinalIgnoreCase))
                    return WindowsPhoneHomeConfig.Refused("runner-id-reserved");
                if (!RunnerIdPattern.IsMatch(runnerId)) return WindowsPhoneHomeConfig.Refused("runner-id-invalid");

                var originValue = present["serverOrigin"];
                if (originValue.ValueKind != JsonValueKind.String) return WindowsPhoneHomeConfig.Refused("origin-invalid");
                var origin = originValue.GetString();
                if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return WindowsPhoneHomeConfig.Refused("origin-invalid");
                if (uri.Scheme != "https") return WindowsPhoneHomeConfig.Refused("origin-not-https");
                if (!string.IsNullOrEmpty(uri.UserInfo)) return WindowsPhoneHomeConfig.Refused("origin-userinfo");
                if (!string.IsNullOrEmpty(uri.Query)) return WindowsPhoneHomeConfig.Refused("origin-query");
                if (!string.IsNullOrEmpty(uri.Fragment)) return WindowsPhoneHomeConfig.Refused("origin-fragment");
                if (uri.AbsolutePath != "/" && uri.AbsolutePath != "") return WindowsPhoneHomeConfig.Refused("origin-path");

                if (Integral(present["capacity"]) is not long capacity) return WindowsPhoneHomeConfig.Refused("capacity-invalid");
                if (capacity != 1 && capacity != 2) return WindowsPhoneHomeConfig.Refused("capacity-out-of-range");

                var runnerRoot = DefaultRoot;
                if (present.TryGetValue("root", out var rootValue))
                {
                    if (rootValue.ValueKind != JsonValueKind.String) return WindowsPhoneHomeConfig.Refused("root-not-drive-rooted");
                    var refusal = RootRefusal(rootValue.GetString());
                    if (refusal != null) return WindowsPhoneHomeConfig.Refused(refusal);
                    runnerRoot = rootValue.GetString();
                }

                var port = BrokerPort;
                if (present.TryGetValue("port", out var portValue))
                {
                    if (Integral(portValue) is not long portNumber || (portNumber != BrokerPort && portNumber != SecondPort))
                        return WindowsPhoneHomeConfig.Refused("port-invalid");
                    port = (int)portNumber;
                }
                if (requirePort != 0 && port != requirePort) return WindowsPhoneHomeConfig.Refused("port-not-broker");
                return new WindowsPhoneHomeConfig(true, null, runnerId, origin, (int)capacity, runnerRoot, port);
            }
        }

        /// <summary>
        /// Resolve-SessionRunnerAutostartPlan: no configuration text is the direct runner on 17204; otherwise the configuration
        /// (at the default path only port 17204), its secret and the checkout must hold, and the plan is the phone-home runner
        /// on the configured port with its variables (the homes are .claude, .codex and .grok under homeDir).
        /// </summary>
        public static SessionRunnerAutostartPlan AutostartPlan(string configText, bool explicitConfigPath, bool secretPresent, string homeDir, string pwshPath,
            IReadOnlyList<KeyValuePair<string, string>> executables, string repoRoot)
        {
            var none = Array.Empty<KeyValuePair<string, string>>();
            if (configText == null)
                return new SessionRunnerAutostartPlan(SessionRunnerAutostartMode.Direct, null, none, "--urls http://localhost:" + BrokerPort.ToString(CultureInfo.InvariantCulture));
            var parsed = ParseConfig(configText, explicitConfigPath ? 0 : BrokerPort);
            if (!parsed.Ok) return new SessionRunnerAutostartPlan(SessionRunnerAutostartMode.Refused, parsed.Reason, none, null);
            if (!secretPresent) return new SessionRunnerAutostartPlan(SessionRunnerAutostartMode.Refused, "secret-missing", none, null);
            if (string.IsNullOrWhiteSpace(repoRoot)) return new SessionRunnerAutostartPlan(SessionRunnerAutostartMode.Refused, "repo-root-missing", none, null);
            var environment = RunnerEnvironment(new WindowsPhoneHomeRunner(parsed.RunnerId, parsed.Root, parsed.Port, parsed.Capacity, parsed.ServerOrigin,
                ChildPath(homeDir, ".claude"), ChildPath(homeDir, ".codex"), ChildPath(homeDir, ".grok"), pwshPath, executables));
            return new SessionRunnerAutostartPlan(SessionRunnerAutostartMode.PhoneHome, null, environment,
                "--urls http://localhost:" + parsed.Port.ToString(CultureInfo.InvariantCulture));
        }

        // [string] of the value as the script cast it: a string, a number's text, True or False, "" for null; null for an
        // object or an array (never a valid runner id).
        private static string Text(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "True",
            JsonValueKind.False => "False",
            JsonValueKind.Null => "",
            _ => null,
        };

        // Test-JsonIntegral: a JSON integer that fits a long (ConvertFrom-Json read 1.0 and 1e0 as Double, a larger integer
        // as a BigInteger; neither was integral).
        private static long? Integral(JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Number) return null;
            var raw = value.GetRawText();
            if (raw.IndexOfAny(new[] { '.', 'e', 'E' }) >= 0) return null;
            return value.TryGetInt64(out var number) ? number : (long?)null;
        }
    }
}
