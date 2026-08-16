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
/// CARD-0047 slice 4B — the standing check interpreter exists, is supervised, and carries a
/// contract that lives in code.
///
/// <para>Supervision itself is NOT re-tested here: the AlwaysOn machinery already owns it and has
/// its own suite. What is tested is the handful of row properties the rest of the design leans on —
/// <c>AlwaysOn</c> (so the supervisor adopts it), <c>IsPoolDelegate = false</c> (so the pool janitor
/// can never retire it out from under the feature), its own working directory (so it gets its own
/// Claude transcript root, which is the CARD-0006 hazard removed by construction), and the contract
/// being reconciled from the constant rather than trusted from the row.</para>
///
/// <para>Every test uses its own slug and its own scratch directory: the fixture database is shared
/// across the whole assembly and <see cref="Agent.Slug"/> is uniquely indexed.</para>
/// </summary>
[Category("Integration")]
public class CheckInterpreterProvisionerTests
{
    [Test]
    public async Task the_first_call_creates_the_specialist_with_the_shape_the_design_depends_on()
    {
        using var scratch = new TempWorkspace();
        var settings = SettingsFor(scratch.Path);

        var created = await EnsureAsync(settings);

        created.ShouldNotBeNull();
        var agent = await ReloadAsync(created.Id);
        agent.Slug.ShouldBe(settings.CheckInterpreterAgentSlug);
        agent.Name.ShouldBe(settings.CheckInterpreterAgentSlug);
        agent.AlwaysOn.ShouldBeTrue(
            "the existing supervisor sweep is what restarts it — no new supervision code is written");
        agent.IsPoolDelegate.ShouldBeFalse("the pool janitor filters on this before retiring anything");
        agent.ModelLevel.ShouldBe(
            AgentModelLevel.Low, "reading a bundle and picking one of five words is haiku work");
        agent.RemoteControlEnabled.ShouldBeFalse();
        agent.Status.ShouldBe(AgentStatus.Idle);
        agent.WorkingDirectory.ShouldBe(scratch.Path, "its own cwd is its own transcript root (CARD-0006)");
        agent.SystemPromptAppend.ShouldBe(CheckInterpretation.Contract);
        agent.PersistentSessionId.ShouldBeNull("nothing was started — no control service is wired here");
    }

    [Test]
    public async Task the_deny_all_tool_hook_is_written_into_its_scratch_directory()
    {
        // The contract SAYS use no tools; this is what refuses one. Wider than the orchestrator's
        // edit-only hook on purpose — the specialist reads a bundle it was handed and needs nothing.
        using var scratch = new TempWorkspace();

        await EnsureAsync(SettingsFor(scratch.Path));

        var hookPath = Path.Combine(scratch.Path, ".claude", "settings.json");
        File.Exists(hookPath).ShouldBeTrue();
        var content = await File.ReadAllTextAsync(hookPath);
        content.ShouldBe(CheckInterpretation.DenyAllToolsSettingsJson);
        content.ShouldContain("\"PreToolUse\"");
        content.ShouldContain(
            "\"matcher\": \"*\"",
            customMessage: "a matcher naming specific tools would leave every other tool allowed");
        content.ShouldContain("exit 2", customMessage: "exit 2 is what feeds the refusal back to the model");
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
        (await verify.Agents.CountAsync(a => a.Slug == settings.CheckInterpreterAgentSlug)).ShouldBe(1);
    }

    [Test]
    public async Task deleting_the_agent_is_recoverable_the_next_call_recreates_it()
    {
        // Deleting the specialist must not be a permanent loss of the feature: the check that finds
        // it gone degrades to a digest, and the one after that has a warm agent again.
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
        recreated.SystemPromptAppend.ShouldBe(CheckInterpretation.Contract);
        recreated.AlwaysOn.ShouldBeTrue();
    }

    [Test]
    public async Task a_hand_edited_contract_is_reconciled_back_to_the_constant()
    {
        // The code is the source of truth and the row is its projection: edit the constant in a PR
        // and the live agent updates itself; edit the row in the UI and it does not stick.
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
        reconciled.SystemPromptAppend.ShouldBe(CheckInterpretation.Contract);
        reconciled.SystemPromptAppend!.ShouldContain("NEVER say the task is complete");
        reconciled.SystemPromptAppend!.ShouldContain($"contract v{CheckInterpretation.ContractVersion}");
    }

    [Test]
    public async Task a_workspace_that_was_cleaned_up_is_healed_on_the_next_call()
    {
        // Temp paths get swept and directories get deleted by hand. A specialist that silently
        // regained tool access because its hook file vanished is the one failure prose cannot cover.
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
        settings.CheckInterpreterEnabled = false;

        (await EnsureAsync(settings)).ShouldBeNull();

        await using var verify = CreateContext();
        (await verify.Agents.AnyAsync(a => a.Slug == settings.CheckInterpreterAgentSlug)).ShouldBeFalse();
        Directory.Exists(Path.Combine(scratch.Path, ".claude")).ShouldBeFalse("not even the hook file");
    }

    // ---- the derived working directory ----------------------------------------------------------

    [Test]
    public void an_unconfigured_directory_is_derived_from_the_first_allowed_root()
    {
        var settings = new DelegationSettings { AllowedRoots = ["C:\\src\\Example", "C:\\src\\Other"] };

        CheckInterpreterProvisioner.ResolveWorkingDirectory(settings)
            .ShouldBe(Path.Combine("C:\\src\\Example", ".antiphon", "check-interpreter"));
    }

    [Test]
    public void with_no_allowed_roots_it_still_lands_somewhere_of_its_own()
    {
        // The requirement is not WHERE, it is "not a directory the operator or another agent also
        // runs Claude in" — that is what keeps its transcript root its own.
        var settings = new DelegationSettings();

        CheckInterpreterProvisioner.ResolveWorkingDirectory(settings)
            .ShouldBe(Path.Combine(Path.GetTempPath(), "antiphon", "check-interpreter"));
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static DelegationSettings SettingsFor(string directory) => new()
    {
        // Unique per test: the fixture database is shared and Agent.Slug is uniquely indexed.
        CheckInterpreterAgentSlug = $"check-interp-{Guid.NewGuid():N}"[..24],
        CheckInterpreterWorkingDirectory = directory,
    };

    private static async Task<Agent?> EnsureAsync(DelegationSettings settings)
    {
        await using var db = CreateContext();
        var provisioner = new CheckInterpreterProvisioner(
            db, Options.Create(settings), TimeProvider.System,
            NullLogger<CheckInterpreterProvisioner>.Instance);
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
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-interp-test").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
