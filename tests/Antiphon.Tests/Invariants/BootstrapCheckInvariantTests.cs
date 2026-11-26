using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Antiphon.Nightly;
using Antiphon.Tests.Infrastructure;
using Antiphon.Tests.Scripts.Nightly;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;
using Antiphon.Tests.Scripts;

namespace Antiphon.Tests.Invariants;

[Category("Unit")]
[Category("RepositoryInvariant")]
public sealed class BootstrapCheckInvariantTests
{
    [Test]
    public void The_checkout_documents_the_bootstrap_command()
    {
        var repo = DockerStackDocuments.RepoRoot;
        File.Exists(Path.Combine(repo, "scripts", "bootstrap-check.ps1")).ShouldBeFalse("the check is a command");
        File.ReadAllBytes(Path.Combine(repo, "scripts", "lib", "Antiphon.Nightly", "BootstrapCheck.cs")).ShouldAllBe(b => b < 128, "ASCII only");
        File.ReadAllText(Path.Combine(repo, "docs", "bootstrap.md")).ShouldContain("scripts\\antiphon.cmd bootstrap-check");
        File.ReadAllText(Path.Combine(repo, ".claude", "skills", "antiphon-run", "SKILL.md")).ShouldContain("bootstrap-check");
    }
}
