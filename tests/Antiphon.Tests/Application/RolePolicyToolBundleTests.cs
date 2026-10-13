using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0507 S1 — role-policy tool bundles: <c>Delegation:RolePolicy:&lt;Role&gt;:Bundles</c>, the
/// validating resolver, and where <see cref="InstructionBundles.ForDelegate"/> places the keys.
/// Pure: the catalog is embedded and the settings are built in code.
/// </summary>
[Category("Unit")]
public class RolePolicyToolBundleTests
{
    // V-1. Mutation: role keys emitted before the stage key -> red at index 0.
    [Test]
    public void a_review_worker_carries_stage_then_basics_then_role_tool_bundles_then_attachments()
    {
        InstructionBundles.ForDelegate(
                AgentTaskKind.Worker, AgentTaskRole.Review, attachedBundleKeys: null, roleBundleKeys: ["tool-x"])
            .ShouldBe(["stage-review", "delegate-basics", "tool-x"]);

        InstructionBundles.ForDelegate(
                AgentTaskKind.Worker, AgentTaskRole.Review, attachedBundleKeys: ["board-api"], roleBundleKeys: ["tool-x"])
            .ShouldBe(["stage-review", "delegate-basics", "tool-x", "board-api"]);
    }

    // V-1b. Mutation: role keys merged before the specialist return -> red at ShouldBeEmpty.
    [Test]
    public void a_specialist_task_ignores_role_tool_bundles()
    {
        foreach (var role in new[] { AgentTaskRole.Check, AgentTaskRole.Distill, AgentTaskRole.Diagnose })
        {
            InstructionBundles.ForDelegate(AgentTaskKind.Worker, role, null, ["tool-x"])
                .ShouldBeEmpty($"{role} is a specialist and carries no bundle");
        }
    }

    // V-2. board-api stands in for a tool bundle so this slice needs no tool file. Both paths deliver
    // the key; the composer renders it once. Mutation: composer dedupe removed -> count 2, red.
    [Test]
    public void a_role_tool_bundle_also_attached_composes_once()
    {
        var keys = InstructionBundles.ForDelegate(
            AgentTaskKind.Worker, AgentTaskRole.Review, attachedBundleKeys: ["board-api"], roleBundleKeys: ["board-api"]);
        keys.ShouldBe(["stage-review", "delegate-basics", "board-api", "board-api"]);

        var composed = InstructionBundleComposer.Compose(keys);

        composed.Bundles.Select(b => b.Key).ShouldBe(["stage-review", "delegate-basics", "board-api"]);
        CountOccurrences(composed.Text, "[bundle:board-api").ShouldBe(1);
    }

    // V-3. Mutation: the resolver skips an unknown key with a log -> row 1 red at Should.Throw.
    [Test]
    [Arguments("tool-does-not-exist", "tool-does-not-exist")]
    [Arguments("stage-code", "tool-")]
    [Arguments("board-api", "tool-")]
    [Arguments("style-terse", "tool-")]
    [Arguments("standing-instructions", "tool-")]
    [Arguments("tool-", "tool-")]
    public void the_resolver_refuses_unknown_and_non_tool_keys(string key, string named)
    {
        var settings = new DelegationSettings();
        settings.RolePolicy["Review"].Bundles = [key];

        var ex = Should.Throw<ValidationException>(
            () => InstructionBundles.ResolveRoleToolBundles(AgentTaskRole.Review, settings));

        ex.Code.ShouldBe("stage_tool_bundle_unknown");
        ex.StatusCode.ShouldBe(422);
        ex.Errors.Keys.ShouldBe(["RolePolicy"]);
        var error = ex.Errors["RolePolicy"].ShouldHaveSingleItem();
        error.ShouldContain("Review");
        error.ShouldContain(key);
        error.ShouldContain(named);
    }

    // V-3x. Custom and Check have no RolePolicy entry as shipped and must keep dispatching.
    // Mutation: throw on a missing role entry -> red.
    [Test]
    public void the_resolver_returns_nothing_for_an_empty_list_or_an_unlisted_role()
    {
        var settings = new DelegationSettings();
        settings.RolePolicy.ContainsKey("Custom").ShouldBeFalse();

        InstructionBundles.ResolveRoleToolBundles(AgentTaskRole.Review, settings).ShouldBeEmpty();
        InstructionBundles.ResolveRoleToolBundles(AgentTaskRole.Custom, settings).ShouldBeEmpty();
    }

    // V-4. The positive row lands with S2 (V-6a). Mutation: IsToolBundleKey returns
    // All.ContainsKey(key) -> the "stage-code" row red.
    [Test]
    [Arguments("stage-code")]
    [Arguments("delegate-basics")]
    [Arguments("style-terse")]
    [Arguments("standing-instructions")]
    [Arguments("tool-")]
    [Arguments("tool-missing")]
    [Arguments("TOOL-OPEN-CODE-REVIEW")]
    public void IsToolBundleKey_is_a_whitelist_of_prefix_and_catalog(string key)
    {
        InstructionBundles.IsToolBundleKey(key).ShouldBeFalse();
    }

    // V-5. Mutation: Review seeded with "tool-open-code-review" -> red.
    [Test]
    public void shipped_role_policy_lists_no_tool_bundles_on_any_role()
    {
        var policy = new DelegationSettings().RolePolicy;

        policy.ContainsKey("Review").ShouldBeTrue();
        policy.Values.ShouldAllBe(p => p.Bundles.Count == 0);
    }

    // V-5b. Mutation: [ConfigurationKeyName("BundleKeys")] on RolePolicyEntry.Bundles (PC-6).
    [Test]
    public void bundles_bind_from_configuration_per_role_without_disturbing_the_rest()
    {
        var settings = new DelegationSettings();
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Delegation:RolePolicy:Review:Bundles:0"] = "tool-open-code-review",
            })
            .Build()
            .GetSection("Delegation")
            .Bind(settings);

        settings.RolePolicy["Review"].Bundles.ShouldBe(["tool-open-code-review"]);
        settings.RolePolicy["Review"].Level.ShouldBe(AgentModelLevel.Frontier);
        settings.RolePolicy["Code"].Bundles.ShouldBeEmpty();
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
