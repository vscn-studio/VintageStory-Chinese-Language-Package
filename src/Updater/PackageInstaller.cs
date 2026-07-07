using System.IO.Compression;
using System.Text.Json;
using Vintagestory.API.Config;

namespace VscnLanguagePackUpdater;

internal static class PackageInstaller
{
    public static string ModsPath => GamePaths.DataPathMods;

    public static string ResolvePackagePath(string assetName)
    {
        var safeName = string.Join("_", assetName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        return Path.Combine(ModsPath, safeName);
    }

    public static void EnsureModsDirectory()
    {
        Directory.CreateDirectory(ModsPath);
    }

    public static void InstallDownloadedPackage(string downloadedPath, string targetPath)
    {
        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }

        File.Move(downloadedPath, targetPath);
    }

    public static void ValidateLanguagePackZip(string zipPath, PackageVersion expectedVersion)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var modInfoEntry = archive.GetEntry("modinfo.json")
            ?? throw new InvalidOperationException("Downloaded package does not contain modinfo.json.");

        using var stream = modInfoEntry.Open();
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;

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
}
