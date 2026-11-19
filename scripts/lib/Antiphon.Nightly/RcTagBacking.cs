// CARD-1820 S5. Test-RcTagBacking over an admission envelope chain.
// CARD-1836 admission is still PowerShell (scripts/lib/rc-admission.ps1), so this
// policy lives in the Nightly core. No tag, publish, or promote caller. gateCredit
// stays false: ledger-basis success is not publication credit.
#nullable disable
using System;

namespace Antiphon.Nightly
{
    public sealed class RcTagBackingResult
    {
        public RcTagBackingResult(string kind, string anchorSha, TimeSpan? age, string reason, bool gateCredit)
        {
            Kind = kind;
            AnchorSha = anchorSha;
            Age = age;
            Reason = reason;
            GateCredit = gateCredit;
            Answer = kind == "full" ? "full"
                : kind == "chain-to-full" ? "chain-to-full(" + anchorSha + "," + FormatAge(age.Value) + ")"
                : "not-backed(" + reason + ")";
        }

        public string Kind { get; }
        public string Answer { get; }
        public string AnchorSha { get; }
        public TimeSpan? Age { get; }
        public string Reason { get; }
        public bool GateCredit { get; }

        public static RcTagBackingResult NotBacked(string reason) =>
            new RcTagBackingResult("not-backed", null, null, reason, false);

        public static string FormatAge(TimeSpan age)
        {
            var negative = age < TimeSpan.Zero;
            var ticks = age.Duration().Ticks;
            var days = ticks / TimeSpan.TicksPerDay;
            ticks %= TimeSpan.TicksPerDay;
            var hours = ticks / TimeSpan.TicksPerHour;
            ticks %= TimeSpan.TicksPerHour;
            var minutes = ticks / TimeSpan.TicksPerMinute;
            ticks %= TimeSpan.TicksPerMinute;
            var seconds = ticks / TimeSpan.TicksPerSecond;
            var fraction = ticks % TimeSpan.TicksPerSecond;
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0}{1}.{2:00}:{3:00}:{4:00}.{5:0000000}",
                negative ? "-" : "", days, hours, minutes, seconds, fraction);
        }
    }

    public static class RcTagBacking
    {
        public const int TrustedShadowCutsRequired = 2;
        public static readonly TimeSpan MaxAnchorAge = TimeSpan.FromDays(7);

        // Deny adapter. The witnesses assert a specific answer and fail against this.
        public static RcTagBackingResult Test(string chainJson) =>
            RcTagBackingResult.NotBacked("unimplemented");
    }
}
