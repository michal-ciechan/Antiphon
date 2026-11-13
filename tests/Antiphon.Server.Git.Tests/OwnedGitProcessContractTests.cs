using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Unit")]
public sealed class OwnedGitProcessContractTests
{
    [Test]
    public void Shared_owner_contract_lives_in_the_git_assembly()
    {
        // Baseline witness: the three callers have no shared invocation owner.
        var owner = typeof(ServerGitProcess).Assembly.GetType(
            "Antiphon.Server.Infrastructure.Git.OwnedGitProcess");
        owner.ShouldNotBeNull("CARD-1850 S1 needs an invocation owner independent of the server");
        typeof(IDisposable).IsAssignableFrom(owner).ShouldBeTrue();
    }
}
