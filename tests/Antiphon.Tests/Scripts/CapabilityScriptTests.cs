using System.Diagnostics;
using System.Net;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>CARD-0398 S2: capability.ps1 never prints the bearer and writes a DPAPI blob.</summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[SupportedOSPlatform("windows")]
public sealed class CapabilityScriptTests
{
    [Test]
    public async Task issue_does_not_print_the_token_and_dpapi_round_trips()
    {
        var canary = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        using var store = new TempDir("cap-store");
        using var root = new TempDir("cap-root");
        using var server = new IssueStub(canary, "script-cap", root.Path);
        var run = await RunAsync(
            server.BaseUrl,
            store.Path,
            "issue", "-Name", "script-cap", "-Roots", root.Path);

        run.ExitCode.ShouldBe(0, run.Output);
        run.Output.ShouldNotContain(canary);
        run.Output.ShouldContain("script-cap");
        var blob = Path.Combine(store.Path, "script-cap.dpapi");
        File.Exists(blob).ShouldBeTrue(blob);
        var plain = Encoding.UTF8.GetString(
            ProtectedData.Unprotect(File.ReadAllBytes(blob), null, DataProtectionScope.CurrentUser));
        plain.ShouldBe(canary);
    }

    [Test]
    public async Task error_paths_never_contain_the_token()
    {
        var canary = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        using var store = new TempDir("cap-store");
        using var root = new TempDir("cap-root");
        using var server = new IssueStub(canary, "err-cap", root.Path, statusCode: 422, body: """{"detail":"roots invalid"}""");
        var run = await RunAsync(
            server.BaseUrl,
            store.Path,
            "issue", "-Name", "err-cap", "-Roots", root.Path);

        run.ExitCode.ShouldNotBe(0);
        run.Output.ShouldNotContain(canary);
        run.Output.ShouldNotContain("AllowedRoots");
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(
        string apiBaseUrl, string store, params string[] args)
    {
        var scriptPath = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "capability.ps1");
        var startInfo = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);
        startInfo.Environment["ANTIPHON_API"] = apiBaseUrl.TrimEnd('/');
        startInfo.Environment["ANTIPHON_CAPABILITY_STORE"] = store;
        startInfo.Environment["ANTIPHON_TASK_TOKEN"] = string.Empty;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("pwsh did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await stdout + await stderr);
    }

    private sealed class IssueStub : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _pump;
        private readonly string _payload;
        private readonly int _status;

        public IssueStub(string token, string name, string root, int statusCode = 201, string? body = null)
        {
            _status = statusCode;
            _payload = body ?? $$"""
                {"id":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","name":"{{name}}",
                 "roots":["{{root.Replace("\\", "\\\\")}}"],"boardId":null,"projectId":null,
                 "token":"{{token}}","storePath":"C:\\\\hint\\\\{{name}}.dpapi",
                 "createdAt":"2026-09-05T00:00:00Z","lastUsedAt":null,"rotatedAt":null,"revokedAt":null}
                """;
            BaseUrl = EphemeralHttpListener.BindLoopback(_listener);
            _pump = Task.Run(PumpAsync);
        }

        public string BaseUrl { get; }

        private async Task PumpAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch (Exception) { return; }

                var bytes = Encoding.UTF8.GetBytes(_payload);
                context.Response.StatusCode = _status;
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch (Exception) { }
            try { _pump.Wait(TimeSpan.FromSeconds(5)); } catch (Exception) { }
            _listener.Close();
            _cts.Dispose();
        }
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir(string prefix) => Path = Directory.CreateTempSubdirectory(prefix).FullName;
        public string Path { get; }
        public void Dispose()
        {
            try { Directory.Delete(Path, true); } catch (IOException) { }
        }
    }
}
