using System.Text.Json;
using TUnit.Core;

namespace Antiphon.E2E.Fixtures;

public static class OwnedLandServerChild
{
    [Before(Assembly)]
    public static async Task StartOwnedChildAsync()
    {
        var settings = Environment.GetEnvironmentVariable("ANTIPHON_C467_CHILD");
        if (settings is null) return;
        var args = JsonSerializer.Deserialize<string[]>(settings)!;
        var owner = Path.GetFullPath(Path.Combine(AntiphonAppFixture.FindRepositoryRoot(), ".antiphon", "acceptance", "card-0467")) + Path.DirectorySeparatorChar;
        if (args.Length != 4 || !Path.GetFullPath(args[0]).StartsWith(owner, StringComparison.OrdinalIgnoreCase)
            || new Uri(args[1]).Port == 17204) throw new InvalidOperationException("Child resources are not fixture-owned");
        await LandDeliveryFixture.RunChildAsync(args[0], args[1], args[2], args[3]);
    }
}
