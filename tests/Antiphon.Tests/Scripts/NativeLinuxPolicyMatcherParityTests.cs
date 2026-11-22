using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.TestSupport;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1613: the native Linux policy witness (NativeLinuxPolicyWitness.Excluded) and the nightly exclusion predicate
/// (NightlyPolicy.NodeExcluded) agree on the same exclusion rows. In-process since W2-nightly S27: the predicate is
/// compiled, and scripts/lib/nightly-coverage.ps1, which the pwsh child of this test dot-sourced, is deleted. The rows
/// and nodes are the records the child built ([pscustomobject] Class and Methods; ClassName, a blank Type and Method).
/// </summary>
[Category("Unit")]
public sealed class NativeLinuxPolicyMatcherParityTests
{
    private const string Exclusions = """
        [
          {"class":"PhoneHomeMirrorJoinTests","methods":[]},
          {"class":"Outer+Inner","methods":["Nested"]},
          {"class":"GenericProbe`1","methods":["GenericCase"]},
          {"class":"CaseTwin","methods":["Case"]},
          {"class":"OverloadProbe","methods":["Case"]},
          {"class":"Ma\u00dfProbe","methods":[]},
          {"class":"CtorSuffix","methods":["Runs"]}
        ]
        """;

    // The same rows as the nightly predicate reads them: Class, and Methods (empty: every method of the class).
    private static readonly object[] Rows =
    [
        new PlainObject(("Class", "PhoneHomeMirrorJoinTests"), ("Methods", Array.Empty<object>())),
        new PlainObject(("Class", "Outer+Inner"), ("Methods", new object[] { "Nested" })),
        new PlainObject(("Class", "GenericProbe`1"), ("Methods", new object[] { "GenericCase" })),
        new PlainObject(("Class", "CaseTwin"), ("Methods", new object[] { "Case" })),
        new PlainObject(("Class", "OverloadProbe"), ("Methods", new object[] { "Case" })),
        new PlainObject(("Class", "Ma\u00dfProbe"), ("Methods", Array.Empty<object>())),
        new PlainObject(("Class", "CtorSuffix"), ("Methods", new object[] { "Runs" })),
    ];

    [Test]
    public void Matcher_agrees_with_nightly_exclusion_predicate()
    {
        var nodes = new (string ClassName, string Method, bool Excluded)[]
        {
            ("Antiphon.Tests.Application.PhoneHomeMirrorJoinTests", "Any", true),
            ("Other.Namespace.PhoneHomeMirrorJoinTests", "Sibling", true),
            ("Other.Namespace.PortableSibling", "Runs", false),
            ("Ns.Outer+Inner", "Nested", true),
            ("Ns.Outer+Inner", "Portable", false),
            ("Ns.GenericProbe`1", "GenericCase", true),
            ("Ns.GenericProbe`1[[System.Int32]]", "GenericCase", false),
            ("casetwin", "Case", true),
            ("CaseTwin", "Other", false),
            ("OverloadProbe", "Case", true),
            ("OverloadProbe", "case", true),
            ("OverloadProbe", "Sibling", false),
            ("Ns.Ma\u00dfProbe", "Runs", true),
            ("CtorSuffix(System.Int32)", "Runs", true),
            ("CtorSuffix(System.Int32)", "Other", false),
            ("Ns.CtorSuffix", "Runs", true),
        };

        using var document = JsonDocument.Parse(Exclusions);
        var exclusions = document.RootElement;
        foreach (var (className, method, excluded) in nodes)
        {
            var label = className + " " + method;
            var expected = NativeLinuxPolicyWitness.Excluded(className, "", method, exclusions);
            expected.ShouldBe(excluded, label);
            var node = new PlainObject(("ClassName", className), ("Type", ""), ("Method", method));
            NightlyPolicy.NodeExcluded(node, Rows).ShouldBe(expected, label);
        }
    }
}
