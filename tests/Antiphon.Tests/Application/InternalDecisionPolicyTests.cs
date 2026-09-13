using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class InternalDecisionPolicyTests
{
    [Test]
    public void Policy_schema_boundaries_valid_v1_normalizes_deterministically()
    {
        var first = Normalize(InternalDecisionFixtures.Sample());
        var second = Normalize(InternalDecisionFixtures.Sample());
        first.ShouldNotBeNull();
        var json = InternalDecisionPolicy.Serialize(first!);
        InternalDecisionPolicy.Serialize(second!).ShouldBe(json);
        InternalDecisionPolicy.Hash(json).ShouldBe(InternalDecisionPolicy.Hash(json));
        json.ShouldContain("\"version\":1");
        json.ShouldContain("backup-transport");
        json.ShouldContain("scripts/deploy-gym-stat.ps1");
        json.ShouldContain("\"grantedBy\"");
        var stored = JsonSerializer.Deserialize<StoredInternalDecisionPolicy>(json, InternalDecisionPolicy.JsonOptions);
        stored!.GrantedBy.Kind.ShouldBe("manual");
        stored.GrantedAt.ShouldBe(InternalDecisionFixtures.GrantedAt);
    }

    [Test]
    public void Policy_schema_boundaries_empty_to_absent()
    {
        Normalize(null).ShouldBeNull();
        Normalize(new InternalDecisionPolicyRequest(1, [])).ShouldBeNull();
        Normalize(new InternalDecisionPolicyRequest(1, null)).ShouldBeNull();
        InternalDecisionPolicy.Parse(null, AgentTaskRole.Code, WorkspaceMode.Shared,
            InternalDecisionFixtures.ManualGrantor(), InternalDecisionFixtures.GrantedAt).ShouldBeNull();
        InternalDecisionPolicy.Parse("{}", AgentTaskRole.Code, WorkspaceMode.Shared,
            InternalDecisionFixtures.ManualGrantor(), InternalDecisionFixtures.GrantedAt).ShouldBeNull();
        InternalDecisionPolicy.Parse("""{"version":2,"grants":[]}""", AgentTaskRole.Code, WorkspaceMode.Shared,
            InternalDecisionFixtures.ManualGrantor(), InternalDecisionFixtures.GrantedAt).ShouldBeNull();
    }

    [Test]
    [Arguments(16, true)]
    [Arguments(17, false)]
    public void Policy_schema_boundaries_grant_count(int grants, bool accepted)
    {
        var request = new InternalDecisionPolicyRequest(
            1,
            Enumerable.Range(0, grants).Select(i => new InternalDecisionGrantRequest(
                $"g{i:00}",
                [InternalDecisionCategory.LineEndings],
                [$"scripts/file-{i:00}.ps1"],
                null,
                "Keep the existing text.")).ToArray());
        if (accepted)
            Normalize(request)!.Grants.Count.ShouldBe(grants);
        else
            Should.Throw<ValidationException>(() => Normalize(request)).Errors.ShouldContainKey("InternalDecisionPolicy");
    }

    [Test]
    [Arguments(64, true)]
    [Arguments(65, false)]
    public void Policy_schema_boundaries_distinct_paths(int paths, bool accepted)
    {
        var request = new InternalDecisionPolicyRequest(
            1,
            [
                new InternalDecisionGrantRequest(
                    "many-paths",
                    [InternalDecisionCategory.BuildTestHarness],
                    Enumerable.Range(0, paths).Select(i => $"tools/file-{i:00}.ps1").ToArray(),
                    null,
                    "Keep the existing verifier."),
            ]);
        if (accepted)
            Normalize(request)!.Grants[0].Paths.Count.ShouldBe(paths);
        else
            Should.Throw<ValidationException>(() => Normalize(request));
    }

    [Test]
    [Arguments(1000, true)]
    [Arguments(1001, false)]
    public void Policy_schema_boundaries_preserve_length(int length, bool accepted)
    {
        var request = InternalDecisionFixtures.Sample(
            categories: [InternalDecisionCategory.LineEndings],
            paths: ["scripts/deploy-gym-stat.ps1"],
            attributeTargets: null,
            preserve: new string('p', length));
        if (accepted)
            Normalize(request)!.Grants[0].Preserve.Length.ShouldBe(length);
        else
            Should.Throw<ValidationException>(() => Normalize(request));
    }

    [Test]
    [Arguments(20_000, true)]
    [Arguments(20_001, false)]
    public void Policy_schema_boundaries_canonical_json_size(int targetLength, bool accepted)
    {
        var request = BuildCanonicalSizedPolicy(targetLength);
        if (accepted)
        {
            var stored = Normalize(request);
            CanonicalDocumentLength(stored!).ShouldBe(targetLength);
        }
        else
            Should.Throw<ValidationException>(() => Normalize(request));
    }

    [Test]
    [Arguments("""{"version":1,"grants":[{"id":"x","categories":["LineEndings"],"paths":["a.ps1"]}]}""")]
    [Arguments("""{"version":1,"grants":[{"id":"x","categories":["LineEndings"],"preserve":"keep"}]}""")]
    [Arguments("""{"version":1,"grants":[{"id":"x","paths":["a.ps1"],"preserve":"keep"}]}""")]
    [Arguments("""{"version":1,"grants":[{"id":"x","categories":[],"paths":["a.ps1"],"preserve":"keep"}]}""")]
    [Arguments("""{"version":1,"grants":[{"id":" ","categories":["LineEndings"],"paths":["a.ps1"],"preserve":"keep"}]}""")]
    [Arguments("""{"version":1,"grants":[{"id":"x","categories":["LineEndings"],"paths":["a.ps1"],"preserve":"   "}]}""")]
    [Arguments("""{"version":1,"grants":[{"id":"x","categories":["Nope"],"paths":["a.ps1"],"preserve":"keep"}]}""")]
    [Arguments("""{"version":9,"grants":[{"id":"x","categories":["LineEndings"],"paths":["a.ps1"],"preserve":"keep"}]}""")]
    [Arguments("""{"version":1,"grants":[{"id":"x","categories":[0],"paths":["a.ps1"],"preserve":"keep"}]}""")]
    [Arguments("""{"version":1,"extra":true,"grants":[{"id":"x","categories":["LineEndings"],"paths":["a.ps1"],"preserve":"keep"}]}""")]
    [Arguments("""{"version":1,"grants":[{"id":"x","categories":["LineEndings"],"paths":["a.ps1"],"preserve":"keep","note":"no"}]}""")]
    [Arguments("""{"version":1,"grantedBy":{"kind":"manual"},"grants":[{"id":"x","categories":["LineEndings"],"paths":["a.ps1"],"preserve":"keep"}]}""")]
    [Arguments("""{"version":1,"grantedAt":"2026-01-01T00:00:00Z","grants":[{"id":"x","categories":["LineEndings"],"paths":["a.ps1"],"preserve":"keep"}]}""")]
    [Arguments("""{"version":1,"hash":"abc","grants":[{"id":"x","categories":["LineEndings"],"paths":["a.ps1"],"preserve":"keep"}]}""")]
    public void Policy_schema_boundaries_invalid_documents_are_rejected(string json)
    {
        var ex = Should.Throw<ValidationException>(() =>
            InternalDecisionPolicy.Parse(
                json, AgentTaskRole.Code, WorkspaceMode.Shared,
                InternalDecisionFixtures.ManualGrantor(), InternalDecisionFixtures.GrantedAt));
        ex.StatusCode.ShouldBe(422);
        ex.Errors.ShouldContainKey("InternalDecisionPolicy");
    }

    [Test]
    public void Policy_schema_boundaries_duplicate_ids_are_rejected()
    {
        var request = new InternalDecisionPolicyRequest(
            1,
            [
                new InternalDecisionGrantRequest("same", [InternalDecisionCategory.LineEndings], ["a.ps1"], null, "keep"),
                new InternalDecisionGrantRequest("SAME", [InternalDecisionCategory.ShellTransport], ["b.ps1"], null, "keep"),
            ]);
        Should.Throw<ValidationException>(() => Normalize(request));
    }

    [Test]
    [Arguments("scripts/deploy-gym-stat.ps1")]
    [Arguments("scripts\\deploy-gym-stat.ps1")]
    [Arguments("./scripts/deploy-gym-stat.ps1")]
    [Arguments("scripts/./deploy-gym-stat.ps1")]
    [Arguments("helpers/new-capture.ps1")]
    public void Exact_paths_only_accepts_named_files_and_proposed_helpers(string path)
    {
        var stored = Normalize(InternalDecisionFixtures.Sample(
            categories: [InternalDecisionCategory.ShellTransport],
            paths: [path],
            attributeTargets: null));
        stored!.Grants[0].Paths.ShouldHaveSingleItem()
            .ShouldBe(InternalDecisionPolicy.NormalizeRepositoryPath(path));
    }

    [Test]
    [Arguments(@"C:\src\file.ps1")]
    [Arguments("C:file.ps1")]
    [Arguments("/etc/passwd")]
    [Arguments(@"\\server\share\file.ps1")]
    [Arguments("//server/share/file.ps1")]
    [Arguments("../file.ps1")]
    [Arguments("scripts/../../etc/passwd")]
    [Arguments("scripts/*.ps1")]
    [Arguments("scripts/foo?.ps1")]
    [Arguments("scripts/")]
    [Arguments("scripts/**/foo.ps1")]
    [Arguments("~/.bashrc")]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments(".")]
    public void Exact_paths_only_rejects_non_exact_repository_files(string path)
    {
        Should.Throw<ValidationException>(() =>
            Normalize(InternalDecisionFixtures.Sample(
                categories: [InternalDecisionCategory.LineEndings],
                paths: [path],
                attributeTargets: null)));
    }

    [Test]
    public void Exact_paths_only_does_not_authorize_a_sibling_prefix()
    {
        var stored = Normalize(InternalDecisionFixtures.Sample(
            categories: [InternalDecisionCategory.ShellTransport],
            paths: ["scripts/deploy.ps1"],
            attributeTargets: null));
        var granted = stored!.Grants[0].Paths;
        InternalDecisionPolicy.PathIsGranted("scripts/deploy.ps1", granted, StringComparer.Ordinal).ShouldBeTrue();
        InternalDecisionPolicy.PathIsGranted("scripts/deploy.ps1.bak", granted, StringComparer.Ordinal).ShouldBeFalse();
        InternalDecisionPolicy.PathIsGranted("scripts/deploy.ps1/extra", granted, StringComparer.Ordinal).ShouldBeFalse();
    }

    [Test]
    public void Exact_paths_only_case_vectors()
    {
        InternalDecisionPolicy.PathsEqual("scripts/Foo.ps1", "scripts/foo.ps1", StringComparer.Ordinal)
            .ShouldBeFalse();
        InternalDecisionPolicy.PathsEqual("scripts/Foo.ps1", "scripts/foo.ps1", StringComparer.OrdinalIgnoreCase)
            .ShouldBeTrue();
        var hostEqual = InternalDecisionPolicy.PathsEqual(
            "scripts/Foo.ps1", "scripts/foo.ps1", InternalDecisionPolicy.FileSystemComparer);
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
            hostEqual.ShouldBeTrue();
        else
            hostEqual.ShouldBeFalse();
    }

    [Test]
    public void Attribute_grant_boundaries_require_targets_inside_grant_paths()
    {
        var stored = Normalize(InternalDecisionFixtures.Sample(
            categories: [InternalDecisionCategory.LineEndings],
            paths: ["scripts/a.ps1", "scripts/b.ps1", ".gitattributes"],
            attributeTargets: ["scripts/a.ps1", "scripts/b.ps1"]));
        stored!.Grants[0].AttributeTargets.ShouldBe(["scripts/a.ps1", "scripts/b.ps1"]);
    }

    [Test]
    public void Attribute_grant_boundaries_reject_unlisted_or_broad_targets()
    {
        Should.Throw<ValidationException>(() => Normalize(InternalDecisionFixtures.Sample(
            categories: [InternalDecisionCategory.LineEndings],
            paths: ["scripts/a.ps1", ".gitattributes"],
            attributeTargets: ["scripts/other.ps1"])));
        Should.Throw<ValidationException>(() => Normalize(InternalDecisionFixtures.Sample(
            categories: [InternalDecisionCategory.LineEndings],
            paths: ["scripts/a.ps1", ".gitattributes"],
            attributeTargets: ["*.ps1"])));
        Should.Throw<ValidationException>(() => Normalize(InternalDecisionFixtures.Sample(
            categories: [InternalDecisionCategory.LineEndings],
            paths: ["scripts/a.ps1", ".gitattributes"],
            attributeTargets: ["scripts/"])));
        Should.Throw<ValidationException>(() => Normalize(InternalDecisionFixtures.Sample(
            categories: [InternalDecisionCategory.LineEndings],
            paths: ["scripts/a.ps1", ".gitattributes"],
            attributeTargets: null)));
        Should.Throw<ValidationException>(() => Normalize(InternalDecisionFixtures.Sample(
            categories: [InternalDecisionCategory.LineEndings],
            paths: ["scripts/a.ps1", ".gitattributes"],
            attributeTargets: [])));
    }

    [Test]
    public void Attribute_grant_boundaries_reject_gitattributes_without_line_endings()
    {
        Should.Throw<ValidationException>(() => Normalize(InternalDecisionFixtures.Sample(
            categories: [InternalDecisionCategory.ShellTransport],
            paths: ["scripts/a.ps1", ".gitattributes"],
            attributeTargets: ["scripts/a.ps1"])));
        Should.Throw<ValidationException>(() => Normalize(InternalDecisionFixtures.Sample(
            categories: [InternalDecisionCategory.BuildTestHarness],
            paths: ["scripts/a.ps1", ".gitattributes"],
            attributeTargets: ["scripts/a.ps1"])));
        Should.Throw<ValidationException>(() => Normalize(InternalDecisionFixtures.Sample(
            categories: [InternalDecisionCategory.ShellTransport],
            paths: ["scripts/a.ps1"],
            attributeTargets: ["scripts/a.ps1"])));
    }

    [Test]
    public void RoleMayCarryGrants_classifies_every_current_role()
    {
        var eligible = new HashSet<AgentTaskRole>
        {
            AgentTaskRole.Code, AgentTaskRole.Debug, AgentTaskRole.Custom, AgentTaskRole.Deploy,
            AgentTaskRole.Plan, AgentTaskRole.TestDesign, AgentTaskRole.Coverage, AgentTaskRole.Docs,
            AgentTaskRole.Commit, AgentTaskRole.Merge,
        };
        foreach (var role in Enum.GetValues<AgentTaskRole>())
            InternalDecisionPolicy.RoleMayCarryGrants(role).ShouldBe(eligible.Contains(role), role.ToString());
        InternalDecisionPolicy.RoleMayCarryGrants((AgentTaskRole)999).ShouldBeFalse();
        eligible.Count.ShouldBe(10);
        Enum.GetValues<AgentTaskRole>().Length.ShouldBe(17);
    }

    private static StoredInternalDecisionPolicy? Normalize(InternalDecisionPolicyRequest? request) =>
        InternalDecisionPolicy.Normalize(
            request, AgentTaskRole.Code, WorkspaceMode.Shared,
            InternalDecisionFixtures.ManualGrantor(), InternalDecisionFixtures.GrantedAt);

    private static InternalDecisionPolicyRequest BuildCanonicalSizedPolicy(int targetLength)
    {
        var probe = OneGrant("pad/a.ps1", "keep");
        var extra = targetLength - DocumentLength(probe);
        extra.ShouldBeGreaterThan(0);
        var request = OneGrant("pad/" + new string('x', extra + 1) + ".ps1", "keep");
        DocumentLength(request).ShouldBe(targetLength);
        return request;
    }

    private static InternalDecisionPolicyRequest OneGrant(string path, string preserve) =>
        new(1,
        [
            new InternalDecisionGrantRequest(
                "pad",
                [InternalDecisionCategory.LineEndings],
                [path],
                null,
                preserve),
        ]);

    private static int DocumentLength(InternalDecisionPolicyRequest request)
    {
        var storedGrants = request.Grants!.Select(grant => new StoredInternalDecisionGrant(
            grant.Id!,
            grant.Categories!.OrderBy(c => (int)c).ToArray(),
            grant.Paths!,
            grant.AttributeTargets,
            grant.Preserve!)).ToArray();
        var document = new { version = request.Version, grants = storedGrants };
        return JsonSerializer.Serialize(document, InternalDecisionPolicy.JsonOptions).Length;
    }

    private static int CanonicalDocumentLength(StoredInternalDecisionPolicy stored)
    {
        var document = new { version = stored.Version, grants = stored.Grants };
        return JsonSerializer.Serialize(document, InternalDecisionPolicy.JsonOptions).Length;
    }
}
