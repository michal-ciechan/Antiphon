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

    [Test]
    public void C1210_Shared_checkpoint_layout_is_verified_without_rewriting_the_stamp()
    {
        foreach (var explicitAppHost in new[] { false, true })
        {
            using var world = QualificationOutput.CreateWithoutAssemblyDirectory();
            var properties = new List<string>();
            if (explicitAppHost) properties.Add("--property:UseAppHost=true");
            var output = Path.GetRelativePath(Path.Combine(world.Root, "tests", "Antiphon.Tests"), world.AssemblyDirectory);
            BuildStep.AppendSharedLayout(properties, world.Root, "tests/Antiphon.Tests", output);
            var producer = CheckpointBuildBinding.Expected(world.Root, "tests/Antiphon.Tests", output,
                properties, QualificationOutput.CleanSource);
            SafeWrite.CreateFreshDirectory(producer.OutputPath, SafeWrite.Under(producer.OutputPath)).ShouldBeNull();
            producer.Write().ShouldBeNull();
            var before = File.ReadAllBytes(producer.PathName);

            var audit = OuterRowBuildStamp.EnsureForBuildOutput(world.AssemblyDirectory, world.Root, QualificationOutput.CleanSource);

            audit.Check().ShouldBe("verified", "c1210-shared-layout-source-stamp");
            audit.Properties.ShouldBe(producer.Properties);
            File.ReadAllBytes(producer.PathName).ShouldBe(before, "producer stamp must not be rewritten");
        }
    }

    [Test]
    public void C1210_Shared_layout_rejects_unexpected_properties_without_rewriting()
    {
        foreach (var replacement in new[] { "ArtifactsPath=elsewhere", "UseArtifactsOutput=false", "UseAppHost=false", "Optimize=true" })
        {
            using var world = QualificationOutput.CreateWithoutAssemblyDirectory();
            var properties = new List<string> { "--property:UseAppHost=true" };
            var output = Path.GetRelativePath(Path.Combine(world.Root, "tests", "Antiphon.Tests"), world.AssemblyDirectory);
            BuildStep.AppendSharedLayout(properties, world.Root, "tests/Antiphon.Tests", output);
            var producer = CheckpointBuildBinding.Expected(world.Root, "tests/Antiphon.Tests", output,
                properties, QualificationOutput.CleanSource);
            SafeWrite.CreateFreshDirectory(producer.OutputPath, SafeWrite.Under(producer.OutputPath)).ShouldBeNull();
            producer.Write().ShouldBeNull();
            var key = replacement[..replacement.IndexOf('=')] + "=";
            producer.Properties.RemoveAll(property => property.StartsWith(key, StringComparison.Ordinal));
            producer.Properties.Add(replacement);
            var altered = JsonSerializer.Serialize(producer, CheckpointApp.Json);
            File.WriteAllText(producer.PathName, altered);

            var audit = OuterRowBuildStamp.EnsureForBuildOutput(world.AssemblyDirectory, world.Root, QualificationOutput.CleanSource);

            audit.Check().ShouldBe("mismatch", "c1210-unexpected-property: " + replacement);
            File.ReadAllText(producer.PathName).ShouldBe(altered, "invalid stamp must not be replaced");
        }
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
