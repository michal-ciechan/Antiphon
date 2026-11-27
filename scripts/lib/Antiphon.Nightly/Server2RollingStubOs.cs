// The offline fixture seams deploy-server2.ps1 read from its environment (unit 65), kept for the W10 node harnesses that drive the
// rolling deploy against PowerShell fakes and a real Docker host (scripts/fixtures/c1008-recycle-real-cases.mjs's C994 lane,
// c1609-wrapper-cases.mjs and c1609-wrapper-v2-cases.mjs). The CLI binds this world when C727_TEST_VERIFY_STUB or
// C727_TEST_HTTP_STUB is set, exactly where the script switched:
//
// - C727_TEST_VERIFY_STUB: the rollout lock, the recycle discovery, the temp container census, the temp retirement census and
//   every host case start pwsh -NoProfile -File <stub> -Case <case> [-Manifest <path>] instead of ssh or verify-docker-stack.ps1
//   (the two censuses write their small manifests first, as the script did). With C727_TEST_STATE too, the host-jq prerequisite is
//   the stub's host-jq-prerequisite case (its manifest written first; its error records are the stderr), and the temp container
//   retirement's clock is the state's clockMs (advanced after each poll). Without C727_TEST_STATE the host jq goes over the real
//   transport, as the script's did.
// - C727_TEST_HTTP_STUB: each runner request and census read is pwsh -NoProfile -File <stub> with the script's parameters; a
//   non-zero exit is no answer, __404__ / __503__ / __NNN__ are the statuses, __TIMEOUT__ a timeout. A stub's typed exception
//   (its reset and WebException faults) is a separate pwsh's failed exit here, so it reads as a transport failure, not retryable.
// - C727_TEST_WAIT_MS, C727_TEST_POLL_MS and C727_TEST_LAND_WAIT_MS: the timings (Server2RollingTiming).
//
// Everything else is the production world. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The C727 fixture seams of the rolling deploy over the production world.</summary>
    public sealed class Server2RollingStubOs : IServer2RollingWorld
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        private readonly IServer2RollingWorld _production;
        private readonly string _verify;
        private readonly string _http;
        private readonly string _state;

        /// <summary>The seams from these variables (C727_TEST_*), over the production world.</summary>
        public Server2RollingStubOs(Func<string, string> variables, IServer2RollingWorld production)
        {
            if (variables == null) throw new ArgumentNullException(nameof(variables));
            _production = production ?? throw new ArgumentNullException(nameof(production));
            _verify = variables("C727_TEST_VERIFY_STUB") ?? "";
            _http = variables("C727_TEST_HTTP_STUB") ?? "";
            _state = variables("C727_TEST_STATE") ?? "";
            int? Ms(string name) => int.TryParse(variables(name), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ? value : null;
            Timing = new Server2RollingTiming
            {
                WaitMs = Ms("C727_TEST_WAIT_MS"), PollMs = Ms("C727_TEST_POLL_MS"), LandWaitMs = Ms("C727_TEST_LAND_WAIT_MS"), StubbedReads = _http.Length > 0,
            };
        }

        /// <summary>Whether a variable names a stub (the CLI binds this world then).</summary>
        public static bool Wanted(Func<string, string> variables) =>
            !string.IsNullOrEmpty(variables("C727_TEST_VERIFY_STUB")) || !string.IsNullOrEmpty(variables("C727_TEST_HTTP_STUB"));

        /// <summary>The timings the variables name.</summary>
        public Server2RollingTiming Timing { get; }

        private bool Verify => _verify.Length > 0;

        private bool Fixture => Verify && _state.Length > 0;

        /// <summary>Get-C994Now: the state's clockMs when the fixture state names one, else null (the host's clock).</summary>
        public DateTime? C994Now()
        {
            if (!Fixture || !(JsonNode.Parse(File.ReadAllText(_state)) is JsonObject state) || !state.ContainsKey("clockMs")) return null;
            var clock = state["clockMs"];
            // [long]$state.clockMs: a null is 0, a fraction rounds to even.
            return DateTime.UnixEpoch.AddMilliseconds(clock == null ? 0 : (long)Math.Round(clock.GetValue<double>(), MidpointRounding.ToEven));
        }

        /// <summary>The temp container retirement's poll advances the fixture state's clockMs.</summary>
        public void C994Advance(int milliseconds)
        {
            if (!Fixture) return;
            if (!(JsonNode.Parse(File.ReadAllText(_state)) is JsonObject state) || !state.ContainsKey("clockMs")) return;
            var clock = state["clockMs"];
            state["clockMs"] = (clock == null ? 0 : (long)Math.Round(clock.GetValue<double>(), MidpointRounding.ToEven)) + milliseconds;
            File.WriteAllText(_state, state.ToJsonString() + Environment.NewLine, Utf8);
        }

        private static ProcessStartInfo Pwsh(IEnumerable<string> arguments, bool input)
        {
            var start = new ProcessStartInfo("pwsh")
            {
                UseShellExecute = false, RedirectStandardInput = input, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Utf8, StandardErrorEncoding = Utf8,
            };
            foreach (var argument in new[] { "-NoProfile" }.Concat(arguments)) start.ArgumentList.Add(argument ?? "");
            return start;
        }

        private static Server2RollingChild Run(IEnumerable<string> arguments)
        {
            Process process;
            try { process = Process.Start(Pwsh(arguments, false)); }
            catch (Exception e) { throw new Server2RollingChildException(Server2RollingFault.Start, e.Message, e); }
            using (process)
            {
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(30000))
                {
                    process.Kill(true);
                    process.WaitForExit();
                    throw new Server2RollingChildException(Server2RollingFault.Deadline, "deadline");
                }
                return new Server2RollingChild(process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
            }
        }

        private static string Quote(string value) => "'" + (value ?? "").Replace("'", "''") + "'";

        private static void Manifest(string path, PlainObject manifest) =>
            File.WriteAllText(path, RollingPs.Json(manifest) + Environment.NewLine, Encoding.ASCII);

        /// <inheritdoc />
        public Server2RollingChild HostJq(Server2RollingCall call)
        {
            if (!Fixture) return _production.HostJq(call);
            // Use the existing offline verifier boundary; its proof still passes the same parser and durable receipt admission as the real
            // SSH child.
            var manifest = Path.Combine(call.EvidenceRoot, "host-jq-" + call.Phase + ".manifest.json");
            Manifest(manifest, RollingPs.Object(("sourceSha", call.Sha), ("runId", call.RunId), ("selectedPhase", call.SelectedPhase), ("phase", call.Phase),
                ("mode", call.Mode), ("evidenceRoot", call.EvidenceRoot)));
            var command = "$r=@(& " + Quote(_verify) + " -Case 'host-jq-prerequisite' -Manifest " + Quote(manifest) + " 2>&1); $code=$LASTEXITCODE; "
                + "foreach($i in $r){ if($i -is [Management.Automation.ErrorRecord]){ [Console]::Error.WriteLine($i.Exception.Message) } else { [Console]::Out.WriteLine([string]$i) } }; exit $code";
            var result = Run(new[] { "-Command", command });
            return new Server2RollingChild(result.Exit, Trim(result.Stdout), Trim(result.Stderr));
        }

        // The in-process call's lines joined by LF, without the last line end.
        private static string Trim(string text) => (text ?? "").Replace("\r\n", "\n").TrimEnd('\n');

        /// <inheritdoc />
        public IServer2RollingLock EnterRolloutLock()
        {
            if (!Verify) return _production.EnterRolloutLock();
            var process = Process.Start(Pwsh(new[] { "-File", _verify, "-Case", "rollout-lock" }, true));
            process.StandardError.ReadToEndAsync();
            try
            {
                var ready = process.StandardOutput.ReadLineAsync();
                if (!ready.Wait(70000) || !string.Equals(ready.Result, "C1008_LOCKED", StringComparison.Ordinal) || process.HasExited)
                    throw new Server2RollingRefusal("RecycleRolloutBusy");
                return new Held(process);
            }
            catch (Exception)
            {
                if (!process.HasExited) { process.Kill(true); process.WaitForExit(); }
                process.Dispose();
                throw new Server2RollingRefusal("RecycleRolloutBusy");
            }
        }

        private sealed class Held : IServer2RollingLock
        {
            private readonly Process _process;

            public Held(Process process) => _process = process;

            public void Release()
            {
                try
                {
                    _process.StandardInput.WriteLine("release");
                    _process.StandardInput.Close();
                    if (!_process.WaitForExit(5000)) { _process.Kill(true); _process.WaitForExit(); }
                }
                finally { _process.Dispose(); }
            }
        }

        private static readonly Regex Status = new Regex("^__(\\d{3})__$");

        /// <inheritdoc />
        public Server2RollingHttpAnswer RunnerHttp(string method, string url, IReadOnlyDictionary<string, string> headers, string body)
        {
            if (_http.Length == 0) return _production.RunnerHttp(method, url, headers, body);
            var path = new Uri(url).AbsolutePath;
            var rest = path.Substring("/api/session-runners/".Length);
            var slash = rest.IndexOf('/');
            var runnerId = Uri.UnescapeDataString(slash < 0 ? rest : rest.Substring(0, slash));
            var suffix = slash < 0 ? "" : rest.Substring(slash);
            var result = Run(new[] { "-File", _http, "-Method", method, "-RunnerId", runnerId, "-Suffix", suffix, "-BodyJson", body ?? "" });
            if (result.Exit != 0) throw new InvalidOperationException("RunnerApiUnavailable");
            var raw = string.Join(" ", Trim(result.Stdout).Split('\n'));
            // The script answered null for __404__ on any method (an empty 200 reads as null here too).
            if (string.Equals(raw, "__404__", StringComparison.OrdinalIgnoreCase)) return new Server2RollingHttpAnswer(200, "");
            if (string.Equals(raw, "__503__", StringComparison.OrdinalIgnoreCase)) return new Server2RollingHttpAnswer(503, "");
            return new Server2RollingHttpAnswer(200, raw);
        }

        /// <inheritdoc />
        public Server2RollingHttpAnswer ReadHttp(string path, string url, IReadOnlyDictionary<string, string> headers)
        {
            if (_http.Length == 0) return _production.ReadHttp(path, url, headers);
            var result = Run(new[] { "-File", _http, "-Method", "GET", "-Path", path });
            if (result.Exit != 0) throw new InvalidOperationException("transport");
            var raw = string.Join(" ", Trim(result.Stdout).Split('\n'));
            if (string.Equals(raw, "__TIMEOUT__", StringComparison.Ordinal)) throw new TimeoutException("timeout");
            var status = Status.Match(raw);
            if (status.Success) return new Server2RollingHttpAnswer(int.Parse(status.Groups[1].Value, CultureInfo.InvariantCulture), "");
            return new Server2RollingHttpAnswer(200, raw);
        }

        /// <inheritdoc />
        public Server2RollingChild RecycleDiscover(Server2RollingCall call) =>
            Verify ? Run(new[] { "-File", _verify, "-Case", "recycle-discover" }) : _production.RecycleDiscover(call);

        /// <inheritdoc />
        public Server2RollingChild TempRetirementRead(Server2RollingCall call)
        {
            if (!Verify) return _production.TempRetirementRead(call);
            var manifest = Path.Combine(call.EvidenceRoot, "temp-retirement-read.manifest.json");
            Manifest(manifest, RollingPs.Object(("sourceSha", call.Sha), ("runId", call.RunId), ("operationId", call.Operation ?? "")));
            var hostCase = string.IsNullOrEmpty(call.Operation) ? "temp-retirement-clearable" : "retirement-proof-read";
            return Run(new[] { "-File", _verify, "-Case", hostCase, "-Manifest", manifest });
        }

        /// <inheritdoc />
        public Server2RollingChild TempProjectCensus(Server2RollingCall call)
        {
            if (!Verify) return _production.TempProjectCensus(call);
            var manifest = Path.Combine(call.EvidenceRoot, "temp-project-absent.manifest.json");
            Manifest(manifest, RollingPs.Object(("sourceSha", call.Sha), ("runId", call.RunId)));
            return Run(new[] { "-File", _verify, "-Case", "temp-project-absent", "-Manifest", manifest });
        }

        /// <inheritdoc />
        public int HostCase(string hostCase, string manifestPath, TextWriter output, TextWriter error)
        {
            if (!Verify) return _production.HostCase(hostCase, manifestPath, output, error);
            using var process = new Process { StartInfo = Pwsh(new[] { "-File", _verify, "-Case", hostCase, "-Manifest", manifestPath }, false) };
            var gate = new object();
            process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (gate) output.WriteLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (gate) error.WriteLine(e.Data); };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
            return process.ExitCode;
        }

        /// <inheritdoc />
        public Server2RollingChild Git(string repo, IReadOnlyList<string> arguments) => _production.Git(repo, arguments);
    }
}
