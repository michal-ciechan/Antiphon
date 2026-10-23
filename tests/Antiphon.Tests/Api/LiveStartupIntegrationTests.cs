using System.Net;
using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Api;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
public sealed class LiveStartupIntegrationTests(AntiphonWebAppFactory factory)
{
    [Test]
    public async Task V9_Real_server_reports_startup_phases_after_listeners_are_ready(CancellationToken cancellationToken)
    {
        using var http = factory.CreateClient();
        using var response = await http.GetAsync("/api/diagnostics/startup", cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var rows = body.RootElement.GetProperty("phases").EnumerateArray().ToArray();
        var operations = rows.Select(x => x.GetProperty("operation").GetString()).ToArray();
        foreach (var operation in new[] { "config-load", "host-build", "migrate", "seed", "setup", "host-start", "listeners-ready" })
            operations.ShouldContain(operation);
        foreach (var row in rows)
        {
            row.GetProperty("elapsedMs").GetDouble().ShouldBeGreaterThanOrEqualTo(0);
            row.GetProperty("outcome").GetString().ShouldBe("completed");
        }
        // The host keeps the established refusing runner; startup cannot launch a live session.
        body.RootElement.GetProperty("runId").GetString().ShouldNotBeNullOrWhiteSpace();
    }
}
