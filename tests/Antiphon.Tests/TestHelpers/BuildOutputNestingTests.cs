using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

// CARD-1238: under the checkpoint tool's one absolute OutputPath every helper project built into
// this assembly's own output, so each Copy* target in Antiphon.Tests.csproj copied the whole output
// (with the earlier helper copies) into the next helper folder: 97,818 files, 38 GB.
[Category("Unit")]
public sealed class BuildOutputNestingTests
{
    // Helper folders Antiphon.Tests.csproj stages beside this assembly.
    internal static readonly string[] HelperFolders =
    [
        "nightly-owned-child", "custody-child", "script-harness-host", "channel-outbound-probe",
        "checkpoint-lifecycle-host", "checkpoint-filter-host", "checkpoint-repeat-host", "timing-probe",
        "fakeclaude", "fakegrok", "fake-native-claude", "fake-native-grok", "ptyhost-native",
    ];

    // Measured 2026-10-14 on server2: 1,468 files for a relative OutputPath build, 2,228 for the
    // checkpoint tool's layout (artifacts/ included) and 97,673 when nested.
    internal const int FileCeiling = 10_000;

    [Test]
    public void Own_output_has_no_nested_helper_copy()
    {
        var found = BuildOutputNesting.Find(AppContext.BaseDirectory, HelperFolders);
        found.Count.ShouldBe(0, string.Join(Environment.NewLine, found.Take(20)));
    }

    [Test]
    [Arguments("nightly-owned-child", "OwnedChild")]
    [Arguments("custody-child", "Antiphon.CustodyTestChild")]
    [Arguments("script-harness-host", "Antiphon.ScriptHarnessHost")]
    [Arguments("channel-outbound-probe", "Antiphon.ChannelOutbound.Probe")]
    [Arguments("checkpoint-lifecycle-host", "Antiphon.Checkpoints.LifecycleHost")]
    [Arguments("checkpoint-filter-host", "Antiphon.Checkpoints.FilterHost")]
    [Arguments("checkpoint-repeat-host", "Antiphon.Checkpoints.RepeatHost")]
    [Arguments("timing-probe", "Antiphon.TestTiming.Probe")]
    [Arguments("fakeclaude", "fakeclaude")]
    [Arguments("fakegrok", "fakegrok")]
    public void Own_output_stages_helper_program(string folder, string assembly)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, folder);
        File.Exists(Path.Combine(dir, assembly + ".dll")).ShouldBeTrue(dir);
        File.Exists(Path.Combine(dir, assembly + ".runtimeconfig.json")).ShouldBeTrue(dir);
    }

    [Test]
    public void Own_output_stays_under_file_ceiling()
    {
        var count = Directory.EnumerateFiles(AppContext.BaseDirectory, "*", SearchOption.AllDirectories).Count();
        count.ShouldBeLessThan(FileCeiling, $"{AppContext.BaseDirectory} holds {count} files");
    }

    [Test]
    public void Finds_test_assembly_and_helper_copied_into_helper_folder()
    {
        var root = Directory.CreateTempSubdirectory("c1238-nest-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "Antiphon.Tests.dll"), "x");
            Directory.CreateDirectory(Path.Combine(root, "fakeclaude", "script-harness-host"));
            File.WriteAllText(Path.Combine(root, "fakeclaude", "Antiphon.Tests.dll"), "x");

            var found = BuildOutputNesting.Find(root, HelperFolders);

            found.ShouldBe(
            [
                Path.Combine("fakeclaude", "Antiphon.Tests.dll"),
                Path.Combine("fakeclaude", "script-harness-host"),
            ], ignoreOrder: true);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Accepts_helper_folders_side_by_side()
    {
        var root = Directory.CreateTempSubdirectory("c1238-flat-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "Antiphon.Tests.dll"), "x");
            Directory.CreateDirectory(Path.Combine(root, "fakeclaude", "runtimes"));
            Directory.CreateDirectory(Path.Combine(root, "script-harness-host"));

            BuildOutputNesting.Find(root, HelperFolders).ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

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
