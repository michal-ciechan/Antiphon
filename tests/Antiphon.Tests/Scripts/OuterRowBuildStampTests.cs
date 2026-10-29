using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Timeout(60_000)]
[Category("Unit")]
public sealed class OuterRowBuildStampTests
{
    [Test]
    public void C1050_Absent_qualification_stamp_is_accepted_by_child_source_check()
    {
        using var world = QualificationOutput.Create();
        var binding = OuterRowBuildStamp.EnsureForBuildOutput(world.AssemblyDirectory, world.Root, QualificationOutput.CleanSource);
        binding.Check().ShouldBe("verified", "c1050-child-source: outer row build stamp required");
        File.ReadAllText(binding.PathName).ShouldBe(JsonSerializer.Serialize(binding, CheckpointApp.Json),
            "c1050-child-source: stamp bytes are CheckpointBuildBinding.Write");
    }

    [Test]
    public void C1050_Corrupt_or_stale_stamp_is_refused_without_replacement()
    {
        foreach (var (field, value) in new[] { ("commit", "\"" + new string('b', 40) + "\""), ("version", "2"),
                     ("fingerprint", "\"" + new string('2', 64) + "\""), ("sourceState", "\"dirty\"") })
        {
            using var world = QualificationOutput.Create();
            var written = OuterRowBuildStamp.EnsureForBuildOutput(world.AssemblyDirectory, world.Root, QualificationOutput.CleanSource);
            written.Check().ShouldBe("verified", "c1050-child-source: fixture stamp before " + field);
            var json = JsonNode.Parse(File.ReadAllText(written.PathName))!;
            json[field] = JsonNode.Parse(value);
            var corrupt = json.ToJsonString();
            File.WriteAllText(written.PathName, corrupt);
            var again = OuterRowBuildStamp.EnsureForBuildOutput(world.AssemblyDirectory, world.Root, QualificationOutput.CleanSource);
            File.ReadAllText(again.PathName).ShouldBe(corrupt, "c1050-child-source: " + field + " stamp is not replaced");
            again.Check().ShouldBe("mismatch", "c1050-child-source: " + field + " stamp refused");
        }
    }

    [Test]
    public void C1050_Row_built_stamp_stays_required_and_is_not_rewritten()
    {
        using var world = QualificationOutput.CreateWithoutAssemblyDirectory();
        var binding = OuterRowBuildStamp.Binding(world.Root, world.AssemblyDirectory, QualificationOutput.CleanSource);
        SafeWrite.CreateFreshDirectory(binding.OutputPath, SafeWrite.Under(binding.OutputPath)).ShouldBeNull("c1050-child-source: row output");
        binding.Write().ShouldBeNull("c1050-child-source: row stamp");
        var before = File.ReadAllBytes(binding.PathName);
        var ensured = OuterRowBuildStamp.EnsureForBuildOutput(world.AssemblyDirectory, world.Root, QualificationOutput.CleanSource);
        ensured.Check().ShouldBe("verified", "c1050-child-source: row stamp accepted");
        File.ReadAllBytes(ensured.PathName).ShouldBe(before, "c1050-child-source: row stamp is not rewritten");
        File.Delete(ensured.PathName);
        Should.Throw<ShouldAssertException>(() => OuterRowBuildStamp.AssertPresent(ensured), "c1050-child-source: row stamp still required")
            .Message.ShouldContain("c1050-child-source: outer row build stamp required");
    }

    private sealed class QualificationOutput : IDisposable
    {
        internal string Root { get; }
        internal string AssemblyDirectory { get; }
        internal static SourceObservation CleanSource { get; } = new(new string('a', 40), 0, new string('1', 64),
            new DateTimeOffset(2026, 10, 29, 0, 0, 0, TimeSpan.Zero), "known");

        private QualificationOutput(string root, string assemblyDirectory)
        {
            Root = root;
            AssemblyDirectory = assemblyDirectory;
        }

        internal static QualificationOutput Create()
        {
            var world = CreateWithoutAssemblyDirectory();
            Directory.CreateDirectory(world.AssemblyDirectory);
            File.WriteAllText(Path.Combine(world.AssemblyDirectory, "Antiphon.Tests.dll"), "qualification-build");
            return world;
        }

        internal static QualificationOutput CreateWithoutAssemblyDirectory()
        {
            var root = Directory.CreateTempSubdirectory("c1050-qual-").FullName;
            var assembly = Path.Combine(root, "tests", "Antiphon.Tests", "bin", "Debug", "net9.0");
            Directory.CreateDirectory(Path.GetDirectoryName(assembly)!);
            return new QualificationOutput(root, assembly);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
