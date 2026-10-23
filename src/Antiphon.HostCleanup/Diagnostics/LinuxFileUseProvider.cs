namespace Antiphon.HostCleanup.Diagnostics;

public sealed class LinuxFileUseProvider(LinuxFileUseSource? source = null) : IFileUseProvider
{
    public FileUseObservation Observe(FileUseRequest request, CancellationToken cancellationToken) =>
        new(FileUseStatus.Unavailable, FileUseCoverage.None, [], [FileUseReason.UnsupportedPlatform]);
}
public class LinuxFileUseSource
{
    public virtual IEnumerable<int> Processes() => Directory.EnumerateDirectories("/proc").Select(Path.GetFileName).Where(n => int.TryParse(n, out _)).Select(n => int.Parse(n!));
    public virtual IEnumerable<string> Fds(int pid) => Directory.EnumerateFileSystemEntries($"/proc/{pid}/fd");
    public virtual string? Link(string path) => new FileInfo(path).LinkTarget;
    public virtual string Read(string path, int maximumBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var bytes = new byte[maximumBytes + 1]; var count = 0;
        while (count < bytes.Length) { var got = stream.Read(bytes, count, bytes.Length - count); if (got == 0) break; count += got; }
        if (count > maximumBytes) throw new FileUseInputLimitException();
        return System.Text.Encoding.UTF8.GetString(bytes, 0, count);
    }
}
public sealed class FileUseInputLimitException : IOException;
