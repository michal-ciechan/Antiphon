namespace Antiphon.Tests.TestHelpers;

internal static class GitFixtureCleanup
{
    // Git creates read-only loose objects on Windows. Teardown must not replace
    // the result of the test body with an attribute-related delete failure.
    public static void Delete(string root)
    {
        try
        {
            if (!Directory.Exists(root)) return;
            ClearReadOnly(root);
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                ClearReadOnly(file);
            foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
                ClearReadOnly(directory);
            Directory.Delete(root, recursive: true);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Git fixture cleanup failed for {root}: {error}");
        }
    }

    private static void ClearReadOnly(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReadOnly) != 0)
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
    }
}
