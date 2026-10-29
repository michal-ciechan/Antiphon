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
            var root = Directory.CreateTempSubdirectory("c1050-qual-").FullName;
            var assembly = Path.Combine(root, "tests", "Antiphon.Tests", "bin", "Debug", "net9.0");
            Directory.CreateDirectory(assembly);
            File.WriteAllText(Path.Combine(assembly, "Antiphon.Tests.dll"), "qualification-build");
            return new QualificationOutput(root, assembly);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
