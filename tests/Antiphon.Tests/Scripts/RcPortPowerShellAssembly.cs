using System.Reflection;

namespace Antiphon.Tests.Scripts;

internal static class RcPortPowerShellAssembly
{
    internal const string Name = "System.Management.Automation";
    private const string Dll = Name + ".dll";

    internal static Assembly Load()
    {
        var windows = OperatingSystem.IsWindows();
        if (windows)
        {
            // The Windows desktop can run pwsh 7.6 on .NET 10, while this test host uses .NET 9.
            // Prefer the compatible runtime selected by the test project's package reference.
            try { return Assembly.Load(Name); }
            catch (FileNotFoundException) { }
        }

        var searchDirectories = Environment.GetEnvironmentVariable("PATH")?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        var path = Resolve(Environment.GetEnvironmentVariable("PSHOME"), searchDirectories, windows,
            File.Exists, ResolveExecutableLink);
        return Assembly.LoadFrom(Path.GetFullPath(path));
    }

    internal static string Resolve(string? psHome, IEnumerable<string> searchDirectories, bool windows,
        Func<string, bool> fileExists, Func<string, string?> resolveLink)
    {
        var probed = new List<string>();
        var comparer = windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        foreach (var directory in ProbeDirectories(psHome, searchDirectories, windows, fileExists, resolveLink)
                     .Distinct(comparer))
        {
            var path = Path.Combine(directory, Dll);
            probed.Add(path);
            if (fileExists(path)) return path;
        }

        var paths = probed.Count == 0 ? "(none)" : string.Join(", ", probed);
        throw new InvalidOperationException($"RC port corpus could not locate {Dll}. Probed assembly paths: {paths}.");
    }

    private static IEnumerable<string> ProbeDirectories(string? psHome, IEnumerable<string> searchDirectories,
        bool windows, Func<string, bool> fileExists, Func<string, string?> resolveLink)
    {
        if (!string.IsNullOrWhiteSpace(psHome)) yield return psHome;

        var executableName = windows ? "pwsh.exe" : "pwsh";
        foreach (var entry in searchDirectories)
        {
            var directory = windows ? entry.Trim('"') : entry;
            var executable = Path.Combine(directory, executableName);
            if (!fileExists(executable)) continue;
            yield return directory;

            var targetDirectory = Path.GetDirectoryName(resolveLink(executable));
            if (!string.IsNullOrEmpty(targetDirectory)) yield return targetDirectory;
        }

        if (windows) yield break;
        yield return "/opt/microsoft/powershell/7";
        yield return "/opt/powershell";
    }

    private static string? ResolveExecutableLink(string executable)
    {
        try { return File.ResolveLinkTarget(executable, returnFinalTarget: true)?.FullName; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
