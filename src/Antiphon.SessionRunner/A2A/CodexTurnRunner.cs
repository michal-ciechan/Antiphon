using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Antiphon.Agents.Pty;

namespace Antiphon.SessionRunner.Agent2Agent;

public sealed record CodexTurnRequest(string Prompt, string WorkingDirectory, string? ResumeThreadId);

public sealed record CodexTurnResult(string? ThreadId, string? FinalMessage, JsonElement? Usage, string? Error,
    string? Stderr = null)
{
    public bool Succeeded => Error is null;
}

public sealed record CodexTurnStartInfo(
    string Executable, IReadOnlyList<string> Arguments, string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment, string Stdin);

/// <summary>A started <c>codex exec</c> child. The seam that tests replace.</summary>
public interface ICodexTurnProcess : IDisposable
{
    TextReader StandardOutput { get; }
    /// <summary>The last <see cref="CodexTurnRunner.StderrTailLimit"/> characters of stderr, complete once the process exits.</summary>
    Task<string> StandardErrorTail { get; }
    Task WaitForExitAsync(CancellationToken cancellationToken);
    int ExitCode { get; }
    void KillTree();
}

public interface ICodexTurnLauncher
{
    ICodexTurnProcess Start(CodexTurnStartInfo info);
}

public static class CodexExecArguments
{
    /// <summary>
    /// <c>codex exec --json</c> for a new thread, <c>codex exec resume --json &lt;thread&gt;</c> to continue one. The
    /// prompt always goes on stdin (<c>-</c>), so no prompt text reaches a command line.
    /// </summary>
    public static IReadOnlyList<string> Build(RunnerA2ASettings settings, CodexTurnRequest request)
    {
        var args = new List<string> { "exec" };
        if (request.ResumeThreadId is not null) args.Add("resume");
        args.AddRange(["--json", "--skip-git-repo-check", "-c", $"sandbox_mode=\"{settings.Sandbox}\""]);
        if (!string.IsNullOrWhiteSpace(settings.Model)) args.AddRange(["-m", settings.Model]);
        if (request.ResumeThreadId is not null) args.Add(request.ResumeThreadId);
        args.Add("-");
        return args;
    }
}

/// <summary>Runs one Codex turn and reports its events as they arrive.</summary>
public sealed class CodexTurnRunner(RunnerA2ASettings settings, ICodexTurnLauncher launcher, TimeProvider clock)
{
    public const int StderrTailLimit = 4096;
    public const int CallerStderrLimit = 1000;
    private static readonly TimeSpan KillWait = TimeSpan.FromSeconds(5);
    // Stderr reaches EOF when Codex exits unless a leftover child holds it; the job kill on dispose ends that child.
    private static readonly TimeSpan StderrWait = TimeSpan.FromSeconds(2);

    public async Task<CodexTurnResult> RunAsync(
        CodexTurnRequest request, Func<CodexExecEvent, ValueTask> onEvent, CancellationToken cancellationToken)
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(settings.CodexHome)) environment["CODEX_HOME"] = settings.CodexHome;
        var info = new CodexTurnStartInfo(settings.CodexExecutable, CodexExecArguments.Build(settings, request),
            request.WorkingDirectory, environment, request.Prompt);

        ICodexTurnProcess process;
        try { process = launcher.Start(info); }
        catch (Exception e) when (e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception
                                     or UnauthorizedAccessException)
        { return new(request.ResumeThreadId, null, null, $"Codex could not start: {e.Message}"); }

        using (process)
        using (var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(settings.TurnTimeoutMinutes), clock))
        using (var turn = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token))
        // A synchronous pipe read on Windows does not observe the token; killing the child closes stdout and ends it.
        using (turn.Token.Register(process.KillTree))
        {
            var threadId = request.ResumeThreadId;
            string? lastMessage = null, failure = null, streamError = null;
            JsonElement? usage = null;
            var completed = false;
            try
            {
                // Stop at the turn's own end: a command Codex left running may hold stdout open after Codex exits.
                while (!completed && failure is null && await process.StandardOutput.ReadLineAsync(turn.Token) is { } line)
                {
                    if (CodexExecEventParser.Parse(line) is not { } parsed) continue;
                    switch (parsed)
                    {
                        case CodexExecEvent.ThreadStarted started: threadId = started.ThreadId; break;
                        case CodexExecEvent.ItemCompleted { ItemType: "agent_message" } message: lastMessage = message.Text; break;
                        case CodexExecEvent.TurnCompleted done: completed = true; usage = done.Usage; break;
                        case CodexExecEvent.TurnFailed failed: failure = failed.Message; break;
                        case CodexExecEvent.StreamError error: streamError = error.Message; break;
                    }
                    await onEvent(parsed);
                }
                await process.WaitForExitAsync(turn.Token);
            }
            catch (OperationCanceledException) when (turn.IsCancellationRequested) { }
            // The kill on cancel can end the read and the process before any await observes the token, so ask the
            // token rather than rely on an OperationCanceledException.
            if (turn.IsCancellationRequested)
            {
                process.KillTree();
                await WaitAfterKillAsync(process);
                cancellationToken.ThrowIfCancellationRequested();
                return new(threadId, lastMessage, usage, $"Codex turn timed out after {settings.TurnTimeoutMinutes} minute(s).");
            }

            if (failure is not null) return new(threadId, lastMessage, usage, failure);
            if (completed && process.ExitCode == 0) return new(threadId, lastMessage, usage, null);
            var stderr = await StderrAsync(process);
            var reason = streamError ?? (completed
                ? $"Codex exited {process.ExitCode} after the turn completed."
                : $"Codex exited {process.ExitCode} without completing the turn.");
            // The caller gets a short tail; the runner log keeps the full one.
            var shown = stderr.Length > CallerStderrLimit ? "..." + stderr[^CallerStderrLimit..] : stderr;
            return new(threadId, lastMessage, usage, shown.Length == 0 ? reason : $"{reason}\n{shown}", stderr);
        }
    }

    private async Task<string> StderrAsync(ICodexTurnProcess process)
    {
        try { return (await process.StandardErrorTail.WaitAsync(StderrWait, clock)).Trim(); }
        catch (TimeoutException) { return ""; }
    }

    private async Task WaitAfterKillAsync(ICodexTurnProcess process)
    {
        try { await process.WaitForExitAsync(CancellationToken.None).WaitAsync(KillWait, clock); }
        catch (TimeoutException) { }
    }
}

/// <summary>Starts the real Codex CLI. The executable must be native (codex.exe on Windows), not the npm .cmd shim.</summary>
public sealed class ProcessCodexTurnLauncher : ICodexTurnLauncher
{
    public ICodexTurnProcess Start(CodexTurnStartInfo info)
    {
        var executable = Resolve(info.Executable, info.WorkingDirectory);
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = info.WorkingDirectory,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in info.Arguments) start.ArgumentList.Add(argument);
        foreach (var (key, value) in info.Environment) start.Environment[key] = value;
        var process = Process.Start(start) ?? throw new InvalidOperationException("Process.Start returned no process.");
        KillOnCloseJob job;
        try { job = KillOnCloseJob.Assign(process.Id); }
        catch (System.ComponentModel.Win32Exception)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            process.Dispose();
            throw;
        }
        return new Child(process, job, info.Stdin);
    }

    internal static string Resolve(string executable, string cwd)
    {
        string? resolved;
        if (OperatingSystem.IsWindows())
        {
            var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["PATH"] = Environment.GetEnvironmentVariable("PATH") ?? "",
                ["PATHEXT"] = Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE",
            };
            var candidate = WindowsCommandLine.ResolveExecutable(executable, cwd, env);
            resolved = File.Exists(candidate) ? Path.GetFullPath(candidate) : null;
        }
        else
        {
            resolved = Path.IsPathRooted(executable) ? (File.Exists(executable) ? executable : null)
                : (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
                    .Where(dir => dir.Length != 0).Select(dir => Path.Combine(dir, executable)).FirstOrDefault(File.Exists);
        }
        if (resolved is null)
            throw new InvalidOperationException($"'{executable}' was not found on PATH. Set {RunnerA2ASettings.SectionName}:CodexExecutable.");
        if (OperatingSystem.IsWindows() && !Path.GetExtension(resolved).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"'{resolved}' is not a native executable. Point {RunnerA2ASettings.SectionName}:CodexExecutable at codex.exe.");
        return resolved;
    }

    private sealed class Child : ICodexTurnProcess
    {
        private readonly Process _process;
        private readonly KillOnCloseJob _job;

        public Child(Process process, KillOnCloseJob job, string stdin)
        {
            _process = process;
            _job = job;
            StandardErrorTail = ReadTailAsync(process.StandardError);
            // Codex reads the whole prompt before it starts; write it, then close stdin.
            _ = WriteStdinAsync(stdin);
        }

        public TextReader StandardOutput => _process.StandardOutput;
        public Task<string> StandardErrorTail { get; }
        public int ExitCode => _process.ExitCode;
        public Task WaitForExitAsync(CancellationToken cancellationToken) => _process.WaitForExitAsync(cancellationToken);

        public void KillTree()
        {
            _job.Terminate();
            try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
        }

        /// <summary>Ends the turn's whole job, including commands Codex left running after it exited.</summary>
        public void Dispose()
        {
            KillTree();
            _job.Dispose();
            _process.Dispose();
        }

        private async Task WriteStdinAsync(string stdin)
        {
            try
            {
                await _process.StandardInput.WriteAsync(stdin);
                await _process.StandardInput.FlushAsync();
                _process.StandardInput.Close();
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException) { }
        }

        private static async Task<string> ReadTailAsync(StreamReader reader)
        {
            var tail = new StringBuilder();
            var buffer = new char[1024];
            int read;
            try
            {
                while ((read = await reader.ReadAsync(buffer)) != 0)
                {
                    tail.Append(buffer, 0, read);
                    if (tail.Length > CodexTurnRunner.StderrTailLimit) tail.Remove(0, tail.Length - CodexTurnRunner.StderrTailLimit);
                }
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException) { }
            return tail.ToString();
        }
    }
}
