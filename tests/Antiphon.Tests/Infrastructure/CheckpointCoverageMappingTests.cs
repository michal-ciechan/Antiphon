using Antiphon.Checkpoints;
using Antiphon.HostCleanup.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Unit")]
public sealed class CheckpointCoverageMappingTests
{
    private const string Mapping = ".msCoverageExtensionSourceRootsMapping_Antiphon.E2E";
    private const string First = "Antiphon.Server|C:/src/server/\n";
    private const string Next = "Antiphon.Contracts|C:/src/contracts/\n";

    private static (HostCleanupCheckpointFixture Files, string Path) Build()
    {
        var f = new HostCleanupCheckpointFixture();
        f.Io.Nodes.Remove(f.Payload);
        f.Io.Platform = CheckpointPlatform.Windows;
        var path = Path.Combine(f.Root, Mapping);
        SafeWrite.Require(f.Write.Text(path, First, f.Write.Under(f.Root)));
        f.Clock.Advance(TimeSpan.FromMilliseconds(100));
        Append(f, path, Next);
        return (f, path);
    }

    private static void Append(HostCleanupCheckpointFixture f, string path, string value)
    {
        // The shared fixture's AppendText replaces its node; a build append must retain identity.
        using var stream = f.Io.Open(path, FileMode.Append, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream);
        writer.Write(value);
    }

    [Test]
    public void Build_append_is_sealed_and_green_output_is_removed()
    {
        var (f, path) = Build();
        f.Write.MatchesCreation(path).ShouldBeFalse("the initial creation hash predates the build append");
        f.Ledger.Seal(f.Root, f.Exit).ShouldBeNull();
        f.Ledger.Check(f.Root).ShouldBeNull("completed build mapping must have final custody");
        f.Ledger.Remove(f.Root).ShouldBeNull();
        f.Io.DirectoryExists(f.Root).ShouldBeFalse();
    }

    [Test]
    [Arguments("changed")]
    [Arguments("added")]
    [Arguments("replaced")]
    [Arguments("link")]
    [Arguments("hardlink")]
    [Arguments("tracked")]
    public void Mapping_append_does_not_authorize_unrelated_entries(string variant)
    {
        var (f, _) = Build();
        var unrelated = Path.Combine(f.Root, "unrelated.txt");
        SafeWrite.Require(f.Write.Text(unrelated, "ORIGINAL", f.Write.Under(f.Root)));
        if (variant == "changed") Append(f, unrelated, "CHANGED");
        if (variant == "replaced") f.Io.Put(unrelated, "REPLACED");
        if (variant == "link") f.Io.Nodes[unrelated].Target = Path.Combine(f.Parent, "outside");
        if (variant == "hardlink") f.Io.Nodes[unrelated].Stat = f.Io.Nodes[unrelated].Stat with { Links = 2 };
        f.Ledger.Seal(f.Root, f.Exit);
        if (variant == "added") f.Io.Put(Path.Combine(f.Root, "added.txt"), "USER");
        if (variant == "tracked") f.Io.GitResult = _ => new(0, unrelated + "\0", "", false);
        f.Ledger.Remove(f.Root).ShouldNotBeNull(variant);
        f.Io.FileExists(unrelated).ShouldBeTrue(variant);
        f.Io.Count("unlink").ShouldBe(0, variant);
    }

    [Test]
    [Arguments("rewrite")]
    [Arguments("replace")]
    [Arguments("late")]
    [Arguments("no-exit")]
    [Arguments("after-seal")]
    [Arguments("reseal")]
    public void Mapping_authority_rejects_non_build_appends(string variant)
    {
        var (f, path) = Build();
        var exit = f.Exit;
        if (variant == "rewrite") f.Io.Nodes[path].Bytes[0] = (byte)'X';
        if (variant == "replace") f.Io.Put(path, First + Next);
        if (variant == "late") { f.Clock.Advance(TimeSpan.FromSeconds(2)); Append(f, path, Next); }
        f.Ledger.Seal(f.Root, variant == "no-exit" ? null : exit).ShouldBeNull();
        if (variant is "after-seal" or "reseal")
        {
            f.Clock.Advance(TimeSpan.FromSeconds(2));
            Append(f, path, Next);
            if (variant == "reseal") f.Ledger.Seal(f.Root, f.Exit).ShouldBeNull();
        }
        f.Ledger.Remove(f.Root).ShouldNotBeNull(variant);
        f.Io.FileExists(path).ShouldBeTrue(variant);
        f.Io.Count("unlink").ShouldBe(0, variant);
    }
}
