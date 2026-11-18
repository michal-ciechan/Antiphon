using System.Collections;
using System.Collections.Generic;
using System.Management.Automation;

namespace Antiphon.Nightly
{
    /// <summary>Whole-collection loops of the test-phase summary tail (test-phase-evidence.ps1).</summary>
    public static class TestPhaseSummaryFast
    {
        /// <summary>
        /// ConvertTo-TestPhaseSummaryRows' completeness loop: true when any test's <c>[bool]$test.incomplete</c>
        /// (LanguagePrimitives.IsTrue of the row's 'incomplete' value) holds. A null collection has no tests.
        /// </summary>
        public static bool AnyIncomplete(IEnumerable tests)
        {
            if (tests == null) return false;
            foreach (var test in tests)
            {
                if (LanguagePrimitives.IsTrue(PsValue.Prop(test, "incomplete"))) return true;
            }
            return false;
        }

        /// <summary>
        /// Write-TestPhaseSummaryFiles' tests.csv body: one TestPhaseSummary.Row line per test, in order.
        /// A null collection has no tests.
        /// </summary>
        public static string[] Rows(IEnumerable tests, string[] fields)
        {
            var lines = new List<string>();
            if (tests == null) return lines.ToArray();
            foreach (var test in tests) lines.Add(TestPhaseSummary.Row(test, fields));
            return lines.ToArray();
        }
    }
}
