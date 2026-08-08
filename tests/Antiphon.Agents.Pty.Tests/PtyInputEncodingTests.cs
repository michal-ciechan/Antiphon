using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// Pins the ONE canonical body encoding for typing into an agent TUI (see PtyInputEncoding):
/// LF normalization kills the CR fragmentation/strand hazard, bracketed paste keeps multi-line
/// bodies whole across ConPTY chunking, and single-line bodies pass through untouched.
/// </summary>
public class PtyInputEncodingTests
{
    [Test]
    public void Single_line_body_passes_through_unwrapped()
    {
        PtyInputEncoding.EncodeBody("hello there").ShouldBe("hello there");
    }

    [Test]
    public void Crlf_line_endings_normalize_to_lf()
    {
        PtyInputEncoding.NormalizeBody("first\r\nsecond\r\nthird").ShouldBe("first\nsecond\nthird");
    }

    [Test]
    public void Bare_cr_line_endings_normalize_to_lf()
    {
        PtyInputEncoding.NormalizeBody("first\rsecond").ShouldBe("first\nsecond");
    }

    [Test]
    public void Trailing_whitespace_and_newlines_are_trimmed()
    {
        // A trailing newline left in place would put an empty line in the composer; a trailing
        // CR would be the submit-swallowing hazard all over again.
        PtyInputEncoding.NormalizeBody("body\r\n").ShouldBe("body");
        PtyInputEncoding.NormalizeBody("body  \n\n").ShouldBe("body");
    }

    [Test]
    public void Multiline_body_is_wrapped_in_bracketed_paste()
    {
        PtyInputEncoding.EncodeBody("first\r\nsecond")
            .ShouldBe("\x1b[200~first\nsecond\x1b[201~");
    }

    [Test]
    public void Encoded_body_never_contains_a_carriage_return()
    {
        // The invariant everything hangs on: only the caller's separate submit write may carry \r.
        var encoded = PtyInputEncoding.EncodeBody("a\r\nb\rc\nd\r\n");
        encoded.ShouldNotContain("\r");
    }
}
