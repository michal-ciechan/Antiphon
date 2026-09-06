using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public class HerdrSupervisionFailureEvidenceTests
{
    [Test]
    [Arguments(AgentExitReason.Unknown, null)]
    [Arguments(AgentExitReason.ProcessExited, HerdrSupervisionFailureKind.NonQualifying)]
    [Arguments(AgentExitReason.KilledByRequest, HerdrSupervisionFailureKind.NonQualifying)]
    [Arguments(AgentExitReason.MemoryKilled, HerdrSupervisionFailureKind.NonQualifying)]
    [Arguments(AgentExitReason.CpuSpinKilled, HerdrSupervisionFailureKind.NonQualifying)]
    [Arguments(AgentExitReason.HerdrRestartPresumedDead, HerdrSupervisionFailureKind.NonQualifying)]
    [Arguments(AgentExitReason.HerdrPaneClosed, HerdrSupervisionFailureKind.PaneClosed)]
    [Arguments(AgentExitReason.HerdrChildGone, HerdrSupervisionFailureKind.ChildGone)]
    [Arguments(AgentExitReason.HerdrPaneLeftOpen, HerdrSupervisionFailureKind.NonQualifying)]
    [Arguments(AgentExitReason.HerdrDetached, HerdrSupervisionFailureKind.NonQualifying)]
    [Arguments(AgentExitReason.HerdrLaunchDetectTimeout, HerdrSupervisionFailureKind.DetectTimeout)]
    public void Every_exit_reason_maps_per_T1(AgentExitReason reason, HerdrSupervisionFailureKind? expected) =>
        HerdrSupervisionFailureEvidence.FromExitReason(reason).ShouldBe(expected);

    [Test]
    [Arguments("detect_timeout", HerdrSupervisionFailureKind.DetectTimeout)]
    [Arguments("pane_occupied", HerdrSupervisionFailureKind.NonQualifying)]
    [Arguments("grok_rules_argv_unsafe", HerdrSupervisionFailureKind.NonQualifying)]
    [Arguments("herdr_grok_native_session_missing", HerdrSupervisionFailureKind.NonQualifying)]
    [Arguments("plain", HerdrSupervisionFailureKind.NonQualifying)]
    public void Launch_failures_map_per_T2(string code, HerdrSupervisionFailureKind expected) =>
        HerdrSupervisionFailureEvidence.FromLaunchFailure(code == "plain"
            ? new InvalidOperationException("detect_timeout is prose, not evidence")
            : new ConflictException("same prose for every code", code)).ShouldBe(expected);

    [Test]
    public void Domain_enum_has_exactly_four_members_in_declared_order() =>
        Enum.GetNames<HerdrSupervisionFailureKind>().ShouldBe(["NonQualifying", "PaneClosed", "ChildGone", "DetectTimeout"]);

    [Test]
    public void HerdrFailureLimit_defaults_to_three() => new SupervisionSettings().HerdrFailureLimit.ShouldBe(3);

    [Test]
    [Arguments(0)] [Arguments(11)] [Arguments(-1)]
    public void HerdrFailureLimit_outside_1_to_10_fails_validation(int limit)
    {
        var result = new SupervisionSettingsValidator().Validate(null, new SupervisionSettings { HerdrFailureLimit = limit });
        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Supervision:HerdrFailureLimit");
    }

    [Test]
    [Arguments(1)] [Arguments(10)]
    public void HerdrFailureLimit_1_and_10_validate(int limit) =>
        new SupervisionSettingsValidator().Validate(null, new SupervisionSettings { HerdrFailureLimit = limit }).Succeeded.ShouldBeTrue();

    [Test]
    public void Model_has_the_evidence_and_state_columns_and_the_migration_adds_only_them()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=unused").Options);
        db.Model.FindEntityType(typeof(AgentSession))!.FindProperty("HerdrSupervisionFailureKind")!.IsNullable.ShouldBeTrue();
        var state = db.Model.FindEntityType(typeof(AgentSupervisionState))!;
        state.FindProperty("HerdrConsecutiveFailures")!.GetDefaultValue().ShouldBe(0);
        foreach (var name in new[] { "HerdrFailureHeldAt", "LastHerdrFailureKind", "LastHerdrObservedSessionId", "LastHerdrObservedStartedAt", "HerdrHealthySince" })
            state.FindProperty(name)!.IsNullable.ShouldBeTrue();
        var operations = new Antiphon.Server.Migrations.AddHerdrSupervisionHold().UpOperations;
        operations.Count.ShouldBe(7);
        operations.ShouldAllBe(o => o is Microsoft.EntityFrameworkCore.Migrations.Operations.AddColumnOperation);
    }
}
