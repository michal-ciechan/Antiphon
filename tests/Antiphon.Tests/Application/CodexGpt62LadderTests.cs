using System.Text.RegularExpressions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Infrastructure;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// Codex High and Medium move to <c>gpt-6.2-sol</c> (floor 0.169.1). Frontier stays
/// <c>gpt-6-astra</c> with no enforced floor. <c>gpt-6.2-astra</c> is recognized and
/// floored, and it is not a ladder rung. The runner image pin must be at least that floor.
/// </summary>
[Category("Unit")]
public sealed class CodexGpt62LadderTests
{
    private const string Sol62 = "gpt-6.2-sol";
    private const string Astra62 = "gpt-6.2-astra";
    private const string Sol61 = "gpt-6.1-sol";
    private const string Floor62 = "0.169.1";
    private const string Floor61 = "0.159.1";
    private const string DocumentedAstraFloor = "0.153.4";
    private const string ImageVersion = "0.170.0";
    private const string NpmIntegrity =
        "sha512-6cCZJaZL17tiTAvhd1Pirt8vsV1dP8CfG94+IHygUO4m24oYLQrIInJJLyKzcMv/uUonbu1dF2JmcXhA/s5bsg==";
    private const string ImageSha512 =
        "e9c09925a64bd7bb624c0be17753e2aedf2fb15d5d3fc09f1bde3e207ca050ee26db8a182d0ac82272492f22b370cbffb94a276eed5d176266717840fece5bb2";

    [Test]
    public void High_and_medium_resolve_to_gpt_6_2_sol_frontier_and_low_stay()
    {
        ModelLevelAliases.ForCodex(AgentModelLevel.High).ShouldBe(Sol62);
        ModelLevelAliases.ForCodex(AgentModelLevel.Medium).ShouldBe(Sol62);
        ModelLevelAliases.ForCodex((AgentModelLevel)999).ShouldBe(Sol62);
        ModelLevelAliases.ForCodex(AgentModelLevel.Frontier).ShouldBe(ModelAlias.Gpt6Astra);
        ModelLevelAliases.ForCodex(AgentModelLevel.Low).ShouldBe(ModelAlias.Gpt56Luna);
        ModelLevelAliases.ForLaunch(AgentKind.Codex, AgentModelLevel.High).ShouldBe(Sol62);
        ModelLevelAliases.ForLaunch(AgentKind.Codex, AgentModelLevel.Medium).ShouldBe(Sol62);
        ModelLevelAliases.For(AgentKind.Codex, AgentModelLevel.High).ShouldBe(Sol62);

        foreach (AgentModelLevel level in Enum.GetValues<AgentModelLevel>())
            ModelLevelAliases.ForCodex(level).ShouldNotBe(Astra62);
        ModelLevelAliases.ForCodex(AgentModelLevel.High).ShouldNotBe(Sol61);
    }

    [Test]
    public void Floors_cover_6_2_sol_6_2_astra_and_historical_6_1_sol()
    {
        string? Floor(string? model) =>
            ModelLevelAliases.MinimumCodexCliVersion(AgentKind.Codex, model)?.ToString();

        ModelLevelAliases.ForCodexEntry(AgentModelLevel.High).ModelId.ShouldBe(Sol62);
        ModelLevelAliases.ForCodexEntry(AgentModelLevel.Medium).ModelId.ShouldBe(Sol62);
        (ModelLevelAliases.ForCodexEntry(AgentModelLevel.High).MinimumCliVersion?.ToString())
            .ShouldBe(Floor62);
        (ModelLevelAliases.ForCodexEntry(AgentModelLevel.Medium).MinimumCliVersion?.ToString())
            .ShouldBe(Floor62);
        Floor(Sol62).ShouldBe(Floor62);
        Floor("GPT-6.2-SOL").ShouldBe(Floor62);
        Floor(Astra62).ShouldBe(Floor62, "catalogued 6.2 Astra keeps the same floor off the ladder");
        Floor("GPT-6.2-ASTRA").ShouldBe(Floor62);
        Floor(Sol61).ShouldBe(Floor61);
        Floor("GPT-6.1-SOL").ShouldBe(Floor61);

        ModelLevelAliases.ForCodexEntry(AgentModelLevel.Frontier).MinimumCliVersion
            .ShouldBeNull("gpt-6-astra stays without an enforced MinimumCliVersion");
        Floor(ModelAlias.Gpt6Astra).ShouldBeNull();
        ModelLevelAliases.ForCodexEntry(AgentModelLevel.Low).MinimumCliVersion.ShouldBeNull();
        Floor(ModelAlias.Gpt56Luna).ShouldBeNull();

        var kinds = DockerStackDocuments.Read("docs/agent-kinds.md").Replace("\r\n", "\n");
        kinds.ShouldContain(DocumentedAstraFloor);
        kinds.ShouldContain("Frontier requires codex-cli " + DocumentedAstraFloor);
        kinds.ShouldContain(Sol62);
        kinds.ShouldContain(Floor62);
        kinds.ShouldNotContain("High and Medium both pin `gpt-6.1-sol`");
    }

    [Test]
    public void Bare_names_stay_historical_and_gpt_6_2_astra_is_not_a_ladder_rung()
    {
        ModelAlias.Normalize(AgentKind.Codex, "astra").ShouldBe(ModelAlias.Gpt6Astra);
        ModelAlias.Normalize(AgentKind.Codex, "sol").ShouldBe(ModelAlias.Gpt6Sol);
        ModelAlias.CanonicalHoldAlias("astra").ShouldBeNull();
        ModelAlias.CanonicalHoldAlias("sol").ShouldBeNull();
        ModelAlias.Normalize(AgentKind.Codex, "astra").ShouldNotBe(Astra62);
        ModelAlias.Normalize(AgentKind.Codex, "sol").ShouldNotBe(Sol62);

        ModelAlias.Normalize(AgentKind.Codex, Sol62).ShouldBe(Sol62);
        ModelAlias.Normalize(AgentKind.Codex, "GPT-6.2-Sol").ShouldBe(Sol62);
        ModelAlias.Normalize(AgentKind.Codex, Astra62).ShouldBe(Astra62);
        ModelAlias.Normalize(AgentKind.Codex, "GPT-6.2-Astra").ShouldBe(Astra62);
        ModelAlias.Normalize(AgentKind.Codex, Sol61).ShouldBe(ModelAlias.Gpt61Sol);

        ModelAlias.CanonicalHoldAlias(Sol62).ShouldBe(Sol62);
        ModelAlias.CanonicalHoldAlias(Astra62).ShouldBe(Astra62);
        ModelAlias.CanonicalHoldAlias(Sol61).ShouldBeNull("6.1 Sol remains recognizable, not current");

        var codex = ModelAlias.DelegatableAliases
            .Where(entry => entry.Kind == AgentKind.Codex)
            .Select(entry => entry.Alias)
            .ToArray();
        codex.ShouldBe([ModelAlias.Gpt6Astra, Sol62, ModelAlias.Gpt56Luna, Astra62]);
        codex.ShouldNotContain(Sol61);
    }

    [Test]
    public void Dockerfile_pin_matches_the_published_0_170_0_tarball_and_clears_the_floor()
    {
        var body = DockerStackDocuments.Read("docker/session-runner-grok/Dockerfile").Replace("\r\n", "\n");
        var version = Regex.Match(body, @"ARG CODEX_VERSION=([0-9]+\.[0-9]+\.[0-9]+)\n");
        version.Success.ShouldBeTrue();
        version.Groups[1].Value.ShouldBe(ImageVersion);

        var digest = Regex.Match(body, @"ARG CODEX_SHA512=([0-9a-f]{128})\n");
        digest.Success.ShouldBeTrue();
        var published = Convert.ToHexStringLower(Convert.FromBase64String(NpmIntegrity["sha512-".Length..]));
        digest.Groups[1].Value.ShouldBe(published);
        digest.Groups[1].Value.ShouldBe(ImageSha512);

        var pinned = CodexCliVersion.Parse(version.Groups[1].Value).ShouldNotBeNull();
        var floor = CodexCliVersion.Parse(Floor62).ShouldNotBeNull();
        pinned.CompareTo(floor).ShouldBeGreaterThanOrEqualTo(0);
        var solFloor = ModelLevelAliases.MinimumCodexCliVersion(AgentKind.Codex, Sol62).ShouldNotBeNull();
        solFloor.ToString().ShouldBe(Floor62);
        pinned.CompareTo(solFloor).ShouldBeGreaterThanOrEqualTo(0);

        foreach (var helper in new[]
        {
            "package/vendor/x86_64-unknown-linux-musl/bin/codex",
            "package/vendor/x86_64-unknown-linux-musl/bin/codex-code-mode-host",
            "package/vendor/x86_64-unknown-linux-musl/codex-path/rg",
            "package/vendor/x86_64-unknown-linux-musl/codex-resources/bwrap",
            "package/vendor/x86_64-unknown-linux-musl/codex-package.json",
            "HOME=/opt/codex-probe CODEX_HOME=/opt/codex-probe/home",
        })
            body.ShouldContain(helper);
    }
}
