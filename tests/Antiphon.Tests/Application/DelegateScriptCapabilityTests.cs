using System.Net;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0398 S2: delegate.ps1 -Capability sends the header and never assigns the env bearer.</summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[SupportedOSPlatform("windows")]
public sealed class DelegateScriptCapabilityTests
{
    [Test]
    public async Task delegate_Capability_sends_header_and_does_not_assign_env_token()
    {
        var source = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "delegate.ps1"));
        source.ShouldNotContain("$env:ANTIPHON_TASK_TOKEN =");
        source.ShouldNotContain("$env:ANTIPHON_TASK_TOKEN=");

        var token = NewToken();
        using var store = new TempDir("cap-store");
        WriteBlob(store.Path, "chat-codex", token);
        using var server = new StubApi();
        var run = await DelegateScriptRunner.RunAsync(
            server.BaseUrl,
            new Dictionary<string, string?> { ["ANTIPHON_CAPABILITY_STORE"] = store.Path },
            "-Role", "Docs", "-Goal", "do it", "-Capability", "chat-codex");

        run.ExitCode.ShouldBe(0, run.Output);
        server.RequestCount.ShouldBe(1);
        server.LastTokenHeader.ShouldBe(token);
        run.Output.ShouldNotContain(token);
    }

    [Test]
    public async Task delegate_ANTIPHON_CAPABILITY_name_sends_header()
    {
        var token = NewToken();
        using var store = new TempDir("cap-store");
        WriteBlob(store.Path, "env-cap", token);
        using var server = new StubApi();
        var run = await DelegateScriptRunner.RunAsync(
            server.BaseUrl,
            new Dictionary<string, string?>
            {
                ["ANTIPHON_CAPABILITY_STORE"] = store.Path,
                ["ANTIPHON_CAPABILITY"] = "env-cap",
                ["ANTIPHON_TASK_TOKEN"] = string.Empty,
            },
            "-Role", "Docs", "-Goal", "do it");

        run.ExitCode.ShouldBe(0, run.Output);
        server.LastTokenHeader.ShouldBe(token);
        run.Output.ShouldNotContain(token);
    }

    [Test]
    public async Task capability_and_task_token_together_is_refused_before_request()
    {
        var token = NewToken();
        using var store = new TempDir("cap-store");
        WriteBlob(store.Path, "both-cap", token);
        using var server = new StubApi();
        var run = await DelegateScriptRunner.RunAsync(
            server.BaseUrl,
            new Dictionary<string, string?>
            {
                ["ANTIPHON_CAPABILITY_STORE"] = store.Path,
                ["ANTIPHON_TASK_TOKEN"] = token,
            },
            "-Role", "Docs", "-Goal", "do it", "-Capability", "both-cap");

        run.ExitCode.ShouldNotBe(0);
        run.Output.ShouldContain("not both");
        server.RequestCount.ShouldBe(0);
        run.Output.ShouldNotContain(token);
    }

    [Test]
    public async Task missing_capability_file_does_not_advise_AllowedRoots()
    {
        using var store = new TempDir("cap-store");
        using var server = new StubApi();
        var run = await DelegateScriptRunner.RunAsync(
            server.BaseUrl,
            new Dictionary<string, string?> { ["ANTIPHON_CAPABILITY_STORE"] = store.Path },
            "-Role", "Docs", "-Goal", "do it", "-Capability", "missing-cap");

        run.ExitCode.ShouldNotBe(0);
        server.RequestCount.ShouldBe(0);
        run.Output.ShouldContain("missing-cap");
        run.Output.ShouldContain(store.Path);
        run.Output.ShouldNotContain("AllowedRoots");
    }

    [Test]
    public async Task auto_load_of_the_only_store_file_is_forbidden()
    {
        var token = NewToken();
        using var store = new TempDir("cap-store");
        WriteBlob(store.Path, "only-file", token);
        using var server = new StubApi();
        var run = await DelegateScriptRunner.RunAsync(
            server.BaseUrl,
            new Dictionary<string, string?> { ["ANTIPHON_CAPABILITY_STORE"] = store.Path },
            "-Role", "Docs", "-Goal", "do it");

        run.ExitCode.ShouldBe(0, run.Output);
        server.RequestCount.ShouldBe(1);
        string.IsNullOrEmpty(server.LastTokenHeader).ShouldBeTrue(
            "the only store file must not become a silent identity");
    }

    [Test]
    public async Task error_paths_never_contain_the_token()
    {
        var token = NewToken();
        using var store = new TempDir("cap-store");
        WriteBlob(store.Path, "err-cap", token);
        using var server = new StubApi(statusCode: 422, body: """{"detail":"Directory is outside the roots of capability 'err-cap'."}""");
        var run = await DelegateScriptRunner.RunAsync(
            server.BaseUrl,
            new Dictionary<string, string?> { ["ANTIPHON_CAPABILITY_STORE"] = store.Path },
            "-Role", "Docs", "-Goal", "do it", "-Capability", "err-cap");

        run.ExitCode.ShouldNotBe(0);
        run.Output.ShouldNotContain(token);
        run.Output.ShouldNotContain("Add it to Delegation:AllowedRoots");
    }

    private static string NewToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    private static void WriteBlob(string store, string name, string token)
    {
        Directory.CreateDirectory(store);
        var path = Path.Combine(store, name + ".dpapi");
        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(token),
            null,
            DataProtectionScope.CurrentUser);
        File.WriteAllBytes(path, protectedBytes);
    }

    private sealed class StubApi : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _pump;
        private readonly int _status;
        private readonly byte[] _payload;

        public StubApi(int statusCode = 201, string? body = null)
        {
            _status = statusCode;
            _payload = Encoding.UTF8.GetBytes(
                body ?? """{"id":"11111111-1111-1111-1111-111111111111","shortId":"11111111","status":"Queued","modelLevel":"High","warning":null,"agentKind":"ClaudeCode"}""");
            BaseUrl = EphemeralHttpListener.BindLoopback(_listener);
            _pump = Task.Run(PumpAsync);
        }

        public string BaseUrl { get; }
        public int RequestCount { get; private set; }
        public string? LastTokenHeader { get; private set; }

        private async Task PumpAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch (Exception) { return; }

                RequestCount++;
                LastTokenHeader = context.Request.Headers["X-Antiphon-Task-Token"];
                context.Response.StatusCode = _status;
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(_payload);
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
