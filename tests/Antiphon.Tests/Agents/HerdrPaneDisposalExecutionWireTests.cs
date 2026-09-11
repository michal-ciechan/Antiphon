using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

public sealed partial class HerdrPaneDisposalHttpWireTests
{
    [Test] [Arguments("Closed")] [Arguments("Unknown")] [Arguments("Refused")]
    public async Task All_disposal_outcomes_round_trip(string outcome)
    {
        await using var h = new HerdrDisposalHttpFixture(); await h.StartAsync();
        if (outcome == "Refused") h.Runner.Processes.Complete = false;
        h.Runner.Backend.DropAfterClose = outcome == "Unknown";
        h.Runner.Processes.Alive = outcome == "Unknown" ? null : false;
        var p = await h.Runner.PreviewAsync(); var request = h.Runner.Request(p);
        using var r = await h.Http.PostAsJsonAsync("/api/herdr/pane-disposals", request);
        r.StatusCode.ShouldBe(outcome switch { "Closed" => HttpStatusCode.OK, "Unknown" => HttpStatusCode.ServiceUnavailable, _ => HttpStatusCode.Conflict });
        var bytes = await r.Content.ReadAsStringAsync(); bytes.ShouldNotContain("secret-canary"); bytes.ShouldNotContain("secret-home");
        var json = JsonDocument.Parse(bytes).RootElement;
        var receipt = (r.IsSuccessStatusCode ? json : json.GetProperty("receipt")).Deserialize<HerdrPaneDisposalReceipt>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        receipt.Outcome.ShouldBe(outcome); receipt.OperationId.ShouldBe(request.OperationId);
        if (outcome == "Unknown") receipt.PaneLeftOpen.ShouldBeNull();
        using var status = await h.Http.GetAsync($"/api/herdr/pane-disposals/{request.OperationId}");
        var actual = (await status.Content.ReadFromJsonAsync<HerdrPaneDisposalReceipt>())!;
        actual.Outcome.ShouldBe(outcome); actual.OperationId.ShouldBe(request.OperationId);
        h.Runner.Methods.Count(m => m == "pane.close").ShouldBe(outcome == "Refused" ? 0 : 1);
    }
    [Test] public async Task C461_G015_Distinct_guard_wire()
    {
        await using var h = new HerdrDisposalHttpFixture(); await h.StartAsync(); var p = await h.Runner.PreviewAsync();
        using var response = await h.Http.PostAsJsonAsync("/api/herdr/pane-disposals", h.Runner.Request(p)); response.EnsureSuccessStatusCode();
        var close = h.Runner.Fake.Requests.Single(r => r.GetProperty("method").GetString() == "pane.close");
        close.GetProperty("params").EnumerateObject().Select(p => p.Name).ShouldBe(["pane_id"]);
        close.GetProperty("params").GetProperty("pane_id").GetString().ShouldBe(h.Runner.PaneId);
    }
    [Test] public Task C461_G109_Typed_status_receipt() => All_disposal_outcomes_round_trip("Unknown");
    [Test] public async Task Server_runner_status_round_trip_survives_lost_post_reply()
    {
        await using var h = new HerdrDisposalHttpFixture(); await h.StartAsync(); var p = await h.Runner.PreviewAsync(); var request = h.Runner.Request(p);
        h.Runner.Backend.DropAfterClose = true;
        using var post = await h.Http.PostAsJsonAsync("/api/herdr/pane-disposals", request); post.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        using var status = await h.Http.GetAsync($"/api/herdr/pane-disposals/{request.OperationId}");
        var r = (await status.Content.ReadFromJsonAsync<HerdrPaneDisposalReceipt>())!;
        r.Outcome.ShouldBe("AlreadyAbsent"); r.OperationId.ShouldBe(request.OperationId); r.PaneLeftOpen.ShouldBe(false);
        h.Runner.Methods.Count(m => m == "pane.close").ShouldBe(1);
    }
}
