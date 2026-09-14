using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Git;

public sealed class WindowsWorktreeLockDiagnostics(WorktreeDiagnosticIO io, IOptions<WorktreeLockSettings> settings,
    IOptions<GitSettings> gitSettings, TimeProvider clock) : IWorktreeLockDiagnostics
{
    public async Task<WorktreeLockSnapshot> CaptureAsync(string root, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var at = clock.GetUtcNow().UtcDateTime; var started = clock.GetTimestamp();
        WorktreeLockSnapshot Result(WorktreeLockStatus status, string reason) => new(status, reason, at, []);
        if (!io.Supported) return Result(WorktreeLockStatus.Unavailable, "UnsupportedPlatform");
        var executable = settings.Value.HandleExecutablePath;
        if (string.IsNullOrWhiteSpace(executable)) return Result(WorktreeLockStatus.Unavailable, "MissingConfiguration");
        var owners = new List<WorktreeLockOwner>(); var omitted = 0; var partial = false;
        var fileControl = false; var directoryControl = false;
        string? version = null, identity = null;
        WorktreeLockSnapshot Snapshot(WorktreeLockStatus status, string reason)
        {
            var snapshot = new WorktreeLockSnapshot(status, reason, at, owners.ToArray(), omitted,
                version, identity, fileControl, directoryControl, (long)clock.GetElapsedTime(started).TotalMilliseconds);
            while (Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(snapshot)) > 32768 && snapshot.Owners.Count > 0)
                snapshot = snapshot with { Owners = snapshot.Owners.SkipLast(1).ToArray(), OmittedOwners = snapshot.OmittedOwners + 1,
                    Status = WorktreeLockStatus.Partial, Reason = "EvidenceTruncated" };
            return snapshot;
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5), clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        var token = linked.Token;
        try
        {
            if (!Path.IsPathFullyQualified(executable) || !io.TrustedExecutable(executable))
                return Result(WorktreeLockStatus.Unavailable, "UntrustedOrMissingTool");
            if (!io.Elevated) return Result(WorktreeLockStatus.Unavailable, "InsufficientPrivileges");
            if (!io.LicenseReady) return Result(WorktreeLockStatus.Unavailable, "LicenseSetupRequired");
            root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            if (!io.Exists(root)) return Result(WorktreeLockStatus.PathGone, "PathGone");
            var scratch = Path.GetFullPath(io.ScratchRoot);
            if (WorktreeNativeIO.Within(scratch, root) || WorktreeNativeIO.Within(scratch, Path.GetFullPath(gitSettings.Value.WorktreeBasePath)))
                return Result(WorktreeLockStatus.Unavailable, "ExternalControlRootRequired");
            (version, identity) = io.ToolIdentity(executable);
            version = WorktreeHandleCsv.Sanitize(version, 80); identity = WorktreeHandleCsv.Sanitize(identity, 128);
            var bytes = new WorktreeDiagnosticBytes();
            async Task<WorktreeDiagnosticOutput> QueryAsync(string path)
            {
                token.ThrowIfCancellationRequested();
                var start = new ProcessStartInfo(executable) { WorkingDirectory = scratch, UseShellExecute = false,
                    CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                start.ArgumentList.Add("-nobanner"); start.ArgumentList.Add("-v"); start.ArgumentList.Add(path);
                return await io.RunAsync(start, bytes, token);
            }
            var target = await QueryAsync(root);
            if (target.ExitCode != 0) return Snapshot(WorktreeLockStatus.Failed, "ToolFailed");
            var parsed = new WorktreeHandleCsv().Parse(target.Csv, root);
            owners.AddRange(parsed.Owners); omitted = parsed.Omitted; partial = parsed.Partial || target.Truncated;
            token.ThrowIfCancellationRequested();
            using var controls = io.CreateControls(scratch);
            if (WorktreeNativeIO.Within(controls.Root, root)
                || WorktreeNativeIO.Within(controls.Root, Path.GetFullPath(gitSettings.Value.WorktreeBasePath)))
                return Snapshot(WorktreeLockStatus.Partial, "ExternalControlRootRequired");
            var calibration = await QueryAsync(controls.Root);
            var calibrated = new WorktreeHandleCsv().Parse(calibration.Csv, controls.Root);
            fileControl = calibrated.Owners.Any(o => o.ProcessId == controls.ProcessId
                && WorktreeNativeIO.Same(Path.Combine(controls.Root, o.RelativePath), controls.File));
            directoryControl = calibrated.Owners.Any(o => o.ProcessId == controls.ProcessId && o.RelativePath == ".");
            if (calibration.ExitCode != 0 || calibration.Truncated || calibrated.Partial || !fileControl || !directoryControl)
                return Snapshot(owners.Count > 0 ? WorktreeLockStatus.Partial : WorktreeLockStatus.Unavailable, "PositiveControlFailed");
            for (var i = 0; i < owners.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var first = io.ProcessStart(owners[i].ProcessId);
                var second = io.ProcessStart(owners[i].ProcessId);
                // CSV lacks a creation identity. Two compatible reads bound enrichment; ancestry stays unknown.
                if (first is null || first != second || first > at.Ticks) { partial = true; continue; }
                owners[i] = owners[i] with { ProcessStartTicks = first };
            }
            token.ThrowIfCancellationRequested();
            return Snapshot(partial ? WorktreeLockStatus.Partial : owners.Count > 0
                ? WorktreeLockStatus.OwnersObserved : WorktreeLockStatus.NoOwnersObserved,
                partial ? "CoverageLimited" : owners.Count > 0 ? "Observed" : "NoMatchingHandlesObserved");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeout.IsCancellationRequested)
        { return Snapshot(WorktreeLockStatus.TimedOut, "DiagnosticBudgetExpired"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException
            or ArgumentException or System.ComponentModel.Win32Exception)
        { return Snapshot(owners.Count > 0 ? WorktreeLockStatus.Partial : WorktreeLockStatus.Failed,
            ex is FormatException ? "MalformedOrUnknownCsv" : "DiagnosticFailed"); }
    }
}
