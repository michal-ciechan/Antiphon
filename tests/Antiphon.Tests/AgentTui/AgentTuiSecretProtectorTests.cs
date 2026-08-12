using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.Tui;

namespace Antiphon.Tests.AgentTui;

[Category("Unit")]
public class AgentTuiSecretProtectorTests
{
    private const string ApplicationName = "Antiphon.AgentTui.Tests";

    [Test]
    public void Protect_is_profile_and_environment_purpose_isolated()
    {
        var provider = new EphemeralDataProtectionProvider();
        var sut = NewReadyProtector(provider);
        var profile = Guid.NewGuid();

        var cipher = sut.Protect(profile, "OPENAI_API_KEY", "canary-secret");

        cipher.ShouldNotContain("canary-secret");
        sut.Unprotect(profile, "OPENAI_API_KEY", cipher).ShouldBe("canary-secret");
        Should.Throw<CryptographicException>(() =>
            sut.Unprotect(Guid.NewGuid(), "OPENAI_API_KEY", cipher));
        Should.Throw<CryptographicException>(() =>
            sut.Unprotect(profile, "OTHER_KEY", cipher));
    }

    [Test]
    public void Resolved_protector_rechecks_readiness_for_every_operation()
    {
        var ready = true;
        var readiness = new AgentTuiKeyProtectionReadiness(() => ready);
        var sut = new DataProtectionAgentTuiSecretProtector(
            new EphemeralDataProtectionProvider(),
            readiness);
        var profileId = Guid.NewGuid();
        var cipher = sut.Protect(profileId, "OPENAI_API_KEY", "dynamic-secret");

        ready = false;

        readiness.IsReady.ShouldBeFalse();
        Should.Throw<CryptographicException>(() =>
            sut.Protect(profileId, "OPENAI_API_KEY", "rejected-secret"));
        Should.Throw<CryptographicException>(() =>
            sut.Unprotect(profileId, "OPENAI_API_KEY", cipher));
    }

    [Test]
    public void Protect_observes_new_key_files_before_cryptography()
    {
        var root = NewTempPath();
        var keyRingPath = Path.Combine(root, "keys");
        var contentRootPath = Path.Combine(root, "repository");
        var certificatePath = Path.Combine(root, "key-protection.pfx");
        var sourceKeyRingPath = Path.Combine(root, "source-keys");
        var newKeyPath = Path.Combine(keyRingPath, "new-key.xml");
        try
        {
            Directory.CreateDirectory(contentRootPath);
            WriteTestCertificate(certificatePath);
            var services = new ServiceCollection();
            AgentTuiDataProtectionSetup.Configure(
                services,
                CertificateSettings(certificatePath),
                CurrentPlatform(),
                keyRingPath,
                contentRootPath).ShouldBeTrue();
            using var provider = services.BuildServiceProvider();
            var readiness = provider.GetRequiredService<AgentTuiKeyProtectionReadiness>();
            var sourceServices = new ServiceCollection();
            AgentTuiDataProtectionSetup.Configure(
                sourceServices,
                CertificateSettings(certificatePath),
                CurrentPlatform(),
                sourceKeyRingPath,
                contentRootPath).ShouldBeTrue();
            using (var sourceProvider = sourceServices.BuildServiceProvider())
            {
                sourceProvider.GetRequiredService<IAgentTuiSecretProtector>()
                    .Protect(Guid.NewGuid(), "OPENAI_API_KEY", "source-key-secret");
            }
            File.Copy(
                Directory.GetFiles(sourceKeyRingPath, "key-*.xml").ShouldHaveSingleItem(),
                newKeyPath);
            MakePersistedKeyFileOwnerOnly(newKeyPath);
            readiness.IsReady.ShouldBeTrue();
            var callbackProvider = new CallbackDataProtectionProvider(
                new EphemeralDataProtectionProvider(),
                () => File.Delete(newKeyPath));
            var sut = new DataProtectionAgentTuiSecretProtector(
                callbackProvider,
                readiness);

            Should.Throw<CryptographicException>(() =>
                sut.Protect(Guid.NewGuid(), "OPENAI_API_KEY", "rejected-secret"));
            File.Exists(newKeyPath).ShouldBeFalse();
            readiness.IsReady.ShouldBeFalse();
        }
        finally
        {
            DeleteTempPath(root);
        }
    }

    [Test]
    public void Protect_rejects_first_key_created_and_deleted_during_cryptography()
    {
        var root = NewTempPath();
        var keyRingPath = Path.Combine(root, "keys");
        var contentRootPath = Path.Combine(root, "repository");
        var certificatePath = Path.Combine(root, "key-protection.pfx");
        var newKeyPath = Path.Combine(keyRingPath, "first-key.xml");
        try
        {
            Directory.CreateDirectory(contentRootPath);
            WriteTestCertificate(certificatePath);
            var services = new ServiceCollection();
            AgentTuiDataProtectionSetup.Configure(
                services,
                CertificateSettings(certificatePath),
                CurrentPlatform(),
                keyRingPath,
                contentRootPath).ShouldBeTrue();
            using var provider = services.BuildServiceProvider();
            var callbackProvider = new CallbackDataProtectionProvider(
                new EphemeralDataProtectionProvider(),
                () =>
                {
                    File.WriteAllText(newKeyPath, "<key />");
                    File.Delete(newKeyPath);
                });
            var sut = new DataProtectionAgentTuiSecretProtector(
                callbackProvider,
                provider.GetRequiredService<AgentTuiKeyProtectionReadiness>());

            Should.Throw<CryptographicException>(() =>
                sut.Protect(Guid.NewGuid(), "OPENAI_API_KEY", "rejected-secret"));
            File.Exists(newKeyPath).ShouldBeFalse();
        }
        finally
        {
            DeleteTempPath(root);
        }
    }

    [Test]
    public void Protect_rejects_payload_whose_key_appears_and_disappears_during_cryptography()
    {
        var root = NewTempPath();
        var keyRingPath = Path.Combine(root, "keys");
        var sourceKeyRingPath = Path.Combine(root, "source-keys");
        var contentRootPath = Path.Combine(root, "repository");
        var certificatePath = Path.Combine(root, "key-protection.pfx");
        var transientKeyPath = Path.Combine(keyRingPath, "transient-key.xml");
        try
        {
            Directory.CreateDirectory(contentRootPath);
            WriteTestCertificate(certificatePath);
            var profileId = Guid.NewGuid();
            var services = new ServiceCollection();
            AgentTuiDataProtectionSetup.Configure(
                services,
                CertificateSettings(certificatePath),
                CurrentPlatform(),
                keyRingPath,
                contentRootPath).ShouldBeTrue();
            using var provider = services.BuildServiceProvider();
            provider.GetRequiredService<IAgentTuiSecretProtector>()
                .Protect(profileId, "OPENAI_API_KEY", "stable-key-secret");

            var sourceServices = new ServiceCollection();
            AgentTuiDataProtectionSetup.Configure(
                sourceServices,
                CertificateSettings(certificatePath),
                CurrentPlatform(),
                sourceKeyRingPath,
                contentRootPath).ShouldBeTrue();
            using var sourceProvider = sourceServices.BuildServiceProvider();
            sourceProvider.GetRequiredService<IAgentTuiSecretProtector>()
                .Protect(profileId, "OPENAI_API_KEY", "transient-key-seed");
            var sourceKeyPath = Directory.GetFiles(sourceKeyRingPath, "key-*.xml")
                .ShouldHaveSingleItem();
            var callbackProvider = new CallbackDataProtectionProvider(
                sourceProvider.GetRequiredService<IDataProtectionProvider>(),
                beforeProtect: () => File.Copy(sourceKeyPath, transientKeyPath),
                afterProtect: () => File.Delete(transientKeyPath));
            var sut = new DataProtectionAgentTuiSecretProtector(
                callbackProvider,
                provider.GetRequiredService<AgentTuiKeyProtectionReadiness>());

            Should.Throw<CryptographicException>(() =>
                sut.Protect(profileId, "OPENAI_API_KEY", "rejected-secret"));
            File.Exists(transientKeyPath).ShouldBeFalse();
        }
        finally
        {
            DeleteTempPath(root);
        }
    }

    [Test]
    public void Unprotect_rejects_payload_whose_key_only_exists_during_cryptography()
    {
        var root = NewTempPath();
        var keyRingPath = Path.Combine(root, "keys");
        var sourceKeyRingPath = Path.Combine(root, "source-keys");
        var contentRootPath = Path.Combine(root, "repository");
        var certificatePath = Path.Combine(root, "key-protection.pfx");
        var transientKeyPath = Path.Combine(keyRingPath, "transient-key.xml");
        try
        {
            Directory.CreateDirectory(contentRootPath);
            WriteTestCertificate(certificatePath);
            var profileId = Guid.NewGuid();
            var services = new ServiceCollection();
            AgentTuiDataProtectionSetup.Configure(
                services,
                CertificateSettings(certificatePath),
                CurrentPlatform(),
                keyRingPath,
                contentRootPath).ShouldBeTrue();
            using var provider = services.BuildServiceProvider();
            provider.GetRequiredService<IAgentTuiSecretProtector>()
                .Protect(profileId, "OPENAI_API_KEY", "stable-key-secret");

            var sourceServices = new ServiceCollection();
            AgentTuiDataProtectionSetup.Configure(
                sourceServices,
                CertificateSettings(certificatePath),
                CurrentPlatform(),
                sourceKeyRingPath,
                contentRootPath).ShouldBeTrue();
            using var sourceProvider = sourceServices.BuildServiceProvider();
            var sourceProtector = sourceProvider.GetRequiredService<IAgentTuiSecretProtector>();
            var protectedValue = sourceProtector.Protect(
                profileId,
                "OPENAI_API_KEY",
                "transient-key-secret");
            var sourceKeyPath = Directory.GetFiles(sourceKeyRingPath, "key-*.xml")
                .ShouldHaveSingleItem();
            var callbackProvider = new CallbackDataProtectionProvider(
                sourceProvider.GetRequiredService<IDataProtectionProvider>(),
                beforeUnprotect: () => File.Copy(sourceKeyPath, transientKeyPath),
                afterUnprotect: () => File.Delete(transientKeyPath));
            var sut = new DataProtectionAgentTuiSecretProtector(
                callbackProvider,
                provider.GetRequiredService<AgentTuiKeyProtectionReadiness>());

            Should.Throw<CryptographicException>(() =>
                sut.Unprotect(profileId, "OPENAI_API_KEY", protectedValue));
            File.Exists(transientKeyPath).ShouldBeFalse();
        }
        finally
        {
            DeleteTempPath(root);
        }
    }

    [Test]
    public void Configured_protector_allows_and_observes_initial_key_generation()
    {
        var root = NewTempPath();
        var keyRingPath = Path.Combine(root, "keys");
        var contentRootPath = Path.Combine(root, "repository");
        var certificatePath = Path.Combine(root, "key-protection.pfx");
        try
        {
            Directory.CreateDirectory(contentRootPath);
            WriteTestCertificate(certificatePath);
            var services = new ServiceCollection();
            AgentTuiDataProtectionSetup.Configure(
                services,
                CertificateSettings(certificatePath),
                CurrentPlatform(),
                keyRingPath,
                contentRootPath).ShouldBeTrue();
            using var provider = services.BuildServiceProvider();
            var sut = provider.GetRequiredService<IAgentTuiSecretProtector>();
            var readiness = provider.GetRequiredService<AgentTuiKeyProtectionReadiness>();
            var profileId = Guid.NewGuid();

            var cipher = sut.Protect(
                profileId,
                "OPENAI_API_KEY",
                "initial-key-generation-secret");

            Directory.GetFiles(keyRingPath, "*.xml", SearchOption.TopDirectoryOnly)
                .ShouldNotBeEmpty();
            readiness.IsReady.ShouldBeTrue();
            sut.Unprotect(profileId, "OPENAI_API_KEY", cipher)
                .ShouldBe("initial-key-generation-secret");
        }
        finally
        {
            DeleteTempPath(root);
        }
    }

    [Test]
    public void Configured_repository_logs_neither_secrets_nor_key_storage_details()
    {
        var root = NewTempPath();
        var keyRingPath = Path.Combine(root, "keys");
        var contentRootPath = Path.Combine(root, "repository");
        var certificatePath = Path.Combine(root, "key-protection.pfx");
        var logControl = $"application-log-control-{Guid.NewGuid():N}";
        var plaintextCanary = $"plaintext-canary-{Guid.NewGuid():N}";
        var failedFriendlyName = $"failed-key-{Guid.NewGuid():N}";
        try
        {
            Directory.CreateDirectory(contentRootPath);
            WriteTestCertificate(certificatePath);
            var loggerProvider = new CapturingLoggerProvider();
            var services = new ServiceCollection();
            services.AddLogging(logging =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.Trace);
                logging.AddProvider(loggerProvider);
            });
            AgentTuiDataProtectionSetup.Configure(
                services,
                CertificateSettings(certificatePath),
                CurrentPlatform(),
                keyRingPath,
                contentRootPath).ShouldBeTrue();
            using var provider = services.BuildServiceProvider();
            var applicationLogger = provider
                .GetRequiredService<ILogger<AgentTuiSecretProtectorTests>>();
            applicationLogger.LogInformation(
                "Agent TUI confidentiality capture control {Control}",
                logControl);
            var ciphertext = provider.GetRequiredService<IAgentTuiSecretProtector>()
                .Protect(Guid.NewGuid(), "OPENAI_API_KEY", plaintextCanary);
            var storedPath = Directory.GetFiles(keyRingPath, "key-*.xml")
                .ShouldHaveSingleItem();
            var storedFileName = Path.GetFileName(storedPath);
            var repository = provider
                .GetRequiredService<IOptions<KeyManagementOptions>>()
                .Value.XmlRepository;
            repository.ShouldNotBeNull();
            repository.GetAllElements().ShouldNotBeEmpty();

            Directory.Delete(keyRingPath, recursive: true);
            File.WriteAllText(keyRingPath, "blocks-the-repository-directory");
            var repositoryException = Should.Throw<CryptographicException>(() =>
                repository.StoreElement(new XElement("key"), failedFriendlyName));
            repositoryException.Message.ShouldBe("Persisted key XML could not be stored.");
            repositoryException.InnerException.ShouldBeNull();
            applicationLogger.LogWarning(
                repositoryException,
                "Agent TUI repository failure was sanitized");

            var captured = string.Join(Environment.NewLine, loggerProvider.Entries);
            captured.ShouldContain(logControl, Case.Sensitive);
            captured.ShouldNotContain(plaintextCanary, Case.Sensitive);
            captured.ShouldNotContain(ciphertext, Case.Sensitive);
            captured.ShouldNotContain(keyRingPath, Case.Insensitive);
            captured.ShouldNotContain(storedFileName, Case.Insensitive);
            captured.ShouldNotContain(failedFriendlyName, Case.Insensitive);
        }
        finally
        {
            DeleteTempPath(root);
        }
    }

    [Test]
    public void Repository_delete_callback_failures_are_not_sanitized()
    {
        var root = NewTempPath();
        var keyRingPath = Path.Combine(root, "keys");
        var contentRootPath = Path.Combine(root, "repository");
        var certificatePath = Path.Combine(root, "key-protection.pfx");
        try
        {
            Directory.CreateDirectory(contentRootPath);
            WriteTestCertificate(certificatePath);
            var services = new ServiceCollection();
            AgentTuiDataProtectionSetup.Configure(
                services,
                CertificateSettings(certificatePath),
                CurrentPlatform(),
                keyRingPath,
                contentRootPath).ShouldBeTrue();
            using var provider = services.BuildServiceProvider();
            provider.GetRequiredService<IAgentTuiSecretProtector>()
                .Protect(Guid.NewGuid(), "OPENAI_API_KEY", "callback-scope-secret");
            var repository = provider
                .GetRequiredService<IOptions<KeyManagementOptions>>()
                .Value.XmlRepository as IDeletableXmlRepository;
            repository.ShouldNotBeNull();
            var callbackFailure = new IOException("Deletion callback failed.");

            var observed = Should.Throw<IOException>(() =>
                repository.DeleteElements(_ => throw callbackFailure));

            ReferenceEquals(observed, callbackFailure).ShouldBeTrue();
        }
        finally
        {
            DeleteTempPath(root);
        }
    }

    [Test]
    public void Public_read_contract_serialization_does_not_expose_secret_value_properties()
    {
        var forbiddenNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Value",
            "Plaintext",
            "Ciphertext",
            "ProtectedValue"
        };
        var assembly = typeof(AgentTuiSettings).Assembly;
        var contractTypes = assembly.GetExportedTypes()
            .Where(type =>
                (type.Namespace == "Antiphon.Server.Application.Dtos"
                 && !IsWriteContract(type))
                || type == typeof(IAgentTuiSecretProtector)
                || type == typeof(AgentTuiSettings)
                || type == typeof(AgentTuiKeyProtectionSettings))
            .Concat(
            [
                typeof(DataProtectionAgentTuiSecretProtector),
                typeof(AgentTuiKeyProtectionReadiness)
            ])
            .Distinct()
            .ToArray();

        contractTypes.ShouldNotBeEmpty();
        var exposed = contractTypes
            .SelectMany(type => type.GetProperties()
                .Where(property => property.GetMethod?.IsPublic == true)
                .Where(property => property.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition
                    is not JsonIgnoreCondition.Always)
                .Select(property => new
                {
                    Contract = $"{type.FullName}.{property.Name}",
                    ClrName = property.Name,
                    JsonName = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                        ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name)
                }))
            .Where(property =>
                forbiddenNames.Contains(property.ClrName)
                || forbiddenNames.Contains(property.JsonName))
            .Select(property => $"{property.Contract} ({property.JsonName})")
            .ToArray();

        exposed.ShouldBeEmpty();
    }

    [Test]
    public void Persisted_key_ring_survives_provider_restart()
    {
        var keyRingPath = NewTempPath();
        try
        {
            string cipher;
            var profileId = Guid.NewGuid();
            using (var providerA = BuildPersistedProvider(keyRingPath, ApplicationName))
            {
                var sut = NewReadyProtector(providerA.GetRequiredService<IDataProtectionProvider>());
                cipher = sut.Protect(profileId, "ANTHROPIC_API_KEY", "restart-secret");
            }

            using var providerB = BuildPersistedProvider(keyRingPath, ApplicationName);
            var restarted = NewReadyProtector(providerB.GetRequiredService<IDataProtectionProvider>());

            restarted.Unprotect(profileId, "ANTHROPIC_API_KEY", cipher).ShouldBe("restart-secret");
        }
        finally
        {
            DeleteTempPath(keyRingPath);
        }
    }

    [Test]
    public void Provider_with_a_missing_key_ring_cannot_decrypt()
    {
        var originalKeyRingPath = NewTempPath();
        var missingKeyRingPath = NewTempPath();
        try
        {
            var profileId = Guid.NewGuid();
            string cipher;
            using (var originalProvider = BuildPersistedProvider(originalKeyRingPath, ApplicationName))
            {
                var original = NewReadyProtector(
                    originalProvider.GetRequiredService<IDataProtectionProvider>());
                cipher = original.Protect(profileId, "OPENAI_API_KEY", "key-ring-secret");
            }

            using var missingProvider = BuildPersistedProvider(missingKeyRingPath, ApplicationName);
            var missing = NewReadyProtector(
                missingProvider.GetRequiredService<IDataProtectionProvider>());

            Should.Throw<CryptographicException>(() =>
                missing.Unprotect(profileId, "OPENAI_API_KEY", cipher));
        }
        finally
        {
            DeleteTempPath(originalKeyRingPath);
            DeleteTempPath(missingKeyRingPath);
        }
    }

    [Test]
    public void Unready_key_protection_rejects_even_a_preexisting_unprotected_key_ring()
    {
        var root = NewTempPath();
        var keyRingPath = Path.Combine(root, "keys");
        var contentRootPath = Path.Combine(root, "repository");
        try
        {
            Directory.CreateDirectory(contentRootPath);
            using (var seedProvider = BuildPersistedProvider(keyRingPath, ApplicationName))
            {
                var seed = NewReadyProtector(seedProvider.GetRequiredService<IDataProtectionProvider>());
                seed.Protect(Guid.NewGuid(), "SEED_KEY", "seed-only");
            }

            var settings = new AgentTuiSettings
            {
                KeyProtection = new AgentTuiKeyProtectionSettings
                {
                    Mode = AgentTuiKeyProtectionMode.X509Certificate,
                    CertificatePath = Path.Combine(root, "missing-certificate.pfx")
                }
            };
            var services = new ServiceCollection();

            AgentTuiDataProtectionSetup.Configure(
                services,
                settings,
                CurrentPlatform(),
                keyRingPath,
                contentRootPath).ShouldBeFalse();
            using var provider = services.BuildServiceProvider();
            var sut = provider.GetRequiredService<IAgentTuiSecretProtector>();

            Should.Throw<CryptographicException>(() =>
                sut.Protect(Guid.NewGuid(), "OPENAI_API_KEY", "must-fail-closed"));
        }
        finally
        {
            DeleteTempPath(root);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void X509_private_key_must_be_outside_content_root_and_key_ring(bool insideKeyRing)
    {
        var root = NewTempPath();
        var keyRingPath = Path.Combine(root, "keys");
        var contentRootPath = Path.Combine(root, "repository");
        var certificatePath = Path.Combine(
            insideKeyRing ? keyRingPath : contentRootPath,
            "key-protection.pfx");
        try
        {
            Directory.CreateDirectory(keyRingPath);
            Directory.CreateDirectory(contentRootPath);
            WriteTestCertificate(certificatePath);
            var settings = CertificateSettings(certificatePath);
            var services = new ServiceCollection();

            AgentTuiDataProtectionSetup.Configure(
                services,
                settings,
                CurrentPlatform(),
                keyRingPath,
                contentRootPath).ShouldBeFalse();
        }
        finally
        {
            DeleteTempPath(root);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void Linked_directory_resolving_inside_protected_directory_is_not_outside(
        bool useNotYetCreatedLeaf)
    {
        var root = NewTempPath();
        var protectedDirectory = Path.Combine(root, "repository");
        var actualDirectory = Path.Combine(protectedDirectory, "actual-keys");
        var externalLink = Path.Combine(root, "external-keys-link");
        try
        {
            Directory.CreateDirectory(actualDirectory);
            CreateDirectoryLink(externalLink, actualDirectory);
            var candidatePath = useNotYetCreatedLeaf
                ? Path.Combine(externalLink, "new-key-ring")
                : externalLink;

            AgentTuiDataProtectionSetup.IsOutsideDirectory(candidatePath, protectedDirectory)
                .ShouldBeFalse();
        }
        finally
        {
            DeleteDirectoryLink(externalLink);
            DeleteTempPath(root);
        }
    }

    [Test]
    public void File_beneath_linked_directory_resolving_inside_protected_directory_is_not_outside()
    {
        var root = NewTempPath();
        var protectedDirectory = Path.Combine(root, "repository");
        var actualDirectory = Path.Combine(protectedDirectory, "actual-key-location");
        var actualFile = Path.Combine(actualDirectory, "key-protection.pfx");
        var externalLink = Path.Combine(root, "external-key-location");
        try
        {
            Directory.CreateDirectory(actualDirectory);
            WriteTestCertificate(actualFile);
            CreateDirectoryLink(externalLink, actualDirectory);
            var linkedFile = Path.Combine(externalLink, "key-protection.pfx");

            AgentTuiDataProtectionSetup.IsOutsideDirectory(linkedFile, protectedDirectory)
                .ShouldBeFalse();
        }
        finally
        {
            DeleteDirectoryLink(externalLink);
            DeleteTempPath(root);
        }
    }

    [Test]
    public void Windows_extended_path_alias_inside_protected_directory_is_not_outside()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var root = NewTempPath();
        var protectedDirectory = Path.Combine(root, "repository");
        var candidatePath = Path.Combine(protectedDirectory, "keys");
        try
        {
            Directory.CreateDirectory(candidatePath);
            var extendedCandidatePath = $@"\\?\{candidatePath}";

            AgentTuiDataProtectionSetup.IsOutsideDirectory(
                    extendedCandidatePath,
                    protectedDirectory)
                .ShouldBeFalse();
        }
        finally
        {
            DeleteTempPath(root);
        }
    }

    [Test]
    public void Windows_extended_path_alias_uses_nearest_existing_parent_for_absent_leaf()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var root = NewTempPath();
        var protectedDirectory = Path.Combine(root, "repository");
        var absentCandidatePath = Path.Combine(protectedDirectory, "new", "keys");
        try
        {
            Directory.CreateDirectory(protectedDirectory);
            var extendedCandidatePath = $@"\\?\{absentCandidatePath}";

            AgentTuiDataProtectionSetup.IsOutsideDirectory(
                    extendedCandidatePath,
                    protectedDirectory)
                .ShouldBeFalse();
        }
        finally
        {
            DeleteTempPath(root);
        }
    }

    [Test]
    public void Windows_volume_guid_alias_inside_protected_directory_is_not_outside()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var root = NewTempPath();
        var protectedDirectory = Path.Combine(root, "repository");
        var candidatePath = Path.Combine(protectedDirectory, "keys");
        try
        {
            Directory.CreateDirectory(candidatePath);
            var volumeRoot = Path.GetPathRoot(candidatePath);
            volumeRoot.ShouldNotBeNullOrWhiteSpace();
            var volumeName = new StringBuilder(1024);
            GetVolumeNameForVolumeMountPointW(
                    volumeRoot,
                    volumeName,
                    checked((uint)volumeName.Capacity))
                .ShouldBeTrue($"Win32 error {Marshal.GetLastWin32Error()}");
            var volumeAlias = Path.Combine(
                volumeName.ToString(),
                candidatePath[volumeRoot.Length..]);

            AgentTuiDataProtectionSetup.IsOutsideDirectory(volumeAlias, protectedDirectory)
                .ShouldBeFalse();
        }
        finally
        {
            DeleteTempPath(root);
        }
    }

    [Test]
    public void MacOS_canonical_path_containment_is_case_insensitive()
    {
        AgentTuiDataProtectionSetup.IsCanonicalPathOutsideDirectory(
                "/Users/build/Repository/keys",
                "/users/build/repository",
                AgentTuiPlatform.MacOS)
            .ShouldBeFalse();
    }

    [Test]
    public void MacOS_canonical_path_containment_normalizes_equivalent_unicode()
    {
        AgentTuiDataProtectionSetup.IsCanonicalPathOutsideDirectory(
                "/Users/build/Re\u0301pository/keys",
                "/Users/build/R\u00E9pository",
                AgentTuiPlatform.MacOS)
            .ShouldBeFalse();
    }

    [Test]
    public void Public_protector_construction_requires_readiness()
    {
        var bypassConstructors = typeof(DataProtectionAgentTuiSecretProtector)
            .GetConstructors()
            .Where(constructor => constructor.GetParameters() is
            [
                { ParameterType: var parameterType }
            ] && parameterType == typeof(IDataProtectionProvider))
            .ToArray();

        bypassConstructors.ShouldBeEmpty();
    }

    [Test]
    public void Unknown_key_protection_mode_is_not_ready()
    {
        var root = NewTempPath();
        var keyRingPath = Path.Combine(root, "keys");
        var contentRootPath = Path.Combine(root, "repository");
        try
        {
            Directory.CreateDirectory(contentRootPath);
            var settings = new AgentTuiSettings
            {
                KeyProtection = new AgentTuiKeyProtectionSettings
                {
                    Mode = (AgentTuiKeyProtectionMode)999
                }
            };
            var services = new ServiceCollection();

            AgentTuiDataProtectionSetup.Configure(
                services,
                settings,
                CurrentPlatform(),
                keyRingPath,
                contentRootPath).ShouldBeFalse();
        }
        finally
        {
            DeleteTempPath(root);
        }
    }

    [Test]
    public void Configured_X509_key_protection_survives_provider_restart()
    {
        var root = NewTempPath();
        var keyRingPath = Path.Combine(root, "keys");
        var contentRootPath = Path.Combine(root, "repository");
        var certificatePath = Path.Combine(root, "key-protection.pfx");
        try
        {
            Directory.CreateDirectory(contentRootPath);
            WriteTestCertificate(certificatePath);
            var settings = new AgentTuiSettings
            {
                KeyProtection = new AgentTuiKeyProtectionSettings
                {
                    Mode = AgentTuiKeyProtectionMode.X509Certificate,
                    CertificatePath = certificatePath
                }
            };
            var profileId = Guid.NewGuid();
            string cipher;

            var servicesA = new ServiceCollection();
            AgentTuiDataProtectionSetup.Configure(
                servicesA,
                settings,
                CurrentPlatform(),
                keyRingPath,
                contentRootPath).ShouldBeTrue();
            AssertPrivateKeyFileOwnerOnly(certificatePath);
            using (var providerA = servicesA.BuildServiceProvider())
            {
                cipher = providerA.GetRequiredService<IAgentTuiSecretProtector>()
                    .Protect(profileId, "OPENAI_API_KEY", "certificate-protected");
            }

            var keyFiles = Directory.GetFiles(keyRingPath, "*.xml");
            keyFiles.Length.ShouldBe(1);
            var keyXml = File.ReadAllText(keyFiles[0]);
            keyXml.Contains("encryptedSecret", StringComparison.Ordinal).ShouldBeTrue(
                "Persisted Data Protection key material must be encrypted.");

            var servicesB = new ServiceCollection();
            AgentTuiDataProtectionSetup.Configure(
                servicesB,
                settings,
                CurrentPlatform(),
                keyRingPath,
                contentRootPath).ShouldBeTrue();
            using var providerB = servicesB.BuildServiceProvider();

            providerB.GetRequiredService<IAgentTuiSecretProtector>()
                .Unprotect(profileId, "OPENAI_API_KEY", cipher)
                .ShouldBe("certificate-protected");
        }
        finally
        {
            DeleteTempPath(root);
        }
    }

    [Test]
    public void Configured_X509_protector_rechecks_external_private_key_source()
    {
        var root = NewTempPath();
        var keyRingPath = Path.Combine(root, "keys");
        var contentRootPath = Path.Combine(root, "repository");
        var certificatePath = Path.Combine(root, "key-protection.pfx");
        try
        {
            Directory.CreateDirectory(contentRootPath);
            WriteTestCertificate(certificatePath);
            var services = new ServiceCollection();
            AgentTuiDataProtectionSetup.Configure(
                services,
                CertificateSettings(certificatePath),
                CurrentPlatform(),
                keyRingPath,
                contentRootPath).ShouldBeTrue();
            using var provider = services.BuildServiceProvider();
            var sut = provider.GetRequiredService<IAgentTuiSecretProtector>();
            var readiness = provider.GetRequiredService<AgentTuiKeyProtectionReadiness>();
            var profileId = Guid.NewGuid();
            var cipher = sut.Protect(profileId, "OPENAI_API_KEY", "source-removal-secret");

            File.Delete(certificatePath);

            readiness.IsReady.ShouldBeFalse();
            Should.Throw<CryptographicException>(() =>
                sut.Protect(profileId, "OPENAI_API_KEY", "rejected-secret"));
            Should.Throw<CryptographicException>(() =>
                sut.Unprotect(profileId, "OPENAI_API_KEY", cipher));
        }
        finally
        {
            DeleteTempPath(root);
        }
    }

    [Test]
    public void Configured_X509_protector_rechecks_current_user_store_certificate()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var root = NewTempPath();
        var keyRingPath = Path.Combine(root, "keys");
        var contentRootPath = Path.Combine(root, "repository");
        var keyName = $"Antiphon-AgentTui-Test-{Guid.NewGuid():N}";
        string? thumbprint = null;
        CngKey? key = null;
        X509Certificate2? certificate = null;
        try
        {
            key = CngKey.Create(
                CngAlgorithm.Rsa,
                keyName,
                new CngKeyCreationParameters
                {
                    KeyUsage = CngKeyUsages.AllUsages
                });
            using (var rsa = new RSACng(key))
            {
                var request = new CertificateRequest(
                    $"CN=Antiphon Agent TUI Test {Guid.NewGuid():N}",
                    rsa,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);
                certificate = request.CreateSelfSigned(
                    DateTimeOffset.UtcNow.AddMinutes(-1),
                    DateTimeOffset.UtcNow.AddMinutes(10));
            }

            thumbprint = certificate.Thumbprint;
            Directory.CreateDirectory(contentRootPath);
            AddCurrentUserCertificate(certificate);
            var services = new ServiceCollection();
            var settings = new AgentTuiSettings
            {
                KeyProtection = new AgentTuiKeyProtectionSettings
                {
                    Mode = AgentTuiKeyProtectionMode.X509Certificate,
                    CertificateThumbprint = thumbprint,
                    CertificateStoreName = StoreName.My.ToString(),
                    CertificateStoreLocation = StoreLocation.CurrentUser.ToString()
                }
            };
            AgentTuiDataProtectionSetup.Configure(
                services,
                settings,
                CurrentPlatform(),
                keyRingPath,
                contentRootPath).ShouldBeTrue();
            using var provider = services.BuildServiceProvider();
            var sut = provider.GetRequiredService<IAgentTuiSecretProtector>();
            var profileId = Guid.NewGuid();
            var cipher = sut.Protect(profileId, "OPENAI_API_KEY", "store-removal-secret");

            RemoveCurrentUserCertificate(thumbprint);

            provider.GetRequiredService<AgentTuiKeyProtectionReadiness>().IsReady.ShouldBeFalse();
            Should.Throw<CryptographicException>(() =>
                sut.Protect(profileId, "OPENAI_API_KEY", "rejected-secret"));
            Should.Throw<CryptographicException>(() =>
                sut.Unprotect(profileId, "OPENAI_API_KEY", cipher));
        }
        finally
        {
            if (thumbprint is not null)
                RemoveCurrentUserCertificate(thumbprint);
            certificate?.Dispose();
            key?.Delete();
            key?.Dispose();
            DeleteTempPath(root);
        }

        thumbprint.ShouldNotBeNull();
        CurrentUserCertificateExists(thumbprint).ShouldBeFalse();
        CngKey.Exists(keyName).ShouldBeFalse();
    }

    [Test]
    public void X509_store_selection_disposes_unselected_candidates()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Antiphon Agent TUI Candidate Test",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using var source = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddMinutes(10));
        var publicCandidate = X509CertificateLoader.LoadCertificate(
            source.Export(X509ContentType.Cert));
        var privateCandidate = X509CertificateLoader.LoadPkcs12(
            source.Export(X509ContentType.Pfx),
            password: null,
            X509KeyStorageFlags.EphemeralKeySet);
        var candidates = new X509Certificate2Collection
        {
            publicCandidate,
            privateCandidate
        };
        try
        {
            var selected = AgentTuiDataProtectionSetup
                .SelectPrivateKeyCertificateAndDisposeOthers(candidates);

            selected.ShouldBeSameAs(privateCandidate);
            publicCandidate.Handle.ShouldBe(IntPtr.Zero);
            privateCandidate.Handle.ShouldNotBe(IntPtr.Zero);
        }
        finally
        {
            publicCandidate.Dispose();
            privateCandidate.Dispose();
        }
    }

    [Test]
    public void Configured_X509_protector_rechecks_certificate_validity_at_operation_time()
    {
        var root = NewTempPath();
        var keyRingPath = Path.Combine(root, "keys");
        var contentRootPath = Path.Combine(root, "repository");
        var certificatePath = Path.Combine(root, "key-protection.pfx");
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(5);
        var timeProvider = new MutableTimeProvider(now);
        try
        {
            Directory.CreateDirectory(contentRootPath);
            WriteTestCertificate(certificatePath, now.AddMinutes(-1), expiresAt);
            var services = new ServiceCollection();
            AgentTuiDataProtectionSetup.Configure(
                services,
                CertificateSettings(certificatePath),
                CurrentPlatform(),
                keyRingPath,
                contentRootPath,
                timeProvider).ShouldBeTrue();
            using var provider = services.BuildServiceProvider();
            var sut = provider.GetRequiredService<IAgentTuiSecretProtector>();
            var profileId = Guid.NewGuid();
            var cipher = sut.Protect(profileId, "OPENAI_API_KEY", "expiring-secret");

            timeProvider.SetUtcNow(expiresAt.AddSeconds(1));

            provider.GetRequiredService<AgentTuiKeyProtectionReadiness>().IsReady.ShouldBeFalse();
            Should.Throw<CryptographicException>(() =>
                sut.Protect(profileId, "OPENAI_API_KEY", "rejected-secret"));
            Should.Throw<CryptographicException>(() =>
                sut.Unprotect(profileId, "OPENAI_API_KEY", cipher));
        }
        finally
        {
            DeleteTempPath(root);
        }
    }

    [Test]
    public void Configured_protector_rechecks_key_ring_presence()
    {
        var root = NewTempPath();
        var keyRingPath = Path.Combine(root, "keys");
        var contentRootPath = Path.Combine(root, "repository");
        var certificatePath = Path.Combine(root, "key-protection.pfx");
        try
        {
            Directory.CreateDirectory(contentRootPath);
            WriteTestCertificate(certificatePath);
            var services = new ServiceCollection();
            AgentTuiDataProtectionSetup.Configure(
                services,
                CertificateSettings(certificatePath),
                CurrentPlatform(),
                keyRingPath,
                contentRootPath).ShouldBeTrue();
            using var provider = services.BuildServiceProvider();
            var sut = provider.GetRequiredService<IAgentTuiSecretProtector>();
            var profileId = Guid.NewGuid();
            var cipher = sut.Protect(profileId, "OPENAI_API_KEY", "key-ring-removal-secret");

            Directory.Delete(keyRingPath, recursive: true);

            provider.GetRequiredService<AgentTuiKeyProtectionReadiness>().IsReady.ShouldBeFalse();
            Should.Throw<CryptographicException>(() =>
                sut.Protect(profileId, "OPENAI_API_KEY", "rejected-secret"));
            Should.Throw<CryptographicException>(() =>
                sut.Unprotect(profileId, "OPENAI_API_KEY", cipher));
        }
        finally
        {
            DeleteTempPath(root);
        }
    }

    [Test]
    public void Configured_protector_rechecks_persisted_key_presence()
    {
        var root = NewTempPath();
        var keyRingPath = Path.Combine(root, "keys");
        var contentRootPath = Path.Combine(root, "repository");
        var certificatePath = Path.Combine(root, "key-protection.pfx");
        try
        {
            Directory.CreateDirectory(contentRootPath);
            WriteTestCertificate(certificatePath);
            var services = new ServiceCollection();
            AgentTuiDataProtectionSetup.Configure(
                services,
                CertificateSettings(certificatePath),
                CurrentPlatform(),
                keyRingPath,
                contentRootPath).ShouldBeTrue();
            using var provider = services.BuildServiceProvider();
            var sut = provider.GetRequiredService<IAgentTuiSecretProtector>();
            var profileId = Guid.NewGuid();
            var cipher = sut.Protect(profileId, "OPENAI_API_KEY", "persisted-key-removal-secret");
            var persistedKeys = Directory.GetFiles(
                keyRingPath,
                "*.xml",
                SearchOption.TopDirectoryOnly);
            persistedKeys.ShouldNotBeEmpty();

            foreach (var persistedKey in persistedKeys)
                File.Delete(persistedKey);

            Directory.Exists(keyRingPath).ShouldBeTrue();
            provider.GetRequiredService<AgentTuiKeyProtectionReadiness>().IsReady.ShouldBeFalse();
            Should.Throw<CryptographicException>(() =>
                sut.Protect(profileId, "OPENAI_API_KEY", "rejected-secret"));
            Should.Throw<CryptographicException>(() =>
                sut.Unprotect(profileId, "OPENAI_API_KEY", cipher));
        }
        finally
        {
            DeleteTempPath(root);
        }
    }

    [Test]
    public void Configured_protector_rejects_replaced_persisted_key_material()
    {
        var root = NewTempPath();
        var keyRingPath = Path.Combine(root, "keys");
        var contentRootPath = Path.Combine(root, "repository");
        var certificatePath = Path.Combine(root, "key-protection.pfx");
        try
        {
            Directory.CreateDirectory(contentRootPath);
            WriteTestCertificate(certificatePath);
            var services = new ServiceCollection();
            AgentTuiDataProtectionSetup.Configure(
                services,
                CertificateSettings(certificatePath),
                CurrentPlatform(),
                keyRingPath,
                contentRootPath).ShouldBeTrue();
            using var provider = services.BuildServiceProvider();
            var sut = provider.GetRequiredService<IAgentTuiSecretProtector>();
            var profileId = Guid.NewGuid();
            var cipher = sut.Protect(profileId, "OPENAI_API_KEY", "key-replacement-secret");
            var persistedKey = Directory.GetFiles(
                    keyRingPath,
                    "*.xml",
                    SearchOption.TopDirectoryOnly)
                .ShouldHaveSingleItem();

            File.WriteAllText(persistedKey, "<corrupted />");

            provider.GetRequiredService<AgentTuiKeyProtectionReadiness>().IsReady.ShouldBeFalse();
            Should.Throw<CryptographicException>(() =>
                sut.Protect(profileId, "OPENAI_API_KEY", "rejected-secret"));
            Should.Throw<CryptographicException>(() =>
                sut.Unprotect(profileId, "OPENAI_API_KEY", cipher));
        }
        finally
        {
            DeleteTempPath(root);
        }
    }

    [Test]
    public void Configured_protector_rejects_unreadable_persisted_key_material()
    {
        var root = NewTempPath();
        var keyRingPath = Path.Combine(root, "keys");
        var contentRootPath = Path.Combine(root, "repository");
        var certificatePath = Path.Combine(root, "key-protection.pfx");
        var rejectKeyReads = false;
        byte[] ReadKeyFile(string path)
        {
            if (rejectKeyReads)
                throw new UnauthorizedAccessException("Simulated unreadable key material.");

            return File.ReadAllBytes(path);
        }

        try
        {
            Directory.CreateDirectory(contentRootPath);
            WriteTestCertificate(certificatePath);
            var services = new ServiceCollection();
            AgentTuiDataProtectionSetup.Configure(
                services,
                CertificateSettings(certificatePath),
                CurrentPlatform(),
                keyRingPath,
                contentRootPath,
                keyFileReader: ReadKeyFile).ShouldBeTrue();
            using var provider = services.BuildServiceProvider();
            var sut = provider.GetRequiredService<IAgentTuiSecretProtector>();
            var profileId = Guid.NewGuid();
            var cipher = sut.Protect(profileId, "OPENAI_API_KEY", "unreadable-key-secret");

            rejectKeyReads = true;

            provider.GetRequiredService<AgentTuiKeyProtectionReadiness>().IsReady.ShouldBeFalse();
            Should.Throw<CryptographicException>(() =>
                sut.Protect(profileId, "OPENAI_API_KEY", "rejected-secret"));
            Should.Throw<CryptographicException>(() =>
                sut.Unprotect(profileId, "OPENAI_API_KEY", cipher));
        }
        finally
        {
            DeleteTempPath(root);
        }
    }

    [Test]
    public void Configured_protector_rejects_corrupt_extra_key_xml()
    {
        var root = NewTempPath();
        var keyRingPath = Path.Combine(root, "keys");
        var contentRootPath = Path.Combine(root, "repository");
        var certificatePath = Path.Combine(root, "key-protection.pfx");
        try
        {
            Directory.CreateDirectory(contentRootPath);
            WriteTestCertificate(certificatePath);
            var services = new ServiceCollection();
            AgentTuiDataProtectionSetup.Configure(
                services,
                CertificateSettings(certificatePath),
                CurrentPlatform(),
                keyRingPath,
                contentRootPath).ShouldBeTrue();
            using var provider = services.BuildServiceProvider();
            var sut = provider.GetRequiredService<IAgentTuiSecretProtector>();
            sut.Protect(Guid.NewGuid(), "OPENAI_API_KEY", "seed-secret");
            File.WriteAllText(Path.Combine(keyRingPath, "key-corrupt.xml"), "<not-a-key />");

            provider.GetRequiredService<AgentTuiKeyProtectionReadiness>().IsReady.ShouldBeFalse();
            Should.Throw<CryptographicException>(() =>
                sut.Protect(Guid.NewGuid(), "OPENAI_API_KEY", "rejected-secret"));
        }
        finally
        {
            DeleteTempPath(root);
        }
    }

    [Test]
    public void Corrupt_key_xml_present_at_startup_is_not_ready()
    {
        var root = NewTempPath();
        var keyRingPath = Path.Combine(root, "keys");
        var contentRootPath = Path.Combine(root, "repository");
        var certificatePath = Path.Combine(root, "key-protection.pfx");
        try
        {
            Directory.CreateDirectory(contentRootPath);
            WriteTestCertificate(certificatePath);
            AgentTuiDataProtectionSetup.EnsureKeyRingDirectory(
                keyRingPath,
                CurrentPlatform()).ShouldBeTrue();
            var corruptKeyPath = Path.Combine(keyRingPath, "key-corrupt.xml");
            File.WriteAllText(corruptKeyPath, "<key id=\"not-a-guid\">");
            MakePersistedKeyFileOwnerOnly(corruptKeyPath);
            var services = new ServiceCollection();

            AgentTuiDataProtectionSetup.Configure(
                services,
                CertificateSettings(certificatePath),
                CurrentPlatform(),
                keyRingPath,
                contentRootPath).ShouldBeFalse();
            using var provider = services.BuildServiceProvider();
            provider.GetRequiredService<AgentTuiKeyProtectionReadiness>().IsReady.ShouldBeFalse();
            Should.Throw<CryptographicException>(() =>
                provider.GetRequiredService<IAgentTuiSecretProtector>()
                    .Protect(Guid.NewGuid(), "OPENAI_API_KEY", "rejected-secret"));
        }
        finally
        {
            DeleteTempPath(root);
        }
    }

    [Test]
    public void Configured_protector_rejects_dtd_bearing_key_ring_xml()
    {
        var root = NewTempPath();
        var keyRingPath = Path.Combine(root, "keys");
        var contentRootPath = Path.Combine(root, "repository");
        var certificatePath = Path.Combine(root, "key-protection.pfx");
        try
        {
            Directory.CreateDirectory(contentRootPath);
            WriteTestCertificate(certificatePath);
            var services = new ServiceCollection();
            AgentTuiDataProtectionSetup.Configure(
                services,
                CertificateSettings(certificatePath),
                CurrentPlatform(),
                keyRingPath,
                contentRootPath).ShouldBeTrue();
            using var provider = services.BuildServiceProvider();
            var sut = provider.GetRequiredService<IAgentTuiSecretProtector>();
            sut.Protect(Guid.NewGuid(), "OPENAI_API_KEY", "seed-secret");
            File.WriteAllText(
                Path.Combine(keyRingPath, "revocation-dtd.xml"),
                "<!DOCTYPE revocation [<!ENTITY probe 'blocked'>]>" +
                "<revocation version=\"1\"><revocationDate>&probe;</revocationDate></revocation>");

            provider.GetRequiredService<AgentTuiKeyProtectionReadiness>().IsReady.ShouldBeFalse();
        }
        finally
        {
            DeleteTempPath(root);
        }
    }

    [Test]
    public void Configured_protector_rejects_oversized_key_ring_xml()
    {
        var root = NewTempPath();
        var keyRingPath = Path.Combine(root, "keys");
        var contentRootPath = Path.Combine(root, "repository");
        var certificatePath = Path.Combine(root, "key-protection.pfx");
        try
        {
            Directory.CreateDirectory(contentRootPath);
            WriteTestCertificate(certificatePath);
            var services = new ServiceCollection();
            AgentTuiDataProtectionSetup.Configure(
                services,
                CertificateSettings(certificatePath),
                CurrentPlatform(),
                keyRingPath,
                contentRootPath).ShouldBeTrue();
            using var provider = services.BuildServiceProvider();
            var sut = provider.GetRequiredService<IAgentTuiSecretProtector>();
            sut.Protect(Guid.NewGuid(), "OPENAI_API_KEY", "seed-secret");
            File.WriteAllText(
                Path.Combine(keyRingPath, "revocation-oversized.xml"),
                "<revocation version=\"1\"><!--" + new string('x', 1024 * 1024) +
                "--><revocationDate>2026-08-12T00:00:00Z</revocationDate></revocation>");

            provider.GetRequiredService<AgentTuiKeyProtectionReadiness>().IsReady.ShouldBeFalse();
        }
        finally
        {
            DeleteTempPath(root);
        }
    }

    [Test]
    public void Restored_key_ring_under_wrong_certificate_is_not_ready()
    {
        var root = NewTempPath();
        var keyRingPath = Path.Combine(root, "keys");
        var contentRootPath = Path.Combine(root, "repository");
        var originalCertificatePath = Path.Combine(root, "original-key-protection.pfx");
        var wrongCertificatePath = Path.Combine(root, "wrong-key-protection.pfx");
        try
        {
            Directory.CreateDirectory(contentRootPath);
            WriteTestCertificate(originalCertificatePath);
            WriteTestCertificate(wrongCertificatePath);
            var profileId = Guid.NewGuid();
            string cipher;
            var originalServices = new ServiceCollection();
            AgentTuiDataProtectionSetup.Configure(
                originalServices,
                CertificateSettings(originalCertificatePath),
                CurrentPlatform(),
                keyRingPath,
                contentRootPath).ShouldBeTrue();
            using (var originalProvider = originalServices.BuildServiceProvider())
            {
                cipher = originalProvider.GetRequiredService<IAgentTuiSecretProtector>()
                    .Protect(profileId, "OPENAI_API_KEY", "wrong-certificate-secret");
            }

            var restoredServices = new ServiceCollection();
            AgentTuiDataProtectionSetup.Configure(
                restoredServices,
                CertificateSettings(wrongCertificatePath),
                CurrentPlatform(),
                keyRingPath,
                contentRootPath).ShouldBeTrue();
            using var restoredProvider = restoredServices.BuildServiceProvider();
            var readiness = restoredProvider.GetRequiredService<AgentTuiKeyProtectionReadiness>();
            var sut = restoredProvider.GetRequiredService<IAgentTuiSecretProtector>();

            readiness.IsReady.ShouldBeFalse();
            Should.Throw<CryptographicException>(() =>
                sut.Protect(profileId, "OPENAI_API_KEY", "rejected-secret"));
            Should.Throw<CryptographicException>(() =>
                sut.Unprotect(profileId, "OPENAI_API_KEY", cipher));
        }
        finally
        {
            DeleteTempPath(root);
        }
    }

    [Test]
    public void Restored_key_ring_with_permissive_file_acl_is_rejected_without_repair()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var root = NewTempPath();
        var keyRingPath = Path.Combine(root, "keys");
        var contentRootPath = Path.Combine(root, "repository");
        var certificatePath = Path.Combine(root, "key-protection.pfx");
        try
        {
            Directory.CreateDirectory(contentRootPath);
            WriteTestCertificate(certificatePath);
            var services = new ServiceCollection();
            AgentTuiDataProtectionSetup.Configure(
                services,
                CertificateSettings(certificatePath),
                CurrentPlatform(),
                keyRingPath,
                contentRootPath).ShouldBeTrue();
            using (var provider = services.BuildServiceProvider())
            {
                provider.GetRequiredService<IAgentTuiSecretProtector>()
                    .Protect(Guid.NewGuid(), "OPENAI_API_KEY", "seed-secret");
            }
            var keyPath = Directory.GetFiles(keyRingPath, "key-*.xml").ShouldHaveSingleItem();
            MakeWindowsFileInsecure(keyPath);
            AssertWindowsFileIsInsecure(keyPath);

            var restoredServices = new ServiceCollection();
            AgentTuiDataProtectionSetup.Configure(
                restoredServices,
                CertificateSettings(certificatePath),
                CurrentPlatform(),
                keyRingPath,
                contentRootPath).ShouldBeFalse();

            AssertWindowsFileIsInsecure(keyPath);
        }
        finally
        {
            DeleteTempPath(root);
        }
    }

    [Test]
    public void Provider_created_key_file_is_tightened_before_acceptance()
    {
        var root = NewTempPath();
        var keyRingPath = Path.Combine(root, "keys");
        var contentRootPath = Path.Combine(root, "repository");
        var certificatePath = Path.Combine(root, "key-protection.pfx");
        try
        {
            Directory.CreateDirectory(contentRootPath);
            WriteTestCertificate(certificatePath);
            var services = new ServiceCollection();
            AgentTuiDataProtectionSetup.Configure(
                services,
                CertificateSettings(certificatePath),
                CurrentPlatform(),
                keyRingPath,
                contentRootPath).ShouldBeTrue();
            using var provider = services.BuildServiceProvider();

            provider.GetRequiredService<IAgentTuiSecretProtector>()
                .Protect(Guid.NewGuid(), "OPENAI_API_KEY", "seed-secret");

            var keyPath = Directory.GetFiles(keyRingPath, "key-*.xml").ShouldHaveSingleItem();
            if (OperatingSystem.IsWindows())
                AssertWindowsFileOwnerOnly(keyPath);
            else
                File.GetUnixFileMode(keyPath).ShouldBe(
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        finally
        {
            DeleteTempPath(root);
        }
    }

    [Test]
    public void Unix_persisted_key_mode_rejects_group_or_other_permissions()
    {
        var ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        AgentTuiDataProtectionSetup.IsUnixPersistedKeyModeOwnerOnly(ownerOnly)
            .ShouldBeTrue();
        AgentTuiDataProtectionSetup.IsUnixPersistedKeyModeOwnerOnly(
                ownerOnly | UnixFileMode.GroupRead)
            .ShouldBeFalse();
        AgentTuiDataProtectionSetup.IsUnixPersistedKeyModeOwnerOnly(
                ownerOnly | UnixFileMode.OtherWrite)
            .ShouldBeFalse();
    }

    [Test]
    public void Persisted_key_file_reparse_points_are_rejected()
    {
        AgentTuiDataProtectionSetup.IsPersistedXmlFileAttributeSetSafe(FileAttributes.Normal)
            .ShouldBeTrue();
        AgentTuiDataProtectionSetup.IsPersistedXmlFileAttributeSetSafe(
                FileAttributes.Normal | FileAttributes.ReparsePoint)
            .ShouldBeFalse();
    }

    [Test]
    public void Restored_key_file_symlink_is_rejected_without_mutating_target()
    {
        var root = NewTempPath();
        var keyRingPath = Path.Combine(root, "keys");
        var contentRootPath = Path.Combine(root, "repository");
        var certificatePath = Path.Combine(root, "key-protection.pfx");
        var targetPath = Path.Combine(root, "restored-key-target.xml");
        try
        {
            Directory.CreateDirectory(contentRootPath);
            WriteTestCertificate(certificatePath);
            var services = new ServiceCollection();
            AgentTuiDataProtectionSetup.Configure(
                services,
                CertificateSettings(certificatePath),
                CurrentPlatform(),
                keyRingPath,
                contentRootPath).ShouldBeTrue();
            using (var provider = services.BuildServiceProvider())
            {
                provider.GetRequiredService<IAgentTuiSecretProtector>()
                    .Protect(Guid.NewGuid(), "OPENAI_API_KEY", "seed-secret");
            }
            var keyPath = Directory.GetFiles(keyRingPath, "key-*.xml").ShouldHaveSingleItem();
            File.Move(keyPath, targetPath);
            var targetBytes = File.ReadAllBytes(targetPath);
            try
            {
                File.CreateSymbolicLink(keyPath, targetPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new SkipTestException(
                    $"File symlink creation is unavailable: {exception.Message}");
            }

            (File.GetAttributes(keyPath) & FileAttributes.ReparsePoint)
                .ShouldBe(FileAttributes.ReparsePoint);
            var restoredServices = new ServiceCollection();

            AgentTuiDataProtectionSetup.Configure(
                restoredServices,
                CertificateSettings(certificatePath),
                CurrentPlatform(),
                keyRingPath,
                contentRootPath).ShouldBeFalse();
            File.ReadAllBytes(targetPath).ShouldBe(targetBytes);
        }
        finally
        {
            DeleteTempPath(root);
        }
    }

    [Test]
    public void Valid_revocation_xml_is_not_treated_as_a_persisted_key()
    {
        var root = NewTempPath();
        var keyRingPath = Path.Combine(root, "keys");
        var contentRootPath = Path.Combine(root, "repository");
        var certificatePath = Path.Combine(root, "key-protection.pfx");
        try
        {
            Directory.CreateDirectory(contentRootPath);
            WriteTestCertificate(certificatePath);
            var services = new ServiceCollection();
            AgentTuiDataProtectionSetup.Configure(
                services,
                CertificateSettings(certificatePath),
                CurrentPlatform(),
                keyRingPath,
                contentRootPath).ShouldBeTrue();
            using var provider = services.BuildServiceProvider();
            provider.GetRequiredService<IAgentTuiSecretProtector>()
                .Protect(Guid.NewGuid(), "OPENAI_API_KEY", "seed-secret");
            var keyManager = provider.GetRequiredService<IKeyManager>();
            var key = keyManager.GetAllKeys().ShouldHaveSingleItem();

            keyManager.RevokeKey(key.KeyId, "test revocation");

            Directory.GetFiles(keyRingPath, "revocation-*.xml", SearchOption.TopDirectoryOnly)
                .ShouldNotBeEmpty();
            provider.GetRequiredService<AgentTuiKeyProtectionReadiness>().IsReady.ShouldBeTrue();
        }
        finally
        {
            DeleteTempPath(root);
        }
    }

    [Test]
    public void Readiness_check_rejects_insecure_key_ring_permissions_without_repairing_them()
    {
        var root = NewTempPath();
        var keyRingPath = Path.Combine(root, "keys");
        var contentRootPath = Path.Combine(root, "repository");
        var certificatePath = Path.Combine(root, "key-protection.pfx");
        try
        {
            Directory.CreateDirectory(contentRootPath);
            WriteTestCertificate(certificatePath);
            var services = new ServiceCollection();
            AgentTuiDataProtectionSetup.Configure(
                services,
                CertificateSettings(certificatePath),
                CurrentPlatform(),
                keyRingPath,
                contentRootPath).ShouldBeTrue();
            using var provider = services.BuildServiceProvider();
            var sut = provider.GetRequiredService<IAgentTuiSecretProtector>();
            var profileId = Guid.NewGuid();
            var cipher = sut.Protect(profileId, "OPENAI_API_KEY", "permission-drift-secret");
            MakeKeyRingDirectoryInsecure(keyRingPath);

            provider.GetRequiredService<AgentTuiKeyProtectionReadiness>().IsReady.ShouldBeFalse();
            Should.Throw<CryptographicException>(() =>
                sut.Protect(profileId, "OPENAI_API_KEY", "rejected-secret"));
            Should.Throw<CryptographicException>(() =>
                sut.Unprotect(profileId, "OPENAI_API_KEY", cipher));
            AssertKeyRingDirectoryIsInsecure(keyRingPath);
        }
        finally
        {
            DeleteTempPath(root);
        }
    }

    [Test]
    public void Wrapper_managed_mode_does_not_require_secret_protection()
    {
        AgentTuiSettings.RequiresSecretProtection(AgentTuiAuthenticationMode.WrapperManaged)
            .ShouldBeFalse();
        AgentTuiSettings.RequiresSecretProtection(AgentTuiAuthenticationMode.ManagedEnvironment)
            .ShouldBeTrue();
        Should.Throw<ArgumentOutOfRangeException>(() =>
            AgentTuiSettings.RequiresSecretProtection((AgentTuiAuthenticationMode)999));
    }

    [Test]
    [Arguments(
        AgentTuiPlatform.Windows,
        @"C:\Users\operator\AppData\Local",
        null,
        @"C:\Users\operator",
        @"C:\Users\operator\AppData\Local\Antiphon\DataProtection-Keys")]
    [Arguments(
        AgentTuiPlatform.Linux,
        null,
        "/srv/operator-data",
        "/home/operator",
        "/srv/operator-data/antiphon/data-protection-keys")]
    [Arguments(
        AgentTuiPlatform.MacOS,
        null,
        "/Users/operator/xdg",
        "/Users/operator",
        "/Users/operator/xdg/antiphon/data-protection-keys")]
    [Arguments(
        AgentTuiPlatform.Linux,
        null,
        null,
        "/home/operator",
        "/home/operator/.local/share/antiphon/data-protection-keys")]
    [Arguments(
        AgentTuiPlatform.MacOS,
        null,
        "",
        "/Users/operator",
        "/Users/operator/.local/share/antiphon/data-protection-keys")]
    public void Default_key_ring_path_is_resolved_from_supplied_platform_environment(
        AgentTuiPlatform platform,
        string? localApplicationData,
        string? xdgDataHome,
        string homeDirectory,
        string expected)
    {
        var settings = new AgentTuiSettings();
        var environment = new AgentTuiPathEnvironment(
            platform,
            localApplicationData,
            xdgDataHome,
            homeDirectory);

        settings.ResolveKeyRingPath(environment).ShouldBe(expected);
    }

    [Test]
    [Arguments("relative/xdg")]
    [Arguments(" ")]
    public void Non_empty_relative_xdg_data_home_is_rejected(string xdgDataHome)
    {
        var settings = new AgentTuiSettings();
        var environment = new AgentTuiPathEnvironment(
            AgentTuiPlatform.Linux,
            null,
            xdgDataHome,
            "/home/operator");

        var exception = Should.Throw<InvalidOperationException>(() =>
            settings.ResolveKeyRingPath(environment));
        exception.Message.ShouldContain("XDG_DATA_HOME");
    }

    [Test]
    public void Relative_explicit_key_ring_path_is_rejected()
    {
        var settings = new AgentTuiSettings { KeyRingPath = "relative/keys" };
        var environment = new AgentTuiPathEnvironment(
            AgentTuiPlatform.Linux,
            null,
            "/var/lib/operator",
            "/home/operator");

        Should.Throw<InvalidOperationException>(() => settings.ResolveKeyRingPath(environment));
    }

    [Test]
    [Arguments(AgentTuiPlatform.Windows, AgentTuiDirectoryPermissionStrategy.WindowsAccessControl)]
    [Arguments(AgentTuiPlatform.Linux, AgentTuiDirectoryPermissionStrategy.UnixOwnerOnly)]
    [Arguments(AgentTuiPlatform.MacOS, AgentTuiDirectoryPermissionStrategy.UnixOwnerOnly)]
    [Arguments(AgentTuiPlatform.Other, AgentTuiDirectoryPermissionStrategy.Unsupported)]
    public void Owner_only_permission_strategy_is_explicit_per_platform(
        AgentTuiPlatform platform,
        AgentTuiDirectoryPermissionStrategy expected)
    {
        AgentTuiSettings.GetDirectoryPermissionStrategy(platform).ShouldBe(expected);
    }

    [Test]
    public void Key_ring_directory_applies_owner_only_permissions_on_the_current_platform()
    {
        var path = NewTempPath();
        var platform = CurrentPlatform();
        try
        {
            AgentTuiDataProtectionSetup.EnsureKeyRingDirectory(path, platform).ShouldBeTrue();

            if (OperatingSystem.IsWindows())
            {
                AssertWindowsOwnerOnly(path);
            }
            else
            {
                AssertUnixOwnerOnly(path);
            }
        }
        finally
        {
            DeleteTempPath(path);
        }
    }

    [Test]
    [Arguments(UnixFileMode.UserRead, true)]
    [Arguments(UnixFileMode.UserRead | UnixFileMode.UserWrite, true)]
    [Arguments(UnixFileMode.UserRead | UnixFileMode.GroupRead, false)]
    [Arguments(UnixFileMode.UserWrite, false)]
    public void Unix_private_key_mode_requires_owner_read_and_no_non_owner_permissions(
        UnixFileMode mode,
        bool expected)
    {
        AgentTuiDataProtectionSetup.IsUnixPrivateKeyModeOwnerOnly(mode).ShouldBe(expected);
    }

    [Test]
    public void Settings_bind_safe_probe_and_key_protection_inputs_without_credentials()
    {
        var values = new Dictionary<string, string?>
        {
            ["AgentTui:ProbeTimeoutSeconds"] = "30",
            ["AgentTui:MaxProbeOutputBytes"] = "65536",
            ["AgentTui:KeyRingPath"] = @"X:\AntiphonDataProtection\keys",
            ["AgentTui:KeyProtection:Mode"] = "X509Certificate",
            ["AgentTui:KeyProtection:CertificatePath"] = @"X:\mounted-secrets\protection.pem",
            ["AgentTui:KeyProtection:CertificatePrivateKeyPath"] = @"X:\mounted-secrets\protection-key.pem",
            ["AgentTui:KeyProtection:CertificateThumbprint"] = "0123456789ABCDEF",
            ["AgentTui:KeyProtection:CertificateStoreName"] = "My",
            ["AgentTui:KeyProtection:CertificateStoreLocation"] = "CurrentUser"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var settings = configuration.GetSection("AgentTui").Get<AgentTuiSettings>();

        settings.ShouldNotBeNull();
        settings.ProbeTimeoutSeconds.ShouldBe(30);
        settings.MaxProbeOutputBytes.ShouldBe(65_536);
        settings.MaxProbeOutputBytes.ShouldBeLessThanOrEqualTo(AgentTuiSettings.MaximumProbeOutputBytes);
        settings.KeyRingPath.ShouldBe(@"X:\AntiphonDataProtection\keys");
        settings.KeyProtection.Mode.ShouldBe(AgentTuiKeyProtectionMode.X509Certificate);
        settings.KeyProtection.CertificatePath.ShouldBe(@"X:\mounted-secrets\protection.pem");
        settings.KeyProtection.CertificatePrivateKeyPath.ShouldBe(@"X:\mounted-secrets\protection-key.pem");
        settings.KeyProtection.CertificateThumbprint.ShouldBe("0123456789ABCDEF");
        settings.KeyProtection.CertificateStoreName.ShouldBe("My");
        settings.KeyProtection.CertificateStoreLocation.ShouldBe("CurrentUser");

        var forbiddenCredentialProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ApiKey", "Credential", "Password", "Secret", "Token", "Value"
        };
        typeof(AgentTuiSettings).GetProperties()
            .Concat(typeof(AgentTuiKeyProtectionSettings).GetProperties())
            .Select(property => property.Name)
            .Where(forbiddenCredentialProperties.Contains)
            .ShouldBeEmpty();
    }

    private static ServiceProvider BuildPersistedProvider(string keyRingPath, string applicationName)
    {
        Directory.CreateDirectory(keyRingPath);
        var services = new ServiceCollection();
        services.AddDataProtection()
            .SetApplicationName(applicationName)
            .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
        return services.BuildServiceProvider();
    }

    private static DataProtectionAgentTuiSecretProtector NewReadyProtector(
        IDataProtectionProvider provider) =>
        new(provider, new AgentTuiKeyProtectionReadiness(() => true));

    private static AgentTuiSettings CertificateSettings(string certificatePath) => new()
    {
        KeyProtection = new AgentTuiKeyProtectionSettings
        {
            Mode = AgentTuiKeyProtectionMode.X509Certificate,
            CertificatePath = certificatePath
        }
    };

    private static bool IsWriteContract(Type type) =>
        type.Name.EndsWith("Request", StringComparison.Ordinal)
        || type.Name.EndsWith("Command", StringComparison.Ordinal)
        || type.Name.EndsWith("Input", StringComparison.Ordinal);

    private static AgentTuiPlatform CurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
            return AgentTuiPlatform.Windows;
        if (OperatingSystem.IsLinux())
            return AgentTuiPlatform.Linux;
        if (OperatingSystem.IsMacOS())
            return AgentTuiPlatform.MacOS;
        return AgentTuiPlatform.Other;
    }

    [SupportedOSPlatform("windows")]
    private static void AssertWindowsOwnerOnly(string path)
    {
        using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
        var user = identity.User;
        user.ShouldNotBeNull();

        var security = new DirectoryInfo(path).GetAccessControl(
            AccessControlSections.Access | AccessControlSections.Owner);
        security.AreAccessRulesProtected.ShouldBeTrue();
        security.GetOwner(typeof(SecurityIdentifier)).ShouldBe(user);
        var rules = security.GetAccessRules(
                includeExplicit: true,
                includeInherited: true,
                targetType: typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToArray();
        rules.ShouldNotBeEmpty();
        rules.ShouldAllBe(rule =>
            rule.AccessControlType == AccessControlType.Allow
            && rule.IdentityReference.Equals(user));
    }

    [UnsupportedOSPlatform("windows")]
    private static void AssertUnixOwnerOnly(string path)
    {
        File.GetUnixFileMode(path).ShouldBe(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static void MakeKeyRingDirectoryInsecure(string path)
    {
        if (OperatingSystem.IsWindows())
            MakeWindowsDirectoryInsecure(path);
        else
            File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.GroupRead);
    }

    private static void AssertKeyRingDirectoryIsInsecure(string path)
    {
        if (OperatingSystem.IsWindows())
            AssertWindowsDirectoryIsInsecure(path);
        else
            (File.GetUnixFileMode(path) & UnixFileMode.GroupRead).ShouldBe(UnixFileMode.GroupRead);
    }

    [SupportedOSPlatform("windows")]
    private static void AddCurrentUserCertificate(X509Certificate2 certificate)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        store.Add(certificate);
    }

    [SupportedOSPlatform("windows")]
    private static void RemoveCurrentUserCertificate(string thumbprint)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        foreach (var certificate in store.Certificates.Find(
                     X509FindType.FindByThumbprint,
                     thumbprint,
                     validOnly: false))
        {
            store.Remove(certificate);
            certificate.Dispose();
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool CurrentUserCertificateExists(string thumbprint)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);
        var certificates = store.Certificates.Find(
            X509FindType.FindByThumbprint,
            thumbprint,
            validOnly: false);
        try
        {
            return certificates.Count > 0;
        }
        finally
        {
            foreach (var certificate in certificates)
                certificate.Dispose();
        }
    }

    [SupportedOSPlatform("windows")]
    private static void MakeWindowsDirectoryInsecure(string path)
    {
        var security = new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, domainSid: null),
            FileSystemRights.ReadAndExecute,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(security);
    }

    [SupportedOSPlatform("windows")]
    private static void AssertWindowsDirectoryIsInsecure(string path)
    {
        using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
        var user = identity.User;
        user.ShouldNotBeNull();
        var rules = new DirectoryInfo(path)
            .GetAccessControl(AccessControlSections.Access)
            .GetAccessRules(
                includeExplicit: true,
                includeInherited: true,
                targetType: typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToArray();
        rules.ShouldContain(rule => !rule.IdentityReference.Equals(user));
    }

    [SupportedOSPlatform("windows")]
    private static void MakeWindowsFileInsecure(string path)
    {
        var security = new FileInfo(path).GetAccessControl(AccessControlSections.Access);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, domainSid: null),
            FileSystemRights.Read,
            AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security);
    }

    [SupportedOSPlatform("windows")]
    private static void AssertWindowsFileIsInsecure(string path)
    {
        using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
        var user = identity.User;
        user.ShouldNotBeNull();
        var rules = new FileInfo(path)
            .GetAccessControl(AccessControlSections.Access)
            .GetAccessRules(
                includeExplicit: true,
                includeInherited: true,
                targetType: typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToArray();
        rules.ShouldContain(rule => !rule.IdentityReference.Equals(user));
    }

    private static void AssertPrivateKeyFileOwnerOnly(string path)
    {
        if (OperatingSystem.IsWindows())
            AssertWindowsFileOwnerOnly(path);
        else
            AssertUnixFileOwnerOnly(path);
    }

    private static void MakePersistedKeyFileOwnerOnly(string path)
    {
        if (OperatingSystem.IsWindows())
            MakeWindowsFileOwnerOnly(path);
        else
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [SupportedOSPlatform("windows")]
    private static void MakeWindowsFileOwnerOnly(string path)
    {
        using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
        var user = identity.User;
        user.ShouldNotBeNull();
        var security = new FileSecurity();
        security.SetOwner(user);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            user,
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security);
    }

    [SupportedOSPlatform("windows")]
    private static void AssertWindowsFileOwnerOnly(string path)
    {
        using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
        var user = identity.User;
        user.ShouldNotBeNull();

        var security = new FileInfo(path).GetAccessControl(
            AccessControlSections.Access | AccessControlSections.Owner);
        security.AreAccessRulesProtected.ShouldBeTrue();
        security.GetOwner(typeof(SecurityIdentifier)).ShouldBe(user);
        var rules = security.GetAccessRules(
                includeExplicit: true,
                includeInherited: true,
                targetType: typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToArray();
        rules.ShouldNotBeEmpty();
        rules.ShouldAllBe(rule =>
            rule.AccessControlType == AccessControlType.Allow
            && rule.IdentityReference.Equals(user));
    }

    [UnsupportedOSPlatform("windows")]
    private static void AssertUnixFileOwnerOnly(string path)
    {
        var mode = File.GetUnixFileMode(path);
        var nonOwnerPermissions =
            UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
        (mode & nonOwnerPermissions).ShouldBe((UnixFileMode)0);
        (mode & UnixFileMode.UserRead).ShouldBe(UnixFileMode.UserRead);
    }

    private static string NewTempPath() =>
        Path.Combine(Path.GetTempPath(), $"antiphon-agent-tui-secrets-{Guid.NewGuid():N}");

    private static void WriteTestCertificate(
        string path,
        DateTimeOffset? notBefore = null,
        DateTimeOffset? notAfter = null)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Antiphon Agent TUI Test",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(
            notBefore ?? DateTimeOffset.UtcNow.AddMinutes(-1),
            notAfter ?? DateTimeOffset.UtcNow.AddDays(1));
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx));
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void SetUtcNow(DateTimeOffset value) => _utcNow = value;
    }

    private sealed class CallbackDataProtectionProvider(
        IDataProtectionProvider inner,
        Action? beforeProtect = null,
        Action? afterProtect = null,
        Action? beforeUnprotect = null,
        Action? afterUnprotect = null) : IDataProtectionProvider
    {
        public IDataProtector CreateProtector(string purpose) =>
            new CallbackDataProtector(
                inner.CreateProtector(purpose),
                beforeProtect,
                afterProtect,
                beforeUnprotect,
                afterUnprotect);

        private sealed class CallbackDataProtector(
            IDataProtector inner,
            Action? beforeProtect,
            Action? afterProtect,
            Action? beforeUnprotect,
            Action? afterUnprotect) : IDataProtector
        {
            public IDataProtector CreateProtector(string purpose) =>
                new CallbackDataProtector(
                    inner.CreateProtector(purpose),
                    beforeProtect,
                    afterProtect,
                    beforeUnprotect,
                    afterUnprotect);

            public byte[] Protect(byte[] plaintext)
            {
                beforeProtect?.Invoke();
                try
                {
                    return inner.Protect(plaintext);
                }
                finally
                {
                    afterProtect?.Invoke();
                }
            }

            public byte[] Unprotect(byte[] protectedData)
            {
                beforeUnprotect?.Invoke();
                try
                {
                    return inner.Unprotect(protectedData);
                }
                finally
                {
                    afterUnprotect?.Invoke();
                }
            }
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _entries = new();

        public IReadOnlyCollection<string> Entries => _entries.ToArray();

        public ILogger CreateLogger(string categoryName) =>
            new CapturingLogger(categoryName, _entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(
            string categoryName,
            ConcurrentQueue<string> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                entries.Enqueue(
                    $"{categoryName}|{logLevel}|{eventId}|{formatter(state, exception)}|{exception}");
            }
        }
    }

    private static void CreateDirectoryLink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return;
        }
        catch (IOException) when (OperatingSystem.IsWindows())
        {
            // Windows symbolic links require a privilege that standard CI/service accounts may not
            // have. A junction is also a reparse point and exercises the same canonicalization path.
        }

        var commandInterpreter = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = commandInterpreter,
            Arguments = $"/d /c mklink /J \"{linkPath}\" \"{targetPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }) ?? throw new InvalidOperationException("Could not start mklink for the junction test.");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new IOException(
                $"Could not create the test junction (exit code {process.ExitCode}).");
        }
    }

    private static void DeleteDirectoryLink(string linkPath)
    {
        if (Directory.Exists(linkPath))
            Directory.Delete(linkPath);
    }

    private static void DeleteTempPath(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPointW(
        string volumeMountPoint,
        StringBuilder volumeName,
        uint bufferLength);
}
