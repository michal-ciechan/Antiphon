using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Agents.Tui;

public sealed partial class RunnerProcessProbe : IRunnerProcessProbe
{
    private const int MaximumArguments = 256;
    private const int MaximumArgumentLength = 2000;
    private const int MaximumEnvironmentEntries = 256;
    private const int MaximumEnvironmentValueLength = 4000;
    private const int CleanupGraceMilliseconds = 200;
    private const int GracefulSignalMilliseconds = 75;
    private readonly TimeSpan _timeout;
    private readonly int _maxOutputBytes;

    public RunnerProcessProbe(IOptions<AgentTuiSettings> settings)
    {
        var timeoutSeconds = settings.Value.ProbeTimeoutSeconds;
        var maxOutputBytes = settings.Value.MaxProbeOutputBytes;
        if (timeoutSeconds is <= 0 or > AgentTuiSettings.MaximumProbeTimeoutSeconds)
        {
            throw new OptionsValidationException(
                nameof(AgentTuiSettings),
                typeof(AgentTuiSettings),
                [$"ProbeTimeoutSeconds must be between 1 and {AgentTuiSettings.MaximumProbeTimeoutSeconds}."]);
        }
        if (maxOutputBytes is <= 0 or > AgentTuiSettings.MaximumProbeOutputBytes)
        {
            throw new OptionsValidationException(
                nameof(AgentTuiSettings),
                typeof(AgentTuiSettings),
                [$"MaxProbeOutputBytes must be between 1 and {AgentTuiSettings.MaximumProbeOutputBytes}."]);
        }

        _timeout = TimeSpan.FromSeconds(timeoutSeconds);
        _maxOutputBytes = maxOutputBytes;
    }

    public RunnerPathCheck CheckExecutable(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable) || executable.Length > MaximumArgumentLength)
            return Unavailable("The executable is invalid or unavailable.");

        try
        {
            if (Path.IsPathRooted(executable) || ContainsDirectorySeparator(executable))
                return File.Exists(executable)
                    ? Available("The executable is available.")
                    : Unavailable("The executable is unavailable.");

            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            var extensions = OperatingSystem.IsWindows()
                ? ExecutableExtensions(executable)
                : [string.Empty];
            foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (var extension in extensions)
                {
                    if (File.Exists(Path.Combine(directory, executable + extension)))
                        return Available("The executable is available.");
                }
            }
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or IOException
                                          or UnauthorizedAccessException)
        {
            return Unavailable("The executable could not be inspected safely.");
        }

        return Unavailable("The executable is unavailable.");
    }

    public RunnerPathCheck CheckFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > MaximumArgumentLength)
            return Unavailable("The required file is invalid or unavailable.");
        try
        {
            return File.Exists(path)
                ? Available("The required file is available.")
                : Unavailable("The required file is unavailable.");
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or IOException
                                          or UnauthorizedAccessException)
        {
            return Unavailable("The required file could not be inspected safely.");
        }
    }

    public RunnerPathCheck CheckDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 1000)
            return Unavailable("The working directory is invalid or unavailable.");
        try
        {
            return Directory.Exists(path)
                ? Available("The working directory is available.")
                : Unavailable("The working directory is unavailable.");
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or IOException
                                          or UnauthorizedAccessException)
        {
            return Unavailable("The working directory could not be inspected safely.");
        }
    }

    public async Task<RunnerProcessResult> RunAsync(
        RunnerProcessRequest request,
        CancellationToken cancellationToken)
    {
        var requestFailure = ValidateRequest(request);
        if (requestFailure is not null)
            return Failure(requestFailure);
        if (cancellationToken.IsCancellationRequested)
            return CancelledBeforeStart();

        var startInfo = new ProcessStartInfo
        {
            FileName = request.Executable,
            WorkingDirectory = request.WorkingDirectory ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in request.Arguments)
            startInfo.ArgumentList.Add(argument);

        var childEnvironment = startInfo.Environment
            .ToDictionary(entry => entry.Key, entry => entry.Value ?? string.Empty, StringComparer.Ordinal);
        foreach (var entry in request.Environment)
            childEnvironment[entry.Key] = entry.Value;
        if (childEnvironment.Count > MaximumEnvironmentEntries
            || childEnvironment.Any(entry => entry.Key.Length > 200
                                             || entry.Value.Length > MaximumEnvironmentValueLength))
        {
            return Failure("The bounded child environment is unavailable.");
        }
        startInfo.Environment.Clear();
        foreach (var entry in childEnvironment)
            startInfo.Environment[entry.Key] = entry.Value;

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
                return Failure("The probe process could not be started.");
        }
        catch (Exception exception) when (exception is Win32Exception
                                          or InvalidOperationException
                                          or IOException
                                          or UnauthorizedAccessException)
        {
            return Failure("The probe process could not be started.");
        }

        var capture = new CombinedOutputCapture(_maxOutputBytes);
        var stdoutDrain = DrainAsync(process.StandardOutput.BaseStream, capture, isStandardError: false);
        var stderrDrain = DrainAsync(process.StandardError.BaseStream, capture, isStandardError: true);
        var timedOut = false;
        var cancelled = false;
        var cleanlyStopped = true;

        using var timeout = new CancellationTokenSource(_timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            timeout.Token,
            cancellationToken);
        try
        {
            var waitForExit = process.WaitForExitAsync(CancellationToken.None);
            if (request.StopAfter is { } stopAfter)
            {
                var boundedStopAfter = stopAfter <= TimeSpan.Zero || stopAfter > _timeout
                    ? _timeout
                    : stopAfter;
                var stopDelay = Task.Delay(boundedStopAfter, linked.Token);
                var completed = await Task.WhenAny(waitForExit, stopDelay);
                if (completed == stopDelay && !linked.IsCancellationRequested)
                {
                    cleanlyStopped = await TryCleanStopAsync(process, waitForExit);
                }
                else
                {
                    await waitForExit.WaitAsync(linked.Token);
                }
            }
            else
            {
                await waitForExit.WaitAsync(linked.Token);
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = cancellationToken.IsCancellationRequested;
            timedOut = !cancelled && timeout.IsCancellationRequested;
            await StopTreeAsync(process);
            cleanlyStopped = false;
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                          or Win32Exception
                                          or IOException)
        {
            await StopTreeAsync(process);
            cleanlyStopped = false;
        }

        await DrainSafelyAsync(stdoutDrain, stderrDrain);
        var rawOutput = capture.GetOutput();
        var sanitized = Sanitize(rawOutput.StandardOutput, rawOutput.StandardError, request.SecretValues);
        var exitCode = TryGetExitCode(process, timedOut || cancelled);
        return new RunnerProcessResult(
            exitCode,
            sanitized.StandardOutput,
            sanitized.StandardError,
            timedOut,
            capture.WasTruncated,
            cancelled,
            Started: true,
            cleanlyStopped,
            sanitized.SensitiveOutputDetected,
            timedOut
                ? "The probe timed out."
                : cancelled
                    ? "The probe was cancelled."
                    : cleanlyStopped
                        ? null
                        : "The probe process required forced cleanup.");
    }

    private static async Task DrainAsync(
        Stream stream,
        CombinedOutputCapture capture,
        bool isStandardError)
    {
        var buffer = new byte[4096];
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer);
                if (read == 0)
                    return;
                capture.Append(buffer.AsSpan(0, read), isStandardError);
            }
        }
        catch (Exception exception) when (exception is IOException
                                          or ObjectDisposedException
                                          or InvalidOperationException)
        {
            // Process termination can close redirected streams while a drain is pending.
        }
    }

    private static async Task DrainSafelyAsync(Task stdout, Task stderr)
    {
        try
        {
            await Task.WhenAll(stdout, stderr)
                .WaitAsync(TimeSpan.FromMilliseconds(CleanupGraceMilliseconds));
        }
        catch (Exception exception) when (exception is TimeoutException
                                          or IOException
                                          or ObjectDisposedException)
        {
            // Captured output remains bounded and usable even when a stream closes abnormally.
        }
    }

    private static async Task<bool> TryCleanStopAsync(Process process, Task waitForExit)
    {
        try
        {
            if (process.HasExited)
                return true;
            process.StandardInput.Close();
            try
            {
                await waitForExit.WaitAsync(TimeSpan.FromMilliseconds(GracefulSignalMilliseconds));
                return true;
            }
            catch (TimeoutException)
            {
                // Fall through to the window-close signal before forced cleanup.
            }
            if (process.CloseMainWindow())
            {
                try
                {
                    await waitForExit.WaitAsync(TimeSpan.FromMilliseconds(GracefulSignalMilliseconds));
                    return true;
                }
                catch (TimeoutException)
                {
                    // Fall through to bounded forced cleanup.
                }
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                          or Win32Exception
                                          or IOException)
        {
            // Fall through to bounded forced cleanup.
        }

        await StopTreeAsync(process);
        return false;
    }

    private static async Task<bool> StopTreeAsync(Process process)
    {
        KillTree(process);
        return await AwaitExitAsync(process);
    }

    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            // The process may have exited between the state check and kill request.
        }
    }

    private static async Task<bool> AwaitExitAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromMilliseconds(CleanupGraceMilliseconds));
            return process.HasExited;
        }
        catch (Exception exception) when (exception is TimeoutException
                                          or InvalidOperationException
                                          or Win32Exception)
        {
            KillTree(process);
            try
            {
                return process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    private static int? TryGetExitCode(Process process, bool forced)
    {
        if (forced)
            return null;
        try
        {
            return process.HasExited ? process.ExitCode : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string? ValidateRequest(RunnerProcessRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Executable)
            || request.Executable.Length > MaximumArgumentLength)
        {
            return "The probe executable is invalid.";
        }
        if (request.Arguments.Count > MaximumArguments
            || request.Arguments.Any(argument => argument is null || argument.Length > MaximumArgumentLength))
        {
            return "The probe arguments are invalid or too large.";
        }
        if (request.WorkingDirectory?.Length > 1000)
            return "The probe working directory is invalid.";
        if (request.Environment.Count > MaximumEnvironmentEntries
            || request.Environment.Any(entry =>
                string.IsNullOrWhiteSpace(entry.Key)
                || entry.Key.Length > 200
                || entry.Value is null
                || entry.Value.Length > MaximumEnvironmentValueLength))
        {
            return "The probe environment is invalid or too large.";
        }
        if (request.SecretValues.Any(value => value is null || value.Length > MaximumEnvironmentValueLength))
            return "The probe redaction values are invalid or too large.";
        return null;
    }

    private static SanitizedOutput Sanitize(
        string standardOutput,
        string standardError,
        IReadOnlyList<string> secretValues)
    {
        var sensitive = false;
        foreach (var secret in secretValues.Where(value => !string.IsNullOrEmpty(value)).Distinct())
        {
            if (standardOutput.Contains(secret, StringComparison.Ordinal)
                || standardError.Contains(secret, StringComparison.Ordinal))
            {
                sensitive = true;
            }
            standardOutput = standardOutput.Replace(secret, "*", StringComparison.Ordinal);
            standardError = standardError.Replace(secret, "*", StringComparison.Ordinal);
        }

        standardOutput = RedactCredentialShapes(standardOutput, ref sensitive);
        standardError = RedactCredentialShapes(standardError, ref sensitive);
        return new SanitizedOutput(standardOutput, standardError, sensitive);
    }

    private static string RedactCredentialShapes(string value, ref bool sensitive)
    {
        var redacted = CredentialAssignmentRegex().Replace(value, "*");
        redacted = BearerCredentialRegex().Replace(redacted, "*");
        if (!string.Equals(redacted, value, StringComparison.Ordinal))
            sensitive = true;
        return redacted;
    }

    private static bool ContainsDirectorySeparator(string executable) =>
        executable.IndexOf(Path.DirectorySeparatorChar) >= 0
        || executable.IndexOf(Path.AltDirectorySeparatorChar) >= 0;

    private static string[] ExecutableExtensions(string executable)
    {
        if (Path.HasExtension(executable))
            return [string.Empty];
        var pathExtensions = Environment.GetEnvironmentVariable("PATHEXT")
            ?.Split(';', StringSplitOptions.RemoveEmptyEntries)
            ?? [".COM", ".EXE", ".BAT", ".CMD"];
        return [string.Empty, .. pathExtensions];
    }

    private static RunnerPathCheck Available(string message) => new(true, message);
    private static RunnerPathCheck Unavailable(string message) => new(false, message);

    private static RunnerProcessResult Failure(string message) =>
        new(null, string.Empty, string.Empty, false, Started: false, CleanlyStopped: false, Error: message);

    private static RunnerProcessResult CancelledBeforeStart() =>
        new(
            null,
            string.Empty,
            string.Empty,
            false,
            Cancelled: true,
            Started: false,
            CleanlyStopped: true,
            Error: "The probe was cancelled.");

    [GeneratedRegex(
        @"(?im)(?<![A-Za-z0-9_])[""']?(?:[A-Za-z][A-Za-z0-9]*[_-])*(?:api[_-]?(?:key|token)|access[_-]?token|refresh[_-]?token|token|password|secret|authorization)[""']?\s*[:=]\s*[^\r\n]+",
        RegexOptions.CultureInvariant)]
    private static partial Regex CredentialAssignmentRegex();

    [GeneratedRegex(@"(?im)\bbearer\s+[^\s\r\n]+", RegexOptions.CultureInvariant)]
    private static partial Regex BearerCredentialRegex();

    private sealed class CombinedOutputCapture
    {
        private readonly int _maximumBytes;
        private readonly MemoryStream _standardOutput = new();
        private readonly MemoryStream _standardError = new();
        private readonly object _sync = new();
        private int _capturedBytes;

        public CombinedOutputCapture(int maximumBytes)
        {
            _maximumBytes = maximumBytes;
        }

        public bool WasTruncated { get; private set; }

        public void Append(ReadOnlySpan<byte> bytes, bool isStandardError)
        {
            lock (_sync)
            {
                var available = _maximumBytes - _capturedBytes;
                var accepted = Math.Min(Math.Max(available, 0), bytes.Length);
                if (accepted < bytes.Length)
                    WasTruncated = true;
                if (accepted == 0)
                    return;

                var target = isStandardError ? _standardError : _standardOutput;
                target.Write(bytes[..accepted]);
                _capturedBytes += accepted;
            }
        }

        public CapturedOutput GetOutput()
        {
            lock (_sync)
            {
                return new CapturedOutput(
                    Encoding.UTF8.GetString(_standardOutput.ToArray()),
                    Encoding.UTF8.GetString(_standardError.ToArray()));
            }
        }
    }

    private sealed record CapturedOutput(string StandardOutput, string StandardError);
    private sealed record SanitizedOutput(
        string StandardOutput,
        string StandardError,
        bool SensitiveOutputDetected);
}
