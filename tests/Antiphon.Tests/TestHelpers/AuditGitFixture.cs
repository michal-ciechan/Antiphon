namespace Antiphon.Tests.TestHelpers;

// The path can only originate from this fixture's own temp-directory creation.
internal sealed class AuditGitFixture
{
    public string Root { get; } = Directory.CreateTempSubdirectory("antiphon-audit-").FullName;

    public static implicit operator string(AuditGitFixture fixture) => fixture.Root;

    public async Task RunAsync(Func<Task> body)
    {
        Exception? bodyError = null;
        try { await body(); }
        catch (Exception error) { bodyError = error; throw; }
        finally
        {
            try { DeleteOwnedRoot(); }
            catch (Exception cleanupError) when (bodyError is not null)
            {
                throw new AggregateException("Audit body and owned-root cleanup both failed.", bodyError, cleanupError);
            }
        }
    }

    private void DeleteOwnedRoot()
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        var full = Path.GetFullPath(Root);
        if (!string.Equals(Path.GetDirectoryName(full), temp, comparison)
            || !Path.GetFileName(full).StartsWith("antiphon-audit-", StringComparison.Ordinal))
            throw new IOException("Audit cleanup escaped its owned temp root.");

        // Check the complete tree before changing any attributes. A replaced root, directory
        // junction or file link must never let attribute normalization reach an outside target.
        for (var ancestor = new DirectoryInfo(full); ancestor is not null; ancestor = ancestor.Parent)
            RefuseLink(ancestor.FullName);
        InspectTree(full);
        ReadOnlyDirectory.Delete(full);
    }

    private static void RefuseLink(string path)
    {
        if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("Audit cleanup refuses a linked path: " + path);
    }

    private static void InspectTree(string directory)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory, "*", new EnumerationOptions
        {
            AttributesToSkip = 0,
            RecurseSubdirectories = false,
            IgnoreInaccessible = false,
        }))
        {
            RefuseLink(entry);
            if (File.GetAttributes(entry).HasFlag(FileAttributes.Directory)) InspectTree(entry);
        }
    }
}
