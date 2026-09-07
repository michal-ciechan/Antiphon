using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Git;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Antiphon.Tests.TestHelpers;

internal sealed class CardFileTestRepository : ICardFileRepository
{
    private readonly CardFileRepository _inner = new(new GitProcessGate(), Options.Create(new GitSettings()), NullLogger<CardFileRepository>.Instance);
    public Func<string, IReadOnlyDictionary<string, string?>, CancellationToken, Task>? BeforeCommit { get; init; }
    public Func<Task>? BeforeWrite { get; init; }
    public Func<Task>? AfterWrite { get; init; }
    public Func<Task>? BeforeInstall { get; init; }
    public Action? AfterDelete { get; init; }
    public Action? AfterPin { get; init; }
    public void ValidatePath(string r, string p) => _inner.ValidatePath(r, p);
    public Task<IReadOnlyList<string>> GetManagedGitPathsAsync(string r, string d, CancellationToken ct) => _inner.GetManagedGitPathsAsync(r, d, ct);
    public async Task<CardFileCommitResult> CommitAsync(string r, string d, IReadOnlyDictionary<string, string?> expected, string subject, CancellationToken ct) { if (BeforeCommit is not null) await BeforeCommit(r, expected, ct); return await _inner.CommitAsync(r, d, expected, subject, ct); }
    public Task<CardFileRepositoryState> InspectAsync(string r, string d, CancellationToken ct) => _inner.InspectAsync(r, d, ct);
    public Task<string> HashAsync(string r, string p, string b, CancellationToken ct) => _inner.HashAsync(r, p, b, ct);
    public Task UnstageAsync(string r, string d, IReadOnlyList<string> p, CancellationToken ct) => _inner.UnstageAsync(r, d, p, ct);
    public Task<bool> IsIgnoredAsync(string r, IReadOnlyList<string> p, CancellationToken ct) => _inner.IsIgnoredAsync(r, p, ct);
    public Task<bool> HasIgnoreProtectionAsync(string r, IReadOnlyList<string> s, CancellationToken ct) => _inner.HasIgnoreProtectionAsync(r, s, ct);
    public async Task InstallIgnoreAsync(string r, CancellationToken ct) { if (BeforeInstall is not null) await BeforeInstall(); await _inner.InstallIgnoreAsync(r, ct); }
    public Task<string?> ReadAsync(string r, string p, CancellationToken ct) => _inner.ReadAsync(r, p, ct);
    public void Delete(string r, string p) { _inner.Delete(r, p); AfterDelete?.Invoke(); }
    public async Task WriteAsync(string r, string p, string b, Guid id, CancellationToken ct) { if (BeforeWrite is not null) await BeforeWrite(); await _inner.WriteAsync(r, p, b, id, ct); if (AfterWrite is not null) await AfterWrite(); }
    public void RemoveTemporaryFiles(string r, string d, Guid id) { _inner.RemoveTemporaryFiles(r, d, id); AfterPin?.Invoke(); }
}
