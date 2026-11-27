// The production world of the deploy-server2 command (unit 65): the children and HTTP deploy-server2.ps1 started, with the same
// argument vectors, inputs, deadlines and reaping. ssh is "ssh" on PATH (BatchMode, a 15 s connect timeout, mc@server2), the
// verifier is pwsh -NoProfile -File <checkout>/scripts/verify-docker-stack.ps1, git is "git" on PATH and HTTP is HttpClient
// (Invoke-WebRequest -SkipHttpErrorCheck: any status is an answer; the census reads time out after 60 s). An environment, when
// given, is laid over each child's (the Linux host-jq fixtures put their fake ssh first on PATH). Plain C#: no PowerShell types.
// ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Antiphon.Nightly
{
    /// <summary>The rolling deploy's real children and HTTP.</summary>
    public sealed class Server2RollingOs : IServer2RollingWorld
    {
        /// <summary>The ssh target.</summary>
        public const string Target = "mc@server2";

        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        private static readonly string[] SshOptions = { "-o", "BatchMode=yes", "-o", "ConnectTimeout=15", Target };

        private readonly string _checkout;
        private readonly IReadOnlyDictionary<string, string> _environment;
        private readonly HttpClient _http;
        private readonly HttpClient _reads;

        /// <summary>The checkout holds scripts/server2-host-jq.sh, the read packet's scripts and the verifier.</summary>
        public Server2RollingOs(string checkout, IReadOnlyDictionary<string, string> environment = null)
        {
            _checkout = checkout ?? throw new ArgumentNullException(nameof(checkout));
            _environment = environment;
            _http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            _reads = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        }

        /// <summary>The host-jq deadline in milliseconds instead of 30 s (check) and 180 s (provision): the Linux fixtures' short deadline.</summary>
        public int? HostJqDeadlineMs { get; init; }

        // With an environment that sets PATH, a bare name is found on that PATH (the child's), as the script's children were found on
        // its own PATH; Process.Start alone would search this process's PATH. Not found: the name, which then fails to start.
        private string Resolve(string fileName)
        {
            if (_environment == null || !_environment.TryGetValue("PATH", out var path) || path == null || fileName.IndexOfAny(new[] { '/', '\\' }) >= 0)
                return fileName;
            var extensions = OperatingSystem.IsWindows() ? new[] { ".exe", ".cmd", ".bat", "" } : new[] { "" };
            foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                foreach (var extension in extensions)
                {
                    var candidate = Path.Combine(directory, fileName + extension);
                    if (File.Exists(candidate)) return candidate;
                }
            return Path.Combine(path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "", fileName);
        }

        private ProcessStartInfo Start(string fileName, IEnumerable<string> arguments, bool input)
        {
            var start = new ProcessStartInfo(Resolve(fileName))
            {
                UseShellExecute = false,
                RedirectStandardInput = input,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Utf8,
                StandardErrorEncoding = Utf8,
            };
            if (input) start.StandardInputEncoding = Utf8;
            foreach (var argument in arguments) start.ArgumentList.Add(argument ?? "");
            if (_environment != null)
                foreach (var pair in _environment)
                {
                    if (pair.Value == null) start.Environment.Remove(pair.Key);
                    else start.Environment[pair.Key] = pair.Value;
                }
            return start;
        }

        private static int Remaining(Stopwatch timer, int deadline) => Math.Max(1, deadline - (int)timer.ElapsedMilliseconds);

        /// <inheritdoc />
        public Server2RollingChild HostJq(Server2RollingCall call)
        {
            if (call == null) throw new ArgumentNullException(nameof(call));
            var deadline = HostJqDeadlineMs ?? (call.Mode == "check" ? 30000 : 180000);
            var process = new Process { StartInfo = Start("ssh", SshOptions.Concat(new[] { "bash", "-s", "--", call.Mode }), input: true) };
            Task<string> stdout = null, stderr = null;
            Task input = null;
            var started = false;
            var timer = Stopwatch.StartNew();
            Server2RollingChild result;
            try
            {
                try
                {
                    process.Start();
                    started = true;
                    stdout = process.StandardOutput.ReadToEndAsync();
                    stderr = process.StandardError.ReadToEndAsync();
                    var helper = File.ReadAllText(Path.Combine(_checkout, "scripts", "server2-host-jq.sh")).Replace("\r\n", "\n");
                    input = process.StandardInput.WriteAsync(helper);
                    if (!input.Wait(Remaining(timer, deadline))) throw new TimeoutException("timeout");
                    input.GetAwaiter().GetResult();
                    process.StandardInput.Close();
                    if (!process.WaitForExit(Remaining(timer, deadline))) throw new TimeoutException("timeout");
                    if (!Task.WhenAll(stdout, stderr).Wait(Remaining(timer, deadline))) throw new TimeoutException("timeout");
                }
                catch (Exception)
                {
                    if (timer.ElapsedMilliseconds >= deadline) throw new Server2RollingRefusal("HostJqTransportTimeout");
                    throw new Server2RollingRefusal("HostJqTransportUnavailable");
                }
                result = new Server2RollingChild(process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
            }
            finally
            {
                // Custody covers timeout, broken stdin and inherited pipe handles, including root exit. A child that never started has
                // no id: the script's $proc.Id threw there too, so that is unknown custody.
                try
                {
                    if (!started) throw new InvalidOperationException("No process is associated with this object.");
                    if (!process.HasExited) process.Kill(true);
                    if (!process.WaitForExit(5000)) throw new TimeoutException("custody");
                    var tasks = new Task[] { stdout, stderr, input }.Where(t => t != null).ToArray();
                    if (tasks.Length > 0 && !Task.WhenAll(tasks).ContinueWith(_ => { }).Wait(5000)) throw new TimeoutException("custody");
                }
                catch (Exception) { process.Dispose(); throw new Server2RollingRefusal("HostJqTransportCustodyUnknown"); }
                process.Dispose();
            }
            return result;
        }

        /// <inheritdoc />
        public IServer2RollingLock EnterRolloutLock()
        {
            const string command = "set -e; mkdir -p /home/mc/antiphon-server2/locks; chmod 700 /home/mc/antiphon-server2/locks; exec 8>/home/mc/antiphon-server2/locks/rollout.lock; flock -w 60 8; printf \"C1008_LOCKED\\n\"; read -r release";
            var process = Process.Start(Start("ssh", SshOptions.Concat(new[] { command }), input: true));
            var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                var ready = process.StandardOutput.ReadLineAsync();
                if (!ready.Wait(70000) || !string.Equals(ready.Result, "C1008_LOCKED", StringComparison.Ordinal) || process.HasExited)
                    throw new Server2RollingRefusal("RecycleRolloutBusy");
                return new HeldLock(process);
            }
            catch (Exception)
            {
                if (!process.HasExited) { process.Kill(true); process.WaitForExit(); }
                process.Dispose();
                throw new Server2RollingRefusal("RecycleRolloutBusy");
            }
        }

        private sealed class HeldLock : IServer2RollingLock
        {
            private readonly Process _process;

            public HeldLock(Process process) => _process = process;

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

        private static Server2RollingHttpAnswer Send(HttpClient client, string method, string url, IReadOnlyDictionary<string, string> headers, string body)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), url);
            foreach (var header in headers ?? new Dictionary<string, string>()) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            if (body != null) request.Content = new StringContent(body, Utf8, "application/json");
            using var response = client.Send(request);
            using var reader = new StreamReader(response.Content.ReadAsStream(), Utf8);
            return new Server2RollingHttpAnswer((int)response.StatusCode, reader.ReadToEnd());
        }

        /// <inheritdoc />
        public Server2RollingHttpAnswer RunnerHttp(string method, string url, IReadOnlyDictionary<string, string> headers, string body) =>
            Send(_http, method, url, headers, body);

        /// <inheritdoc />
        public Server2RollingHttpAnswer ReadHttp(string path, string url, IReadOnlyDictionary<string, string> headers) =>
            Send(_reads, "GET", url, headers, null);

        // One child with an optional input, all of it read, within the deadline; a child past it is stopped.
        private Server2RollingChild Bounded(string fileName, IEnumerable<string> arguments, string input, int deadline)
        {
            Process process;
            try { process = Process.Start(Start(fileName, arguments, input != null)); }
            catch (Exception e) { throw new Server2RollingChildException(Server2RollingFault.Start, e.Message, e); }
            var timer = Stopwatch.StartNew();
            try
            {
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (input != null)
                {
                    var write = process.StandardInput.WriteAsync(input);
                    if (!write.Wait(deadline)) throw new Server2RollingChildException(Server2RollingFault.Deadline, "deadline");
                    write.GetAwaiter().GetResult();
                    process.StandardInput.Close();
                }
                if (!process.WaitForExit(Remaining(timer, deadline))) throw new Server2RollingChildException(Server2RollingFault.Deadline, "deadline");
                if (!Task.WhenAll(stdout, stderr).Wait(Remaining(timer, deadline))) throw new Server2RollingChildException(Server2RollingFault.Deadline, "drain");
                return new Server2RollingChild(process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
            }
            finally
            {
                try { if (!process.HasExited) { process.Kill(true); process.WaitForExit(); } } catch (InvalidOperationException) { }
                process.Dispose();
            }
        }

        /// <inheritdoc />
        public Server2RollingChild RecycleDiscover(Server2RollingCall call)
        {
            var command = "set -e; command -v jq >/dev/null; for receipt in /home/mc/antiphon-server2/recycle/c1008*.json; do test -e \"$receipt\" || continue; test -f \"$receipt\" && test ! -L \"$receipt\"; jq -er --arg sha " + call.Sha
                + " 'if .schema!=1 then error(\"schema\") elif .sourceSha==$sha and .project==\"antiphon-runner\" and .phase!=\"completed\" then .operationId else empty end' \"$receipt\" || test \"$?\" = 4; done";
            return Bounded("ssh", SshOptions.Concat(new[] { command }), null, 30000);
        }

        /// <inheritdoc />
        public Server2RollingChild TempRetirementRead(Server2RollingCall call)
        {
            var coordinator = File.ReadAllText(Path.Combine(_checkout, "scripts", "c1008-retirement.sh"));
            var remote = File.ReadAllText(Path.Combine(_checkout, "scripts", "c590-remote.sh"));
            var packet = RollingPs.NewC1609RetirementReadPacket(coordinator, remote, call.Operation, string.IsNullOrEmpty(call.Operation));
            return Bounded("ssh", SshOptions.Concat(new[] { "bash", "-s" }), packet, 30000);
        }

        /// <inheritdoc />
        public Server2RollingChild TempProjectCensus(Server2RollingCall call) =>
            Bounded("ssh", SshOptions.Concat(new[] { "docker ps -aq --filter label=com.docker.compose.project=antiphon-runner-temp" }), null, 30000);

        /// <inheritdoc />
        public int HostCase(string hostCase, string manifestPath, TextWriter output, TextWriter error)
        {
            var verifier = Path.Combine(_checkout, "scripts", "verify-docker-stack.ps1");
            var start = Start("pwsh", new[] { "-NoProfile", "-File", verifier, "-Case", hostCase, "-Manifest", manifestPath }, input: false);
            using var process = new Process { StartInfo = start };
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
        public Server2RollingChild Git(string repo, IReadOnlyList<string> arguments)
        {
            using var process = Process.Start(Start("git", new[] { "-C", repo }.Concat(arguments), input: false));
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            return new Server2RollingChild(process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
        }
    }
}
