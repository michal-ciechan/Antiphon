using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Launches one owned crash worker with <c>dotnet</c> and the test assembly's own deps file.
/// Connection text stays in the child environment. Ready observation is an immediate read after
/// subscribe, then file and exit events, with no sleep poll.
/// </summary>
internal static class CrashWorkerProcess
{
    internal const string StdoutSentinel = "c889-ready-diagnostic";
    internal const string StderrSentinel = "c889-stderr-diagnostic";
    internal const string ConnectionSentinel = "c889-connection-sentinel";
    internal const string SentinelVariable = "ANTIPHON_C889_CONNECTION_SENTINEL";

    internal sealed record Spec(
        string Marker,
        string RequestJson,
        string ConnectionVariable,
        string ConnectionString,
        string TreenodeFilter,
        string ReadyPath,
        string RequestedCut);

    internal sealed record Prepared(ProcessStartInfo Start, Spec Spec);

    internal sealed class Running : IAsyncDisposable
    {
        public required Process Process { get; init; }
        public required Task<string> Stdout { get; init; }
        public required Task<string> Stderr { get; init; }

        public async Task DrainAsync()
        {
            if (!Process.HasExited)
                Process.Kill(entireProcessTree: true);
            await Process.WaitForExitAsync();
            await Task.WhenAll(Stdout, Stderr);
        }

        public async ValueTask DisposeAsync() => await DrainAsync();
    }

    internal readonly record struct ReadyView(
        string Outcome, string? Cut, int? Pid, int? ExitCode, string? Error);

    private static Stream? _standardOutput;

    /// <summary>
    /// The testing host replaces <see cref="Console.Out"/> before the assembly hook. Write the
    /// diagnostic to the process stdout handle and keep that stream alive so a later collection
    /// does not close the handle. The parent's drain then observes the sentinel.
    /// </summary>
    internal static void WriteStdoutSentinel()
    {
        var bytes = Encoding.UTF8.GetBytes(StdoutSentinel + Environment.NewLine);
        _standardOutput ??= Console.OpenStandardOutput();
        _standardOutput.Write(bytes, 0, bytes.Length);
        _standardOutput.Flush();
    }

    internal static Prepared Prepare(Spec spec)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Environment.CurrentDirectory,
        };
        start.ArgumentList.Add(typeof(CrashWorkerProcess).Assembly.Location);
        start.ArgumentList.Add("--treenode-filter");
        start.ArgumentList.Add(spec.TreenodeFilter);
        // Seed every owned marker into this copied environment, then clear them, so a launch
        // that stops clearing fails the single-marker check without touching the parent environment.
        foreach (var mode in TestWorkerModes.All)
            start.Environment[mode.Marker] = "c889-inherited-probe";
        foreach (var mode in TestWorkerModes.All)
            start.Environment.Remove(mode.Marker);
        start.Environment[spec.Marker] = spec.RequestJson;
        start.Environment[spec.ConnectionVariable] = spec.ConnectionString;
        start.Environment[SentinelVariable] = ConnectionSentinel;
        return new Prepared(start, spec);
    }

    internal static Running Start(Prepared prepared)
    {
        var process = Process.Start(prepared.Start)
            ?? throw new InvalidOperationException("crash worker did not start");
        return new Running
        {
            Process = process,
            Stdout = process.StandardOutput.ReadToEndAsync(),
            Stderr = process.StandardError.ReadToEndAsync(),
        };
    }

    internal static ReadyView Decide(
        bool exited, int? exitCode, string? readyText, string requestedCut, int capturedPid, bool cycleEnded)
    {
        if (TryReadReady(readyText, out var cut, out var pid))
        {
            var mismatch = string.Equals(cut, requestedCut, StringComparison.Ordinal) && pid == capturedPid
                ? null
                : "ready identity mismatch";
            return new ReadyView("accepted", cut, pid, exitCode, mismatch);
        }

        if (exited)
            return new ReadyView("early-exit", null, null, exitCode, "child exited before ready");

        if (cycleEnded)
            return new ReadyView("not-ready", null, null, exitCode, "ready not observed");

        return new ReadyView("pending", null, null, null, null);
    }

    internal static async Task<ReadyView> ObserveAsync(
        Process process, string readyPath, string requestedCut, CancellationToken cancellationToken,
        Task? cycleEnded = null)
    {
        var decision = new TaskCompletionSource<ReadyView>(TaskCreationOptions.RunContinuationsAsynchronously);
        string? captured = null;

        void Evaluate()
        {
            if (decision.Task.IsCompleted) return;
            var exited = SafeExited(process);
            int? exitCode = exited ? SafeExitCode(process) : null;
            var cycle = cycleEnded?.IsCompleted == true;
            var view = Decide(exited, exitCode, captured, requestedCut, process.Id, cycle);
            if (view.Outcome is "accepted" or "early-exit")
                decision.TrySetResult(view);
            else if (cycle && view.Outcome == "not-ready")
                decision.TrySetResult(view);
        }

        void ReadNow()
        {
            captured = TryReadFile(readyPath);
            Evaluate();
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(readyPath))
            ?? throw new InvalidOperationException("ready path has no directory");
        using var watcher = new FileSystemWatcher(directory, Path.GetFileName(readyPath))
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        FileSystemEventHandler onFile = (_, _) => ReadNow();
        watcher.Created += onFile;
        watcher.Changed += onFile;
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => ReadNow();
        ReadNow();

        if (decision.Task.IsCompleted)
            return await decision.Task;

        if (cycleEnded is { IsCompleted: true })
        {
            Evaluate();
            return await decision.Task;
        }

        if (cycleEnded is null)
            return await decision.Task.WaitAsync(cancellationToken);

        var finished = await Task.WhenAny(decision.Task, cycleEnded).WaitAsync(cancellationToken);
        if (finished != decision.Task)
            Evaluate();
        return await decision.Task.WaitAsync(cancellationToken);
    }

    internal static async Task JoinOwnedAsync(IReadOnlyList<Process> owned, Process? witness)
    {
        if (witness is not null && !SafeExited(witness))
        {
            try { witness.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            await witness.WaitForExitAsync();
        }

        foreach (var process in owned)
        {
            if (!SafeExited(process))
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            }

            await process.WaitForExitAsync();
        }
    }

    private static bool TryReadReady(string? readyText, out string cut, out int pid)
    {
        cut = "";
        pid = 0;
        if (string.IsNullOrWhiteSpace(readyText)) return false;
        try
        {
            using var doc = JsonDocument.Parse(readyText);
            if (!doc.RootElement.TryGetProperty("cut", out var cutValue)
                || !doc.RootElement.TryGetProperty("worker", out var pidValue)
                || cutValue.ValueKind != JsonValueKind.String
                || pidValue.ValueKind != JsonValueKind.Number)
                return false;
            cut = cutValue.GetString() ?? "";
            pid = pidValue.GetInt32();
            return cut.Length > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? TryReadFile(string path)
    {
        if (!File.Exists(path)) return null;
        try { return File.ReadAllText(path); }
        catch (IOException) { return null; }
    }

    private static bool SafeExited(Process process)
    {
        try { return process.HasExited; }
        catch (InvalidOperationException) { return true; }
    }

    private static int? SafeExitCode(Process process)
    {
        try { return process.ExitCode; }
        catch (InvalidOperationException) { return null; }
    }
}
