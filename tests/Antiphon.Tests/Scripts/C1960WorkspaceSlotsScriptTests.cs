using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1960 W-A4, W-A5. The <c>release</c> verb of <c>scripts/workspace-slots.ps1</c>, run as the REAL script
/// under pwsh against a stub API: it posts the disposition, reason and requester to the quarantine release route
/// with the operator token (never printed), maps the runner's outcome to the exit code, and refuses usage errors
/// with exit 2 before any request.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
// CARD-1960 plan, performance budget (provisional): about one pwsh start per case (2-5 s, unmeasured). Re-derive from n>=5.
[Timeout(60_000)]
public sealed class C1960WorkspaceSlotsScriptTests
{
    private const string DirId = "0123456789ab";

    [Test]
    [Arguments("removed", 0)]
    [Arguments("refused", 1)]
    [Arguments("removal_failed", 1)]
    [Arguments("http-409", 1)]
    public async Task Release_posts_body_and_maps_outcomes(string answer, int exitCode)
    {
        // W-A4. Exit 0 only when the runner removed the directory.
        using var server = new StubApi(answer == "http-409"
            ? (409, """{"error":"workspace_slot_quarantine_conflict","detail":"not open"}""")
            : (200, JsonSerializer.Serialize(new
            {
                dirId = DirId,
                outcome = answer,
                detail = answer == "removed" ? null : "dirty: ?? work.txt",
            })));
        using var token = new TokenFile();
        var run = await RunAsync(server, token, "release", "-Runner", "server2", "-DirId", DirId,
            "-Disposition", "AcceptLoss", "-Reason", "diff kept", "-Requester", "orchestrator");

        run.ExitCode.ShouldBe(exitCode, run.Output);
        server.Requests.ShouldBe(1, run.Output);
        server.LastMethod.ShouldBe("POST");
        server.LastPath.ShouldBe("/api/session-runners/server2/workspace-slots/quarantine/" + DirId + "/release");
        server.LastOperatorToken.ShouldBe(token.Value);
        var body = server.LastBody.ShouldNotBeNull().RootElement;
        body.GetProperty("disposition").GetString().ShouldBe("AcceptLoss");
        body.GetProperty("reason").GetString().ShouldBe("diff kept");
        body.GetProperty("requester").GetString().ShouldBe("orchestrator");
        if (answer == "http-409")
            run.Output.ShouldContain("runner=server2 dir=" + DirId + " failed");
        else
            run.Output.ShouldContain("runner=server2 dir=" + DirId + " outcome=" + answer);
        if (answer is "refused" or "removal_failed")
            run.Output.ShouldContain("dirty: ?? work.txt");
        run.Output.ShouldNotContain(token.Value, Case.Insensitive);
    }

    [Test]
    [Arguments("all-runners")]
    [Arguments("slot")]
    [Arguments("missing-dirid")]
    public async Task Release_usage_errors_exit_2_and_file_is_ascii(string shape)
    {
        // W-A5. A usage error exits 2 and sends nothing. The file stays ASCII for Windows PowerShell 5.1.
        using var server = new StubApi((200, """{"dirId":"0123456789ab","outcome":"removed"}"""));
        using var token = new TokenFile();
        string[] args = shape switch
        {
            "all-runners" => ["release", "-AllRunners", "-DirId", DirId, "-Disposition", "Pushed", "-Reason", "published"],
            "slot" => ["release", "-Runner", "server2", "-Slot", "ws-01", "-DirId", DirId, "-Disposition", "Pushed", "-Reason", "published"],
            _ => ["release", "-Runner", "server2", "-Disposition", "Pushed", "-Reason", "published"],
        };
        var run = await RunAsync(server, token, args);

        run.ExitCode.ShouldBe(2, run.Output);
        server.Requests.ShouldBe(0, "a usage error sends nothing");
        run.Output.ShouldNotContain(token.Value, Case.Insensitive);
        var bytes = File.ReadAllBytes(ScriptPath);
        var firstNonAscii = Array.FindIndex(bytes, static b => b > 127);
        firstNonAscii.ShouldBe(-1, $"workspace-slots.ps1 has a non-ASCII byte at offset {firstNonAscii}");
    }

    private static string ScriptPath => Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "workspace-slots.ps1");

    private static async Task<(int ExitCode, string Output)> RunAsync(StubApi server, TokenFile token, params string[] args)
    {
        var startInfo = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(ScriptPath);
        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);
        startInfo.Environment["ANTIPHON_API"] = server.BaseUrl.TrimEnd('/');
        startInfo.Environment["ANTIPHON_OPERATOR_TOKEN_FILE"] = token.Path;
        startInfo.Environment["ANTIPHON_TASK_TOKEN"] = string.Empty;
        startInfo.Environment["ANTIPHON_TASK_ID"] = string.Empty;
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("pwsh did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(50));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        return (process.ExitCode, await stdout + await stderr);
    }

    private sealed class TokenFile : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("c1960-operator-").FullName;

        public TokenFile()
        {
            Path = System.IO.Path.Combine(_directory, "operator-token");
            File.WriteAllText(Path, Value + "\n");
        }

        public string Path { get; }
        public string Value { get; } = Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant();

        public void Dispose()
        {
            try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        }
    }

    private sealed class StubApi : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _pump;
        private readonly (int Status, string Json) _answer;

        public StubApi((int Status, string Json) answer)
        {
            _answer = answer;
            BaseUrl = EphemeralHttpListener.BindLoopback(out _listener);
            _pump = Task.Run(PumpAsync);
        }

        public string BaseUrl { get; }
        public int Requests { get; private set; }
        public string? LastMethod { get; private set; }
        public string? LastPath { get; private set; }
        public string? LastOperatorToken { get; private set; }
        public JsonDocument? LastBody { get; private set; }

        private async Task PumpAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch (Exception) { return; }

                Requests++;
                LastMethod = context.Request.HttpMethod;
                LastPath = context.Request.Url?.AbsolutePath;
                LastOperatorToken = context.Request.Headers["X-Antiphon-Operator-Token"];
                using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                {
                    var raw = await reader.ReadToEndAsync();
                    if (!string.IsNullOrWhiteSpace(raw))
                        LastBody = JsonDocument.Parse(raw);
                }

                var payload = Encoding.UTF8.GetBytes(_answer.Json);
                context.Response.StatusCode = _answer.Status;
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(payload);
                context.Response.Close();
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch (Exception) { }
            try { _pump.Wait(TimeSpan.FromSeconds(5)); } catch (Exception) { }
            _listener.Close();
            LastBody?.Dispose();
            _cts.Dispose();
        }
    }
}
