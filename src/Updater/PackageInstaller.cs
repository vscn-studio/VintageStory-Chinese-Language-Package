using System.IO.Compression;
using System.Text.Json;
using Vintagestory.API.Config;

namespace VscnLanguagePackUpdater;

internal static class PackageInstaller
{
    private const int MaxArchiveEntries = 4096;
    private const long MaxArchiveUncompressedBytes = 256L * 1024 * 1024;
    private const long MaxEntryUncompressedBytes = 64L * 1024 * 1024;
    private const long MaxModInfoBytes = 1024 * 1024;

    public static string ModsPath => GamePaths.DataPathMods;

    public static string ResolvePackagePath(string assetName)
    {
        if (string.IsNullOrWhiteSpace(assetName) ||
            !string.Equals(assetName, Path.GetFileName(assetName), StringComparison.Ordinal) ||
            assetName.IndexOfAny(['/', '\\']) >= 0 ||
            assetName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            string.Equals(assetName, ".", StringComparison.Ordinal) ||
            string.Equals(assetName, "..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("发布文件名包含不安全的路径字符。");
        }

        return EnsurePathInsideMods(Path.Combine(ModsPath, assetName));
    }

    public static string CreateTemporaryPackagePath(string assetName)
    {
        var targetPath = ResolvePackagePath(assetName);
        var directory = Path.GetDirectoryName(targetPath)!;
        return EnsurePathInsideMods(Path.Combine(
            directory,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.download"));
    }

    public static void EnsureModsDirectory()
    {
        Directory.CreateDirectory(ModsPath);
    }

    public static void InstallDownloadedPackage(string downloadedPath, string targetPath)
    {
        downloadedPath = EnsurePathInsideMods(downloadedPath);
        targetPath = EnsurePathInsideMods(targetPath);
        EnsureRegularFile(downloadedPath);
        if (File.Exists(targetPath))
        {
            EnsureRegularFile(targetPath);
            File.Move(downloadedPath, targetPath, overwrite: true);
            return;
        }

        File.Move(downloadedPath, targetPath);
    }

    public static void ValidateLanguagePackZip(string zipPath, PackageVersion expectedVersion)
    {
        zipPath = EnsurePathInsideMods(zipPath);
        EnsureRegularFile(zipPath);
        if (new FileInfo(zipPath).Length > 128L * 1024 * 1024)
        {
            throw new InvalidOperationException("汉化包文件超过允许的大小限制。");
        }

        using var archive = ZipFile.OpenRead(zipPath);
        if (archive.Entries.Count > MaxArchiveEntries)
        {
            throw new InvalidOperationException("汉化包包含过多 ZIP 条目。");
        }

        var entryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalUncompressedBytes = 0;
        ZipArchiveEntry? modInfoEntry = null;
        foreach (var entry in archive.Entries)
        {
            var entryName = entry.FullName;
            if (string.IsNullOrWhiteSpace(entryName) ||
                entryName.Contains('\\') ||
                entryName.StartsWith("/", StringComparison.Ordinal) ||
                Path.IsPathRooted(entryName) ||
                IsSymbolicLinkEntry(entry) ||
                (!IsAllowedPackageEntry(entryName) &&
                 !entryName.StartsWith("assets/", StringComparison.Ordinal)) ||
                entryName.Split('/', StringSplitOptions.RemoveEmptyEntries)
                    .Any(part => string.Equals(part, "..", StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("汉化包包含不安全的 ZIP 路径。");
            }

            if (!entryNames.Add(entryName))
            {
                throw new InvalidOperationException("汉化包包含重复的 ZIP 条目。");
            }

            if (entry.Length > MaxEntryUncompressedBytes ||
                (totalUncompressedBytes += entry.Length) > MaxArchiveUncompressedBytes)
            {
                throw new InvalidOperationException("汉化包解压后大小超过允许的限制。");
            }

            if (string.Equals(entryName, "modinfo.json", StringComparison.Ordinal))
            {
                modInfoEntry = entry;
            }

            if (!entry.FullName.EndsWith("/", StringComparison.Ordinal))
            {
                using var entryStream = entry.Open();
                entryStream.CopyTo(Stream.Null);
            }
        }

        if (modInfoEntry is null)
        {
            throw new InvalidOperationException("Downloaded package does not contain modinfo.json.");
        }
        if (modInfoEntry.Length > MaxModInfoBytes)
        {
            throw new InvalidOperationException("汉化包 modinfo.json 过大。");
        }

        using var stream = modInfoEntry.Open();
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;

        var type = GetString(root, "type");
        if (!string.Equals(type, "content", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("汉化包必须是 content 类型模组。");
        }

        var modId = GetString(root, "modid");
        if (!string.Equals(modId, UpdaterConstants.LanguagePackModId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Downloaded package modid is '{modId}', expected '{UpdaterConstants.LanguagePackModId}'.");
        }

        var versionText = GetString(root, "version");
        if (!PackageVersion.TryParse(versionText, out var packageVersion) ||
            packageVersion is null ||
            packageVersion.CompareTo(expectedVersion) != 0)
        {
            throw new InvalidOperationException($"Downloaded package version is '{versionText}', expected '{expectedVersion}'.");
        }
    }

    public static void DeleteOldPackages(string keepPath, string prefix, string suffix, Action<string> onCleanupFailure)
    {
        if (!Directory.Exists(ModsPath))
        {
            return;
        }

        var keepFullPath = Path.GetFullPath(keepPath);
        foreach (var path in Directory.EnumerateFiles(ModsPath, prefix + "*" + suffix, SearchOption.TopDirectoryOnly))
        {
            var fullPath = Path.GetFullPath(path);
            if (string.Equals(fullPath, keepFullPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                File.Delete(fullPath);
            }
            catch
            {
                try
                {
                    File.Move(fullPath, fullPath + ".disabled", overwrite: true);
                }
                catch (Exception ex)
                {
                    onCleanupFailure($"{Path.GetFileName(fullPath)}: {ex.Message}");
                }
            }
        }
    }

    public static PackageVersion? TryGetPackageVersionFromFileName(string fileName, string prefix, string suffix)
    {
        if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var versionText = fileName[prefix.Length..^suffix.Length];
        return PackageVersion.TryParse(versionText, out var version) ? version : null;
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(property.GetString()))
        {
            return property.GetString()!.Trim();
        }

        throw new InvalidOperationException($"Downloaded package modinfo.json did not contain '{propertyName}'.");
    }

    private static string EnsurePathInsideMods(string path)
    {
        var modsRoot = Path.GetFullPath(ModsPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(modsRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("汉化包路径不在游戏 Mods 目录内。");
        }

        return fullPath;
    }

    private static void EnsureRegularFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("汉化包文件不存在。", path);
        }

        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException("拒绝使用符号链接或重解析点作为汉化包文件。");
        }
    }

    private static bool IsSymbolicLinkEntry(ZipArchiveEntry entry)
    {
        const int UnixFileTypeMask = 0xF000;
        const int UnixSymbolicLink = 0xA000;
        return ((entry.ExternalAttributes >> 16) & UnixFileTypeMask) == UnixSymbolicLink;
    }

    private static bool IsAllowedPackageEntry(string entryName)
    {
        return string.Equals(entryName, "modinfo.json", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(entryName, "README.md", StringComparison.OrdinalIgnoreCase);
    }
}
