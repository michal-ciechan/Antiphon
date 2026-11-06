using Microsoft.Extensions.DependencyInjection;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Removes opted-out card residue in one repository before a server whole-index commit.
/// A scope keeps the sweep off the singleton git services that call it.
/// </summary>
public sealed class CardFilePreCommitSweep
{
    private static readonly AsyncLocal<bool> Sweeping = new();
    private readonly IServiceScopeFactory? _scopes;
    private readonly Func<string, CancellationToken, Task>? _sweep;

    [ActivatorUtilitiesConstructor]
    public CardFilePreCommitSweep(IServiceScopeFactory scopes) => _scopes = scopes;

    public CardFilePreCommitSweep(Func<string, CancellationToken, Task> sweep) => _sweep = sweep;

    public async Task SweepAsync(string repositoryPath, CancellationToken ct)
    {
        if (Sweeping.Value) return;
        Sweeping.Value = true;
        try
        {
            if (_sweep is not null)
            {
                await _sweep(repositoryPath, ct);
                return;
            }
            await using var scope = _scopes!.CreateAsyncScope();
            var files = scope.ServiceProvider.GetRequiredService<CardTaskFileService>();
            await files.SweepRepositoryAsync(repositoryPath, ct);
        }
        finally { Sweeping.Value = false; }
    }
}
