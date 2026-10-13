using System.Security.Cryptography;
using System.Text;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1213 S2 (V-8/R-8): the fragment catalog and <c>{{NAME}}</c> expansion inside
/// <see cref="InstructionBundleComposer"/>. Synthetic bundles and fragments go through the internal
/// Compose overload that the public one also calls; expected text and hashes are written by hand
/// or computed here from the fixture bytes, never read back from the production result.
/// </summary>
[Category("Unit")]
public class InstructionFragmentTests
{
    private const string Fixture = "GIT-COMMON contract v1.\nINVARIANTS:\n- one synthetic rule.";

    [Test]
    public void C1213_CatalogAndHash()
    {
        var resources = typeof(InstructionBundles).Assembly.GetManifestResourceNames()
            .Where(r => r.StartsWith("Antiphon.Server.Bundles.Fragments.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).ToArray();
        resources.ShouldBe(["Antiphon.Server.Bundles.Fragments.git-common.md"], "fragment-resources");
        InstructionFragments.All.Keys.Order(StringComparer.Ordinal).ShouldBe(["git-common"], "fragment-keys");
        InstructionBundles.All.Keys.Order(StringComparer.Ordinal).ShouldBe([
            "board-api", "check-interpreter", "delegate-basics", "diagnose", "orchestrator", "output-distiller",
            "stage-code", "stage-investigate", "stage-mutation", "stage-plan", "stage-review", "stage-test-design",
            "standing-instructions",
            "style-brief", "style-caveman", "style-explanatory", "style-normal", "style-phone", "style-terse",
        ], "bundle-keys");

        var source = File.ReadAllText(Path.Combine(
            DelegateScriptRunner.RepoRoot, "server", "Bundles", "Fragments", "git-common.md"));
        var normalised = source.Replace("\r\n", "\n").Trim();
        var shipped = InstructionFragments.Get(InstructionFragments.GitCommon);
        shipped.Text.ShouldBe(normalised, "content-hash-text");
        shipped.Version.ShouldBe(Sha8(normalised), "content-hash");
        shipped.Header.ShouldBe($"[fragment:git-common v{Sha8(normalised)}]", "content-hash-header");
        shipped.Render().ShouldBe($"[fragment:git-common v{Sha8(normalised)}]\n{normalised}", "content-hash-render");

        var lf = InstructionFragments.Create("git-common", Fixture + "\n");
        var crlf = InstructionFragments.Create("git-common", Fixture.Replace("\n", "\r\n") + "\r\n");
        lf.Version.ShouldBe(Sha8(Fixture), "content-hash-lf");
        crlf.Version.ShouldBe(Sha8(Fixture), "content-hash-crlf");
        crlf.Text.ShouldBe(Fixture, "content-hash-crlf-text");
        InstructionFragments.Create("git-common", Fixture.Replace("synthetic", "Synthetic")).Version
            .ShouldNotBe(Sha8(Fixture), "content-hash-one-char");
    }

    [Test]
    public void C1213_ExpansionOnce()
    {
        var fragment = InstructionFragments.Create("git-common", Fixture);
        var version = Sha8(Fixture);
        var bundles = Bundles(
            ("a", "A1 {{GIT-COMMON}} A2 {{GIT-COMMON}}"),
            ("b", "B {{GIT-COMMON}}"));

        var composed = InstructionBundleComposer.Compose(["a", "b"], null, null, k => bundles[k], [fragment]);

        composed.Text.ShouldBe(
            "[bundle:a v0000000a]\nA1 [fragment:git-common v" + version + "]\n" + Fixture
            + " A2 [fragment:git-common v" + version + ": see above]"
            + "\n\n[bundle:b v0000000b]\nB [fragment:git-common v" + version + ": see above]",
            "expansion-count");
        composed.Fragments.Select(f => f.Key).ShouldBe(["git-common"], "expansion-count-fragments");

        var fresh = InstructionBundleComposer.Compose(["b"], null, null, k => bundles[k], [fragment]);
        fresh.Text.ShouldBe(
            "[bundle:b v0000000b]\nB [fragment:git-common v" + version + "]\n" + Fixture,
            "expansion-count-fresh");
    }

    [Test]
    public void C1213_UnknownPlaceholderFailsLoudly()
    {
        var fragment = InstructionFragments.Create("git-common", Fixture);
        foreach (var token in new[] { "{{NOPE}}", "{{GIT-COMMON-X}}" })
        {
            var bundles = Bundles(("synthetic-x", "before " + token + " after"));
            var ex = Should.Throw<InvalidOperationException>(
                () => InstructionBundleComposer.Compose(["synthetic-x"], null, null, k => bundles[k], [fragment]),
                "unknown-token " + token);
            ex.Message.ShouldContain("'synthetic-x'", Case.Sensitive, "unknown-token-bundle " + token);
            ex.Message.ShouldContain("'" + token + "'", Case.Sensitive, "unknown-token-token " + token);
            ex.Message.ShouldContain("unknown placeholder", Case.Sensitive, "unknown-token-kind " + token);
            ex.Message.ShouldContain("Known: {{GIT-COMMON}}", Case.Sensitive, "unknown-token-known " + token);
        }
    }

    /// <summary>
    /// Brief-directed fail-closed grammar: a <c>{{</c> that does not open a well-formed known
    /// placeholder throws (malformed), a well-formed unknown name throws (unknown); the operator
    /// append and the brief are not scanned and stay byte-exact.
    /// </summary>
    [Test]
    public void C1213_ExpansionScope()
    {
        var fragment = InstructionFragments.Create("git-common", Fixture);
        var version = Sha8(Fixture);
        var bundles = Bundles(("a", "A {{GIT-COMMON}}"), ("plain", "P"), ("style-x", "S"));
        const string append = "keep {{GIT-COMMON}} and {{NOPE}} and {{x literal";

        var withStyle = InstructionBundleComposer.Compose(["a"], "style-x", append, k => bundles[k], [fragment]);
        withStyle.Text.ShouldBe(
            "[bundle:a v0000000a]\nA [fragment:git-common v" + version + "]\n" + Fixture
            + "\n\n[bundle:style-x v0000000c]\nS\n\n" + append,
            "literal-append");
        var plain = InstructionBundleComposer.Compose(["plain"], null, append, k => bundles[k], [fragment]);
        plain.Text.ShouldBe("[bundle:plain v0000000b]\nP\n\n" + append, "literal-append-plain");
        plain.Fragments.ShouldBeEmpty("literal-append-no-fragment");

        var task = new AgentTask
        {
            Id = Guid.NewGuid(),
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Code,
            Status = AgentTaskStatus.Queued,
            Goal = "goal keeps {{GIT-COMMON}} and {{NOPE}} literal",
            WorkingDirectory = Path.GetTempPath(),
            Workspace = WorkspaceMode.Shared,
            CreatedAt = DateTime.UtcNow,
        };
        var brief = DelegationReportFormatter.BuildBrief(task, new DelegationSettings());
        brief.ShouldContain("goal keeps {{GIT-COMMON}} and {{NOPE}} literal", Case.Sensitive, "literal-brief");
        brief.ShouldNotContain("[fragment:", Case.Sensitive, "literal-brief-no-fragment");

        var name32 = "A" + new string('B', 31);
        var rows = new (string Label, string Text, string Kind)[]
        {
            ("one-char-name", "{{X}}", "malformed placeholder"),
            ("lowercase", "{{git-common}}", "malformed placeholder"),
            ("underscore", "{{GIT_COMMON}}", "malformed placeholder"),
            ("name-33", "{{" + name32 + "C}}", "malformed placeholder"),
            ("unterminated", "{{GIT-COMMON", "malformed placeholder"),
            ("nested", "{{{{GIT-COMMON}}}}", "malformed placeholder"),
            ("inner-space", "{{ GIT-COMMON }}", "malformed placeholder"),
            ("name-32-unknown", "{{" + name32 + "}}", "unknown placeholder"),
        };
        foreach (var (label, text, kind) in rows)
        {
            var row = Bundles(("grammar", "x " + text + " y"));
            var ex = Should.Throw<InvalidOperationException>(
                () => InstructionBundleComposer.Compose(["grammar"], null, null, k => row[k], [fragment]),
                "grammar-" + label);
            ex.Message.ShouldContain(kind, Case.Sensitive, "grammar-kind-" + label);
            ex.Message.ShouldContain("'grammar'", Case.Sensitive, "grammar-bundle-" + label);
        }

        var single = Bundles(("single", "x {GIT-COMMON} }} y"));
        InstructionBundleComposer.Compose(["single"], null, null, k => single[k], [fragment]).Text
            .ShouldBe("[bundle:single v0000000a]\nx {GIT-COMMON} }} y", "grammar-single-brace-literal");
    }

    [Test]
    public void C1213_FragmentLimits()
    {
        var shipped = InstructionFragments.Get(InstructionFragments.GitCommon);
        shipped.Text.Split('\n')[0].ShouldBe("GIT-COMMON contract v1.", "fragment-contract");
        shipped.Text.Split('\n').Count(line => line.StartsWith("INVARIANTS", StringComparison.Ordinal))
            .ShouldBe(1, "fragment-invariants");
        shipped.Text.Length.ShouldBeLessThanOrEqualTo(1_500, "fragment-size");
        foreach (var forbidden in new[] { "{{", "{agentName}", "{channels}" })
            shipped.Text.ShouldNotContain(forbidden, Case.Sensitive, "fragment-forbidden " + forbidden);

        const string head = "GIT-COMMON contract v1.\nINVARIANTS:\n";
        var exactly1500 = head + new string('x', 1_500 - head.Length);
        InstructionFragments.Create("git-common", exactly1500).Text.Length.ShouldBe(1_500, "fragment-size-1500");

        var refused = new (string Label, string Text, string Message)[]
        {
            ("size-1501", exactly1500 + "x", "limit is 1,500"),
            ("open-braces", Fixture + " {{", "contains '{{'"),
            ("agent-name", Fixture + " {agentName}", "contains '{agentName}'"),
            ("channels", Fixture + " {channels}", "contains '{channels}'"),
            ("no-contract-line", "INVARIANTS:\n- rule.", "must open with 'GIT-COMMON contract v<n>.'"),
            ("other-name-contract", "OTHER contract v1.\nINVARIANTS:\n- rule.", "must open with 'GIT-COMMON contract v<n>.'"),
            ("no-invariants", "GIT-COMMON contract v1.\n- rule.", "no line starting with 'INVARIANTS'"),
        };
        foreach (var (label, text, message) in refused)
        {
            var ex = Should.Throw<InvalidOperationException>(
                () => InstructionFragments.Create("git-common", text), "fragment-forbidden-" + label);
            ex.Message.ShouldContain(message, Case.Sensitive, "fragment-forbidden-message-" + label);
        }
    }

    [Test]
    public void C1213_FragmentStampsChangeWithText()
    {
        var before = InstructionFragments.Create("git-common", Fixture);
        var changed = Fixture.Replace("one synthetic rule", "one synthetic rule!");
        var after = InstructionFragments.Create("git-common", changed);
        var bundles = Bundles(("a", "{{GIT-COMMON}}"), ("b", "{{GIT-COMMON}}"));

        var first = InstructionBundleComposer.Compose(["a", "b"], null, null, k => bundles[k], [before]);
        var second = InstructionBundleComposer.Compose(["a", "b"], null, null, k => bundles[k], [after]);

        first.Stamps.ShouldBe(["a v0000000a", "b v0000000b", "git-common v" + Sha8(Fixture)], "fragment-stamp");
        second.Stamps.ShouldBe(["a v0000000a", "b v0000000b", "git-common v" + Sha8(changed)], "fragment-stamp-changed");
        first.StampLine.ShouldNotBe(second.StampLine, "fragment-stamp-line");
        InstructionBundleComposer.IsOutOfDate(first.StampLine, second).ShouldBeTrue("fragment-stamp-drift");
        InstructionBundleComposer.IsOutOfDate(first.StampLine, first).ShouldBeFalse("fragment-stamp-same");

        var shipped = InstructionBundleComposer.Compose(
            InstructionBundles.ForDelegate(AgentTaskKind.Worker, AgentTaskRole.Review));
        shipped.Stamps.Count(s => s.StartsWith("git-common v", StringComparison.Ordinal))
            .ShouldBe(1, "fragment-stamp-shipped-once");
        shipped.Stamps.ShouldContain(
            "git-common v" + InstructionFragments.Get(InstructionFragments.GitCommon).Version, "fragment-stamp-shipped");
    }

    [Test]
    public void C1213_ExpandedBudgetIsEnforced()
    {
        const string head = "GIT-COMMON contract v1.\nINVARIANTS:\n";
        var big = InstructionFragments.Create("git-common", head + new string('x', 1_500 - head.Length));
        var bundles = Bundles(("a", "{{GIT-COMMON}}"));
        var composed = InstructionBundleComposer.Compose(["a"], null, null, k => bundles[k], [big]);

        var expectedText = "[bundle:a v0000000a]\n[fragment:git-common v" + big.Version + "]\n" + big.Text;
        composed.Text.ShouldBe(expectedText, "expanded-budget-text");
        // 22 chars of --append-system-prompt plus 2 x 3 quoting overhead, no other args.
        var exact = expectedText.Length + 22 + 6;
        Should.NotThrow(() => InstructionBundleComposer.EnsureWithinCommandLineBudget(composed, [], exact, "fits"),
            "expanded-budget-exact");
        var ex = Should.Throw<InvalidOperationException>(
            () => InstructionBundleComposer.EnsureWithinCommandLineBudget(composed, [], exact - 1, "synthetic"),
            "expanded-budget");
        ex.Message.ShouldContain("fragment git-common v" + big.Version + " 1,500 chars", Case.Sensitive, "expanded-budget-names-fragment");
        ex.Message.ShouldContain("Nothing was truncated", Case.Sensitive, "expanded-budget-no-truncation");
        composed.Text.ShouldEndWith(big.Text, Case.Sensitive, "expanded-budget-text-intact");

        var budget = new DelegationSettings().CommandLineBudgetChars;
        var fragment = InstructionFragments.Get(InstructionFragments.GitCommon);
        foreach (var role in Enum.GetValues<AgentTaskRole>().Where(r => !AgentTaskRoles.IsSpecialist(r)))
        foreach (var kind in Enum.GetValues<AgentTaskKind>())
        {
            var shipped = InstructionBundleComposer.Compose(
                InstructionBundles.ForDelegate(kind, role, [InstructionBundles.BoardApi, "style-explanatory"]),
                systemPromptAppend: ChannelPreamble.TelegramPresetTemplate.ReplaceLineEndings("\r\n"));
            Count(shipped.Text, fragment.Render()).ShouldBe(1, $"expanded-budget-shipped-once {kind}/{role}");
            Should.NotThrow(() => InstructionBundleComposer.EnsureWithinCommandLineBudget(shipped,
                ["--name", "task-1a2b3c4d", "--model", "opus", "--session-id", Guid.NewGuid().ToString("D")],
                budget - 500, $"{kind}/{role}"), $"expanded-budget-shipped {kind}/{role}");
        }
    }

    private static Dictionary<string, InstructionBundle> Bundles(params (string Key, string Text)[] rows)
    {
        var result = new Dictionary<string, InstructionBundle>(StringComparer.Ordinal);
        for (var i = 0; i < rows.Length; i++)
            result[rows[i].Key] = new InstructionBundle(rows[i].Key, "0000000" + (char)('a' + i), rows[i].Text);
        return result;
    }

    internal static string Sha8(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant()[..8];

    internal static int Count(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}

/// <summary>
/// CARD-1213: what a shipped bundle looks like inside a composition. The placeholder becomes the
/// fragment's header and text at its first use and the see-above line afterwards.
/// </summary>
internal static class C1213Expected
{
    public const string Placeholder = "{{GIT-COMMON}}";

    public static string FirstUse(string bundleText) =>
        bundleText.Contains(Placeholder, StringComparison.Ordinal)
            ? bundleText.Replace(Placeholder, InstructionFragments.Get(InstructionFragments.GitCommon).Render(), StringComparison.Ordinal)
            : bundleText;

    public static string LaterUse(string bundleText) =>
        bundleText.Contains(Placeholder, StringComparison.Ordinal)
            ? bundleText.Replace(Placeholder, InstructionFragments.Get(InstructionFragments.GitCommon).SeeAbove, StringComparison.Ordinal)
            : bundleText;
}
