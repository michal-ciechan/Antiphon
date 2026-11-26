using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using System.Text.Json.Nodes;
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
public sealed class PtyHostReaperInvariantTests
{
    [Test]
    public void The_checkout_documents_the_pty_host_reaper()
    {
        var repo = DockerStackDocuments.RepoRoot;
        File.Exists(Path.Combine(repo, "scripts", "reap-orphaned-pty-hosts.ps1")).ShouldBeFalse("the reaper is a command");
        File.ReadAllBytes(Path.Combine(repo, "scripts", "lib", "Antiphon.Nightly", "PtyHostReaper.cs")).ShouldAllBe(b => b < 128, "ASCII only");
        File.ReadAllText(Path.Combine(repo, "docs", "testing-and-build.md")).ShouldContain("scripts\\antiphon.cmd reap-orphaned-pty-hosts");
        File.ReadAllText(Path.Combine(repo, "docs", "ops-http.md")).ShouldContain("reap-orphaned-pty-hosts");
    }
}
