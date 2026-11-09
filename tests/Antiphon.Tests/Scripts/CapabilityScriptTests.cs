using System.Diagnostics;
using System.Net;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

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

    /// <summary>
    /// CARD-1811: ProtectedData is Windows-only, so the DPAPI case skips off Windows
    /// before it starts capability.ps1 and the Linux rc profile excludes exactly that
    /// method. The error-path sibling stays required on every platform.
    /// </summary>
    [Test]
    public async Task C1811_Dpapi_case_is_windows_required_and_linux_excluded()
    {
        var policyPath = Path.Combine(DelegateScriptRunner.RepoRoot, "tests", "test-execution-policy.json");
        using var policy = JsonDocument.Parse(await File.ReadAllTextAsync(policyPath));
        var rows = policy.RootElement.GetProperty("profiles").GetProperty("rc").GetProperty("exclusions")
            .EnumerateArray()
            .Where(row => row.GetProperty("class").GetString() == nameof(CapabilityScriptTests))
            .ToArray();
        rows.Length.ShouldBe(1, "one rc exclusion row for CapabilityScriptTests");
        var row = rows[0];
        row.GetProperty("suite").GetString().ShouldBe("antiphon");
        row.GetProperty("platform").GetString().ShouldBe("linux");
        row.GetProperty("methods").EnumerateArray().Select(method => method.GetString()).ToArray()
            .ShouldBe([nameof(issue_does_not_print_the_token_and_dpapi_round_trips)]);
        row.GetProperty("reason").GetString().ShouldNotStartWith("Linux-only by construction:");
        row.GetProperty("owner").GetString().ShouldBe("CARD-1811");

        if (OperatingSystem.IsWindows())
            return; // The DPAPI case itself is the Windows witness.

        var skip = await Should.ThrowAsync<SkipTestException>(issue_does_not_print_the_token_and_dpapi_round_trips());
        skip.Message.ShouldContain("DPAPI");
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
        private readonly HttpListener _listener;
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
            BaseUrl = EphemeralHttpListener.BindLoopback(out _listener);
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
