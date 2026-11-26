using Antiphon.Tests.Scripts.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Invariants;

[Category("Unit")]
[Category("RepositoryInvariant")]
public sealed class AwayDigestSendInvariantTests
{
    [Test]
    public void Command_and_server_use_the_same_send_route()
    {
        // The route the command posts to is the server's.
        File.ReadAllText(Path.Combine(NightlyPlanCases.Repo, "server/Api/Endpoints/DigestEndpoints.cs"))
            .ShouldContain("app.MapPost(\"/api/digest/send\"", Case.Sensitive);
        File.ReadAllText(Path.Combine(NightlyPlanCases.Repo, "scripts/lib/Antiphon.Nightly/AwayDigestSend.cs"))
            .ShouldContain("\"/api/digest/send\"", Case.Sensitive);
    }
}
