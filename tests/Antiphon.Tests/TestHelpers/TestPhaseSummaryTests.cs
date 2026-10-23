using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class TestPhaseSummaryTests
{
    [Test]
    [Arguments("full-match")]
    [Arguments("trx-only")]
    [Arguments("torn-tail")]
    [Arguments("conflicting-duplicate")]
    [Arguments("stale-run")]
    [Arguments("ambiguous-missing")]
    [Timeout(180_000)]
    public async Task C1514T_MergeAndQuality(string mode, CancellationToken cancellationToken)
    {
        var root = C1514Support.FreshRoot();
        if (mode == "full-match")
        {
            await C1514Support.LaunchProbe(root, enabled: true, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(root, "timing", "groups-planned.json"),
                "{\"version\":1,\"runId\":\"run-a\"}", cancellationToken);
        }
        else
        {
            Directory.CreateDirectory(Path.Combine(root, "timing"));
            await File.WriteAllTextAsync(Path.Combine(root, "timing", "groups-planned.json"),
                "{\"version\":1,\"runId\":\"run-a\",\"assembly\":\"asm1\"}", cancellationToken);
            WriteLiteral(root, mode);
        }

        var before = Fingerprint(root);
        var ran = await Summarize(root, cancellationToken);
        ran.ExitCode.ShouldBe(0, ran.Text);
        before.ShouldBe(Fingerprint(root));
        var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "timing", "summary.json"))).RootElement;
        var tests = summary.GetProperty("tests");
        var quality = Quality(summary);
        switch (mode)
        {
            case "full-match":
                tests.GetArrayLength().ShouldBe(7);
                summary.GetProperty("complete").GetBoolean().ShouldBeTrue();
                var failed = 0;
                foreach (var row in tests.EnumerateArray())
                {
                    row.GetProperty("joined").GetBoolean().ShouldBeTrue();
                    row.GetProperty("incomplete").GetBoolean().ShouldBeFalse();
                    var outcome = row.GetProperty("outcome").GetString();
                    (outcome is "Passed" or "Failed").ShouldBeTrue();
                    if (outcome == "Failed")
                        failed++;
                }

                failed.ShouldBe(2);
                break;
            case "trx-only":
                tests.GetArrayLength().ShouldBe(1);
                tests[0].GetProperty("outcome").GetString().ShouldBe("Failed");
                tests[0].GetProperty("joined").GetBoolean().ShouldBeFalse();
                tests[0].GetProperty("db").GetInt64().ShouldBe(0);
                break;
            case "torn-tail":
                quality.ShouldContain("torn-tail");
                summary.GetProperty("tornLines").GetInt32().ShouldBeGreaterThan(0);
                tests[0].GetProperty("outcome").GetString().ShouldBe("Passed");
                break;
            case "conflicting-duplicate":
                quality.ShouldContain("conflicting-duplicate");
                tests[0].GetProperty("outcome").GetString().ShouldNotBe("Passed");
                tests[0].GetProperty("incomplete").GetBoolean().ShouldBeTrue();
                break;
            case "stale-run":
                quality.ShouldContain("stale-input-not-joined");
                break;
            case "ambiguous-missing":
                quality.ShouldContain("ambiguous-id");
                quality.ShouldContain("missing-terminal-remains-incomplete");
                Row(tests, "phase-only").GetProperty("outcome").GetString().ShouldNotBe("Passed", "missing-terminal-remains-incomplete");
                Row(tests, "started-only").GetProperty("outcome").GetString().ShouldNotBe("Passed", "missing-terminal-remains-incomplete");
                Row(tests, "phase-only").GetProperty("incomplete").GetBoolean().ShouldBeTrue("missing-terminal-remains-incomplete");
                break;
            default:
                throw new InvalidOperationException(mode);
        }
    }

    [Test]
    [Timeout(180_000)]
    public async Task C1514T_PS51AndRerun(CancellationToken cancellationToken)
    {
        foreach (var relative in new[] { "scripts/lib/test-phase-evidence.ps1", "scripts/summarize-test-phases.ps1" })
        {
            var path = Path.Combine(C1514Support.RepoRoot(), relative);
            var text = File.ReadAllText(path);
            text.ShouldContain("#requires -Version 5.1", Case.Sensitive);
            text.ShouldNotContain("??");
            text.ShouldNotContain("?.");
            text.ShouldNotContain("-AsHashtable");
            text.ShouldNotContain("HashData");
            foreach (var b in System.Text.Encoding.UTF8.GetBytes(text))
                b.ShouldBeLessThan((byte)128);
        }

        var parser = Path.Combine(Path.GetTempPath(), "c1514-parse-" + Guid.NewGuid().ToString("N") + ".ps1");
        await File.WriteAllTextAsync(parser, """
            $ErrorActionPreference = 'Stop'
            $tokens = $null
            $errors = $null
            foreach ($rel in @('scripts/lib/test-phase-evidence.ps1','scripts/summarize-test-phases.ps1')) {
                [void][System.Management.Automation.Language.Parser]::ParseFile((Join-Path $args[0] $rel), [ref]$tokens, [ref]$errors)
                if ($errors -and $errors.Count -gt 0) { throw $errors[0].ToString() }
            }
            Write-Output 'PARSE_OK'
            """, cancellationToken);
        var parsed = await C1514Support.RunProcess(Pwsh(parser, C1514Support.RepoRoot()), cancellationToken);
        parsed.ExitCode.ShouldBe(0, parsed.Text);
        parsed.Text.ShouldContain("PARSE_OK");

        var absent = C1514Support.FreshRoot();
        var absentRun = await Summarize(absent, cancellationToken);
        absentRun.ExitCode.ShouldBe(0, absentRun.Text);
        File.Exists(Path.Combine(absent, "timing", "summary.json")).ShouldBeFalse();

        var root = C1514Support.FreshRoot();
        Directory.CreateDirectory(Path.Combine(root, "timing"));
        await File.WriteAllTextAsync(Path.Combine(root, "timing", "groups-planned.json"),
            "{\"version\":1,\"runId\":\"run-a\",\"assembly\":\"asm1\"}", cancellationToken);
        WriteLiteral(root, "trx-only");
        var inputs = Fingerprint(root);
        var first = await Summarize(root, cancellationToken);
        first.ExitCode.ShouldBe(0, first.Text);
        var summary = File.ReadAllBytes(Path.Combine(root, "timing", "summary.json"));
        var csv = File.ReadAllBytes(Path.Combine(root, "timing", "tests.csv"));
        var markdown = File.ReadAllBytes(Path.Combine(root, "timing", "summary.md"));
        summary.Length.ShouldBeGreaterThan(0);
        csv.Length.ShouldBeGreaterThan(0);
        markdown.Length.ShouldBeGreaterThan(0);
        var second = await Summarize(root, cancellationToken);
        second.ExitCode.ShouldBe(0, second.Text);
        File.ReadAllBytes(Path.Combine(root, "timing", "summary.json")).ShouldBe(summary);
        File.ReadAllBytes(Path.Combine(root, "timing", "tests.csv")).ShouldBe(csv);
        File.ReadAllBytes(Path.Combine(root, "timing", "summary.md")).ShouldBe(markdown);
        Fingerprint(root).ShouldBe(inputs);
    }

    private static void WriteLiteral(string root, string mode)
    {
        var timing = Path.Combine(root, "timing");
        string Header(string run, string assembly) =>
            "{\"recordKind\":\"header\",\"version\":1,\"pid\":1,\"nonce\":\"n1\",\"frequencyHz\":10000000,\"epochUtc\":\"2026-10-23T00:00:00.0000000Z\",\"runId\":\""
            + run + "\",\"groupId\":\"g\",\"attempt\":\"1\",\"suite\":\"s\",\"assembly\":\"" + assembly + "\"}";
        string Complete(string id, string outcome) =>
            "{\"recordKind\":\"complete\",\"testId\":\"" + id + "\",\"className\":\"C\",\"method\":\"M\",\"args\":[],\"startOffsetMs\":0,\"endOffsetMs\":1,\"outcome\":\""
            + outcome + "\",\"phases\":[],\"processes\":[],\"exclusive\":{\"db\":0,\"host\":0,\"process\":0,\"fs\":0,\"network\":0,\"fixture\":0},\"overlapMs\":0,\"remainderMs\":0,\"childUnionMs\":0,\"droppedPhaseCount\":0,\"shared\":false,\"incomplete\":false}";
        string Trx(string body) => "<?xml version=\"1.0\" encoding=\"utf-8\"?><TestRun><Results>" + body + "</Results></TestRun>";
        string Row(string id, string outcome, string stdout) =>
            "<UnitTestResult testId=\"" + id + "\" testName=\"" + id + "\" outcome=\"" + outcome + "\"><Output><StdOut>" + stdout + "</StdOut></Output></UnitTestResult>";

        switch (mode)
        {
            case "trx-only":
                File.WriteAllText(Path.Combine(root, "run.trx"), Trx(Row("only-trx", "Failed", "")));
                break;
            case "torn-tail":
                File.WriteAllText(Path.Combine(timing, "cases-1-n1.jsonl"), Header("run-a", "asm1") + "\n" + Complete("case-a", "Passed") + "\n{\"recordKind\":\"comp");
                File.WriteAllText(Path.Combine(root, "run.trx"), Trx(Row("case-a", "Passed", "ok")));
                break;
            case "conflicting-duplicate":
                File.WriteAllText(Path.Combine(timing, "cases-1-n1.jsonl"), Header("run-a", "asm1") + "\n" + Complete("case-a", "Passed") + "\n" + Complete("case-a", "Failed") + "\n");
                File.WriteAllText(Path.Combine(root, "run.trx"), Trx(Row("case-a", "Passed", "ok")));
                break;
            case "stale-run":
                File.WriteAllText(Path.Combine(timing, "cases-1-n1.jsonl"), Header("other", "asm1") + "\n" + Complete("case-a", "Passed") + "\n");
                File.WriteAllText(Path.Combine(root, "run.trx"), Trx(Row("case-a", "Failed", "ok")));
                break;
            case "ambiguous-missing":
                var start = "{\"recordKind\":\"start\",\"testId\":\"started-only\",\"className\":\"\",\"method\":\"\",\"args\":[],\"startOffsetMs\":0}";
                File.WriteAllText(Path.Combine(timing, "cases-1-n1.jsonl"),
                    Header("run-a", "asm1") + "\n" + start + "\n" + Complete("phase-only", "Passed") + "\n" + Complete("native-shared", "Passed") + "\n");
                File.WriteAllText(Path.Combine(root, "run.trx"), Trx(
                    Row("guid-1", "Passed", "C1514_NATIVE body native-shared")
                    + Row("guid-2", "Failed", "C1514_NATIVE body native-shared")));
                break;
        }
    }

    private static JsonElement Row(JsonElement tests, string id)
    {
        foreach (var row in tests.EnumerateArray())
        {
            if (row.GetProperty("testId").GetString() == id)
                return row;
        }

        throw new InvalidOperationException("missing row " + id);
    }

    private static List<string> Quality(JsonElement summary)
    {
        var values = new List<string>();
        foreach (var item in summary.GetProperty("quality").EnumerateArray())
            values.Add(item.GetString() ?? "");
        return values;
    }

    private static string Fingerprint(string root)
    {
        var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}timing{Path.DirectorySeparatorChar}summary", StringComparison.Ordinal)
                && !path.EndsWith("tests.csv", StringComparison.Ordinal)
                && !path.EndsWith("summary.md", StringComparison.Ordinal)
                && !path.EndsWith("phase-summary-failed", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal);
        var sb = new System.Text.StringBuilder();
        foreach (var path in files)
        {
            sb.Append(path).Append(':').Append(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))).Append(';');
        }

        return sb.ToString();
    }

    private static async Task<(int ExitCode, string Text)> Summarize(string root, CancellationToken cancellationToken)
    {
        var script = Path.Combine(C1514Support.RepoRoot(), "scripts", "summarize-test-phases.ps1");
        return await C1514Support.RunProcess(Pwsh(script, "-RunDirectory", root, "-Top", "50"), cancellationToken);
    }

    private static ProcessStartInfo Pwsh(string script, params string[] args)
    {
        var start = new ProcessStartInfo("pwsh");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(script);
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        return start;
    }
}
