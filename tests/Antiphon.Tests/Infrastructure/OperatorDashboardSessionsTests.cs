using System.Security.Cryptography;
using Antiphon.Server.Infrastructure.Security;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>CARD-0658: the dashboard login store - single-use nonces and bounded sessions.</summary>
[Category("Unit")]
public class OperatorDashboardSessionsTests
{
    [Test]
    public async Task Nonce_redeems_exactly_once()
    {
        var store = new OperatorDashboardSessions(new FakeTimeProvider());
        var nonce = store.IssueNonce();

        var session = store.Redeem(nonce);

        session.ShouldNotBeNullOrEmpty();
        session.ShouldNotBe(nonce);
        store.IsLive(session).ShouldBeTrue();
        store.Redeem(nonce).ShouldBeNull();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Nonce_expires_after_two_minutes()
    {
        var clock = new FakeTimeProvider();
        var store = new OperatorDashboardSessions(clock);
        var fresh = store.IssueNonce();
        var stale = store.IssueNonce();

        clock.Advance(TimeSpan.FromSeconds(119));
        store.Redeem(fresh).ShouldNotBeNull();
        clock.Advance(TimeSpan.FromSeconds(2));
        store.Redeem(stale).ShouldBeNull();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Session_is_live_for_twelve_hours_then_not()
    {
        var clock = new FakeTimeProvider();
        var store = new OperatorDashboardSessions(clock);
        var session = store.Redeem(store.IssueNonce());

        clock.Advance(TimeSpan.FromHours(11) + TimeSpan.FromMinutes(59));
        store.IsLive(session).ShouldBeTrue();
        clock.Advance(TimeSpan.FromMinutes(2));
        store.IsLive(session).ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Unknown_empty_or_null_values_are_never_live()
    {
        var store = new OperatorDashboardSessions(new FakeTimeProvider());
        var nonce = store.IssueNonce();

        store.IsLive(null).ShouldBeFalse();
        store.IsLive("").ShouldBeFalse();
        store.IsLive(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant()).ShouldBeFalse();
        // A nonce is not a session; an unredeemed link grants nothing by itself.
        store.IsLive(nonce).ShouldBeFalse();
        store.Redeem(null).ShouldBeNull();
        store.Redeem("").ShouldBeNull();
        await Task.CompletedTask;
    }
}
