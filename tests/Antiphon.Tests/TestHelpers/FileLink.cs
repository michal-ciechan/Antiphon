namespace Antiphon.Tests.TestHelpers;

internal sealed class FileLink : IDisposable
{
    internal const string PrivilegeReason =
        "CARD-1210: file-link controls require symbolic-link creation privilege on Windows; capability unavailable.";

    internal string Path { get; }

    private FileLink(string path) => Path = path;

    internal static FileLink? TryCreate(string path, string target) =>
        TryCreate(path, target, OperatingSystem.IsWindows(),
            (link, destination) => File.CreateSymbolicLink(link, destination));

    internal static FileLink? TryCreate(string path, string target, bool isWindows,
        Action<string, string> create)
    {
        try { create(path, target); }
        catch (Exception error) when (isWindows && (error is UnauthorizedAccessException { HResult: unchecked((int)0x80070522) }
            || error is System.ComponentModel.Win32Exception { NativeErrorCode: 1314 }
            || error is IOException { HResult: unchecked((int)0x80070522) }))
        {
            return null;
        }
        if (!File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("The file-link fixture did not create a reparse point.");
        return new FileLink(path);
    }

    public void Dispose() => File.Delete(Path);
}
