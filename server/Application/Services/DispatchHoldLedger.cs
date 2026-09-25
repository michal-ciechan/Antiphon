namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0672 D-3: how a queued task's current stint was spent, per hold class, derived from its
/// stored <c>Held</c> rows alone so it is restart-safe with no new column (as CARD-0535's
/// escalation is). A stint runs from one <c>Held</c> row's instant to the next row's, and the last
/// one runs to <c>now</c>: the dispatcher writes a <c>Held</c> row only when the reason changes, so
/// the row in force is the reason for the whole interval.
/// </summary>
public sealed class DispatchHoldLedger
{
    private readonly Dictionary<DispatchHoldClass, double> _seconds;

    private DispatchHoldLedger(Dictionary<DispatchHoldClass, double> seconds, DispatchHoldClass? dominant)
    {
        _seconds = seconds;
        Dominant = dominant;
    }

    /// <summary>The class with the most time; <see cref="DispatchHoldClass.Lease"/> wins a tie. Null with no rows.</summary>
    public DispatchHoldClass? Dominant { get; }

    /// <summary>Whole seconds spent in <paramref name="holdClass"/>.</summary>
    public int SecondsFor(DispatchHoldClass holdClass) =>
        _seconds.TryGetValue(holdClass, out var seconds) ? (int)Math.Floor(seconds) : 0;

    /// <summary>Whole seconds in every class the ledger does not name on its own (scope, agent, landing, routing, other).</summary>
    public int OtherSeconds => (int)Math.Floor(_seconds
        .Where(p => p.Key is not (DispatchHoldClass.Lease or DispatchHoldClass.RemotePrep
            or DispatchHoldClass.Runner or DispatchHoldClass.Cap))
        .Sum(p => p.Value));

    /// <summary>The dominant class as the API names it: lower-case, or <c>none</c>.</summary>
    public string DominantName => Dominant is { } dominant ? NameOf(dominant) : "none";

    /// <param name="heldRows">The task's <c>Held</c> rows, in any order.</param>
    /// <param name="floor">The latest <c>Dispatched</c> instant; rows at or before it belong to an earlier stint.</param>
    /// <param name="now">Where the last stint ends.</param>
    public static DispatchHoldLedger FromRows(
        IEnumerable<(DateTime At, string Detail)> heldRows, DateTime floor, DateTime now)
    {
        var rows = heldRows.Where(r => r.At > floor).OrderBy(r => r.At).ToList();
        var seconds = new Dictionary<DispatchHoldClass, double>();
        for (var i = 0; i < rows.Count; i++)
        {
            var end = i + 1 < rows.Count ? rows[i + 1].At : now;
            var stint = Math.Max(0, (end - rows[i].At).TotalSeconds);
            var holdClass = DispatchHoldDetails.ClassOf(rows[i].Detail);
            seconds[holdClass] = (seconds.TryGetValue(holdClass, out var sum) ? sum : 0) + stint;
        }

        DispatchHoldClass? dominant = null;
        foreach (var holdClass in Enum.GetValues<DispatchHoldClass>())
        {
            // Enum order breaks ties, and Lease is first: lease starvation is what this names.
            if (seconds.TryGetValue(holdClass, out var value)
                && (dominant is null || value > seconds[dominant.Value]))
                dominant = holdClass;
        }

        return new DispatchHoldLedger(seconds, dominant);
    }

    /// <summary>
    /// The fields appended to a <c>HeldAged</c> escalation and a <c>DispatchHeld</c> item's evidence.
    /// </summary>
    public string Describe() =>
        $"leaseWait={SecondsFor(DispatchHoldClass.Lease)}s; prepWait={SecondsFor(DispatchHoldClass.RemotePrep)}s; "
        + $"runnerWait={SecondsFor(DispatchHoldClass.Runner)}s; capWait={SecondsFor(DispatchHoldClass.Cap)}s; "
        + $"otherWait={OtherSeconds}s; class={DominantName}";

    public static string NameOf(DispatchHoldClass holdClass) => holdClass.ToString().ToLowerInvariant();
}
