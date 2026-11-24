namespace Antiphon.Card0490.NativeHarness;

public sealed record MethodProjectPair(string Project, string ClassName, string Method);

public sealed record CommissionedBinding(string O, string L, string Task, string Creation, string ExecutionBinding, string SourceRoot);

public sealed record NativeRefusal(int ExitCode, string Message);

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

    public static readonly string[] Pcs = ["PC-28", "PC-29", "PC-30", "PC-31"];

    public static readonly string[] Phases = ["baseline", "red", "green"];

    public static readonly string[] RequiredValues = ["-SnapshotRoot", "-AssetProfile", "-Pc", "-Phase", "-EvidenceRoot"];

    private static readonly string[] Flags = [.. RequiredValues, "-BindingFile", "-Ordinary"];

    public static bool OrdinaryAndBindingConflict(IEnumerable<string> args) =>
        args.Contains("-Ordinary", StringComparer.OrdinalIgnoreCase)
        && args.Contains("-BindingFile", StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The CARD-0490 D-12 invocation contract, one PC and phase per invocation (scripts/test-card0490-native.ps1 checked it
    /// until unit 39 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md; the harness is run directly now):
    /// -SnapshotRoot, -AssetProfile, -Pc (PC-28 to PC-31), -Phase (baseline, red, green) and -EvidenceRoot each take a
    /// value, then -Ordinary or -BindingFile &lt;file&gt;, never both. Null admits; otherwise the exit code (3 a sourced run
    /// that would downgrade to ordinary, 2 usage) and the one line to print. Names and set values match ignoring case.
    /// </summary>
    public static NativeRefusal? Admit(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
            return new NativeRefusal(2, "Antiphon.Card0490.NativeHarness: pass -Ordinary or sourced arguments (-BindingFile <file>) with "
                + "-SnapshotRoot, -AssetProfile, -Pc, -Phase and -EvidenceRoot.");
        if (OrdinaryAndBindingConflict(args))
            return new NativeRefusal(3, "Sourced refusal never downgrades to ordinary.");
        foreach (var name in RequiredValues)
        {
            if (string.IsNullOrWhiteSpace(Value(args, name)))
                return new NativeRefusal(2, name + " requires a value.");
        }
        if (!Pcs.Contains(Value(args, "-Pc"), StringComparer.OrdinalIgnoreCase))
            return new NativeRefusal(2, "-Pc must be one of " + string.Join(", ", Pcs) + ".");
        if (!Phases.Contains(Value(args, "-Phase"), StringComparer.OrdinalIgnoreCase))
            return new NativeRefusal(2, "-Phase must be one of " + string.Join(", ", Phases) + ".");
        if (args.Contains("-BindingFile", StringComparer.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(Value(args, "-BindingFile")))
            return new NativeRefusal(2, "-BindingFile requires a value.");
        if (!args.Contains("-Ordinary", StringComparer.OrdinalIgnoreCase) && !args.Contains("-BindingFile", StringComparer.OrdinalIgnoreCase))
            return new NativeRefusal(2, "Sourced execution requires BindingFile.");
        return null;
    }

    /// <summary>The argument after the named flag; null when the flag is absent, last, or followed by another flag of the contract.</summary>
    public static string? Value(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return Flags.Contains(args[i + 1], StringComparer.OrdinalIgnoreCase) ? null : args[i + 1];
        }

        return null;
    }

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
