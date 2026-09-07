using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Tests.TestHelpers;

    internal sealed class ScriptedSessionRunnerClient : ISessionRunnerClient
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, SessionRunnerTranscriptDto> _transcripts = new();
        private readonly System.Threading.Channels.Channel<SessionRunnerEvent> _events = System.Threading.Channels.Channel.CreateUnbounded<SessionRunnerEvent>();
        public System.Collections.Concurrent.ConcurrentQueue<Guid> Received { get; } = new();
        public System.Collections.Concurrent.ConcurrentQueue<Guid> SnapshotReads { get; } = new();
        public IReadOnlyList<SessionRunnerSessionDto> Sessions { get; set; } = [];
        public TaskCompletionSource Streaming { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Produce(SessionRunnerTranscriptEvent entry) => _events.Writer.TryWrite(new("transcript", entry.SessionId, Transcript: entry));

        public void SetTranscript(SessionRunnerTranscriptDto transcript) =>
            _transcripts[transcript.SessionId] = transcript;

        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
            Task.FromResult(Sessions);

        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct)
            => throw new NotSupportedException();

        public SessionRunnerSessionDto? SessionResponse { get; set; }
        public RunnerCapabilitiesDto? Capabilities { get; set; }
        public Task<RunnerCapabilitiesDto?> GetCapabilitiesAsync(CancellationToken ct) => Task.FromResult(Capabilities);
        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct)
            => Task.FromResult(SessionResponse ?? throw new NotSupportedException());

        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct)
        {
            SnapshotReads.Enqueue(sessionId);
            return Task.FromResult(_transcripts.TryGetValue(sessionId, out var transcript)
                ? transcript : new SessionRunnerTranscriptDto(sessionId, [], 0));
        }

        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct)
            => throw new NotSupportedException();

        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            Streaming.TrySetResult();
            await foreach (var entry in _events.Reader.ReadAllAsync(ct))
            {
                Received.Enqueue(entry.SessionId);
                yield return entry;
            }
        }
    }
