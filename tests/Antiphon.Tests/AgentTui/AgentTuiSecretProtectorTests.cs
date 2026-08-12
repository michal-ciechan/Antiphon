using System.Diagnostics;
using System.Reflection;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;
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
    [Arguments(
        AgentTuiPlatform.Linux,
        null,
        "relative/xdg",
        "/home/operator",
        "/home/operator/.local/share/antiphon/data-protection-keys")]
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
        new(provider, new AgentTuiKeyProtectionReadiness(isReady: true));

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

    private static void AssertPrivateKeyFileOwnerOnly(string path)
    {
        if (OperatingSystem.IsWindows())
            AssertWindowsFileOwnerOnly(path);
        else
            AssertUnixFileOwnerOnly(path);
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

    private static void WriteTestCertificate(string path)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Antiphon Agent TUI Test",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(1));
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx));
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
}
