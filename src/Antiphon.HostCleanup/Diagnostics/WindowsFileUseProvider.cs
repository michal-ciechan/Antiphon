namespace Antiphon.HostCleanup.Diagnostics;

public sealed record WindowsFileUseProcess(int Pid, long StartFileTime, string Name);
public sealed record WindowsFileUseMetadata(int Pid, long StartFileTime, string Name, string? Executable,
    string? Arguments, int? ParentPid);
public sealed record WindowsFileUseList(int Error, uint Needed, IReadOnlyList<WindowsFileUseProcess> Processes);

// Read-only native boundary. Arguments are an argv vector separated by NUL, never shell text.
public class WindowsFileUseSource
{
    public virtual bool IsSupported => OperatingSystem.IsWindows();
    public virtual string PathKind(string path) => File.Exists(path) ? "file" : Directory.Exists(path) ? "directory" : "absent";
    public virtual IEnumerable<string> Files(string directory) => Directory.EnumerateFiles(directory, "*", new EnumerationOptions
    { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false });
    public virtual int Start(out uint session) { session = 0; return 120; }
    public virtual int Register(uint session, string[] files) => 120;
    public virtual WindowsFileUseList List(uint session, uint capacity) => new(120, 0, []);
    public virtual void End(uint session) { }
    public virtual IEnumerable<int> Processes() => [];
    public virtual WindowsFileUseMetadata Metadata(int pid) => throw new UnauthorizedAccessException();
    public virtual long Generation(int pid) => Metadata(pid).StartFileTime;
    public virtual IEnumerable<string> Modules(int pid) => [];
}

public sealed class WindowsFileUseProvider(WindowsFileUseSource? source = null) : IFileUseProvider
{
    public FileUseObservation Observe(FileUseRequest request, CancellationToken cancellationToken) =>
        new(FileUseStatus.Unavailable, FileUseCoverage.None, [], [FileUseReason.UnsupportedPlatform]);
}
