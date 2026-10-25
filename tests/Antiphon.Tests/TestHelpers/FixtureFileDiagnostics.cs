using System.Security.AccessControl;

namespace Antiphon.Tests.TestHelpers;

/// <summary>Read-only metadata for fixture failures; never retries or changes deletion authority.</summary>
internal static class FixtureFileDiagnostics
{
    public static void Write(string path, Exception? failure = null)
    {
        try
        {
            Console.WriteLine($"FIXTURE_FILE pid={Environment.ProcessId} path={path} chars={path.Length} failure={failure}");
            if (!Path.Exists(path)) return;
            var attributes = File.GetAttributes(path);
            FileSystemInfo entry = (attributes & FileAttributes.Directory) != 0
                ? new DirectoryInfo(path) : new FileInfo(path);
            Console.WriteLine($"FIXTURE_FILE attributes={attributes} created={entry.CreationTimeUtc:O} modified={entry.LastWriteTimeUtc:O} link={entry.LinkTarget}");
            if (entry is FileInfo file) Console.WriteLine($"FIXTURE_FILE length={file.Length}");
            if (OperatingSystem.IsWindows())
            {
                FileSystemSecurity acl = entry is DirectoryInfo directory ? directory.GetAccessControl() : ((FileInfo)entry).GetAccessControl();
                Console.WriteLine("FIXTURE_FILE acl=" + acl.GetSecurityDescriptorSddlForm(AccessControlSections.Access));
            }
        }
        catch (Exception ex)
        {
            // Diagnostics must preserve the original refusal/assertion, including inaccessible paths.
            Console.WriteLine($"FIXTURE_FILE metadata-unavailable path={path} reason={ex.GetType().Name} hresult={ex.HResult:X8}");
        }
    }

    public static void WriteLandingLocks(string root, Exception failure)
    {
        Write(root, failure);
        try
        {
            var pending = new Stack<string>();
            pending.Push(root);
            var count = 0;
            while (pending.TryPop(out var directory) && count < 4096)
            {
                foreach (var path in Directory.EnumerateFileSystemEntries(directory))
                {
                    if (++count > 4096) break;
                    var attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if (Path.GetFileName(path) == "landing.lock") Write(path);
                    if ((attributes & FileAttributes.Directory) != 0) pending.Push(path);
                }
            }
            Console.WriteLine($"FIXTURE_FILE inventoryEntries={count} truncated={pending.Count > 0 || count > 4096}; holder identity requires native Handle capture while residue exists");
        }
        catch (Exception ex) { Console.WriteLine("FIXTURE_FILE lock-inventory-unavailable: " + ex.Message); }
    }
}
