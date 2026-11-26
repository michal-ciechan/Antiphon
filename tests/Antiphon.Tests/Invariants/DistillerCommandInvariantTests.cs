using Antiphon.Nightly;
using Antiphon.Tests.Infrastructure;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;
using Antiphon.Tests.Scripts;

namespace Antiphon.Tests.Invariants;

[Category("Unit")]
[Category("RepositoryInvariant")]
public sealed class DistillerCommandInvariantTests
{
    [Test]
    public void Distiller_command_is_ascii()
    {
        File.ReadAllBytes(Path.Combine(DockerStackDocuments.RepoRoot, "scripts", "lib", "Antiphon.Nightly", "DistillerLedger.cs")).ShouldAllBe(b => b < 128);
    }
}
