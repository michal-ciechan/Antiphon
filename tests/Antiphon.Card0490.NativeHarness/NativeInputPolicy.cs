namespace Antiphon.Card0490.NativeHarness;

public sealed record MethodProjectPair(string Project, string ClassName, string Method);

public sealed record CommissionedBinding(string O, string L, string Task, string Creation, string ExecutionBinding, string SourceRoot);

public static class NativeInputPolicy
{
    public static readonly MethodProjectPair[] Allowed =
    [
        new("tests/Antiphon.PtyHost.Tests", "ShadowCopyStoreTests", "Linux_copy_preserves_execute_mode"),
        new("tests/Antiphon.PtyHost.Tests", "LinuxPtyHostLauncherTests", "Detach_creates_a_new_session"),
        new("tests/Antiphon.PtyHost.Tests", "LinuxPtyHostLauncherTests", "Intermediary_pipes_reach_eof_while_host_lives"),
        new("tests/Antiphon.PtyHost.Tests", "LinuxPtyHostLauncherTests", "Canceled_launch_leaves_no_owned_host"),
    ];

    public static readonly string[] AllowedFixtureProbes =
    [
        "unlisted-method", "changed-digest", "unqualified-toolchain", "non-ext4", "prior-workspace", "seeded-output"
    ];

    public static bool AllowMethod(string project, string filter)
    {
        foreach (var pair in Allowed)
        {
            if (filter.Contains(pair.ClassName, StringComparison.Ordinal)
                && filter.Contains(pair.Method, StringComparison.Ordinal)
                && project.Contains(pair.Project, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    public static bool AllowFixtureProbe(string probeCase) =>
        AllowedFixtureProbes.Contains(probeCase, StringComparer.Ordinal);

    public static bool BindingEquals(CommissionedBinding expected, CommissionedBinding actual) =>
        expected == actual;

    public static bool IsCleanCommissioning(bool dirtyWorktree, bool dirtyIndex) =>
        !dirtyWorktree && !dirtyIndex;

    public static bool PackageIsWorkingBytes(ReadOnlySpan<byte> packaged, ReadOnlySpan<byte> working) =>
        packaged.SequenceEqual(working);

    public static bool PathsAreTrackedOnly(IReadOnlyCollection<string> actual, IReadOnlyCollection<string> expected) =>
        actual.Count == expected.Count && actual.All(expected.Contains);

    public static bool DigestsMatch(string left, string right) =>
        string.Equals(left, right, StringComparison.Ordinal);

    public static bool AssetMapEquals(IReadOnlyDictionary<string, string> expected, IReadOnlyDictionary<string, string> actual) =>
        expected.Count == actual.Count && expected.All(kv => actual.TryGetValue(kv.Key, out var v) && v == kv.Value);

    public static bool RestoredEqualsBaseline(IReadOnlyDictionary<string, string> baseline, IReadOnlyDictionary<string, string> restored) =>
        AssetMapEquals(baseline, restored);

    public static bool ProductCycleHarnessUnchanged(IReadOnlyCollection<string> changedPaths) =>
        !changedPaths.Any(p => p.Contains("Tests", StringComparison.OrdinalIgnoreCase)
            || p.Contains("NativeHarness", StringComparison.OrdinalIgnoreCase)
            || p.Contains("guest-init", StringComparison.OrdinalIgnoreCase));
}
