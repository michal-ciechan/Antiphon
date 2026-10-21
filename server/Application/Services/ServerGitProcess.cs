using System.Diagnostics;
using System.Globalization;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1432 S1 (R3, D-1): a server git child never runs a foreground auto gc or auto maintenance,
/// so a leased land or dispatch cannot stall behind one. The environment form of <c>-c</c>
/// (<c>GIT_CONFIG_COUNT/KEY_n/VALUE_n</c>, same precedence) keeps the argument vector that the
/// profile, the mutation classifier and the test seams read unchanged.
/// </summary>
public static class ServerGitProcess
{
    /// <summary>Appends <c>gc.auto=0</c> and <c>maintenance.auto=false</c> after any existing entries.
    /// Call it after the caller's own environment set-up and before the process starts.</summary>
    public static void DisableAutoMaintenance(ProcessStartInfo start)
    {
        var count = 0;
        if (start.Environment.TryGetValue("GIT_CONFIG_COUNT", out var existing) && existing is not null
            && (!int.TryParse(existing, NumberStyles.None, CultureInfo.InvariantCulture, out count) || count < 0))
            throw new InvalidOperationException("git_config_count_invalid");
        Add(start, ref count, "gc.auto", "0");
        Add(start, ref count, "maintenance.auto", "false");
        start.Environment["GIT_CONFIG_COUNT"] = count.ToString(CultureInfo.InvariantCulture);
    }

    private static void Add(ProcessStartInfo start, ref int index, string key, string value)
    {
        var suffix = index.ToString(CultureInfo.InvariantCulture);
        start.Environment["GIT_CONFIG_KEY_" + suffix] = key;
        start.Environment["GIT_CONFIG_VALUE_" + suffix] = value;
        index++;
    }
}
