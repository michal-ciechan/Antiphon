using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Shouldly;
using TUnit.Core;
using Antiphon.Server.Infrastructure.Agents.Tui;
using Antiphon.Server.Infrastructure.Security;

namespace Antiphon.Tests.ApiKeys;

/// <summary>
/// CARD-0106 S1 — the API key protector. It reuses the agent-TUI key ring and readiness handshake
/// wholesale; the only thing that is genuinely new is the purpose chain, and the one property that
/// chain exists to buy is RENAME SAFETY. So that is what these pin, along with the readiness
/// re-check that makes the whole store fail closed rather than silently plaintext.
/// </summary>
[Category("Unit")]
public class ApiKeyProtectorTests
{
    [Test]
    public void a_value_round_trips_and_the_ciphertext_never_contains_it()
    {
        var sut = NewReadyProtector();
        var keyId = Guid.NewGuid();

        var cipher = sut.Protect(keyId, "canary-secret-value");

        cipher.ShouldNotContain("canary-secret-value");
        sut.Unprotect(keyId, cipher).ShouldBe("canary-secret-value");
    }

    [Test]
    public void the_purpose_chain_is_keyed_on_the_row_id_so_a_rename_cannot_orphan_a_ciphertext()
    {
        // The whole reason the chain is not keyed on the NAME (as the TUI protector's is): renaming
        // a key is an operator typing in a text box, and it must not silently destroy the value.
        // Nothing about the name reaches the protector at all, so a rename is a no-op to it.
        var sut = NewReadyProtector();
        var keyId = Guid.NewGuid();

        var cipher = sut.Protect(keyId, "anthropic-token");

        sut.Unprotect(keyId, cipher).ShouldBe("anthropic-token");
    }

    [Test]
    public void one_keys_ciphertext_does_not_decrypt_under_another_keys_id()
    {
        var sut = NewReadyProtector();
        var mine = Guid.NewGuid();

        var cipher = sut.Protect(mine, "mine-only");

        Should.Throw<CryptographicException>(() => sut.Unprotect(Guid.NewGuid(), cipher));
    }

    [Test]
    public void an_unsaved_row_is_refused_rather_than_sharing_the_empty_guid_purpose()
    {
        // Guid.Empty would give every not-yet-persisted key the SAME purpose chain, so one row's
        // ciphertext would decrypt under another's. Refused loudly instead.
        var sut = NewReadyProtector();

        Should.Throw<CryptographicException>(() => sut.Protect(Guid.Empty, "x"));
        Should.Throw<CryptographicException>(() => sut.Unprotect(Guid.Empty, "x"));
    }

    [Test]
    public void readiness_is_rechecked_for_every_operation_not_once_at_startup()
    {
        var ready = true;
        var readiness = new AgentTuiKeyProtectionReadiness(() => ready);
        var sut = new DataProtectionApiKeyProtector(new EphemeralDataProtectionProvider(), readiness);
        var keyId = Guid.NewGuid();
        var cipher = sut.Protect(keyId, "dynamic-secret");

        ready = false;

        Should.Throw<CryptographicException>(() => sut.Protect(keyId, "rejected"));
        Should.Throw<CryptographicException>(() => sut.Unprotect(keyId, cipher));
    }

    [Test]
    public void a_payload_that_is_not_a_data_protection_blob_is_a_cryptographic_failure()
    {
        var sut = NewReadyProtector();

        Should.Throw<CryptographicException>(() => sut.Unprotect(Guid.NewGuid(), "not-a-payload"));
        Should.Throw<CryptographicException>(() =>
            sut.Unprotect(Guid.NewGuid(), new string('!', 64)));
    }

    private static DataProtectionApiKeyProtector NewReadyProtector() =>
        new(new EphemeralDataProtectionProvider(), new AgentTuiKeyProtectionReadiness(() => true));
}
