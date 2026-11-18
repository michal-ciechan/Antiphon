using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1981. One owned scratch directory under the temp root. Callers release it in
/// finally or Dispose. A process-exit backstop deletes anything still tracked when the
/// test host exits. The temp sweep registers <c>antiphon-owned-</c> and stays off.
/// </summary>
internal static class OwnedTempRoot
{
    internal const string OwnedPrefix = "antiphon-owned-";

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> All = new();

    static OwnedTempRoot()
    {
        AppDomain.CurrentDomain.ProcessExit += static (_, _) => ReleaseAll();
    }

    internal static string CreateOwned()
    {
        var path = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), OwnedPrefix + Guid.NewGuid().ToString("N"))).FullName;
        Track(path);
        return path;
    }

    internal static void Track(string path)
    {
        if (!string.IsNullOrEmpty(path))
            All[path] = 1;
    }

    internal static void Release(string path)
    {
        All.TryRemove(path, out _);
        TryDelete(path);
    }

    /// <summary>Deletes every directory still tracked. The assembly hook calls this after every test.</summary>
    internal static void ReleaseAll()
    {
        foreach (var path in All.Keys)
        {
            All.TryRemove(path, out _);
            TryDelete(path);
        }
    }

    internal static void TryDelete(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path))
                return;
            var full = Path.GetFullPath(path);
            var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()))
                + Path.DirectorySeparatorChar;
            if (!full.StartsWith(temp, StringComparison.Ordinal))
                return;
            if (Directory.Exists(full))
                Directory.Delete(full, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>
/// CARD-1981. After every test in the assembly has finished, delete scratch directories
/// that a fixture tracked and did not release. TUnit does not flow AsyncLocal into
/// <c>After(Test)</c>, so this hook is assembly-scoped rather than per test.
/// </summary>
internal static class OwnedTempRootAssemblyHook
{
    [After(Assembly)]
    public static void Release_tracked_temp_roots() => OwnedTempRoot.ReleaseAll();
}
