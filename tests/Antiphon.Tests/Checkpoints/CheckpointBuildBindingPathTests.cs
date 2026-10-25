using System.Text.Json.Nodes;
using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class CheckpointBuildBindingPathTests
{
    [Test]
    public void Producer_stamp_is_accepted_by_reconstructed_audit_path()
    {
        using var fixture = new BindingFixture();
        // RowRunner/RunScheduler produce the stamp from a required forward-slash OutputPath.
        var producer = fixture.Binding("bin-c1210-producer/");
        fixture.CreateOutput(producer);
        producer.Write().ShouldBeNull("c1210-fresh-producer-stamp");
        // The Windows child audit reconstructs the same directory without its separator.
        var output = Path.GetDirectoryName(producer.PathName)!;
        var audit = fixture.Binding(Path.GetRelativePath(Path.Combine(fixture.Root, "tests"), output));
        Console.WriteLine("producer=" + File.ReadAllText(producer.PathName));
        Console.WriteLine("audit output=" + audit.OutputPath + " verdict=" + audit.Check());
        audit.Check().ShouldBe("verified", "c1210-producer-to-audit-source-stamp");
    }

    [Test]
    public void Equivalent_directory_spellings_preserve_binding()
    {
        foreach (var field in new[] { "repositoryRoot", "project", "outputPath" })
        {
            var variants = new List<Func<string, string>> { original => original,
                original => original + Path.DirectorySeparatorChar,
                original => original + Path.DirectorySeparatorChar + Path.DirectorySeparatorChar,
                original => Path.Combine(original, "."), original => Path.Combine(original, "child", "..") };
            if (OperatingSystem.IsWindows())
                variants.AddRange([original => original.Replace('\\', '/'), original => original.Replace('\\', '/') + "/",
                    original => original + "\\", original => original.ToUpperInvariant()]);
            foreach (var spelling in variants)
            {
                // Each spelling starts from an independently created stamp. Writes are
                // exclusive under CARD-1199, so a prior altered stamp cannot be reset by Write.
                using var fixture = new BindingFixture();
                var binding = fixture.Binding("bin-c1210-producer");
                fixture.CreateOutput(binding);
                binding.Write().ShouldBeNull("c1210-fresh-equivalent-stamp: " + field);
                var stamp = JsonNode.Parse(File.ReadAllText(binding.PathName))!;
                var variant = spelling(stamp[field]!.GetValue<string>());
                stamp[field] = variant;
                File.WriteAllText(binding.PathName, stamp.ToJsonString());
                binding.Check().ShouldBe("verified", "c1210-equivalent-path: " + field + "=" + variant);
            }
        }
        if (!OperatingSystem.IsWindows())
        {
            using var fixture = new BindingFixture();
            var binding = fixture.Binding("bin-c1210-producer");
            fixture.CreateOutput(binding);
            binding.Write().ShouldBeNull("c1210-fresh-case-distinct-stamp");
            var stamp = JsonNode.Parse(File.ReadAllText(binding.PathName))!;
            stamp["outputPath"] = binding.OutputPath.ToUpperInvariant();
            File.WriteAllText(binding.PathName, stamp.ToJsonString());
            binding.Check().ShouldBe("mismatch", "c1210-linux-path-case-is-distinct");
        }
    }

    [Test]
    public void Different_directory_or_provenance_is_refused()
    {
        using (var missing = new BindingFixture())
            missing.Binding("bin-c1210-producer").Check().ShouldBe("unknown", "c1210-missing-stamp");
        foreach (var field in new[] { "repositoryRoot", "project", "outputPath", "commit", "fingerprint",
            "sourceState", "version", "properties" })
        {
            using var fixture = new BindingFixture();
            var binding = fixture.Binding("bin-c1210-producer");
            fixture.CreateOutput(binding);
            JsonNode? value = field switch
            {
                "repositoryRoot" or "project" => JsonValue.Create(Path.Combine(fixture.Root, "different")),
                "outputPath" => JsonValue.Create(binding.OutputPath + "-different/"),
                "commit" => JsonValue.Create(new string('b', 40)),
                "fingerprint" => JsonValue.Create(new string('2', 64)),
                "sourceState" => JsonValue.Create("dirty"),
                "version" => JsonValue.Create(2),
                "properties" => new JsonArray("UseAppHost=false"),
                _ => throw new InvalidOperationException(field),
            };
            binding.Write().ShouldBeNull("c1210-fresh-negative-stamp: " + field);
            binding.Check().ShouldBe("verified", "c1210-negative-healthy-companion: " + field);
            var stamp = JsonNode.Parse(File.ReadAllText(binding.PathName))!;
            stamp[field] = value;
            File.WriteAllText(binding.PathName, stamp.ToJsonString());
            binding.Check().ShouldBe("mismatch", "c1210-provenance-refused: " + field);
        }
    }

    private sealed class BindingFixture : IDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory("c1210-binding-").FullName;
        internal CheckpointBuildBinding Binding(string output) => CheckpointBuildBinding.Expected(Root, "tests", output, [],
            new SourceObservation(new string('a', 40), 0, new string('1', 64), DateTimeOffset.UtcNow, "known"));
        internal void CreateOutput(CheckpointBuildBinding binding)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(binding.OutputPath))!);
            SafeWrite.Require(SafeWrite.CreateFreshDirectory(binding.OutputPath, SafeWrite.Under(binding.OutputPath)));
        }
        public void Dispose()
        {
            CheckpointNativeDiagnostics.Capture(nameof(CheckpointBuildBindingPathTests), [Root]);
            Directory.Delete(Root, recursive: true);
        }
    }
}
