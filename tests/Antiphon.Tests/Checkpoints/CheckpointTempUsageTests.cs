using System.Security.Cryptography;
using System.Text.Json;
using Antiphon.Checkpoints;
using Antiphon.TestSupport;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

// CARD-0804/0805. The temp inventory is the checkpoints tool's inspect-temp verb since unit 20 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md (scripts/inspect-checkpoint-temp.ps1 is gone), run here in
// process. The usage verifiers (verify-checkpoint-temp-usage.ps1, verify-checkpoint-orphan-usage.ps1 and their
// lib/checkpoint-usage.ps1) were deleted with run-checkpoint.ps1's body; the roster the test assembly writes for them stays.
[Category("Integration")]
[ParallelLimiter<CheckpointToolSpawnLimit>]
public sealed class CheckpointTempUsageTests : CheckpointTestBase
{
    [Test]
    public async Task inspection_is_read_only()
    {
        // A folder of its own: the test's temp root holds its own custody files.
        var sandbox = Directory.CreateDirectory(Path.Combine(TempDir(), "sandbox")).FullName;
        var candidate = Path.Combine(sandbox, "c723-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(candidate);
        var sentinel = Path.Combine(candidate, "sentinel.bin");
        await File.WriteAllBytesAsync(sentinel, RandomNumberGenerator.GetBytes(4096));
        var before = SHA256.HashData(await File.ReadAllBytesAsync(sentinel));
        var marked = Path.Combine(sandbox, "c723-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(marked);
        await File.WriteAllTextAsync(Path.Combine(marked, ".checkpoint-test-root.json"),
            JsonSerializer.Serialize(new { Version = 1, RootId = Path.GetFileName(marked)[5..], RootPath = marked }));
        Directory.CreateDirectory(Path.Combine(sandbox, "c723-not-a-root"));
        var file = Path.Combine(sandbox, "inventory", "temp.json");
        var output = new StringWriter();
        var exit = await Antiphon.Checkpoints.Program.RunAsync(["inspect-temp", "--temp-root", sandbox, "--output-file", file],
            new CheckpointApp.Runtime { Output = output });
        exit.ShouldBe(0, output.ToString());
        using var report = JsonDocument.Parse(output.ToString());
        report.RootElement.GetProperty("schemaVersion").GetInt32().ShouldBe(1);
        report.RootElement.GetProperty("entriesExamined").GetInt32().ShouldBe(3);
        var roots = report.RootElement.GetProperty("roots").EnumerateArray()
            .ToDictionary(root => root.GetProperty("path").GetString()!, root => root);
        roots.Count.ShouldBe(2);
        roots[candidate].GetProperty("reason").GetString().ShouldBe("legacy-unmarked");
        roots[candidate].GetProperty("sampledDescendants").GetInt32().ShouldBe(1);
        roots[candidate].GetProperty("sampledLogicalBytes").GetInt64().ShouldBe(4096);
        roots[marked].GetProperty("reason").GetString().ShouldBe("marked-identity-unverified");
        File.ReadAllText(file).ShouldBe(output.ToString().TrimEnd('\r', '\n'));
        SHA256.HashData(await File.ReadAllBytesAsync(sentinel)).ShouldBe(before);
        Directory.Exists(candidate).ShouldBeTrue();

        var bounded = new StringWriter();
        (await Antiphon.Checkpoints.Program.RunAsync(["inspect-temp", "--temp-root", sandbox, "--max-entries", "0"],
            new CheckpointApp.Runtime { Output = bounded })).ShouldBe(ExitCodes.Invalid);
        bounded.ToString().ShouldContain("Inspection bounds are outside the supported range.");
    }

    [Test]
    public void full_roster_excludes_explicit_cases()
    {
        var assembly = typeof(CheckpointTempUsageTests).Assembly;
        var explicitClasses = assembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && type.IsDefined(typeof(ExplicitAttribute), inherit: true))
            .Where(type => TestClassificationMetadata.GetTestMethods(type).Count > 0)
            .ToArray();
        explicitClasses.ShouldNotBeEmpty("this assembly carries [Explicit] cases the Full roster must drop");
        var cases = explicitClasses
            .SelectMany(type => TestClassificationMetadata.GetTestMethods(type).Select(method => (type, method.Name)))
            .Select((test, i) => new CheckpointRoster.Case("explicit-" + i, test.type, test.Name, test.Name))
            .Append(new CheckpointRoster.Case("ordinary", typeof(CheckpointTempUsageTests),
                nameof(full_roster_excludes_explicit_cases), nameof(full_roster_excludes_explicit_cases)))
            .ToArray();
        cases.Length.ShouldBeGreaterThan(1);
        var target = Path.Combine(TempDir(), "roster.json");
        CheckpointRoster.Write(target, cases);
        using var roster = JsonDocument.Parse(File.ReadAllText(target));
        roster.RootElement.EnumerateArray().Select(test => test.GetProperty("id").GetString()).ShouldBe(["ordinary"]);
        var compiled = CheckpointRoster.CompiledCases(assembly);
        compiled.ShouldContain((typeof(CheckpointTempUsageTests).FullName!, nameof(full_roster_excludes_explicit_cases)));
        compiled.ShouldNotContain(test => explicitClasses.Any(type => type.FullName == test.ClassName));
    }
}
