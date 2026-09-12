using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Integration")]
[NotInParallel("Headed")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CodexCommandLengthHttpAcceptanceTests
{
    [Test, Timeout(120_000)]
    public async Task Oversized_codex_launch_is_a_409_problem_with_no_session_and_no_leak(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
            throw new TUnit.Core.Exceptions.SkipTestException("Owned Windows runner test");

        using var layout = new CodexNpmLayout();
        var root = Path.Combine(Path.GetTempPath(), $"c0497-http-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var artifacts = Path.Combine(
            AppContext.BaseDirectory, "TestOutput", "Logs", nameof(CodexCommandLengthHttpAcceptanceTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(artifacts);
        var info = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "Antiphon.SessionRunner.exe"))
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[]
                 {
                     "--urls", "http://127.0.0.1:0",
                     "--SessionRunner:SessionLogPath", root,
                     "--SessionRunner:Herdr:Enabled", "false",
                     "--SessionRunner:PtyBackend", "modern",
                     "--SessionRunner:PtyHostLingerHours", "0.002",
                     "--Serilog:LogPath", Path.Combine(root, "logs"),
                 })
            info.ArgumentList.Add(arg);

        var listening = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = new ConcurrentQueue<string>();
        using var process = new Process { StartInfo = info };
        void Capture(string? line)
        {
            if (line is null) return;
            output.Enqueue(line);
            var match = Regex.Match(line, @"Now listening on:\s+(http://127\.0\.0\.1:\d+)");
            if (match.Success) listening.TrySetResult(match.Groups[1].Value);
        }
        process.OutputDataReceived += (_, e) => Capture(e.Data);
        process.ErrorDataReceived += (_, e) => Capture(e.Data);
        process.Start().ShouldBeTrue();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        HttpClient? http = null;
        try
        {
            var address = await listening.Task.WaitAsync(TimeSpan.FromSeconds(25), ct);
            new Uri(address).Port.ShouldNotBe(17204);
            http = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(30) };
            var sentinel = "c0497-http-" + Guid.NewGuid().ToString("N");
            var path = layout.Root + Path.PathSeparator + (Environment.GetEnvironmentVariable("PATH") ?? "");
            var request = new RunnerLaunchRequest(
                Guid.NewGuid(),
                "codex.cmd",
                ["--no-alt-screen", "-c", "developer_instructions=" + sentinel + new string('X', 40_000)],
                new Dictionary<string, string> { ["PATH"] = path },
                layout.Root,
                120,
                30,
                TranscriptFormat: TranscriptFormats.Codex,
                CommandLineBudgetChars: 30_000);
            using var response = await http.PostAsJsonAsync("/sessions", request, ct);
            response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            json.GetProperty("type").GetString().ShouldBe(CodexLaunchProblemTypes.CommandLineTooLong);
            var detail = json.GetProperty("detail").GetString().ShouldNotBeNull();
            detail.ShouldContain("30,000");
            detail.ShouldNotContain(sentinel);

            var list = await http.GetStringAsync("/sessions", ct);
            list.ShouldBe("[]");
            File.Exists(PtyHostManifest.PathFor(Path.Combine(root, "pty-hosts", "manifests"), request.SessionId))
                .ShouldBeFalse();
            File.Exists(Path.Combine(root, $"{request.SessionId:N}.ansi.log")).ShouldBeFalse();
            string.Join("\n", output).ShouldNotContain(sentinel);
        }
        finally
        {
            http?.Dispose();
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            await File.WriteAllLinesAsync(Path.Combine(artifacts, "runner-http.log"), output);
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best-effort.
            }
        }
    }
}
