using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("MessageQueue")]
public class PhoneHomeImmediateSendTests
{
    [Test]
    public async Task C696Red_ModeNow_before_first_List_returns_503_without_mutation()
    {
        await using var h = await PhoneHomeOutageHarness.CreateAsync();
        using var http = h.Http; // Finish Program's startup before taking the state snapshot.
        var before = await h.DurableStateAsync();
        using var response = await http.PostAsJsonAsync($"/api/sessions/{h.SessionId}/messages", new { body = "now before first List", mode = "Now" });
        await AssertUnavailableAsync(response);
        (await h.DurableStateAsync()).ShouldBe(before);
        h.Host.Local.Calls.ShouldNotContain("input");
    }

    [Test]
    public async Task C696Red_SendNow_in_reconnect_gap_returns_503_without_mutation()
    {
        await using var h = await PhoneHomeOutageHarness.CreateAsync();
        await h.Queue.EnqueueAsync(h.SessionId, "previously attempted input", MessageSendMode.WhenIdle, CancellationToken.None);
        var id = (await h.RowsAsync()).Single().Id;
        await using (var db = h.Db())
            await db.SessionQueuedMessages.Where(m => m.Id == id).ExecuteUpdateAsync(u => u
                .SetProperty(m => m.DeliveryAttempts, 1)
                .SetProperty(m => m.LastDeliveryStartedAt, DateTime.UtcNow.AddMinutes(-2))
                .SetProperty(m => m.LastDeliveryGeneration, DateTime.UtcNow.AddMinutes(-3))
                .SetProperty(m => m.LastDeliveryBaselineSequence, 7L)
                .SetProperty(m => m.DeliveryVerdict, DeliveryVerdict.NoTranscriptRecord));
        await using var a = await h.RecoverAsync();
        await h.DisconnectAsync(a);
        using var http = h.Http;
        var before = await h.DurableStateAsync();
        using var response = await http.PostAsJsonAsync($"/api/sessions/{h.SessionId}/messages/{id}/send-now", new { });
        await AssertUnavailableAsync(response);
        (await h.DurableStateAsync()).ShouldBe(before);
        a.RequestCount(PhoneHomeOperation.Input).ShouldBe(0);
    }

    internal static async Task AssertUnavailableAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable, body);
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("code").GetString().ShouldBe(PhoneHomeProblemTypes.Unavailable);
    }
}
