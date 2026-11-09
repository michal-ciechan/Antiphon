using System.Diagnostics;
using System.Globalization;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-1441 (CARD-1432 F-1, D-1). A non-inspection runner git child never runs a foreground
/// auto gc or auto maintenance. The environment form of <c>-c</c> (<c>GIT_CONFIG_COUNT/KEY_n/VALUE_n</c>)
/// leaves the argument vector unchanged. Inspection already isolates config and sets both keys.
/// </summary>
internal static class RunnerGitProcess
{
    /// <summary>Appends <c>gc.auto=0</c> and <c>maintenance.auto=false</c> after any existing entries.</summary>
    internal static void DisableAutoMaintenance(ProcessStartInfo start)
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
