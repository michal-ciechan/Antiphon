using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

public enum SpecialistEvidenceProvenance { Unverified = 0, Synthetic = 1, RealProvider = 2 }
public sealed record SpecialistExecutionEvidence(string CapabilityFingerprint, string Fingerprint,
    int MaxInputUtf8Bytes, SpecialistEvidenceProvenance Provenance);
public sealed record SpecialistQualificationEvidence(string Fingerprint, SpecialistEvidenceProvenance Provenance,
    IReadOnlyList<Guid> TaskIds, IReadOnlyList<string> EvidenceNonces);
public sealed record SpecialistLaunchEvidence(string Executable, string ExecutableSha256, string PolicyFingerprint,
    SpecialistEvidenceProvenance Provenance);

/// <summary>External CLI/file/evidence verification seam. Test doubles never install production certificates.</summary>
public interface ISpecialistExecutionEvidenceReader
{
    Task<SpecialistExecutionEvidence?> ReadAsync(Agent seat, AgentSession session, CancellationToken ct);
    bool Trusts(SpecialistQualificationEvidence evidence);
}

public sealed class SpecialistExecutionEvidenceReader(IOptions<SupervisionSettings> settings) : ISpecialistExecutionEvidenceReader
{
    // Only a complete V-5 artifact may add an entry. Partial transport or tool-family tests
    // intentionally install nothing; no operator/API flag can bypass this catalog.
    private static readonly IReadOnlyDictionary<string, int> CertifiedEnvelopes = new Dictionary<string, int>();

    public async Task<SpecialistExecutionEvidence?> ReadAsync(Agent seat, AgentSession session, CancellationToken ct)
    {
        if (seat.Kind != AgentKind.ClaudeCode || session.AgentKind != seat.Kind
            || session.SessionBackend != SessionBackend.PtyHost || session.SpecialistLaunchEvidenceJson is null) return null;
        SpecialistLaunchEvidence? launch;
        try { launch = JsonSerializer.Deserialize<SpecialistLaunchEvidence>(session.SpecialistLaunchEvidenceJson); }
        catch (JsonException) { return null; }
        if (launch is null || launch.PolicyFingerprint != PolicyFingerprint) return null;
        var key = launch.ExecutableSha256 + ":" + launch.PolicyFingerprint;
        if (!CertifiedEnvelopes.TryGetValue(key, out var max)) return null;
        try
        {
            await using var stream = File.OpenRead(launch.Executable);
            if (Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)) != launch.ExecutableSha256) return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        var currentFiles = InstructionFileStamps.Compute(seat.WorkingDirectory,
            settings.Value.PolicyRefresh.InstructionFiles ?? PolicyRefreshSettings.DefaultInstructionFiles).StampLine;
        var fingerprint = Hash(JsonSerializer.Serialize(new
        {
            seat.Id, seat.Kind, seat.ModelLevel, seat.ModelId, seat.TuiProfileId, seat.LaunchEnvJson,
            seat.SystemPromptAppend, seat.ReplyStyle, seat.WorkingDirectory, currentFiles,
            sessionId = session.Id, session.StartedAt, session.TuiProfileRevisionId, session.EffectiveModelId,
            session.ComposedBundleStamp, session.InstructionFileStamp, session.SpecialistLaunchEvidenceJson,
            contract = CheckInterpretation.Contract,
        }));
        return new(Hash(key), fingerprint, max, launch.Provenance);
    }

    public bool Trusts(SpecialistQualificationEvidence evidence) => evidence.Provenance == SpecialistEvidenceProvenance.RealProvider
        && !string.IsNullOrWhiteSpace(evidence.Fingerprint) && evidence.TaskIds.Count == 2
        && evidence.TaskIds.Distinct().Count() == 2 && evidence.TaskIds.All(t => t != Guid.Empty)
        && evidence.EvidenceNonces.Count == 2 && evidence.EvidenceNonces.Distinct().Count() == 2
        && evidence.EvidenceNonces.All(n => !string.IsNullOrWhiteSpace(n));

    public static string PolicyFingerprint => Hash("claude-check-isolated-settings-mcp-v1\n" + CheckInterpretation.DenyAllToolsSettingsJson);
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    public static string? CaptureLaunch(AgentLaunchSpec launch)
    {
        if (launch.Kind != AgentKind.ClaudeCode || !File.Exists(launch.Exe)
            || !Path.GetFileName(launch.Exe).Equals("claude.exe", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            using var stream = File.OpenRead(launch.Exe);
            var hash = Convert.ToHexString(SHA256.HashData(stream));
            var provenance = SpecialistEvidenceProvenance.RealProvider;
            if (launch.Env.TryGetValue("ANTHROPIC_BASE_URL", out var url) && !string.IsNullOrWhiteSpace(url))
                provenance = Uri.TryCreate(url, UriKind.Absolute, out var parsed) && parsed.Host == "api.anthropic.com"
                    ? SpecialistEvidenceProvenance.RealProvider : SpecialistEvidenceProvenance.Synthetic;
            return JsonSerializer.Serialize(new SpecialistLaunchEvidence(launch.Exe, hash, PolicyFingerprint, provenance));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}
