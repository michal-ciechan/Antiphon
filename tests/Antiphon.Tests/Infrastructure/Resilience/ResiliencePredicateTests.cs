using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using Antiphon.Resilience;
using Antiphon.Server.Infrastructure.Resilience;
using Npgsql;
using Polly;
using Polly.Timeout;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure.Resilience;

[Category("Unit")]
[NotInParallel("antiphon-resilience-meter")]
public class ResiliencePredicateTests
{
    private static readonly string[] ApprovedSql =
    [
        "08000", "08001", "08003", "08006", "57P01", "57P02", "57P03", "53300", "40001", "40P01",
    ];

    private static readonly string[] ProviderTransientOutsideAllowlist = ["53400", "55P03", "08004", "08007", "58000"];

    [Test]
    public async Task Approved_sqlstates_retry_and_other_provider_transient_states_do_not()
    {
        var settings = new ResilienceSettings();
        foreach (var state in ApprovedSql)
        {
            var exception = Postgres(state);
            NpgsqlTransientFailureClassifier.IsRetryable(exception, settings, CancellationToken.None).ShouldBeTrue(state);
        }

        foreach (var state in ProviderTransientOutsideAllowlist)
        {
            var exception = Postgres(state);
            exception.IsTransient.ShouldBeTrue(state);
            NpgsqlTransientFailureClassifier.IsRetryable(exception, settings, CancellationToken.None).ShouldBeFalse(state);
            NpgsqlTransientFailureClassifier.IsAvailabilityFailure(exception, settings, CancellationToken.None).ShouldBeFalse(state);
        }

        NpgsqlTransientFailureClassifier.IsRetryable(Postgres("40001"), settings, CancellationToken.None).ShouldBeTrue();
        NpgsqlTransientFailureClassifier.IsAvailabilityFailure(Postgres("40001"), settings, CancellationToken.None).ShouldBeFalse();
        NpgsqlTransientFailureClassifier.IsAvailabilityFailure(Postgres("40P01"), settings, CancellationToken.None).ShouldBeFalse();
        NpgsqlTransientFailureClassifier.IsAvailabilityFailure(Postgres("08006"), settings, CancellationToken.None).ShouldBeTrue();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Non_postgres_npgsql_retries_only_when_the_provider_says_transient()
    {
        var settings = new ResilienceSettings();
        NpgsqlTransientFailureClassifier.IsRetryable(new MarkedNpgsqlException(true), settings, CancellationToken.None)
            .ShouldBeTrue();
        NpgsqlTransientFailureClassifier.IsRetryable(new MarkedNpgsqlException(false), settings, CancellationToken.None)
            .ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Recognized_ef_wrapper_chain_retries_and_the_outer_type_alone_does_not()
    {
        var settings = new ResilienceSettings();
        var provider = Postgres("08006");
        var update = new DbUpdateWrapper(provider);
        NpgsqlTransientFailureClassifier.IsRetryable(
            new InvalidOperationException("An exception has been raised that is likely due to a transient failure.", provider),
            settings, CancellationToken.None).ShouldBeTrue();
        NpgsqlTransientFailureClassifier.IsRetryable(
            new InvalidOperationException("wrapped update", update),
            settings, CancellationToken.None).ShouldBeTrue();
        NpgsqlTransientFailureClassifier.IsRetryable(update, settings, CancellationToken.None).ShouldBeTrue();
        NpgsqlTransientFailureClassifier.IsRetryable(
            new InvalidOperationException("outer type alone"),
            settings, CancellationToken.None).ShouldBeFalse();
        NpgsqlTransientFailureClassifier.IsRetryable(
            new InvalidOperationException("deep", new Exception("middle", provider)),
            settings, CancellationToken.None).ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Permanent_constraints_and_unapproved_wrappers_do_not_retry()
    {
        var settings = new ResilienceSettings();
        foreach (var state in new[] { "23505", "23503", "23502", "42601", "57014", "42P01" })
        {
            NpgsqlTransientFailureClassifier.IsRetryable(Postgres(state), settings, CancellationToken.None).ShouldBeFalse(state);
        }

        var unique = Postgres("23505");
        NpgsqlTransientFailureClassifier.IsRetryable(
            new InvalidOperationException("not the ef shape", unique),
            settings, CancellationToken.None).ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Http_statuses_retry_only_the_five_allowlisted_codes()
    {
        var settings = new ResilienceSettings();
        foreach (var code in new[] { 408, 429, 502, 503, 504 })
            (await RetryStatus(settings, code)).ShouldBeTrue(code.ToString());
        foreach (var code in new[] { 500, 501, 400, 401, 403, 404, 409, 422 })
            (await RetryStatus(settings, code)).ShouldBeFalse(code.ToString());
    }

    [Test]
    public async Task Socket_allowlist_retries_only_the_named_errors()
    {
        var settings = new ResilienceSettings();
        foreach (var error in new[]
        {
            SocketError.ConnectionRefused, SocketError.ConnectionReset, SocketError.ConnectionAborted,
            SocketError.TimedOut, SocketError.NetworkDown, SocketError.NetworkUnreachable,
            SocketError.HostDown, SocketError.HostUnreachable, SocketError.TryAgain,
        })
        {
            (await RetrySocket(settings, error, wrapIo: false)).ShouldBeTrue(error.ToString());
            (await RetrySocket(settings, error, wrapIo: true)).ShouldBeTrue(error.ToString());
        }

        (await RetrySocket(settings, SocketError.HostNotFound, wrapIo: false)).ShouldBeFalse();
        (await RetrySocket(settings, SocketError.NoData, wrapIo: true)).ShouldBeFalse();
    }

    [Test]
    public async Task Tls_dns_and_unclassified_http_failures_do_not_retry()
    {
        var settings = new ResilienceSettings();
        var tls = new HttpRequestException("tls", new AuthenticationException("untrusted"));
        var bare = new HttpRequestException("no socket");
        var deep = new HttpRequestException("deep", new IOException("outer", new IOException("inner", new SocketException((int)SocketError.ConnectionReset))));
        (await Retry(settings, Outcome.FromException<HttpResponseMessage>(tls))).ShouldBeFalse();
        (await Retry(settings, Outcome.FromException<HttpResponseMessage>(bare))).ShouldBeFalse();
        (await Retry(settings, Outcome.FromException<HttpResponseMessage>(deep))).ShouldBeFalse();
        (await Retry(settings, Outcome.FromException<HttpResponseMessage>(new HttpRequestException("protocol")))).ShouldBeFalse();
    }

    [Test]
    public async Task Cancellation_and_timeout_rejection_do_not_retry()
    {
        var settings = new ResilienceSettings();
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        var status = Outcome.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        (await Retry(settings, status, canceled.Token)).ShouldBeFalse();
        (await Retry(settings, Outcome.FromException<HttpResponseMessage>(new OperationCanceledException()))).ShouldBeFalse();
        (await Retry(settings, Outcome.FromException<HttpResponseMessage>(new TaskCanceledException()))).ShouldBeFalse();
        (await Retry(settings, Outcome.FromException<HttpResponseMessage>(new TimeoutRejectedException("attempt")))).ShouldBeFalse();
        NpgsqlTransientFailureClassifier.IsRetryable(new TimeoutRejectedException("db"), settings, CancellationToken.None)
            .ShouldBeFalse();
        NpgsqlTransientFailureClassifier.IsRetryable(new TimeoutException("io"), settings, CancellationToken.None)
            .ShouldBeTrue();
        NpgsqlTransientFailureClassifier.IsRetryable(new TimeoutException("io"), settings, canceled.Token).ShouldBeFalse();
    }

    private static Task<bool> RetryStatus(ResilienceSettings settings, int status) =>
        Retry(settings, Outcome.FromResult(new HttpResponseMessage((HttpStatusCode)status)));

    private static Task<bool> RetrySocket(ResilienceSettings settings, SocketError error, bool wrapIo)
    {
        Exception inner = new SocketException((int)error);
        if (wrapIo)
            inner = new IOException("transport", inner);
        return Retry(settings, Outcome.FromException<HttpResponseMessage>(new HttpRequestException("transport", inner)));
    }

    private static Task<bool> Retry(
        ResilienceSettings settings, Outcome<HttpResponseMessage> outcome, CancellationToken cancellation = default)
    {
        var decision = HttpTransientFailureClassifier.IsRetryable(
            outcome,
            settings,
            ResilienceDependencies.GitHub,
            ResilienceOperations.GitHubUser,
            HttpMethod.Get,
            budget: null,
            TimeProvider.System,
            cancellation);
        return Task.FromResult(decision);
    }

    private static PostgresException Postgres(string state) =>
        new("marker", "ERROR", "ERROR", state);

    private sealed class MarkedNpgsqlException(bool transient) : NpgsqlException("marker")
    {
        public override bool IsTransient => transient;
    }

    private sealed class DbUpdateWrapper(Exception inner) : Microsoft.EntityFrameworkCore.DbUpdateException("update", inner);
}
