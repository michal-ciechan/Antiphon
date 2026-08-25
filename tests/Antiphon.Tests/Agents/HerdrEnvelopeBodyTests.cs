using System.Text;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

[Category("Unit")]
public class HerdrEnvelopeBodyTests
{
    [Test]
    [Arguments(43_200)]
    [Arguments(86_400)]
    public void BuildMultilineEnvelopeBody_is_exact_utf8_and_multiline(int size)
    {
        var body = HerdrRealCliCanarySupport.BuildMultilineEnvelopeBody(size, "HEAD", "TAIL");
        Encoding.UTF8.GetByteCount(body).ShouldBe(size);
        body.ShouldContain('\n');
        body.ShouldStartWith("HEAD\n");
        body.ShouldEndWith("\nTAIL");
    }
}
