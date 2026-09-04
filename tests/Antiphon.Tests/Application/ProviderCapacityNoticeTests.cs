using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public class ProviderCapacityNoticeTests
{
    [Test]
    public void Format_names_kind_status_and_asks_a_human_when_no_fallback()
    {
        var text = ProviderCapacityNotice.Format(
            AgentKind.Grok,
            "grok-4.6",
            402,
            "Payment Required",
            fallbackDeclared: false,
            detail: "usage balance exhausted");

        text.ShouldContain("can't answer right now");
        text.ShouldContain("Grok");
        text.ShouldContain("HTTP 402");
        text.ShouldContain("Payment Required");
        text.ShouldContain("usage balance exhausted");
        text.ShouldContain("Your message is kept");
        text.ShouldContain("Someone needs to restore capacity");
        text.ShouldNotContain("fallback");
    }

    [Test]
    public void Scrub_drops_bearer_tokens_keys_and_urls()
    {
        var text = ProviderCapacityNotice.Format(
            AgentKind.Grok,
            "grok-4.6",
            402,
            "Payment Required",
            fallbackDeclared: false,
            detail: "usage balance exhausted Bearer xai-secretvalue https://api.x.ai/v1/fail");

        text.ShouldNotContain("xai-secretvalue");
        text.ShouldNotContain("https://api.x.ai");
        text.ShouldNotContain("Bearer xai-secretvalue");
        text.ShouldContain("usage balance exhausted");
    }

    [Test]
    public void FormatTransport_names_the_endpoint_and_consumes_the_retry_suffix()
    {
        var text = ProviderCapacityNotice.FormatTransport(
            AgentKind.Grok,
            "grok-4.6",
            retryCount: 0,
            detail: "error sending request for url (http://localhost:10746/v1/chat/completions) [after 15 retries]");

        text.ShouldContain("can't answer right now");
        text.ShouldContain("couldn't reach");
        text.ShouldContain("Grok");
        text.ShouldContain("after 15 attempts");
        text.ShouldContain("error sending request");
        text.ShouldNotContain("http://");
        text.ShouldNotContain("[after 15 retries]");
        text.ShouldContain("please send it again");
    }

    [Test]
    public void FormatProviderError_names_status_and_says_retries_are_parked()
    {
        var text = ProviderCapacityNotice.FormatProviderError(
            AgentKind.Grok,
            "grok-4.6",
            500,
            "Internal Server Error",
            detail: "The model is currently at capacity");

        text.ShouldContain("can't answer right now");
        text.ShouldContain("Grok");
        text.ShouldContain("HTTP 500");
        text.ShouldContain("Internal Server Error");
        text.ShouldContain("automatic retries are parked");
        text.ShouldContain("Please send it again later");
    }
}
