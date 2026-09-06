using System.Text;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Unit")]
public sealed class GrokRulesFileStoreTests
{
    [Test]
    public async Task Rules_content_preserves_control_newlines_unicode_and_tail_beyond_1000_lines()
    {
        using var fixture = new StoreFixture();
        var content = "START\rLF\nCRLF\r\n\"$` café 😀\"\r\n"
            + string.Join("\r\n", Enumerable.Range(1, 1105).Select(i => $"line {i}: café")) + "\r\nTAIL";
        var payload = new GrokRulesPayload(content, 1, Guid.NewGuid());
        var receipt = await fixture.Store.WriteAsync(fixture.SessionId, payload, CancellationToken.None);
        (await File.ReadAllBytesAsync(receipt.Path)).ShouldBe(new UTF8Encoding(false, true).GetBytes(content));
        receipt.ByteCount.ShouldBe(Encoding.UTF8.GetByteCount(content));
        receipt.Sha256.ShouldBe(GrokRulesTransport.Hash(Encoding.UTF8.GetBytes(content)));
        (await fixture.Store.VerifyAsync(fixture.SessionId, receipt, CancellationToken.None)).ShouldBeTrue();
        Directory.GetFiles(Path.GetDirectoryName(receipt.Path)!, "*.tmp").ShouldBeEmpty();
    }

    [Test]
    public void File_byte_limit_accepts_exactly_262144_and_rejects_262145_without_truncation()
    {
        var content = new string('é', 131072);
        GrokRulesTransport.Encode(new(content, 1, Guid.NewGuid()), true, 262144).Length.ShouldBe(262144);
        var error = Should.Throw<GrokRulesTransportException>(() =>
            GrokRulesTransport.Encode(new(content + "x", 1, Guid.NewGuid()), true, 262144));
        error.Message.ShouldBe("grok_rules_content_invalid: too_large byteCount=262145 limit=262144");
    }

    [Test]
    [Arguments("secret-sentinel\0", "nul")]
    [Arguments("secret-sentinel{{key:NAME}}", "unresolved_key")]
    public void Invalid_content_diagnostics_exclude_content(string content, string reason)
    {
        var error = Should.Throw<GrokRulesTransportException>(() =>
            GrokRulesTransport.Encode(new(content, 1, Guid.NewGuid()), true, 262144));
        error.Reason.ShouldBe(reason);
        error.StatusCode.ShouldBe(422);
        error.Message.ShouldNotContain("secret-sentinel");
    }

    [Test]
    public void Invalid_unicode_and_wrong_kind_are_rejected()
    {
        Should.Throw<GrokRulesTransportException>(() =>
            GrokRulesTransport.Encode(new("\ud800", 1, Guid.NewGuid()), true, 262144))
            .Reason.ShouldBe("invalid_unicode");
        Should.Throw<GrokRulesTransportException>(() =>
            GrokRulesTransport.Encode(new("safe", 1, Guid.NewGuid()), false, 262144))
            .Reason.ShouldBe("wrong_kind");
    }

    [Test]
    public async Task Resume_atomically_replaces_rules_at_the_same_path_and_changes_receipt()
    {
        using var fixture = new StoreFixture();
        var old = await fixture.Store.WriteAsync(fixture.SessionId, new("old\r\nrules", 1, Guid.NewGuid()), CancellationToken.None);
        var current = await fixture.Store.WriteAsync(fixture.SessionId, new("new\nrules", 1, Guid.NewGuid()), CancellationToken.None);
        current.Path.ShouldBe(old.Path);
        current.Generation.ShouldNotBe(old.Generation);
        current.Sha256.ShouldNotBe(old.Sha256);
        (await File.ReadAllTextAsync(current.Path)).ShouldBe("new\nrules");
        (await fixture.Store.VerifyAsync(fixture.SessionId, old, CancellationToken.None)).ShouldBeFalse();
    }

    [Test]
    public async Task Replace_failure_preserves_prior_complete_file_and_cleans_own_temp()
    {
        using var fixture = new StoreFixture();
        var old = await fixture.Store.WriteAsync(fixture.SessionId, new("old\r\nrules", 1, Guid.NewGuid()), CancellationToken.None);
        // Windows sharing denial is a real replace failure; other platforms use a directory collision.
        if (OperatingSystem.IsWindows())
        {
            using var held = File.Open(old.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var error = await Should.ThrowAsync<GrokRulesTransportException>(() =>
                fixture.Store.WriteAsync(fixture.SessionId, new("replacement", 1, Guid.NewGuid()), CancellationToken.None));
            error.Reason.ShouldBe("replace");
        }
        else
        {
            File.Delete(old.Path);
            Directory.CreateDirectory(old.Path);
            (await Should.ThrowAsync<GrokRulesTransportException>(() =>
                fixture.Store.WriteAsync(fixture.SessionId, new("replacement", 1, Guid.NewGuid()), CancellationToken.None)))
                .Reason.ShouldBe("replace");
            Directory.Delete(old.Path);
            await File.WriteAllTextAsync(old.Path, "old\r\nrules");
        }
        (await File.ReadAllTextAsync(old.Path)).ShouldBe("old\r\nrules");
        Directory.GetFiles(Path.GetDirectoryName(old.Path)!, "*.tmp").ShouldBeEmpty();
    }

    [Test]
    public async Task Shared_cwd_sessions_have_isolated_files_and_generations()
    {
        using var fixture = new StoreFixture();
        var second = Guid.NewGuid();
        var receipts = await Task.WhenAll(
            fixture.Store.WriteAsync(fixture.SessionId, new("first", 1, Guid.NewGuid()), CancellationToken.None),
            fixture.Store.WriteAsync(second, new("second", 1, Guid.NewGuid()), CancellationToken.None));
        receipts[0].Path.ShouldNotBe(receipts[1].Path);
        (await File.ReadAllTextAsync(receipts[0].Path)).ShouldBe("first");
        (await File.ReadAllTextAsync(receipts[1].Path)).ShouldBe("second");
    }

    [Test]
    [Arguments("C:\\remote runner café\\sessions\\")]
    [Arguments("/remote runner café/sessions/")]
    [Arguments("\\\\runner\\share\\sessions\\")]
    public void Remote_receipt_paths_and_generated_bootstrap_are_validated_without_local_stat(string root)
    {
        var id = Guid.NewGuid();
        var separator = root.EndsWith('/') ? '/' : '\\';
        var path = root + string.Join(separator, "instructions", "grok", id.ToString("N"), "rules.md");
        var payload = new GrokRulesPayload("body\nrules", 1, Guid.NewGuid());
        var receipt = new GrokRulesReceipt(path, GrokRulesTransport.Hash(Encoding.UTF8.GetBytes(payload.Content)),
            Encoding.UTF8.GetByteCount(payload.Content), 1, payload.Generation);
        GrokRulesTransport.ValidateReceipt(receipt, id, payload, 262144);
        var bootstrap = GrokRulesTransport.Bootstrap(path);
        GrokRulesArgvPolicy.ValidatePayload(bootstrap, true, true).ShouldBeNull();
        bootstrap.ShouldNotContain(payload.Content);
        bootstrap.ShouldNotContain(receipt.Sha256);
        bootstrap.ShouldNotContain(payload.Generation.ToString("N"));
        Should.Throw<GrokRulesTransportException>(() =>
            GrokRulesTransport.ValidateReceipt(receipt with { Generation = Guid.NewGuid() }, id, payload, 262144))
            .Reason.ShouldBe("generation");
        Should.Throw<GrokRulesTransportException>(() =>
            GrokRulesTransport.ValidateReceipt(receipt with { Path = root + ".." + separator + path[root.Length..] }, id, payload, 262144))
            .Reason.ShouldBe("path");
    }

    private sealed class StoreFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "card0395", Guid.NewGuid().ToString("N"));
        public Guid SessionId { get; } = Guid.NewGuid();
        public GrokRulesFileStore Store { get; }
        public StoreFixture() => Store = new(Path.Combine(_root, "runner rules café", "sessions"), new());
        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }
}
