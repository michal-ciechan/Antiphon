using System.Text.RegularExpressions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.Application;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1137 consumer guard, bounded on purpose. For exactly the three test classes that compare EF
/// entities through <see cref="EntityScalarSnapshot"/>, it reads the source and fails on ANY
/// System.Text.Json serialization call that is not an exact allow-listed token, and on any drop of
/// the snapshot uses below the recorded floor. It never classifies an argument's type (the removed
/// assembly-wide scanner misclassified projections), so a reverted call site is red whatever it
/// serializes. Full-line comments are skipped; anything else that mentions an API is flagged.
/// </summary>
[Category("Unit")]
public sealed class EntityScalarSnapshotConsumerGuardTests
{
    /// <summary>The protected call-site classes and their current EntityScalarSnapshot.Of uses.</summary>
    private static readonly (string File, int SnapshotFloor)[] Consumers =
    [
        ("tests/Antiphon.Tests/Application/TerminalRunnerSeatReleaseTests.cs", 2),
        ("tests/Antiphon.Tests/Application/ReviewEvidenceRecoveryTests.cs", 6),
        ("tests/Antiphon.Tests/Application/CardFilePrivacySyncAcceptanceTests.cs", 4),
    ];

    /// <summary>Exact serialization tokens (whitespace collapsed) a consumer may keep.</summary>
    private static readonly (string File, string Token)[] Allowed =
    [
        // Phone-home Error frame payload: an anonymous literal, not an entity.
        ("tests/Antiphon.Tests/Application/TerminalRunnerSeatReleaseTests.cs",
            "JsonSerializer.SerializeToElement(new { code = \"unsupported_operation\" })"),
        // ReviewRecoveryWorld.RowsAsync is List<StageOutcome>; StageOutcome has no navigation
        // (pinned by Allow_list_premises_hold), so no entity graph is walked. Predates CARD-1137.
        ("tests/Antiphon.Tests/Application/ReviewEvidenceRecoveryTests.cs", "JsonSerializer.Serialize(await w.RowsAsync())"),
        ("tests/Antiphon.Tests/Application/ReviewEvidenceRecoveryTests.cs", "JsonSerializer.Serialize((await w.RowsAsync()).Single())"),
        ("tests/Antiphon.Tests/Application/ReviewEvidenceRecoveryTests.cs", "JsonSerializer.Serialize(rows.Single(o => o.Id == w.OldId))"),
    ];

    // Calls whose token runs to the balanced close parenthesis, plus aliases that hide the type name.
    private static readonly Regex SerializationCall = new(
        @"\bJsonSerializer\s*\.\s*\w+|\bJsonContent\s*\.\s*Create\b|\bJsonValue\s*\.\s*Create\b|\b(?:Post|Put|Patch)AsJsonAsync\b",
        RegexOptions.Compiled);
    private static readonly Regex HiddenSerializer = new(
        @"using\s+static\s+(?:global::)?System\.Text\.Json\.JsonSerializer\b|using\s+\w+\s*=\s*(?:global::)?System\.Text\.Json\.JsonSerializer\b",
        RegexOptions.Compiled);

    public static IEnumerable<Func<(string File, int SnapshotFloor)>> ConsumerFiles() =>
        Consumers.Select(c => (Func<(string, int)>)(() => c));

    [Test]
    [MethodDataSource(nameof(ConsumerFiles))]
    public void Consumer_serializes_only_allow_listed_tokens_and_keeps_its_snapshots(string file, int snapshotFloor)
    {
        var code = CodeOf(file);
        var allowed = Allowed.Where(a => a.File == file).Select(a => a.Token).ToHashSet(StringComparer.Ordinal);
        var tokens = SerializationCall.Matches(code).Select(m => Token(code, m.Index)).ToArray();

        HiddenSerializer.Matches(code).Select(m => m.Value).ShouldBeEmpty(
            $"{file}: an alias or using static hides JsonSerializer from this guard (CARD-1137)");
        tokens.Where(t => !allowed.Contains(t)).ShouldBeEmpty(
            $"{file}: compare EF entities with EntityScalarSnapshot.Of(db, ...), not System.Text.Json serialization (CARD-1137); "
            + "a non-entity payload needs an exact, commented Allowed entry");
        allowed.Where(a => !tokens.Contains(a)).ShouldBeEmpty($"{file}: stale Allowed entry; remove it");
        Regex.Count(code, @"\bEntityScalarSnapshot\s*\.\s*Of\s*\(").ShouldBeGreaterThanOrEqualTo(snapshotFloor,
            $"{file}: EntityScalarSnapshot.Of uses dropped below {snapshotFloor}; a removed comparison must lower the floor deliberately");
    }

    [Test]
    public void Allow_list_premises_hold()
    {
        Consumers.Length.ShouldBe(3, "the three CARD-1137 call-site classes");
        Consumers.ShouldAllBe(c => c.SnapshotFloor > 0);
        Allowed.Select(a => a.File).Except(Consumers.Select(c => c.File)).ShouldBeEmpty("an Allowed entry names an unprotected file");
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=entity_snapshot_consumers;Username=unused;Password=unused").Options);
        var stageOutcome = db.Model.FindEntityType(typeof(StageOutcome)).ShouldNotBeNull();
        stageOutcome.GetNavigations().Select(n => n.Name).Concat(stageOutcome.GetSkipNavigations().Select(n => n.Name))
            .ShouldBeEmpty("the ReviewEvidenceRecoveryTests Allowed entries rely on StageOutcome having no navigation");
    }

    /// <summary>The file's lines without full-line comments; a missing file fails, never passes.</summary>
    private static string CodeOf(string file)
    {
        var path = Path.Combine(DelegateScriptRunner.RepoRoot, file);
        File.Exists(path).ShouldBeTrue($"protected consumer {file} not found at {path}; update Consumers if it moved");
        return string.Join('\n', File.ReadAllLines(path).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
    }

    /// <summary>The call text from <paramref name="start"/> to its balanced close parenthesis, whitespace collapsed.</summary>
    private static string Token(string code, int start)
    {
        var open = code.IndexOf('(', start);
        var end = open;
        for (var depth = 0; open >= 0 && end < code.Length; end++)
        {
            if (code[end] == '(') depth++;
            else if (code[end] == ')' && --depth == 0) break;
        }
        var text = open < 0 || end >= code.Length ? code[start..] : code[start..(end + 1)];
        return Regex.Replace(text, @"\s+", " ").Trim();
    }
}
