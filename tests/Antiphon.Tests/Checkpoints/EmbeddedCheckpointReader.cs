using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Shouldly;

namespace Antiphon.Tests.Checkpoints;

/// <summary>
/// The ConfinedFileReader that scripts/lib/checkpoint-file-reader.ps1 embeds (its $confinedReaderSource here-string),
/// compiled here by Roslyn as Add-Type compiles it there: that source alone, against the runtime's framework
/// assemblies. The script library keeps the reader embedded (it reads every other checkpoint .cs file through it),
/// so its witnesses compile the same text instead of running PowerShell.
/// </summary>
internal static class EmbeddedCheckpointReader
{
    internal const string TypeName = "Antiphon.Checkpoints.Coverage.ConfinedFileReader";

    internal static string LibraryPath => Path.Combine(CheckpointFixtures.RepoRoot, "scripts/lib/checkpoint-file-reader.ps1");

    internal static string Source()
    {
        var match = Regex.Match(File.ReadAllText(LibraryPath), @"\$confinedReaderSource = @'\r?\n(.*?)\r?\n'@", RegexOptions.Singleline);
        match.Success.ShouldBeTrue("the embedded reader here-string is missing from " + LibraryPath);
        return match.Groups[1].Value;
    }

    /// <summary>Compiles the embedded reader into directory/ConfinedFileReader.dll and returns that path.</summary>
    internal static string EmitTo(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "ConfinedFileReader.dll");
        var compilation = CSharpCompilation.Create(
            "ConfinedFileReader.Embedded",
            [CSharpSyntaxTree.ParseText(Source(), new CSharpParseOptions(LanguageVersion.Latest))],
            FrameworkReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using (var stream = File.Create(path))
        {
            var emit = compilation.Emit(stream);
            emit.Success.ShouldBeTrue(string.Join("\n", emit.Diagnostics));
        }
        return path;
    }

    // The trusted platform assemblies under the runtime directory: the framework only, never the test's own
    // Antiphon assemblies (the compiled facade declares the same type name).
    private static List<MetadataReference> FrameworkReferences()
    {
        var runtime = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(path => string.Equals(Path.GetDirectoryName(path), runtime, comparison))
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
    }
}
