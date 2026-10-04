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
        if (_activeCompleted is { Task.IsCompleted: false } || _active is { HasExited: false })
            throw new InvalidOperationException("script case still owns a process");
        RestoreSeedForParity();
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
    internal Action? DisposalStarted { get; set; }
    internal Func<Task>? BeforeInvocationCompletion { get; set; }
    private readonly List<Task<DriverResult>> _owned = [];
    private bool _disposed;
    private Process? _active;
    private bool _failed;
    public void Dispose()
    {
        if (_disposed) return;
        DisposalStarted?.Invoke();
        if (_activeCompleted is { Task.IsCompleted: false }) StopWorkerAsync(force: true).GetAwaiter().GetResult();
        if (_active is { HasExited: false }) _active.Kill(entireProcessTree: true);
        Task<DriverResult>[] owned;
        lock (_owned) owned = _owned.ToArray();
        try { Task.WhenAll(owned).GetAwaiter().GetResult(); } catch (Exception) { /* primary failure remains on each owned invocation */ }
        StopWorkerAsync(force: false).GetAwaiter().GetResult();
        if (_worker is not null) _worker.StandardOutput.ReadToEnd();
        BeforeRootDeletion?.Invoke();
        GitFixtureCleanup.Delete(Root);
        _disposed = true;
    }

    internal void FailActiveInvocation()
    {
        _failed = true;
        if (_worker is { HasExited: false }) _worker.Kill(entireProcessTree: true);
        if (_active is { HasExited: false }) _active.Kill(entireProcessTree: true);
    }

    internal async Task<JsonElement> ProbeIdleAsync()
    {
        if (SessionMode)
        {
            await _serial.WaitAsync();
            try
            {
                StartWorker();
                await _worker!.StandardInput.WriteLineAsync("{\"kind\":\"idle\"}");
                await _worker.StandardInput.FlushAsync();
                var response = await _worker.StandardOutput.ReadLineAsync();
                using var idle = JsonDocument.Parse(response ?? throw new IOException("idle worker response missing"));
                return idle.RootElement.Clone();
            }
            finally { _serial.Release(); }
        }
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

    internal Task<DriverResult> InvokeAsync(string script, IReadOnlyList<string>? arguments = null,
        IReadOnlyDictionary<string, string?>? environment = null, CancellationToken cancellationToken = default)
    {
        var invocation = SessionMode ? InvokeSessionAsync(script, arguments, environment, cancellationToken)
            : InvokeProcessAsync(script, arguments, environment, cancellationToken);
        lock (_owned) _owned.Add(invocation);
        return invocation;
    }

    private async Task<DriverResult> InvokeProcessAsync(string script, IReadOnlyList<string>? arguments = null,
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
    private Process? _worker;
    private Task<string>? _workerStderr;
    private readonly SemaphoreSlim _serial = new(1, 1);
    private TaskCompletionSource? _activeCompleted;

    private void StartWorker()
    {
        if (_worker is not null) return;
        var asset = Path.Combine(External, "worker.ps1");
        File.WriteAllText(asset, WorkerScript);
        _worker = new Process { StartInfo = new ProcessStartInfo("pwsh")
        {
            WorkingDirectory = Repo, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false,
        } };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", asset })
            _worker.StartInfo.ArgumentList.Add(argument);
        _worker.StartInfo.Environment["GIT_AUTHOR_DATE"] = "2026-10-01T00:00:00Z";
        _worker.StartInfo.Environment["GIT_COMMITTER_DATE"] = "2026-10-01T00:00:00Z";
        _worker.Start();
        _workerStderr = _worker.StandardError.ReadToEndAsync();
    }

    private async Task<DriverResult> InvokeSessionAsync(string script, IReadOnlyList<string>? arguments,
        IReadOnlyDictionary<string, string?>? environment, CancellationToken cancellationToken)
    {
        await _serial.WaitAsync(cancellationToken);
        _activeCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            if (_disposed) throw new ObjectDisposedException(nameof(CheckpointSourceScriptFixture));
            StartWorker();
            var parameters = new List<object>();
            var args = arguments ?? [];
            for (var index = 0; index < args.Count; index++)
            {
                var name = args[index].TrimStart('-');
                object value = true;
                if (index + 1 < args.Count && !args[index + 1].StartsWith('-')) value = args[++index];
                parameters.Add(new { name, value });
            }
            var request = JsonSerializer.Serialize(new { kind = "invoke", script, parameters, cwd = Repo,
                environment = environment ?? new Dictionary<string, string?>() });
            await _worker!.StandardInput.WriteLineAsync(request);
            await _worker.StandardInput.FlushAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
            var line = await _worker.StandardOutput.ReadLineAsync(cancel.Token);
            if (line is null) throw new IOException("fixture-primary-failure: worker exited: " + await _workerStderr!);
            using var json = JsonDocument.Parse(line);
            var response = json.RootElement;
            var result = new DriverResult(response.GetProperty("exit").GetInt32(), response.GetProperty("stdout").GetString()!,
                response.GetProperty("stderr").GetString()!, _worker.Id, _worker.StartTime.ToUniversalTime().Ticks,
                response.GetProperty("runspace").GetGuid(), response.GetProperty("terminated").GetBoolean());
            Invocations.Add(result);
            return result;
        }
        catch
        {
            await StopWorkerAsync(force: true);
            throw;
        }
        finally
        {
            if (BeforeInvocationCompletion is not null) await BeforeInvocationCompletion();
            _activeCompleted.TrySetResult();
            _serial.Release();
        }
    }

    private async Task StopWorkerAsync(bool force)
    {
        if (_worker is null) return;
        if (!_worker.HasExited)
        {
            if (force) _worker.Kill(entireProcessTree: true);
            else { await _worker.StandardInput.WriteLineAsync("{\"kind\":\"stop\"}"); await _worker.StandardInput.FlushAsync(); }
        }
        await _worker.WaitForExitAsync();
        await _workerStderr!;
    }

    private const string WorkerScript = """"
        $ErrorActionPreference = 'Stop'
        Add-Type -TypeDefinition @'
        using System;
        using System.Collections.Generic;
        using System.Collections.ObjectModel;
        using System.Globalization;
        using System.Management.Automation;
        using System.Management.Automation.Host;
        using System.Security;
        using System.Text;
        public sealed class C886Host : PSHost {
            public readonly C886UI Screen = new C886UI();
            public int ExitCode;
            public bool Exited;
            private readonly Guid id = Guid.NewGuid();
            public override Guid InstanceId => id;
            public override string Name => "C886";
            public override Version Version => new Version(1,0);
            public override CultureInfo CurrentCulture => CultureInfo.InvariantCulture;
            public override CultureInfo CurrentUICulture => CultureInfo.InvariantCulture;
            public override PSHostUserInterface UI => Screen;
            public override void SetShouldExit(int code) { ExitCode = code; Exited = true; }
            public override void EnterNestedPrompt() { throw new InvalidOperationException("nested prompt"); }
            public override void ExitNestedPrompt() { }
            public override void NotifyBeginApplication() { }
            public override void NotifyEndApplication() { }
        }
        public sealed class C886UI : PSHostUserInterface {
            public readonly StringBuilder Output = new StringBuilder();
            public readonly StringBuilder Error = new StringBuilder();
            public override PSHostRawUserInterface RawUI => null;
            public override string ReadLine() { throw new InvalidOperationException("interactive prompt"); }
            public override SecureString ReadLineAsSecureString() { throw new InvalidOperationException("interactive prompt"); }
            public override void Write(string value) { Output.Append(value); }
            public override void Write(ConsoleColor fg, ConsoleColor bg, string value) { Write(value); }
            public override void WriteLine(string value) { Output.AppendLine(value); }
            public override void WriteErrorLine(string value) { Error.AppendLine(value); }
            public override void WriteDebugLine(string value) { Output.AppendLine("DEBUG: " + value); }
            public override void WriteVerboseLine(string value) { Output.AppendLine("VERBOSE: " + value); }
            public override void WriteWarningLine(string value) { Output.AppendLine("WARNING: " + value); }
            public override void WriteProgress(long id, ProgressRecord value) { }
            public override Dictionary<string,PSObject> Prompt(string caption, string message, Collection<FieldDescription> descriptions) { throw new InvalidOperationException("interactive prompt"); }
            public override PSCredential PromptForCredential(string caption, string message, string user, string target) { throw new InvalidOperationException("interactive prompt"); }
            public override PSCredential PromptForCredential(string caption, string message, string user, string target, PSCredentialTypes types, PSCredentialUIOptions options) { throw new InvalidOperationException("interactive prompt"); }
            public override int PromptForChoice(string caption, string message, Collection<ChoiceDescription> choices, int defaultChoice) { throw new InvalidOperationException("interactive prompt"); }
        }
        '@
        $workerCwd = [Environment]::CurrentDirectory
        while ($null -ne ($line = [Console]::In.ReadLine())) {
            $request = $line | ConvertFrom-Json -AsHashtable
            if ($request.kind -eq 'stop') { break }
            if ($request.kind -eq 'idle') {
                $idle = @{cwd=[Environment]::CurrentDirectory;probe=$env:C835_PROBE;empty=$env:C835_EMPTY;absent=[Environment]::GetEnvironmentVariables().Contains('C835_ABSENT')}
                [Console]::Out.WriteLine(($idle | ConvertTo-Json -Compress))
                continue
            }
            $before = [Environment]::GetEnvironmentVariables()
            $cwd = [Environment]::CurrentDirectory
            $hostCapture = [C886Host]::new()
            $space = $null
            $pipeline = $null
            $terminated = $false
            $identity = [Guid]::Empty
            try {
                foreach ($key in $request.environment.Keys) {
                    [Environment]::SetEnvironmentVariable($key, $request.environment[$key])
                }
                [Environment]::CurrentDirectory = $request.cwd
                $space = [runspacefactory]::CreateRunspace($hostCapture)
                $space.Open()
                $identity = $space.InstanceId
                $space.SessionStateProxy.Path.SetLocation($request.cwd) | Out-Null
                $pipeline = [powershell]::Create()
                $pipeline.Runspace = $space
                $null = $pipeline.AddCommand($request.script)
                foreach ($parameter in $request.parameters) {
                    $null = $pipeline.AddParameter([string]$parameter.name, $parameter.value)
                }
                try {
                    $values = $pipeline.Invoke()
                    foreach ($value in $values) { $hostCapture.Screen.Output.AppendLine([string]$value) | Out-Null }
                    if ($pipeline.InvocationStateInfo.State -eq 'Failed') { $terminated = $true }
                } catch {
                    $terminated = $true
                    $hostCapture.Screen.Error.AppendLine([string]$_) | Out-Null
                }
                foreach ($errorRecord in $pipeline.Streams.Error) {
                    $hostCapture.Screen.Error.AppendLine([string]$errorRecord) | Out-Null
                }
            } catch {
                $terminated = $true
                $hostCapture.Screen.Error.AppendLine([string]$_) | Out-Null
            } finally {
                if ($null -ne $pipeline) { $pipeline.Dispose() }
                if ($null -ne $space) { $space.Dispose() }
                foreach ($key in @([Environment]::GetEnvironmentVariables().Keys)) {
                    if (-not $before.Contains($key)) { [Environment]::SetEnvironmentVariable($key, $null) }
                }
                foreach ($key in $before.Keys) { [Environment]::SetEnvironmentVariable($key, [string]$before[$key]) }
                [Environment]::CurrentDirectory = $cwd
            }
            $exitCode = if ($terminated) { 1 } elseif ($hostCapture.Exited) { $hostCapture.ExitCode } else { 0 }
            $response = @{exit=$exitCode;stdout=$hostCapture.Screen.Output.ToString();stderr=$hostCapture.Screen.Error.ToString();runspace=$identity;terminated=$terminated}
            [Console]::Out.WriteLine(($response | ConvertTo-Json -Compress -Depth 8))
        }
        """";

}
