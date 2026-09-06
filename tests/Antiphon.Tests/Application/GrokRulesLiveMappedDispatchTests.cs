using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Application;

[Explicit]
[Category("Integration")]
[NotInParallel("Headed")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class GrokRulesLiveMappedDispatchTests
{
    [Test]
    [Timeout(600_000)]
    public async Task Live_model_delegate_obeys_file_only_rules_and_consumes_full_brief(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("ANTIPHON_HEADED_TESTS") != "1"
            || Environment.GetEnvironmentVariable("ANTIPHON_GROK_RULES_LIVE_TESTS") != "1")
            throw new SkipTestException("Explicit live Grok and headed opt-ins required");
        var root = Path.Combine(DelegateScriptRunner.RepoRoot, "tests", "Antiphon.Tests", "TestOutput", "Logs",
            nameof(GrokRulesLiveMappedDispatchTests), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var home = Path.Combine(root, "home"); Directory.CreateDirectory(home);
        await File.WriteAllTextAsync(Path.Combine(home, "config.toml"), "[cli]\nuse_leader = false\n[compat.claude]\nhooks = false\nmcps = false\nrules = false\nskills = false\n[compat.codex]\nhooks = false\nskills = false\n", ct);
        var authPath = Environment.GetEnvironmentVariable("ANTIPHON_GROK_TEST_AUTH_PATH")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".grok", "auth.json");
        File.Exists(authPath).ShouldBeTrue("An existing native Grok login is required; never copy or synthesize it for live acceptance");
        using var repo = await RealCliStubBServerHarness.GitRepo.CreateAsync();
        var env = new Dictionary<string,string>();
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var key = (string)entry.Key;
            if (key.StartsWith("GROK_", StringComparison.OrdinalIgnoreCase) || key.StartsWith("XAI_", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("X_LLM_", StringComparison.OrdinalIgnoreCase)) env[key] = "";
        }
        env["GROK_HOME"] = home; env["GROK_AUTH_PATH"] = authPath;
        env["GROK_DISABLE_AUTO_UPDATER"] = "1"; env["GROK_TELEMETRY_ENABLED"] = "0"; env["GROK_FEEDBACK_ENABLED"] = "0";
        await using var runner = new DirectSessionRunnerClient(Path.Combine(root, "runner rules"), ptyBackend: "modern");
        await using var factory = new LiveFactory(runner, repo, env, RealCliStubGate.ResolveGrokOrThrow());
        using var http = factory.CreateClient();
        using var forwarder = new MappedForwarder(http);
        var expectedRules = InstructionBundleComposer.Compose(InstructionBundles.ForDelegate(AgentTaskKind.Worker, AgentTaskRole.Code)).Text;
        var lines = expectedRules.ReplaceLineEndings("\n").Split('\n');
        var indexes = new[] { 1, lines.Length / 2, lines.Length - 2 };
        for (var i = 0; i < indexes.Length; i++) while (string.IsNullOrWhiteSpace(lines[indexes[i]])) indexes[i]--;
        var expected = indexes.Select(i => lines[i].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Last()).ToArray();
        var nonce = "LIVE-" + Guid.NewGuid().ToString("N");
        var goal = nonce + "-HEAD\nThis is a harmless transport acceptance. Make no code changes or external calls.\n"
            + "From your standing rules file, report the final whitespace-delimited word on each of these one-based lines: "
            + string.Join(", ", indexes.Select(i => i + 1)) + ". Label the three answers early, middle, tail. Preserve punctuation.\n"
            + "Also report the HEAD, MIDDLE and TAIL markers found in this brief. This completes the requested work.\n\n"
            + string.Join("\n\n", Enumerable.Range(1, 75).Select(i => (i == 38 ? nonce + "-MIDDLE\n" : "")
                + $"Neutral paragraph {i:D3}: the phrase \"disposable acceptance material\" exists only to exercise full multiline brief delivery."))
            + "\n" + nonce + "-TAIL";
        await File.WriteAllTextAsync(Path.Combine(root, "goal.md"), goal, ct);
        await File.WriteAllTextAsync(Path.Combine(root, "expected.json"), JsonSerializer.Serialize(new { indexes, expected }), ct);
        var wrapper = Path.Combine(root, "dispatch.ps1");
        await File.WriteAllTextAsync(wrapper,"param($Script,$GoalFile,$Repo)\n$goalText=Get-Content -LiteralPath $GoalFile -Raw\n& $Script Code -Kind Grok -Level High -Dir $Repo -Worktree -ReadOnly -Goal $goalText -Title 'CARD-0395 live acceptance' -NoInheritEnv\nexit $LASTEXITCODE\n",ct);
        Guid taskId=Guid.Empty, sessionId=Guid.Empty; var began=DateTimeOffset.UtcNow;var elapsed=Stopwatch.StartNew();string outcome="failed";
        using var pump=new CancellationTokenSource();Task? pumping=null;
        try
        {
            var psi=new ProcessStartInfo("pwsh") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
            foreach(var arg in new[]{"-NoProfile","-NonInteractive","-File",wrapper,Path.Combine(DelegateScriptRunner.RepoRoot,"scripts","delegate.ps1"),Path.Combine(root,"goal.md"),repo.RepoPath}) psi.ArgumentList.Add(arg);
            psi.Environment["ANTIPHON_API"]=forwarder.BaseUrl.TrimEnd('/');psi.Environment["ANTIPHON_TASK_TOKEN"]="";
            using(var process=Process.Start(psi)!)
            {
                var stdout=process.StandardOutput.ReadToEndAsync(ct);var stderr=process.StandardError.ReadToEndAsync(ct);
                try { await process.WaitForExitAsync(ct); } finally { if(!process.HasExited) { process.Kill(true);await process.WaitForExitAsync(); } }
                var output=await stdout+await stderr; await File.WriteAllTextAsync(Path.Combine(root,"delegate.log"),output,ct);
                process.ExitCode.ShouldBe(0,output);output.ShouldContain("[Grok]");
            }
            await using(var db=factory.Db()) { var task=await db.AgentTasks.SingleAsync(t=>t.WorkingDirectory==repo.RepoPath,ct);taskId=task.Id;task.Goal.ShouldBe(goal); }
            using(var scope=factory.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
            await using(var db=factory.Db()) { var task=await db.AgentTasks.SingleAsync(t=>t.Id==taskId,ct);task.Status.ShouldBe(AgentTaskStatus.Dispatched,task.FailureReason);sessionId=task.AgentSessionId.ShouldNotBeNull(); }
            pumping=GrokDelegateEndToEndTests.PumpTranscriptAsync(factory.Services,sessionId,pump.Token);
            await factory.Services.GetRequiredService<AgentSessionLaunchQueue>().WaitForIdleAsync(TimeSpan.FromMinutes(4),ct);
            await GrokDelegateEndToEndTests.WaitUntilAsync(async()=> { await using var db=factory.Db();return await db.AgentTasks.AnyAsync(t=>t.Id==taskId && t.Status==AgentTaskStatus.Succeeded,ct); },TimeSpan.FromMinutes(3),async()=> { await using var db=factory.Db();var t=await db.AgentTasks.SingleAsync(t=>t.Id==taskId,ct);return $"{root}: {t.Status} {t.FailureReason}"; });
            await using var verify=factory.Db();var settled=await verify.AgentTasks.SingleAsync(t=>t.Id==taskId,ct);
            var session=await verify.AgentSessions.SingleAsync(s=>s.Id==sessionId,ct);var receipt=GrokRulesRefreshService.Receipt(session).ShouldNotBeNull();
            var refresh=await verify.SessionQueuedMessages.SingleAsync(m=>m.AgentSessionId==sessionId && m.RulesRefreshKey!=null,ct);
            var brief=await verify.SessionQueuedMessages.SingleAsync(m=>m.AgentSessionId==sessionId && m.RulesRefreshKey==null,ct);
            refresh.RulesAcknowledgedAt.ShouldNotBeNull();brief.CreatedAt.ShouldBeGreaterThanOrEqualTo(refresh.RulesAcknowledgedAt!.Value);
            settled.ReportEvidence.ShouldBe(AgentTaskReportEvidence.Marked);
            var result=settled.Result.ShouldNotBeNull();
            foreach(var answer in expected) result.ShouldContain(answer);
            foreach(var suffix in new[]{"HEAD","MIDDLE","TAIL"}) result.ShouldContain(nonce+"-"+suffix);
            result.ShouldContain("restart: none",customMessage:"This requirement appears only in stage-code standing rules, not in the task goal");
            (await File.ReadAllTextAsync(receipt.Path,ct)).ShouldBe(expectedRules);
            await File.WriteAllTextAsync(Path.Combine(root,"rules.md"),expectedRules,ct);
            await File.WriteAllTextAsync(Path.Combine(root,"result.md"),result,ct);
            await File.WriteAllTextAsync(Path.Combine(root,"timeline.json"),JsonSerializer.Serialize(new{receipt,refresh.Id,refresh.SentAt,refresh.RulesAcknowledgedAt,briefCreatedAt=brief.CreatedAt,settled.Status,settled.CompletedAt,settled.TokensIn,settled.TokensOut,settled.CostUsd}),ct);
            var nativeFiles = Directory.EnumerateFiles(home,"updates.jsonl",SearchOption.AllDirectories).ToArray();
            nativeFiles.Length.ShouldBe(1);
            var native = await File.ReadAllTextAsync(nativeFiles[0],ct);
            await File.WriteAllTextAsync(Path.Combine(root,"native-acp.jsonl"),native,ct);
            var readOutput = string.Join("\n",native.Split('\n').Where(l=>l.Contains("tool_call_update")).SelectMany(l=> {
                using var parsed=JsonDocument.Parse(l);return AllStrings(parsed.RootElement).ToArray();
            }));
            foreach(var line in expectedRules.ReplaceLineEndings("\n").Split('\n').Where(l=>!string.IsNullOrWhiteSpace(l)))
                readOutput.ShouldContain(line,customMessage:"Live native tool output must contain every standing-rules line");
            outcome="passed";
        }
        finally
        {
            elapsed.Stop();pump.Cancel();if(pumping is not null) await pumping;
            if(sessionId!=Guid.Empty)
            {
                try
                {
                    await File.WriteAllTextAsync(Path.Combine(root,"transcript.json"),JsonSerializer.Serialize(await runner.GetTranscriptAsync(sessionId,CancellationToken.None)));
                    await File.WriteAllTextAsync(Path.Combine(root,"snapshot.json"),JsonSerializer.Serialize(await runner.GetSnapshotAsync(sessionId,CancellationToken.None)));
                    var nativeFiles=Directory.EnumerateFiles(home,"updates.jsonl",SearchOption.AllDirectories);
                    await File.WriteAllTextAsync(Path.Combine(root,"native-acp.jsonl"),string.Join("\n",nativeFiles.Select(File.ReadAllText)));
                    await using var db=factory.Db();
                    await File.WriteAllTextAsync(Path.Combine(root,"failure-state.json"),JsonSerializer.Serialize(new {
                        session=await db.AgentSessions.Where(s=>s.Id==sessionId).Select(s=>new{s.Id,s.Status,s.GrokRulesState,s.FailureReason}).SingleAsync(),
                        queue=await db.SessionQueuedMessages.Where(m=>m.AgentSessionId==sessionId).Select(m=>new{m.Id,m.RulesRefreshKey,m.CreatedAt,m.SentAt,m.RulesAcknowledgedAt}).ToListAsync()
                    }));
                }
                finally { await runner.KillAsync(sessionId,CancellationToken.None); }
            }
            await File.WriteAllTextAsync(Path.Combine(root,"manifest.json"),JsonSerializer.Serialize(new{outcome,began,ended=DateTimeOffset.UtcNow,elapsedSeconds=elapsed.Elapsed.TotalSeconds,taskId,sessionId,backend="PtyHost/modern",route="real Program mapped POST /api/agent-tasks/ through transparent HTTP forwarding",auth="native OAuth store reference; no key fallback",model="grok-4.6"}));
        }
    }

    private static IEnumerable<string> AllStrings(JsonElement node)
    {
        if(node.ValueKind==JsonValueKind.String) yield return node.GetString()!;
        else if(node.ValueKind==JsonValueKind.Array) foreach(var c in node.EnumerateArray()) foreach(var s in AllStrings(c)) yield return s;
        else if(node.ValueKind==JsonValueKind.Object) foreach(var p in node.EnumerateObject()) foreach(var s in AllStrings(p.Value)) yield return s;
    }
    private sealed class LiveFactory(DirectSessionRunnerClient runner,RealCliStubBServerHarness.GitRepo repo,Dictionary<string,string> env,string grok):AntiphonWebAppFactory
    {
        protected override void ApplyTestOverrides(IServiceCollection services)
        {
            services.RemoveAll<ISessionRunnerClient>();services.AddSingleton<ISessionRunnerClient>(runner);
            services.RemoveAll<IHostedService>(); // Drive the production dispatch/sync owners explicitly; no unrelated periodic work.
            services.PostConfigure<AgentRegistrySettings>(s=> { s.Definitions.Clear();s.DefaultDefinition="grok";s.GrokCredentialProbeEnabled=true;s.Definitions["grok"]=new(){Kind="Grok",Exe=grok,ArgsTemplate=["--always-approve","--no-alt-screen","--no-subagents","--disable-web-search"],Env=env,NonSecretEnvironmentNames=["GROK_HOME","GROK_AUTH_PATH","GROK_DISABLE_AUTO_UPDATER","GROK_TELEMETRY_ENABLED","GROK_FEEDBACK_ENABLED"]}; });
            services.PostConfigure<DelegationSettings>(s=> { s.AllowedRoots=[repo.RepoPath];s.CheckEnabled=false;s.CheckInterpreterEnabled=false;s.DiagnoseEnabled=false;s.OutputDistillerEnabled=false; });
            services.PostConfigure<GitSettings>(s=> { s.WorkspacePath=repo.RepoPath;s.WorktreeBasePath=repo.WorktreeRoot; });
        }
        public AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString, o => o.MigrationsAssembly("Antiphon.Server").SetPostgresVersion(16,0)).Options);
    }
    private sealed class MappedForwarder:IDisposable
    {
        private readonly HttpListener listener=new();private readonly HttpClient client;private readonly Task pump;
        public string BaseUrl { get; }
        public MappedForwarder(HttpClient http) { client=http;BaseUrl=EphemeralHttpListener.BindLoopback(listener);pump=Task.Run(RunAsync); }
        private async Task RunAsync()
        {
            while(listener.IsListening)
            {
                HttpListenerContext c;try{c=await listener.GetContextAsync();}catch{break;}
                try
                {
                    using var request=new HttpRequestMessage(new HttpMethod(c.Request.HttpMethod),c.Request.RawUrl);
                    using var reader=new StreamReader(c.Request.InputStream);request.Content=new StringContent(await reader.ReadToEndAsync(),Encoding.UTF8,"application/json");
                    using var response=await client.SendAsync(request);c.Response.StatusCode=(int)response.StatusCode;c.Response.ContentType="application/json";
                    await c.Response.OutputStream.WriteAsync(await response.Content.ReadAsByteArrayAsync());c.Response.Close();
                }
                catch { c.Response.StatusCode=500;c.Response.Close(); }
            }
        }
        public void Dispose(){listener.Stop();listener.Close();pump.GetAwaiter().GetResult();}
    }
}
