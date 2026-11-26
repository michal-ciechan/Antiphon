using System.Security.Cryptography;
using Antiphon.Nightly;
using Antiphon.Tests.Infrastructure;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;
using Antiphon.Tests.Scripts;
using static Antiphon.Tests.Scripts.RegisterPostgresSnapshotTests;

namespace Antiphon.Tests.Invariants;

[Category("Unit")]
[Category("RepositoryInvariant")]
public sealed class RegisterPostgresSnapshotInvariantTests
{
    [Test]
    public void Snapshot_registration_uses_the_shipped_definitions()
    {
        RegisterPostgresSnapshotOptions.Parse([], DockerStackDocuments.RepoRoot).DefinitionRoot.ShouldBe(Windmill);
        File.Exists(Path.Combine(DockerStackDocuments.RepoRoot, "scripts", "register-postgres-snapshot.ps1")).ShouldBeFalse();
        File.Exists(Path.Combine(Windmill, "antiphon-postgres-snapshot.json")).ShouldBeTrue();
        File.Exists(Path.Combine(Windmill, "antiphon-postgres-snapshot-stale.json")).ShouldBeTrue();
    }
}
