using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Shouldly;

namespace Antiphon.SessionRunner.Tests;

internal sealed class RestartFixture : IDisposable
{
    private readonly RestartPreflightCache _cache;
    private int _seq;

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "antiphon-c420-" + Guid.NewGuid().ToString("N"));
    public static string Repo
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln"))) dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException();
        }
    }
    public Dictionary<string, object?> Config { get; } = new()
    { ["healthyAt"] = 0, ["status"] = 200, ["startMs"] = 0 };

    public int ValidatorChildStarts { get; private set; }
    public int ExecutionChildStarts { get; private set; }
    public int ScriptChildStarts { get; private set; }
    public TimeSpan ChildTimeout { get; set; } = TimeSpan.FromSeconds(45);
    public Action? OnBeforeExecution { get; set; }
    public int LastChildPid { get; private set; }
    public string CopiedEntry => Path.Combine(Root, "scripts", "restart-session-runner.ps1");
    public string CopiedHelper => Path.Combine(Root, "scripts", RestartAstValidator.CopiedHelperFileName);
    public string CopiedPlatform => Path.Combine(Root, "scripts", "platform.ps1");
    public string WrapperPath => Path.Combine(Root, "scripts", "session-runner-restart-health.ps1");
    public string EvidenceRoot => Path.Combine(Repo, ".antiphon", "c420-evidence", Path.GetFileName(Root));

    public RestartFixture(RestartPreflightCache? cache = null)
    {
        _cache = cache ?? RestartPreflightCache.Shared;
        Directory.CreateDirectory(Path.Combine(Root, "scripts"));
        File.WriteAllText(Path.Combine(Root, "state"), "stopped-sentinel");
        File.WriteAllText(Path.Combine(Root, "pid"), "pid-sentinel");
        var sourceEntry = Path.Combine(Repo, "scripts", "restart-session-runner.ps1");
        File.Copy(sourceEntry, CopiedEntry);
        SHA256.HashData(File.ReadAllBytes(CopiedEntry)).ShouldBe(SHA256.HashData(File.ReadAllBytes(sourceEntry)));
        var sourceHelper = Path.Combine(Repo, "scripts", "session-runner-restart-health.ps1");
        File.Copy(sourceHelper, CopiedHelper);
        SHA256.HashData(File.ReadAllBytes(CopiedHelper)).ShouldBe(SHA256.HashData(File.ReadAllBytes(sourceHelper)));
        var sourcePlatform = Path.Combine(Repo, "tests", "Antiphon.SessionRunner.Tests", "Fixtures", "RunnerRestart", "platform.ps1");
        File.Copy(sourcePlatform, CopiedPlatform);
        SHA256.HashData(File.ReadAllBytes(CopiedPlatform)).ShouldBe(SHA256.HashData(File.ReadAllBytes(sourcePlatform)));
        File.WriteAllBytes(WrapperPath, RestartAstValidator.WrapperBytes);
    }

    public static string Quote(string value) => value.Replace("'", "''");

    public async Task<(int Exit, string Output)> Script(string script, string shell = "pwsh.exe", params string[] args)
    {
        ScriptChildStarts++;
        var path = Path.Combine(Root, "driver.ps1");
        File.WriteAllText(path, script);
        var resolved = new ShellIdentityResolver().Resolve(shell);
        return await Process(path, resolved.NormalizedPath, args);
    }

    private async Task<(int Exit, string Output)> Process(string path, string shellPath, params string[] args)
    {
        var psi = new ProcessStartInfo(shellPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", path }.Concat(args))
            psi.ArgumentList.Add(arg);
        using var process = System.Diagnostics.Process.Start(psi)!;
        LastChildPid = process.Id;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(ChildTimeout);
        }
        catch
        {
            if (!process.HasExited)
                process.Kill(true);
            await process.WaitForExitAsync();
            throw;
        }
        return (process.ExitCode, (await stdout) + (await stderr));
    }

    public async Task<RestartResult> Run(string shell = "pwsh.exe", params string[] args)
    {
        foreach (var required in new[] { CopiedEntry, CopiedHelper, CopiedPlatform, WrapperPath })
        {
            if (!File.Exists(required))
                throw new PreflightRefusalException("missing input " + Path.GetFileName(required));
        }

        var wrapperBytes = File.ReadAllBytes(WrapperPath);
        if (!wrapperBytes.AsSpan().SequenceEqual(RestartAstValidator.WrapperBytes))
            throw new PreflightRefusalException("wrapper contract");

        var handles = new List<FileStream>();
        var validatorStarted = false;
        var cached = false;
        var preflightMs = 0d;
        var executionMs = 0d;
        try
        {
            try
            {
                foreach (var path in new[] { CopiedEntry, CopiedHelper, CopiedPlatform, WrapperPath })
                    handles.Add(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
            }
            catch (IOException ex)
            {
                throw new PreflightRefusalException("cannot bind input", ex);
            }

            var inputs = new PreflightInputs(shell, CopiedEntry, CopiedHelper, CopiedPlatform, WrapperPath);
            var preflightClock = Stopwatch.StartNew();
            var disposition = await _cache.ApproveAsync(
                inputs,
                validate: async (call, ct) =>
                {
                    validatorStarted = true;
                    ValidatorChildStarts++;
                    var validatorPath = Path.Combine(Root, "preflight-validator.ps1");
                    await File.WriteAllTextAsync(validatorPath, RestartAstValidator.Text, new UTF8Encoding(false), ct);
                    var r = await Process(
                        validatorPath,
                        call.Shell.NormalizedPath,
                        "-EntryPath",
                        call.Inputs.EntryPath,
                        "-HelperPath",
                        call.Inputs.HelperPath);
                    if (r.Exit != 0)
                        throw new InvalidOperationException(r.Output);
                    return r.Exit;
                });
            cached = disposition == PreflightDisposition.Hit;
            preflightMs = preflightClock.Elapsed.TotalMilliseconds;

            OnBeforeExecution?.Invoke();

            var launched = new ShellIdentityResolver().Resolve(shell);
            File.WriteAllText(Path.Combine(Root, "config.json"), JsonSerializer.Serialize(Config));
            ExecutionChildStarts++;
            var execClock = Stopwatch.StartNew();
            var run = await Process(CopiedEntry, launched.NormalizedPath, args);
            executionMs = execClock.Elapsed.TotalMilliseconds;
            var lines = run.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var final = lines.Where(l => l.StartsWith("RUNNER RESTART RESULT: ")).ToArray();
            final.Length.ShouldBe(1, run.Output);
            lines.Last().ShouldBe(final[0]);
            var result = JsonDocument.Parse(final[0][23..]).RootElement.Clone();
            result.GetProperty("exitCode").GetInt32().ShouldBe(run.Exit, run.Output);
            var artifact = EvidenceRoot;
            Directory.CreateDirectory(artifact);
            File.WriteAllText(Path.Combine(artifact, "output.txt"), run.Output);
            foreach (var name in new[] { "config.json", "trace.jsonl", "startup.log" })
                if (File.Exists(Path.Combine(Root, name)))
                    File.Copy(Path.Combine(Root, name), Path.Combine(artifact, name), true);
            var seq = Interlocked.Increment(ref _seq);
            File.WriteAllText(
                Path.Combine(artifact, "preflight-" + seq + ".json"),
                JsonSerializer.Serialize(new
                {
                    cached,
                    validatorChildStarted = validatorStarted,
                    executionChildStarted = true,
                    preflightMs,
                    executionMs,
                    shell = launched.NormalizedPath
                }));
            return new(run.Exit, result, run.Output, Trace());
        }
        finally
        {
            foreach (var handle in handles)
                handle.Dispose();
        }
    }

    public JsonElement[] Trace() => File.Exists(Path.Combine(Root, "trace.jsonl"))
        ? File.ReadAllLines(Path.Combine(Root, "trace.jsonl")).Select(x => JsonDocument.Parse(x).RootElement.Clone()).ToArray() : [];

    public async Task DecodeCapturedMilestones(string content, int producerPid, string processStart, string producer, string expectedPhase)
    {
        File.WriteAllText(Path.Combine(Root, "captured.log"), content);
        var script = $$"""
            $ErrorActionPreference='Stop'
            . '{{Quote(Path.Combine(Repo, "scripts", "session-runner-restart-health.ps1"))}}'
            $identity=@{pid={{producerPid}};startTimeUtc='{{processStart}}';path='fixture-owned'}
            $p=@{
                Process={param($id) if($id -eq {{producerPid}}){$identity} }
                Verify={param($i) $i.pid -eq {{producerPid}} }
                Census={ @{supervisor={{(producer == "supervisor" ? "$identity" : "$null")}} } }
            }
            $reader=New-RunnerMilestoneReader '{{Quote(Path.Combine(Root, "captured.log"))}}'
            Read-RunnerMilestones $reader $p '2000-01-01T00:00:00Z'
            if($reader.Phase.event -ne '{{expectedPhase}}'){throw "production decoder did not accept producer records: $($reader.Phase.event)"}
            """;
        var result = await Script(script); result.Exit.ShouldBe(0, result.Output);
    }

    public void Dispose() { try { Directory.Delete(Root, true); } catch { } }
}

internal sealed record RestartResult(int Exit, JsonElement Json, string Output, JsonElement[] Trace)
{
    public string Outcome => Json.GetProperty("outcome").GetString()!;
    public double Wait => Json.GetProperty("waitElapsedMs").GetDouble();
    public string[] Mutations => Trace.Select(t => t.GetProperty("op").GetString()!).Where(op => op is not ("probe" or "sleep")).ToArray();
}
