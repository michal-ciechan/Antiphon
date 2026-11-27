// The production world of the CARD-1609 retirement entry and the CARD-0849 front door (unit 67 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md): the children scripts/retire-runner-volumes.ps1,
// scripts/c1609-manual.ps1 and scripts/verify-card0849-caches.ps1 started, with the same argument vectors, deadlines and streams,
// the runner status request their Invoke-RestMethod made, and the stack verifier in this process (they started
// scripts/verify-docker-stack.ps1 by path). ssh and scp are found as .NET found them (ProcessStartInfo); git as PowerShell found it
// (C590BridgeOs.Find: PATH with PATHEXT on Windows), its errors on this command's error stream. Plain C#: no PowerShell types.
// ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Abstractions;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Antiphon.Nightly
{
    /// <summary>The retirement entry's real children, status request and in-process verifier.</summary>
    public sealed class RetireRunnerVolumesOs : IRetireRunnerVolumesWorld
    {
        private readonly NightlyHost _host;
        private readonly IFileSystem _files;
        private readonly string _checkout;
        private readonly C590BridgeSeams _bridge;

        /// <summary>A world on this host and file system; the verifier runs the checkout's cases on the bridge's seams.</summary>
        public RetireRunnerVolumesOs(NightlyHost host, IFileSystem files, string checkout, C590BridgeSeams bridge)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _files = files ?? throw new ArgumentNullException(nameof(files));
            _checkout = checkout ?? "";
            _bridge = bridge;
        }

        private static ProcessStartInfo Start(string file, IEnumerable<string> arguments, bool input)
        {
            var start = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = input };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            if (input) start.StandardInputEncoding = new UTF8Encoding(false);
            return start;
        }

        /// <inheritdoc />
        public RetireRunnerVolumesChild Server2Bash(string script, int timeoutMs)
        {
            using var process = Process.Start(Start("ssh", new[] { "-o", "BatchMode=yes", "-o", "ConnectTimeout=15", "mc@server2", "bash", "-s" }, input: true));
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            process.StandardInput.Write(script ?? "");
            process.StandardInput.Close();
            if (!process.WaitForExit(timeoutMs))
            {
                process.Kill(true);
                process.WaitForExit();
                return RetireRunnerVolumesChild.Deadline;
            }
            return new RetireRunnerVolumesChild(process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
        }

        /// <inheritdoc />
        public bool Scp(string remote, string destination)
        {
            var process = Process.Start(Start("scp", new[] { "-o", "BatchMode=yes", "-o", "ConnectTimeout=15", "mc@server2:" + remote, destination }, input: false));
            try
            {
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(30000)) return false;
                return Task.WhenAll(stdout, stderr).Wait(2000) && process.ExitCode == 0;
            }
            finally
            {
                try { if (!process.HasExited) { process.Kill(true); process.WaitForExit(); } } catch (InvalidOperationException) { }
                process.Dispose();
            }
        }

        /// <inheritdoc />
        public string RunnerStatus(string url, string token)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation(OperatorApi.OperatorTokenHeader, token);
            using var response = client.Send(request);
            response.EnsureSuccessStatusCode();
            using var reader = new StreamReader(response.Content.ReadAsStream(), new UTF8Encoding(false));
            return reader.ReadToEnd();
        }

        /// <inheritdoc />
        public IReadOnlyList<string> GitHead(string repo) => GitHeadLines(_host.Error, repo).Lines;

        /// <inheritdoc />
        public int Verify(string hostCase, string manifest) => DockerStackVerify.RunInProcess(hostCase, manifest, _checkout, _host, _files, _bridge);

        /// <summary>
        /// &amp; git -C &lt;repo&gt; rev-parse HEAD as the scripts ran it: git found as PowerShell finds an application (else the message
        /// PowerShell threw, as a C590Failure), its errors on error; its exit code and output lines.
        /// </summary>
        public static (int Exit, IReadOnlyList<string> Lines) GitHeadLines(TextWriter error, string repo)
        {
            var git = new C590BridgeOs(TextWriter.Null, error).Find("git")
                ?? throw new C590Failure("The term 'git' is not recognized as a name of a cmdlet, function, script file, or executable program.");
            var lines = new List<string>();
            using var process = new Process { StartInfo = Start(git, new[] { "-C", repo ?? "", "rev-parse", "HEAD" }, input: false) };
            var gate = new object();
            process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (gate) lines.Add(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (gate) error.WriteLine(e.Data); };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
            return (process.ExitCode, lines);
        }
    }
}
