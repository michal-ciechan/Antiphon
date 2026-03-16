using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Antiphon.Server.Infrastructure.Agents;

namespace Antiphon.Tests;

/// <summary>
/// Unit tests for the CachingChatClient normalizer and cache modes.
/// </summary>
public class CachingChatClientTests : IDisposable
{
    private readonly string _tempCacheDir;

    public CachingChatClientTests()
    {
        _tempCacheDir = Path.Combine(Path.GetTempPath(), $"antiphon-cache-test-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempCacheDir))
        {
            Directory.Delete(_tempCacheDir, recursive: true);
        }
    }

    [Fact]
    public void NormalizeContent_strips_iso8601_timestamps()
    {
        var input = "Created at 2026-03-16T12:34:56Z and updated at 2026-03-16T12:34:56.789+05:00";
        var result = CachingChatClient.NormalizeContent(input);

        result.Should().NotContain("2026-03-16T12:34:56Z");
        result.Should().Contain("<TIMESTAMP>");
    }

    [Fact]
    public void NormalizeContent_strips_uuids()
    {
        var input = "Workflow a0000000-0000-0000-0000-000000000001 started";
        var result = CachingChatClient.NormalizeContent(input);

        result.Should().NotContain("a0000000-0000-0000-0000-000000000001");
        result.Should().Contain("<UUID>");
    }

    [Fact]
    public void NormalizeContent_strips_git_shas()
    {
        var input = "Commit 698a1a4b5c6d7e8f9a0b1c2d3e4f5a6b7c8d9e0f deployed";
        var result = CachingChatClient.NormalizeContent(input);

        result.Should().NotContain("698a1a4b5c6d7e8f9a0b1c2d3e4f5a6b7c8d9e0f");
        result.Should().Contain("<SHA>");
    }

    [Fact]
    public void NormalizeContent_strips_windows_paths()
    {
        var input = @"File located at C:\Users\developer\project\file.cs";
        var result = CachingChatClient.NormalizeContent(input);

        result.Should().Contain("<PATH>");
    }

    [Fact]
    public void NormalizeContent_strips_unix_paths()
    {
        var input = "Config at /home/user/project/config.yml";
        var result = CachingChatClient.NormalizeContent(input);

        result.Should().Contain("<PATH>");
    }

    [Fact]
    public void ComputeHash_is_deterministic()
    {
        var content = "Hello, world!";
        var hash1 = CachingChatClient.ComputeHash(content);
        var hash2 = CachingChatClient.ComputeHash(content);

        hash1.Should().Be(hash2);
        hash1.Should().HaveLength(64); // SHA256 hex string
    }

    [Fact]
    public void ComputeHash_differs_for_different_content()
    {
        var hash1 = CachingChatClient.ComputeHash("content A");
        var hash2 = CachingChatClient.ComputeHash("content B");

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public async Task Record_mode_always_caches_response()
    {
        var client = new CachingChatClient(CacheMode.Record, NullLogger<CachingChatClient>.Instance, _tempCacheDir);

        await client.CacheResponseAsync("test request", "test response");

        var files = Directory.GetFiles(_tempCacheDir, "*.json");
        files.Should().HaveCount(1);
    }

    [Fact]
    public async Task RecordIfMissing_does_not_overwrite_existing()
    {
        var client = new CachingChatClient(CacheMode.RecordIfMissing, NullLogger<CachingChatClient>.Instance, _tempCacheDir);

        await client.CacheResponseAsync("test request", "original response");
        await client.CacheResponseAsync("test request", "new response");

        var cached = await client.GetCachedResponseAsync("test request");
        cached.Should().Be("original response");
    }

    [Fact]
    public async Task Replay_mode_throws_on_cache_miss()
    {
        var client = new CachingChatClient(CacheMode.Replay, NullLogger<CachingChatClient>.Instance, _tempCacheDir);

        var act = () => client.GetCachedResponseAsync("nonexistent request");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cache miss in Replay mode*");
    }

    [Fact]
    public async Task Replay_mode_returns_cached_response()
    {
        // First, cache a response using Record mode
        var recorder = new CachingChatClient(CacheMode.Record, NullLogger<CachingChatClient>.Instance, _tempCacheDir);
        await recorder.CacheResponseAsync("test request", "cached response");

        // Then replay
        var replayer = new CachingChatClient(CacheMode.Replay, NullLogger<CachingChatClient>.Instance, _tempCacheDir);
        var result = await replayer.GetCachedResponseAsync("test request");

        result.Should().Be("cached response");
    }

    [Fact]
    public async Task PassThrough_mode_never_reads_or_writes_cache()
    {
        var client = new CachingChatClient(CacheMode.PassThrough, NullLogger<CachingChatClient>.Instance, _tempCacheDir);

        await client.CacheResponseAsync("test request", "test response");

        // PassThrough should not create any files
        Directory.Exists(_tempCacheDir).Should().BeFalse();
    }

    [Fact]
    public void Cache_files_stored_as_json_in_cache_directory()
    {
        var client = new CachingChatClient(CacheMode.Record, NullLogger<CachingChatClient>.Instance, _tempCacheDir);

        client.CacheDirectory.Should().Be(_tempCacheDir);
        client.Mode.Should().Be(CacheMode.Record);
    }
}
