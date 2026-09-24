using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class ExpectationDirectiveTests
{
    private static readonly Guid AgentId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");
    private static readonly Guid BoardId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb1");
    private static readonly Guid OtherBoardId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb2");
    private static readonly Guid CardId = Guid.Parse("cccccccc-cccc-cccc-cccc-ccccccccccc1");
    private static readonly Guid ChannelId = Guid.Parse("dddddddd-dddd-dddd-dddd-ddddddddddd1");
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 2, 6, 23, TimeSpan.Zero);

    [Test]
    public async Task C650_Rejects_invalid_and_duplicate_directives()
    {
        var validator = new ExpectationWatchdogSettingsValidator();

        var valid = validator.Validate(null, Settings(ValidDirective()));
        valid.Succeeded.ShouldBeTrue();
        valid.Failures.ShouldBeNull();

        Failures(validator.Validate(null, Settings(ValidDirective(), ValidDirective()))).ShouldContain("duplicate directive id");

        var secondBoard = ValidDirective();
        secondBoard.Id = "later";
        Failures(validator.Validate(null, Settings(ValidDirective(), secondBoard))).ShouldContain("duplicate enabled board");

        var disabledTwin = ValidDirective();
        disabledTwin.Id = "later";
        disabledTwin.Enabled = false;
        validator.Validate(null, Settings(ValidDirective(), disabledTwin)).Succeeded.ShouldBeTrue();

        var duplicateRunner = ValidDirective();
        duplicateRunner.Targets.Add(new ExpectationTargetSettings
        {
            RunnerId = "server2",
            InFlightTarget = 1,
            Candidates = [Candidate("backup")],
        });
        Failures(validator.Validate(null, Settings(duplicateRunner))).ShouldContain("duplicate runner");

        var zeroTarget = ValidDirective();
        zeroTarget.Targets[0].InFlightTarget = 0;
        Failures(validator.Validate(null, Settings(zeroTarget))).ShouldContain("InFlightTarget must be positive");

        var noCandidates = ValidDirective();
        noCandidates.Targets[0].Candidates = [];
        Failures(validator.Validate(null, Settings(noCandidates))).ShouldContain("at least one candidate");

        var blankKey = ValidDirective();
        blankKey.Targets[0].Candidates[0].SubscriptionKey = " ";
        Failures(validator.Validate(null, Settings(blankKey))).ShouldContain("SubscriptionKey");

        var duplicateCandidate = ValidDirective();
        duplicateCandidate.Targets[0].Candidates.Add(Candidate("primary"));
        Failures(validator.Validate(null, Settings(duplicateCandidate))).ShouldContain("duplicate candidate");

        var notUtc = ValidDirective();
        notUtc.ActiveUntilUtc = new DateTimeOffset(2026, 9, 24, 3, 0, 0, TimeSpan.FromHours(1));
        Failures(validator.Validate(null, Settings(notUtc))).ShouldContain("ActiveUntilUtc must be UTC");

        var noTargets = ValidDirective();
        noTargets.Targets = [];
        Failures(validator.Validate(null, Settings(noTargets))).ShouldContain("at least one target");

        var disabledFeature = Settings(ValidDirective(), ValidDirective());
        disabledFeature.Enabled = false;
        Failures(validator.Validate(null, disabledFeature)).ShouldContain("duplicate directive id");

        await Task.CompletedTask;
    }

    [Test]
    public async Task C650_Validates_scope_and_recipient_references()
    {
        Codes(ValidDirective(), ValidCatalog()).ShouldBeEmpty();

        var wrongCard = ValidCatalog();
        Codes(ValidDirective(), wrongCard with
        {
            CardBoards = new Dictionary<Guid, Guid> { [CardId] = OtherBoardId },
        }).ShouldContain("audit_card_wrong_board");

        Codes(ValidDirective(), ValidCatalog() with
        {
            CardBoards = new Dictionary<Guid, Guid>(),
        }).ShouldContain("audit_card_missing");

        Codes(ValidDirective(), ValidCatalog() with
        {
            Agents = new Dictionary<Guid, ExpectationAgentReference>
            {
                [AgentId] = new(OtherBoardId, false),
            },
        }).ShouldContain("agent_wrong_board");

        var pool = ExpectationDirectiveReferences.Evaluate(ValidDirective(), ValidCatalog() with
        {
            Agents = new Dictionary<Guid, ExpectationAgentReference>
            {
                [AgentId] = new(BoardId, true),
            },
        });
        pool.Select(fault => fault.Code).ShouldContain("pool_agent");
        pool.Select(fault => fault.Code).ShouldNotContain("agent_wrong_board");

        Codes(ValidDirective(), ValidCatalog() with
        {
            Agents = new Dictionary<Guid, ExpectationAgentReference>(),
        }).ShouldContain("agent_missing");

        Codes(ValidDirective(), ValidCatalog() with
        {
            ChannelsEnabled = new Dictionary<Guid, bool> { [ChannelId] = false },
        }).ShouldContain("channel_disabled");

        Codes(ValidDirective(), ValidCatalog() with
        {
            ChannelsEnabled = new Dictionary<Guid, bool>(),
        }).ShouldContain("channel_missing");

        var runners = ExpectationDirectiveReferences.Evaluate(ValidDirective(), ValidCatalog() with
        {
            ConfiguredRunnerIds = new HashSet<string>(StringComparer.Ordinal),
        });
        runners.ShouldContain(fault => fault.Code == "runner_not_configured" && fault.Detail == "server2");
        runners.ShouldNotContain(fault => fault.Code == "runner_not_configured" && fault.Detail == "local");

        await Task.CompletedTask;
    }

    [Test]
    public async Task C650_Config_digest_changes_only_for_semantic_edits()
    {
        var original = ExpectationDirectiveDigest.Compute(ValidDirective());

        var reordered = ValidDirective();
        reordered.Targets =
        [
            new ExpectationTargetSettings
            {
                RunnerId = " server2 ",
                InFlightTarget = 3,
                Candidates = [Candidate(" claude-main ")],
            },
            new ExpectationTargetSettings
            {
                RunnerId = null,
                InFlightTarget = 3,
                Candidates =
                [
                    new ExpectationCandidateSettings
                    {
                        AgentKind = AgentKind.ClaudeCode,
                        ModelLevel = AgentModelLevel.High,
                        SubscriptionKey = " overflow ",
                    },
                    Candidate(" primary "),
                ],
            },
        ];
        ExpectationDirectiveDigest.Compute(reordered).ShouldBe(original);

        var targetChanged = ValidDirective();
        targetChanged.Targets[0].InFlightTarget = 4;
        ExpectationDirectiveDigest.Compute(targetChanged).ShouldNotBe(original);

        var keyChanged = ValidDirective();
        keyChanged.Targets[0].Candidates[0].SubscriptionKey = "backup";
        ExpectationDirectiveDigest.Compute(keyChanged).ShouldNotBe(original);

        var boardChanged = ValidDirective();
        boardChanged.BoardId = OtherBoardId;
        ExpectationDirectiveDigest.Compute(boardChanged).ShouldNotBe(original);

        await Task.CompletedTask;
    }

    [Test]
    public async Task C650_Disabled_or_expired_directive_has_no_effects()
    {
        var directive = ValidDirective();
        var enabled = Settings(directive);
        ExpectationDirectiveActivity.HasEffects(enabled, directive, Now).ShouldBeTrue();

        ExpectationDirectiveActivity.HasEffects(
            new ExpectationWatchdogSettings { Enabled = false, Directives = [directive] },
            directive,
            Now).ShouldBeFalse();

        directive.Enabled = false;
        ExpectationDirectiveActivity.HasEffects(enabled, directive, Now).ShouldBeFalse();

        directive.Enabled = true;
        directive.ActiveUntilUtc = Now;
        ExpectationDirectiveActivity.HasEffects(enabled, directive, Now).ShouldBeFalse();
        directive.ActiveUntilUtc = Now.AddMinutes(-1);
        ExpectationDirectiveActivity.HasEffects(enabled, directive, Now).ShouldBeFalse();
        directive.ActiveUntilUtc = Now.AddMinutes(1);
        ExpectationDirectiveActivity.HasEffects(enabled, directive, Now).ShouldBeTrue();
        directive.ActiveUntilUtc = null;
        ExpectationDirectiveActivity.HasEffects(enabled, directive, Now).ShouldBeTrue();

        await Task.CompletedTask;
    }

    private static ExpectationWatchdogSettings Settings(params ExpectationDirectiveSettings[] directives) =>
        new() { Enabled = true, Directives = directives.ToList() };

    private static ExpectationDirectiveSettings ValidDirective() => new()
    {
        Id = "tonight",
        AgentId = AgentId,
        BoardId = BoardId,
        AuditCardId = CardId,
        OperatorChannelId = ChannelId,
        Enabled = true,
        Targets =
        [
            new ExpectationTargetSettings
            {
                RunnerId = null,
                InFlightTarget = 3,
                Candidates =
                [
                    Candidate("primary"),
                    new ExpectationCandidateSettings
                    {
                        AgentKind = AgentKind.ClaudeCode,
                        ModelLevel = AgentModelLevel.High,
                        SubscriptionKey = "overflow",
                    },
                ],
            },
            new ExpectationTargetSettings
            {
                RunnerId = "server2",
                InFlightTarget = 3,
                Candidates = [Candidate("claude-main", AgentKind.ClaudeCode, AgentModelLevel.High)],
            },
        ],
    };

    private static ExpectationCandidateSettings Candidate(
        string key,
        AgentKind kind = AgentKind.Grok,
        AgentModelLevel level = AgentModelLevel.Frontier) =>
        new() { AgentKind = kind, ModelLevel = level, SubscriptionKey = key };

    private static ExpectationReferenceCatalog ValidCatalog() => new(
        new Dictionary<Guid, Guid> { [CardId] = BoardId },
        new Dictionary<Guid, ExpectationAgentReference> { [AgentId] = new(BoardId, false) },
        new Dictionary<Guid, bool> { [ChannelId] = true },
        new HashSet<string>(StringComparer.Ordinal) { "server2" });

    private static string Failures(Microsoft.Extensions.Options.ValidateOptionsResult result) =>
        result.Failures is null ? string.Empty : string.Join("\n", result.Failures);

    private static List<string> Codes(ExpectationDirectiveSettings directive, ExpectationReferenceCatalog catalog) =>
        ExpectationDirectiveReferences.Evaluate(directive, catalog).Select(fault => fault.Code).ToList();
}
