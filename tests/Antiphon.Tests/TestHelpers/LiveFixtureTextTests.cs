using System.Text;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

[Category("Unit")]
public sealed class LiveFixtureTextTests
{
    [Test]
    [Arguments("")]
    [Arguments("dispatch-")]
    [Arguments("dispatch-ready")]
    public async Task Barrier_reader_shares_an_existing_writer_and_preserves_partial_text(string text)
    {
        var observed = await LiveFixtureText.ReadAsync("synthetic-barrier", (_, share) =>
        {
            // Model Windows' reciprocal sharing check without opening a native handle.
            if ((share & FileShare.Write) == 0)
                throw new IOException("sharing violation: the existing barrier handle has write access");
            return new MemoryStream(Encoding.UTF8.GetBytes(text));
        });
        observed.ShouldBe(text, "the polling caller, not the reader, decides whether the barrier is complete");
    }
}
