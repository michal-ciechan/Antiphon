using Antiphon.Tests.Application;
using System.Diagnostics;
using System.Text.Json;
using Antiphon.Tests.TestHelpers;

namespace Antiphon.Tests.Scripts;

internal sealed partial class CheckpointSourceScriptFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "c835-script-" + Guid.NewGuid().ToString("N"));
    public string Repo => Path.Combine(Root, "source");
    public string External => Path.Combine(Root, "external");
    public string Head { get; }
    public string Stamp => Path.Combine(Repo, "sample", "bin-c835", "checkpoint-build-source.json");
    public int Calls => File.Exists(Path.Combine(External, "calls.txt"))
        ? File.ReadAllLines(Path.Combine(External, "calls.txt")).Length : 0;
    public int SlotCalls => File.Exists(Path.Combine(External, "slot-calls.txt"))
        ? File.ReadAllLines(Path.Combine(External, "slot-calls.txt")).Length : 0;
    private int _round;
    private CheckpointSourceFixtureImage _seed = null!;
    public bool SessionMode { get; set; }
    public List<DriverResult> Invocations { get; } = [];
    private static string ProjectRoot => DelegateScriptRunner.RepoRoot;

    public CheckpointSourceScriptFixture()
    {
        Directory.CreateDirectory(Repo);
        Directory.CreateDirectory(External);
        Run("git", Repo, ["init", "-q"]);
        Run("git", Repo, ["config", "user.name", "Checkpoint Test"]);
        Run("git", Repo, ["config", "user.email", "checkpoint@example.invalid"]);
        Run("git", Repo, ["config", "core.autocrlf", "false"]);
        Write(".gitignore", "bin-*/\nobj/\n.antiphon/\n");
        Write("tracked.txt", "seed");
        Write("sample/sample.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        Run("git", Repo, ["add", "."]);
        Run("git", Repo, ["commit", "-qm", "seed"]);
        Head = Run("git", Repo, ["rev-parse", "HEAD"]).Output.Trim();
        File.WriteAllText(Path.Combine(External, "shim.ps1"), """
            param()
            $items = @($args)
            Add-Content -LiteralPath $env:C835_CALLS -Value ([string]$items[0])
            $phase = [string]$items[0]
            if ($env:C835_DRIFT -eq $phase) {
                Set-Content -LiteralPath (Join-Path $env:C835_REPO 'tracked.txt') -Value 'changed during driver'
            }
            if ($env:C835_DRIFT -eq ('head-' + $phase)) {
                git -C $env:C835_REPO commit --allow-empty -qm 'move head during driver'
            }
            if ($env:C835_DRIFT -eq ('restore-' + $phase)) {
                git -C $env:C835_REPO restore -- tracked.txt
            }
            if ($env:C835_DRIFT -eq ('same-count-' + $phase)) {
                Set-Content -LiteralPath (Join-Path $env:C835_REPO 'tracked.txt') -Value 'second dirty value'
            }
            if ($phase -eq 'build') { exit [int]$env:C835_BUILD_EXIT }
            $result = ''
            for ($i = 0; $i -lt $items.Count; $i++) {
                if ($items[$i] -eq '--results-directory') { $result = [string]$items[$i + 1] }
            }
            if ($env:C835_TRX -and $result) { Copy-Item -LiteralPath $env:C835_TRX -Destination (Join-Path $result 'run.trx') }
            exit 0
            """);
        File.WriteAllText(Path.Combine(External, "slot-shim.ps1"), """
            param([string]$Method, [string]$Uri, [string]$BodyJson)
            Add-Content -LiteralPath $env:C835_SLOT_CALLS -Value $Method
            if ($env:C835_SLOT_DRIFT -eq '1') {
                Set-Content -LiteralPath (Join-Path $env:C835_REPO 'tracked.txt') -Value 'changed while waiting for slot'
            }
            return @{ Status = 200; Body = '{"unlimited":true,"maxCpuCount":4}' }
            """);
        _seed = CheckpointSourceFixtureImage.Capture(Repo);
    }

    public void ResetScenario()
    {
        // S1: the test observes the old image/counters before any next invocation.
    }

    internal void RestoreSeedForParity()
    {
        _seed.Restore();
        File.Delete(Path.Combine(External, "calls.txt"));
        File.Delete(Path.Combine(External, "slot-calls.txt"));
    }

    public void Write(string path, string value)
    {
        var target = Path.Combine(Repo, path);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, value);
    }

    public async Task<Result> RunAsync(string? expectedSha = null, bool noBuild = false,
        string? trx = "c585-green.trx", int buildExit = 0, string? driftPhase = null,
        bool useSlot = false, bool slotDrift = false, bool busySlot = false)
    {
        var round = ++_round;
        var resultRoot = Path.Combine(External, "results-" + round);
        var args = new List<string> { "-NoProfile", "-NonInteractive", "-File",
            Path.Combine(ProjectRoot, "scripts", "run-checkpoint.ps1"), "-Name", "CP-2",
            "-Project", "sample", "-OutputPath", "bin-c835/", "-Filter", "/*/*/C585SampleTests/*",
            "-ResultsRoot", resultRoot, "-MinExecuted", "1", "-DotnetShim",
            Path.Combine(External, "shim.ps1") };
        if (!useSlot) args.Add("-NoSlot");
        if (expectedSha is not null) args.AddRange(["-ExpectedSourceSha", expectedSha]);
        if (noBuild) args.Add("-NoBuild");
        var environment = new Dictionary<string, string?>
        {
            ["C835_CALLS"] = Path.Combine(External, "calls.txt"),
            ["C835_REPO"] = Repo,
            ["C835_BUILD_EXIT"] = buildExit.ToString(),
            ["C835_TRX"] = trx is null ? null : Path.Combine(ProjectRoot, "scripts", "fixtures", trx),
            ["C835_DRIFT"] = driftPhase,
            ["C835_SLOT_CALLS"] = Path.Combine(External, "slot-calls.txt"),
            ["C835_SLOT_DRIFT"] = slotDrift ? "1" : null,
            ["C589_SLOT_SHIM"] = Path.Combine(External, "slot-shim.ps1"),
            ["C585_STAMP"] = "round-" + round,
            ["ANTIPHON_BUILD_SLOTS_URL"] = "http://127.0.0.1:1/build-slots",
        };
        if (busySlot)
        {
            var busy = Path.Combine(External, "busy-slot.ps1");
            await File.WriteAllTextAsync(busy, "param([string]$Method,[string]$Uri,[string]$BodyJson)\nreturn @{Status=409;Body='{\"type\":\"build_slot_busy\"}'}");
            environment["C589_SLOT_SHIM"] = busy;
            environment["C589_SLOT_WAIT_SECONDS"] = "3";
            environment["C589_SLOT_RETRY_MS"] = "50";
        }
        var result = await InvokeAsync(args[3], args.Skip(4).ToArray(), environment);
        var evidence = Directory.GetFiles(resultRoot, "source.json", SearchOption.AllDirectories).Single();
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(evidence));
        var lines = result.Output.Split('\n');
        var line = lines.Single(value => value.StartsWith("CHECKPOINT CP-2 commit=", StringComparison.Ordinal));
        return new Result(result.Exit, result.Output, line, evidence, json.RootElement.Clone());
    }

    public Task<(int Exit, string Output)> ValidateAsync(string evidence, string? expectedSha = null) =>
        RunAsync("pwsh", Repo, ["-NoProfile", "-NonInteractive", "-File",
            Path.Combine(ProjectRoot, "scripts", "validate-checkpoint-receipt.ps1"),
            "-Evidence", evidence, "-ExpectedSourceSha", expectedSha ?? Head], null);

    public Task<(int Exit, string Output)> CheckSourceHelperAsync(string evidence, string expectedSha)
    {
        static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
        var helper = Path.Combine(ProjectRoot, "scripts", "lib", "checkpoint-source.ps1");
        var command = ". " + Quote(helper) + "; $e = Get-Content -LiteralPath " + Quote(evidence) +
            " -Raw | ConvertFrom-Json; if (Test-CheckpointSourceEvidence $e " + Quote(expectedSha) +
            ") { exit 0 } else { exit 1 }";
        return RunAsync("pwsh", Repo, ["-NoProfile", "-NonInteractive", "-Command", command], null);
    }

    internal Action? BeforeRootDeletion { get; set; }
    private bool _disposed;
    private Process? _active;
    private bool _failed;
    public void Dispose()
    {
        if (_disposed) return;
        BeforeRootDeletion?.Invoke();
        GitFixtureCleanup.Delete(Root);
        _disposed = true;
    }

    internal void FailActiveInvocation()
    {
        _failed = true;
        if (_active is { HasExited: false }) _active.Kill(entireProcessTree: true);
    }

    internal async Task<JsonElement> ProbeIdleAsync()
    {
        var script = Path.Combine(External, "idle.ps1");
        await File.WriteAllTextAsync(script, "[ordered]@{cwd=[Environment]::CurrentDirectory;probe=$env:C835_PROBE;empty=$env:C835_EMPTY;absent=[Environment]::GetEnvironmentVariables().Contains('C835_ABSENT')} | ConvertTo-Json -Compress");
        var result = await InvokeAsync(script);
        using var json = JsonDocument.Parse(result.Stdout);
        return json.RootElement.Clone();
    }

    private static (int Exit, string Output) Run(string file, string cwd, IReadOnlyList<string> args) =>
        RunAsync(file, cwd, args, null).GetAwaiter().GetResult();

    internal static async Task<(int Exit, string Output)> RunAsync(string file, string cwd,
        IReadOnlyList<string> args, IReadOnlyDictionary<string, string?>? environment)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo(file) { WorkingDirectory = cwd,
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
        process.StartInfo.Environment["GIT_AUTHOR_DATE"] = "2026-10-01T00:00:00Z";
        process.StartInfo.Environment["GIT_COMMITTER_DATE"] = "2026-10-01T00:00:00Z";
        if (environment is not null)
            foreach (var (key, value) in environment) process.StartInfo.Environment[key] = value;
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try { await process.WaitForExitAsync(cancel.Token); }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } await Task.WhenAll(stdout, stderr); }
        return (process.ExitCode, await stdout + await stderr);
    }
}


internal sealed record Result(int Exit, string Output, string Line, string Evidence, JsonElement Source);

internal sealed partial class CheckpointSourceScriptFixture
{
    internal sealed record DriverResult(int Exit, string Stdout, string Stderr, int ProcessId, long StartTicks,
        Guid RunspaceId, bool Terminated)
    {
        public string Output => Stdout + Stderr;
    }

    internal async Task<DriverResult> InvokeAsync(string script, IReadOnlyList<string>? arguments = null,
        IReadOnlyDictionary<string, string?>? environment = null, CancellationToken cancellationToken = default)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo("pwsh")
        {
            WorkingDirectory = Repo, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            UseShellExecute = false,
        } };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-File", script }.Concat(arguments ?? []))
            process.StartInfo.ArgumentList.Add(arg);
        process.StartInfo.Environment["GIT_AUTHOR_DATE"] = "2026-10-01T00:00:00Z";
        process.StartInfo.Environment["GIT_COMMITTER_DATE"] = "2026-10-01T00:00:00Z";
        if (environment is not null)
            foreach (var (key, value) in environment) process.StartInfo.Environment[key] = value;
        process.Start();
        _active = process;
        var pid = process.Id;
        var start = process.StartTime.ToUniversalTime().Ticks;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try { await process.WaitForExitAsync(cancel.Token); }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            await Task.WhenAll(stdout, stderr);
        }
        _active = null;
        if (_failed) throw new IOException("fixture-primary-failure");
        var result = new DriverResult(process.ExitCode, await stdout, await stderr, pid, start, Guid.Empty,
            process.ExitCode == 1 && (await stderr).Length != 0);
        Invocations.Add(result);
        return result;
    }
}
