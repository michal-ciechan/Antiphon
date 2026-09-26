using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0738 V-19: the sweep route previews unless apply is set. Rows are fleet-global.</summary>
[NotInParallel]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
[Category("Integration")]
public sealed class ClosedCardSweepApiTests
{
    private readonly AntiphonWebAppFactory _factory;

    public ClosedCardSweepApiTests(AntiphonWebAppFactory factory) => _factory = factory;

    [Test]
    public async Task the_sweep_route_answers_with_a_preview_by_default()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/agent-tasks/closed-card-sweep", new { });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("applied").GetBoolean().ShouldBeFalse();
        doc.RootElement.GetProperty("rows").ValueKind.ShouldBe(JsonValueKind.Array);
    }
}
