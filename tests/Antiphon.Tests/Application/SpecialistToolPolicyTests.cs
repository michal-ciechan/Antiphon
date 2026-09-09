using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public class SpecialistToolPolicyTests
{
    [Test]
    public async Task Card0415_V03_failed_hook_preparation_refuses_new_Check_seat()
    {
        using var workspace = new Workspace();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var spec = workspace.Spec;
        File.WriteAllText(Path.Combine(workspace.Root, ".claude"), "obstruct directory creation");
        var service = workspace.Provisioner(db);

        var failure = await Should.ThrowAsync<ConflictException>(() => service.EnsureAsync(spec, CancellationToken.None));

        failure.Code.ShouldBe("specialist_tool_policy_unavailable");
        (await db.Agents.AnyAsync(a => a.Slug == spec.Slug)).ShouldBeFalse();
        File.Exists(workspace.TrustPath).ShouldBeFalse("failure must precede trust seeding and launch");
    }

    [Test]
    public async Task Card0415_V03_failed_rearming_refuses_existing_Check_seat()
    {
        using var workspace = new Workspace();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var service = workspace.Provisioner(db);
        var agent = await service.EnsureAsync(workspace.Spec, CancellationToken.None);
        var settingsPath = Path.Combine(workspace.Root, ".claude", "settings.json");
        File.Delete(settingsPath);
        Directory.CreateDirectory(settingsPath);

        var failure = await Should.ThrowAsync<ConflictException>(() => service.EnsureAsync(workspace.Spec, CancellationToken.None));

        failure.Code.ShouldBe("specialist_tool_policy_unavailable");
        agent.PersistentSessionId.ShouldBeNull();
        (await db.Agents.CountAsync(a => a.Slug == workspace.Spec.Slug)).ShouldBe(1);
    }

    [Test]
    public async Task Card0415_V03_successful_preparation_repairs_policy_before_returning_seat()
    {
        using var workspace = new Workspace();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var service = workspace.Provisioner(db);
        var first = await service.EnsureAsync(workspace.Spec, CancellationToken.None);
        var settingsPath = Path.Combine(workspace.Root, ".claude", "settings.json");
        File.WriteAllText(settingsPath, "{}");

        var second = await service.EnsureAsync(workspace.Spec, CancellationToken.None);

        second.Id.ShouldBe(first.Id);
        File.ReadAllText(settingsPath).ShouldBe(workspace.Spec.DenyAllToolsSettingsJson);
    }

    [Test]
    [Arguments(AgentTaskRole.Diagnose)]
    [Arguments(AgentTaskRole.Distill)]
    public async Task Card0415_V03_other_specialists_keep_existing_preparation_behavior(AgentTaskRole role)
    {
        using var workspace = new Workspace();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        File.WriteAllText(Path.Combine(workspace.Root, ".claude"), "obstruct directory creation");

        // A second standing specialist declares its own seat properties; it is not the Check spec
        // wearing another role label. Without ToolPolicyRequired, failed preparation warns and continues.
        var other = workspace.Spec with { Role = role, OwnsStandingSeat = false, ToolPolicyRequired = false };
        var agent = await workspace.Provisioner(db).EnsureAsync(other, CancellationToken.None);

        agent.ShouldNotBeNull();
    }

    private sealed class Workspace : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("c415-policy-").FullName;
        public string TrustPath => Path.Combine(Root, "isolated-claude.json");
        public SpecialistSpec Spec => CheckInterpreterProvisioner.Spec(new DelegationSettings
        {
            CheckInterpreterAgentSlug = Path.GetFileName(Root),
            CheckInterpreterWorkingDirectory = Root,
        });
        public StandingSpecialistProvisioner Provisioner(AppDbContext db) =>
            new(db, TimeProvider.System, NullLogger.Instance, claudeConfigJsonPath: TrustPath);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
