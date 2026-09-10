using System.Text.Json;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

public sealed class HerdrPaneDisposalServiceTests
{
    [Test]
    [Arguments("")]
    [Arguments("p1")]
    [Arguments("w1:t1")]
    [Arguments("w1:p*")]
    [Arguments("w1:p1,w1:p2")]
    [Arguments("current")]
    [Arguments("w1:p1\n")]
    [Arguments("../w1:p1")]
    public async Task Invalid_target_refuses_before_backend_io(string pane)
    {
        await using var h = new HerdrPaneDisposalFixture();
        await Should.ThrowAsync<ArgumentException>(() => h.Service.PreviewAsync(new(pane, h.SessionId), CancellationToken.None));
        h.Methods.ShouldBeEmpty();
    }

    [Test]
    public async Task Expected_identity_is_mandatory_and_empty_uuid_is_invalid()
    {
        await using var h = new HerdrPaneDisposalFixture();
        foreach (var request in new[]
        {
            new HerdrPaneDisposalPreviewRequest(h.PaneId),
            new(h.PaneId, Guid.Empty), new(h.PaneId, null, Guid.Empty),
            new(h.PaneId, h.SessionId, Guid.Empty),
        })
            await Should.ThrowAsync<ArgumentException>(() => h.Service.PreviewAsync(request, CancellationToken.None));
        h.Methods.ShouldBeEmpty();
    }

    [Test]
    public async Task Protocol20_preview_is_read_only_ineligible_and_redacts_process_arguments()
    {
        await using var h = new HerdrPaneDisposalFixture();
        await h.StartAsync();
        h.Fake.SetPaneProcessInfo(h.PaneId, 42,
            [(43, @"C:\private-home\grok.exe", new[] { "grok", "--api-key", "secret-canary" }, @"C:\private-home")]);
        h.Fake.SetPaneAgentSession(h.PaneId, "antiphon", "uuid", h.SessionId.ToString("D"));
        var preview = await h.PreviewAsync();
        preview.Eligible.ShouldBeFalse();
        preview.GuardAvailable.ShouldBeFalse();
        preview.ProcessInventoryComplete.ShouldBeFalse();
        preview.Blockers.ShouldContain(HerdrPaneDisposalCodes.GuardUnavailable);
        preview.Blockers.ShouldContain(HerdrPaneDisposalCodes.IdentityUnproven);
        preview.WouldLeaveTabEmpty.ShouldBe(false);
        preview.Foreground.ShouldNotBeNull().Single().ExecutableName.ShouldBe("grok.exe");
        var json = JsonSerializer.Serialize(preview);
        json.ShouldNotContain("secret-canary");
        json.ShouldNotContain("private-home");
        h.Methods.ShouldAllBe(m => new[] { "ping", "pane.get", "pane.process_info", "workspace.list", "tab.list" }.Contains(m));
        Directory.Exists(h.Settings.SessionLogPath).ShouldBeFalse();
    }

    [Test]
    public async Task Refusal_is_durable_idempotent_and_never_dispatches_teardown()
    {
        await using var h = new HerdrPaneDisposalFixture();
        await h.StartAsync();
        var preview = await h.PreviewAsync();
        var request = new HerdrPaneDisposalRequest(Guid.NewGuid(), preview.PreviewId, "exact selected leftover");
        var methods = h.Methods;
        var receipts = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => h.Service.ExecuteAsync(request, CancellationToken.None)));
        receipts.ShouldAllBe(r => r == receipts[0]);
        receipts[0].Outcome.ShouldBe("Refused");
        receipts[0].PaneLeftOpen.ShouldBeNull();
        receipts[0].CleanupPending.ShouldBeFalse();
        h.Methods.ShouldBe(methods);
        h.RecreateService();
        (await h.Service.GetAsync(request.OperationId, CancellationToken.None)).ShouldBe(receipts[0]);
        (await h.Service.ExecuteAsync(request, CancellationToken.None)).ShouldBe(receipts[0]);
        var conflict = await Should.ThrowAsync<HerdrLaunchException>(() =>
            h.Service.ExecuteAsync(request with { Reason = "different" }, CancellationToken.None));
        conflict.Code.ShouldBe(HerdrPaneDisposalCodes.OperationConflict);
        h.Methods.ShouldBe(methods);
        Directory.GetFiles(Path.Combine(h.Settings.SessionLogPath, "herdr", "disposals")).Length.ShouldBe(1);
    }

    [Test]
    public async Task Expired_restarted_and_unknown_previews_never_create_receipts()
    {
        await using var h = new HerdrPaneDisposalFixture();
        await h.StartAsync();
        var first = await h.PreviewAsync();
        h.Clock.Offset = TimeSpan.FromMinutes(2);
        var expired = await Should.ThrowAsync<HerdrLaunchException>(() => h.Service.ExecuteAsync(
            new(Guid.NewGuid(), first.PreviewId, "expired"), CancellationToken.None));
        expired.Code.ShouldBe(HerdrPaneDisposalCodes.PreviewExpired);
        h.Clock.Offset = TimeSpan.Zero;
        var current = await h.PreviewAsync();
        h.RecreateService();
        foreach (var id in new[] { current.PreviewId, Guid.NewGuid() })
        {
            var error = await Should.ThrowAsync<HerdrLaunchException>(() => h.Service.ExecuteAsync(
                new(Guid.NewGuid(), id, "unknown"), CancellationToken.None));
            error.Code.ShouldBe(HerdrPaneDisposalCodes.PreviewInvalid);
        }
        Directory.Exists(h.Settings.SessionLogPath).ShouldBeFalse();
    }

    [Test]
    public async Task Missing_pane_does_not_mint_a_preview_or_claim_absence_of_reviewed_incarnation()
    {
        await using var h = new HerdrPaneDisposalFixture();
        await h.StartAsync();
        var error = await Should.ThrowAsync<HerdrLaunchException>(() => h.Service.PreviewAsync(
            new("w1:p999", h.SessionId), CancellationToken.None));
        error.Code.ShouldBe(HerdrProblemTypes.PaneNotFound);
        (await h.Service.GetAsync(Guid.NewGuid(), CancellationToken.None)).ShouldBeNull();
        Directory.Exists(h.Settings.SessionLogPath).ShouldBeFalse();
    }

    [Test]
    public async Task All_readable_locator_claims_survive_preview_and_refusal()
    {
        await using var h = new HerdrPaneDisposalFixture();
        await h.StartAsync();
        var sidecar = new HerdrPaneSidecar
        {
            SessionId = h.SessionId, WorkspaceKey = "test", WorkspaceId = "w1",
            TabId = "w1:t1", PaneId = h.PaneId, Origin = HerdrPaneOrigins.Attached,
        };
        sidecar.SaveAtomic(HerdrPaneSidecar.PathFor(h.Settings.SessionLogPath, h.SessionId));
        var other = Guid.NewGuid();
        var otherSidecar = sidecar with { SessionId = other, Origin = HerdrPaneOrigins.Launched };
        otherSidecar.SaveAtomic(HerdrPaneSidecar.PathFor(h.Settings.SessionLogPath, other));
        var before = File.ReadAllBytes(HerdrPaneSidecar.PathFor(h.Settings.SessionLogPath, h.SessionId));
        var preview = await h.PreviewAsync();
        preview.Claims.Select(c => c.SessionId).ShouldContain(h.SessionId);
        preview.Claims.Select(c => c.SessionId).ShouldContain(other);
        preview.Blockers.ShouldContain(HerdrProblemTypes.PaneBound);
        await h.Service.ExecuteAsync(new(Guid.NewGuid(), preview.PreviewId, "refuse"), CancellationToken.None);
        File.ReadAllBytes(HerdrPaneSidecar.PathFor(h.Settings.SessionLogPath, h.SessionId)).ShouldBe(before);
        File.Exists(HerdrPaneSidecar.PathFor(h.Settings.SessionLogPath, other)).ShouldBeTrue();
        Directory.Exists(HerdrLastPane.DirectoryFor(h.Settings.SessionLogPath)).ShouldBeFalse();
    }
}
