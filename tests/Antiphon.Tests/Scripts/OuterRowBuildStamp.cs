using Antiphon.Checkpoints;
using Shouldly;

namespace Antiphon.Tests.Scripts;

// Qualification runs plain `dotnet build` and never writes checkpoint-build-source.json.
// A checkpoint row does. C1050 and C1210 both require that stamp for the output they execute.
internal static class OuterRowBuildStamp
{
    public static string OutputDirectory(string baseDirectory)
    {
        var output = CheckpointBuildBinding.NormalizeDirectory(baseDirectory);
        if (File.Exists(Path.Combine(output, "checkpoint-build-source.json"))) return output;
        var parent = Directory.GetParent(output);
        if (parent is not null && File.Exists(Path.Combine(parent.FullName, "checkpoint-build-source.json")))
            return CheckpointBuildBinding.NormalizeDirectory(parent.FullName);
        return output;
    }

    public static CheckpointBuildBinding Binding(string repositoryRoot, string outputDirectory, SourceObservation source) =>
        CheckpointBuildBinding.Expected(repositoryRoot, "tests/Antiphon.Tests",
            Path.GetRelativePath(Path.Combine(repositoryRoot, "tests/Antiphon.Tests"), outputDirectory), [], source);

    public static CheckpointBuildBinding EnsureForBuildOutput(string baseDirectory, string repositoryRoot, SourceObservation source)
    {
        var outputDirectory = OutputDirectory(baseDirectory);
        var binding = Binding(repositoryRoot, outputDirectory, source);
        // A row-built stamp stays where the runner wrote it, including a corrupt or stale one.
        // Qualification output has none: CheckpointBuildBinding.Write records this build.
        if (!File.Exists(binding.PathName))
            WriteMissing(binding);
        else if (binding.Check() != "verified")
        {
            // RunScheduler adds the canonical shared layout. Native audits also permit
            // its explicit apphost build. Reconstruct both supported property sets from
            // our paths; never adopt arbitrary properties from the stamp being checked.
            var output = Path.GetRelativePath(Path.Combine(repositoryRoot, "tests", "Antiphon.Tests"), outputDirectory);
            foreach (var properties in new[] { new List<string>(), new List<string> { "--property:UseAppHost=true" } })
            {
                BuildStep.AppendSharedLayout(properties, repositoryRoot, "tests/Antiphon.Tests", output);
                var shared = CheckpointBuildBinding.Expected(repositoryRoot, "tests/Antiphon.Tests", output, properties, source);
                if (shared.Check() == "verified")
                    return shared;
            }
        }
        AssertPresent(binding);
        return binding;
    }

    // Write() seals the output and returns output-owner-missing unless the owner file
    // CreateFreshDirectory leaves is already present. A qualification directory already
    // exists, so that fresh-directory call cannot mint the owner. The owner text is the
    // same SafeWrite.Text call; the stamp bytes come only from CheckpointBuildBinding.Write.
    private static void WriteMissing(CheckpointBuildBinding binding)
    {
        var owner = Path.Combine(binding.OutputPath, ".checkpoint-output-owner.json");
        if (!File.Exists(owner))
        {
            var owned = SafeWrite.Text(owner, Guid.NewGuid().ToString("N"), SafeWrite.Under(binding.OutputPath));
            owned.ShouldBeNull("c1050-child-source: checkpoint output owner");
        }
        var written = binding.Write();
        if (written == "pre-existing-build-source") return;
        written.ShouldBeNull("c1050-child-source: checkpoint build stamp writer");
    }

    public static void AssertPresent(CheckpointBuildBinding binding) =>
        File.Exists(binding.PathName).ShouldBeTrue("c1050-child-source: outer row build stamp required");
}
