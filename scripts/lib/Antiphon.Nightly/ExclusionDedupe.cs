// D-5 exclusions dedupe (nightly-tests-impl.ps1, Invoke-NightlyTests summary): the per-row reference check, compiled.
namespace Antiphon.Nightly
{
    public static class ExclusionDedupe
    {
        /// <summary>
        /// True when both arrays have the same length and hold the very same object (or both null) at every
        /// index. Compares the stored elements as they are, without unwrapping PSObject: a raw match implies the
        /// PowerShell [object]::ReferenceEquals loop matched too, and a raw miss only sends the caller to its
        /// ConvertTo-Json comparison, which decides the same way for the same objects.
        /// </summary>
        public static bool SameElementReferences(object[] list, object[] first)
        {
            if (ReferenceEquals(list, first)) return true;
            if (list == null || first == null) return false;
            if (list.Length != first.Length) return false;
            for (var i = 0; i < list.Length; i++)
            {
                if (!ReferenceEquals(list[i], first[i])) return false;
            }
            return true;
        }
    }
}
