using System.Text.Json.Nodes;
using Antiphon.NightlyWatchdog;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0545. The 13 CARD-0544 notification/outage/recipient controls (G/PC-78..86, 95..98) keep their IDs, guard
/// text and method names and now run in-process against the independent watchdog on <see cref="C545World"/>;
/// positive receipt evidence is produced only by the fake transport delivering into the fake recipient chat.
/// Plus the CARD-0545 delivery/readiness controls and the wrappers for its PowerShell harness cases.
/// </summary>
public sealed partial class NightlyVerificationContractTests
{
    private const string C545Due = "2026-09-18";

    private static async Task<C545World> HopWorld(Action<C545World>? setup = null)
    {
        var world = await C545World.CreateAsync();
        world.Windmill.Jobs.Add(world.Jobs.HopFailed(C545Due, "j-hop"));
        setup?.Invoke(world);
        return world;
    }

    private static string HopNid(C545World world) => world.Outages().Single(o => o.Kind == "windows-hop-failed").FailureNid!;

    // G-78 D-6: Windows/SSH outage must be detected outside that failure domain.
    [Test]
    public async Task C544_IndependentOutage()
    {
        await using var world = await HopWorld(w =>
        {
            w.Windmill.WorkerLastPingUtc = C545World.Start.UtcDateTime.AddMinutes(-61);
            w.Windmill.Jobs[0] = w.Jobs.HopFailed(C545Due, "j-hop", "ssh: connect to host windows port 22: Connection timed out\r\nexit 255");
        });
        var tick1 = await world.TickAsync();
        tick1.Transitions.ShouldContain(t => t.Change == OutageChange.Opened && t.Kind == "windows-hop-failed", "hop opened");
        tick1.Transitions.ShouldContain(t => t.Change == OutageChange.Opened && t.Kind == "desktop-worker-missing", "worker missing opened");
        var hopNid = HopNid(world);
        tick1.Sends.ShouldContain(s => s.Nid == hopNid && s.Accepted, "hop failure sent");
        world.Chat.Messages.Select(m => m.Text).ShouldContain(world.Attempts(hopNid)[0].Body, "recipient chat has the produced body");

        var tick2 = await world.TickAsync();
        tick2.Imports.ShouldContain(i => i.Nid == hopNid && i.Reason == "imported", "hop receipt imported");
        world.Notification(hopNid).State.ShouldBe("received", "hop received");
        world.Windmill.Calls.ShouldAllBe(c => typeof(IWindmillApi).GetMethods().Any(m => m.Name == c), "only Windmill API calls");
        Directory.GetFiles(world.StateDir).Select(Path.GetFileName)
            .ShouldAllBe(f => f == "ledger.db" || f == "ledger.db-wal" || f == "ledger.db-shm", "no Windows-side file touched");
        world.Notification(world.Outages().Single(o => o.Kind == "windows-hop-failed").FailureNid!).State.ShouldBe("received", "decisive");
    }

    // G-79 D-6/D-7: Nightly failure intent persists before notification enqueue.
    [Test]
    public async Task C544_NotificationIntent()
    {
        await using (var world = await HopWorld())
        {
            await world.TickAsync();
            world.Transport.IntentVisibleAtSend.ShouldBe(true, "intent visible from a second connection at send time");
        }

        await using var crash = await HopWorld(w => w.CrashAt = CrashPoint.Intent);
        await Should.ThrowAsync<WatchdogCrashException>(() => crash.TickAsync());
        crash.Chat.Messages.ShouldBeEmpty("crash-after-intent nothing sent");
        var nid = crash.Notifications().Single().Nid;
        crash.Notifications().Single().State.ShouldBe("pending", "crash-after-intent pending");
        await crash.RestartAsync();
        var resumed = await crash.TickAsync();
        resumed.Sends[0].Nid.ShouldBe(nid, "crash-after-intent same nid sent");
        crash.Chat.Messages.Count.ShouldBe(1, "crash-after-intent delivered");
        await crash.TickAsync();
        crash.Notification(nid).State.ShouldBe("received", "crash-after-intent received");
        crash.Notifications().Count.ShouldBe(1, "crash-after-intent one logical notification");
    }

    // G-80 D-6: Enqueue failure stays retryable with the original identity.
    [Test]
    public async Task C544_NotificationRetry()
    {
        await using var world = await HopWorld(w => w.Transport.Mode = TransportMode.Throw);
        await world.TickAsync();
        var nid = HopNid(world);
        var first = world.Attempts(nid).Single();
        (first.Accepted, first.ErrorClass).ShouldBe((false, "transport"), "attempt 1 failed");
        world.Notification(nid).State.ShouldBe("pending", "pending");
        world.Chat.Messages.ShouldBeEmpty("nothing delivered");

        world.Transport.Mode = TransportMode.Deliver;
        var tick2 = await world.AdvanceAndTickAsync(TimeSpan.FromMinutes(2));
        (tick2.Sends[0].Nid, tick2.Sends[0].Attempt).ShouldBe((nid, 2), "attempt 2 same nid");
        world.Attempts(nid).Count.ShouldBe(2, "decisive: two attempts at +2 min");
        world.Notifications().Count.ShouldBe(1, "one logical notification");
        world.Chat.Messages.Single().Text.ShouldContain(" attempt=2 ", Case.Sensitive, "marker attempt 2");

        await world.TickAsync();
        world.Notification(nid).State.ShouldBe("received", "received via attempt 2");
        world.Receipts().Single().Attempt.ShouldBe(2, "receipt names attempt 2");
    }

    // G-81 D-6: Transport/job acceptance is not recipient receipt.
    [Test]
    public async Task C544_RecipientEvidence()
    {
        await using var world = await HopWorld(w => w.Transport.Mode = TransportMode.AcceptWithoutDelivery);
        await world.TickAsync();
        var nid = HopNid(world);
        var attempt = world.Attempts(nid).Single();
        (attempt.Accepted, attempt.MessageId).ShouldBe((true, "1"), "tier-1 accepted");
        world.Notification(nid).State.ShouldBe("sent", "decisive: accepted is sent, not received");
        world.Receipts().ShouldBeEmpty("no receipt");
        var recent = (await world.SnapshotJsonAsync())["recentNotifications"]!.AsArray().Single(n => n!["nid"]!.GetValue<string>() == nid)!;
        recent["state"]!.GetValue<string>().ShouldBe("sent", "snapshot state");
        recent["receivedAt"].ShouldBeNull("snapshot receivedAt");

        var tick2 = await world.TickAsync();
        world.Notification(nid).State.ShouldBe("sent", "still sent with an eligible empty view");
        tick2.Imports.ShouldBeEmpty("nothing imported");
    }

    // G-82 D-6: Receipt matches notification identity.
    [Test]
    public async Task C544_ReceiptNotificationIdentity()
    {
        await using var a = await HopWorld();
        await a.TickAsync();
        await a.TickAsync();
        var n1 = HopNid(a);
        a.Notification(n1).State.ShouldBe("received", "world A received N1");

        await using var b = await C545World.CreateAsync(sharedChat: a.Chat);
        b.Windmill.Jobs.Add(b.Jobs.HopFailed(C545Due, "j-hop"));
        b.Transport.Mode = TransportMode.Throw;
        await b.TickAsync();
        var n2 = HopNid(b);
        b.Outages().Single(o => o.Kind == "windows-hop-failed").OutageId.ShouldBe(a.Outages().Single(o => o.Kind == "windows-hop-failed").OutageId, "same outage identity");
        n2.ShouldNotBe(n1, "different nid");
        var tick = await b.TickAsync();
        tick.Imports.Single(i => i.Nid == n1).Reason.ShouldBe("unknown-notification", "decisive");
        b.Notification(n2).State.ShouldBe("pending", "N2 still pending");
        b.Receipts().ShouldBeEmpty("no receipt in B");

        await using var attemptRow = await HopWorld(w => w.Reader.Transform = t => t.Replace(" attempt=1 ", " attempt=2 "));
        await attemptRow.TickAsync();
        (await attemptRow.TickAsync()).Imports[0].Reason.ShouldBe("unknown-attempt", "attempt mismatch");
    }

    // G-83 D-6: Receipt matches run identity.
    [Test]
    public async Task C544_ReceiptRunIdentity()
    {
        async Task<(C545World World, TickReport Tick2)> Row(Func<string, string>? transform)
        {
            var world = await C545World.CreateAsync();
            world.Windmill.Jobs.Add(world.Jobs.Success(C545Due, "r18", reportDelivered: false));
            world.Reader.Transform = transform;
            await world.TickAsync();
            world.Attempts(world.Notifications().Single().Nid)[0].Body.ShouldContain("run r18", Case.Sensitive, "body names the run");
            return (world, await world.TickAsync());
        }

        var oid = $"oid=nw:mc/test:{C545Due}:report-undelivered:1";
        var (runWorld, runTick) = await Row(t => t.Replace("run r18", "run r99"));
        await using (runWorld)
        {
            runTick.Imports[0].Reason.ShouldBe("run-mismatch", "decisive run row");
            runWorld.Notifications().Single().State.ShouldBe("sent", "run row not received");
        }
        var (oidWorld, oidTick) = await Row(t => t.Replace(oid, oid[..^1] + "2"));
        await using (oidWorld) oidTick.Imports[0].Reason.ShouldBe("identity-mismatch", "oid row");
        var (dueWorld, dueTick) = await Row(t => t.Replace($" due={C545Due} ", " due=2026-09-19 "));
        await using (dueWorld) dueTick.Imports[0].Reason.ShouldBe("identity-mismatch", "due row");
        var (control, controlTick) = await Row(null);
        await using (control) controlTick.Imports[0].Reason.ShouldBe("imported", "control");
    }

    // G-84 D-6: Crash after accepted enqueue or recipient observation recovers without duplicate notification.
    [Test]
    public async Task C544_NotificationCrash()
    {
        foreach (var point in new[] { CrashPoint.Intent, CrashPoint.SendBeforeResponse, CrashPoint.TransportAccepted, CrashPoint.ReaderObservation })
        {
            await using var world = await HopWorld(w => w.CrashAt = point);
            // The observation cut is reached on the first tick that reads a delivered message (tick 2).
            var threw = false;
            for (var i = 0; i < 2 && !threw; i++)
            {
                try { await world.TickAsync(); }
                catch (WatchdogCrashException ex) { ex.Point.ShouldBe(point, $"{point} crash point"); threw = true; }
            }
            threw.ShouldBeTrue($"{point} crashed");
            await world.RestartAsync();
            for (var i = 0; i < 2 && world.Notifications().Single().State != "received"; i++)
                await world.AdvanceAndTickAsync(TimeSpan.FromMinutes(2));
            var nid = world.Notifications().Single().Nid;
            world.Notification(nid).State.ShouldBe("received", $"{point} received");
            world.Notifications().Count.ShouldBe(1, $"{point} one logical notification");
            world.Receipts().Count.ShouldBe(1, $"{point} one receipt");
            world.Chat.Messages.Count.ShouldBe(1, $"{point} one message");
            world.Attempts(nid).Count.ShouldBe(1, $"{point} one attempt");
        }
    }

    // G-85 D-6: Recovery notification is produced and received after an outage clears.
    [Test]
    public async Task C544_RecoveryNotification()
    {
        await using var world = await HopWorld();
        await world.TickAsync();
        await world.TickAsync();
        var failureNid = HopNid(world);
        world.Notification(failureNid).State.ShouldBe("received", "failure received");
        var failureReceipt = world.Receipts().Single();

        world.Windmill.Jobs.Add(world.Jobs.Success(C545Due, "r18", id: "j-ok", createdOffset: TimeSpan.FromMinutes(30)));
        var close = await world.AdvanceAndTickAsync(TimeSpan.FromMinutes(1));
        close.Transitions.ShouldContain(t => t.Change == OutageChange.Closed && t.Kind == "windows-hop-failed", "closed");
        var recovery = world.Notifications().Single(n => n.Kind == "recovery");
        recovery.LinkedNid.ShouldBe(failureNid, "linked");
        recovery.OutageId.ShouldBe(world.Notification(failureNid).OutageId, "same outage");
        recovery.Nid.ShouldNotBe(failureNid, "own nid");
        world.Chat.Messages.Count.ShouldBe(2, "recovery delivered");
        world.Chat.Messages[1].Text.ShouldStartWith("Antiphon nightly watchdog: RECOVERED windows-hop-failed", Case.Sensitive, "recovery head");
        world.Chat.Messages[1].Text.ShouldContain("failureReceived=true", Case.Sensitive, "failure was received");

        await world.TickAsync();
        world.Notifications().Single(n => n.Kind == "recovery").State.ShouldBe("received", "decisive");
        world.Receipts().Single(r => r.Nid == failureNid).ShouldBe(failureReceipt, "failure receipt unchanged");
    }

    // G-86 D-6: Unauthorized/missing destination cannot be silently replaced.
    [Test]
    public async Task C544_AuthorizedDestination()
    {
        foreach (var (row, chat) in new[] { ("unset", (string?)null), ("non-numeric", "abc") })
        {
            await using var world = await HopWorld(w => w.Options.DestinationChatId = chat);
            var handler = world.UseTelegramTransport();
            var tick = await world.TickAsync();
            tick.Transitions.ShouldContain(t => t.Change == OutageChange.Opened, row + " outage opened");
            tick.Sends[0].ErrorClass.ShouldBe("destination-unauthorized", row);
            handler.Requests.Count.ShouldBe(0, row + " decisive: no request");
            var attempt = world.Attempts(HopNid(world)).Single();
            attempt.Accepted.ShouldBeFalse(row + " not accepted");
            world.Notification(HopNid(world)).State.ShouldBe("pending", row + " pending");
            (await world.SnapshotJsonAsync())["destination"]!["qualified"]!.GetValue<bool>().ShouldBeFalse(row + " snapshot unqualified");
        }

        await using (var mismatch = await C545World.CreateAsync())
        {
            mismatch.Windmill.Jobs.Add(mismatch.Jobs.Running(C545World.Start.UtcDateTime.AddMinutes(-60)));
            await mismatch.TickAsync();
            mismatch.Heartbeat().DestinationHash.ShouldBe(WatchdogOptions.DestinationHash(C545World.ChatId), "qualified destination stored");
            mismatch.Options.DestinationChatId = "987654321";
            await mismatch.RestartAsync();
            var handler = mismatch.UseTelegramTransport();
            mismatch.Windmill.Jobs.Add(mismatch.Jobs.HopFailed(C545Due, "j-hop"));
            var tick = await mismatch.TickAsync();
            var send = tick.Sends.Single();
            send.ErrorClass.ShouldBe("destination-unauthorized", "mismatch");
            mismatch.Ledger.Attempts().Single().ErrorClass.ShouldBe("destination-unauthorized", "mismatch attempt");
            handler.Requests.Count.ShouldBe(0, "mismatch no request");
        }

        await using var control = await HopWorld();
        var ok = control.UseTelegramTransport(new ScriptedResponse(200, """{"ok":true,"result":{"message_id":5}}"""));
        await control.TickAsync();
        ok.Requests.Count.ShouldBe(1, "control one request");
        JsonNode.Parse(ok.Requests[0].BodyJson!)!["chat_id"]!.GetValue<string>().ShouldBe(C545World.ChatId, "control chat id");
    }

    // G-95 D-6: Recipient evidence must come from the authorized destination readback.
    [Test]
    public async Task C544_ReceiptDestination()
    {
        await using var world = await HopWorld(w => w.Reader.PeerOverride = "peer-other");
        await world.TickAsync();
        var tick2 = await world.TickAsync();
        tick2.Imports[0].Reason.ShouldBe("peer-unauthorized", "decisive");
        world.Notification(HopNid(world)).State.ShouldBe("sent", "not received");
        world.Receipts().ShouldBeEmpty("no receipt");
        world.Reader.PeerOverride = null;
        (await world.TickAsync()).Imports[0].Reason.ShouldBe("imported", "authorized peer imports");
    }

    // G-96 D-6: Recipient readback must contain the whole produced payload.
    [Test]
    public async Task C544_ReceiptWholeBody()
    {
        foreach (var (row, transform, expected) in new (string, Func<string, string>?, string?)[]
                 {
                     ("marker-only", t => t[(t.LastIndexOf('\n') + 1)..], "body-mismatch"),
                     ("truncated", t => t[..^10], null),
                     ("header-edited", t => { var lines = t.Split('\n'); lines[1] = lines[1].Replace("workspace mc", "workspace mx"); return string.Join('\n', lines); }, "body-mismatch"),
                     ("control", null, "imported"),
                 })
        {
            await using var world = await HopWorld(w => w.Reader.Transform = transform);
            await world.TickAsync();
            var outcome = (await world.TickAsync()).Imports[0];
            if (expected is null)
            {
                outcome.Imported.ShouldBeFalse(row);
                outcome.Reason.ShouldNotBe("imported", row);
            }
            else
            {
                outcome.Reason.ShouldBe(expected, row);
            }
        }
    }

    // G-97 D-6: Recipient evidence predating the current notification attempt cannot confirm it.
    [Test]
    public async Task C544_ReceiptAttemptFloor()
    {
        foreach (var (seconds, expected) in new[] { (-121, "predates-attempt"), (-120, "imported") })
        {
            await using var world = await HopWorld(w => w.Reader.DateOffset = TimeSpan.FromSeconds(seconds));
            await world.TickAsync();
            (await world.TickAsync()).Imports[0].Reason.ShouldBe(expected, $"offset {seconds} s");
        }
    }

    // G-98 D-6: Independent outage recovery state must survive Windows being inaccessible.
    [Test]
    public async Task C544_IndependentState()
    {
        await using var world = await HopWorld(w =>
        {
            w.Windmill.WorkerLastPingUtc = C545World.Start.UtcDateTime.AddMinutes(-61);
            w.Transport.Mode = TransportMode.Throw;
        });
        await world.TickAsync();
        var hopNid = HopNid(world);
        world.Chat.Messages.ShouldBeEmpty("nothing delivered");
        await world.RestartAsync(ledgerOnly: true);
        // Only ledger.db moved; the reopened ledger adds its own WAL/SHM files and nothing else.
        Directory.GetFiles(world.StateDir).Select(Path.GetFileName)
            .ShouldAllBe(f => f == "ledger.db" || f == "ledger.db-wal" || f == "ledger.db-shm", "only the ledger survived");
        world.Transport.Mode = TransportMode.Deliver;
        var tick = await world.AdvanceAndTickAsync(TimeSpan.FromMinutes(2));
        tick.Sends.Select(s => s.Nid).ShouldContain(hopNid, "decisive: original nid resumed");
        world.Chat.Messages.Select(m => m.Text).ShouldContain(t => t.Contains("nid=" + hopNid), "delivered");
        await world.TickAsync();
        world.Notification(hopNid).State.ShouldBe("received", "received");
        world.Notifications().Count(n => n.OutageId == world.Notification(hopNid).OutageId).ShouldBe(1, "one logical hop notification");
    }

    // V-545-10 / G-545-5: reader-first retry never issues a second logical notification.
    [Test]
    public async Task C545_ReaderFirstRetry()
    {
        await using (var world = await HopWorld(w => w.Transport.Mode = TransportMode.LoseResponse))
        {
            await world.TickAsync();
            var nid = HopNid(world);
            world.Attempts(nid).Single().Accepted.ShouldBeFalse("lost-response/reader-sees-it attempt 1 unaccepted");
            world.Chat.Messages.Count.ShouldBe(1, "lost-response/reader-sees-it message reached the chat");
            var tick2 = await world.AdvanceAndTickAsync(TimeSpan.FromMinutes(2));
            tick2.Imports.ShouldContain(i => i.Nid == nid && i.Attempt == 1 && i.Reason == "imported", "lost-response/reader-sees-it imported");
            tick2.Sends.ShouldBeEmpty("lost-response/reader-sees-it no resend");
            world.Attempts(nid).Count.ShouldBe(1, "lost-response/reader-sees-it one attempt");
            world.Chat.Messages.Count.ShouldBe(1, "lost-response/reader-sees-it one message");
            world.Notification(nid).State.ShouldBe("received", "lost-response/reader-sees-it received");
        }

        await using (var world = await HopWorld(w => w.Transport.Mode = TransportMode.Throw))
        {
            await world.TickAsync();
            var nid = HopNid(world);
            (await world.AdvanceAndTickAsync(new TimeSpan(0, 1, 59))).Sends.ShouldBeEmpty("lost-response/reader-empty backoff not reached");
            world.Transport.Mode = TransportMode.Deliver;
            var send = (await world.AdvanceAndTickAsync(TimeSpan.FromSeconds(1))).Sends.Single();
            (send.Nid, send.Attempt).ShouldBe((nid, 2), "lost-response/reader-empty attempt 2 same nid");
            world.Chat.Messages.Single().Text.ShouldContain(" attempt=2 ", Case.Sensitive, "lost-response/reader-empty marker");
            world.Notifications().Count.ShouldBe(1, "lost-response/reader-empty one notification");
            (await world.TickAsync()).Imports.ShouldContain(i => i.Nid == nid && i.Attempt == 2 && i.Imported, "lost-response/reader-empty imports attempt 2");
        }

        await using (var world = await HopWorld(w => w.Transport.Mode = TransportMode.Throw))
        {
            await world.TickAsync();
            for (var minute = 1; minute <= 170; minute++)
                await world.AdvanceAndTickAsync(TimeSpan.FromMinutes(1));
            var nid = HopNid(world);
            world.Transport.Sends.Where(s => s.Message.Nid == nid)
                .Select(s => (int)(s.At - C545World.Start.UtcDateTime).TotalMinutes)
                .ShouldBe([0, 2, 7, 17, 47, 107, 167], "backoff-schedule");
        }

        await using (var world = await HopWorld(w => w.Transport.Reject("rate-limited", 600)))
        {
            await world.TickAsync();
            var nid = HopNid(world);
            for (var minute = 1; minute <= 12; minute++)
                await world.AdvanceAndTickAsync(TimeSpan.FromMinutes(1));
            world.Transport.Sends.Where(s => s.Message.Nid == nid).Select(s => (int)(s.At - C545World.Start.UtcDateTime).TotalMinutes)
                .ShouldBe([0, 10], "retry-after");
        }

        await using (var world = await HopWorld(w => w.Transport.Mode = TransportMode.AcceptWithoutDelivery))
        {
            await world.TickAsync();
            var nid = HopNid(world);
            world.Notification(nid).State.ShouldBe("sent", "accepted-unreceived/grace sent");
            (await world.AdvanceAndTickAsync(new TimeSpan(0, 29, 59))).Sends.ShouldBeEmpty("accepted-unreceived/grace 29:59");
            var tick = await world.AdvanceAndTickAsync(TimeSpan.FromSeconds(1));
            (tick.Sends.Single().Nid, tick.Sends.Single().Attempt).ShouldBe((nid, 2), "accepted-unreceived/grace 30:00 resend");
            tick.Sends.Single().Note.ShouldBe("accepted-but-unreceived", "accepted-unreceived/grace note");
            world.Attempts(nid)[0].ErrorClass.ShouldBeNull("accepted-unreceived/grace attempt 1 unchanged");
        }
    }

    // V-545-25 / G-545-30: a held reader never triggers a resend before the hold expiry.
    [Test]
    public async Task C545_HeldReaderNoResend()
    {
        await using (var world = await HopWorld(w => w.Reader.Hold("session-locked")))
        {
            var tick1 = await world.TickAsync();
            tick1.ReaderState.ShouldBe(ReaderState.Held, "held tick 1");
            var nid = HopNid(world);
            world.Attempts(nid).Single().Accepted.ShouldBeTrue("held attempt 1 sent");
            var tick2 = await world.AdvanceAndTickAsync(new TimeSpan(0, 29, 59));
            world.Attempts(nid).Count.ShouldBe(1, "held no resend at 29:59");
            tick2.ReaderState.ShouldBe(ReaderState.Held, "held state");

            world.Reader.Release();
            var tick3 = await world.TickAsync();
            tick3.Imports.ShouldContain(i => i.Nid == nid && i.Attempt == 1 && i.Imported, "held-then-eligible imported");
            world.Receipts().Count.ShouldBe(1, "held-then-eligible one receipt");
            world.Chat.Messages.Count.ShouldBe(1, "held-then-eligible one message");
        }

        await using (var world = await HopWorld(w => { w.Transport.Mode = TransportMode.AcceptWithoutDelivery; w.Reader.Hold("session-locked"); }))
        {
            await world.TickAsync();
            var nid = HopNid(world);
            await world.AdvanceAndTickAsync(TimeSpan.FromMinutes(30));
            world.Attempts(nid).Count.ShouldBe(2, "hold-expiry resends at 30:00");
        }

        await using (var world = await HopWorld(w => { w.Transport.Mode = TransportMode.LoseResponse; w.Reader.Hold("session-locked"); }))
        {
            await world.TickAsync();
            var nid = HopNid(world);
            await world.AdvanceAndTickAsync(new TimeSpan(0, 29, 59));
            world.Attempts(nid).Count.ShouldBe(1, "held-lost-response decisive: no resend while held at 29:59");
            world.Reader.Release();
            (await world.TickAsync()).Imports.ShouldContain(i => i.Nid == nid && i.Imported, "held-lost-response imported");
            world.Chat.Messages.Count.ShouldBe(1, "held-lost-response one message");
        }
    }

    // V-545-26 / G-545-7: recovery is sent even when the failure was never received, and says so.
    [Test]
    public async Task C545_RecoveryLinksFailure()
    {
        await using var world = await HopWorld(w => w.Transport.Mode = TransportMode.AcceptWithoutDelivery);
        await world.TickAsync();
        var failureNid = HopNid(world);
        world.Notification(failureNid).State.ShouldBe("sent", "failure sent never received");

        world.Windmill.Jobs.Add(world.Jobs.Success(C545Due, "r18", id: "j-ok", createdOffset: TimeSpan.FromMinutes(30)));
        world.Transport.Mode = TransportMode.Deliver;
        var close = await world.AdvanceAndTickAsync(TimeSpan.FromMinutes(1));
        close.Transitions.ShouldContain(t => t.Change == OutageChange.Closed, "closed");
        var recovery = world.Notifications().Single(n => n.Kind == "recovery");
        recovery.LinkedNid.ShouldBe(failureNid, "linked");
        recovery.OutageId.ShouldBe(world.Notification(failureNid).OutageId, "same outage");
        close.Sends.ShouldContain(s => s.Nid == recovery.Nid && s.Accepted, "recovery delivered");
        world.Chat.Messages.Single().Text.ShouldContain($"kind=recovery attempt=1 due={C545Due} link={failureNid} failureReceived=false", Case.Sensitive, "marker");

        await world.TickAsync();
        world.Notification(recovery.Nid).State.ShouldBe("received", "recovery received");
        world.Notification(failureNid).State.ShouldBe("sent", "failure still sent");
        world.Receipts().Count.ShouldBe(1, "one receipt");
    }

    // V-545-27 / G-545-34: the qualification notice is ledger-backed through the same transport/reader.
    [Test]
    public async Task C545_QualificationNoticePath()
    {
        await using var world = await C545World.CreateAsync();
        world.Windmill.Jobs.Add(world.Jobs.Running(C545World.Start.UtcDateTime.AddMinutes(-60)));
        var notice = await world.Loop.SendQualificationNoticeAsync();
        world.Notifications().Count.ShouldBe(1, "one notification");
        var row = world.Notifications().Single();
        (row.Kind, row.OutageId).ShouldBe(("qualification", (string?)null), "qualification without outage");
        world.Attempts(notice.Nid).Single().Accepted.ShouldBeTrue("attempt 1 delivered");
        var text = world.Chat.Messages.Single().Text;
        text.ShouldContain($"nid={notice.Nid} oid=none kind=qualification", Case.Sensitive, "marker");

        var tick = await world.TickAsync();
        tick.Imports.ShouldContain(i => i.Nid == notice.Nid && i.Imported, "imported");
        world.Notification(notice.Nid).State.ShouldBe("received", "received");
        world.Outages().ShouldBeEmpty("no outage row");
        (await world.SnapshotJsonAsync())["recentNotifications"]!.AsArray()[0]!["kind"]!.GetValue<string>().ShouldBe("qualification", "snapshot");
    }

    // V-545-11 / G-545-8: namespace mc refuses AllowFaultInjection/CrashAfter.
    [Test]
    public async Task C545_ProductionRefusesFaultInjection()
    {
        var production = new WatchdogOptions { Namespace = "mc", SnapshotBind = "127.0.0.1:17290", AllowFaultInjection = true };
        production.Validate().ShouldContain("fault-injection-forbidden", "mc allow");
        WatchdogLoop.CreateFaultHook(production, p => throw new WatchdogCrashException(p)).ShouldBeNull("mc allow no hook");
        new WatchdogOptions { Namespace = "mc", SnapshotBind = "127.0.0.1:17290", CrashAfter = "intent" }.Validate()
            .ShouldContain("fault-injection-forbidden", "mc crash-after");

        await using (var qual = await C545World.CreateAsync(o => { o.Namespace = "mc/qual"; o.AllowFaultInjection = true; o.CrashAfter = "intent"; }))
        {
            qual.Options.Validate().ShouldBeEmpty("qualification allows faults");
            var hook = WatchdogLoop.CreateFaultHook(qual.Options, p => throw new WatchdogCrashException(p));
            hook.ShouldNotBeNull("qualification hook");
            var loop = new WatchdogLoop(qual.Options, qual.Ledger, qual.Windmill, qual.Transport, qual.Reader, qual.Clock, hook);
            qual.Windmill.Jobs.Add(qual.Jobs.HopFailed(C545Due, "j-hop"));
            (await Should.ThrowAsync<WatchdogCrashException>(() => loop.TickAsync())).Point.ShouldBe(CrashPoint.Intent, "crash at intent");
        }

        await using var mc = await C545World.CreateAsync(o => o.Namespace = "mc");
        var noHook = WatchdogLoop.CreateFaultHook(mc.Options, p => throw new WatchdogCrashException(p));
        noHook.ShouldBeNull("production has no hook");
        var productionLoop = new WatchdogLoop(mc.Options, mc.Ledger, mc.Windmill, mc.Transport, mc.Reader, mc.Clock, noHook);
        mc.Windmill.Jobs.Add(mc.Jobs.HopFailed(C545Due, "j-hop"));
        (await productionLoop.TickAsync()).Transitions.ShouldContain(t => t.Change == OutageChange.Opened, "production tick opens an outage");
    }

    // V-545-12 / G-545-14: tier-1 fields recorded and shown separately; the read marker never gates received.
    [Test]
    public async Task C545_AcceptanceRecordedSeparately()
    {
        await using (var world = await HopWorld(w => w.Transport.Mode = TransportMode.AcceptWithoutDelivery))
        {
            await world.TickAsync();
            var nid = HopNid(world);
            var attempt = world.Attempts(nid).Single();
            (attempt.Accepted, attempt.AcceptedAt, attempt.MessageId).ShouldBe((true, (DateTime?)world.Clock.GetUtcNow().UtcDateTime, "1"), "accepted-shown attempt");
            var recent = (await world.SnapshotJsonAsync())["recentNotifications"]!.AsArray().Single(n => n!["nid"]!.GetValue<string>() == nid)!;
            recent["state"]!.GetValue<string>().ShouldBe("sent", "accepted-shown state");
            recent["acceptedAt"].ShouldNotBeNull("accepted-shown acceptedAt");
            recent["messageId"]!.GetValue<string>().ShouldBe("1", "accepted-shown messageId");
            recent["receivedAt"].ShouldBeNull("accepted-shown receivedAt");
        }

        await using (var world = await HopWorld())
        {
            await world.TickAsync();
            await world.TickAsync();
            var nid = HopNid(world);
            world.Notification(nid).State.ShouldBe("received", "received-then-read received");
            world.Receipts().Single().ReadObservedAt.ShouldBeNull("received-then-read not yet read");
            world.Reader.ReadByRecipient = true;
            await world.AdvanceAndTickAsync(TimeSpan.FromMinutes(1));
            world.Receipts().Single().ReadObservedAt.ShouldNotBeNull("received-then-read read observed");
            world.Notification(nid).State.ShouldBe("received", "received-then-read still received");
            world.Receipts().Count.ShouldBe(1, "received-then-read one receipt");
        }

        await using (var world = await HopWorld(w => { w.Reader.Transform = t => t[(t.LastIndexOf('\n') + 1)..]; w.Reader.ReadByRecipient = true; }))
        {
            await world.TickAsync();
            var tick2 = await world.TickAsync();
            tick2.Imports[0].Reason.ShouldBe("body-mismatch", "read-marker-without-body decisive");
            world.Notification(HopNid(world)).State.ShouldBe("sent", "read-marker-without-body sent");
            world.Receipts().ShouldBeEmpty("read-marker-without-body no receipt");
        }
    }

    // V-545-24 / G-545-1: delivery and readback proceed while Windmill is unreachable; the unit has no Windmill/Docker dependency.
    [Test]
    public async Task C545_NoWindmillDependency()
    {
        await using var world = await HopWorld(w => w.Transport.Mode = TransportMode.Throw);
        await world.TickAsync();
        var nid = HopNid(world);
        world.Attempts(nid).Single().Accepted.ShouldBeFalse("attempt 1 failed");
        world.Windmill.ThrowOnEverything = true;
        world.Transport.Mode = TransportMode.Deliver;
        var tick2 = await world.AdvanceAndTickAsync(TimeSpan.FromMinutes(2));
        tick2.Sends.Single(s => s.Nid == nid).Accepted.ShouldBeTrue("sent while Windmill is down");
        world.Chat.Messages.Count.ShouldBe(1, "delivered while Windmill is down");
        var tick3 = await world.TickAsync();
        tick3.Imports.ShouldContain(i => i.Nid == nid && i.Imported, "imported while Windmill is down");
        world.Notification(nid).State.ShouldBe("received", "received");

        var template = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "nightly-watchdog", "antiphon-nightly-watchdog.service.template"));
        foreach (var forbidden in new[] { "docker", "windmill", "Requires=", "BindsTo=", "After=docker" })
            template.ShouldNotContain(forbidden, Case.Insensitive, $"unit template declares no {forbidden}");
    }

    // ---- PowerShell harness cases (CARD-0545 S1, S4, S5) ----

    [Test]
    public Task C545_ResultLine() => RunHarnessCaseAsync("test-nightly-run.ps1", "C545", "C545_ResultLine", 16,
        "C545 ResultLine refusal last line is the JSON record",
        "C545 ResultLine dst-summer localDueDate 2026-07-01",
        "C545 ResultLine green flags equal last-run.json",
        "C545 ResultLine no-report reportDelivered false and no green file");

    [Test]
    public Task C545_JobResultFetch() => RunHarnessCaseAsync("test-nightly-health.ps1", "C545", "C545_JobResultFetch", 13,
        "C545 JobResultFetch c6 stays unknown beyond the cap",
        "C545 JobResultFetch c3 unfetchable is unknown",
        "C545 JobResultFetch no production Windmill notification sink",
        "C545 JobResultFetch requests are list then c1..c5 in order");

    [Test]
    public Task C545_WatchdogFresh() => RunHarnessCaseAsync("test-nightly-health.ps1", "C545", "C545_WatchdogFresh", 8,
        "C545 WatchdogFresh age 20:00 fresh",
        "C545 WatchdogFresh real HTTP snapshot read",
        "C545 WatchdogFresh evaluator healthy with a fresh watchdog",
        "C545 WatchdogFresh readiness-config supplies every value");

    [Test]
    public Task C545_WatchdogStale() => RunHarnessCaseAsync("test-nightly-health.ps1", "C545", "C545_WatchdogStale", 13,
        "C545 WatchdogStale age 20:01 stale",
        "C545 WatchdogStale unreachable",
        "C545 WatchdogStale malformed not-json",
        "C545 WatchdogStale malformed missing-heartbeat",
        "C545 WatchdogStale malformed schema-2",
        "C545 WatchdogStale malformed empty-instance",
        "C545 WatchdogStale namespace mc/qual mismatch",
        "C545 WatchdogStale instance wd-2 mismatch",
        "C545 WatchdogStale open outage unhealthy");

    [Test]
    public Task C545_ReadinessRouting() => RunHarnessCaseAsync("test-nightly-health.ps1", "C545", "C545_ReadinessRouting", 6,
        "C545 ReadinessRouting readiness definition is desktop bash",
        "C545 ReadinessRouting retired health definition is absent",
        "C545 ReadinessRouting G116 monitor routing still rejects desktop");

    [Test]
    public Task C545_DeployRefusesWithoutProfile() => RunHarnessCaseAsync("test-deploy-nightly-watchdog.ps1", "C545", "C545_DeployRefusesWithoutProfile", 7,
        "C545 DeployRefusesWithoutProfile no-profile/no-arg exit 3 no runner call",
        "C545 DeployRefusesWithoutProfile placeholder/sshTarget exit 3",
        "C545 DeployRefusesWithoutProfile control valid profile preflight exit 0");

    [Test]
    public Task C545_DeployRendersFromProfile() => RunHarnessCaseAsync("test-deploy-nightly-watchdog.ps1", "C545", "C545_DeployRendersFromProfile", 9,
        "C545 DeployRendersFromProfile preflight-no-upload",
        "C545 DeployRendersFromProfile env fragment has exactly the absent non-secret keys",
        "C545 DeployRendersFromProfile unit placeholders fully substituted",
        "C545 DeployRendersFromProfile deploy-verifies-snapshot");

    [Test]
    public Task C545_HostAgnosticAssets() => RunHarnessCaseAsync("test-deploy-nightly-watchdog.ps1", "C545", "C545_HostAgnosticAssets", 3,
        "C545 HostAgnosticAssets no ssh-target token",
        "C545 HostAgnosticAssets no IPv4 literal",
        "C545 HostAgnosticAssets no absolute home path");
}
