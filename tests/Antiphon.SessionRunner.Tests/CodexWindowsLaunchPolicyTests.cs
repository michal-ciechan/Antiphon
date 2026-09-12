using System.Text;
using Antiphon.Agents.Pty;
using Antiphon.FakeLlmApi;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Unit")]
public sealed class CodexWindowsLaunchPolicyTests
{
    [Test]
    public void Standard_npm_shim_becomes_node_exe_plus_codex_js()
    {
        using var layout = new CodexNpmLayout();
        var args = new[] { "--no-alt-screen", "-c", "developer_instructions=" + CodexInstructionFixtures.Incident };
        var request = CodexRequest(layout.ShimPath, args, layout.Root);

        var effective = CodexWindowsLaunchPolicy.Apply(request, useHerdr: false);

        effective.Exe.ShouldBe(layout.SiblingNodePath);
        effective.Args[0].ShouldBe(layout.JsPath);
        effective.Args.Skip(1).ToArray().ShouldBe(args);
        effective.Args[2].ShouldBe("developer_instructions=" + CodexInstructionFixtures.Incident);
    }

    [Test]
    public void Sibling_node_wins_over_PATH_node()
    {
        using var layout = new CodexNpmLayout();
        var pathDir = layout.PathNodeDir();
        var request = CodexRequest(
            layout.ShimPath, ["--no-alt-screen"], layout.Root,
            env: new Dictionary<string, string> { ["PATH"] = pathDir });

        var effective = CodexWindowsLaunchPolicy.Apply(request, useHerdr: false);
        effective.Exe.ShouldBe(layout.SiblingNodePath);
        effective.Exe.ShouldNotBe(Path.Combine(pathDir, "node.exe"));
    }

    [Test]
    public void PATH_node_is_used_when_no_sibling()
    {
        using var layout = new CodexNpmLayout(siblingNode: false);
        var pathDir = layout.PathNodeDir();
        var request = CodexRequest(
            layout.ShimPath, ["--no-alt-screen"], layout.Root,
            env: new Dictionary<string, string> { ["PATH"] = pathDir });

        var effective = CodexWindowsLaunchPolicy.Apply(request, useHerdr: false);
        effective.Exe.ShouldBe(Path.GetFullPath(Path.Combine(pathDir, "node.exe")));
    }

    [Test]
    public void Install_root_with_spaces_and_unicode_round_trips()
    {
        using var layout = new CodexNpmLayout(rootName: "c0497 npm rôot 😀 " + Guid.NewGuid().ToString("N")[..8]);
        var request = CodexRequest(layout.ShimPath, ["--no-alt-screen"], layout.Root);

        var effective = CodexWindowsLaunchPolicy.Apply(request, useHerdr: false);
        var line = WindowsCommandLine.Serialize(effective.Exe, effective.Args);
        var argv = LaunchArgvGuard.ParseArgv(line);
        argv[0].ShouldBe(effective.Exe);
        argv[1].ShouldBe(effective.Args[0]);
        WindowsCommandLine.Measure(effective.Exe, effective.Args).ShouldBe(line.Length);
        "😀".Length.ShouldBe(2);
    }

    [Test]
    public void An_already_normalized_request_is_not_prefixed_twice()
    {
        using var layout = new CodexNpmLayout();
        var args = new[] { layout.JsPath, "--no-alt-screen" };
        var request = CodexRequest(layout.SiblingNodePath!, args, layout.Root);

        var effective = CodexWindowsLaunchPolicy.Apply(request, useHerdr: false);
        effective.ShouldBe(request);
        effective.Args.Count.ShouldBe(args.Length);
        effective.Args[0].ShouldBe(layout.JsPath);
    }

    [Test]
    public void Explicit_codex_exe_is_left_alone()
    {
        using var layout = new CodexNpmLayout();
        var request = CodexRequest(layout.NativePath, ["--no-alt-screen"], layout.Root);

        var effective = CodexWindowsLaunchPolicy.Apply(request, useHerdr: false);
        effective.Exe.ShouldBe(request.Exe);
        effective.Args.ShouldBe(request.Args);
    }

    [Test]
    public void Explicit_node_codex_js_is_left_alone_but_still_budgeted()
    {
        using var layout = new CodexNpmLayout();
        var args = new[] { layout.JsPath, "--no-alt-screen" };
        var request = CodexRequest(layout.SiblingNodePath!, args, layout.Root, budget: 100);

        var ex = Should.Throw<CodexLaunchException>(() =>
            CodexWindowsLaunchPolicy.Apply(request, useHerdr: false));
        ex.Code.ShouldBe(CodexLaunchProblemTypes.CommandLineTooLong);
    }

    [Test]
    public void Serialized_hop1_at_budget_is_accepted_and_plus_one_is_refused()
    {
        using var layout = new CodexNpmLayout();
        const int budget = 30_000;
        var sentinel = "C0497-HOP1-" + Guid.NewGuid().ToString("N");
        var prefix = new[] { "--no-alt-screen", "-c" };
        var atMinusOne = FitHop1(layout, prefix, sentinel, budget - 1);
        var atBudget = FitHop1(layout, prefix, sentinel, budget);
        var plusOne = atBudget + "X";

        Should.NotThrow(() => ApplySized(layout, prefix, atMinusOne, budget));
        Should.NotThrow(() => ApplySized(layout, prefix, atBudget, budget));

        var ex = Should.Throw<CodexLaunchException>(() => ApplySized(layout, prefix, plusOne, budget));
        ex.Code.ShouldBe(CodexLaunchProblemTypes.CommandLineTooLong);
        MeasureHop1(layout, prefix, plusOne).ShouldBe(budget + 1);
        ex.Message.ShouldContain("node.exe codex.js");
        ex.Message.ShouldContain("30,001");
        ex.Message.ShouldContain("30,000");
        ex.Message.ShouldNotContain(sentinel);
        ex.Message.ShouldNotContain(plusOne);
    }

    [Test]
    public void Native_hop_is_bounded_by_the_longest_installed_native_path()
    {
        using var layout = new CodexNpmLayout(nativeTriple: "x86_64-pc-windows-msvc-" + new string('N', 120));
        const int hop1Target = 29_980;
        var sentinel = "C0497-HOP2-" + Guid.NewGuid().ToString("N");
        var prefix = new[] { "--no-alt-screen", "-c" };
        var payload = FitHop1(layout, prefix, sentinel, hop1Target);
        var hop1 = MeasureHop1(layout, prefix, payload);
        hop1.ShouldBe(hop1Target);
        var hop2 = MeasureHop2(layout, prefix, payload);
        hop2.ShouldBeGreaterThan(CodexLaunchProblemTypes.TransportCeilingChars);

        var ex = Should.Throw<CodexLaunchException>(() => ApplySized(layout, prefix, payload, 30_000));
        ex.Code.ShouldBe(CodexLaunchProblemTypes.CommandLineTooLong);
        ex.Message.ShouldContain("native hop");
        ex.Message.ShouldContain($"{hop1:N0}");
        ex.Message.ShouldContain($"{hop2:N0}");
        ex.Message.ShouldNotContain(sentinel);
    }

    [Test]
    public void Quote_expansion_is_counted_not_estimated()
    {
        using var layout = new CodexNpmLayout();
        var quotes = new string('"', 9_047);
        var args = new[] { "--no-alt-screen", quotes };
        var request = CodexRequest(layout.ShimPath, args, layout.Root, budget: 15_000);

        var nodeArgs = PrependJs(layout, args);
        var payloadEstimate = quotes.Length + 3;
        payloadEstimate.ShouldBe(9_050);
        var oldEstimate = layout.SiblingNodePath!.Length + nodeArgs.Sum(a => a.Length + 3);
        var crt = WindowsCommandLine.Measure(layout.SiblingNodePath!, nodeArgs);
        oldEstimate.ShouldBeLessThan(15_000);
        crt.ShouldBeGreaterThan(18_000);
        crt.ShouldBeGreaterThan(oldEstimate);

        var ex = Should.Throw<CodexLaunchException>(() =>
            CodexWindowsLaunchPolicy.Apply(request, useHerdr: false));
        ex.Code.ShouldBe(CodexLaunchProblemTypes.CommandLineTooLong);
        Console.WriteLine($"oldEstimate={oldEstimate} crt={crt}");
    }

    [Test]
    public void Missing_sibling_and_PATH_node_is_codex_launcher_unavailable()
    {
        using var layout = new CodexNpmLayout(siblingNode: false);
        var dummy = Path.Combine(layout.Root, "empty-path");
        Directory.CreateDirectory(dummy);
        var request = CodexRequest(
            layout.ShimPath, ["--no-alt-screen"], layout.Root,
            env: new Dictionary<string, string> { ["PATH"] = dummy });

        var ex = Should.Throw<CodexLaunchException>(() =>
            CodexWindowsLaunchPolicy.Apply(request, useHerdr: false));
        ex.Code.ShouldBe(CodexLaunchProblemTypes.LauncherUnavailable);
        ex.Message.ShouldContain("Node");
    }

    [Test]
    public void Shim_without_codex_js_is_codex_launcher_unavailable()
    {
        using var layout = new CodexNpmLayout(js: false);
        var ex = Should.Throw<CodexLaunchException>(() =>
            CodexWindowsLaunchPolicy.Apply(CodexRequest(layout.ShimPath, ["--no-alt-screen"], layout.Root), false));
        ex.Code.ShouldBe(CodexLaunchProblemTypes.LauncherUnavailable);
        ex.Message.ShouldContain("codex.js");
    }

    [Test]
    public void Recognized_shim_without_a_vendored_native_package_is_codex_launcher_unsupported()
    {
        using var layout = new CodexNpmLayout(native: false);
        var ex = Should.Throw<CodexLaunchException>(() =>
            CodexWindowsLaunchPolicy.Apply(CodexRequest(layout.ShimPath, ["--no-alt-screen"], layout.Root), false));
        ex.Code.ShouldBe(CodexLaunchProblemTypes.LauncherUnsupported);
    }

    [Test]
    public void A_customized_wrapper_is_not_rewritten_and_falls_under_the_batch_ceiling()
    {
        using var layout = new CodexNpmLayout(shimText: CodexWindowsLaunchPolicy.StockNpmShimText + "\necho extra\n");
        var original = CodexRequest(layout.ShimPath, ["--no-alt-screen"], layout.Root);
        var effective = CodexWindowsLaunchPolicy.Apply(original, useHerdr: false);
        effective.Exe.ShouldBe(original.Exe);

        var longArgs = new[] { new string('x', 8_000) };
        var ex = Should.Throw<CodexLaunchException>(() =>
            CodexWindowsLaunchPolicy.Apply(CodexRequest(layout.ShimPath, longArgs, layout.Root), false));
        ex.Code.ShouldBe(CodexLaunchProblemTypes.CommandLineTooLong);
        ex.Message.ShouldContain("recognized");
    }

    [Test]
    public void A_configured_budget_above_30000_does_not_raise_the_transport_ceiling()
    {
        using var layout = new CodexNpmLayout();
        var sentinel = "C0497-CEIL-" + Guid.NewGuid().ToString("N");
        var prefix = new[] { "--no-alt-screen", "-c" };
        var payload = FitHop1(layout, prefix, sentinel, 30_001);
        var ex = Should.Throw<CodexLaunchException>(() =>
            ApplySized(layout, prefix, payload, 40_000));
        ex.Code.ShouldBe(CodexLaunchProblemTypes.CommandLineTooLong);
        ex.Message.ShouldContain("30,000");
        ex.Message.ShouldNotContain("40,000");
    }

    [Test]
    public void Explicit_cmd_launcher_is_capped_at_7000()
    {
        var wrapper = Path.Combine(Path.GetTempPath(), $"c0497-wrap-{Guid.NewGuid():N}.cmd");
        File.WriteAllText(wrapper, "echo custom\n");
        try
        {
            var cwd = Path.GetDirectoryName(wrapper)!;
            var at7000 = FitExe(wrapper, 7_000);
            var at7001 = FitExe(wrapper, 7_001);
            Should.NotThrow(() =>
                CodexWindowsLaunchPolicy.Apply(CodexRequest(wrapper, [at7000], cwd), false));
            var ex = Should.Throw<CodexLaunchException>(() =>
                CodexWindowsLaunchPolicy.Apply(CodexRequest(wrapper, [at7001], cwd), false));
            ex.Code.ShouldBe(CodexLaunchProblemTypes.CommandLineTooLong);
            ex.Message.ShouldContain("recognized");

            var underConfigured = Should.Throw<CodexLaunchException>(() =>
                CodexWindowsLaunchPolicy.Apply(
                    CodexRequest(wrapper, [FitExe(wrapper, 6_001)], cwd, budget: 6_000), false));
            underConfigured.Code.ShouldBe(CodexLaunchProblemTypes.CommandLineTooLong);
        }
        finally
        {
            File.Delete(wrapper);
        }
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public void Nonpositive_budget_is_refused(int budget)
    {
        using var layout = new CodexNpmLayout();
        var ex = Should.Throw<CodexLaunchException>(() =>
            CodexWindowsLaunchPolicy.Apply(
                CodexRequest(layout.ShimPath, ["--no-alt-screen"], layout.Root, budget: budget), false));
        ex.Code.ShouldBe(CodexLaunchProblemTypes.CommandLineTooLong);
        ex.Message.ShouldContain("not positive");
    }

    [Test]
    public void Nul_in_any_argument_is_refused()
    {
        using var layout = new CodexNpmLayout();
        var ex = Should.Throw<CodexLaunchException>(() =>
            CodexWindowsLaunchPolicy.Apply(
                CodexRequest(layout.ShimPath, ["ok", "bad\0value"], layout.Root), false));
        ex.Code.ShouldBe(CodexLaunchProblemTypes.CommandLineTooLong);
        ex.Message.ShouldContain("NUL");
    }

    [Test]
    public void Surrogate_pairs_count_two_units()
    {
        using var layout = new CodexNpmLayout();
        var emojis = string.Concat(Enumerable.Repeat("😀", 2_048));
        emojis.Length.ShouldBe(4_096);
        var prefix = new[] { "--no-alt-screen", "-c" };
        var payload = FitHop1(layout, prefix, emojis, 30_001);
        payload.ShouldContain("😀");
        Encoding.UTF8.GetByteCount(payload).ShouldNotBe(payload.Length);

        var ex = Should.Throw<CodexLaunchException>(() => ApplySized(layout, prefix, payload, 30_000));
        ex.Code.ShouldBe(CodexLaunchProblemTypes.CommandLineTooLong);
        ex.Message.ShouldContain("30,001");
    }

    [Test]
    public void The_incident_fixture_is_accepted_on_the_node_path()
    {
        using var layout = new CodexNpmLayout();
        var args = new[]
        {
            "--no-alt-screen",
            "--dangerously-bypass-approvals-and-sandbox",
            "-c",
            CodexLaunchArgsDisablePasteBurst(),
            "-c",
            "developer_instructions=" + CodexInstructionFixtures.Incident,
        };
        var effective = CodexWindowsLaunchPolicy.Apply(
            CodexRequest(layout.ShimPath, args, layout.Root), false);
        var hop1 = WindowsCommandLine.Measure(effective.Exe, effective.Args);
        var hop2 = WindowsCommandLine.Measure(layout.NativePath, effective.Args.Skip(1).ToArray());
        hop1.ShouldBeLessThanOrEqualTo(30_000);
        hop2.ShouldBeLessThanOrEqualTo(30_000);
        Console.WriteLine($"incident hop1={hop1} hop2={hop2}");
    }

    [Test]
    public void Missing_node_never_returns_the_batch_request()
    {
        using var layout = new CodexNpmLayout(siblingNode: false);
        var dummy = Path.Combine(layout.Root, "no-node");
        Directory.CreateDirectory(dummy);
        var original = CodexRequest(
            layout.ShimPath, ["--no-alt-screen"], layout.Root,
            env: new Dictionary<string, string> { ["PATH"] = dummy });
        var ex = Should.Throw<CodexLaunchException>(() =>
            CodexWindowsLaunchPolicy.Apply(original, false));
        ex.Code.ShouldBe(CodexLaunchProblemTypes.LauncherUnavailable);
    }

    [Test]
    public void A_claude_request_whose_exe_ends_in_codex_cmd_is_untouched()
    {
        using var layout = new CodexNpmLayout();
        var request = new RunnerLaunchRequest(
            Guid.NewGuid(), layout.ShimPath, ["--append-system-prompt", "x"],
            new Dictionary<string, string>(), layout.Root, 120, 30,
            TranscriptFormat: TranscriptFormats.Claude);
        CodexWindowsLaunchPolicy.Apply(request, false).Exe.ShouldBe(request.Exe);
    }

    [Test]
    public void A_codex_transcript_request_with_a_non_shim_exe_is_untouched()
    {
        var exe = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        var request = CodexRequest(exe, ["--no-alt-screen"], Path.GetTempPath());
        var effective = CodexWindowsLaunchPolicy.Apply(request, false);
        effective.Exe.ShouldBe(request.Exe);
    }

    private static string CodexLaunchArgsDisablePasteBurst() => "disable_paste_burst=true";

    private static RunnerLaunchRequest CodexRequest(
        string exe,
        IReadOnlyList<string> args,
        string cwd,
        IReadOnlyDictionary<string, string>? env = null,
        int? budget = null) =>
        new(
            Guid.NewGuid(),
            exe,
            args,
            env ?? new Dictionary<string, string>(),
            cwd,
            120,
            30,
            TranscriptFormat: TranscriptFormats.Codex,
            CommandLineBudgetChars: budget);

    private static RunnerLaunchRequest ApplySized(
        CodexNpmLayout layout, IReadOnlyList<string> prefix, string payload, int budget) =>
        CodexWindowsLaunchPolicy.Apply(
            CodexRequest(layout.ShimPath, [.. prefix, "developer_instructions=" + payload], layout.Root, budget: budget),
            false);

    private static int MeasureHop1(CodexNpmLayout layout, IReadOnlyList<string> prefix, string payload) =>
        WindowsCommandLine.Measure(
            layout.SiblingNodePath!,
            PrependJs(layout, [.. prefix, "developer_instructions=" + payload]));

    private static int MeasureHop2(CodexNpmLayout layout, IReadOnlyList<string> prefix, string payload) =>
        WindowsCommandLine.Measure(
            layout.NativePath,
            [.. prefix, "developer_instructions=" + payload]);

    private static string[] PrependJs(CodexNpmLayout layout, IReadOnlyList<string> args)
    {
        var result = new string[args.Count + 1];
        result[0] = layout.JsPath;
        for (var i = 0; i < args.Count; i++)
            result[i + 1] = args[i];
        return result;
    }

    private static string FitHop1(CodexNpmLayout layout, IReadOnlyList<string> prefix, string sentinel, int target)
    {
        var filler = new string('A', Math.Max(8, target));
        var payload = sentinel + filler;
        var measured = MeasureHop1(layout, prefix, payload);
        if (measured > target)
        {
            var extra = measured - target;
            extra.ShouldBeLessThan(payload.Length);
            payload = payload[..^extra];
            MeasureHop1(layout, prefix, payload).ShouldBe(target);
            return payload;
        }

        payload += new string('A', target - measured);
        MeasureHop1(layout, prefix, payload).ShouldBe(target);
        return payload;
    }

    private static string FitExe(string exe, int target)
    {
        var filler = new string('A', Math.Max(8, target));
        var measured = WindowsCommandLine.Measure(exe, [filler]);
        if (measured > target)
        {
            filler = filler[..^ (measured - target)];
            WindowsCommandLine.Measure(exe, [filler]).ShouldBe(target);
            return filler;
        }

        filler += new string('A', target - measured);
        WindowsCommandLine.Measure(exe, [filler]).ShouldBe(target);
        return filler;
    }
}
