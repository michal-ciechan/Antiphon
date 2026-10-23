using System.ComponentModel;
using System.Globalization;
using System.Text;

namespace Antiphon.HostCleanup.Diagnostics;

public sealed record WindowsFileUseProcess(int Pid, long StartFileTime, string Name);
public sealed record WindowsFileUseMetadata(int Pid, long StartFileTime, string Name, string? Executable,
    string? Arguments, int? ParentPid);
public sealed record WindowsFileUseList(int Error, uint Needed, IReadOnlyList<WindowsFileUseProcess> Processes);

// Read-only native boundary. Arguments are an argv vector separated by NUL, never shell text.
public class WindowsFileUseSource
{
    public virtual bool IsSupported => OperatingSystem.IsWindows();
    public virtual string PathKind(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0) return "link";
            return (attributes & FileAttributes.Directory) != 0 ? "directory" : "file";
        }
        catch (FileNotFoundException) { return "absent"; }
        catch (DirectoryNotFoundException) { return "absent"; }
    }
    public virtual IEnumerable<string> Files(string directory) => Directory.EnumerateFiles(directory, "*", new EnumerationOptions
    { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false });
    public virtual int Start(out uint session) { session = 0; throw new PlatformNotSupportedException(); }
    public virtual int Register(uint session, string[] files) => throw new PlatformNotSupportedException();
    public virtual WindowsFileUseList List(uint session, uint capacity) => throw new PlatformNotSupportedException();
    public virtual void End(uint session) => throw new PlatformNotSupportedException();
    public virtual IEnumerable<int> Processes() => throw new PlatformNotSupportedException();
    public virtual WindowsFileUseMetadata Metadata(int pid) => throw new PlatformNotSupportedException();
    public virtual long Generation(int pid) => Metadata(pid).StartFileTime;
    public virtual IEnumerable<string> Modules(int pid) => throw new PlatformNotSupportedException();
}

// Synchronous OS calls live on FileUseCapture's single bounded worker, never on a new diagnostic child.
public sealed class WindowsFileUseProvider(WindowsFileUseSource? source = null) : IFileUseProvider
{
    private readonly WindowsFileUseSource source = source ??
        (OperatingSystem.IsWindows() ? new NativeWindowsFileUseSource() : new WindowsFileUseSource());

    public FileUseObservation Observe(FileUseRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!source.IsSupported) return new(FileUseStatus.Unavailable, FileUseCoverage.None, [], [FileUseReason.UnsupportedPlatform]);
        var owners = new List<FileUseOwner>();
        var reasons = new HashSet<FileUseReason> { FileUseReason.NamespaceLimited };
        var errors = new HashSet<int>();
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new List<string>();
        var observed = new HashSet<int>();
        var omitted = 0;
        var bytes = 0;
        var existing = false;
        var needsFallback = false;
        string? kind = null;
        bool Charge(params string?[] values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var value in values)
            {
                if (value is null) continue;
                if (value.Length > 65536 || bytes + Encoding.UTF8.GetByteCount(value) > 8 * 1024 * 1024)
                { reasons.Add(FileUseReason.InputLimit); omitted++; return false; }
                bytes += Encoding.UTF8.GetByteCount(value);
            }
            return true;
        }
        void Failure(Exception error)
        {
            if (error is OperationCanceledException) cancellationToken.ThrowIfCancellationRequested();
            var code = error is Win32Exception win32 ? win32.NativeErrorCode :
                error is UnauthorizedAccessException ? 5 : 0;
            if (code != 0) errors.Add(code);
            reasons.Add(code == 5 ? FileUseReason.AccessDenied : FileUseReason.ProviderFailure);
        }
        void NativeError(int code)
        {
            errors.Add(code);
            reasons.Add(code == 5 ? FileUseReason.AccessDenied : code == 353 ? FileUseReason.SessionLimit : FileUseReason.NativeFailure);
        }
        FileUseObservation Result(FileUseStatus status) => new(status, FileUseCoverage.Partial, owners, reasons.ToArray(), omitted)
        { PathKind = kind, Backend = "restart-manager+cim", NativeErrors = errors.Take(8).ToArray() };
        void AddFile(string path)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (files.Count >= 128) { reasons.Add(FileUseReason.ResourceLimit); omitted++; return; }
            if (Charge(path)) files.Add(path);
        }
        try
        {
            var paths = new[] { request.Path }.Concat(request.AdditionalPaths).Take(128);
            if (request.AdditionalPaths.Count >= 128)
            { reasons.Add(FileUseReason.ResourceLimit); omitted += request.AdditionalPaths.Count + 1 - 128; }
            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pathKind = source.PathKind(path);
                kind ??= pathKind;
                if (pathKind == "absent") continue;
                existing = true;
                if (pathKind == "file") AddFile(path);
                else if (pathKind == "directory")
                {
                    needsFallback = true;
                    directories.Add(path);
                    foreach (var file in source.Files(path).Take(129)) AddFile(file);
                }
                else { needsFallback = true; reasons.Add(FileUseReason.ResourceLimit); }
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        { existing = true; needsFallback = true; Failure(error); }
        if (!existing) return new(FileUseStatus.PathGone, FileUseCoverage.None, [], [FileUseReason.PathGone])
        { PathKind = "absent", Backend = "restart-manager+cim" };

        IReadOnlyList<WindowsFileUseProcess> rows = [];
        var started = false;
        uint session = 0;
        try
        {
            if (files.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var status = source.Start(out session);
                started = status == 0;
                if (status != 0) { NativeError(status); needsFallback = true; }
                else
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    status = source.Register(session, files.ToArray());
                    if (status != 0) { NativeError(status); needsFallback = true; }
                    else
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var list = source.List(session, 0);
                        for (var attempt = 0; list.Error == 234 && attempt < 3; attempt++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (list.Needed > 4096) { reasons.Add(FileUseReason.ProcessLimit); omitted++; break; }
                            list = source.List(session, list.Needed);
                        }
                        if (list.Error != 0) { NativeError(list.Error); needsFallback = true; }
                        else rows = list.Processes;
                    }
                }
            }
            else needsFallback = true;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        { Failure(error); needsFallback = true; }
        finally
        {
            if (started)
            {
                try { source.End(session); }
                catch (Exception error) { Failure(error); }
            }
        }

        void Enrich(WindowsFileUseProcess row, string match, string resource, WindowsFileUseMetadata? metadata = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (owners.Count >= 32) { reasons.Add(FileUseReason.OwnerLimit); omitted++; return; }
            if (!Charge(row.Name, resource)) return;
            var generation = row.StartFileTime.ToString(CultureInfo.InvariantCulture);
            var owner = new FileUseOwner(row.Pid, row.Name, generation, SafeStart(row.StartFileTime),
                null, null, [], null, "identity only", match, resource);
            try
            {
                metadata ??= source.Metadata(row.Pid);
                if (metadata.Pid != row.Pid || metadata.StartFileTime != row.StartFileTime ||
                    source.Generation(row.Pid) != row.StartFileTime)
                {
                    reasons.Add(FileUseReason.IdentityChanged);
                    owners.Add(owner with { State = FileUseOwnerState.IdentityChanged }); return;
                }
                var ancestors = new List<FileUseAncestor>();
                var seen = new HashSet<int> { row.Pid };
                var parent = metadata.ParentPid;
                var childStart = row.StartFileTime;
                while (parent is > 0 && ancestors.Count < 8 && seen.Add(parent.Value))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var ancestor = source.Metadata(parent.Value);
                        // A reused parent PID born after the child cannot establish its ancestry.
                        if (ancestor.StartFileTime > childStart || source.Generation(parent.Value) != ancestor.StartFileTime)
                        { reasons.Add(FileUseReason.IdentityChanged); break; }
                        if (!Charge(ancestor.Name, ancestor.Arguments)) break;
                        ancestors.Add(new(parent.Value, ancestor.StartFileTime.ToString(CultureInfo.InvariantCulture), ancestor.Name, ancestor.Arguments));
                        childStart = ancestor.StartFileTime; parent = ancestor.ParentPid;
                    }
                    catch (Exception error) when (error is not OperationCanceledException) { Failure(error); break; }
                }
                if (parent is > 0 && ancestors.Count == 8) { reasons.Add(FileUseReason.AncestorLimit); omitted++; }
                if (source.Generation(row.Pid) != row.StartFileTime)
                { reasons.Add(FileUseReason.IdentityChanged); owners.Add(owner with { State = FileUseOwnerState.IdentityChanged }); return; }
                if (!Charge(metadata.Name, metadata.Executable, metadata.Arguments)) { owners.Add(owner with { State = FileUseOwnerState.MetadataUnavailable }); return; }
                var own = request.OwnedGenerations.Any(g => g.Pid == row.Pid && g.NativeStart == generation) ||
                    ancestors.Any(a => request.OwnedGenerations.Any(g => g.Pid == a.Pid && g.NativeStart == a.NativeStart));
                var foreign = request.ForeignGenerations.Any(g => g.Pid == row.Pid && g.NativeStart == generation);
                owners.Add(owner with { Name = metadata.Name, CommandLine = metadata.Arguments, ParentPid = metadata.ParentPid,
                    Ancestors = ancestors, AntiphonOwned = own ? true : foreign ? false : null,
                    Attribution = own ? "known generation/ancestor" : foreign ? "known foreign generation" : "unknown",
                    ProvenExecutable = metadata.Executable });
            }
            catch (ArgumentException)
            { reasons.Add(FileUseReason.ExitedDuringCapture); owners.Add(owner with { State = FileUseOwnerState.ExitedDuringCapture }); }
            catch (InvalidOperationException)
            { reasons.Add(FileUseReason.ExitedDuringCapture); owners.Add(owner with { State = FileUseOwnerState.ExitedDuringCapture }); }
            catch (Exception error) when (error is not OperationCanceledException)
            { Failure(error); owners.Add(owner with { State = FileUseOwnerState.MetadataUnavailable }); }
        }

        foreach (var row in rows.Take(4096))
        {
            if (owners.Count >= 32) { reasons.Add(FileUseReason.OwnerLimit); omitted++; break; }
            if (observed.Add(row.Pid)) Enrich(row, "RestartManager", request.Path);
        }
        if (needsFallback)
        {
            try
            {
                var count = 0;
                foreach (var pid in source.Processes())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (count++ >= 4096) { reasons.Add(FileUseReason.ProcessLimit); omitted++; break; }
                    if (owners.Count >= 32) { reasons.Add(FileUseReason.OwnerLimit); omitted++; break; }
                    if (observed.Contains(pid)) continue;
                    try
                    {
                        var metadata = source.Metadata(pid);
                        if (metadata.StartFileTime <= 0) { reasons.Add(FileUseReason.IdentityChanged); continue; }
                        if (!Charge(metadata.Arguments, metadata.Name, metadata.Executable)) break;
                        string? resource = null;
                        var moduleCount = 0;
                        try
                        {
                            foreach (var module in source.Modules(pid))
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                if (moduleCount++ >= 4096) { reasons.Add(FileUseReason.ModuleLimit); omitted++; break; }
                                if (!Charge(module)) break;
                                if (Matches(module)) { resource = module; break; }
                            }
                        }
                        catch (Exception error) when (error is not OperationCanceledException) { Failure(error); }
                        var match = "LoadedModule";
                        if (resource is null)
                        {
                            match = "CommandLineCandidate";
                            resource = metadata.Arguments?.Split('\0', StringSplitOptions.RemoveEmptyEntries)
                                .Take(256).FirstOrDefault(Matches);
                        }
                        if (resource is not null)
                        {
                            observed.Add(pid);
                            Enrich(new(pid, metadata.StartFileTime, metadata.Name), match, resource, metadata);
                        }
                    }
                    catch (Exception error) when (error is not OperationCanceledException) { Failure(error); }
                }
            }
            catch (Exception error) when (error is not OperationCanceledException) { Failure(error); }
        }
        return Result(owners.Count > 0 ? FileUseStatus.Observed : FileUseStatus.Partial);

        bool Matches(string path)
        {
            try
            {
                if (path.Length > 8192 || string.IsNullOrWhiteSpace(path)) return false;
                var full = Path.GetFullPath(path);
                return files.Any(file => string.Equals(Path.GetFullPath(file), full, StringComparison.OrdinalIgnoreCase)) ||
                    directories.Any(dir => full.StartsWith(Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                        Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }
    }

    private static DateTimeOffset? SafeStart(long fileTime)
    { try { return DateTime.FromFileTimeUtc(fileTime); } catch { return null; } }
}
