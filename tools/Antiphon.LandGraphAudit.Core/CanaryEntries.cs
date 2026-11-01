namespace Antiphon.LandGraphAudit;

/// <summary>
/// One ordinal canary path. Git paths stay as Git wrote them. Documentation paths
/// enumerated on disk use the supplied separator and are rewritten to '/'.
/// The production call passes <see cref="Path.DirectorySeparatorChar"/>.
/// </summary>
internal static class CanaryEntries
{
    internal static HashSet<string> Build(IEnumerable<string> tracked, IEnumerable<string> documentation, char separator)
    {
        var files = new HashSet<string>(tracked, StringComparer.Ordinal);
        foreach (var path in documentation)
            files.Add(path.Replace(separator, '/'));
        return files;
    }
}
