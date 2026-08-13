using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Options;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.Server.Infrastructure.Agents.Tui;

public sealed partial class RunnerProcessProbe : IRunnerProcessProbe
{
    private const int MaximumArguments = 256;
    private const int MaximumArgumentLength = 2000;
    private const int MaximumEnvironmentEntries = 256;
    private const int MaximumEnvironmentValueLength = 4000;
    private const int CleanupGraceMilliseconds = 200;
    private const int GracefulSignalMilliseconds = 500;
    private const uint WindowsFileReadData = 0x0001;
    private const uint WindowsFileListDirectory = 0x0001;
    private const uint WindowsFileExecute = 0x0020;
    private const uint WindowsFileTraverse = 0x0020;
    private const uint WindowsFileFlagBackupSemantics = 0x02000000;
    private const int UnixReadAccess = 4;
    private const int UnixExecuteAccess = 1;
    private const int LinuxAtCurrentWorkingDirectory = -100;
    private const int LinuxAtEffectiveAccess = 0x0200;
    private const int MacOsAtCurrentWorkingDirectory = -2;
    private const int MacOsAtEffectiveAccess = 0x0010;
    private readonly TimeSpan _timeout;
    private readonly int _maxOutputBytes;
    private readonly RunnerProcessReaper _reaper;
    private readonly Func<Process, CancellationToken, Task<bool>> _stopTreeAsync;
    private readonly Func<string, CancellationToken, Task<RunnerPathCheck>> _checkExecutableAsync;
    private readonly Func<string, CancellationToken, Task<RunnerPathCheck>> _checkFileAsync;
    private readonly Func<string, CancellationToken, Task<RunnerPathCheck>> _checkDirectoryAsync;
    private readonly Func<RunnerProcessStartGuard, CancellationToken, Task<bool>> _startProcessAsync;
    private readonly Action? _startCommitted;

    public RunnerProcessProbe(IOptions<AgentTuiSettings> settings)
        : this(settings, new RunnerProcessReaper())
    {
    }

    public RunnerProcessProbe(
        IOptions<AgentTuiSettings> settings,
        RunnerProcessReaper reaper)
        : this(settings, reaper, seams: null)
    {
    }

    internal RunnerProcessProbe(
        IOptions<AgentTuiSettings> settings,
        RunnerProcessReaper reaper,
        RunnerProcessProbeSeams? seams)
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
        _reaper = reaper;
        _stopTreeAsync = seams?.StopTreeAsync ?? RunnerProcessCleanup.StopTreeAsync;
        _checkExecutableAsync = seams?.CheckExecutableAsync ?? DefaultCheckExecutableAsync;
        _checkFileAsync = seams?.CheckFileAsync ?? DefaultCheckFileAsync;
        _checkDirectoryAsync = seams?.CheckDirectoryAsync ?? DefaultCheckDirectoryAsync;
        _startProcessAsync = seams?.StartProcessAsync ?? DefaultStartProcessAsync;
        _startCommitted = seams?.StartCommitted;
    }

    public Task<RunnerPathCheck> CheckExecutableAsync(
        string executable,
        CancellationToken cancellationToken) =>
        RunPathCheckAsync(
            executable,
            _checkExecutableAsync,
            "The executable inspection reached its deadline.",
            cancellationToken);

    public Task<RunnerPathCheck> CheckFileAsync(
        string path,
        CancellationToken cancellationToken) =>
        RunPathCheckAsync(
            path,
            _checkFileAsync,
            "The required-file inspection reached its deadline.",
            cancellationToken);

    public Task<RunnerPathCheck> CheckDirectoryAsync(
        string path,
        CancellationToken cancellationToken) =>
        RunPathCheckAsync(
            path,
            _checkDirectoryAsync,
            "The working-directory inspection reached its deadline.",
            cancellationToken);

    private RunnerPathCheck CheckExecutableCore(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable) || executable.Length > MaximumArgumentLength)
            return Unavailable("The executable is invalid or unavailable.");

        try
        {
            if (Path.IsPathRooted(executable) || ContainsDirectorySeparator(executable))
                return IsExecutableFile(executable)
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
                    if (IsExecutableFile(Path.Combine(directory, executable + extension)))
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

    private RunnerPathCheck CheckFileCore(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > MaximumArgumentLength)
            return Unavailable("The required file is invalid or unavailable.");
        try
        {
            return IsReadableFile(path)
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

    private RunnerPathCheck CheckDirectoryCore(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 1000)
            return Unavailable("The working directory is invalid or unavailable.");
        try
        {
            return HasDirectorySearchAccess(path)
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
        using var admission = _reaper.TryAdmitProbe();
        if (admission is null)
        {
            return Failure(
                "The runner process probe is unavailable because the host is shutting down.");
        }
        using var timeout = new CancellationTokenSource(_timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            timeout.Token,
            cancellationToken);
        var requestFailure = ValidateRequest(request);
        if (requestFailure is not null)
            return Failure(requestFailure);
        if (linked.IsCancellationRequested)
        {
            var preStartCancelled = cancellationToken.IsCancellationRequested;
            return InterruptedBeforeStart(
                timedOut: !preStartCancelled,
                cancelled: preStartCancelled,
                cleanupConfirmed: true);
        }

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

        var childEnvironment = new Dictionary<string, string>(EnvironmentNameComparer());
        foreach (var name in BootstrapEnvironmentNames())
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (value is not null)
                childEnvironment[name] = value;
        }
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

        var process = new Process { StartInfo = startInfo };
        var startGuard = new RunnerProcessStartGuard(process, _startCommitted);
        var trackingId = _reaper.Register(startGuard);
        admission.Dispose();
        Task<bool>? startTask = null;
        try
        {
            try
            {
                linked.Token.ThrowIfCancellationRequested();
                startTask = _startProcessAsync(startGuard, linked.Token);
                if (!await startTask.WaitAsync(linked.Token))
                    return Failure("The probe process could not be started.");
            }
            catch (OperationCanceledException)
            {
                var startCancelled = cancellationToken.IsCancellationRequested;
                var startTimedOut = !startCancelled && timeout.IsCancellationRequested;
                if (linked.IsCancellationRequested && !startCancelled && !startTimedOut)
                    startTimedOut = true;
                return InterruptedBeforeStart(startTimedOut, startCancelled, startTask is null);
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
            var cleanupConfirmed = true;

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
                        var cleanup = await TryCleanStopAsync(
                            process,
                            waitForExit,
                            linked.Token);
                        cleanlyStopped = cleanup.CleanlyStopped;
                        cleanupConfirmed = cleanup.CleanupConfirmed;
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
                cleanupConfirmed = await StopTreeSafelyAsync(process);
                cleanlyStopped = false;
            }
            catch (Exception exception) when (exception is InvalidOperationException
                                              or Win32Exception
                                              or IOException)
            {
                cleanupConfirmed = await StopTreeSafelyAsync(process);
                cleanlyStopped = false;
            }

            await DrainSafelyAsync(stdoutDrain, stderrDrain);
            var rawOutput = capture.GetOutput();
            var outputUnusable = capture.WasTruncated || !rawOutput.IsValidUtf8;
            var sanitized = outputUnusable
                ? new SanitizedOutput(string.Empty, string.Empty, false)
                : Sanitize(rawOutput.StandardOutput, rawOutput.StandardError, request.SecretValues);
            var exitCode = TryGetExitCode(process, timedOut || cancelled || !cleanupConfirmed);
            return new RunnerProcessResult(
                exitCode,
                sanitized.StandardOutput,
                sanitized.StandardError,
                timedOut,
                outputUnusable,
                cancelled,
                Started: true,
                cleanlyStopped,
                cleanupConfirmed,
                sanitized.SensitiveOutputDetected,
                !cleanupConfirmed
                    ? "The probe process cleanup could not be confirmed; background cleanup is continuing."
                    : timedOut
                        ? "The probe timed out."
                    : cancelled
                        ? "The probe was cancelled."
                        : cleanlyStopped
                            ? null
                            : "The probe process required forced cleanup.");
        }
        finally
        {
            TransferRegisteredProcess(trackingId, startGuard, startTask);
        }
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

    private async Task<RunnerPathCheck> RunPathCheckAsync(
        string value,
        Func<string, CancellationToken, Task<RunnerPathCheck>> checkAsync,
        string deadlineMessage,
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(_timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            timeout.Token,
            cancellationToken);
        try
        {
            return await checkAsync(value, linked.Token).WaitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return Unavailable(deadlineMessage);
        }
    }

    private Task<RunnerPathCheck> DefaultCheckExecutableAsync(
        string executable,
        CancellationToken cancellationToken) =>
        Task.Run(() => CheckExecutableCore(executable), CancellationToken.None);

    private Task<RunnerPathCheck> DefaultCheckFileAsync(
        string path,
        CancellationToken cancellationToken) =>
        Task.Run(() => CheckFileCore(path), CancellationToken.None);

    private Task<RunnerPathCheck> DefaultCheckDirectoryAsync(
        string path,
        CancellationToken cancellationToken) =>
        Task.Run(() => CheckDirectoryCore(path), CancellationToken.None);

    private static Task<bool> DefaultStartProcessAsync(
        RunnerProcessStartGuard process,
        CancellationToken cancellationToken) =>
        Task.Run(process.TryStart, CancellationToken.None);

    private async Task<bool> StopTreeSafelyAsync(Process process)
    {
        try
        {
            return await _stopTreeAsync(process, CancellationToken.None);
        }
        catch (Exception)
        {
            return false;
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

    private async Task<CleanupOutcome> TryCleanStopAsync(
        Process process,
        Task waitForExit,
        CancellationToken cancellationToken)
    {
        try
        {
            if (process.HasExited)
                return new CleanupOutcome(true, true);
            process.StandardInput.Close();
            try
            {
                await waitForExit.WaitAsync(
                    TimeSpan.FromMilliseconds(GracefulSignalMilliseconds),
                    cancellationToken);
                return new CleanupOutcome(true, true);
            }
            catch (TimeoutException)
            {
                // Fall through to the window-close signal before forced cleanup.
            }
            if (process.CloseMainWindow())
            {
                try
                {
                    await waitForExit.WaitAsync(
                        TimeSpan.FromMilliseconds(GracefulSignalMilliseconds),
                        cancellationToken);
                    return new CleanupOutcome(true, true);
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

        var cleanupConfirmed = await StopTreeSafelyAsync(process);
        return new CleanupOutcome(false, cleanupConfirmed);
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
        var exactSecrets = secretValues
            .Where(value => !string.IsNullOrEmpty(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        standardOutput = RedactOutput(standardOutput, exactSecrets, ref sensitive);
        standardError = RedactOutput(standardError, exactSecrets, ref sensitive);
        return new SanitizedOutput(standardOutput, standardError, sensitive);
    }

    private static string RedactOutput(
        string value,
        IReadOnlyList<string> exactSecrets,
        ref bool sensitive)
    {
        if (value.Length == 0)
            return value;

        var redactionDeltas = new int[value.Length + 1];
        var detected = CollectCredentialAssignmentRedactions(value, redactionDeltas);
        detected |= CollectBearerCredentialRedactions(value, redactionDeltas);
        detected |= CollectExactSecretRedactions(value, exactSecrets, redactionDeltas);
        if (!detected)
            return value;

        sensitive = true;
        return RenderRedactions(value, redactionDeltas);
    }

    private static bool CollectExactSecretRedactions(
        string value,
        IReadOnlyList<string> exactSecrets,
        int[] redactionDeltas)
    {
        var detected = false;
        foreach (var secret in exactSecrets)
        {
            var searchStart = 0;
            while (searchStart < value.Length)
            {
                var occurrence = value.IndexOf(secret, searchStart, StringComparison.Ordinal);
                if (occurrence < 0)
                    break;

                detected = true;
                AddRedaction(redactionDeltas, occurrence, occurrence + secret.Length);
                searchStart = occurrence + 1;
            }
        }
        return detected;
    }

    private static bool CollectBearerCredentialRedactions(string value, int[] redactionDeltas)
    {
        var detected = false;
        foreach (Match match in BearerCredentialRegex().Matches(value))
        {
            AddRedaction(redactionDeltas, match.Index, match.Index + match.Length);
            detected = true;
        }
        return detected;
    }

    private static bool CollectCredentialAssignmentRedactions(string value, int[] redactionDeltas)
    {
        var matches = CredentialAssignmentRegex().Matches(value);
        var lineEnds = IndexLineEnds(value);
        var lineEndIndex = 0;
        var coveredThrough = 0;
        var detected = false;

        for (var index = 0; index < matches.Count; index++)
        {
            var match = matches[index];
            if (!IsCredentialName(match.Groups["name"].Value))
                continue;

            var assignment = match.Groups["assignment"];
            if (assignment.Index < coveredThrough)
                continue;
            var redactionEnd = assignment.Index + assignment.Length;
            var unquotedValue = match.Groups["unquotedValue"];
            if (unquotedValue.Success)
            {
                while (lineEnds[lineEndIndex] < redactionEnd)
                    lineEndIndex++;
                var lineEnd = lineEnds[lineEndIndex];
                var malformedQuotedValue = unquotedValue.Value[0] is '"' or '\'';
                redactionEnd = !malformedQuotedValue
                               && index + 1 < matches.Count
                               && matches[index + 1].Index < lineEnd
                    ? matches[index + 1].Index
                    : lineEnd;
            }

            AddRedaction(redactionDeltas, assignment.Index, redactionEnd);
            coveredThrough = redactionEnd;
            detected = true;
        }
        return detected;
    }

    private static void AddRedaction(int[] redactionDeltas, int start, int end)
    {
        redactionDeltas[start]++;
        redactionDeltas[end]--;
    }

    private static string RenderRedactions(string value, int[] redactionDeltas)
    {
        var redacted = new StringBuilder(value.Length);
        var copiedThrough = 0;
        var redactionStart = -1;
        var coverage = 0;
        for (var index = 0; index < value.Length; index++)
        {
            coverage += redactionDeltas[index];
            if (coverage > 0)
            {
                if (redactionStart < 0)
                    redactionStart = index;
                continue;
            }

            if (redactionStart < 0)
                continue;
            redacted.Append(value, copiedThrough, redactionStart - copiedThrough);
            redacted.Append('*');
            copiedThrough = index;
            redactionStart = -1;
        }

        if (redactionStart >= 0)
        {
            redacted.Append(value, copiedThrough, redactionStart - copiedThrough);
            redacted.Append('*');
            copiedThrough = value.Length;
        }
        redacted.Append(value, copiedThrough, value.Length - copiedThrough);
        return redacted.ToString();
    }

    private static int[] IndexLineEnds(string value)
    {
        var lineEnds = new List<int>();
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] is '\r' or '\n')
                lineEnds.Add(index);
        }
        return [.. lineEnds, value.Length];
    }

    private static bool IsCredentialName(string name) =>
        ExactOrDelimitedCredentialNameRegex().IsMatch(name)
        || CamelCaseCredentialSuffixRegex().IsMatch(name);

    private static bool ContainsDirectorySeparator(string executable) =>
        executable.IndexOf(Path.DirectorySeparatorChar) >= 0
        || executable.IndexOf(Path.AltDirectorySeparatorChar) >= 0;

    private static bool IsExecutableFile(string path)
    {
        if (!File.Exists(path))
            return false;
        if (OperatingSystem.IsWindows())
        {
            var extension = Path.GetExtension(path);
            return extension.Length > 0
                   && ExecutableExtensions(string.Empty)
                       .Any(candidate => string.Equals(candidate, extension, StringComparison.OrdinalIgnoreCase))
                   && HasWindowsPathAccess(path, WindowsFileExecute, directory: false);
        }

        return HasUnixPathAccess(path, UnixExecuteAccess);
    }

    private static bool IsReadableFile(string path)
    {
        if (!File.Exists(path))
            return false;
        if (OperatingSystem.IsWindows()
                ? !HasWindowsPathAccess(path, WindowsFileReadData, directory: false)
                : !HasUnixPathAccess(path, UnixReadAccess))
            return false;

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        return stream.CanRead;
    }

    private static bool HasDirectorySearchAccess(string path)
    {
        if (!Directory.Exists(path))
            return false;
        if (OperatingSystem.IsWindows())
        {
            return HasWindowsPathAccess(
                path,
                WindowsFileListDirectory | WindowsFileTraverse,
                directory: true);
        }

        return HasUnixPathAccess(path, UnixExecuteAccess);
    }

    private static bool HasWindowsPathAccess(string path, uint access, bool directory)
    {
        using var handle = CreateFileW(
            path,
            access,
            FileShare.ReadWrite | FileShare.Delete,
            IntPtr.Zero,
            FileMode.Open,
            directory ? WindowsFileFlagBackupSemantics : 0,
            IntPtr.Zero);
        return !handle.IsInvalid;
    }

    private static bool HasUnixPathAccess(string path, int mode)
    {
        if (OperatingSystem.IsLinux())
        {
            return FileAccessAt(
                       LinuxAtCurrentWorkingDirectory,
                       path,
                       mode,
                       LinuxAtEffectiveAccess) == 0;
        }
        if (OperatingSystem.IsMacOS())
        {
            return FileAccessAt(
                       MacOsAtCurrentWorkingDirectory,
                       path,
                       mode,
                       MacOsAtEffectiveAccess) == 0;
        }
        return false;
    }

    private static StringComparer EnvironmentNameComparer() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static IReadOnlyList<string> BootstrapEnvironmentNames() => OperatingSystem.IsWindows()
        ? ["PATH", "PATHEXT", "SystemRoot", "WINDIR", "ComSpec", "TEMP", "TMP", "USERPROFILE"]
        : ["PATH", "HOME", "USER", "LOGNAME", "SHELL", "TMPDIR", "LANG", "LC_ALL", "LC_CTYPE"];

    private static string[] ExecutableExtensions(string executable)
    {
        if (Path.HasExtension(executable))
            return [string.Empty];
        var pathExtensions = Environment.GetEnvironmentVariable("PATHEXT")
            ?.Split(';', StringSplitOptions.RemoveEmptyEntries)
            ?? [".COM", ".EXE", ".BAT", ".CMD"];
        return [string.Empty, .. pathExtensions];
    }

    private void TransferRegisteredProcess(
        Guid trackingId,
        RunnerProcessStartGuard startGuard,
        Task<bool>? startTask)
    {
        if (startGuard.HasStarted)
        {
            if (startGuard.IsSettled)
                _reaper.Release(trackingId);
            else
                _reaper.AdoptStarted(trackingId);
            return;
        }

        if (startGuard.IsSettled
            || startTask is null
            || (startTask.IsCompletedSuccessfully && !startTask.Result))
        {
            _reaper.Release(trackingId);
            return;
        }

        _reaper.AdoptPendingStart(trackingId, startTask);
    }

    private static RunnerPathCheck Available(string message) => new(true, message);
    private static RunnerPathCheck Unavailable(string message) => new(false, message);

    private static RunnerProcessResult Failure(string message) =>
        new(null, string.Empty, string.Empty, false, Started: false, CleanlyStopped: false, Error: message);

    private static RunnerProcessResult InterruptedBeforeStart(
        bool timedOut,
        bool cancelled,
        bool cleanupConfirmed) =>
        new(
            null,
            string.Empty,
            string.Empty,
            timedOut,
            Cancelled: cancelled,
            Started: false,
            CleanlyStopped: cleanupConfirmed,
            CleanupConfirmed: cleanupConfirmed,
            Error: cleanupConfirmed
                ? timedOut
                    ? "The probe timed out before process startup."
                    : "The probe was cancelled before process startup."
                : "Process startup exceeded the deadline; background cleanup is monitoring for a late start.");

    [GeneratedRegex(
        @"(?:\A|[^A-Za-z0-9_])(?<assignment>[""']?(?<name>[A-Za-z][A-Za-z0-9_-]*)[""']?[ \t]*[:=][ \t]*(?<value>(?<quotedValue>""(?:\\[^\r\n]|[^""\\\r\n])*""|'(?:\\[^\r\n]|[^'\\\r\n])*')|(?<unquotedValue>[^\s,;&|}\]]+)))",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex CredentialAssignmentRegex();

    [GeneratedRegex(
        @"(?i)\A(?:[A-Za-z][A-Za-z0-9]*[_-])*(?:api[_-]?(?:key|token)|access[_-]?token|refresh[_-]?token|secret[_-]?access[_-]?key|private[_-]?key|database[_-]?url|connection[_-]?string|token|password|secret|authorization)\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex ExactOrDelimitedCredentialNameRegex();

    [GeneratedRegex(
        @"[a-z0-9](?:ApiKey|ApiToken|AccessToken|RefreshToken|SecretAccessKey|PrivateKey|DatabaseUrl|ConnectionString|AuthToken|Authorization|Token|Password|Secret)\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex CamelCaseCredentialSuffixRegex();

    [GeneratedRegex(
        @"(?im)\bbearer\s+[^\s\r\n]+",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex BearerCredentialRegex();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        FileShare shareMode,
        IntPtr securityAttributes,
        FileMode creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("libc", EntryPoint = "faccessat", SetLastError = true)]
    private static extern int FileAccessAt(
        int directoryFileDescriptor,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        int mode,
        int flags);

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
                try
                {
                    var strictUtf8 = new UTF8Encoding(
                        encoderShouldEmitUTF8Identifier: false,
                        throwOnInvalidBytes: true);
                    return new CapturedOutput(
                        strictUtf8.GetString(_standardOutput.ToArray()),
                        strictUtf8.GetString(_standardError.ToArray()),
                        IsValidUtf8: true);
                }
                catch (DecoderFallbackException)
                {
                    return new CapturedOutput(string.Empty, string.Empty, IsValidUtf8: false);
                }
            }
        }
    }

    private sealed record CapturedOutput(
        string StandardOutput,
        string StandardError,
        bool IsValidUtf8);
    private sealed record CleanupOutcome(bool CleanlyStopped, bool CleanupConfirmed);
    private sealed record SanitizedOutput(
        string StandardOutput,
        string StandardError,
        bool SensitiveOutputDetected);
}

internal sealed class RunnerProcessProbeSeams
{
    internal Func<Process, CancellationToken, Task<bool>>? StopTreeAsync { get; init; }
    internal Func<string, CancellationToken, Task<RunnerPathCheck>>? CheckExecutableAsync { get; init; }
    internal Func<string, CancellationToken, Task<RunnerPathCheck>>? CheckFileAsync { get; init; }
    internal Func<string, CancellationToken, Task<RunnerPathCheck>>? CheckDirectoryAsync { get; init; }
    internal Func<RunnerProcessStartGuard, CancellationToken, Task<bool>>? StartProcessAsync { get; init; }
    internal Action? StartCommitted { get; init; }
}
