using Antiphon.Server.Application.Services;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

// CARD-0604 G-21. A REMOTE session's spilled body must never be written on the desktop: the
// session's Cwd is a Windows path it cannot see, so a desktop write leaves a file no one reads
// behind a prompt that tells the agent to "read it in full before you do anything else".
[Category("Unit")]
public sealed class PhoneHomeSpillTests
{
    [Test]
    public void Remote_spill_never_writes_desktop_file()
    {
        var desktopCwd = Path.Combine(Path.GetTempPath(), "c604-spill-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(desktopCwd);
        try
        {
            var relative = TypedBodySpill.InboxRelativePath("brief");
            var absolute = TypedBodySpill.InboxAbsolutePath(desktopCwd, "brief");
            var body = new string('x', 4096);

            // The remote shape: no absolute path is ever handed to the spill, and the pointer is
            // built from the relative path the RUNNER will write to.
            var remote = TypedBodySpill.Fit(new TypedBodySpill.Request(
                Body: body, CeilingBytes: 512, AbsoluteSpillPath: null,
                RelativeSpillPath: relative, ApiFallback: relative));
            remote.Spilled.ShouldBeTrue();
            remote.ToType.ShouldContain(relative);
            File.Exists(absolute).ShouldBeFalse("a remote spill wrote a desktop file");

            // The local shape still writes, so the guard is about the remote branch, not about
            // spilling having stopped working.
            var local = TypedBodySpill.Fit(new TypedBodySpill.Request(
                Body: body, CeilingBytes: 512, AbsoluteSpillPath: absolute,
                RelativeSpillPath: relative));
            local.Spilled.ShouldBeTrue();
            File.Exists(absolute).ShouldBeTrue();
        }
        finally
        {
            if (Directory.Exists(desktopCwd))
                Directory.Delete(desktopCwd, recursive: true);
        }
    }

    [Test]
    public void Remote_spill_travels_in_input_payload()
    {
        var courier = new RemoteSpillCourier();
        var sessionId = Guid.NewGuid();
        courier.IsStaged(sessionId).ShouldBeFalse();

        courier.Stage(sessionId, "/work/worktrees/task-deadbeef",
            new PhoneHomeInputSpill(".antiphon/inbox/brief.md", "the whole brief"));
        courier.IsStaged(sessionId).ShouldBeTrue();

        courier.TryTake(sessionId, out var staged).ShouldBeTrue();
        staged.RunnerCwd.ShouldBe("/work/worktrees/task-deadbeef");
        staged.Spill.RelativePath.ShouldBe(".antiphon/inbox/brief.md");
        staged.Spill.Body.ShouldBe("the whole brief");

        // One-shot: a retyped pointer after a composer-evidence retry must not rewrite the file,
        // and a body left staged by a delivery that never happened must not leak into the next.
        courier.TryTake(sessionId, out _).ShouldBeFalse();
        courier.IsStaged(sessionId).ShouldBeFalse();
    }

    [Test]
    public void Different_pointers_keep_their_own_staged_bodies()
    {
        var courier = new RemoteSpillCourier();
        var sessionId = Guid.NewGuid();
        courier.Stage(sessionId, "/work", new PhoneHomeInputSpill("a.md", "first"));
        courier.Stage(sessionId, "/work", new PhoneHomeInputSpill("b.md", "second"));
        courier.TryPeek(sessionId, "read a.md", out var first).ShouldBeTrue();
        first.Spill.Body.ShouldBe("first");
        courier.TryPeek(sessionId, "read b.md", out var second).ShouldBeTrue();
        second.Spill.Body.ShouldBe("second");
        courier.Ack(sessionId, first).ShouldBeTrue();
        courier.TryPeek(sessionId, "read b.md", out _).ShouldBeTrue();
    }

    [Test]
    public void Sessions_do_not_share_a_staged_body()
    {
        var courier = new RemoteSpillCourier();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        courier.Stage(first, "/work", new PhoneHomeInputSpill("a.md", "first"));
        courier.TryTake(second, out _).ShouldBeFalse();
        courier.TryTake(first, out var staged).ShouldBeTrue();
        staged.Spill.Body.ShouldBe("first");
    }
}
