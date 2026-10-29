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
        var binding = Binding(repositoryRoot, OutputDirectory(baseDirectory), source);
        AssertPresent(binding);
        return binding;
    }

    public static void AssertPresent(CheckpointBuildBinding binding) =>
        File.Exists(binding.PathName).ShouldBeTrue("c1050-child-source: outer row build stamp required");
}
