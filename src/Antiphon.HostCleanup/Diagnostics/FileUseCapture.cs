using System.ComponentModel;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Antiphon.HostCleanup.DiagnosticText;

namespace Antiphon.HostCleanup.Diagnostics;

// Caller-owned service. A timed-out native worker retains the only seat until it returns.
public sealed class FileUseCapture(IFileUseProvider? provider = null, IFileUseEvidenceWriter? writer = null, TimeProvider? timeProvider = null)
{
    private readonly IFileUseProvider provider = provider ??
        (OperatingSystem.IsWindows() ? new WindowsFileUseProvider() : new LinuxFileUseProvider());
    private readonly IFileUseEvidenceWriter writer = writer ?? new FileUseEvidenceWriter();
    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;
    private int busy;
    private Task worker = Task.CompletedTask;
    public Task DrainAsync() => Volatile.Read(ref worker);

    public static bool IsEligible(Exception error, string? failedCommandText = null)
    {
        if (error is OperationCanceledException) return false;
        if (error is UnauthorizedAccessException or SecurityException) return true;
        var code = error is Win32Exception native ? native.NativeErrorCode : error.HResult & 0xffff;
        if (error is IOException or Win32Exception && code is 1 or 5 or 11 or 13 or 16 or 26 or 32 or 33) return true;
        if (failedCommandText is not { Length: > 0 and <= 8192 }) return false;
        return new[] { "in use", "access denied", "permission denied", "being used by another process", "text file busy" }
            .Any(text => failedCommandText.Contains(text, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<FileUseSnapshot> TryCaptureAsync(FileUseRequest request, CancellationToken cancellationToken = default)
    {
        FileUseSnapshot result;
        try { result = await CaptureAsync(request, cancellationToken); }
        catch
        {
            result = new(Guid.NewGuid().ToString("N"), FileUseStatus.Unavailable, FileUseCoverage.None,
                "[unavailable]", "[unavailable]", 0, "[unavailable]", time.GetUtcNow(), time.GetUtcNow(), [], [FileUseReason.ProviderFailure]);
        }
        if (result.Status != FileUseStatus.NotTriggered)
        {
            // Timeout/Busy are first-failure observations too. A later retry must reuse
            // their identity rather than start a later census after the evidence changed.
            try
            {
                lock (request.Operation.Captures)
                {
                    if (request.Operation.Captures.Count < 8)
                        request.Operation.Captures.TryAdd(request.Path + "\0" + result.NativeCode, result);
                }
            }
            catch { /* The caller's original outcome remains authoritative. */ }
            try { request.Error.Data["fileUseCaptureId"] = result.CaptureId; request.Error.Data["fileUseCaptureSummary"] = result.Summary; }
            catch { /* Read-only or hostile exception Data never changes the failure path. */ }
        }
        return result;
    }

    private async Task<FileUseSnapshot> CaptureAsync(FileUseRequest request, CancellationToken cancellationToken)
    {
        FileUseSnapshot snapshot = Envelope(request);
        if (!IsEligible(request.Error, request.FailedCommandText)) return snapshot with { Status = FileUseStatus.NotTriggered };
        var key = request.Path + "\0" + snapshot.NativeCode;
        lock (request.Operation.Captures)
        {
            if (request.Operation.Captures.TryGetValue(key, out var previous)) return previous;
            if (request.Operation.Captures.Count >= 8) return snapshot with { Reasons = [FileUseReason.Suppressed] };
        }
        if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) return snapshot with { Status = FileUseStatus.Busy };
        var clockStart = time.GetTimestamp();
        var budget = request.Budget > TimeSpan.Zero ? TimeSpan.FromTicks(Math.Min(request.Budget.Ticks, TimeSpan.FromSeconds(5).Ticks)) : TimeSpan.Zero;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var totalDeadline = Task.Delay(budget, time, cancellation.Token);
        var observationDeadline = Task.Delay(TimeSpan.FromTicks(budget.Ticks * 4 / 5), time, cancellation.Token);
        Task current = Task.CompletedTask;
        try
        {
            if (cancellationToken.IsCancellationRequested || budget == TimeSpan.Zero) return snapshot with { Status = FileUseStatus.TimedOut, Reasons = [FileUseReason.Budget] };
            var observing = Task.Run(() => provider.Observe(request, cancellation.Token));
            current = observing; Volatile.Write(ref worker, observing);
            if (await Task.WhenAny(observing, observationDeadline, totalDeadline) != observing)
                return snapshot with { Status = FileUseStatus.TimedOut, Reasons = [FileUseReason.Budget] };
            FileUseObservation observation;
            try { observation = await observing; }
            catch (UnauthorizedAccessException) { observation = new(FileUseStatus.Unavailable, FileUseCoverage.None, [], [FileUseReason.AccessDenied]); }
            catch { observation = new(FileUseStatus.Unavailable, FileUseCoverage.None, [], [FileUseReason.ProviderFailure]); }
            var envelope = snapshot;
            var writing = Task.Run(() =>
            {
                cancellation.Token.ThrowIfCancellationRequested();
                // Redaction/serialization share the remaining deadline and the one worker seat.
                var safe = Sanitize(envelope, observation, request) with
                {
                    Backend = observation.Backend ?? (OperatingSystem.IsLinux() ? "proc" : "unavailable"),
                    NativeErrors = observation.NativeErrors.Take(8).ToArray(),
                    DurationMilliseconds = time.GetElapsedTime(clockStart).TotalMilliseconds,
                    ObservationEndUtc = time.GetUtcNow()
                };
                try
                {
                    var root = Path.GetFullPath(request.EvidenceRoot);
                    var target = Path.GetFullPath(request.Path).TrimEnd(Path.DirectorySeparatorChar);
                    if (root == target || root.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new IOException("invalid destination");
                    var path = writer.Write(safe with { Persistence = "Persisted" }, root, cancellation.Token);
                    return path is null ? safe with { Reasons = [.. safe.Reasons, FileUseReason.EvidenceUnavailable] }
                        : safe with { EvidencePath = path, Persistence = "Persisted" };
                }
                catch { return safe with { Reasons = [.. safe.Reasons, FileUseReason.EvidenceUnavailable] }; }
            });
            current = writing; Volatile.Write(ref worker, writing);
            if (await Task.WhenAny(writing, totalDeadline) != writing)
                snapshot = snapshot with { Status = FileUseStatus.TimedOut, Persistence = "Unavailable", Reasons = [.. snapshot.Reasons, FileUseReason.Budget, FileUseReason.EvidenceUnavailable] };
            else
            {
                try
                {
                    snapshot = await writing;
                }
                catch { snapshot = snapshot with { Reasons = [.. snapshot.Reasons, FileUseReason.RedactionUnavailable, FileUseReason.EvidenceUnavailable] }; }
            }
            lock (request.Operation.Captures) { if (request.Operation.Captures.Count < 8) request.Operation.Captures.TryAdd(key, snapshot); }
            return snapshot;
        }
        catch { return snapshot with { Reasons = [.. snapshot.Reasons, FileUseReason.ProviderFailure] }; }
        finally
        {
            try { cancellation.Cancel(); } catch { /* A hostile diagnostic callback cannot escape. */ }
            // The continuation owns only seat release, never publication or late observation.
            if (current.IsCompleted) Interlocked.Exchange(ref busy, 0);
            else Volatile.Write(ref worker, current.ContinueWith(done => { _ = done.Exception; Interlocked.Exchange(ref busy, 0); }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default));
        }
    }

    private FileUseSnapshot Envelope(FileUseRequest request)
    {
        // Safe even if policy/input is unavailable. Never use a diagnostic exception's message.
        var native = request.Error is Win32Exception win32 ? win32.NativeErrorCode : request.Error.HResult & 0xffff;
        var snapshot = new FileUseSnapshot(Guid.NewGuid().ToString("N"), FileUseStatus.Unavailable, FileUseCoverage.None,
            "[unavailable]", request.Error.GetType().Name, native, "[unavailable]", request.FailureUtc, time.GetUtcNow(), [], [])
        { FailureTimestamp = request.FailureTimestamp };
        try
        {
            var safe = new FileUseText(request);
            return snapshot with { Path = safe.Text(request.Path, 1024), FailureText = safe.Text(request.Error.Message, 2048),
                OperationId = safe.Text(request.OperationId, 128), TaskId = safe.Optional(request.TaskId, 128), SourceSha = safe.Optional(request.SourceSha, 64),
                RunId = safe.Optional(request.RunId, 128), SessionId = safe.Optional(request.SessionId, 128),
                PathIdentity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Path))) };
        }
        catch { return snapshot with { Reasons = [FileUseReason.RedactionUnavailable] }; }
    }

    private static FileUseSnapshot Sanitize(FileUseSnapshot envelope, FileUseObservation raw, FileUseRequest request)
    {
        var safe = new FileUseText(request); var reasons = raw.Reasons.Take(32).ToList();
        var omitted = raw.Omitted;
        if (raw.Owners.Count > 32) { reasons.Add(FileUseReason.OwnerLimit); omitted += raw.Owners.Count - 32; }
        var owners = new List<FileUseOwner>();
        foreach (var owner in raw.Owners.Take(32))
        {
            if (owner.Ancestors.Count > 8) { reasons.Add(FileUseReason.AncestorLimit); omitted += owner.Ancestors.Count - 8; }
            if (owner.Name.Length > 256 || owner.Resource.Length > 1024) { reasons.Add(FileUseReason.TextLimit); omitted++; }
            var command = safe.Command(owner.CommandLine, owner.ProvenModules, owner.ProvenExecutable);
            if (command is null && owner.CommandLine is not null) { reasons.Add(FileUseReason.RedactionUnavailable); omitted++; }
            owners.Add(owner with { Name = safe.Text(owner.Name, 256), NativeStart = safe.Text(owner.NativeStart, 128),
                CommandLine = command, Resource = safe.Text(owner.Resource, 1024), Attribution = safe.Text(owner.Attribution, 128),
                MatchKind = safe.Text(owner.MatchKind, 64), ProvenModules = [], ProvenExecutable = null,
                Role = Path.GetFileNameWithoutExtension(owner.Name).ToLowerInvariant() switch
                {
                    "dotnet" => "dotnet", "msbuild" => "MSBuild", "vbcscompiler" => "VBCSCompiler",
                    "openconsole" => "OpenConsole", "git" => "git", "testhost" => "testhost",
                    "antiphon.sessionrunner" => "runner", "antiphon.apphost" => "AppHost", _ => null
                },
                Ancestors = owner.Ancestors.Take(8).Select(a => a with { Name = safe.Text(a.Name, 256), NativeStart = safe.Text(a.NativeStart, 128), CommandLine = safe.Command(a.CommandLine, []) }).ToArray() });
        }
        return FileUseEvidenceWriter.Bound(envelope with { Status = raw.Status, Coverage = raw.Coverage, Owners = owners,
            Reasons = reasons.Distinct().ToArray(), Omitted = omitted, PathKind = safe.Optional(raw.PathKind, 32), ResourceIdentity = safe.Optional(raw.ResourceIdentity, 64) });
    }
}

internal sealed class FileUseText
{
    private readonly DiagnosticTextRedactor text;
    private readonly IReadOnlyList<string> secrets;
    internal FileUseText(FileUseRequest request)
    {
        text = new(false, request.ProjectDirectories.Take(128));
        secrets = request.SecretValues.Concat(new[] { Environment.GetEnvironmentVariable("ANTIPHON_TASK_TOKEN") ?? "" }).ToArray();
        if (secrets.Count > 256 || secrets.Any(s => s.Length > 4000) || secrets.Sum(s => s.Length) > 256 * 1024) throw new IOException("redaction unavailable");
    }
    internal string? Optional(string? value, int limit) => value is null ? null : Text(value, limit);
    internal string Text(string value, int limit)
    {
        if (value.Length > 65536) return "[redacted: input limit]";
        var safe = DiagnosticCredentialRedactor.Sanitize(value, "", secrets).StandardOutput;
        safe = text.Redact(DiagnosticUrlRedactor.Redact(safe, allSchemes: true));
        // Separate-value credential switches are not covered by assignment redaction.
        safe = Regex.Replace(safe, @"(?i)--?[\w-]*(?:token|password|secret|api-key|authorization)\s+(?:""[^""]*""|'[^']*'|\S+)", "[redacted]", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        safe = Regex.Replace(safe, @"(?i)(?:^|\s)-(?:EncodedCommand|Command|c)\s+.*", " [redacted script]", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        return safe.Length <= limit ? safe : safe[..Math.Max(0, limit - 15)] + "...[truncated]";
    }
    internal string? Command(string? raw, IReadOnlyList<string> modules, string? executable = null)
    {
        if (raw is null || raw.Length > 65536) return null;
        // Linux cmdline is already an argv vector. Unknown shell quoting is never reconstructed.
        var args = raw.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        if (args.Length == 0 || args.Length > 256 || args.Any(a => a.Length > 8192)) return null;
        var output = new List<string> { executable is null ? "[executable unavailable]" : Text(Path.GetFileName(executable), 256) };
        foreach (var arg in args.Skip(1))
        {
            if (modules.Contains(arg, StringComparer.Ordinal)) output.Add(Text(Path.GetFileName(arg), 256));
            else if (arg is "--no-logo" or "--nologo" or "--no-profile" or "--no-build" or "--verbose") output.Add(arg);
            else output.Add("[redacted]");
        }
        return Text(string.Join(' ', output), 4096);
    }
}
