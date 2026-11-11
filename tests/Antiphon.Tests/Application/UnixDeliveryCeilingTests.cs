using System.Globalization;
using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1224 S2. Reads the committed fake-agent receipt and pins the Unix arm,
/// the runner report, and the two living docs. The arm and the docs land in S3,
/// so the witnesses that require them are red on this slice.
/// </summary>
[Category("Unit")]
public sealed class UnixDeliveryCeilingTests
{
    private const string MeasurementId = "card-1224-unixpty";
    private const string ReceiptName = "2026-11-09-card-1224-linux-delivery-envelope-receipt.md";
    private const string SpillMarker = "YOUR BRIEF IS NOT IN THIS MESSAGE";
    private const string ContractSentence =
        "A UnixPty body over the recorded single-write ceiling is still sent and raises OversizedTerminalDelivery.";

    private const int EvidenceEdge = 86_400;
    private const int InboxSingle = 1_024;
    private const int InboxBrief = 900;
    private const int InboxReply = 3_000;
    private const int RaiseFloor = 1_024;

    private static readonly int[] SingleSizes = [64, 900, 1024, 1350, 4096, 16384, 43200, 86400];
    private static readonly int[] PacedSizes = [1350, 43200, 86400];

    [Test]
    public void Unix_arm_names_the_receipt()
    {
        var receipt = LoadReceipt();
        var ceilings = new DelegationSettings().CeilingsFor(PtyBackend.UnixPty, "probe");

        ceilings.Reason.ShouldContain(receipt.MeasurementId);
    }

    [Test]
    public void Unix_numbers_match_the_receipt()
    {
        var receipt = LoadReceipt();
        var expected = Derive(receipt);
        receipt.Single.ShouldBe(expected.Single, "the receipt summary is D-3 applied to its trials");
        receipt.Brief.ShouldBe(expected.Brief, "the receipt summary is D-3 applied to its trials");
        receipt.Reply.ShouldBe(expected.Reply, "the receipt summary is D-3 applied to its trials");

        var ceilings = new DelegationSettings().CeilingsFor(PtyBackend.UnixPty, "probe");
        ceilings.SingleWriteMaxBytes.ShouldBe(expected.Single);
        ceilings.BriefInlineMaxBytes.ShouldBe(expected.Brief);
        ceilings.ReplyInlineMaxChars.ShouldBe(expected.Reply);
        if (expected.Raised)
        {
            ceilings.Backend.ToString().ShouldBe("UnixPty");
            ((int)ceilings.Backend).ShouldBe(3);
        }
        else
        {
            ceilings.Backend.ShouldBe(DeliveryBackend.InboxConhost);
        }
    }

    [Test]
    public void Claude_brief_follows_the_recorded_inline_ceiling()
    {
        var expected = Derive(LoadReceipt());
        var settings = new DelegationSettings();
        var ceilings = settings.CeilingsFor(PtyBackend.UnixPty, "probe");
        var spillRoot = Path.Combine(Path.GetTempPath(), "antiphon-c1224-s2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(spillRoot);
        try
        {
            var under = BriefTask(settings, ceilings.ReplyInlineMaxChars, expected.Brief, over: false, spillRoot);
            var over = BriefTask(settings, ceilings.ReplyInlineMaxChars, expected.Brief, over: true, spillRoot);

            var underTyped = AgentTaskDispatcher.FitBriefForTyping(
                under, settings, ceilings, agentKind: AgentKind.ClaudeCode);
            var overTyped = AgentTaskDispatcher.FitBriefForTyping(
                over, settings, ceilings, agentKind: AgentKind.ClaudeCode);

            underTyped.ShouldNotContain(SpillMarker,
                customMessage: "a Claude brief at or under the recorded inline ceiling is typed inline");
            underTyped.ShouldContain("C1224GOAL");
            overTyped.ShouldContain(SpillMarker,
                customMessage: "a Claude brief over the recorded inline ceiling spills to a pointer");
        }
        finally
        {
            if (Directory.Exists(spillRoot))
                Directory.Delete(spillRoot, recursive: true);
        }
    }

    [Test]
    public void Runner_unix_report_uses_the_unix_arm()
    {
        var receipt = LoadReceipt();
        var bound = RunnerBoundDelivery.ForRunner(new DelegationSettings(), "UnixPty");

        bound.Reason.ShouldContain(receipt.MeasurementId);
    }

    [Test]
    public void Runner_without_a_report_stays_inbox()
    {
        var settings = new DelegationSettings();
        var receipt = LoadReceipt();

        foreach (var report in new string?[] { null, "ModernConPty" })
        {
            var bound = RunnerBoundDelivery.ForRunner(settings, report);
            bound.BriefInlineMaxBytes.ShouldBe(InboxBrief);
            bound.SingleWriteMaxBytes.ShouldBe(InboxSingle);
            bound.Backend.ShouldBe(DeliveryBackend.InboxConhost);
            bound.Reason.ShouldNotContain(receipt.MeasurementId);
        }
    }

    [Test]
    public void Non_claude_brief_inline_ceiling_stays_zero()
    {
        var ceilings = new DelegationSettings().CeilingsFor(PtyBackend.UnixPty, "probe");

        ceilings.ForAgentKind(AgentKind.Grok).BriefInlineMaxBytes.ShouldBe(0);
        ceilings.ForAgentKind(AgentKind.Codex).BriefInlineMaxBytes.ShouldBe(0);
    }

    [Test]
    public void Docker_stack_records_the_unix_receipt()
    {
        var receipt = LoadReceipt();
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "docker-stack.md"));

        text.ShouldContain(receipt.MeasurementId);
        text.ShouldContain(receipt.Single.ToString(CultureInfo.InvariantCulture));
        text.ShouldContain(receipt.Brief.ToString(CultureInfo.InvariantCulture));
        text.ShouldContain(receipt.Reply.ToString(CultureInfo.InvariantCulture));
    }

    [Test]
    public void Session_runtime_records_the_unix_contract()
    {
        var receipt = LoadReceipt();
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "session-runtime-invariants.md"));

        text.ShouldContain(receipt.MeasurementId);
        text.ShouldContain(ContractSentence);
        text.ShouldContain(receipt.Single.ToString(CultureInfo.InvariantCulture));
        text.ShouldContain(receipt.Brief.ToString(CultureInfo.InvariantCulture));
        text.ShouldContain(receipt.Reply.ToString(CultureInfo.InvariantCulture));
    }

    [Test]
    public void Specialist_full_inline_stays_on_modern()
    {
        var text = File.ReadAllText(Path.Combine(
            RepoRoot(), "server", "Application", "Services", "SpecialistInputPolicy.cs"));

        text.ShouldContain("Backend != DeliveryBackend.ModernConPty");
    }

    private static AgentTask BriefTask(
        DelegationSettings settings, int replyChars, int recordedBrief, bool over, string spillRoot)
    {
        var empty = Measure(0);
        empty.ShouldBeLessThanOrEqualTo(recordedBrief,
            "a Claude brief with an empty goal must fit the recorded inline ceiling");

        var high = recordedBrief;
        while (Measure(high) <= recordedBrief)
            high = checked(high * 2 + 1);

        var low = 0;
        while (low < high)
        {
            var mid = low + ((high - low + 1) / 2);
            if (Measure(mid) <= recordedBrief)
                low = mid;
            else
                high = mid - 1;
        }

        var pad = over ? low + 1 : low;
        var size = Measure(pad);
        if (over)
            size.ShouldBeGreaterThan(recordedBrief);
        else
            size.ShouldBeLessThanOrEqualTo(recordedBrief);
        return NewTask(Goal(pad), spillRoot);

        int Measure(int padBytes)
        {
            var probe = NewTask(Goal(padBytes), spillRoot);
            var text = DelegationReportFormatter.BuildBrief(probe, settings, replyChars);
            return System.Text.Encoding.UTF8.GetByteCount(text);
        }
    }

    private static string Goal(int pad) => "C1224GOAL " + new string('x', pad);

    private static AgentTask NewTask(string goal, string spillRoot) => new()
    {
        Id = Guid.NewGuid(),
        Title = "ceiling probe",
        Goal = goal,
        Kind = AgentTaskKind.Worker,
        Role = AgentTaskRole.Docs,
        ModelLevel = AgentModelLevel.Medium,
        Workspace = WorkspaceMode.Shared,
        WorkingDirectory = spillRoot,
        Status = AgentTaskStatus.Dispatched,
    };

    private static DerivedCeilings Derive(Receipt receipt)
    {
        var wholeSingle = LargestWhole(receipt.Trials, "single", SingleSizes);
        var wholePaced = LargestWhole(receipt.Trials, "paced", PacedSizes);
        if (!receipt.MarkersSurvived || wholeSingle < RaiseFloor || wholePaced < RaiseFloor)
            return new DerivedCeilings(InboxSingle, InboxBrief, InboxReply, false);

        var single = Math.Min(wholeSingle, EvidenceEdge);
        var brief = Math.Min(wholePaced, single / 2);
        if (brief < RaiseFloor)
            return new DerivedCeilings(InboxSingle, InboxBrief, InboxReply, false);

        return new DerivedCeilings(single, brief, brief / 3, true);
    }

    private static int LargestWhole(IReadOnlyList<Trial> trials, string mode, int[] sizes)
    {
        var whole = 0;
        foreach (var size in sizes)
        {
            var reps = trials.Where(row => row.Mode == mode && row.Size == size).ToArray();
            if (reps.Length == 2 && reps.All(row => row.Whole) && size > whole)
                whole = size;
        }

        return whole;
    }

    private static Receipt LoadReceipt()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var trials = new List<Trial>();
        foreach (var raw in File.ReadAllLines(Path.Combine(
            RepoRoot(), "docs", "superpowers", "plans", ReceiptName)))
        {
            if (raw.Length == 0)
                continue;
            if (raw.StartsWith("trial: ", StringComparison.Ordinal))
            {
                var parts = raw.Split(' ');
                if (parts.Length != 5 || (parts[1] != "single" && parts[1] != "paced")
                    || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var size)
                    || !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var rep)
                    || (parts[4] != "WHOLE" && parts[4] != "LOST"))
                    throw new InvalidOperationException("malformed trial line: " + raw);
                trials.Add(new Trial(parts[1], size, rep, parts[4] == "WHOLE"));
                continue;
            }

            var split = raw.IndexOf(':');
            if (split <= 0)
                throw new InvalidOperationException("malformed receipt line: " + raw);
            values[raw[..split]] = raw[(split + 1)..].Trim();
        }

        values["measurementId"].ShouldBe(MeasurementId);
        values["oracle"].ShouldBe("fake-agent");
        values["codex"].ShouldBe("unmeasured");
        return new Receipt(
            values["measurementId"],
            ParseBool(values, "markersSurvived"),
            ParseInt(values, "singleWriteBytes"),
            ParseInt(values, "briefInlineMaxBytes"),
            ParseInt(values, "replyInlineMaxChars"),
            trials);
    }

    private static int ParseInt(Dictionary<string, string> values, string key)
    {
        values.TryGetValue(key, out var text).ShouldBeTrue(key);
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value).ShouldBeTrue(key);
        return value;
    }

    private static bool ParseBool(Dictionary<string, string> values, string key)
    {
        values.TryGetValue(key, out var text).ShouldBeTrue(key);
        if (text == "true")
            return true;
        if (text == "false")
            return false;
        throw new InvalidOperationException("malformed bool " + key + ": " + text);
    }

    private static string RepoRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "docs", "superpowers", "plans", ReceiptName)))
                    return dir.FullName;
                dir = dir.Parent;
            }
        }

        throw new FileNotFoundException("card-1224 receipt is not committed at docs/superpowers/plans/" + ReceiptName);
    }

    private sealed record Receipt(
        string MeasurementId, bool MarkersSurvived, int Single, int Brief, int Reply, IReadOnlyList<Trial> Trials);

    private readonly record struct Trial(string Mode, int Size, int Rep, bool Whole);

    private readonly record struct DerivedCeilings(int Single, int Brief, int Reply, bool Raised);
}
