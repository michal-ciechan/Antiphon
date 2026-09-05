using System.Text.Json;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0398 D12: tracked appsettings must not grow AllowedRoots.</summary>
[Category("Unit")]
public class DelegationAllowedRootsFileTests
{
    [Test]
    public void tracked_appsettings_Delegation_has_no_AllowedRoots_key()
    {
        var path = Path.Combine(DelegateScriptRunner.RepoRoot, "server", "appsettings.json");
        File.Exists(path).ShouldBeTrue(path);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var delegation = doc.RootElement.GetProperty("Delegation");
        delegation.TryGetProperty("AllowedRoots", out _).ShouldBeFalse(
            "tracked Delegation:AllowedRoots is the silent grant CARD-0398 exists to forbid");
    }
}
