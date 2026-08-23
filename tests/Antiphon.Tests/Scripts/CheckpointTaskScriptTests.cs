using System.Diagnostics;
using System.Net;
using System.Text;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0153 S4 — <c>scripts/checkpoint-task.ps1</c>. Drives the real script under pwsh against a
/// stub API and a temp git repo.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointTaskScriptTests
{
    [Test]
    public async Task Dirty_worktree_commits_a_wip_checkpoint()
    {
        using var repo = new ScratchGitRepo("card0153-ckpt");
        await repo.CommitFileAsync("README.md", "base\n");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "wip.cs"), "uncommitted\n");

        using var server = new StubApi(repo.Path);
        var run = await RunAsync(server, "-TaskId", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        run.ExitCode.ShouldBe(0, run.Output);
        run.Output.ShouldContain("checkpoint ");
        run.Output.ShouldContain("not verified");
        var status = await ScratchGitRepo.GitInAsync(repo.Path, "status", "--porcelain");
        status.StdOut.Trim().ShouldBeEmpty("tree is clean after the checkpoint");
        var log = await repo.GitReadAsync("log", "-1", "--pretty=%s");
        log.ShouldContain("wip(checkpoint):");
        log.ShouldContain("not verified");
    }

    [Test]
    public async Task Clean_worktree_exits_zero_with_nothing_to_checkpoint()
    {
        using var repo = new ScratchGitRepo("card0153-ckpt-clean");
        await repo.CommitFileAsync("README.md", "base\n");

        using var server = new StubApi(repo.Path);
        var run = await RunAsync(server, "-TaskId", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        run.ExitCode.ShouldBe(0, run.Output);
        run.Output.ShouldContain("nothing to checkpoint");
        var count = (await repo.GitReadAsync("rev-list", "--count", "HEAD")).Trim();
        count.ShouldBe("1", "no empty commit");
    }

    [Test]
    public async Task Non_git_directory_exits_2()
    {
        var dir = Directory.CreateTempSubdirectory("card0153-nogit").FullName;
        try
        {
            using var server = new StubApi(dir);
            var run = await RunAsync(server, "-TaskId", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
            run.ExitCode.ShouldBe(2, run.Output);
            run.Output.ShouldContain("Not a git repository");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public async Task DryRun_lists_files_and_does_not_commit()
    {
        using var repo = new ScratchGitRepo("card0153-ckpt-dry");
        await repo.CommitFileAsync("README.md", "base\n");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "wip.cs"), "uncommitted\n");

        using var server = new StubApi(repo.Path);
        var run = await RunAsync(server, "-TaskId", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "-DryRun");

        run.ExitCode.ShouldBe(0, run.Output);
        run.Output.ShouldContain("Would checkpoint");
        run.Output.ShouldContain("wip.cs");
        var count = (await repo.GitReadAsync("rev-list", "--count", "HEAD")).Trim();
        count.ShouldBe("1");
    }

    [Test]
    public async Task Shared_checkout_with_another_active_worker_exits_3()
    {
        using var repo = new ScratchGitRepo("card0153-ckpt-shared");
        await repo.CommitFileAsync("README.md", "base\n");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "wip.cs"), "uncommitted\n");

        using var server = new StubApi(repo.Path, extraOpenTaskOnSameDir: true);
        var run = await RunAsync(server, "-TaskId", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        run.ExitCode.ShouldBe(3, run.Output);
        run.Output.ShouldContain("shared checkout");
        var count = (await repo.GitReadAsync("rev-list", "--count", "HEAD")).Trim();
        count.ShouldBe("1", "refused, so nothing committed");
    }

    private static Task<(int ExitCode, string Output)> RunAsync(StubApi server, params string[] args)
    {
        var scriptPath = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "checkpoint-task.ps1");
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
        startInfo.Environment["ANTIPHON_API"] = server.BaseUrl.TrimEnd('/');
        startInfo.Environment["ANTIPHON_TASK_TOKEN"] = string.Empty;

        return RunProcessAsync(startInfo);
    }

    private static async Task<(int ExitCode, string Output)> RunProcessAsync(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("pwsh did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await stdout + await stderr);
    }

    private sealed class StubApi : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _pump;
        private readonly string _dir;
        private readonly bool _extra;

        public StubApi(string workingDirectory, bool extraOpenTaskOnSameDir = false)
        {
            _dir = workingDirectory.Replace('\\', '/');
            _extra = extraOpenTaskOnSameDir;
            var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            BaseUrl = $"http://localhost:{port}/";
            _listener.Prefixes.Add(BaseUrl);
            _listener.Start();
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

                var path = context.Request.Url?.AbsolutePath ?? "";
                var query = context.Request.Url?.Query ?? "";
                object body;
                var self = new
                {
                    id = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                    summary = new
                    {
                        id = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                        title = "CARD-0153 stall checkpoint",
                        status = "Working",
                        workspace = "Shared",
                        workingDirectory = _dir,
                    },
                };
                if (path.Contains("/api/agent-tasks/", StringComparison.OrdinalIgnoreCase)
                    && !query.Contains("status=", StringComparison.OrdinalIgnoreCase))
                {
                    body = self;
                }
                else if (query.Contains("status=Working", StringComparison.OrdinalIgnoreCase))
                {
                    body = _extra
                        ? new object[]
                        {
                            self,
                            new
                            {
                                id = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
                                workingDirectory = _dir,
                                summary = new
                                {
                                    id = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
                                    status = "Working",
                                    workingDirectory = _dir,
                                },
                            },
                        }
                        : new object[] { self };
                }
                else
                {
                    body = Array.Empty<object>();
                }

                var json = System.Text.Json.JsonSerializer.Serialize(body);

                var payload = Encoding.UTF8.GetBytes(json);
                context.Response.StatusCode = 200;
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
            _cts.Dispose();
        }
    }
}
