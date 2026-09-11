using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed class HerdrPaneDisposalEndpointTests
{
    [Test] [Arguments("p1")] [Arguments("w1:p*")] [Arguments("w1:p1,w1:p2")] [Arguments("current")]
    public async Task C461_G001_Exact_pane_target(string pane)
    {
        await using var h = new HerdrDisposalHttpFixture(); await h.StartAsync();
        using var response = await h.Http.PostAsJsonAsync("/api/herdr/pane-disposals/preview", new HerdrPaneDisposalPreviewRequest(pane, h.Runner.SessionId));
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest); h.Runner.Methods.ShouldBeEmpty();
    }
    [Test] [Arguments(null)] [Arguments("1234")] [Arguments("00000000-0000-0000-0000-000000000000")]
    public async Task C461_G002_Full_expected_identity(string? identity)
    {
        await using var h = new HerdrDisposalHttpFixture(); await h.StartAsync();
        using var r = await h.Http.PostAsJsonAsync("/api/herdr/pane-disposals/preview", new { paneId = h.Runner.PaneId, expectedSessionId = identity });
        r.StatusCode.ShouldBe(HttpStatusCode.BadRequest); h.Runner.Methods.ShouldBeEmpty();
    }
    [Test] [Arguments(false)] [Arguments(true)]
    public async Task C461_G009_Reason_required(bool longReason)
    {
        await using var h = new HerdrDisposalHttpFixture(); await h.StartAsync(); var p = await h.Runner.PreviewAsync();
        using var r = await h.Http.PostAsJsonAsync("/api/herdr/pane-disposals", new HerdrPaneDisposalRequest(Guid.NewGuid(), p.PreviewId, longReason ? new string('x', 4097) : " \n"));
        r.StatusCode.ShouldBe(HttpStatusCode.BadRequest); h.Runner.Backend.Closes.ShouldBe(0);
    }
    [Test] public async Task C461_G010_No_force_bypass()
    {
        await using var h = new HerdrDisposalHttpFixture(); await h.StartAsync();
        h.Runner.Backend.Transform = o => o with { Claims = [new(h.Runner.SessionId, "runtime", null, true)] };
        var p = await h.Runner.PreviewAsync();
        using var r = await h.Http.PostAsJsonAsync("/api/herdr/pane-disposals", new { operationId = Guid.NewGuid(), previewId = p.PreviewId, reason = "force attempt", force = true });
        r.StatusCode.ShouldBe(HttpStatusCode.Conflict); h.Runner.Backend.Closes.ShouldBe(0);
    }
    [Test] public Task C461_G013_Runner_feature_gate() => new HerdrPaneDisposalHttpWireTests().Old_runner_refuses_before_any_disposal_request();
    [Test] public async Task C461_G106_Preview_response_redaction()
    {
        await using var h = new HerdrDisposalHttpFixture(); await h.StartAsync(); h.Runner.Occupied();
        h.Runner.Fake.SetPaneProcessInfo(h.Runner.PaneId, 4242, [(4243, @"C:\secret-home\grok.exe", new[] { "--session-id", h.Runner.SessionId.ToString(), "--key", "secret-canary" }, @"C:\secret-home")]);
        using var r = await h.Http.PostAsJsonAsync("/api/herdr/pane-disposals/preview", new HerdrPaneDisposalPreviewRequest(h.Runner.PaneId, h.Runner.SessionId));
        r.StatusCode.ShouldBe(HttpStatusCode.OK); var json = await r.Content.ReadAsStringAsync();
        json.ShouldNotContain("secret-canary"); json.ShouldNotContain("secret-home");
    }
    [Test] public Task C461_G116_Receipt_response_redaction() => new HerdrPaneDisposalHttpWireTests().All_disposal_outcomes_round_trip("Closed");
    [Test] public Task Routes_validate_preview_execute_and_status() => new HerdrPaneDisposalHttpWireTests().Invalid_prefixes_missing_panes_and_unknown_operations_keep_typed_status();
}
