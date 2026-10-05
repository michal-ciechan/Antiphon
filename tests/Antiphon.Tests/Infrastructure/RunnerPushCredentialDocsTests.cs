using System.Text.RegularExpressions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Unit")]
public sealed class RunnerPushCredentialDocsTests
{
    [Test]
    public void Credentials_doc_records_the_token_custody_expiry_and_rotation()
    {
        var credentials = DockerStackDocuments.Read("docs/agent-credentials.md");
        var row = credentials.Split('\n').SingleOrDefault(line => line.StartsWith('|')
            && line.Contains("secrets/github-token/token", StringComparison.Ordinal));
        row.ShouldNotBeNull();
        foreach (var required in new[] { "GitHub PAT - server2 push", "97819ca3-710f-43f8-99ce-b4da013e32c1",
                     "antiphon-all (desktop gh + server2/server2-temp) 2026-10-05", "2027-10-05", "2027-09-14",
                     "github-push-token", "refresh-server2-github-token.ps1", "RUNNER_GITHUB_TOKEN_DIR", "0400", "1654",
                     "repo", "workflow", "write:packages", "read:org", "gist" })
            row.ShouldContain(required);
        credentials.ShouldContain("organisation and collaborator repositories");
        credentials.ShouldContain("PhoneHome:AllowedCloneSources");
        var stack = DockerStackDocuments.Read("docs/docker-stack.md");
        var mount = stack.Split('\n').SingleOrDefault(line => line.StartsWith("| `secrets/github-token/`", StringComparison.Ordinal));
        mount.ShouldNotBeNull();
        mount.ShouldContain("RUNNER_GITHUB_TOKEN_DIR");
        mount.ShouldContain("/run/antiphon/github-token:ro");
        mount.ShouldContain("0700");
        foreach (var doc in new[] { credentials, stack })
            Regex.IsMatch(doc, @"ghp_[A-Za-z0-9]{20,}").ShouldBeFalse("custody docs must not contain token values");
        DockerStackDocuments.Read("docs/testing-and-build.md").ShouldContain("CARD-0817");
    }
}
