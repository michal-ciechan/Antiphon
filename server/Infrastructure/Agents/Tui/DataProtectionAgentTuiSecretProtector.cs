using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32.SafeHandles;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
        EnsureReadyAfterOperation(GetPayloadKeyId(protectedValue));
        return protectedValue;
    }

    public string Unprotect(Guid profileId, string environmentName, string protectedValue)
    {
        ArgumentNullException.ThrowIfNull(protectedValue);
        var keyId = GetPayloadKeyId(protectedValue);
        var plaintext = For(profileId, environmentName, keyId).Unprotect(protectedValue);
        EnsureReadyAfterOperation(keyId);
        return plaintext;
    }

    private IDataProtector For(
        Guid profileId,
        string environmentName,
        Guid? requiredKeyId = null)
    {
        var isReady = requiredKeyId.HasValue
            ? _readiness.RevalidateAndObserveBeforeOperation(requiredKeyId.Value)
            : _readiness.RevalidateAndObserveBeforeOperation();
        if (!isReady)
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

    private void EnsureReadyAfterOperation(Guid requiredKeyId)
    {
        if (!_readiness.RevalidateAndObserveAfterOperation(requiredKeyId))
        {
            throw new CryptographicException(
                "Agent TUI managed-secret protection is not ready.");
        }
    }

    private static Guid GetPayloadKeyId(string protectedValue)
    {
        const int EncodedPrefixLength = 28;
        const int DecodedPrefixLength = 21;
        if (protectedValue.Length < EncodedPrefixLength)
            throw new CryptographicException("Agent TUI managed-secret payload is invalid.");

        try
        {
            var header = WebEncoders.Base64UrlDecode(protectedValue[..EncodedPrefixLength]);
            if (header.Length != DecodedPrefixLength
                || header[0] != 0x09
                || header[1] != 0xF0
                || header[2] != 0xC9
                || header[3] != 0xF0)
            {
                throw new CryptographicException("Agent TUI managed-secret payload is invalid.");
            }

            return new Guid(header.AsSpan(4, 16));
        }
        catch (FormatException exception)
        {
            throw new CryptographicException(
                "Agent TUI managed-secret payload is invalid.",
                exception);
        }
    }
}

public sealed class AgentTuiKeyProtectionReadiness
{
    private readonly Func<bool> _evaluate;
    private readonly Func<bool> _revalidateAndObserveBeforeOperation;
    private readonly Func<bool> _revalidateAndObserveAfterOperation;
    private readonly Func<Guid, bool> _revalidateAndObserveBeforeOperationForKey;
    private readonly Func<Guid, bool> _revalidateAndObserveAfterOperationForKey;

    internal AgentTuiKeyProtectionReadiness(
        Func<bool> evaluate,
        Func<bool>? revalidateAndObserveBeforeOperation = null,
        Func<bool>? revalidateAndObserveAfterOperation = null,
        Func<Guid, bool>? revalidateAndObserveBeforeOperationForKey = null,
        Func<Guid, bool>? revalidateAndObserveAfterOperationForKey = null)
    {
        _evaluate = evaluate ?? throw new ArgumentNullException(nameof(evaluate));
        _revalidateAndObserveBeforeOperation = revalidateAndObserveBeforeOperation ?? evaluate;
        _revalidateAndObserveAfterOperation = revalidateAndObserveAfterOperation
            ?? _revalidateAndObserveBeforeOperation;
        _revalidateAndObserveBeforeOperationForKey = revalidateAndObserveBeforeOperationForKey
            ?? (_ => _revalidateAndObserveBeforeOperation());
        _revalidateAndObserveAfterOperationForKey = revalidateAndObserveAfterOperationForKey
            ?? (_ => _revalidateAndObserveAfterOperation());
    }

    public bool IsReady => TryEvaluate(_evaluate);

    internal bool RevalidateAndObserveBeforeOperation() =>
        TryEvaluate(_revalidateAndObserveBeforeOperation);

    internal bool RevalidateAndObserveAfterOperation() =>
        TryEvaluate(_revalidateAndObserveAfterOperation);

    internal bool RevalidateAndObserveBeforeOperation(Guid requiredKeyId) =>
        TryEvaluate(() => _revalidateAndObserveBeforeOperationForKey(requiredKeyId));

    internal bool RevalidateAndObserveAfterOperation(Guid requiredKeyId) =>
        TryEvaluate(() => _revalidateAndObserveAfterOperationForKey(requiredKeyId));

    private static bool TryEvaluate(Func<bool> evaluate)
    {
        try
        {
            return evaluate();
        }
        catch (Exception exception) when (exception is CryptographicException
                                          or InvalidDataException
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
    private const int MaxKeyXmlBytes = 1024 * 1024;
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
        TimeProvider? timeProvider = null,
        Func<string, byte[]>? keyFileReader = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settings);
        timeProvider ??= TimeProvider.System;

        var dataProtection = services.AddDataProtection()
            .SetApplicationName("Antiphon.AgentTui");

        var directoryReady = !string.IsNullOrWhiteSpace(keyRingPath)
            && IsOutsideDirectory(keyRingPath, contentRootPath, platform)
            && EnsureKeyRingDirectory(keyRingPath, platform);
        if (directoryReady)
        {
            dataProtection.Services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(sp =>
            {
                var loggerFactory = sp.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance;
                return new ConfigureOptions<KeyManagementOptions>(options =>
                {
                    options.XmlRepository = new CustodyEnforcingFileSystemXmlRepository(
                        new DirectoryInfo(keyRingPath!),
                        loggerFactory,
                        platform);
                });
            });
        }

        var persistedKeyReadiness = directoryReady
            ? PersistedKeyRingReadiness.TryCreate(keyRingPath!, platform, keyFileReader)
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
        var protectionReady = setupReady
            && IsOutsideDirectory(keyRingPath!, contentRootPath, platform)
            && IsKeyRingDirectoryReady(keyRingPath!, platform)
            && keyProtectionReadiness!();
        if (!protectionReady)
            dataProtection.DisableAutomaticKeyGeneration();

        services.AddSingleton<AgentTuiKeyProtectionReadiness>(sp =>
        {
            var keyManager = sp.GetRequiredService<IKeyManager>();
            return new AgentTuiKeyProtectionReadiness(
                () => setupReady
                    && IsOutsideDirectory(keyRingPath!, contentRootPath, platform)
                    && IsKeyRingDirectoryReady(keyRingPath!, platform)
                    && persistedKeyReadiness!.IsReady(keyManager)
                    && keyProtectionReadiness!(),
                () => setupReady
                    && IsOutsideDirectory(keyRingPath!, contentRootPath, platform)
                    && IsKeyRingDirectoryReady(keyRingPath!, platform)
                    && keyProtectionReadiness!()
                    && persistedKeyReadiness!.RevalidateAndObserve(
                        keyManager,
                        requireKey: false),
                () => setupReady
                    && IsOutsideDirectory(keyRingPath!, contentRootPath, platform)
                    && IsKeyRingDirectoryReady(keyRingPath!, platform)
                    && keyProtectionReadiness!()
                    && persistedKeyReadiness!.RevalidateAndObserve(
                        keyManager,
                        requireKey: true),
                requiredKeyId => setupReady
                    && IsOutsideDirectory(keyRingPath!, contentRootPath, platform)
                    && IsKeyRingDirectoryReady(keyRingPath!, platform)
                    && keyProtectionReadiness!()
                    && persistedKeyReadiness!.RevalidateAndObserve(
                        keyManager,
                        requireKey: true,
                        requiredKeyId: requiredKeyId),
                requiredKeyId => setupReady
                    && IsOutsideDirectory(keyRingPath!, contentRootPath, platform)
                    && IsKeyRingDirectoryReady(keyRingPath!, platform)
                    && keyProtectionReadiness!()
                    && persistedKeyReadiness!.RevalidateAndObserve(
                        keyManager,
                        requireKey: true,
                        requiredKeyId: requiredKeyId));
        });
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
        private readonly AgentTuiPlatform _platform;
        private readonly Func<string, Stream> _openKeyFile;
        private readonly Dictionary<string, PersistedXmlSnapshot> _observedXmlFiles;
        private readonly object _sync = new();

        private PersistedKeyRingReadiness(
            string keyRingPath,
            AgentTuiPlatform platform,
            Func<string, byte[]>? readKeyFile)
        {
            _keyRingPath = keyRingPath;
            _platform = platform;
            _openKeyFile = readKeyFile is null
                ? OpenKeyFileForRead
                : path => new MemoryStream(readKeyFile(path), writable: false);
            _observedXmlFiles = SnapshotXmlFiles(
                platform == AgentTuiPlatform.Windows
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal);
        }

        public static PersistedKeyRingReadiness? TryCreate(
            string keyRingPath,
            AgentTuiPlatform platform,
            Func<string, byte[]>? readKeyFile)
        {
            try
            {
                return new PersistedKeyRingReadiness(keyRingPath, platform, readKeyFile);
            }
            catch (Exception exception) when (exception is IOException
                                              or InvalidDataException
                                              or UnauthorizedAccessException
                                              or PlatformNotSupportedException
                                              or System.Security.SecurityException
                                              or ArgumentException)
            {
                return null;
            }
        }

        public bool IsReady(IKeyManager keyManager)
        {
            lock (_sync)
            {
                var currentXmlFiles = SnapshotXmlFiles(
                    _observedXmlFiles.Comparer);
                return ObservedXmlFilesMatch(currentXmlFiles)
                    && IsAuthoritativeKeySetReady(currentXmlFiles, keyManager);
            }
        }

        public bool RevalidateAndObserve(
            IKeyManager keyManager,
            bool requireKey,
            Guid? requiredKeyId = null)
        {
            lock (_sync)
            {
                var currentXmlFiles = SnapshotXmlFiles(
                    _observedXmlFiles.Comparer);
                var currentKeyCount = currentXmlFiles.Values.Count(
                    snapshot => snapshot.Kind == PersistedXmlKind.Key);
                if (requireKey && currentKeyCount == 0)
                    return false;
                if (requiredKeyId.HasValue
                    && !currentXmlFiles.Values.Any(snapshot =>
                        snapshot.Kind == PersistedXmlKind.Key
                        && snapshot.KeyId == requiredKeyId))
                {
                    return false;
                }
                if (!ObservedXmlFilesMatch(currentXmlFiles)
                    || !IsAuthoritativeKeySetReady(currentXmlFiles, keyManager))
                {
                    return false;
                }

                foreach (var currentXmlFile in currentXmlFiles)
                    _observedXmlFiles.TryAdd(currentXmlFile.Key, currentXmlFile.Value);
                return true;
            }
        }

        private bool ObservedXmlFilesMatch(
            IReadOnlyDictionary<string, PersistedXmlSnapshot> currentXmlFiles)
        {
            foreach (var observedXmlFile in _observedXmlFiles)
            {
                if (!currentXmlFiles.TryGetValue(observedXmlFile.Key, out var currentSnapshot)
                    || !CryptographicOperations.FixedTimeEquals(
                        observedXmlFile.Value.Fingerprint,
                        currentSnapshot.Fingerprint))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsAuthoritativeKeySetReady(
            IReadOnlyDictionary<string, PersistedXmlSnapshot> currentXmlFiles,
            IKeyManager keyManager)
        {
            var persistedKeyIds = currentXmlFiles.Values
                .Where(snapshot => snapshot.Kind == PersistedXmlKind.Key)
                .Select(snapshot => snapshot.KeyId!.Value)
                .ToArray();
            if (persistedKeyIds.Distinct().Count() != persistedKeyIds.Length)
                return false;

            var loadedKeys = keyManager.GetAllKeys().ToArray();
            var loadedKeyIds = loadedKeys.Select(key => key.KeyId).ToArray();
            if (loadedKeyIds.Distinct().Count() != loadedKeyIds.Length
                || !persistedKeyIds.ToHashSet().SetEquals(loadedKeyIds))
            {
                return false;
            }

            foreach (var loadedKey in loadedKeys)
            {
                if (loadedKey.CreateEncryptor() is null)
                    return false;
            }

            return true;
        }

        private Dictionary<string, PersistedXmlSnapshot> SnapshotXmlFiles(
            IEqualityComparer<string> comparer) =>
            Directory.EnumerateFiles(
                    _keyRingPath,
                    "*.xml",
                    SearchOption.TopDirectoryOnly)
                .ToDictionary(
                    path => Path.GetFileName(path),
                    ReadXmlSnapshot,
                    comparer);

        private static Stream OpenKeyFileForRead(string path) => new FileStream(
            path,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.SequentialScan
            });

        private PersistedXmlSnapshot ReadXmlSnapshot(string path)
        {
            if (!IsPersistedXmlFileOwnerOnly(path, _platform))
                throw new InvalidDataException("Persisted key XML custody is invalid.");

            using var input = _openKeyFile(path);
            if (!input.CanRead)
                throw new InvalidDataException("Persisted key XML is not readable.");
            if (input.CanSeek && input.Length > MaxKeyXmlBytes)
                throw new InvalidDataException("Persisted key XML exceeds the size limit.");

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var captured = new MemoryStream();
            var buffer = new byte[16 * 1024];
            try
            {
                var totalBytes = 0;
                int bytesRead;
                while ((bytesRead = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    totalBytes = checked(totalBytes + bytesRead);
                    if (totalBytes > MaxKeyXmlBytes)
                        throw new InvalidDataException("Persisted key XML exceeds the size limit.");

                    hash.AppendData(buffer, 0, bytesRead);
                    captured.Write(buffer, 0, bytesRead);
                }

                captured.Position = 0;
                var document = LoadSafeXml(captured);
                var (kind, keyId) = ClassifyXml(document);
                return new PersistedXmlSnapshot(hash.GetHashAndReset(), kind, keyId);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(buffer);
                if (captured.TryGetBuffer(out var capturedBytes))
                    CryptographicOperations.ZeroMemory(capturedBytes.AsSpan());
            }
        }

        private static XDocument LoadSafeXml(Stream input)
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                MaxCharactersInDocument = MaxKeyXmlBytes,
                XmlResolver = null
            };
            try
            {
                using var reader = XmlReader.Create(input, settings);
                return XDocument.Load(reader, LoadOptions.None);
            }
            catch (XmlException exception)
            {
                throw new InvalidDataException("Persisted key XML is invalid.", exception);
            }
        }

        private static (PersistedXmlKind Kind, Guid? KeyId) ClassifyXml(XDocument document)
        {
            var root = document.Root
                ?? throw new InvalidDataException("Persisted key XML has no root element.");
            if (!root.Name.NamespaceName.Equals(string.Empty, StringComparison.Ordinal)
                || !string.Equals((string?)root.Attribute("version"), "1", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Persisted key XML has an unsupported shape.");
            }

            if (root.Name.LocalName.Equals("key", StringComparison.Ordinal))
            {
                if (!Guid.TryParse((string?)root.Attribute("id"), out var keyId))
                    throw new InvalidDataException("Persisted key XML has an invalid key identifier.");

                return (PersistedXmlKind.Key, keyId);
            }

            if (root.Name.LocalName.Equals("revocation", StringComparison.Ordinal))
            {
                var revocationDate = (string?)root.Element("revocationDate");
                var revokedKeyId = (string?)root.Element("key")?.Attribute("id");
                if (!DateTimeOffset.TryParse(
                        revocationDate,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out _)
                    || (revokedKeyId != "*" && !Guid.TryParse(revokedKeyId, out _)))
                {
                    throw new InvalidDataException("Persisted revocation XML is invalid.");
                }

                return (PersistedXmlKind.Revocation, null);
            }

            throw new InvalidDataException("Persisted key XML has an unsupported root element.");
        }

        private enum PersistedXmlKind
        {
            Key,
            Revocation
        }

        private sealed record PersistedXmlSnapshot(
            byte[] Fingerprint,
            PersistedXmlKind Kind,
            Guid? KeyId);
    }

    private sealed class CustodyEnforcingFileSystemXmlRepository : IDeletableXmlRepository
    {
        private readonly DirectoryInfo _directory;
        private readonly FileSystemXmlRepository _inner;
        private readonly AgentTuiPlatform _platform;

        public CustodyEnforcingFileSystemXmlRepository(
            DirectoryInfo directory,
            ILoggerFactory loggerFactory,
            AgentTuiPlatform platform)
        {
            _directory = directory;
            // Framework repository messages include physical key-ring paths and filenames.
            _inner = new FileSystemXmlRepository(directory, NullLoggerFactory.Instance);
            _platform = platform;
        }

        public IReadOnlyCollection<XElement> GetAllElements()
        {
            foreach (var path in Directory.EnumerateFiles(
                         _directory.FullName,
                         "*.xml",
                         SearchOption.TopDirectoryOnly))
            {
                if (!IsPersistedXmlFileOwnerOnly(path, _platform))
                    throw new CryptographicException("Persisted key XML custody is invalid.");
            }

            return _inner.GetAllElements();
        }

        public void StoreElement(XElement element, string friendlyName)
        {
            if (!IsSafeFileName(friendlyName))
                throw new ArgumentException("Persisted key XML filename is invalid.", nameof(friendlyName));

            _inner.StoreElement(element, friendlyName);
            var path = Path.Combine(_directory.FullName, $"{friendlyName}.xml");
            if (!EnsurePersistedXmlFileOwnerOnly(path, _platform))
                throw new CryptographicException("Persisted key XML custody could not be secured.");
        }

        public bool DeleteElements(Action<IReadOnlyCollection<IDeletableElement>> chooseElements) =>
            _inner.DeleteElements(chooseElements);

        private static bool IsSafeFileName(string value) =>
            !string.IsNullOrEmpty(value)
            && value.All(character => character is '-'
                or '_'
                or >= '0' and <= '9'
                or >= 'A' and <= 'Z'
                or >= 'a' and <= 'z');
    }

    internal static bool IsOutsideDirectory(string path, string directoryPath) =>
        IsOutsideDirectory(path, directoryPath, CurrentPlatform());

    private static bool IsOutsideDirectory(
        string path,
        string directoryPath,
        AgentTuiPlatform platform)
    {
        try
        {
            var fullPath = ResolveCanonicalPath(path);
            var fullDirectoryPath = ResolveCanonicalPath(directoryPath);
            return IsCanonicalPathOutsideDirectory(fullPath, fullDirectoryPath, platform);
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

    internal static bool IsCanonicalPathOutsideDirectory(
        string canonicalPath,
        string canonicalDirectoryPath,
        AgentTuiPlatform platform)
    {
        var comparison = platform is AgentTuiPlatform.Windows or AgentTuiPlatform.MacOS
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var normalizedPath = canonicalPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedDirectoryPath = canonicalDirectoryPath.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        if (normalizedPath.Equals(normalizedDirectoryPath, comparison))
            return false;

        return normalizedPath.Length <= normalizedDirectoryPath.Length
            || !normalizedPath.StartsWith(normalizedDirectoryPath, comparison)
            || !IsDirectorySeparator(normalizedPath[normalizedDirectoryPath.Length]);
    }

    private static bool IsDirectorySeparator(char value) => value is '/' or '\\';

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

    private static string ResolveCanonicalPath(string path)
    {
        var fullPath = Path.GetFullPath(
            OperatingSystem.IsWindows()
                ? NormalizeWindowsExtendedPath(path)
                : path);
        return OperatingSystem.IsWindows()
            ? ResolveWindowsNativePath(fullPath)
            : ResolveExistingLinks(fullPath);
    }

    [SupportedOSPlatform("windows")]
    private static string ResolveWindowsNativePath(string fullPath)
    {
        var unresolvedSegments = new Stack<string>();
        var existingPath = fullPath;
        while (!Directory.Exists(existingPath) && !File.Exists(existingPath))
        {
            var segment = Path.GetFileName(existingPath);
            var parentPath = Path.GetDirectoryName(existingPath);
            if (string.IsNullOrEmpty(segment)
                || string.IsNullOrEmpty(parentPath)
                || parentPath.Equals(existingPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Path has no accessible existing ancestor.");
            }

            unresolvedSegments.Push(segment);
            existingPath = parentPath;
        }

        var canonicalPath = GetFinalWindowsPath(existingPath);
        while (unresolvedSegments.TryPop(out var unresolvedSegment))
            canonicalPath = Path.Combine(canonicalPath, unresolvedSegment);
        return Path.GetFullPath(canonicalPath);
    }

    [SupportedOSPlatform("windows")]
    private static string GetFinalWindowsPath(string path)
    {
        const uint FileFlagBackupSemantics = 0x02000000;
        using var handle = CreateFileW(
            path,
            desiredAccess: 0,
            FileShare.ReadWrite | FileShare.Delete,
            securityAttributes: IntPtr.Zero,
            FileMode.Open,
            FileFlagBackupSemantics,
            templateFile: IntPtr.Zero);
        if (handle.IsInvalid)
        {
            throw new IOException(
                $"Could not open path for canonicalization (Win32 error {Marshal.GetLastWin32Error()}).");
        }

        var buffer = new StringBuilder(512);
        while (true)
        {
            var length = GetFinalPathNameByHandleW(
                handle,
                buffer,
                checked((uint)buffer.Capacity),
                flags: 0);
            if (length == 0)
            {
                throw new IOException(
                    $"Could not canonicalize path (Win32 error {Marshal.GetLastWin32Error()}).");
            }
            if (length < buffer.Capacity)
                return NormalizeWindowsExtendedPath(buffer.ToString());

            buffer.EnsureCapacity(checked((int)length + 1));
        }
    }

    private static string NormalizeWindowsExtendedPath(string path)
    {
        const string ExtendedUncPrefix = @"\\?\UNC\";
        const string ExtendedPrefix = @"\\?\";
        if (path.StartsWith(ExtendedUncPrefix, StringComparison.OrdinalIgnoreCase))
            return @"\\" + path[ExtendedUncPrefix.Length..];
        if (path.StartsWith(ExtendedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var unprefixed = path[ExtendedPrefix.Length..];
            if (unprefixed.Length >= 3
                && char.IsAsciiLetter(unprefixed[0])
                && unprefixed[1] == ':'
                && IsDirectorySeparator(unprefixed[2]))
            {
                return unprefixed;
            }
        }
        return path;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        FileShare shareMode,
        IntPtr securityAttributes,
        FileMode creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(
        SafeFileHandle file,
        StringBuilder filePath,
        uint filePathLength,
        uint flags);

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
            || !IsOutsideDirectory(privateKeyPath, contentRootPath, platform)
            || !IsOutsideDirectory(privateKeyPath, keyRingPath, platform))
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
            || !IsOutsideDirectory(privateKeyPath, contentRootPath, platform)
            || !IsOutsideDirectory(privateKeyPath, keyRingPath, platform))
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
            var candidates = store.Certificates.Find(
                X509FindType.FindByThumbprint,
                settings.CertificateThumbprint,
                validOnly: false);
            certificate = SelectPrivateKeyCertificateAndDisposeOthers(candidates);
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

    internal static X509Certificate2? SelectPrivateKeyCertificateAndDisposeOthers(
        X509Certificate2Collection candidates)
    {
        X509Certificate2? selected = null;
        try
        {
            foreach (var candidate in candidates)
            {
                if (selected is null && candidate.HasPrivateKey)
                    selected = candidate;
                else
                    candidate.Dispose();
            }

            return selected;
        }
        catch
        {
            foreach (var candidate in candidates)
            {
                if (!ReferenceEquals(candidate, selected))
                    candidate.Dispose();
            }
            selected?.Dispose();
            throw;
        }
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

    private static bool EnsurePersistedXmlFileOwnerOnly(
        string path,
        AgentTuiPlatform platform)
    {
        if (!File.Exists(path)
            || !IsPersistedXmlFileAttributeSetSafe(File.GetAttributes(path)))
        {
            return false;
        }

        return AgentTuiSettings.GetDirectoryPermissionStrategy(platform) switch
        {
            AgentTuiDirectoryPermissionStrategy.WindowsAccessControl when OperatingSystem.IsWindows() =>
                EnsureWindowsFileOwnerOnly(path),
            AgentTuiDirectoryPermissionStrategy.UnixOwnerOnly when !OperatingSystem.IsWindows() =>
                EnsureUnixFileOwnerOnly(path),
            _ => false
        };
    }

    private static bool IsPersistedXmlFileOwnerOnly(
        string path,
        AgentTuiPlatform platform)
    {
        if (!File.Exists(path)
            || !IsPersistedXmlFileAttributeSetSafe(File.GetAttributes(path)))
        {
            return false;
        }

        return AgentTuiSettings.GetDirectoryPermissionStrategy(platform) switch
        {
            AgentTuiDirectoryPermissionStrategy.WindowsAccessControl when OperatingSystem.IsWindows() =>
                IsWindowsFileOwnerOnly(path),
            AgentTuiDirectoryPermissionStrategy.UnixOwnerOnly when !OperatingSystem.IsWindows() =>
                IsUnixPersistedKeyModeOwnerOnly(File.GetUnixFileMode(path)),
            _ => false
        };
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

    internal static bool IsUnixPersistedKeyModeOwnerOnly(UnixFileMode mode) =>
        mode == OwnerOnlyUnixFileMode;

    internal static bool IsPersistedXmlFileAttributeSetSafe(FileAttributes attributes) =>
        (attributes & FileAttributes.ReparsePoint) == 0;
}
