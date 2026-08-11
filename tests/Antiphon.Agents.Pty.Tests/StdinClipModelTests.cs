using System.Text;
using Antiphon.FakeClaude;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// The clip model's arithmetic, driven directly rather than through a pty.
///
/// <para><b>Why not do all of this through ConPTY?</b> Because one rule cannot be tested there at
/// all. MEASURED here (fakeclaude's <c>ANTIPHON_FAKE_DEBUG_INPUT=1</c> READS line): a 1 291-byte
/// UTF-8 body of em-dash-bearing lines reaches a .NET console peer behind ConPTY as <b>1 023
/// bytes</b> — one byte per em-dash, not three — even though the peer's console input codepage
/// reads back as 65001. ConPTY narrows non-ASCII input for a ReadFile client, so a multibyte body
/// cannot demonstrate byte-vs-char chunking through the pty: down there, bytes and chars are the
/// same number. <see cref="FakeClaudeContractTests"/> pins the behaviour that DOES survive the
/// transport; this pins the rule that does not.</para>
///
/// <para>That rule is not academic: the delivery ceilings shipped once comparing
/// <c>string.Length</c> (UTF-16 chars) while the read quantum the TUI drops is measured in UTF-8
/// bytes, and briefs here are em-dash-heavy at 3 bytes each (21743a5). A clip model that counted
/// chars would agree with that bug instead of catching it.</para>
/// </summary>
[Category("Unit")]
public class StdinClipModelTests
{
    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    /// <summary>Lines of exactly 7 bytes ("Q00000\n") — the resolution CARD-0027 measured the cut at.</summary>
    private static byte[] MarkedBody(int lines) =>
        Bytes(string.Concat(Enumerable.Range(0, lines).Select(i => $"Q{i:D5}\n")));

    // MEASURED: 810 and 972-byte bodies arrived whole 3/3. A body inside one read chunk has no
    // earlier chunk to lose — this is the boundary DelegationSettings.BriefInlineMaxBytes sits under.
    [Test]
    [Arguments(1)]
    [Arguments(810)]
    [Arguments(1024)]
    public void A_burst_inside_one_chunk_is_returned_untouched(int size)
    {
        var burst = Bytes(new string('x', size));

        new StdinClipModel().Apply(burst, out var note).ShouldBe(burst);
        note.ShouldBeNull("nothing was dropped, so there is nothing to report");
    }

    // The live shape: the head goes and the survivor is a WHOLE chunk cut on a 1024-byte boundary.
    [Test]
    public void A_two_chunk_burst_keeps_only_its_final_whole_chunk()
    {
        var burst = MarkedBody(200); // 1 400 bytes

        var kept = new StdinClipModel().Apply(burst, out var note);

        kept.Length.ShouldBe(1400 - 1024, "the survivor starts exactly at body byte 1024");
        kept.ShouldBe(burst[1024..]);
        note.ShouldNotBeNull().ShouldContain("2 chunks");
    }

    // The other live signature: a 5 194-byte body kept its FIRST chunk only.
    [Test]
    public void Keep_first_keeps_the_leading_whole_chunk()
    {
        var burst = MarkedBody(200);

        var kept = new StdinClipModel(keepFirst: true).Apply(burst, out _);

        kept.ShouldBe(burst[..1024]);
    }

    // THE regression this model exists to catch. 128 lines of "Q00000—\n" are 1 024 CHARS and
    // 1 280 BYTES: a char-counting model calls that one chunk and loses nothing.
    [Test]
    public void The_read_quantum_is_counted_in_utf8_bytes_not_utf16_chars()
    {
        var text = string.Concat(Enumerable.Range(0, 128).Select(i => $"Q{i:D5}—\n"));
        text.Length.ShouldBe(1024, "1 024 UTF-16 chars…");
        var burst = Bytes(text);
        burst.Length.ShouldBe(1280, "…and 1 280 UTF-8 bytes, because an em-dash costs 3");

        var kept = new StdinClipModel().Apply(burst, out var note);

        note.ShouldNotBeNull(
            "a body under 1 024 CHARS but over 1 024 BYTES spans two read chunks and must lose one — "
            + "if it survives whole, the model is counting chars and agrees with the bug it exists to catch");
        kept.ShouldBe(burst[1024..]);
    }

    // The cut lands on a byte boundary, not a character boundary — so a multibyte character
    // straddling it is severed. That is what the real TUI does too; a model that snapped to the
    // nearest character would quietly under-report the damage.
    [Test]
    public void A_character_straddling_the_cut_is_severed_like_the_real_thing()
    {
        // Em-dash 341 occupies bytes 1 023-1 025, so the cut at 1 024 lands inside it.
        var burst = Bytes(new string('—', 400)); // 1 200 bytes = 2 chunks

        var kept = new StdinClipModel().Apply(burst, out _);

        kept[0].ShouldBe((byte)0x80, "the survivor begins mid-character, on the continuation byte");
        Encoding.UTF8.GetString(kept)[0].ShouldBe('�', "which decodes as a replacement char");
    }

    // Bracketed-paste markers are re-attached to the survivor: dropping the chunk that carried the
    // closing 201~ would leave the peer stuck in paste mode — an artefact of the model, not a
    // modelled behaviour. Their bytes are also excluded from the chunk arithmetic, which is what
    // puts the cut at BODY byte 1024 (measured 1029 ± 7 with a 6-byte prefix in flight).
    [Test]
    public void Paste_markers_are_preserved_and_excluded_from_the_chunk_arithmetic()
    {
        var body = MarkedBody(200);
        var burst = Bytes("\x1b[200~").Concat(body).Concat(Bytes("\x1b[201~")).ToArray();

        var kept = new StdinClipModel().Apply(burst, out _);

        Encoding.UTF8.GetString(kept).ShouldStartWith("\x1b[200~");
        Encoding.UTF8.GetString(kept).ShouldEndWith("\x1b[201~");
        kept[6..^6].ShouldBe(body[1024..], "the cut is at BODY byte 1024, markers not counted");
    }

    // Live, the loss is non-deterministic — three identical 1 400-byte trials gave whole, clipped,
    // clipped. CI gets the worst case instead: same input, same verdict, every time.
    [Test]
    public void Deterministic_mode_is_deterministic()
    {
        var results = Enumerable.Range(0, 5)
            .Select(_ => new StdinClipModel().Apply(MarkedBody(400), out string? _))
            .ToList();

        results.ShouldAllBe(r => r.SequenceEqual(results[0]));
    }

    // Turn grouping is the live variable, and the dose-response that named the mechanism: chunks
    // that land in separate event-loop turns all survive (0 ms kept 8 %, 50-100 ms 56 %).
    [Test]
    public void Every_chunk_in_its_own_turn_survives()
    {
        var burst = MarkedBody(400); // 2 800 bytes = 3 chunks

        new StdinClipModel(random: true, splitPct: 100).Apply(burst, out var note).ShouldBe(burst);
        note.ShouldBeNull();
    }

    // A non-deterministic failure is only useful if it can be replayed: same seed, same loss.
    [Test]
    public void Random_mode_replays_identically_for_the_same_seed_and_differs_across_seeds()
    {
        byte[] Run(int seed) =>
            new StdinClipModel(random: true, splitPct: 50, seed: seed)
                .Apply(MarkedBody(2000), out string? _);

        Run(7).ShouldBe(Run(7));
        // Not a coin flip: over a 14 000-byte body the chance two seeds agree on every turn
        // boundary AND every survivor is vanishing.
        Run(7).SequenceEqual(Run(99)).ShouldBeFalse("different seeds must explore different outcomes");
    }

    // Whatever the seed, a survivor is always a whole number of chunks — never a fragment of one.
    // "What survives is always a whole number of chunks" is the measured signature; a model that
    // could emit a half chunk would let a test pass on damage the real TUI never produces.
    [Test]
    public void Random_survivors_are_always_whole_chunks()
    {
        var burst = MarkedBody(1000); // 7 000 bytes = 7 chunks

        for (var seed = 0; seed < 25; seed++)
        {
            var kept = new StdinClipModel(random: true, splitPct: 40, seed: seed).Apply(burst, out _);

            // Every surviving chunk must be byte-identical to one of the source chunks, in order.
            var offset = 0;
            while (offset < kept.Length)
            {
                var take = Math.Min(1024, kept.Length - offset);
                var slice = kept[offset..(offset + take)];
                Enumerable.Range(0, 7)
                    .Any(c => burst[(c * 1024)..Math.Min((c + 1) * 1024, burst.Length)].SequenceEqual(slice))
                    .ShouldBeTrue($"seed {seed}: the run at {offset} is not a whole source chunk");
                offset += take;
            }
        }
    }

    [Test]
    public void An_unknown_mode_fails_loudly_rather_than_silently_disabling_the_model()
    {
        Environment.SetEnvironmentVariable(StdinClipModel.ModeVar, "yes-please");
        try
        {
            Should.Throw<ArgumentException>(() => StdinClipModel.FromEnvironment());
        }
        finally
        {
            Environment.SetEnvironmentVariable(StdinClipModel.ModeVar, null);
        }
    }
}
