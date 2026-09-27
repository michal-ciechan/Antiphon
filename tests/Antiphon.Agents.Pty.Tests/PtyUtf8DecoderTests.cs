using System.Text;
using System.Threading.Channels;
using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

[Category("Unit")]
public class PtyUtf8DecoderTests
{
    private sealed class ScriptedStream : Stream
    {
        private readonly Channel<object> _steps = Channel.CreateUnbounded<object>();
        public void Push(byte[]? bytes) => _steps.Writer.TryWrite(bytes ?? Eof);
        public void Fail(IOException error) => _steps.Writer.TryWrite(error);
        private static readonly object Eof = new();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var step = await _steps.Reader.ReadAsync(cancellationToken);
            if (step == Eof) return 0;
            if (step is IOException error) throw error;
            var bytes = (byte[])step;
            bytes.AsSpan().CopyTo(buffer.Span);
            return bytes.Length;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class Probe : IAsyncDisposable
    {
        private readonly ScriptedStream _stream = new();
        private readonly CancellationTokenSource _cancel = new();
        private readonly Channel<bool> _processed = Channel.CreateUnbounded<bool>();
        private readonly List<string> _subscriberA = [];
        private readonly List<string> _subscriberB = [];
        public PtyAgentRunner Runner { get; } = new();
        public Task Reading { get; }
        public bool CancelAfterNextRead { get; set; }
        public Probe()
        {
            Runner.OnData += text => _subscriberA.Add(text);
            Runner.OnData += text => _subscriberB.Add(text);
            Runner.ReadProcessedForTest = () =>
            {
                _processed.Writer.TryWrite(true);
                if (CancelAfterNextRead) _cancel.Cancel();
            };
            Reading = Runner.ReadStreamForTestAsync(_stream, _cancel.Token);
        }
        public async Task PushAsync(byte[]? bytes)
        {
            _stream.Push(bytes);
            await _processed.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
        public void AssertText(string expected, int? events = null)
        {
            Runner.SnapshotText().ShouldBe(expected);
            string.Concat(Runner.Output.Snapshot()).ShouldBe(expected);
            string.Concat(_subscriberA).ShouldBe(expected);
            string.Concat(_subscriberB).ShouldBe(expected);
            _subscriberA.ShouldBe(_subscriberB);
            if (events is int n) { Runner.Output.Count.ShouldBe(n); _subscriberA.Count.ShouldBe(n); }
        }
        public void Cancel() => _cancel.Cancel();
        public void Fail(IOException error) => _stream.Fail(error);
        public async ValueTask DisposeAsync()
        {
            if (!Reading.IsCompleted) _cancel.Cancel();
            await Reading.WaitAsync(TimeSpan.FromSeconds(5));
            await Runner.DisposeAsync();
            _cancel.Dispose();
        }
    }

    private static async Task AssertPartitionsAsync(byte[] bytes, string expected)
    {
        for (var cut = 0; cut <= bytes.Length; cut++)
        {
            await using var p = new Probe();
            if (cut > 0) await p.PushAsync(bytes[..cut]);
            if (cut < bytes.Length) await p.PushAsync(bytes[cut..]);
            await p.PushAsync(null);
            await p.Reading;
            p.AssertText(expected);
        }
        await using var bytewise = new Probe();
        foreach (var b in bytes) await bytewise.PushAsync([b]);
        await bytewise.PushAsync(null);
        bytewise.AssertText(expected);
    }

    [Test]
    public async Task C464_Every_byte_boundary_preserves_text_and_runner_outputs()
    {
        await AssertPartitionsAsync(Encoding.UTF8.GetBytes("Aé─😀Z"), "Aé─😀Z");
        var decoder = new PtyUtf8Decoder();
        decoder.Decode(ReadOnlySpan<byte>.Empty).ShouldBe("");
        decoder.Decode([0xC3]).ShouldBe("");
        decoder.Decode(ReadOnlySpan<byte>.Empty).ShouldBe("");
        decoder.Decode([0xA9]).ShouldBe("é");
        await using (var p = new Probe())
        {
            await p.PushAsync([0xE2]); p.AssertText("", 0);
            await p.PushAsync([0x94]); p.AssertText("", 0);
            await p.PushAsync([0x80]); p.AssertText("─", 1);
            await p.PushAsync([0x5A]); p.AssertText("─Z", 2);
            await p.PushAsync(null);
        }
        await using (var a = new Probe())
        await using (var b = new Probe())
        {
            await a.PushAsync([0xC3]); a.AssertText("", 0);
            await b.PushAsync([0x42]); b.AssertText("B", 1);
            await a.PushAsync([0xA9]); a.AssertText("é", 1);
            await a.PushAsync(null); await b.PushAsync(null);
        }
        await using (var p = new Probe())
        {
            foreach (var b in Encoding.UTF8.GetBytes("A─\x1b[2;3HZ")) await p.PushAsync([b]);
            await p.PushAsync(null);
            p.Runner.SnapshotRow(0).ShouldBe("A─");
            p.Runner.SnapshotRow(1).ShouldBe("  Z");
        }
    }

    [Test]
    public async Task C464_Malformed_bytes_preserve_following_text()
    {
        await AssertPartitionsAsync([0xC3, 0x28, 0x5A], "\uFFFD(Z");
        await AssertPartitionsAsync([0xE2, 0x28, 0xA1, 0x5A], "\uFFFD(\uFFFDZ");
        await AssertPartitionsAsync([0xFF, 0x80, 0x5A], "\uFFFD\uFFFDZ");
        var bytes = new byte[] { 0xC3, 0x28, 0xC3, 0xA9 }.Concat(Encoding.UTF8.GetBytes("\x1b[2;3HTAIL")).ToArray();
        await AssertPartitionsAsync(bytes, "\uFFFD(é\x1b[2;3HTAIL");
        await using var p = new Probe();
        foreach (var b in bytes) await p.PushAsync([b]);
        await p.PushAsync(null);
        p.Runner.SnapshotRow(1).ShouldBe("  TAIL");
    }

    [Test]
    public async Task C464_Only_clean_eof_flushes_one_replacement()
    {
        foreach (var suffix in new byte[][] { [0xC3], [0xE2, 0x94], [0xF0, 0x9F, 0x98] })
        {
            await using var p = new Probe();
            await p.PushAsync([0x41]);
            await p.PushAsync(suffix);
            p.AssertText("A", 1);
            await p.PushAsync(null);
            await p.Reading;
            p.AssertText("A\uFFFD", 2);
            p.Runner.SnapshotRow(0).ShouldBe("A\uFFFD");
        }
        await using (var p = new Probe())
        {
            await p.PushAsync([0x41]); await p.PushAsync([0xC3, 0xA9]);
            await p.PushAsync(null); p.AssertText("Aé", 2);
        }
        await using (var p = new Probe())
        {
            foreach (var b in Encoding.UTF8.GetBytes("A\x1b]0;")) await p.PushAsync([b]);
            await p.PushAsync([0xC3]); await p.PushAsync(null);
            p.AssertText("A\x1b]0;\uFFFD");
            p.Runner.SnapshotRow(0).ShouldBe("A");
        }
        await using (var p = new Probe())
        {
            await p.PushAsync([0x41]); await p.PushAsync([0xC3]);
            p.Cancel();
            await p.Reading;
            p.AssertText("A", 1);
        }
        await using (var p = new Probe())
        {
            await p.PushAsync([0x41]);
            p.CancelAfterNextRead = true;
            await p.PushAsync([0xC3]);
            await p.Reading;
            p.AssertText("A", 1);
        }
        await using (var p = new Probe())
        {
            await p.PushAsync([0x41]); await p.PushAsync([0xC3]);
            p.Fail(new IOException("scripted read fault"));
            await p.Reading;
            p.AssertText("A", 1);
        }
        await using (var fresh = new Probe())
        {
            await fresh.PushAsync([0x5A]); await fresh.PushAsync(null);
            fresh.AssertText("Z", 1);
        }
    }
}
