// The live-lane discovery of live-lane-manifest.ps1 (CARD-1545 S1a), compiled in W2-nightly S24: list the tests of one
// owned test assembly (never run them), overlay each node's display name and parsed [Arguments] from the diagnostic
// log, and keep the capture under .antiphon/live-lane-capture. The listing is NightlyDiscoveryProducer.Produce (the
// twin of Invoke-NightlyProduceDiscovery) on a host whose launcher admits only "dotnet exec <bound assembly>
// --list-tests ..." (LiveLaneDiscoveryLauncher, the old StartProcess seam); the manifest no longer loads
// nightly-tests-impl.ps1 for Invoke-NightlyOwnedProcess.
//
// The child runs under OwnedLauncher on Windows (a private job: a timed-out listing's whole tree is killed, its output
// drained to the log) and as a plain redirected child elsewhere (LiveLanePlainLauncher, what the seam started on every
// platform). A timed-out listing is observed as exit 124, as the seam reported it. No provider call, no live Claude.
// Plain C#. ASCII-only.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>
    /// A discovered test node as the live-lane manifest reads it: the capture's node (project, uid, namespace, type,
    /// method, displayName, arguments, signature, categories) or a JSON node that names its class instead of its type.
    /// </summary>
    public sealed class LiveLaneNode
    {
        public string Project { get; set; } = "";
        public string Uid { get; set; } = "";
        public string Namespace { get; set; } = "";
        public string Type { get; set; } = "";

        /// <summary>A node read from JSON that has "class" (Get-LiveLaneMatchingNodes falls back to it when type is blank).</summary>
        public string Class { get; set; } = "";

        public string Method { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public IReadOnlyList<string> Arguments { get; set; } = new string[0];
        public string Signature { get; set; } = "";
        public IReadOnlyList<string> Categories { get; set; } = new string[0];

        /// <summary>The node's type, else its class.</summary>
        public string TypeOrClass => string.IsNullOrWhiteSpace(Type) ? Class : Type;

        /// <summary>A node of a ConvertFrom-Json document (missing fields read blank).</summary>
        public static LiveLaneNode FromJson(JsonElement node) => new LiveLaneNode
        {
            Project = LiveLaneJson.Field(node, "project"),
            Uid = LiveLaneJson.Field(node, "uid"),
            Namespace = LiveLaneJson.Field(node, "namespace"),
            Type = LiveLaneJson.Field(node, "type"),
            Class = LiveLaneJson.Field(node, "class"),
            Method = LiveLaneJson.Field(node, "method"),
            DisplayName = LiveLaneJson.Field(node, "displayName"),
            Arguments = LiveLaneJson.Strings(node, "arguments"),
            Signature = LiveLaneJson.Field(node, "signature"),
            Categories = LiveLaneJson.Strings(node, "categories"),
        };

        /// <summary>A node of a PowerShell caller (a custom object, a dictionary or a plain object), read by name.</summary>
        public static LiveLaneNode FromObject(object node) => new LiveLaneNode
        {
            Project = PsValue.Str(PsValue.Prop(node, "project")),
            Uid = PsValue.Str(PsValue.Prop(node, "uid")),
            Namespace = PsValue.Str(PsValue.Prop(node, "namespace")),
            Type = PsValue.Str(PsValue.Prop(node, "type")),
            Class = PsValue.Str(PsValue.Prop(node, "class")),
            Method = PsValue.Str(PsValue.Prop(node, "method")),
            DisplayName = PsValue.Str(PsValue.Prop(node, "displayName")),
            Arguments = LiveLaneDiscovery.ObjectStrings(PsValue.Prop(node, "arguments")),
            Signature = PsValue.Str(PsValue.Prop(node, "signature")),
            Categories = LiveLaneDiscovery.ObjectStrings(PsValue.Prop(node, "categories")),
        };

        /// <summary>The capture's node: project, uid, namespace, type, method, displayName, arguments, signature, categories.</summary>
        public LiveLaneJsonObject ToJson() => new LiveLaneJsonObject
        {
            { "project", Project },
            { "uid", Uid },
            { "namespace", Namespace },
            { "type", Type },
            { "method", Method },
            { "displayName", DisplayName },
            { "arguments", Arguments },
            { "signature", Signature },
            { "categories", Categories },
        };
    }

    /// <summary>Invoke-LiveLaneDiscovery's answer.</summary>
    public sealed class LiveLaneDiscoveryResult
    {
        /// <summary>The producer was accepted: exit 0, a diagnostic log, nodes, and the bound assembly's hash.</summary>
        public bool Ok { get; init; }

        /// <summary>Blank when accepted; else the producer's refusal text, else the reason.</summary>
        public string Error { get; init; } = "";

        /// <summary>The listings the transport admitted and started.</summary>
        public int LaunchCount { get; init; }

        public IReadOnlyList<LiveLaneNode> Nodes { get; init; } = new LiveLaneNode[0];

        /// <summary>The SHA-256 of the bound assembly (blank when it could not be hashed).</summary>
        public string AssemblyHash { get; init; } = "";

        /// <summary>The hash the discovery document names.</summary>
        public string ObservedAssemblyHash { get; init; } = "";

        public string AssemblyPath { get; init; } = "";
        public int DiscoveryMs { get; init; }

        /// <summary>The listing's exit code (124 when it timed out); null when nothing was started.</summary>
        public int? ExitCode { get; init; }

        /// <summary>nonzero-exit, missing-diag, empty-discovery, wrong-assembly-hash or blank.</summary>
        public string Reason { get; init; } = "";
    }

    /// <summary>
    /// The live-lane transport (Install-LiveLaneSeam with Invoke-LiveLaneSeamProcess): a launch is refused (exit 2,
    /// log "transport-refused", nothing started) unless it lists the tests of the bound assembly and does not run them
    /// (Test-LiveLaneListOnly); an admitted launch is counted and runs on the inner launcher.
    /// </summary>
    public sealed class LiveLaneDiscoveryLauncher : INightlyLauncher
    {
        private readonly string _bound;
        private readonly INightlyLauncher _inner;

        public LiveLaneDiscoveryLauncher(string boundAssembly, INightlyLauncher inner)
        {
            _bound = Path.GetFullPath(boundAssembly);
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        /// <summary>The bound assembly (a full path).</summary>
        public string BoundAssembly => _bound;

        /// <summary>The admitted launches.</summary>
        public int LaunchCount { get; private set; }

        /// <summary>The last admitted launch's exit code, 124 when it timed out; null before one.</summary>
        public int? ObservedExit { get; private set; }

        public NightlyLaunchResult Run(NightlyLaunch launch)
        {
            if (launch == null) throw new ArgumentNullException(nameof(launch));
            if (!LiveLaneDiscovery.ListOnly(launch.FileName, launch.Arguments, _bound))
            {
                // The seam's log, as DelegateLauncher wrote it (a log that cannot be written is dropped).
                DelegateLauncher.WriteLog(launch.LogPath, "transport-refused");
                return new NightlyLaunchResult { ExitCode = 2, TimedOut = false, LogText = "transport-refused", ChildrenExited = true };
            }
            LaunchCount++;
            var result = _inner.Run(launch);
            ObservedExit = result.TimedOut ? 124 : result.ExitCode;
            return result;
        }

        public bool WaitCleanup(NightlyLaunchResult result) => _inner.WaitCleanup(result);

        public int NativeActiveCount => _inner.NativeActiveCount;
    }

    /// <summary>
    /// A plain child (Invoke-LiveLaneSeamProcess): FileName with the arguments, stdout and stderr redirected, waited for
    /// up to TimeoutMilliseconds (0 or less: 120 s). A timed-out child's tree is killed: exit 124, log "timeout".
    /// Otherwise the log is stdout, a line feed and stderr. The log is written to LogPath (UTF-8, no byte-order mark,
    /// a final line end) when both are set.
    /// </summary>
    public sealed class LiveLanePlainLauncher : INightlyLauncher
    {
        public NightlyLaunchResult Run(NightlyLaunch launch)
        {
            if (launch == null) throw new ArgumentNullException(nameof(launch));
            var start = new ProcessStartInfo(launch.FileName)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in launch.Arguments ?? new string[0]) start.ArgumentList.Add(argument ?? "");
            if (!string.IsNullOrWhiteSpace(launch.WorkingDirectory)) start.WorkingDirectory = launch.WorkingDirectory;
            WatchedLauncher.ApplyEnvironment(start, launch.Environment);
            NightlyLaunchResult result;
            using (var process = Process.Start(start) ?? throw new InvalidOperationException("the child did not start: " + launch.FileName))
            {
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                var budget = launch.TimeoutMilliseconds > 0 ? launch.TimeoutMilliseconds : 120000;
                if (!process.WaitForExit(budget))
                {
                    try { process.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) { }
                    result = new NightlyLaunchResult { ExitCode = 124, TimedOut = true, LogText = "timeout", Pid = process.Id };
                }
                else
                {
                    process.WaitForExit();
                    var log = "";
                    try { log = stdout.GetAwaiter().GetResult() + "\n" + stderr.GetAwaiter().GetResult(); }
                    catch (Exception e) when (e is IOException || e is InvalidOperationException) { log = ""; }
                    result = new NightlyLaunchResult { ExitCode = process.ExitCode, LogText = log, ChildrenExited = true, Pid = process.Id };
                }
            }
            DelegateLauncher.WriteLog(launch.LogPath, result.LogText);
            return result;
        }

        public bool WaitCleanup(NightlyLaunchResult result) => result != null && result.ChildrenExited;

        public int NativeActiveCount => 0;
    }

    public static class LiveLaneDiscovery
    {
        /// <summary>
        /// The listing's watchdog. Loaded-host n=3 discovery 71966, 74696, 79685 ms (p95 = 79685); 160000 ms is about 2x
        /// that p95. The parent roster wall is 320000 ms.
        /// </summary>
        public const int BudgetMilliseconds = 160000;

        private static readonly Regex NodeLine = new Regex(@"TestNodeUid \{ Value = (?<uid>[^}]+?) \}, DisplayName = (?<display>.*?), Properties = \[");
        private static readonly Regex SignatureRx = new Regex(@"ParameterTypeFullNames = \[(?<sig>[^\]]*)\]");

        private static readonly EnumerationOptions DiagListing = new EnumerationOptions
        {
            MatchType = MatchType.Win32,
            MatchCasing = MatchCasing.CaseInsensitive,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            IgnoreInaccessible = true,
        };

        /// <summary>The production child launcher: OwnedLauncher on Windows, a plain redirected child elsewhere.</summary>
        public static INightlyLauncher DefaultLauncher() => OperatingSystem.IsWindows() ? new OwnedLauncher() : new LiveLanePlainLauncher();

        /// <summary>
        /// Test-LiveLaneListOnly: the launch lists tests (--list-tests), does not run them (no --report-trx), and names
        /// the bound assembly (the argument after the last "exec", else the file itself when it is a .dll), compared as
        /// full paths ignoring case.
        /// </summary>
        public static bool ListOnly(string filePath, IReadOnlyList<string> arguments, string boundAssembly)
        {
            var list = arguments ?? new string[0];
            var hasList = false;
            var hasRun = false;
            var dll = "";
            for (var i = 0; i < list.Count; i++)
            {
                var a = list[i] ?? "";
                if (LiveLaneJson.Same(a, "--list-tests")) hasList = true;
                if (LiveLaneJson.Same(a, "--report-trx") || a.StartsWith("--report-trx=", StringComparison.CurrentCulture)) hasRun = true;
                if (LiveLaneJson.Same(a, "exec") && i + 1 < list.Count) dll = list[i + 1] ?? "";
            }
            if (string.IsNullOrWhiteSpace(dll) && (filePath ?? "").EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) dll = filePath;
            if (string.IsNullOrWhiteSpace(dll) || string.IsNullOrWhiteSpace(boundAssembly)) return false;
            string full;
            string bound;
            try
            {
                full = Path.GetFullPath(dll);
                bound = Path.GetFullPath(boundAssembly);
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException) { return false; }
            if (!hasList || hasRun) return false;
            return string.Equals(full, bound, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Invoke-LiveLaneDiscovery: lists the assembly's tests (dotnet exec &lt;assembly&gt; --list-tests ... under the
        /// live-lane transport, within BudgetMilliseconds) into captureDir: &lt;project&gt;.diag/, .discovery-doc.json,
        /// .discovery.log and the capture &lt;project&gt;.discovery.json. A missing assembly answers assembly-missing and
        /// writes nothing. inner: the child launcher (null: DefaultLauncher).
        /// </summary>
        public static LiveLaneDiscoveryResult Run(string repoRoot, string project, string assemblyPath, string captureDir, INightlyLauncher inner = null)
        {
            if (string.IsNullOrWhiteSpace(assemblyPath) || !(File.Exists(assemblyPath) || Directory.Exists(assemblyPath)))
                return new LiveLaneDiscoveryResult { Ok = false, Error = "assembly-missing", LaunchCount = 0 };
            var assembly = Path.GetFullPath(assemblyPath);
            Directory.CreateDirectory(captureDir);
            var diagDir = Path.Combine(captureDir, project + ".diag");
            if (Directory.Exists(diagDir)) LiveLaneScratch.DeleteTree(diagDir);
            else if (File.Exists(diagDir)) File.Delete(diagDir);
            Directory.CreateDirectory(diagDir);
            var outPath = Path.Combine(captureDir, project + ".discovery-doc.json");
            var logPath = Path.Combine(captureDir, project + ".discovery.log");
            var launcher = new LiveLaneDiscoveryLauncher(assembly, inner ?? DefaultLauncher());
            var host = NightlyHost.Create(null, launcher, TextWriter.Null, TextWriter.Null, null, null, null, null);
            var dotnet = OwnedLauncher.FindApplication("dotnet") ?? "dotnet";
            var started = Stopwatch.StartNew();
            var errorText = "";
            object doc = null;
            try
            {
                var request = new NightlyDiscoveryRequest(dotnet, new[] { "exec", assembly }, assembly, outPath, diagDir, logPath, repoRoot, null);
                doc = NightlyDiscoveryProducer.Produce(host, request, BudgetMilliseconds);
            }
            catch (Exception e)
            {
                errorText = e.Message;
            }
            started.Stop();
            var exitCode = launcher.ObservedExit;
            string diagPath = "";
            foreach (var file in Directory.EnumerateFiles(diagDir, "*.diag", DiagListing)) { diagPath = file; break; }
            var hash = "";
            try { hash = File.Exists(assembly) ? NightlyClassNames.FileSha256(assembly) : ""; }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { hash = ""; }
            var docHash = doc == null ? "" : PsValue.Str(PsValue.Prop(doc, "assemblyHash"));
            var nodes = doc != null && diagPath.Length > 0 ? Overlay(doc, diagPath, project) : new List<LiveLaneNode>();
            var accepted = true;
            var reason = "";
            if (!string.IsNullOrWhiteSpace(errorText)) { accepted = false; reason = "missing-diag"; }
            if (exitCode == null || exitCode.Value != 0) { accepted = false; if (reason.Length == 0) reason = "nonzero-exit"; }
            if (string.IsNullOrWhiteSpace(diagPath)) { accepted = false; if (reason.Length == 0) reason = "missing-diag"; }
            if (nodes.Count == 0) { accepted = false; if (reason.Length == 0) reason = "empty-discovery"; }
            if (!string.IsNullOrWhiteSpace(docHash) && !string.Equals(docHash, hash, StringComparison.OrdinalIgnoreCase))
            {
                accepted = false;
                reason = "wrong-assembly-hash";
            }
            var discoveryMs = (int)started.ElapsedMilliseconds;
            var nodeRows = new List<LiveLaneJsonObject>(nodes.Count);
            foreach (var node in nodes) nodeRows.Add(node.ToJson());
            var capture = new LiveLaneJsonObject
            {
                { "format", "antiphon-live-lane-discovery-capture-v1" },
                { "project", project },
                { "assemblyPath", assembly },
                { "assemblyHash", hash },
                { "observedAssemblyHash", docHash },
                { "launchCount", launcher.LaunchCount },
                { "exitCode", exitCode },
                { "producerAccepted", accepted },
                { "reason", reason },
                { "detail", errorText },
                { "discoveryMs", discoveryMs },
                { "nodes", nodeRows },
            };
            LiveLaneJson.WriteFile(Path.Combine(captureDir, project + ".discovery.json"), capture, finalNewLine: true);
            return new LiveLaneDiscoveryResult
            {
                Ok = accepted,
                Error = accepted ? "" : (errorText.Length > 0 ? errorText : reason),
                LaunchCount = launcher.LaunchCount,
                Nodes = nodes,
                AssemblyHash = hash,
                ObservedAssemblyHash = docHash,
                AssemblyPath = assembly,
                DiscoveryMs = discoveryMs,
                ExitCode = exitCode,
                Reason = reason,
            };
        }

        /// <summary>
        /// Get-LiveLaneDisplayMap: each "TestNodeUid { Value = uid }, DisplayName = display, Properties = [" record of
        /// the diagnostic log (CR dropped), with the ParameterTypeFullNames within 1200 characters of it; a later record
        /// of a uid replaces an earlier one, uids compared ignoring case. A missing log is an empty map.
        /// </summary>
        public static Dictionary<string, (string Display, string Signature)> DisplayMap(string diagPath)
        {
            var map = new Dictionary<string, (string Display, string Signature)>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(diagPath) || !File.Exists(diagPath)) return map;
            var raw = File.ReadAllText(diagPath).Replace("\r", "");
            foreach (Match m in NodeLine.Matches(raw))
            {
                var uid = m.Groups["uid"].Value.Trim();
                var display = m.Groups["display"].Value.Trim();
                var window = raw.Substring(m.Index, Math.Min(1200, raw.Length - m.Index));
                var sig = "";
                var sigMatch = SignatureRx.Match(window);
                if (sigMatch.Success) sig = sigMatch.Groups["sig"].Value.Trim();
                map[uid] = (display, sig);
            }
            return map;
        }

        /// <summary>
        /// Get-LiveLaneParsedArguments: the values of "Method(a, 'b', \"c\")" (quotes and CR dropped, blank values left
        /// out); none when the display is not the method's call text.
        /// </summary>
        public static string[] ParsedArguments(string method, string display)
        {
            if (string.IsNullOrWhiteSpace(display)) return new string[0];
            var prefix = (method ?? "") + "(";
            if (!display.StartsWith(prefix, StringComparison.Ordinal)) return new string[0];
            if (!display.EndsWith(")", StringComparison.Ordinal)) return new string[0];
            var inner = display.Substring(prefix.Length, display.Length - prefix.Length - 1).Trim();
            if (string.IsNullOrWhiteSpace(inner)) return new string[0];
            var parts = new List<string>();
            var current = new StringBuilder();
            char? quote = null;
            foreach (var ch in inner)
            {
                if (quote != null)
                {
                    if (ch == quote.Value) quote = null; else current.Append(ch);
                    continue;
                }
                if (ch == '"' || ch == '\'') { quote = ch; continue; }
                if (ch == ',') { parts.Add(current.ToString().Trim()); current.Clear(); continue; }
                current.Append(ch);
            }
            var tail = current.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(tail) || parts.Count > 0) parts.Add(tail);
            var values = new List<string>();
            foreach (var part in parts)
            {
                var value = part.Trim().Trim('"').Trim('\'').Replace("\r", "");
                if (!string.IsNullOrWhiteSpace(value)) values.Add(value);
            }
            return values.ToArray();
        }

        /// <summary>
        /// Add-LiveLaneDiscoveryOverlay: the discovery document's nodes for the project, each with its display name and
        /// signature from the diagnostic log (else its method and own signature) and its arguments parsed from the display.
        /// </summary>
        public static List<LiveLaneNode> Overlay(object document, string diagPath, string project)
        {
            var map = DisplayMap(diagPath);
            var nodes = new List<LiveLaneNode>();
            foreach (var n in Items(PsValue.Prop(document, "nodes")))
            {
                var uid = PsValue.Str(PsValue.Prop(n, "uid"));
                var method = PsValue.Str(PsValue.Prop(n, "method"));
                var display = method;
                var sig = PsValue.Str(PsValue.Prop(n, "signature"));
                if (map.TryGetValue(uid, out var shown))
                {
                    display = shown.Display;
                    if (!string.IsNullOrWhiteSpace(shown.Signature)) sig = shown.Signature;
                }
                nodes.Add(new LiveLaneNode
                {
                    Project = project,
                    Uid = uid,
                    Namespace = PsValue.Str(PsValue.Prop(n, "namespace")),
                    Type = PsValue.Str(PsValue.Prop(n, "type")),
                    Method = method,
                    DisplayName = display,
                    Arguments = ParsedArguments(method, display),
                    Signature = sig,
                    Categories = ObjectStrings(PsValue.Prop(n, "categories")),
                });
            }
            return nodes;
        }

        /// <summary>@(ConvertTo-LiveLaneList $value) of a plain or PowerShell value: none for null.</summary>
        internal static object[] Items(object value) => value == null ? new object[0] : PsValue.AsArray(value);

        /// <summary>@(ConvertTo-LiveLaneList $value | ForEach-Object { [string]$_ }).</summary>
        internal static string[] ObjectStrings(object value)
        {
            var items = Items(value);
            var values = new string[items.Length];
            for (var i = 0; i < items.Length; i++) values[i] = PsValue.Str(items[i]);
            return values;
        }
    }
}
