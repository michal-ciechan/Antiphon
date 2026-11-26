using System.Text;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.Infrastructure;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using Antiphon.Tests.Scripts;

namespace Antiphon.Tests.Invariants;

[Category("Integration")]
[Category("RepositoryInvariant")]
public sealed class OsWitnessCheckInvariantTests
{
    [Test]
    public void C1526_The_testing_doc_names_the_command()
    {
        var testing = File.ReadAllText(Path.Combine(DockerStackDocuments.RepoRoot, "docs", "testing-and-build.md"));
        testing.ShouldContain("## OS branch witnesses");
        testing.ShouldContain("scripts/antiphon check-os-witnesses --base-ref <full-base-sha> --head-ref <full-head-sha> --evidence-path <ignored-json> --plan <repo-relative-plan.md>");
        testing.ShouldNotContain("check-os-witnesses.ps1");
        File.ReadAllBytes(Path.Combine(DockerStackDocuments.RepoRoot, "scripts", "lib", "Antiphon.Nightly", "OsWitnessCheck.cs")).ShouldAllBe(b => b < 128);
    }
}
