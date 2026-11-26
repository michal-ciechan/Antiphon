using System.Security.Cryptography;
using System.Text;
using Antiphon.Nightly;
using Antiphon.Tests.Infrastructure;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;
using Antiphon.Tests.Scripts;

namespace Antiphon.Tests.Invariants;

[Category("Unit")]
[Category("RepositoryInvariant")]
public sealed class PostgresSnapshotJobInvariantTests
{
    [Test]
    public void The_launcher_keeps_the_four_source_literals_the_runner_resolves_by()
    {
        var text = File.ReadAllText(Path.Combine(DockerStackDocuments.RepoRoot, "scripts", "postgres-snapshot.ps1"));
        foreach (var literal in PostgresSnapshotJob.SourceLiterals) text.ShouldContain(literal, Case.Sensitive, literal);
    }
}
