// CARD-1836 S4: the impact-ordered ready queue (plan D-5) with same-attempt trust-gate expansion (D-4). Tiers: 0 an
// unresolved failure or interruption (the CARD-1823 fold of RcDirtyOrder.Launch, which also refuses a carry over an
// open failure and never lets a same-tip pass discharge one), 1 an end-user product test directly exercising changed
// logic, 2 another direct hit (a trusted gate's own tests included), 3 a transitive dependent (held consumers
// included), 4 the remaining mandatory work (missing or unrun rows, periodic-Full remainder). Within a tier: prior
// verified Windows wall time, else the sum of priced members, ascending; unknown cost last; then the ordinal key. Held
// consumers wait for their gate; a red gate expands them into the same attempt and the queue is re-evaluated; a green
// gate with equal contracts carries them. No chunk is admitted twice. Diagnostic: Full still executes. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Antiphon.Nightly
{
    /// <summary>One obligation of the attempt.</summary>
    public sealed class RcImpactObligation
    {
        public string Key { get; set; } = "";
        /// <summary>Dirty (owed), Held (waits for Gate) or Carry (Origin names the receipt).</summary>
        public string Disposition { get; set; } = "Dirty";
        /// <summary>product-direct, direct, transitive or mandatory.</summary>
        public string Impact { get; set; } = "mandatory";
        public double? PriorWallSeconds { get; set; }
        public double? MemberSeconds { get; set; }
        /// <summary>For a held consumer: the gate obligation's key.</summary>
        public string Gate { get; set; } = "";
        /// <summary>For a gate: its B and A contract hashes were both present and equal.</summary>
        public bool ContractsEqual { get; set; }
        public string Origin { get; set; } = "";
    }

    public sealed class RcImpactLaunch
    {
        public string Key { get; set; } = "";
        public int Tier { get; set; }
        public int Rank { get; set; }
        public string CostSource { get; set; } = "";
        public double? Cost { get; set; }
        public string Outcome { get; set; } = "";
    }

    public sealed class RcImpactCarry
    {
        public string Key { get; set; } = "";
        public string Origin { get; set; } = "";
    }

    public sealed class RcImpactRun
    {
        public List<RcImpactLaunch> Launches { get; } = new List<RcImpactLaunch>();
        public List<RcImpactCarry> Carried { get; } = new List<RcImpactCarry>();
        public List<string> Expanded { get; } = new List<string>();
    }

    public static class RcImpactQueue
    {
        /// <summary>
        /// Dispatches the obligations one at a time through launch (it returns the chunk's outcome: pass or anything else)
        /// in impact order, re-evaluating the ready set after every completion. attempts/fixes/repositoryRoot/candidateSha
        /// are RcDirtyOrder.Launch's failure history.
        /// </summary>
        public static RcImpactRun Dispatch(IReadOnlyList<RcImpactObligation> obligations, object attempts, object fixes, string repositoryRoot, string candidateSha,
            Func<string, string> launch)
        {
            var byKey = new Dictionary<string, RcImpactObligation>(StringComparer.Ordinal);
            foreach (var o in obligations)
            {
                if (string.IsNullOrWhiteSpace(o.Key) || byKey.ContainsKey(o.Key)) throw new InvalidOperationException("duplicate-obligation");
                if (o.Disposition != "Dirty" && o.Disposition != "Held" && o.Disposition != "Carry") throw new InvalidOperationException("invalid-disposition");
                if (o.Disposition == "Held" && (string.IsNullOrEmpty(o.Gate) || !obligations.Any(g => g.Key == o.Gate && g.Disposition == "Dirty")))
                    throw new InvalidOperationException("held-without-gate");
                byKey.Add(o.Key, o);
            }
            // The CARD-1823 fold: open failures and interruptions, and the carry refusal. Held work folds as owed work.
            var fold = RcDirtyOrder.Launch(obligations.Select(o => new Dictionary<string, object>
            {
                ["key"] = o.Key, ["disposition"] = o.Disposition == "Carry" ? "Carry" : "Dirty",
            }).ToArray(), attempts, fixes, repositoryRoot, candidateSha);
            var open = new HashSet<string>(fold.Launch.Where(i => i.Band == "failed" || i.Band == "interrupted").Select(i => i.Key), StringComparer.Ordinal);

            var run = new RcImpactRun();
            var ready = new List<RcImpactObligation>();
            var waiting = new Dictionary<string, List<RcImpactObligation>>(StringComparer.Ordinal);
            foreach (var o in obligations)
            {
                if (o.Disposition == "Carry") run.Carried.Add(new RcImpactCarry { Key = o.Key, Origin = o.Origin });
                else if (o.Disposition == "Held")
                {
                    if (!waiting.TryGetValue(o.Gate, out var list)) waiting[o.Gate] = list = new List<RcImpactObligation>();
                    list.Add(o);
                }
                else ready.Add(o);
            }
            var admitted = new HashSet<string>(StringComparer.Ordinal);
            while (ready.Count > 0)
            {
                var next = ready.OrderBy(o => Tier(o, open)).ThenBy(o => Cost(o).Cost.HasValue ? 0 : 1).ThenBy(o => Cost(o).Cost ?? 0)
                    .ThenBy(o => o.Key, StringComparer.Ordinal).First();
                ready.Remove(next);
                if (!admitted.Add(next.Key)) throw new InvalidOperationException("duplicate-launch:" + next.Key);
                var cost = Cost(next);
                var outcome = launch(next.Key) ?? "";
                run.Launches.Add(new RcImpactLaunch
                {
                    Key = next.Key, Tier = Tier(next, open), Rank = run.Launches.Count + 1, CostSource = cost.Source, Cost = cost.Cost, Outcome = outcome,
                });
                if (waiting.TryGetValue(next.Key, out var held))
                {
                    waiting.Remove(next.Key);
                    if (outcome == "pass" && next.ContractsEqual)
                        foreach (var h in held) run.Carried.Add(new RcImpactCarry { Key = h.Key, Origin = "trust-gate-passed:" + next.Key });
                    else
                        foreach (var h in held)
                        {
                            run.Expanded.Add(h.Key);
                            ready.Add(h);
                        }
                }
            }
            // A gate that never launched leaves its holds owed: expand them rather than lose them.
            foreach (var pending in waiting.Values.SelectMany(v => v)) throw new InvalidOperationException("held-gate-not-launched:" + pending.Key);
            return run;
        }

        /// <summary>The lowest tier the obligation qualifies for.</summary>
        public static int Tier(RcImpactObligation o, ISet<string> openFailures)
        {
            if (openFailures.Contains(o.Key)) return 0;
            switch (o.Impact)
            {
                case "product-direct": return 1;
                case "direct": return 2;
                case "transitive": return 3;
                default: return o.Disposition == "Held" ? 3 : 4;
            }
        }

        static (double? Cost, string Source) Cost(RcImpactObligation o)
        {
            if (o.PriorWallSeconds.HasValue && o.PriorWallSeconds.Value >= 0) return (o.PriorWallSeconds, "prior-wall");
            if (o.MemberSeconds.HasValue && o.MemberSeconds.Value >= 0) return (o.MemberSeconds, "member-sum");
            return (null, "unknown");
        }
    }
}
