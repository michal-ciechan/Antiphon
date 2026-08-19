using System.Text;
using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0025: the origin-agnostic spill helper, no pty. Queue/spawn call sites live in their
/// own suites; this pins the contract those sites rely on.
/// </summary>
[Category("Unit")]
public class TypedBodySpillTests
{
    private static PtyDeliveryCeilings Inbox =>
        new DelegationSettings().CeilingsFor(PtyBackend.InboxConhost, "test");

    private static PtyDeliveryCeilings Modern =>
        new DelegationSettings().CeilingsFor(PtyBackend.ModernConPty, "test");

    [Test]
    public void Under_the_ceiling_returns_the_original_and_writes_no_file()
    {
        using var tmp = new TempDir();
        var body = "short enough";
        var path = TypedBodySpill.InboxAbsolutePath(tmp.Path, Guid.NewGuid().ToString("D"));

        var result = TypedBodySpill.Fit(new(
            Body: body,
            CeilingBytes: Inbox.SingleWriteMaxBytes,
            AbsoluteSpillPath: path,
            RelativeSpillPath: TypedBodySpill.InboxRelativePath("id")));

        result.Spilled.ShouldBeFalse();
        result.ToType.ShouldBe(body);
        File.Exists(path).ShouldBeFalse();
    }

    [Test]
    public void Over_the_ceiling_writes_the_original_and_returns_a_short_pointer()
    {
        using var tmp = new TempDir();
        var body = new string('x', 2_000);
        Encoding.UTF8.GetByteCount(body).ShouldBeGreaterThan(Inbox.SingleWriteMaxBytes);
        var stem = Guid.NewGuid().ToString("D");
        var path = TypedBodySpill.InboxAbsolutePath(tmp.Path, stem);
        var relative = TypedBodySpill.InboxRelativePath(stem);

        var result = TypedBodySpill.Fit(new(
            Body: body,
            CeilingBytes: Inbox.SingleWriteMaxBytes,
            AbsoluteSpillPath: path,
            RelativeSpillPath: relative));

        result.Spilled.ShouldBeTrue();
        File.ReadAllText(path).ShouldBe(body);
        result.ToType.ShouldContain(TypedBodySpill.PointerHeadline);
        result.ToType.ShouldContain(relative);
        result.ToType.ShouldNotContain("[antiphon-task:");
        Encoding.UTF8.GetByteCount(result.ToType).ShouldBeLessThan(Inbox.SingleWriteMaxBytes,
            "the pointer itself must fit in one inbox read chunk");
        result.ToType.ShouldNotBe(body);
    }

    [Test]
    public void Write_failure_returns_the_original()
    {
        using var tmp = new TempDir();
        var blocker = Path.Combine(tmp.Path, "not-a-dir");
        File.WriteAllText(blocker, "x");
        var path = Path.Combine(blocker, "inbox", "id.md");
        var body = new string('y', 2_000);

        var result = TypedBodySpill.Fit(new(
            Body: body,
            CeilingBytes: Inbox.SingleWriteMaxBytes,
            AbsoluteSpillPath: path,
            RelativeSpillPath: TypedBodySpill.InboxRelativePath("id")));

        result.Spilled.ShouldBeFalse();
        result.ToType.ShouldBe(body);
    }

    [Test]
    public void Grok_pointer_is_one_line_with_the_path_quoted()
    {
        using var tmp = new TempDir();
        var body = new string('z', 2_000);
        var relative = TypedBodySpill.InboxRelativePath("id");
        var path = TypedBodySpill.InboxAbsolutePath(tmp.Path, "id");

        var pointer = TypedBodySpill.Fit(new(
            Body: body,
            CeilingBytes: Inbox.SingleWriteMaxBytes,
            AbsoluteSpillPath: path,
            RelativeSpillPath: relative,
            AgentKind: AgentKind.Grok)).ToType;

        pointer.ShouldNotContain("\n");
        pointer.ShouldContain($"'{relative}' Everything you need is there",
            customMessage: "quoted AND separated: the path must not grow the next sentence");
        pointer.ShouldContain(TypedBodySpill.PointerHeadline);
    }

    [Test]
    public void Channel_envelope_prefix_survives_on_the_pointer()
    {
        using var tmp = new TempDir();
        var envelope = "[Telegram \"Family\" — Mike (@mciechan) 14:32]";
        var body = envelope + " " + new string('m', 2_000);
        var path = TypedBodySpill.InboxAbsolutePath(tmp.Path, "id");

        TypedBodySpill.TryReadChannelEnvelope(body).ShouldBe(envelope);

        var pointer = TypedBodySpill.Fit(new(
            Body: body,
            CeilingBytes: Inbox.SingleWriteMaxBytes,
            AbsoluteSpillPath: path,
            RelativeSpillPath: TypedBodySpill.InboxRelativePath("id"),
            EnvelopePrefix: envelope)).ToType;

        pointer.ShouldStartWith(envelope);
        pointer.ShouldContain(TypedBodySpill.PointerHeadline);
        pointer.ShouldNotContain(new string('m', 50),
            customMessage: "the oversize text lives in the file, not on the pointer");
    }

    [Test]
    public void The_same_spawn_prompt_spills_on_inbox_and_is_typed_inline_on_modern()
    {
        using var tmp = new TempDir();
        var prompt = "Work on card CARD-0001: title\n\nDescription:\n" + new string('a', 20_000);
        Encoding.UTF8.GetByteCount(prompt).ShouldBeGreaterThan(Inbox.BriefInlineMaxBytes);
        Encoding.UTF8.GetByteCount(prompt).ShouldBeLessThan(Modern.BriefInlineMaxBytes);
        var path = TypedBodySpill.InboxAbsolutePath(tmp.Path, "spawn-deadbeef");
        var relative = TypedBodySpill.InboxRelativePath("spawn-deadbeef");

        var onInbox = TypedBodySpill.Fit(new(prompt, Inbox.BriefInlineMaxBytes, path, relative));
        onInbox.Spilled.ShouldBeTrue();
        onInbox.ToType.ShouldContain(TypedBodySpill.PointerHeadline);
        File.ReadAllText(path).ShouldBe(prompt);

        File.Delete(path);
        var onModern = TypedBodySpill.Fit(new(prompt, Modern.BriefInlineMaxBytes, path, relative));
        onModern.Spilled.ShouldBeFalse();
        onModern.ToType.ShouldBe(prompt);
        File.Exists(path).ShouldBeFalse();
    }

    [Test]
    public void Api_fallback_is_used_when_the_file_cannot_be_written()
    {
        using var tmp = new TempDir();
        var blocker = Path.Combine(tmp.Path, "not-a-dir");
        File.WriteAllText(blocker, "x");
        var body = new string('q', 2_000);

        var result = TypedBodySpill.Fit(new(
            Body: body,
            CeilingBytes: Inbox.SingleWriteMaxBytes,
            AbsoluteSpillPath: Path.Combine(blocker, "inbox", "id.md"),
            ApiFallback: "GET /api/cards/CARD-0001"));

        result.Spilled.ShouldBeTrue();
        result.ToType.ShouldContain("GET /api/cards/CARD-0001");
        result.ToType.ShouldContain(TypedBodySpill.PointerHeadline);
        result.ToType.ShouldNotBe(body);
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"antiphon-typed-spill-{Guid.NewGuid():N}");

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
            catch { /* best-effort */ }
        }
    }
}
