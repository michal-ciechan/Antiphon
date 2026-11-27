// The offline fixture seams deploy-server2.ps1 read from its environment (unit 65), kept for the W10 node harnesses that drive the
// rolling deploy against fakes and a real Docker host (scripts/fixtures/c1008-recycle-real-cases.mjs's C994 lane,
// c1609-wrapper-cases.mjs and c1609-wrapper-v2-cases.mjs). The CLI binds this world when C727_TEST_VERIFY_STUB or
// C727_TEST_HTTP_STUB is set, exactly where the script switched. Unit 68 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md
// made every seam start no pwsh: the two PowerShell fakes are C727Fakes in process, and a harness adapter is its own executable.
//
// - C727_TEST_VERIFY_STUB: the rollout lock, the recycle discovery, the temp container census, the temp retirement census and
//   every host case go to the verifier stub instead of ssh or the stack verifier (the two censuses write their small manifests
//   first, as the script did). c727-fake-verify is the fake in process (scripts/fixtures/c727-fake-verify.ps1 until unit 68);
//   anything else is a harness adapter, an executable started directly with --case <case> [--manifest <path>]: its stdout,
//   stderr and exit code, 30 s for a census or the discovery; the rollout-lock case's first line is C1008_LOCKED within 70 s and
//   it holds until a line on its input. With C727_TEST_STATE too, the host-jq prerequisite is the stub's host-jq-prerequisite case
//   (its manifest written first), and the temp container retirement's clock is the state's clockMs (advanced after each poll).
//   Without C727_TEST_STATE the host jq goes over the real transport, as the script's did.
// - C727_TEST_HTTP_STUB: each runner request and census read is the HTTP fake in process (c727-fake-http, the only value;
//   scripts/fixtures/c727-fake-http.ps1 until unit 68): no answer is RunnerApiUnavailable or a transport failure, __404__ /
//   __503__ / __NNN__ are the statuses, __TIMEOUT__ a timeout. A typed fault (the fake's reset and WebException faults) failed
//   the fake's own pwsh, so it stays a transport failure, not a retryable one.
// - The fakes read C727_TEST_STATE at the start of each call and write it back after a save (ConvertTo-Json -Compress at the
//   fake's depth, then a line end, as Set-Content did); each trace row is appended to C727_TEST_TRACE. A fake call that throws is
//   the fake's failed exit (1, the message on stderr), as a terminating error in its pwsh was.
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
        /// <summary>C727_TEST_HTTP_STUB's value for the HTTP fake in process.</summary>
        public const string FakeHttp = "c727-fake-http";

        /// <summary>C727_TEST_VERIFY_STUB's value for the verifier fake in process.</summary>
        public const string FakeVerify = "c727-fake-verify";

        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        private readonly IServer2RollingWorld _production;
        private readonly string _verify;
        private readonly string _http;
        private readonly string _state;
        private readonly string _trace;
        private readonly string _checkout;

        /// <summary>The seams from these variables (C727_TEST_*), over the production world; checkout holds the marker reader.</summary>
        public Server2RollingStubOs(Func<string, string> variables, IServer2RollingWorld production, string checkout = null)
        {
            if (variables == null) throw new ArgumentNullException(nameof(variables));
            _production = production ?? throw new ArgumentNullException(nameof(production));
            _verify = variables("C727_TEST_VERIFY_STUB") ?? "";
            _http = variables("C727_TEST_HTTP_STUB") ?? "";
            _state = variables("C727_TEST_STATE") ?? "";
            _trace = variables("C727_TEST_TRACE") ?? "";
            _checkout = checkout ?? "";
            if (_http.Length > 0 && !string.Equals(_http, FakeHttp, StringComparison.Ordinal))
                throw new NightlyUsageException("C727_TEST_HTTP_STUB names no fake: '" + _http + "' (the HTTP fake is " + FakeHttp + ")");
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

        private bool InProcess => string.Equals(_verify, FakeVerify, StringComparison.Ordinal);

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

        // ---- the fakes in process ----

        // One fake call on the state file as the PowerShell fake read it; the state is written back after a save.
        private T Fake<T>(Func<C727Fakes, T> call, TextWriter output = null)
        {
            if (_state.Length == 0) throw new InvalidOperationException("C727_TEST_STATE names no fixture state");
            var fakes = new C727Fakes
            {
                State = JsonNode.Parse(File.ReadAllText(_state)).AsObject(),
                Trace = row =>
                {
                    if (_trace.Length == 0) throw new InvalidOperationException("C727_TEST_TRACE names no trace file");
                    File.AppendAllText(_trace, row.ToJsonString() + Environment.NewLine, Utf8);
                },
                MarkerReader = C727Fakes.CheckoutMarkerReader(_checkout, output ?? TextWriter.Null),
            };
            try { return call(fakes); }
            finally
            {
                if (fakes.SavedText != null) File.WriteAllText(_state, fakes.SavedText + Environment.NewLine, Utf8);
            }
        }

        // A verify fake call that throws is the fake's failed exit: 1 and the message on stderr.
        private Server2RollingChild FakeChild(Func<C727Fakes, Server2RollingChild> call)
        {
            try { return Fake(call); }
            catch (Exception e) when (!(e is Server2RollingRefusal)) { return new Server2RollingChild(1, "", e.Message); }
        }

        /// <summary>One call of the in-process fakes on the state file, writing no manifest (the c727-fake-verify verb): a throw is exit 1.</summary>
        public Server2RollingChild FakeOnly(Func<C727Fakes, Server2RollingChild> call) => FakeChild(call);

        // ---- a harness adapter, started directly ----

        private ProcessStartInfo Adapter(IEnumerable<string> arguments, bool input)
        {
            var start = new ProcessStartInfo(_verify)
            {
                UseShellExecute = false, RedirectStandardInput = input, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Utf8, StandardErrorEncoding = Utf8,
            };
            foreach (var argument in arguments) start.ArgumentList.Add(argument ?? "");
            return start;
        }

        private static IEnumerable<string> CaseArguments(string hostCase, string manifest) =>
            manifest == null ? new[] { "--case", hostCase } : new[] { "--case", hostCase, "--manifest", manifest };

        private Server2RollingChild Run(string hostCase, string manifest)
        {
            Process process;
            try { process = Process.Start(Adapter(CaseArguments(hostCase, manifest), false)); }
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

        private static void Manifest(string path, PlainObject manifest) =>
            File.WriteAllText(path, RollingPs.Json(manifest) + Environment.NewLine, Encoding.ASCII);

        // The in-process call's lines joined by LF, without the last line end.
        private static string Trim(string text) => (text ?? "").Replace("\r\n", "\n").TrimEnd('\n');

        /// <inheritdoc />
        public Server2RollingChild HostJq(Server2RollingCall call)
        {
            if (!Fixture) return _production.HostJq(call);
            // Use the existing offline verifier boundary; its proof still passes the same parser and durable receipt admission as the real
            // SSH child.
            var manifest = Path.Combine(call.EvidenceRoot, "host-jq-" + call.Phase + ".manifest.json");
            Manifest(manifest, RollingPs.Object(("sourceSha", call.Sha), ("runId", call.RunId), ("selectedPhase", call.SelectedPhase), ("phase", call.Phase),
                ("mode", call.Mode), ("evidenceRoot", call.EvidenceRoot)));
            var result = InProcess ? FakeChild(f => f.HostJq(call)) : Run("host-jq-prerequisite", manifest);
            return new Server2RollingChild(result.Exit, Trim(result.Stdout), Trim(result.Stderr));
        }

        /// <inheritdoc />
        public IServer2RollingLock EnterRolloutLock()
        {
            if (!Verify) return _production.EnterRolloutLock();
            if (InProcess)
            {
                try { return Fake(f => f.EnterRolloutLock()); }
                catch (Exception) { throw new Server2RollingRefusal("RecycleRolloutBusy"); }
            }
            var process = Process.Start(Adapter(CaseArguments("rollout-lock", null), true));
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
            string text;
            try { text = Fake(f => f.RunnerText(method, url, body)); }
            catch (Exception) { throw new InvalidOperationException("RunnerApiUnavailable"); }
            if (text == null) throw new InvalidOperationException("RunnerApiUnavailable");
            var raw = string.Join(" ", Trim(text).Split('\n'));
            // The script answered null for __404__ on any method (an empty 200 reads as null here too).
            if (string.Equals(raw, "__404__", StringComparison.OrdinalIgnoreCase)) return new Server2RollingHttpAnswer(200, "");
            if (string.Equals(raw, "__503__", StringComparison.OrdinalIgnoreCase)) return new Server2RollingHttpAnswer(503, "");
            return new Server2RollingHttpAnswer(200, raw);
        }

        /// <inheritdoc />
        public Server2RollingHttpAnswer ReadHttp(string path, string url, IReadOnlyDictionary<string, string> headers)
        {
            if (_http.Length == 0) return _production.ReadHttp(path, url, headers);
            string text;
            try { text = Fake(f => f.ReadText(path, url)); }
            catch (Exception) { throw new InvalidOperationException("transport"); }
            if (text == null) throw new InvalidOperationException("transport");
            var raw = string.Join(" ", Trim(text).Split('\n'));
            if (string.Equals(raw, "__TIMEOUT__", StringComparison.Ordinal)) throw new TimeoutException("timeout");
            var status = Status.Match(raw);
            if (status.Success) return new Server2RollingHttpAnswer(int.Parse(status.Groups[1].Value, CultureInfo.InvariantCulture), "");
            return new Server2RollingHttpAnswer(200, raw);
        }

        /// <inheritdoc />
        public Server2RollingChild RecycleDiscover(Server2RollingCall call)
        {
            if (!Verify) return _production.RecycleDiscover(call);
            return InProcess ? FakeChild(f => f.RecycleDiscover()) : Run("recycle-discover", null);
        }

        /// <inheritdoc />
        public Server2RollingChild TempRetirementRead(Server2RollingCall call)
        {
            if (!Verify) return _production.TempRetirementRead(call);
            var manifest = Path.Combine(call.EvidenceRoot, "temp-retirement-read.manifest.json");
            Manifest(manifest, RollingPs.Object(("sourceSha", call.Sha), ("runId", call.RunId), ("operationId", call.Operation ?? "")));
            var hostCase = string.IsNullOrEmpty(call.Operation) ? "temp-retirement-clearable" : "retirement-proof-read";
            return InProcess ? FakeChild(f => f.TempRetirementRead(call)) : Run(hostCase, manifest);
        }

        /// <inheritdoc />
        public Server2RollingChild TempProjectCensus(Server2RollingCall call)
        {
            if (!Verify) return _production.TempProjectCensus(call);
            var manifest = Path.Combine(call.EvidenceRoot, "temp-project-absent.manifest.json");
            Manifest(manifest, RollingPs.Object(("sourceSha", call.Sha), ("runId", call.RunId)));
            return InProcess ? FakeChild(f => f.TempProjectCensus(call)) : Run("temp-project-absent", manifest);
        }

        /// <inheritdoc />
        public int HostCase(string hostCase, string manifestPath, TextWriter output, TextWriter error)
        {
            if (!Verify) return _production.HostCase(hostCase, manifestPath, output, error);
            if (InProcess)
            {
                try { return Fake(f => f.HostCase(hostCase, manifestPath), output); }
                catch (Exception e) { error.WriteLine(e.Message); return 1; }
            }
            using var process = new Process { StartInfo = Adapter(CaseArguments(hostCase, manifestPath), false) };
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
