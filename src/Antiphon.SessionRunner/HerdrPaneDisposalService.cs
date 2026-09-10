using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;

namespace Antiphon.SessionRunner;

/// <summary>
/// Read-only preview and durable refusal increment for CARD-0461. There is intentionally no
/// teardown call here: protocol 20 cannot fence external process creation at teardown. An
/// installed CLI version, pane revision or runner lock cannot supply that missing guarantee.
/// Full process identity, acquisition leases and guarded execution remain a backend-dependent
/// implementation prerequisite. No preview from this implementation is executable.
/// </summary>
public sealed class HerdrPaneDisposalService
{
    private readonly HerdrClient _client;
    private readonly SessionRunnerRuntime _runtime;
    private readonly TimeProvider _time;
    private readonly string _receiptDirectory;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _storeGate = new(1, 1);
    private readonly Dictionary<Guid, HerdrPaneDisposalPreview> _previews = [];

    public HerdrPaneDisposalService(HerdrClient client, SessionRunnerRuntime runtime,
        IOptions<SessionRunnerSettings> settings, TimeProvider time)
    {
        _client = client;
        _runtime = runtime;
        _time = time;
        _receiptDirectory = Path.Combine(settings.Value.SessionLogPath, "herdr", "disposals");
    }

    public async Task<HerdrPaneDisposalPreview> PreviewAsync(
        HerdrPaneDisposalPreviewRequest request, CancellationToken cancellationToken)
    {
        Validate(request);
        var backend = await _client.ConnectAndValidateAsync(cancellationToken);
        HerdrPaneInfo pane;
        try { pane = await _client.PaneGetAsync(request.PaneId, cancellationToken); }
        catch (HerdrApiException ex) when (ex.Code is "pane_not_found" or "not_found")
        { throw new HerdrLaunchException("The selected pane does not exist.", HerdrProblemTypes.PaneNotFound); }
        if (pane.PaneId != request.PaneId)
            throw new HerdrLaunchException("The backend resolved a different pane.", HerdrProblemTypes.PaneChanged);

        var processes = await _client.PaneProcessInfoAsync(request.PaneId, cancellationToken);
        if (processes.PaneId != request.PaneId)
            throw new HerdrLaunchException("The backend returned a different process target.", HerdrProblemTypes.PaneChanged);
        var workspaces = await _client.WorkspaceListAsync(cancellationToken);
        var tabs = await _client.TabListAsync(pane.WorkspaceId, cancellationToken);
        var claims = _runtime.InspectHerdrDisposalClaims(request.PaneId).ToList();
        if (pane.Tokens?.TryGetValue("antiphon-session", out var token) == true
            && Guid.TryParseExact(token, "D", out var tokenId) && tokenId != Guid.Empty)
            claims.Add(new(tokenId, "antiphon-session-token", null, false));
        var blockers = new List<string>
        {
            HerdrPaneDisposalCodes.GuardUnavailable,
            // Protocol 20 does not provide a complete affected process set or an incarnation-
            // bound native observation. Never classify a missing foreground list as idle.
            HerdrPaneDisposalCodes.IdentityUnproven,
        };
        if (claims.Any(c => c.Live)) blockers.Add(HerdrProblemTypes.PaneBound);
        if (request.ExpectedSessionId is { } expected
            && (claims.Count == 0 || claims.Any(c => c.SessionId != expected)))
            blockers.Add("herdr_pane_association_unproven");

        var now = _time.GetUtcNow();
        var tab = tabs.SingleOrDefault(t => t.TabId == pane.TabId);
        var preview = new HerdrPaneDisposalPreview(
            Guid.NewGuid(), now.AddMinutes(2), pane.PaneId,
            request.ExpectedSessionId, request.ExpectedNativeSessionId,
            pane.WorkspaceId, pane.TabId, pane.TerminalId,
            workspaces.SingleOrDefault(w => w.WorkspaceId == pane.WorkspaceId)?.Label,
            tab?.Label, pane.Label, backend.Version, backend.Protocol,
            processes.ShellPid,
            processes.ForegroundProcesses?.Select(p => new HerdrPaneDisposalProcess(
                p.Pid, SafeExecutableName(p.Name))).ToArray(),
            claims.ToArray(), tab is null ? null : tab.PaneCount == 1,
            Eligible: false, GuardAvailable: false, ProcessInventoryComplete: false,
            Blockers: blockers.ToArray());
        await _storeGate.WaitAsync(cancellationToken);
        try
        {
            foreach (var id in _previews.Where(p => p.Value.ExpiresAtUtc <= now).Select(p => p.Key).ToArray())
                _previews.Remove(id);
            if (_previews.Count >= 256)
                _previews.Remove(_previews.MinBy(p => p.Value.ExpiresAtUtc).Key);
            _previews.Add(preview.PreviewId, preview);
            // Do not return the store's mutable array instances to in-process callers.
            return JsonSerializer.Deserialize<HerdrPaneDisposalPreview>(JsonSerializer.Serialize(preview, _json), _json)!;
        }
        finally { _storeGate.Release(); }
    }

    public async Task<HerdrPaneDisposalReceipt> ExecuteAsync(
        HerdrPaneDisposalRequest request, CancellationToken cancellationToken)
    {
        if (request.OperationId == Guid.Empty || request.PreviewId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 4096)
            throw new ArgumentException("Nonempty operationId/previewId and a reason of 1-4096 characters are required.");
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request, _json)));
        await _storeGate.WaitAsync(cancellationToken);
        try
        {
            var existing = ReadStored(request.OperationId);
            if (existing is not null)
            {
                if (existing.Fingerprint != fingerprint)
                    throw new HerdrLaunchException("Operation ID already belongs to a different request.",
                        HerdrPaneDisposalCodes.OperationConflict);
                return existing.Receipt;
            }
            if (!_previews.TryGetValue(request.PreviewId, out var preview)
                || preview.ExpiresAtUtc <= _time.GetUtcNow())
                throw new HerdrLaunchException("Preview is unknown, expired or belongs to a previous runner instance.",
                    HerdrPaneDisposalCodes.PreviewInvalid);

            // Even the most convincing preview cannot authorize an unconditional close. No
            // backend RPC, process kill, session transition or locator cleanup occurs here.
            var receipt = new HerdrPaneDisposalReceipt(request.OperationId, request.PreviewId,
                preview.PaneId, "GuardUnavailable", HerdrPaneDisposalCodes.GuardUnavailable,
                _time.GetUtcNow(), PaneLeftOpen: null, CleanupPending: false);
            SaveStored(new(fingerprint, receipt));
            return receipt;
        }
        finally { _storeGate.Release(); }
    }

    public async Task<HerdrPaneDisposalReceipt?> GetAsync(Guid operationId, CancellationToken cancellationToken)
    {
        if (operationId == Guid.Empty) throw new ArgumentException("A nonempty operationId is required.");
        await _storeGate.WaitAsync(cancellationToken);
        try { return ReadStored(operationId)?.Receipt; }
        finally { _storeGate.Release(); }
    }

    private StoredRefusal? ReadStored(Guid id)
    {
        var path = Path.Combine(_receiptDirectory, $"{id:N}.json");
        try
        {
            var stored = JsonSerializer.Deserialize<StoredRefusal>(File.ReadAllText(path), _json)
                ?? throw new IOException("Disposal receipt is unreadable.");
            if (stored.Receipt.OperationId != id || stored.Receipt.Outcome != "GuardUnavailable")
                throw new IOException("Disposal receipt identity or outcome is unsupported.");
            return stored;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    private void SaveStored(StoredRefusal stored)
    {
        Directory.CreateDirectory(_receiptDirectory);
        var path = Path.Combine(_receiptDirectory, $"{stored.Receipt.OperationId:N}.json");
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(JsonSerializer.SerializeToUtf8Bytes(stored, _json));
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: false);
        }
        finally { File.Delete(temp); }
    }

    internal static void Validate(HerdrPaneDisposalPreviewRequest request)
    {
        if (request.PaneId is null || request.PaneId.Length > 128
            || !Regex.IsMatch(request.PaneId, @"\Aw[0-9A-Za-z]+:p[0-9A-Za-z]+\z"))
            throw new ArgumentException("paneId must be one exact workspace-qualified pane ID.");
        if ((request.ExpectedSessionId is null && request.ExpectedNativeSessionId is null)
            || request.ExpectedSessionId == Guid.Empty || request.ExpectedNativeSessionId == Guid.Empty)
            throw new ArgumentException("At least one full nonempty expected session or native UUID is required.");
    }

    private static string? SafeExecutableName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var leaf = name.Replace('\\', '/').Split('/')[^1];
        return Regex.IsMatch(leaf, @"\A[A-Za-z0-9_.-]{1,128}\z") ? leaf : null;
    }

    private sealed record StoredRefusal(string Fingerprint, HerdrPaneDisposalReceipt Receipt);
}
