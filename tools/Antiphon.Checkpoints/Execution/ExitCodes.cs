namespace Antiphon.Checkpoints;

public static class ExitCodes
{
    public const int Green = 0;
    public const int FailedTests = 1;
    public const int Invalid = 2;
    public const int RosterOrMin = 3;
    public const int SlotTimeout = 4;
    public const int Timeout = 5;
    public const int ExecutorCrashed = 6;
    public const int StillRunning = 75;

    // Highest precedence first. PC-10 swaps the 1 and 2 entries.
    private static readonly int[] Precedence = [2, 6, 4, 5, 1, 3, 0];

    public static int FromRowStates(IEnumerable<int> states)
    {
        var set = states as ISet<int> ?? states.ToHashSet();
        foreach (var code in Precedence)
        {
            if (set.Contains(code))
                return code;
        }

        return Green;
    }
}
