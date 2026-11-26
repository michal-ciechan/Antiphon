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
public sealed class WatchedTestRunInvariantTests
{
    [Test]
    public void The_checkout_documents_the_watched_test_command()
    {
        var repo = DockerStackDocuments.RepoRoot;
        File.Exists(Path.Combine(repo, "scripts", "run-tests-watched.ps1")).ShouldBeFalse("the watched run is a command");
        File.ReadAllBytes(Path.Combine(repo, "scripts", "lib", "Antiphon.Nightly", "WatchedTestRun.cs")).ShouldAllBe(b => b < 128, "ASCII only");
        File.ReadAllText(Path.Combine(repo, "docs", "testing-and-build.md")).ShouldContain("scripts\\antiphon.cmd run-tests-watched --exe");
    }
}
