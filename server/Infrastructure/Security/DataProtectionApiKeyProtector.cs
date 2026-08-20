using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Infrastructure.Agents.Tui;

namespace Antiphon.Server.Infrastructure.Security;

/// <summary>
/// API key protection over the EXISTING agent-TUI DataProtection key ring (CARD-0106 S1): same
/// provider, same DPAPI/certificate-protected ring outside the content root, same owner-only ACLs,
/// same <see cref="AgentTuiKeyProtectionReadiness"/> handshake before and after every operation.
/// A second key ring would be a second thing to protect, back up and get wrong.
///
/// <para>The only new thing is the purpose chain, and it is keyed on the row's own id — so a key
/// renamed from <c>anthropic</c> to <c>anthropic-maven</c> still decrypts, where the TUI
/// protector's name-keyed chain would have orphaned it.</para>
/// </summary>
public sealed class DataProtectionApiKeyProtector : IApiKeyProtector
{
    private const string NotReadyMessage = "API key protection is not ready.";
    private const string InvalidPayloadMessage = "API key payload is invalid.";

    private readonly IDataProtectionProvider _provider;
    private readonly AgentTuiKeyProtectionReadiness _readiness;

    public DataProtectionApiKeyProtector(
        IDataProtectionProvider provider,
        AgentTuiKeyProtectionReadiness readiness)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
    }

    public string Protect(Guid keyId, string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        var protectedValue = For(keyId).Protect(plaintext);
        EnsureReadyAfterOperation(DataProtectionPayload.GetKeyId(protectedValue, InvalidPayloadMessage));
        return protectedValue;
    }

    public string Unprotect(Guid keyId, string protectedValue)
    {
        ArgumentNullException.ThrowIfNull(protectedValue);
        var ringKeyId = DataProtectionPayload.GetKeyId(protectedValue, InvalidPayloadMessage);
        var plaintext = For(keyId, ringKeyId).Unprotect(protectedValue);
        EnsureReadyAfterOperation(ringKeyId);
        return plaintext;
    }

    private IDataProtector For(Guid keyId, Guid? requiredRingKeyId = null)
    {
        if (keyId == Guid.Empty)
        {
            // An empty id would give every unsaved key the same purpose chain, so one row's
            // ciphertext would decrypt under another's. Refused rather than silently shared.
            throw new CryptographicException("API key protection requires a persisted key id.");
        }

        var isReady = requiredRingKeyId.HasValue
            ? _readiness.RevalidateAndObserveBeforeOperation(requiredRingKeyId.Value)
            : _readiness.RevalidateAndObserveBeforeOperation();
        if (!isReady)
            throw new CryptographicException(NotReadyMessage);

        return _provider.CreateProtector("Antiphon", "ApiKey", keyId.ToString("D"));
    }

    private void EnsureReadyAfterOperation(Guid requiredRingKeyId)
    {
        if (!_readiness.RevalidateAndObserveAfterOperation(requiredRingKeyId))
            throw new CryptographicException(NotReadyMessage);
    }
}
