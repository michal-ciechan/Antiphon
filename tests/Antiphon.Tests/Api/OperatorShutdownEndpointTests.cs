using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Infrastructure.Security;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Api;

/// <summary>CARD-0716 D-2: POST /api/operator/shutdown is operator-token gated and then stops the host.</summary>
[Category("Integration")]
public class OperatorShutdownEndpointTests
{
    [Test]
    public async Task Shutdown_without_the_operator_token_is_403_and_stops_nothing()
    {
        await using var host = await PhoneHomeTestHost.StartAsync();
        using var response = await host.PostOperatorAsync(
            "/api/operator/shutdown", new { reason = "restart-apphost" }, token: null);
        ((int)response.StatusCode).ShouldBe(403);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("operator_token_required");
        await Task.Delay(500);
        host.App.Lifetime.ApplicationStopping.IsCancellationRequested.ShouldBeFalse();
    }

    [Test]
    public async Task Shutdown_with_the_token_answers_202_then_begins_stopping()
    {
        await using var host = await PhoneHomeTestHost.StartAsync();
        using (var version = await host.Http.GetAsync("/api/version"))
        {
            version.EnsureSuccessStatusCode();
            var versionBody = await version.Content.ReadAsStringAsync();
            versionBody.ShouldContain("operator-shutdown-v1");
        }

        var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = host.App.Lifetime.ApplicationStopping.Register(() => stopping.TrySetResult());
        var token = OperatorTokenFile.ReadOrCreate(host.OperatorTokenPath);
        using var response = await host.PostOperatorAsync(
            "/api/operator/shutdown", new { reason = "restart-apphost" }, token);
        ((int)response.StatusCode).ShouldBe(202);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("accepted").GetBoolean().ShouldBeTrue();
        dto.GetProperty("pid").GetInt32().ShouldBe(Environment.ProcessId);

        var completed = await Task.WhenAny(stopping.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        completed.ShouldBe(stopping.Task);
    }
}
