using System.Security.Cryptography;
using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>CARD-0499. Bounded progress observation; fetch/pin run only under the repository lease.</summary>
public class TaskProgressGit : LandingGit, ITaskProgressGit
{
    public const string ProgressRefPrefix = "refs/antiphon/progress/";
    private readonly IRepositoryMutationLease? _leases;

    public TaskProgressGit(IRepositoryMutationLease? leases = null) => _leases = leases;

    public async Task<ProgressRevParse> RevParseCommitAsync(string repository, string revision, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var spec = revision.EndsWith("^{commit}", StringComparison.Ordinal) ? revision : revision + "^{commit}";
        var result = await RunAsync(repository, ["rev-parse", "--verify", spec], ct);
        if (!result.Succeeded)
            return new(false, null, result.ExitCode == 128 ? "repair_source_identity_unavailable" : "rev_parse_failed");
        var sha = result.Output.Trim();
        return GitObjectId.IsFull(sha) ? new(true, sha, null) : new(false, null, "invalid_commit");
    }

    public async Task<ProgressSymbolicHead> SymbolicHeadAsync(string repository, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var result = await RunAsync(repository, ["symbolic-ref", "-q", "HEAD"], ct);
        if (result.ExitCode == 1) return new(true, null, "detached_head");
        if (!result.Succeeded) return new(false, null, "symbolic_head_error");
        return new(true, result.Output.Trim(), null);
    }

    public async Task<bool> HasOriginAsync(string repository, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var result = await RunAsync(repository, ["remote", "get-url", "origin"], ct);
        return result.Succeeded && result.Output.Trim().Length > 0;
    }

    public async Task<string?> EndpointFingerprintAsync(string repository, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var endpoint = await ReadEndpointAsync(repository, ct);
            return Fingerprint(endpoint);
        }
        catch (IOException)
        {
            return null;
        }
    }

    public async Task<ProgressRemoteObservation> ObserveExactRefAsync(
        string repository, string fullRef, string? expectedFingerprint, Guid taskId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!fullRef.StartsWith("refs/heads/", StringComparison.Ordinal))
            return new(ProgressRemoteState.Unavailable, Reason: "invalid_observation_identity");

        if (!await HasOriginAsync(repository, ct))
            return new(ProgressRemoteState.NotConfigured);

        string endpoint;
        string fingerprint;
        try
        {
            endpoint = await ReadEndpointAsync(repository, ct);
            fingerprint = Fingerprint(endpoint);
        }
        catch (IOException)
        {
            return new(ProgressRemoteState.Unavailable, Reason: "source_remote_endpoint_ambiguous");
        }

        if (expectedFingerprint is { Length: > 0 }
            && !string.Equals(expectedFingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
            return new(ProgressRemoteState.Unavailable, EndpointFingerprint: fingerprint, Reason: "source_remote_endpoint_changed");

        for (var attempt = 0; attempt < 3; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                endpoint = await ReadEndpointAsync(repository, ct);
                fingerprint = Fingerprint(endpoint);
            }
            catch (IOException)
            {
                return new(ProgressRemoteState.Unavailable, Reason: "source_remote_endpoint_ambiguous");
            }

            if (expectedFingerprint is { Length: > 0 }
                && !string.Equals(expectedFingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
                return new(ProgressRemoteState.Unavailable, EndpointFingerprint: fingerprint, Reason: "source_remote_endpoint_changed");

            var read = await RunAsync(repository, ["ls-remote", "--refs", "--exit-code", endpoint, fullRef], ct);
            if (read.ExitCode == 2)
                return new(ProgressRemoteState.Missing, EndpointFingerprint: fingerprint);
            if (!read.Succeeded)
                return new(ProgressRemoteState.Unavailable, EndpointFingerprint: fingerprint, Reason: "source_remote_unreadable");

            var lines = read.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var fields = lines.Length == 1 ? lines[0].TrimEnd('\r').Split('\t') : [];
            if (fields.Length != 2 || fields[1] != fullRef || !GitObjectId.IsFull(fields[0]))
                return new(ProgressRemoteState.Unavailable, EndpointFingerprint: fingerprint, Reason: "source_remote_response_invalid");

            var advertised = fields[0];
            var local = await RunAsync(repository, ["cat-file", "-e", advertised + "^{commit}"], ct);
            if (local.Succeeded)
                return new(ProgressRemoteState.Present, advertised, fingerprint);

            if (_leases is null)
                return new(ProgressRemoteState.Unavailable, EndpointFingerprint: fingerprint, Reason: "repository_lease_busy");

            await using var lease = await _leases.TryAcquireAsync(repository, ct);
            if (lease is null)
                return new(ProgressRemoteState.Unavailable, EndpointFingerprint: fingerprint, Reason: "repository_lease_busy");

            var pin = $"{ProgressRefPrefix}{taskId:N}/observe-{ShaSuffix(fullRef)}";
            var check = await RunAsync(repository, ["check-ref-format", pin], ct);
            if (!check.Succeeded)
                return new(ProgressRemoteState.Unavailable, EndpointFingerprint: fingerprint, Reason: "invalid_observation_ref");

            var fetch = await RunAsync(repository,
                ["fetch", "--no-tags", "--no-write-fetch-head", endpoint, $"{fullRef}:{pin}"], ct);
            if (!fetch.Succeeded)
                return new(ProgressRemoteState.Unavailable, EndpointFingerprint: fingerprint, Reason: "source_remote_fetch_failed");

            var observed = await RevParseCommitAsync(repository, pin, ct);
            if (!observed.Succeeded || observed.Sha != advertised)
                continue;

            try
            {
                var after = Fingerprint(await ReadEndpointAsync(repository, ct));
                if (!string.Equals(after, fingerprint, StringComparison.OrdinalIgnoreCase))
                    return new(ProgressRemoteState.Unavailable, EndpointFingerprint: fingerprint, Reason: "source_remote_endpoint_changed");
            }
            catch (IOException)
            {
                return new(ProgressRemoteState.Unavailable, EndpointFingerprint: fingerprint, Reason: "source_remote_endpoint_ambiguous");
            }

            return new(ProgressRemoteState.Present, advertised, fingerprint, ObservationRef: pin);
        }

        return new(ProgressRemoteState.Unavailable, Reason: "changed_during_confirmation");
    }

    public async Task<bool?> IsAncestorAsync(string repository, string ancestorSha, string descendantSha, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!GitObjectId.IsFull(ancestorSha) || !GitObjectId.IsFull(descendantSha))
            return null;
        var result = await RunAsync(repository, ["merge-base", "--is-ancestor", ancestorSha, descendantSha], ct);
        return result.ExitCode switch
        {
            0 => true,
            1 => false,
            _ => null,
        };
    }

    public async Task<ProgressPinResult> PinBaselineAsync(string repository, Guid taskId, string name, string sha, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!GitObjectId.IsFull(sha))
            return new(false, Reason: "invalid_commit");
        var pin = $"{ProgressRefPrefix}{taskId:N}/baseline-{name}";
        var valid = await RunAsync(repository, ["check-ref-format", pin], ct);
        if (!valid.Succeeded) return new(false, Reason: "invalid_observation_ref");

        if (_leases is null)
            return new(false, Reason: "repository_lease_busy");
        await using var lease = await _leases.TryAcquireAsync(repository, ct);
        if (lease is null)
            return new(false, Reason: "repository_lease_busy");

        var existing = await RunAsync(repository, ["show-ref", "--verify", "--hash", pin], ct);
        if (existing.Succeeded)
            return existing.Output.Trim() == sha ? new(true, pin) : new(false, pin, "recovery_ref_collision");

        var update = await RunAsync(repository, ["update-ref", pin, sha, new string('0', sha.Length)], ct);
        return update.Succeeded ? new(true, pin) : new(false, pin, "pin_failed");
    }

    public async Task<IReadOnlyList<string>> ListProgressPinsAsync(string repository, Guid taskId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var prefix = $"{ProgressRefPrefix}{taskId:N}/";
        var result = await RunAsync(repository, ["for-each-ref", "--format=%(refname)", prefix], ct);
        if (!result.Succeeded) return [];
        return result.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
    }

    private async Task<string> ReadEndpointAsync(string repository, CancellationToken ct)
    {
        var result = await RunAsync(repository, ["remote", "get-url", "--push", "--all", "origin"], ct);
        if (!result.Succeeded) throw new IOException("source_remote_endpoint_ambiguous");
        var endpoints = result.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (endpoints.Length != 1 || endpoints[0].StartsWith('-')) throw new IOException("source_remote_endpoint_ambiguous");
        return endpoints[0];
    }

    private static string Fingerprint(string endpoint) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint)));

    private static string ShaSuffix(string fullRef)
    {
        var hex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullRef)));
        return hex[..12].ToLowerInvariant();
    }
}
