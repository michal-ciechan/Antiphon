using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;

namespace Antiphon.Server.Infrastructure.Agents.Tui;

public sealed class DataProtectionAgentTuiSecretProtector : IAgentTuiSecretProtector
{
    private readonly IDataProtectionProvider _provider;
    private readonly AgentTuiKeyProtectionReadiness _readiness;

    public DataProtectionAgentTuiSecretProtector(
        IDataProtectionProvider provider,
        AgentTuiKeyProtectionReadiness readiness)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
    }

    public string Protect(Guid profileId, string environmentName, string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        var protectedValue = For(profileId, environmentName).Protect(plaintext);
        EnsureReadyAfterOperation();
        return protectedValue;
    }

    public string Unprotect(Guid profileId, string environmentName, string protectedValue)
    {
        ArgumentNullException.ThrowIfNull(protectedValue);
        var plaintext = For(profileId, environmentName).Unprotect(protectedValue);
        EnsureReadyAfterOperation();
        return plaintext;
    }

    private IDataProtector For(Guid profileId, string environmentName)
    {
        if (!_readiness.RevalidateAndObserve())
        {
            throw new CryptographicException(
                "Agent TUI managed-secret protection is not ready.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);
        return _provider.CreateProtector(
            "Antiphon",
            "AgentTui",
            "ProfileSecret",
            profileId.ToString("D"),
            environmentName);
    }

    private void EnsureReadyAfterOperation()
    {
        if (!_readiness.RevalidateAndObserve())
        {
            throw new CryptographicException(
                "Agent TUI managed-secret protection is not ready.");
        }
    }
}

public sealed class AgentTuiKeyProtectionReadiness
{
    private readonly Func<bool> _evaluate;
    private readonly Func<bool> _revalidateAndObserve;

    internal AgentTuiKeyProtectionReadiness(
        Func<bool> evaluate,
        Func<bool>? revalidateAndObserve = null)
    {
        _evaluate = evaluate ?? throw new ArgumentNullException(nameof(evaluate));
        _revalidateAndObserve = revalidateAndObserve ?? evaluate;
    }

    public bool IsReady => TryEvaluate(_evaluate);

    internal bool RevalidateAndObserve() => TryEvaluate(_revalidateAndObserve);

    private static bool TryEvaluate(Func<bool> evaluate)
    {
        try
        {
            return evaluate();
        }
        catch (Exception exception) when (exception is CryptographicException
                                          or IOException
                                          or UnauthorizedAccessException
                                          or PlatformNotSupportedException
                                          or System.Security.SecurityException
                                          or ArgumentException)
        {
            return false;
        }
    }
}

internal static class AgentTuiDataProtectionSetup
{
    private const UnixFileMode OwnerOnlyUnixMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode OwnerOnlyUnixFileMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode NonOwnerUnixFilePermissions =
        UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

    public static bool Configure(
        IServiceCollection services,
        AgentTuiSettings settings,
        AgentTuiPlatform platform,
        string? keyRingPath,
        string contentRootPath,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settings);
        timeProvider ??= TimeProvider.System;

        var dataProtection = services.AddDataProtection()
            .SetApplicationName("Antiphon.AgentTui");

        var directoryReady = !string.IsNullOrWhiteSpace(keyRingPath)
            && IsOutsideDirectory(keyRingPath, contentRootPath)
            && EnsureKeyRingDirectory(keyRingPath, platform);
        if (directoryReady)
        {
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyRingPath!));
        }

        var persistedKeyReadiness = directoryReady
            ? PersistedKeyRingReadiness.TryCreate(keyRingPath!, platform)
            : null;
        var keyProtectionReadiness = directoryReady
            ? ConfigureKeyProtection(
                dataProtection,
                settings.KeyProtection,
                platform,
                keyRingPath!,
                contentRootPath,
                timeProvider)
            : null;
        var setupReady = directoryReady
            && persistedKeyReadiness is not null
            && keyProtectionReadiness is not null;
        var readiness = new AgentTuiKeyProtectionReadiness(
            () => setupReady
                && IsOutsideDirectory(keyRingPath!, contentRootPath)
                && IsKeyRingDirectoryReady(keyRingPath!, platform)
                && persistedKeyReadiness!.IsReady()
                && keyProtectionReadiness!(),
            () => setupReady
                && IsOutsideDirectory(keyRingPath!, contentRootPath)
                && IsKeyRingDirectoryReady(keyRingPath!, platform)
                && keyProtectionReadiness!()
                && persistedKeyReadiness!.RevalidateAndObserve());
        var protectionReady = readiness.IsReady;
        if (!protectionReady)
            dataProtection.DisableAutomaticKeyGeneration();

        services.AddSingleton(readiness);
        services.AddSingleton<IAgentTuiSecretProtector, DataProtectionAgentTuiSecretProtector>();
        return protectionReady;
    }

    internal static bool EnsureKeyRingDirectory(string path, AgentTuiPlatform platform)
    {
        try
        {
            return AgentTuiSettings.GetDirectoryPermissionStrategy(platform) switch
            {
                AgentTuiDirectoryPermissionStrategy.WindowsAccessControl when OperatingSystem.IsWindows() =>
                    EnsureWindowsOwnerOnly(path),
                AgentTuiDirectoryPermissionStrategy.UnixOwnerOnly when !OperatingSystem.IsWindows() =>
                    EnsureUnixOwnerOnly(path),
                _ => false
            };
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or PlatformNotSupportedException
                                          or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static bool IsKeyRingDirectoryReady(string path, AgentTuiPlatform platform)
    {
        if (!Directory.Exists(path))
            return false;

        return AgentTuiSettings.GetDirectoryPermissionStrategy(platform) switch
        {
            AgentTuiDirectoryPermissionStrategy.WindowsAccessControl when OperatingSystem.IsWindows() =>
                IsWindowsDirectoryOwnerOnly(path),
            AgentTuiDirectoryPermissionStrategy.UnixOwnerOnly when !OperatingSystem.IsWindows() =>
                File.GetUnixFileMode(path) == OwnerOnlyUnixMode,
            _ => false
        };
    }

    private sealed class PersistedKeyRingReadiness
    {
        private readonly string _keyRingPath;
        private readonly HashSet<string> _observedKeyFiles;
        private readonly object _sync = new();

        private PersistedKeyRingReadiness(
            string keyRingPath,
            AgentTuiPlatform platform)
        {
            _keyRingPath = keyRingPath;
            _observedKeyFiles = EnumerateKeyFiles().ToHashSet(
                platform == AgentTuiPlatform.Windows
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal);
        }

        public static PersistedKeyRingReadiness? TryCreate(
            string keyRingPath,
            AgentTuiPlatform platform)
        {
            try
            {
                return new PersistedKeyRingReadiness(keyRingPath, platform);
            }
            catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException
                                              or System.Security.SecurityException
                                              or ArgumentException)
            {
                return null;
            }
        }

        public bool IsReady()
        {
            lock (_sync)
            {
                var currentKeyFiles = EnumerateKeyFiles().ToHashSet(
                    _observedKeyFiles.Comparer);
                return _observedKeyFiles.IsSubsetOf(currentKeyFiles);
            }
        }

        public bool RevalidateAndObserve()
        {
            lock (_sync)
            {
                var currentKeyFiles = EnumerateKeyFiles().ToHashSet(
                    _observedKeyFiles.Comparer);
                if (!_observedKeyFiles.IsSubsetOf(currentKeyFiles))
                    return false;

                _observedKeyFiles.UnionWith(currentKeyFiles);
                return true;
            }
        }

        private IEnumerable<string> EnumerateKeyFiles() =>
            Directory.EnumerateFiles(
                    _keyRingPath,
                    "*.xml",
                    SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(fileName => !string.IsNullOrEmpty(fileName))!;
    }

    internal static bool IsOutsideDirectory(string path, string directoryPath)
    {
        try
        {
            var fullPath = ResolveExistingLinks(path);
            var fullDirectoryPath = ResolveExistingLinks(directoryPath);
            var relativePath = Path.GetRelativePath(fullDirectoryPath, fullPath);
            return relativePath != "."
                && (Path.IsPathRooted(relativePath)
                    || relativePath.Equals("..", StringComparison.Ordinal)
                    || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal));
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or IOException
                                          or NotSupportedException
                                          or UnauthorizedAccessException
                                          or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static string ResolveExistingLinks(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(root))
            throw new ArgumentException("Path must be fully qualified.", nameof(path));

        var segments = fullPath[root.Length..]
            .Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);
        var currentPath = root;
        for (var index = 0; index < segments.Length; index++)
        {
            var candidatePath = Path.Combine(currentPath, segments[index]);
            FileSystemInfo? candidate = Directory.Exists(candidatePath)
                ? new DirectoryInfo(candidatePath)
                : File.Exists(candidatePath)
                    ? new FileInfo(candidatePath)
                    : null;
            if (candidate is null)
            {
                return Path.GetFullPath(
                    Path.Combine(currentPath, Path.Combine(segments[index..])));
            }

            currentPath = candidate.ResolveLinkTarget(returnFinalTarget: true)?.FullName
                ?? candidate.FullName;
        }

        return Path.GetFullPath(currentPath);
    }

    private static Func<bool>? ConfigureKeyProtection(
        IDataProtectionBuilder builder,
        AgentTuiKeyProtectionSettings settings,
        AgentTuiPlatform platform,
        string keyRingPath,
        string contentRootPath,
        TimeProvider timeProvider)
    {
        try
        {
            var mode = ResolveProtectionMode(settings, platform);
            switch (mode)
            {
                case AgentTuiKeyProtectionMode.DpapiCurrentUser when OperatingSystem.IsWindows():
                    ProtectWithDpapi(builder, protectToLocalMachine: false);
                    return static () => true;
                case AgentTuiKeyProtectionMode.DpapiLocalMachine when OperatingSystem.IsWindows():
                    ProtectWithDpapi(builder, protectToLocalMachine: true);
                    return static () => true;
                case AgentTuiKeyProtectionMode.X509Certificate:
                    if (!EnsureCertificateKeyCustody(
                            settings,
                            platform,
                            keyRingPath,
                            contentRootPath))
                    {
                        return null;
                    }

                    var certificate = LoadCertificate(settings, timeProvider.GetUtcNow());
                    if (certificate is null)
                        return null;
                    var certificateHash = certificate.GetCertHash(HashAlgorithmName.SHA256);
                    builder.Services.AddSingleton(certificate);
                    builder.ProtectKeysWithCertificate(certificate);
                    return () => IsCertificateProtectionReady(
                        settings,
                        platform,
                        keyRingPath,
                        contentRootPath,
                        timeProvider,
                        certificateHash);
                default:
                    return null;
            }
        }
        catch (Exception exception) when (exception is CryptographicException
                                          or IOException
                                          or UnauthorizedAccessException
                                          or PlatformNotSupportedException
                                          or System.Security.SecurityException
                                          or ArgumentException)
        {
            return null;
        }
    }

    private static bool IsCertificateProtectionReady(
        AgentTuiKeyProtectionSettings settings,
        AgentTuiPlatform platform,
        string keyRingPath,
        string contentRootPath,
        TimeProvider timeProvider,
        byte[] configuredCertificateHash)
    {
        if (!IsCertificateKeyCustodyReady(
                settings,
                platform,
                keyRingPath,
                contentRootPath))
        {
            return false;
        }

        using var certificate = LoadCertificate(settings, timeProvider.GetUtcNow());
        return certificate is not null
            && CryptographicOperations.FixedTimeEquals(
                configuredCertificateHash,
                certificate.GetCertHash(HashAlgorithmName.SHA256));
    }

    private static AgentTuiKeyProtectionMode ResolveProtectionMode(
        AgentTuiKeyProtectionSettings settings,
        AgentTuiPlatform platform)
    {
        return settings.Mode switch
        {
            AgentTuiKeyProtectionMode.Auto when
                !string.IsNullOrWhiteSpace(settings.CertificatePath)
                || !string.IsNullOrWhiteSpace(settings.CertificateThumbprint) =>
                AgentTuiKeyProtectionMode.X509Certificate,
            AgentTuiKeyProtectionMode.Auto when platform == AgentTuiPlatform.Windows =>
                AgentTuiKeyProtectionMode.DpapiCurrentUser,
            AgentTuiKeyProtectionMode.Auto => AgentTuiKeyProtectionMode.Auto,
            AgentTuiKeyProtectionMode.DpapiCurrentUser => AgentTuiKeyProtectionMode.DpapiCurrentUser,
            AgentTuiKeyProtectionMode.DpapiLocalMachine => AgentTuiKeyProtectionMode.DpapiLocalMachine,
            AgentTuiKeyProtectionMode.X509Certificate => AgentTuiKeyProtectionMode.X509Certificate,
            _ => throw new CryptographicException(
                "The configured Agent TUI key-protection mode is unsupported.")
        };
    }

    private static bool EnsureCertificateKeyCustody(
        AgentTuiKeyProtectionSettings settings,
        AgentTuiPlatform platform,
        string keyRingPath,
        string contentRootPath)
    {
        if (!string.IsNullOrWhiteSpace(settings.CertificateThumbprint))
            return true;

        if (string.IsNullOrWhiteSpace(settings.CertificatePath)
            || !Path.IsPathFullyQualified(settings.CertificatePath))
        {
            return false;
        }

        var privateKeyPath = string.IsNullOrWhiteSpace(settings.CertificatePrivateKeyPath)
            ? settings.CertificatePath
            : settings.CertificatePrivateKeyPath;
        if (!Path.IsPathFullyQualified(privateKeyPath)
            || !File.Exists(privateKeyPath)
            || !IsOutsideDirectory(privateKeyPath, contentRootPath)
            || !IsOutsideDirectory(privateKeyPath, keyRingPath))
        {
            return false;
        }

        return AgentTuiSettings.GetDirectoryPermissionStrategy(platform) switch
        {
            AgentTuiDirectoryPermissionStrategy.WindowsAccessControl when OperatingSystem.IsWindows() =>
                EnsureWindowsFileOwnerOnly(privateKeyPath),
            AgentTuiDirectoryPermissionStrategy.UnixOwnerOnly when !OperatingSystem.IsWindows() =>
                EnsureUnixFileOwnerOnly(privateKeyPath),
            _ => false
        };
    }

    private static bool IsCertificateKeyCustodyReady(
        AgentTuiKeyProtectionSettings settings,
        AgentTuiPlatform platform,
        string keyRingPath,
        string contentRootPath)
    {
        if (!string.IsNullOrWhiteSpace(settings.CertificateThumbprint))
            return true;

        if (string.IsNullOrWhiteSpace(settings.CertificatePath)
            || !Path.IsPathFullyQualified(settings.CertificatePath))
        {
            return false;
        }

        var privateKeyPath = string.IsNullOrWhiteSpace(settings.CertificatePrivateKeyPath)
            ? settings.CertificatePath
            : settings.CertificatePrivateKeyPath;
        if (!Path.IsPathFullyQualified(privateKeyPath)
            || !File.Exists(privateKeyPath)
            || !IsOutsideDirectory(privateKeyPath, contentRootPath)
            || !IsOutsideDirectory(privateKeyPath, keyRingPath))
        {
            return false;
        }

        return AgentTuiSettings.GetDirectoryPermissionStrategy(platform) switch
        {
            AgentTuiDirectoryPermissionStrategy.WindowsAccessControl when OperatingSystem.IsWindows() =>
                IsWindowsFileOwnerOnly(privateKeyPath),
            AgentTuiDirectoryPermissionStrategy.UnixOwnerOnly when !OperatingSystem.IsWindows() =>
                IsUnixPrivateKeyModeOwnerOnly(File.GetUnixFileMode(privateKeyPath)),
            _ => false
        };
    }

    private static X509Certificate2? LoadCertificate(
        AgentTuiKeyProtectionSettings settings,
        DateTimeOffset utcNow)
    {
        X509Certificate2? certificate = null;
        if (!string.IsNullOrWhiteSpace(settings.CertificateThumbprint))
        {
            if (!Enum.TryParse<StoreName>(settings.CertificateStoreName, ignoreCase: true, out var storeName)
                || !Enum.TryParse<StoreLocation>(
                    settings.CertificateStoreLocation,
                    ignoreCase: true,
                    out var storeLocation))
            {
                return null;
            }

            using var store = new X509Store(storeName, storeLocation);
            store.Open(OpenFlags.ReadOnly);
            certificate = store.Certificates
                .Find(
                    X509FindType.FindByThumbprint,
                    settings.CertificateThumbprint,
                    validOnly: false)
                .OfType<X509Certificate2>()
                .FirstOrDefault(candidate => candidate.HasPrivateKey);
        }
        else if (!string.IsNullOrWhiteSpace(settings.CertificatePath))
        {
            var extension = Path.GetExtension(settings.CertificatePath);
            if (extension.Equals(".pem", StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrWhiteSpace(settings.CertificatePrivateKeyPath))
            {
                var privateKeyPath = string.IsNullOrWhiteSpace(settings.CertificatePrivateKeyPath)
                    ? settings.CertificatePath
                    : settings.CertificatePrivateKeyPath;
                certificate = X509Certificate2.CreateFromPemFile(
                    settings.CertificatePath,
                    privateKeyPath);
            }
            else
            {
                certificate = X509CertificateLoader.LoadPkcs12FromFile(
                    settings.CertificatePath,
                    password: null,
                    X509KeyStorageFlags.EphemeralKeySet);
            }
        }

        if (certificate is null
            || !certificate.HasPrivateKey
            || certificate.NotBefore.ToUniversalTime() > utcNow.UtcDateTime
            || certificate.NotAfter.ToUniversalTime() <= utcNow.UtcDateTime)
        {
            certificate?.Dispose();
            return null;
        }

        using var publicKey = certificate.GetRSAPublicKey();
        using var privateKey = certificate.GetRSAPrivateKey();
        if (publicKey is null || privateKey is null)
        {
            certificate.Dispose();
            return null;
        }

        return certificate;
    }

    [SupportedOSPlatform("windows")]
    private static void ProtectWithDpapi(
        IDataProtectionBuilder builder,
        bool protectToLocalMachine) =>
        builder.ProtectKeysWithDpapi(protectToLocalMachine);

    [SupportedOSPlatform("windows")]
    private static bool EnsureWindowsOwnerOnly(string path)
    {
        Directory.CreateDirectory(path);
        using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
        var user = identity.User;
        if (user is null)
            return false;

        var security = new DirectorySecurity();
        security.SetOwner(user);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            user,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        var directory = new DirectoryInfo(path);
        directory.SetAccessControl(security);

        return IsWindowsDirectoryOwnerOnly(path);
    }

    [SupportedOSPlatform("windows")]
    private static bool IsWindowsDirectoryOwnerOnly(string path)
    {
        using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
        var user = identity.User;
        if (user is null)
            return false;

        var directory = new DirectoryInfo(path);
        var applied = directory.GetAccessControl(
            AccessControlSections.Access | AccessControlSections.Owner);
        var appliedOwner = applied.GetOwner(typeof(SecurityIdentifier));
        if (!applied.AreAccessRulesProtected
            || appliedOwner is null
            || !appliedOwner.Equals(user))
        {
            return false;
        }

        var rules = applied.GetAccessRules(
                includeExplicit: true,
                includeInherited: true,
                targetType: typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToArray();
        return rules.Length > 0
            && rules.All(rule =>
                rule.AccessControlType == AccessControlType.Allow
                && rule.IdentityReference.Equals(user));
    }

    [SupportedOSPlatform("windows")]
    private static bool EnsureWindowsFileOwnerOnly(string path)
    {
        using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
        var user = identity.User;
        if (user is null)
            return false;

        var security = new FileSecurity();
        security.SetOwner(user);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            user,
            FileSystemRights.FullControl,
            AccessControlType.Allow));

        var file = new FileInfo(path);
        file.SetAccessControl(security);

        return IsWindowsFileOwnerOnly(path);
    }

    [SupportedOSPlatform("windows")]
    private static bool IsWindowsFileOwnerOnly(string path)
    {
        using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
        var user = identity.User;
        if (user is null)
            return false;

        var file = new FileInfo(path);
        var applied = file.GetAccessControl(
            AccessControlSections.Access | AccessControlSections.Owner);
        var appliedOwner = applied.GetOwner(typeof(SecurityIdentifier));
        if (!applied.AreAccessRulesProtected
            || appliedOwner is null
            || !appliedOwner.Equals(user))
        {
            return false;
        }

        var rules = applied.GetAccessRules(
                includeExplicit: true,
                includeInherited: true,
                targetType: typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToArray();
        return rules.Length > 0
            && rules.All(rule =>
                rule.AccessControlType == AccessControlType.Allow
                && rule.IdentityReference.Equals(user));
    }

    [UnsupportedOSPlatform("windows")]
    private static bool EnsureUnixOwnerOnly(string path)
    {
        if (Directory.Exists(path))
            File.SetUnixFileMode(path, OwnerOnlyUnixMode);
        else
            Directory.CreateDirectory(path, OwnerOnlyUnixMode);

        return File.GetUnixFileMode(path) == OwnerOnlyUnixMode;
    }

    [UnsupportedOSPlatform("windows")]
    private static bool EnsureUnixFileOwnerOnly(string path)
    {
        if (IsUnixPrivateKeyModeOwnerOnly(File.GetUnixFileMode(path)))
            return true;

        File.SetUnixFileMode(path, OwnerOnlyUnixFileMode);
        return IsUnixPrivateKeyModeOwnerOnly(File.GetUnixFileMode(path));
    }

    internal static bool IsUnixPrivateKeyModeOwnerOnly(UnixFileMode mode) =>
        (mode & UnixFileMode.UserRead) == UnixFileMode.UserRead
        && (mode & NonOwnerUnixFilePermissions) == 0;
}
