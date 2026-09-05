using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0330 S2 — the standing output-distiller seat exists, is supervised, and carries a contract
/// that lives in code. Mirrors <see cref="CheckInterpreterProvisionerTests"/> against the
/// distiller spec.
/// </summary>
[Category("Integration")]
public class OutputDistillerProvisionerTests
{
    [Test]
    public async Task the_first_call_creates_the_specialist_with_the_shape_the_design_depends_on()
    {
        using var scratch = new TempWorkspace();
        var settings = SettingsFor(scratch.Path);

        var created = await EnsureAsync(settings);

        created.ShouldNotBeNull();
        var agent = await ReloadAsync(created.Id);
        agent.Slug.ShouldBe(settings.OutputDistillerAgentSlug);
        agent.Name.ShouldBe(settings.OutputDistillerAgentSlug);
        agent.AlwaysOn.ShouldBeTrue(
            "the existing supervisor sweep is what restarts it — no new supervision code is written");
        agent.IsPoolDelegate.ShouldBeFalse("the pool janitor filters on this before retiring anything");
        agent.ModelLevel.ShouldBe(
            AgentModelLevel.Low, "distilling a finished report is haiku work");
        agent.RemoteControlEnabled.ShouldBeFalse();
        agent.Status.ShouldBe(AgentStatus.Idle);
        agent.WorkingDirectory.ShouldBe(scratch.Path, "its own cwd is its own transcript root (CARD-0006)");
        agent.SystemPromptAppend.ShouldBe(OutputDistillation.Contract);
        agent.PersistentSessionId.ShouldBeNull("nothing was started — no control service is wired here");
    }

    [Test]
    public async Task the_deny_all_tool_hook_is_written_into_its_scratch_directory()
    {
        using var scratch = new TempWorkspace();

        await EnsureAsync(SettingsFor(scratch.Path));

        var hookPath = Path.Combine(scratch.Path, ".claude", "settings.json");
        File.Exists(hookPath).ShouldBeTrue();
        var content = await File.ReadAllTextAsync(hookPath);
        content.ShouldBe(OutputDistillation.DenyAllToolsSettingsJson);
        content.ShouldContain("\"PreToolUse\"");
        content.ShouldContain(
            "\"matcher\": \"*\"",
            customMessage: "a matcher naming specific tools would leave every other tool allowed");
        content.ShouldContain("exit 2", customMessage: "exit 2 is what feeds the refusal back to the model");
        content.ShouldContain("output distiller");
    }

    [Test]
    public async Task a_second_call_changes_nothing()
    {
        using var scratch = new TempWorkspace();
        var settings = SettingsFor(scratch.Path);

        var first = await EnsureAsync(settings);
        first.ShouldNotBeNull();
        var stampAfterCreate = (await ReloadAsync(first.Id)).UpdatedAt;

        var second = await EnsureAsync(settings);

        second.ShouldNotBeNull();
        second.Id.ShouldBe(first.Id, "found by slug, not created again");
        (await ReloadAsync(first.Id)).UpdatedAt.ShouldBe(stampAfterCreate, "and not written to either");
        await using var verify = CreateContext();
        (await verify.Agents.CountAsync(a => a.Slug == settings.OutputDistillerAgentSlug)).ShouldBe(1);
    }

    [Test]
    public async Task deleting_the_agent_is_recoverable_the_next_call_recreates_it()
    {
        using var scratch = new TempWorkspace();
        var settings = SettingsFor(scratch.Path);
        var first = await EnsureAsync(settings);
        first.ShouldNotBeNull();

        await using (var db = CreateContext())
        {
            db.Agents.Remove(await db.Agents.SingleAsync(a => a.Id == first.Id));
            await db.SaveChangesAsync();
        }

        var recreated = await EnsureAsync(settings);

        recreated.ShouldNotBeNull();
        recreated.Id.ShouldNotBe(first.Id, "a new row");
        recreated.SystemPromptAppend.ShouldBe(OutputDistillation.Contract);
        recreated.AlwaysOn.ShouldBeTrue();
    }

    [Test]
    public async Task a_hand_edited_contract_is_reconciled_back_to_the_constant()
    {
        using var scratch = new TempWorkspace();
        var settings = SettingsFor(scratch.Path);
        var agent = await EnsureAsync(settings);
        agent.ShouldNotBeNull();

        await using (var db = CreateContext())
        {
            var row = await db.Agents.SingleAsync(a => a.Id == agent.Id);
            row.SystemPromptAppend = "You are a helpful assistant. Feel free to investigate.";
            await db.SaveChangesAsync();
        }

        await EnsureAsync(settings);

        var reconciled = await ReloadAsync(agent.Id);
        reconciled.SystemPromptAppend.ShouldBe(OutputDistillation.Contract);
        reconciled.SystemPromptAppend!.ShouldContain("NEVER invent, round, rename or paraphrase an identifier or a number.");
        reconciled.SystemPromptAppend!.ShouldContain($"contract v{OutputDistillation.ContractVersion}");
        reconciled.SystemPromptAppend!.ShouldContain("contract v1");
        reconciled.SystemPromptAppend!.ShouldContain("USE NO TOOLS");
    }

    [Test]
    public async Task a_workspace_that_was_cleaned_up_is_healed_on_the_next_call()
    {
        using var scratch = new TempWorkspace();
        var settings = SettingsFor(scratch.Path);
        await EnsureAsync(settings);
        Directory.Delete(Path.Combine(scratch.Path, ".claude"), recursive: true);

        await EnsureAsync(settings);

        File.Exists(Path.Combine(scratch.Path, ".claude", "settings.json")).ShouldBeTrue();
    }

    [Test]
    public async Task the_feature_switch_provisions_nothing_at_all()
    {
        using var scratch = new TempWorkspace();
        var settings = SettingsFor(scratch.Path);
        settings.OutputDistillerEnabled = false;

        (await EnsureAsync(settings)).ShouldBeNull();

        await using var verify = CreateContext();
        (await verify.Agents.AnyAsync(a => a.Slug == settings.OutputDistillerAgentSlug)).ShouldBeFalse();
        Directory.Exists(Path.Combine(scratch.Path, ".claude")).ShouldBeFalse("not even the hook file");
    }

    [Test]
    public void the_version_label_matches_the_bundle()
    {
        OutputDistillation.ContractVersion.ShouldBe("1");
        OutputDistillation.Contract.ShouldContain($"contract v{OutputDistillation.ContractVersion}");
        OutputDistillation.Contract.ShouldBe(InstructionBundles.TextOf(InstructionBundles.OutputDistiller));
        OutputDistillation.Contract.ShouldStartWith("You are the Antiphon OUTPUT DISTILLER (contract v1).");
        OutputDistillation.OutputFormatReminder.ShouldContain("never `blocked`");
        var reporting = DelegationReportFormatter.DistillReportingContract(
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), 20_000);
        reporting.ShouldContain("Never emit a `blocked` report token");
        reporting.ShouldContain(DelegationReportFormatter.ReportToken(
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), "done"));
    }

    [Test]
    public void an_unconfigured_directory_is_derived_from_the_first_allowed_root()
    {
        var settings = new DelegationSettings { AllowedRoots = ["C:\\src\\Example", "C:\\src\\Other"] };

        OutputDistillerProvisioner.ResolveWorkingDirectory(settings)
            .ShouldBe(Path.Combine("C:\\src\\Example", ".antiphon", "output-distiller"));
    }

    [Test]
    public void with_no_allowed_roots_it_still_lands_somewhere_of_its_own()
    {
        var settings = new DelegationSettings();

        OutputDistillerProvisioner.ResolveWorkingDirectory(settings)
            .ShouldBe(Path.Combine(Path.GetTempPath(), "antiphon", "output-distiller"));
    }

    private static DelegationSettings SettingsFor(string directory) => new()
    {
        OutputDistillerAgentSlug = $"distiller-{Guid.NewGuid():N}"[..24],
        OutputDistillerWorkingDirectory = directory,
    };

    private static async Task<Agent?> EnsureAsync(DelegationSettings settings)
    {
        await using var db = CreateContext();
        var provisioner = new OutputDistillerProvisioner(
            db, Options.Create(settings), TimeProvider.System,
            NullLogger<OutputDistillerProvisioner>.Instance);
        return await provisioner.EnsureAsync(CancellationToken.None);
    }

    private static async Task<Agent> ReloadAsync(Guid agentId)
    {
        await using var db = CreateContext();
        return await db.Agents.AsNoTracking().SingleAsync(a => a.Id == agentId);
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-distiller-test").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
