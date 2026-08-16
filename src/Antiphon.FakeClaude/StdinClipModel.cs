using System.Text;

namespace Antiphon.FakeClaude;

/// <summary>
/// Models the ONE behaviour of the real Claude TUI that fakeclaude previously could not exhibit:
/// it <em>silently discards</em> most of a large paste. Opt-in via <c>ANTIPHON_FAKE_STDIN_CLIP</c>;
/// with the variable unset this type is never constructed and the fake is byte-for-byte the
/// lossless peer it has always been (which is what <c>PtyLargeWriteTests</c> pins — our transport
/// really is lossless, and that must stay pinned).
///
/// <para><b>Why a fake needs to lose bytes.</b> fakeclaude is a .NET console peer that receives
/// every byte; the real consumer is a Bun/Ink TUI that keeps ONE ~1024-byte read chunk per
/// event-loop turn and drops the rest. Because the fake was lossless where the real one clips, the
/// 2026-08-10 investigation used it to conclude "the loss is not in our code" and shipped delivery
/// ceilings built on that reading — and the ceilings were wrong twice over. A fake that cannot
/// exhibit the failure cannot protect us from it (CARD-0028).</para>
///
/// <para><b>The model, and the measurement behind each rule</b>
/// (<c>docs/investigations/2026-08-11-pty-chunk-loss-root-cause-CARD-0027.md</c>):</para>
/// <list type="bullet">
///   <item>reads arrive in ~1024-byte chunks (1024 Node, 1040 Bun) — <see cref="ChunkBytes"/>;</item>
///   <item>of the chunks that land in ONE event-loop turn, exactly one survives, and survivors are
///     always a whole number of chunks;</item>
///   <item>the cut sits on a 1024-byte boundary of the BODY (measured 1029 ± 7 on a 1 400-byte body,
///     bracketed-paste markers excluded — hence <see cref="StripMarkers"/> before chunking);</item>
///   <item>a body inside a single chunk cannot lose anything (810 and 972 bytes were whole 3/3), so
///     <see cref="Apply"/> returns short bursts untouched;</item>
///   <item>more gap between writes ⇒ more chunks survive (0 ms kept 8 %, 25 ms 26 %, 50-100 ms 56 %).
///     Modelled by the caller: fakeclaude groups arriving bytes into BURSTS by a quiet gap, and one
///     burst is one event-loop turn, so writes spread over time are already several turns.</item>
/// </list>
///
/// <para><b>Bytes, not chars.</b> Chunking is done on the UTF-8 byte stream. The delivery ceilings
/// shipped once comparing <c>string.Length</c> (UTF-16 chars) and an em-dash costs 3 bytes, so a
/// 900-CHARACTER brief could be 2 700 bytes and mangle while passing the guard (21743a5). A fake
/// that chunked in chars would agree with that bug instead of catching it.</para>
///
/// <para><b>Determinism is a feature.</b> Live, the loss is non-deterministic — three identical
/// 1 400-byte trials gave whole, clipped, clipped. A flaky fake is useless to CI, so the default
/// mode is the deterministic WORST CASE (every chunk of a burst is one turn ⇒ exactly one survives).
/// The non-determinism is offered separately (<c>ANTIPHON_FAKE_STDIN_CLIP=random</c>) and is SEEDED,
/// so a failure can be replayed by re-running with the seed the fake printed.</para>
/// </summary>
internal sealed class StdinClipModel
{
    /// <summary>Off / unset = no clipping. <c>1</c>|<c>on</c>|<c>deterministic</c> = worst case. <c>random</c> = seeded.</summary>
    public const string ModeVar = "ANTIPHON_FAKE_STDIN_CLIP";
    public const string ChunkBytesVar = "ANTIPHON_FAKE_STDIN_CLIP_CHUNK_BYTES";
    public const string KeepVar = "ANTIPHON_FAKE_STDIN_CLIP_KEEP";
    public const string SeedVar = "ANTIPHON_FAKE_STDIN_CLIP_SEED";
    public const string SplitPctVar = "ANTIPHON_FAKE_STDIN_CLIP_SPLIT_PCT";

    private static readonly byte[] PasteStart = Encoding.ASCII.GetBytes("\x1b[200~");
    private static readonly byte[] PasteEnd = Encoding.ASCII.GetBytes("\x1b[201~");

    private readonly bool _random;
    private readonly int _splitPct;
    private readonly int _seed;
    private readonly bool _keepFirst;
    private int _turnGroupIndex;

    /// <summary>The read quantum, in UTF-8 BYTES. 1024 is the measured Node/ConPTY value.</summary>
    public int ChunkBytes { get; }

    /// <summary>Internal (not private) so <c>StdinClipModelTests</c> can drive the model without env vars.</summary>
    internal StdinClipModel(
        bool random = false, int chunkBytes = 1024, bool keepFirst = false, int seed = 1, int splitPct = 25)
    {
        _random = random;
        ChunkBytes = chunkBytes;
        _keepFirst = keepFirst;
        _seed = seed;
        _splitPct = splitPct;
    }

    /// <summary>Null unless <see cref="ModeVar"/> asks for clipping — the fake's default is unchanged.</summary>
    public static StdinClipModel? FromEnvironment()
    {
        var mode = (Environment.GetEnvironmentVariable(ModeVar) ?? string.Empty).Trim().ToLowerInvariant();
        if (mode is "" or "0" or "off" or "false") return null;

        var random = mode is "random" or "nondeterministic";
        if (!random && mode is not ("1" or "on" or "true" or "deterministic" or "worst"))
            throw new ArgumentException(
                $"{ModeVar}='{mode}' is not a mode. Use 1 (deterministic worst case) or random (seeded).");

        return new StdinClipModel(
            random,
            chunkBytes: Positive(ChunkBytesVar, 1024),
            keepFirst: string.Equals(
                Environment.GetEnvironmentVariable(KeepVar), "first", StringComparison.OrdinalIgnoreCase),
            seed: int.TryParse(Environment.GetEnvironmentVariable(SeedVar), out var s) ? s : 1,
            splitPct: Math.Clamp(
                int.TryParse(Environment.GetEnvironmentVariable(SplitPctVar), out var p) ? p : 25, 0, 100));
    }

    private static int Positive(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var v) && v > 0 ? v : fallback;

    /// <summary>
    /// Printed at startup so a CI failure carries the exact model that produced it — including the
    /// seed, which is the whole point of offering the non-deterministic mode.
    /// </summary>
    public string Describe() =>
        $"CLIP:mode={(_random ? "random" : "deterministic")} chunkBytes={ChunkBytes} "
        + $"keep={(_random ? "seeded" : _keepFirst ? "first" : "last")} seed={_seed} splitPct={_splitPct}";

    /// <summary>
    /// One burst = one event-loop turn's worth of reads. Returns what a TUI that keeps one chunk per
    /// turn would have accumulated, and (when anything was dropped) a note naming what went, so a
    /// test failure or a human reading the screen can see the loss instead of inferring it.
    /// </summary>
    public byte[] Apply(byte[] burst, out string? note)
    {
        note = null;

        var (content, hadStart, hadEnd) = StripMarkers(burst);
        // A body inside ONE read chunk has no earlier chunk to lose — measured whole 3/3 at 810 and
        // 972 bytes. This is also what keeps the submitting CR (a 1-byte burst) intact.
        if (content.Length <= ChunkBytes) return burst;

        var chunks = (content.Length + ChunkBytes - 1) / ChunkBytes;
        var rng = _random ? new Random(unchecked(_seed * 397 + _turnGroupIndex++)) : null;

        // Group the chunks into event-loop turns, then keep exactly one chunk from each turn.
        var kept = new List<int>();
        var turns = 0;
        var turnStart = 0;
        for (var i = 1; i <= chunks; i++)
        {
            // Deterministic mode: the whole burst is ONE turn (the worst case CI needs). Random
            // mode: each chunk may start a new turn — which is precisely the live variable, since
            // the same body arrived whole once and clipped twice depending on how its chunks fell
            // across turns, and widening the gap between writes (⇒ more turns) saved more of it.
            var boundary = i == chunks || (rng is not null && rng.Next(100) < _splitPct);
            if (!boundary) continue;

            var count = i - turnStart;
            kept.Add(turnStart + (rng is not null ? rng.Next(count) : _keepFirst ? 0 : count - 1));
            turnStart = i;
            turns++;
        }

        if (kept.Count == chunks) return burst; // every chunk got its own turn — nothing lost

        var survivors = new List<byte>(kept.Count * ChunkBytes);
        if (hadStart) survivors.AddRange(PasteStart);
        foreach (var c in kept)
        {
            var from = c * ChunkBytes;
            survivors.AddRange(content[from..Math.Min(from + ChunkBytes, content.Length)]);
        }
        if (hadEnd) survivors.AddRange(PasteEnd);

        note = $"CLIPPED:{content.Length}B in {chunks} chunks over {turns} turn(s) -> kept "
            + $"{kept.Count} chunk(s) [{string.Join(",", kept)}] = {survivors.Count}B";
        return survivors.ToArray();
    }

    /// <summary>
    /// Bracketed-paste markers are removed before chunking and re-attached after, for two reasons.
    /// The measured cut lands on a 1024-byte boundary of the BODY (1029 ± 7 with a 6-byte
    /// <c>\e[200~</c> prefix in flight, which rules out a stream-aligned cut at body byte 1018), and
    /// dropping a chunk that happened to carry the closing marker would leave the fake stuck in
    /// paste mode — an artefact of the model, not a modelled behaviour.
    /// </summary>
    private static (byte[] Content, bool HadStart, bool HadEnd) StripMarkers(byte[] burst)
    {
        var hadStart = IndexOf(burst, PasteStart, 0) >= 0;
        var hadEnd = IndexOf(burst, PasteEnd, 0) >= 0;
        if (!hadStart && !hadEnd) return (burst, false, false);

        var content = new List<byte>(burst.Length);
        for (var i = 0; i < burst.Length;)
        {
            if (Matches(burst, PasteStart, i)) { i += PasteStart.Length; continue; }
            if (Matches(burst, PasteEnd, i)) { i += PasteEnd.Length; continue; }
            content.Add(burst[i++]);
        }
        return (content.ToArray(), hadStart, hadEnd);
    }

    private static bool Matches(byte[] haystack, byte[] needle, int at)
    {
        if (at + needle.Length > haystack.Length) return false;
        for (var i = 0; i < needle.Length; i++)
            if (haystack[at + i] != needle[i]) return false;
        return true;
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int from)
    {
        for (var i = from; i + needle.Length <= haystack.Length; i++)
            if (Matches(haystack, needle, i)) return i;
        return -1;
    }
}
