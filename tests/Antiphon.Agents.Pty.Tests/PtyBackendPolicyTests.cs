using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

[Category("Unit")]
public class PtyBackendPolicyTests
{
    private static PtyBackendDecision Windows(string? request, string? environment = null) =>
        PtyBackendPolicy.Resolve(request, environment, true, () => ("approved/conpty.dll", "shipped pair"));

    [Test]
    public void Windows_defaults_request_modern()
    {
        foreach (var raw in new string?[] { null, "", " ", "\t" })
        {
            var d = Windows(raw);
            d.Backend.ShouldBe(PtyBackend.ModernConPty, "default-modern");
            d.Requested.ShouldBe(raw ?? "");
            d.FellBack.ShouldBeFalse();
            d.Deprecated.ShouldBeFalse();
        }
    }

    [Test]
    public void Windows_modern_aliases_resolve_modern()
    {
        foreach (var raw in new[] { "modern", "conpty", "1", "on", "true", "yes", " MoDeRn " })
        {
            var d = Windows(raw);
            d.Backend.ShouldBe(PtyBackend.ModernConPty, "modern-alias");
            d.Requested.ShouldBe(raw);
            d.ConPtyDllPath.ShouldBe("approved/conpty.dll");
            d.Deprecated.ShouldBeFalse();
        }
    }

    [Test]
    public void Windows_unknown_selector_warns_and_chooses_modern()
    {
        var d = Windows(" typo ");
        d.Backend.ShouldBe(PtyBackend.ModernConPty, "unknown-modern");
        d.Requested.ShouldBe(" typo ");
        d.Reason.ShouldContain("unrecognised");
        d.RequiresWarning.ShouldBeTrue("unknown selector must warn");
        d.FellBack.ShouldBeFalse();
    }

    [Test]
    public void Windows_legacy_aliases_are_deprecated_without_fallback()
    {
        foreach (var raw in new[] { "inbox", "0", "off", "false", "no", " InBoX " })
        {
            var d = PtyBackendPolicy.Resolve(raw, null, true,
                () => throw new InvalidOperationException("legacy selector must not probe"));
            d.Backend.ShouldBe(PtyBackend.InboxConhost);
            d.Deprecated.ShouldBeTrue("legacy-deprecated");
            d.FellBack.ShouldBeFalse();
            d.ConPtyDllPath.ShouldBeNull();
        }
    }

    [Test]
    public void Windows_missing_pairs_fall_back_and_preserve_request()
    {
        var root = Path.Combine(Path.GetTempPath(), "c1022-pair-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
        foreach (var raw in new string?[] { null, "modern", "typo" })
        foreach (var mask in new[] { 0, 1, 2, 3 })
        {
            var dir = Path.Combine(root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            if ((mask & 1) != 0) File.WriteAllText(Path.Combine(dir, "conpty.dll"), "discovery fixture, not native code");
            if ((mask & 2) != 0) File.WriteAllText(Path.Combine(dir, "OpenConsole.exe"), "discovery fixture, not native code");
            var present = mask == 3;
            var calls = 0;
            var d = PtyBackendPolicy.Resolve(raw, null, true, () =>
            {
                calls++;
                ConPtyRedistributable.TryLocate(true, [dir], out var dll, out var why);
                return (dll, why);
            });
            calls.ShouldBe(1, "pair-fallback");
            d.Backend.ShouldBe(present ? PtyBackend.ModernConPty : PtyBackend.InboxConhost, "pair-fallback");
            d.FellBack.ShouldBe(!present, "pair-fallback");
            d.Deprecated.ShouldBe(!present);
            d.Requested.ShouldBe(raw ?? "");
        }
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public void Unix_ignores_windows_selectors_without_probing()
    {
        foreach (var raw in new string?[] { null, "", " ", "modern", "conpty", "1", "on", "true", "yes", "inbox", "0", "off", "false", "no", "typo" })
        {
            var calls = 0;
            var d = PtyBackendPolicy.Resolve(raw, null, false, () => { calls++; return ("pair", "unused"); });
            d.Backend.ShouldBe(PtyBackend.UnixPty, "unix-no-probe");
            calls.ShouldBe(0, "unix-locator-calls=0");
            d.FellBack.ShouldBeFalse();
            d.Deprecated.ShouldBeFalse();
            d.ConPtyDllPath.ShouldBeNull();
            d.Requested.ShouldBe(raw ?? "");
        }
    }

    [Test]
    public void Enum_wire_values_remain_stable()
    {
        ((int)PtyBackend.InboxConhost).ShouldBe(0, "enum-stable");
        ((int)PtyBackend.ModernConPty).ShouldBe(1, "enum-stable");
        ((int)PtyBackend.UnixPty).ShouldBe(2, "enum-stable");
    }

    [Test]
    public void Instance_request_outranks_environment()
    {
        Windows("modern", "inbox").Backend.ShouldBe(PtyBackend.ModernConPty, "instance-wins");
        Windows("inbox", "modern").Backend.ShouldBe(PtyBackend.InboxConhost, "instance-wins");
        Windows(null, "inbox").Backend.ShouldBe(PtyBackend.InboxConhost, "instance-wins");
        Windows("", "inbox").Backend.ShouldBe(PtyBackend.ModernConPty, "instance-wins");
    }
}
