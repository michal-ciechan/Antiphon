using System.Text.RegularExpressions;

namespace Antiphon.Checkpoints;

public static class RepeatRequest
{
    public static int Parse(string? value)
    {
        if (!int.TryParse(value, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var count) || count < 1)
            throw new ManifestValidationException("repeat", "--repeat must be a positive integer (total executions per case)");
        return count;
    }

    public static string CanonicalProject(string root, string project)
    {
        var path = Path.GetFullPath(Path.IsPathRooted(project) ? project : Path.Combine(root, project));
        if (Directory.Exists(path))
        {
            var matches = Directory.GetFiles(path, "*.csproj", SearchOption.TopDirectoryOnly);
            if (matches.Length != 1)
                throw new ManifestValidationException("project", $"repeat project '{project}' must resolve to exactly one csproj");
            path = matches[0];
        }
        if (!File.Exists(path) || !path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            throw new ManifestValidationException("project", $"repeat project '{project}' must resolve to one csproj");
        var xml = System.Xml.Linq.XDocument.Load(path);
        if (!xml.Descendants().Any(element => element.Name.LocalName == "PackageReference"
            && string.Equals((string?)element.Attribute("Include"), "TUnit", StringComparison.OrdinalIgnoreCase)))
            throw new ManifestValidationException("project", $"repeat project '{project}' must reference TUnit directly");
        return path;
    }

    public static void RejectReservedProperty(string key, string value)
    {
        if (key.Equals("AntiphonCheckpointRepeat", StringComparison.OrdinalIgnoreCase)
            || key.Equals("AntiphonCheckpointRepeatProject", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(value, @"(?:^|;)\s*AntiphonCheckpointRepeat(?:Project)?\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new ManifestValidationException("msbuildProperty", "AntiphonCheckpointRepeat and AntiphonCheckpointRepeatProject are reserved for --repeat");
    }
}
