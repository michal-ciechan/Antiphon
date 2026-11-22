using System.Text;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

[Category("Unit")]
public sealed class LiveFixtureTextTests
{
    [Test]
    public async Task Published_marker_waits_for_content_after_transient_open_failures(CancellationToken cancellationToken)
    {
        var attempts = 0;
        var observed = await LiveFixtureText.ReadPublishedAsync("synthetic-marker", (_, share) =>
        {
            share.ShouldBe(FileShare.ReadWrite | FileShare.Delete);
            return ++attempts switch
            {
                1 => new MemoryStream(),
                2 => throw new FileNotFoundException(),
                3 => throw new IOException("simulated sharing violation", unchecked((int)0x80070020)),
                _ => new MemoryStream(Encoding.UTF8.GetBytes("12345")),
            };
        }, cancellationToken);

        observed.ShouldBe("12345");
        attempts.ShouldBe(4);
    }

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
