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
        producer.Write();
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
        using var fixture = new BindingFixture();
        var binding = fixture.Binding("bin-c1210-producer");
        foreach (var field in new[] { "repositoryRoot", "project", "outputPath" })
        {
            binding.Write();
            var original = JsonNode.Parse(File.ReadAllText(binding.PathName))![field]!.GetValue<string>();
            var variants = new List<string> { original, original + Path.DirectorySeparatorChar,
                original + Path.DirectorySeparatorChar + Path.DirectorySeparatorChar,
                Path.Combine(original, "."), Path.Combine(original, "child", "..") };
            if (OperatingSystem.IsWindows())
                variants.AddRange([original.Replace('\\', '/'), original.Replace('\\', '/') + "/",
                    original + "\\", original.ToUpperInvariant()]);
            foreach (var variant in variants)
            {
                binding.Write();
                var stamp = JsonNode.Parse(File.ReadAllText(binding.PathName))!;
                stamp[field] = variant;
                File.WriteAllText(binding.PathName, stamp.ToJsonString());
                binding.Check().ShouldBe("verified", "c1210-equivalent-path: " + field + "=" + variant);
            }
        }
        if (!OperatingSystem.IsWindows())
        {
            binding.Write();
            var stamp = JsonNode.Parse(File.ReadAllText(binding.PathName))!;
            stamp["outputPath"] = binding.OutputPath.ToUpperInvariant();
            File.WriteAllText(binding.PathName, stamp.ToJsonString());
            binding.Check().ShouldBe("mismatch", "c1210-linux-path-case-is-distinct");
        }
    }

    [Test]
    public void Different_directory_or_provenance_is_refused()
    {
        using var fixture = new BindingFixture();
        var binding = fixture.Binding("bin-c1210-producer");
        binding.Check().ShouldBe("unknown", "c1210-missing-stamp");
        foreach (var (field, value) in new (string, JsonNode?)[]
        {
            ("repositoryRoot", JsonValue.Create(Path.Combine(fixture.Root, "different"))),
            ("project", JsonValue.Create(Path.Combine(fixture.Root, "different"))),
            ("outputPath", JsonValue.Create(binding.OutputPath + "-different/")),
            ("commit", JsonValue.Create(new string('b', 40))),
            ("fingerprint", JsonValue.Create(new string('2', 64))),
            ("sourceState", JsonValue.Create("dirty")),
            ("version", JsonValue.Create(2)),
            ("properties", new JsonArray("UseAppHost=false")),
        })
        {
            binding.Write();
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
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
