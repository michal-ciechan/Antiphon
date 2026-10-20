using System.Diagnostics;
using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>Recovery metadata is a fence. Only this provider's live L-bound object can grant an exception.</summary>
internal sealed class RepositoryVerificationWindows(ILandingGit git)
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Window> _live = new();
    private static string MarkerPath(string common) => Path.Combine(common, "antiphon", "verification-window.json");

    public async Task<RepositoryVerificationWindow> OpenAsync(string common, IAsyncDisposable execution,
        RepositoryVerificationIdentity identity, CancellationToken ct)
    {
        if (execution is not FileStream handle || !LiveHandle(handle)
            || !LandingGit.PathsEqual(handle.Name, Path.Combine(common, "antiphon", "land-execution.lock"))
            || !GitObjectId.IsFull(identity.CandidateSha) || identity.Attempt < 1)
            throw new InvalidOperationException("verification_window_execution_lease_required");
        var existing = await InspectAsync(common, null, ct);
        if (!existing.Allowed) throw new IOException("verification_window_recovery_required");
        using var process = Process.GetCurrentProcess();
        var record = new Marker(1, Guid.NewGuid(), common, process.Id,
            process.StartTime.ToUniversalTime().Ticks, identity);
        var path = MarkerPath(common);
        LandVerificationArtifacts.RequireNoLinks(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + record.Id.ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, record, cancellationToken: ct);
                stream.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        var window = new Window(this, record, handle);
        lock (_gate) _live.Add(record.Id, window);
        return window;
    }

    public async Task<Admission> InspectAsync(string common, RepositoryPreflight? preflight, CancellationToken ct)
    {
        try
        {
            var path = MarkerPath(common);
            LandVerificationArtifacts.RequireNoLinks(path);
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.Directory) != 0) return new(false);
            }
            catch (FileNotFoundException) { return new(true); }
            catch (DirectoryNotFoundException) { return new(true); }
            var marker = await ReadAsync(path, ct);
            if (marker is not { Schema: 1, ProcessId: > 0, StartTicks: > 0 }
                || marker.Id == Guid.Empty || marker.Identity is null
                || marker.Identity.TaskId == Guid.Empty || marker.Identity.RequestId == Guid.Empty || marker.Identity.OperationId == Guid.Empty
                || marker.Identity.Attempt < 1 || !GitObjectId.IsFull(marker.Identity.CandidateSha)
                || string.IsNullOrWhiteSpace(marker.Common) || !Path.IsPathFullyQualified(marker.Common)
                || string.IsNullOrWhiteSpace(marker.Identity.WorktreePath) || !Path.IsPathFullyQualified(marker.Identity.WorktreePath)
                || string.IsNullOrWhiteSpace(marker.Identity.SourcePath) || !Path.IsPathFullyQualified(marker.Identity.SourcePath)
                || string.IsNullOrWhiteSpace(marker.Identity.SourceRef) || string.IsNullOrWhiteSpace(marker.Identity.TargetRef)
                || !LandingGit.PathsEqual(marker.Common, common))
                return new(false);
            var alive = await git.IsProcessAliveAsync(marker.ProcessId, marker.StartTicks, ct);
            // A dead coordinator grants no compatibility. Ordinary child recovery still applies.
            if (alive == false) return new(true);
            if (alive != true || preflight is null) return new(false);
            Window? live;
            lock (_gate) _live.TryGetValue(marker.Id, out live);
            if (live is null || live.Closed || live.Record != marker || !LiveHandle(live.Execution)
                || marker.ProcessId != Environment.ProcessId)
                return new(false);
            if (!Compatible(common, marker.Identity, preflight)) return new(false);
            return new(true, marker.Id, marker.Identity.TaskId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                   or ArgumentException or InvalidOperationException)
        { return new(false); }
    }

    private static bool Compatible(string common, RepositoryVerificationIdentity identity, RepositoryPreflight operation)
    {
        if (operation.Identity == Guid.Empty) return false;
        if (operation.StartRefSha is { } sha)
            return GitObjectId.IsFull(sha) && operation.WorktreePath is null && operation.Branch is null;
        if (operation.WorktreePath is not { } path || !Path.IsPathFullyQualified(path)
            || operation.Branch is not { } branch || !DelegateBranchName.TryParse(branch, out var parsed)
            || parsed.ShortId != DelegationReportFormatter.Short(operation.Identity)
            || "refs/heads/" + branch == identity.SourceRef || "refs/heads/" + branch == identity.TargetRef)
            return false;
        LandVerificationArtifacts.RequireNoLinks(path);
        foreach (var protectedPath in new[] { common, identity.WorktreePath, identity.SourcePath })
            if (DelegationWorkspaceResolver.IsWithinRoot(path, protectedPath)
                || DelegationWorkspaceResolver.IsWithinRoot(protectedPath, path)) return false;
        return true;
    }

    private async ValueTask CloseAsync(Window window)
    {
        lock (_gate)
        {
            if (window.Closed) return;
            window.Closed = true;
            _live.Remove(window.Id);
        }
        // Never turn an unjoined child into an unfenced repository. Admission stays closed
        // behind the marker/journal for explicit recovery if custody is uncertain.
        if (await RepositoryChildJournal.HasUnjoinedVerificationAsync(window.Record.Common, window.Id, CancellationToken.None))
            return;
        var path = MarkerPath(window.Record.Common);
        if (File.Exists(path) && await ReadAsync(path, CancellationToken.None) == window.Record)
            File.Delete(path);
    }

    private static async Task<Marker?> ReadAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        return await JsonSerializer.DeserializeAsync<Marker>(stream, cancellationToken: ct);
    }
    private static bool LiveHandle(FileStream stream)
    {
        try { return stream.CanWrite && !stream.SafeFileHandle.IsClosed && !stream.SafeFileHandle.IsInvalid; }
        catch (ObjectDisposedException) { return false; }
    }
    internal sealed record Admission(bool Allowed, Guid? WindowId = null, Guid? TaskId = null);
    private sealed record Marker(int Schema, Guid Id, string Common, int ProcessId, long StartTicks,
        RepositoryVerificationIdentity Identity);
    private sealed class Window(RepositoryVerificationWindows owner, Marker record, FileStream execution)
        : RepositoryVerificationWindow
    {
        public Marker Record { get; } = record;
        public FileStream Execution { get; } = execution;
        public bool Closed { get; set; }
        public override Guid Id => Record.Id;
        public override ValueTask DisposeAsync() => owner.CloseAsync(this);
    }
}
