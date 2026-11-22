// W2-nightly S03: the C# twin of scripts/lib/toolchain-floor.ps1 (CARD-1698 host toolchain floors; plan
// 2026-11-21-w2-nightly-port-slices.md). Warn, never refuse (operator decision 2026-11-03). The ps1 stays: the restart
// chain (session-runner-restart-health.ps1, restart-server.ps1) dot-sources it and must not depend on the compiled
// core. The run core (NightlyRun.cs) and the tests run (NightlyTests.cs) call this twin; ToolchainFloorTwinTests holds the two to the
// same rows and warnings on the same inputs.
//
// The manifest names where each floor is declared; it restates no version number.
// - git: docker/git-minimum-version.sh (GIT_MINIMUM_VERSION=, CARD-1260), the same file both images run at build time.
// - .NET SDK: global.json sdk.version (rollForward latestMinor: an SDK of the same major at or above the pin satisfies it).
// - pwsh, node/npm, docker: floor still to decide, listed but not enforced.
// A floor or a version that cannot be read becomes a warning, so no entry point changes its exit code.
// Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>One Get-ToolchainFloorManifest row: where the tool's floor is declared and what an old tool breaks.</summary>
    public sealed record ToolchainFloorEntry(string Tool, string Source, bool Enforced, string Impact, string FixWindows, string FixOther);

    /// <summary>One report row. Status: ok, below-floor, version-unreadable, floor-unreadable or not-enforced.</summary>
    public sealed record ToolchainFloorRow(string Tool, string Found, string Floor, string Source, bool Enforced, string Status);

    /// <summary>One warning: the row's tool, found version and floor, the manifest's impact and the host's fix, the status as Reason, and the printed Message.</summary>
    public sealed record ToolchainFloorWarning(string Tool, string Found, string Floor, string Impact, string Fix, string Reason, string Message);

    /// <summary>Get-ToolchainFloorReport: one row per manifest entry, and the warnings of the rows that need one.</summary>
    public sealed record ToolchainFloorReport(IReadOnlyList<ToolchainFloorRow> Tools, IReadOnlyList<ToolchainFloorWarning> Warnings);

    /// <summary>The toolchain floor checks of toolchain-floor.ps1.</summary>
    public static class ToolchainFloor
    {
        private static readonly Regex VersionPrefix = new Regex(@"^(\d+)\.(\d+)(?:\.(\d+))?", RegexOptions.IgnoreCase);
        private static readonly Regex GitFloorLine = new Regex(@"(?m)^GIT_MINIMUM_VERSION=(\d+\.\d+)\r?$");
        private static readonly Regex SdkPin = new Regex(@"^\d+\.\d+\.\d+$", RegexOptions.IgnoreCase);
        private static readonly Regex GitVersionLine = new Regex(@"^\s*git version (\d+\.\d+(?:\.\d+)?)");
        private static readonly Regex SdkListLine = new Regex(@"^\s*(\d+\.\d+\.\d+)\S*\s+\[");
        private static readonly Regex LineBreak = new Regex("\r?\n");

        private static readonly ToolchainFloorEntry[] Entries =
        {
            new ToolchainFloorEntry("git", "docker/git-minimum-version.sh", true,
                "lands will fail: git show-ref --exists needs the floor (LandingGit, LandOperationFactory, guarded worktree and verification removal return source_ref_error)",
                "winget upgrade Git.Git",
                "install Git at or above the floor (the images build Git from source; see docs/docker-stack.md)"),
            new ToolchainFloorEntry("dotnet-sdk", "global.json", true,
                "builds and tests will fail: global.json pins the SDK (rollForward latestMinor needs the same major at or above the pin)",
                "winget install Microsoft.DotNet.SDK.10",
                "install the pinned SDK or a later patch of its major (https://dot.net/v1/dotnet-install.sh --channel 10.0)"),
            new ToolchainFloorEntry("pwsh", "", false, "", "", ""),
            new ToolchainFloorEntry("node", "", false, "", "", ""),
            new ToolchainFloorEntry("docker", "", false, "", "", ""),
        };

        /// <summary>Get-ToolchainFloorManifest: git, dotnet-sdk (enforced), then pwsh, node and docker (listed, not enforced).</summary>
        public static IReadOnlyList<ToolchainFloorEntry> Manifest() => Entries;

        /// <summary>Test-ToolchainFloorWindowsHost: the fix column the warnings name.</summary>
        public static bool IsWindowsHost() => OperatingSystem.IsWindows();

        /// <summary>ConvertTo-ToolchainFloorVersion: major.minor[.build] from the start of the text (build 0 when absent); null when it does not start so.</summary>
        public static Version ParseVersion(string text)
        {
            var match = VersionPrefix.Match(text ?? "");
            if (!match.Success) return null;
            var build = match.Groups[3].Success && match.Groups[3].Value.Length > 0 ? int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
            return new Version(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture), build);
        }

        /// <summary>
        /// Get-ToolchainFloorValue: git's GIT_MINIMUM_VERSION (major.minor) from docker/git-minimum-version.sh, the SDK's
        /// sdk.version (major.minor.patch) from global.json; empty when the file is missing, does not declare it, cannot be
        /// read, or the tool has no floor source.
        /// </summary>
        public static string Value(string repoRoot, string tool)
        {
            if (string.IsNullOrEmpty(repoRoot)) return "";
            try
            {
                if (tool == "git")
                {
                    var path = Path.Combine(repoRoot, "docker", "git-minimum-version.sh");
                    if (!File.Exists(path)) return "";
                    var match = GitFloorLine.Match(File.ReadAllText(path));
                    return match.Success ? match.Groups[1].Value : "";
                }
                if (tool == "dotnet-sdk")
                {
                    var path = Path.Combine(repoRoot, "global.json");
                    if (!File.Exists(path)) return "";
                    var options = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
                    using var document = JsonDocument.Parse(File.ReadAllText(path), options);
                    var sdk = Property(document.RootElement, "sdk");
                    if (sdk == null || sdk.Value.ValueKind != JsonValueKind.Object) return "";
                    var version = Property(sdk.Value, "version");
                    if (version == null || version.Value.ValueKind != JsonValueKind.String) return "";
                    var text = version.Value.GetString() ?? "";
                    return SdkPin.IsMatch(text) ? text : "";
                }
            }
            catch (Exception)
            {
                // The ps1's catch-all: a floor that cannot be read is a warning, never an error.
            }
            return "";
        }

        /// <summary>
        /// Get-ToolchainFloorVersionText: the raw version text, `git --version` for git and `dotnet --list-sdks` for the
        /// SDK, as Out-String gives it (each stdout line followed by a newline; stderr dropped). Empty for another tool or
        /// when the tool cannot run.
        /// </summary>
        public static string VersionText(string tool)
        {
            string[] command = tool == "git" ? new[] { "git", "--version" } : tool == "dotnet-sdk" ? new[] { "dotnet", "--list-sdks" } : null;
            if (command == null) return "";
            try
            {
                var start = new ProcessStartInfo(command[0])
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                start.ArgumentList.Add(command[1]);
                using var process = Process.Start(start);
                if (process == null) return "";
                var error = process.StandardError.ReadToEndAsync();
                var text = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                error.GetAwaiter().GetResult();
                var lines = LineBreak.Split(text);
                var count = lines.Length > 0 && lines[lines.Length - 1].Length == 0 ? lines.Length - 1 : lines.Length;
                var output = new StringBuilder();
                for (var i = 0; i < count; i++) output.Append(lines[i]).Append(Environment.NewLine);
                return output.ToString();
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is InvalidOperationException || e is IOException)
            {
                return "";
            }
        }

        /// <summary>
        /// Get-ToolchainFloorFound: git's "git version x.y[.z]" at the start of the text; for the SDK, the highest
        /// "x.y.z[suffix] [path]" line of the floor's major, else the highest of any major. Null when none is read or the
        /// tool has no reader.
        /// </summary>
        public static string Found(string tool, string text, Version floor)
        {
            text ??= "";
            if (tool == "git")
            {
                var match = GitVersionLine.Match(text);
                return match.Success ? match.Groups[1].Value : null;
            }
            if (tool == "dotnet-sdk")
            {
                string best = null, bestSame = null;
                foreach (var line in LineBreak.Split(text))
                {
                    var match = SdkListLine.Match(line);
                    if (!match.Success) continue;
                    var value = match.Groups[1].Value;
                    var version = ParseVersion(value);
                    if (best == null || version > ParseVersion(best)) best = value;
                    if (floor != null && version.Major == floor.Major && (bestSame == null || version > ParseVersion(bestSame))) bestSame = value;
                }
                return bestSame ?? best;
            }
            return null;
        }

        /// <summary>Get-ToolchainFloorReport on this host (IsWindowsHost picks the fix column).</summary>
        public static ToolchainFloorReport Report(string repoRoot, Func<string, string> versionSource = null) =>
            Report(repoRoot, versionSource, IsWindowsHost());

        /// <summary>
        /// Get-ToolchainFloorReport: versionSource(tool) gives the raw version text (null: VersionText, the real probes); a
        /// source that throws leaves the version unread, and the warning quotes its message. windowsHost picks the fix.
        /// </summary>
        public static ToolchainFloorReport Report(string repoRoot, Func<string, string> versionSource, bool windowsHost)
        {
            var rows = new List<ToolchainFloorRow>();
            var warnings = new List<ToolchainFloorWarning>();
            foreach (var entry in Entries)
            {
                if (!entry.Enforced)
                {
                    rows.Add(new ToolchainFloorRow(entry.Tool, "", "to decide", entry.Source, false, "not-enforced"));
                    continue;
                }
                var fix = windowsHost ? entry.FixWindows : entry.FixOther;
                var floorText = Value(repoRoot, entry.Tool);
                var text = "";
                var readError = "";
                try { text = (versionSource != null ? versionSource(entry.Tool) : VersionText(entry.Tool)) ?? ""; }
                catch (Exception e) { readError = e.Message ?? ""; }
                var floor = floorText.Length > 0 ? ParseVersion(floorText) : null;
                var found = Found(entry.Tool, text, floor) ?? "";
                string status;
                var message = "";
                if (floor == null)
                {
                    status = "floor-unreadable";
                    message = string.Format(CultureInfo.InvariantCulture, "WARNING: the {0} floor could not be read from {1}; {0} was not checked.", entry.Tool, entry.Source);
                }
                else if (found.Length == 0)
                {
                    status = "version-unreadable";
                    var raw = text.Trim();
                    if (readError.Length > 0) raw = readError;
                    if (raw.Length > 120) raw = raw.Substring(0, 120);
                    message = string.Format(CultureInfo.InvariantCulture, "WARNING: the {0} version could not be read ('{1}'); the minimum is {2}; if it is older, {3}. Fix: {4}",
                        entry.Tool, raw, floorText, entry.Impact, fix);
                }
                else if (ParseVersion(found) < floor || (entry.Tool == "dotnet-sdk" && ParseVersion(found).Major != floor.Major))
                {
                    status = "below-floor";
                    message = string.Format(CultureInfo.InvariantCulture, "WARNING: {0} {1} is below the minimum {2}; {3}. Fix: {4}", entry.Tool, found, floorText, entry.Impact, fix);
                }
                else
                {
                    status = "ok";
                }
                if (message.Length > 0) warnings.Add(new ToolchainFloorWarning(entry.Tool, found, floorText, entry.Impact, fix, status, message));
                rows.Add(new ToolchainFloorRow(entry.Tool, found, floorText, entry.Source, true, status));
            }
            return new ToolchainFloorReport(rows, warnings);
        }

        /// <summary>Get-ToolchainFloorWarnings: the report's warnings; never throws (a failed check is one check-failed warning).</summary>
        public static IReadOnlyList<ToolchainFloorWarning> Warnings(string repoRoot, Func<string, string> versionSource = null)
        {
            try { return Report(repoRoot, versionSource).Warnings; }
            catch (Exception e)
            {
                return new[] { new ToolchainFloorWarning("toolchain", "", "", "", "", "check-failed", "WARNING: the toolchain floor check failed: " + e.Message) };
            }
        }

        // PowerShell's member access on ConvertFrom-Json output: the first property of that name, ignoring case.
        private static JsonElement? Property(JsonElement element, string name)
        {
            if (element.ValueKind != JsonValueKind.Object) return null;
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) return property.Value;
            }
            return null;
        }
    }
}
