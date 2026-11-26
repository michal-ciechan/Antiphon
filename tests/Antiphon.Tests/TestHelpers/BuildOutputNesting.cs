namespace Antiphon.Tests.TestHelpers;

internal static class BuildOutputNesting
{
    // Returns paths, relative to root, of a helper folder or a copy of this test assembly found
    // inside a helper folder.
    public static List<string> Find(string root, IReadOnlyCollection<string> helperFolders)
    {
        var found = new List<string>();
        foreach (var helper in helperFolders)
        {
            var dir = Path.Combine(root, helper);
            if (!Directory.Exists(dir))
                continue;
            foreach (var entry in Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(entry);
                var nested = Directory.Exists(entry)
                    ? helperFolders.Contains(name, StringComparer.OrdinalIgnoreCase)
                    : name.Equals("Antiphon.Tests.dll", StringComparison.OrdinalIgnoreCase);
                if (nested)
                    found.Add(Path.GetRelativePath(root, entry));
            }
        }

        return found;
    }
}
