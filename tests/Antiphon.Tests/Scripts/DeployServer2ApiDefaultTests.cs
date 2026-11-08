using System.Diagnostics;
using System.Net;
using Antiphon.TestSupport;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1777. deploy-server2.ps1 requires ANTIPHON_API and does not read the
/// operator token, or call docker, when that name is unset. check-census still
/// skips the token. The value is never printed.
/// </summary>
[NativeLinuxOnly("Linux-only by construction: the witness puts a #!/bin/sh docker and ssh on PATH and chmod's them. The case remains required on Linux.")]
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class DeployServer2ApiDefaultTests
{
    private const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string TokenSentinel = "SENTINEL_C1777_OPERATOR_TOKEN";

    [Test]
    public async Task Unset_refuses_before_token_read_or_docker()
    {
        foreach (var phase in new[] { "deploy-temp", "check-census" })
        {
            var removed = await Run(phase, api: null, token: TokenMode.Missing);
            AssertRefused(removed, phase);
            var blank = await Run(phase, api: "   ", token: TokenMode.Missing);
            AssertRefused(blank, phase);
        }
    }

    [Test]
    public async Task Set_api_value_is_used_for_the_census_read()
    {
        await using var listener = await LoopbackListener.Start();
        var run = await Run("check-census", listener.Api, TokenMode.Present);
        listener.Requests.ShouldBe(new[] { listener.Api + "/api/projects?includeArchived=true" }, run.Output);
        run.Output.ShouldNotContain(listener.Api, Case.Sensitive);
        run.Output.ShouldNotContain(TokenSentinel, Case.Sensitive);
        run.Output.ShouldNotContain("OperatorTokenMissing", Case.Sensitive);
        run.Output.ShouldNotContain("localhost:17202", Case.Sensitive);
        run.DockerCalled.ShouldBeFalse(run.Output);
    }

    [Test]
    public async Task Check_census_does_not_read_the_operator_token()
    {
        await using var listener = await LoopbackListener.Start();
        var run = await Run("check-census", listener.Api, TokenMode.Missing);
        listener.Requests.ShouldBe(new[] { listener.Api + "/api/projects?includeArchived=true" }, run.Output);
        run.Exit.ShouldBe(2, run.Output);
        run.Output.ShouldNotContain("OperatorTokenMissing", Case.Sensitive);
        run.Output.ShouldNotContain("localhost:17202", Case.Sensitive);
        run.DockerCalled.ShouldBeFalse(run.Output);
    }

    private static void AssertRefused(ScriptRun run, string phase)
    {
        run.Exit.ShouldBe(2, phase + " " + run.Output);
        run.Output.ShouldContain("ANTIPHON_API is required", Case.Sensitive, phase);
        run.Output.ShouldContain("docs/docker-stack.md", Case.Sensitive, phase);
        run.Output.ShouldNotContain("OperatorTokenMissing", Case.Sensitive, phase);
        run.Output.ShouldNotContain("localhost:17202", Case.Sensitive, phase);
        run.Output.ShouldNotContain(TokenSentinel, Case.Sensitive, phase);
        run.DockerCalled.ShouldBeFalse(phase + " " + run.Output);
    }

    private static async Task<ScriptRun> Run(string phase, string? api, TokenMode token)
    {
        var root = Path.Combine(Path.GetTempPath(), "c1777-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var bin = Path.Combine(root, "bin");
            Directory.CreateDirectory(bin);
            var marker = Path.Combine(root, "called");
            foreach (var name in new[] { "docker", "ssh" })
            {
                var path = Path.Combine(bin, name);
                await File.WriteAllTextAsync(path, "#!/bin/sh\nprintf '%s\\n' \"$0\" >> \"$C1777_CALLED\"\nexit 0\n");
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            }

            var tokenPath = Path.Combine(root, "operator-token");
            if (token == TokenMode.Present)
                await File.WriteAllTextAsync(tokenPath, TokenSentinel);

            var psi = new ProcessStartInfo("pwsh")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = DelegateScriptRunner.RepoRoot,
            };
            foreach (var arg in new[]
                     {
                         "-NoProfile", "-NonInteractive", "-File",
                         Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "deploy-server2.ps1"),
                         "-Rolling", "-Sha", Sha, "-Phase", phase,
                     })
                psi.ArgumentList.Add(arg);

            psi.Environment.Remove("ANTIPHON_API");
            psi.Environment.Remove("ANTIPHON_TASK_TOKEN");
            foreach (var proxy in new[]
                     {
                         "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY",
                         "http_proxy", "https_proxy", "all_proxy", "no_proxy",
                     })
                psi.Environment.Remove(proxy);
            if (api is not null)
                psi.Environment["ANTIPHON_API"] = api;
            psi.Environment["ANTIPHON_OPERATOR_TOKEN_FILE"] = tokenPath;
            psi.Environment["PATH"] = bin + ":" + psi.Environment["PATH"];
            psi.Environment["C1777_CALLED"] = marker;
            psi.Environment["TEMP"] = "/tmp";
            psi.Environment["TMP"] = "/tmp";

            using var process = Process.Start(psi) ?? throw new InvalidOperationException("pwsh did not start");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await process.WaitForExitAsync(deadline.Token);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
                throw;
            }

            var output = await stdout + await stderr;
            return new ScriptRun(process.ExitCode, output, File.Exists(marker));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private enum TokenMode { Missing, Present }

    private sealed record ScriptRun(int Exit, string Output, bool DockerCalled);

    private sealed class LoopbackListener : IAsyncDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Task _accept;
        private readonly List<string> _requests = new();

        private LoopbackListener(int port)
        {
            Api = "http://127.0.0.1:" + port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _listener.Prefixes.Add(Api + "/");
            _listener.Start();
            _accept = Accept();
        }

        public string Api { get; }

        public string[] Requests
        {
            get
            {
                lock (_requests)
                    return _requests.ToArray();
            }
        }

        public static async Task<LoopbackListener> Start()
        {
            for (var attempt = 0; attempt < 8; attempt++)
            {
                var port = Random.Shared.Next(20000, 45000);
                try
                {
                    return new LoopbackListener(port);
                }
                catch (HttpListenerException)
                {
                    await Task.Delay(20);
                }
            }

            throw new InvalidOperationException("loopback listener did not bind");
        }

        private async Task Accept()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (HttpListenerException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                var url = context.Request.Url!.GetComponents(UriComponents.SchemeAndServer | UriComponents.PathAndQuery, UriFormat.UriEscaped);
                lock (_requests)
                    _requests.Add(url);
                var body = "[]"u8.ToArray();
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = body.Length;
                await context.Response.OutputStream.WriteAsync(body);
                context.Response.Close();
            }
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            _listener.Close();
            try
            {
                await _accept.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (TimeoutException)
            {
            }
        }
    }
}
