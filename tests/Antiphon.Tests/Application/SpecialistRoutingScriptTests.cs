using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.Scripts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0415 V18. The specialist-routing command (Antiphon.Nightly.SpecialistRoutingCli, which replaced
/// scripts/specialist-routing.ps1 in unit 24 of the 2026-11-22 plan) in process against the in-process test server over
/// loopback: inspect, set, revalidate and disable with fresh concurrency tokens, and an incomplete pair refused before any
/// request.
/// </summary>
[Category("Integration")]
public class SpecialistRoutingScriptTests
{
    [Test]
    public async Task Card0415_V18_helper_inspects_sets_and_revalidates_with_fresh_tokens()
    {
        await using var h = await StandingSpecialistRoutingHttpTests.Harness.CreateAsync(network: true);
        var inspect = Run(h, "inspect");
        inspect.Exit.ShouldBe(0, inspect.All);
        using (var json = JsonDocument.Parse(inspect.Output))
            json.RootElement.GetProperty("primaryModelAlias").GetString().ShouldBe("sonnet");
        var set = Run(h, "set", "--candidates", "ClaudeCode/High,codex/low");
        set.Exit.ShouldBe(0, set.All);
        var saved = await h.GetAsync();
        saved.Candidates.ShouldBe(h.Pairs);
        var revalidate = Run(h, "revalidate");
        revalidate.Exit.ShouldBe(0, revalidate.All);
        var current = await h.GetAsync();
        current.ConcurrencyToken.ShouldNotBe(saved.ConcurrencyToken);
        current.CandidateStates.ShouldAllBe(c => c.Status != StandingSpecialistCandidateStatus.Qualified);
        var disabled = Run(h, "set", "--disable");
        disabled.Exit.ShouldBe(0, disabled.All);
        (await h.GetAsync()).Enabled.ShouldBe(false);
        (await h.GetAsync()).Candidates.ShouldBe(h.Pairs, "--disable alone re-sends the current candidates");
    }

    [Test]
    public async Task Card0415_V18_helper_rejects_incomplete_pairs_without_changing_configuration()
    {
        await using var h = await StandingSpecialistRoutingHttpTests.Harness.CreateAsync(network: true);
        var invalid = Run(h, "set", "--candidates", "Codex");
        invalid.Exit.ShouldBe(1);
        invalid.Error.Trim().ShouldBe("Invalid candidate 'Codex'; use a complete ClaudeCode/Level or Codex/Level pair.");
        (await h.GetAsync()).ConcurrencyToken.ShouldBeNull();
        var revalidate = Run(h, "revalidate");
        revalidate.Exit.ShouldBe(1);
        revalidate.Error.Trim().ShouldBe("Declare a routing chain before requesting revalidation.");
    }

    [Test]
    public void Card0415_V18_helper_usage_errors_and_ascii_source()
    {
        File.ReadAllBytes(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "lib", "Antiphon.Nightly", "SpecialistRoutingCli.cs")).ShouldAllBe(b => b < 128);
        var agent = Guid.NewGuid().ToString("D");
        Should.Throw<NightlyUsageException>(() => SpecialistRoutingCliOptions.Parse(["inspect"], null, null)).Message.ShouldBe("--agent is required and must be a GUID");
        Should.Throw<NightlyUsageException>(() => SpecialistRoutingCliOptions.Parse(["inspect", "--agent", agent, "--disable"], null, null)).Message
            .ShouldBe("--candidates and --disable apply only to set");
        Should.Throw<NightlyUsageException>(() => SpecialistRoutingCliOptions.Parse(["set", "--agent", agent], null, null)).Message
            .ShouldBe("set requires --candidates Kind/Level,... or --disable");
        SpecialistRoutingCliOptions.Parse(["--agent", agent], null, null).Verb.ShouldBe("inspect");
    }

    private static BoardCommandRun.Result Run(StandingSpecialistRoutingHttpTests.Harness h, params string[] arguments) =>
        BoardCommandRun.Run(host => SpecialistRoutingCli.Run(
            SpecialistRoutingCliOptions.Parse([.. arguments, "--agent", h.AgentId.ToString("D")], h.Client.BaseAddress!.ToString(), null),
            host, NightlyReport.HttpTransport(timeout: TimeSpan.FromSeconds(30))));
}
