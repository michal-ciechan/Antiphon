using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

[Category("Integration")]
public sealed partial class HerdrPaneDisposalHttpWireTests
{
    [Test]
    public async Task Refusal_and_durable_status_round_trip_through_both_route_families()
    {
        await using var h = new HerdrDisposalHttpFixture();
        await h.StartAsync();
        h.Runner.Processes.Complete = false;
        using var response = await h.Http.PostAsJsonAsync("/api/herdr/pane-disposals/preview",
            new HerdrPaneDisposalPreviewRequest(h.Runner.PaneId, h.Runner.SessionId));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var preview = (await response.Content.ReadFromJsonAsync<HerdrPaneDisposalPreview>())!;
        preview.Eligible.ShouldBeFalse();
        var request = new HerdrPaneDisposalRequest(Guid.NewGuid(), preview.PreviewId, "leftover selected by operator");
        var methods = h.Runner.Methods;
        using var refused = await h.Http.PostAsJsonAsync("/api/herdr/pane-disposals", request);
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().ShouldBe(HerdrPaneDisposalCodes.IdentityUnproven);
        problem.GetProperty("operationId").GetGuid().ShouldBe(request.OperationId);
        var receipt = problem.GetProperty("receipt").Deserialize<HerdrPaneDisposalReceipt>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        receipt.PaneLeftOpen.ShouldBeNull();
        receipt.Outcome.ShouldBe("Refused");
        using var status = await h.Http.GetAsync($"/api/herdr/pane-disposals/{request.OperationId:D}");
        status.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await status.Content.ReadFromJsonAsync<HerdrPaneDisposalReceipt>()).ShouldBe(receipt);
        h.Runner.Methods.ShouldBe(methods);
        (await h.Runner.RecreateService().GetAsync(request.OperationId, CancellationToken.None)).ShouldBe(receipt);
    }

    [Test]
    public async Task Old_runner_refuses_before_any_disposal_request()
    {
        await using var h = new HerdrDisposalHttpFixture { AdvertiseCapability = false };
        await h.StartAsync();
        using var response = await h.Http.PostAsJsonAsync("/api/herdr/pane-disposals",
            new HerdrPaneDisposalRequest(Guid.NewGuid(), Guid.NewGuid(), "test"));
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().ShouldBe(HerdrPaneDisposalCodes.GuardUnavailable);
        h.RunnerDisposalRequests.ShouldBe(0);
        h.Runner.Methods.ShouldBeEmpty();
    }

    [Test]
    public async Task Invalid_prefixes_missing_panes_and_unknown_operations_keep_typed_status()
    {
        await using var h = new HerdrDisposalHttpFixture();
        await h.StartAsync();
        using var invalid = await h.Http.PostAsJsonAsync("/api/herdr/pane-disposals/preview",
            new { paneId = h.Runner.PaneId, expectedSessionId = "1234" });
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var invalidPane = await h.Http.PostAsJsonAsync("/api/herdr/pane-disposals/preview",
            new HerdrPaneDisposalPreviewRequest("w1:p*", h.Runner.SessionId));
        invalidPane.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        h.Runner.Methods.ShouldBeEmpty();
        using var missing = await h.Http.PostAsJsonAsync("/api/herdr/pane-disposals/preview",
            new HerdrPaneDisposalPreviewRequest("w1:p999", h.Runner.SessionId));
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var unknown = await h.Http.GetAsync($"/api/herdr/pane-disposals/{Guid.NewGuid():D}");
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
