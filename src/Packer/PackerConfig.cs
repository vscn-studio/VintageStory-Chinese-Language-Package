using System.Text.Json.Serialization;

namespace Packer;

public sealed class PackerConfig
{
    [JsonPropertyName("packageName")]
    public string PackageName { get; set; } = "VSCN Vintage Story 汉化包";

    [JsonPropertyName("packageVersion")]
    public string PackageVersion { get; set; } = "0.0.0";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "聚合简体中文语言包，覆盖 Vintage Story 游戏本体和已安装的受支持模组。";

    [JsonPropertyName("authors")]
    public string[] Authors { get; set; } = ["VSCN-Studio"];

    [JsonPropertyName("modId")]
    public string ModId { get; set; } = "vscnlangpack";

    [JsonPropertyName("targetLanguage")]
    public string TargetLanguage { get; set; } = "zh-cn";

    [JsonPropertyName("contentRoot")]
    public string ContentRoot { get; set; } = "projects/assets";

    [JsonPropertyName("gameTranslation")]
    public GameTranslationConfig? GameTranslation { get; set; }

    [JsonPropertyName("outputDirectory")]
    public string OutputDirectory { get; set; } = "build";

    [JsonPropertyName("outputFileNameTemplate")]
    public string OutputFileNameTemplate { get; set; } = "VintageStory-Chinese-Language-Package-{version}.zip";

    [JsonPropertyName("excludedProjects")]
    public string[] ExcludedProjects { get; set; } = [];

    [JsonPropertyName("excludedModIds")]
    public string[] ExcludedModIds { get; set; } = [];

    [JsonPropertyName("excludedVersions")]
    public string[] ExcludedVersions { get; set; } = [];

    [JsonPropertyName("versionSelectionStrategy")]
    public string VersionSelectionStrategy { get; set; } = "highest-semver";

    public void ApplyDefaults()
    {
        PackageName = NormalizeOrDefault(PackageName, "VSCN Vintage Story 汉化包");
        PackageVersion = NormalizeOrDefault(PackageVersion, "0.0.0");
        Description = NormalizeOrDefault(Description, "聚合简体中文语言包，覆盖 Vintage Story 游戏本体和已安装的受支持模组。");
        Authors = NormalizeList(Authors, "VSCN-Studio");
        ModId = NormalizeOrDefault(ModId, "vscnlangpack");
        TargetLanguage = NormalizeOrDefault(TargetLanguage, "zh-cn");
        ContentRoot = NormalizeOrDefault(ContentRoot, "projects/assets");
        GameTranslation?.ApplyDefaults();
        OutputDirectory = NormalizeOrDefault(OutputDirectory, "build");
        OutputFileNameTemplate = NormalizeOrDefault(OutputFileNameTemplate, "VintageStory-Chinese-Language-Package-{version}.zip");
        ExcludedProjects = NormalizeList(ExcludedProjects);
        ExcludedModIds = NormalizeList(ExcludedModIds);
        ExcludedVersions = NormalizeList(ExcludedVersions);
        VersionSelectionStrategy = NormalizeOrDefault(VersionSelectionStrategy, "highest-semver");
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(PackageName))
        {
            throw new PackerException("packageName must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(PackageVersion))
        {
            throw new PackerException("packageVersion must not be empty.");
        }

        if (!PackageVersionCalculator.IsSupportedPackageVersion(PackageVersion))
        {
            throw new PackerException(
                $"packageVersion must match the Vintage Story mod site version format 'X.Y.Z' or 'X.Y.Z-(dev|pre|rc).N', got '{PackageVersion}'.");
        }

        if (string.IsNullOrWhiteSpace(ModId))
        {
            throw new PackerException("modId must not be empty.");
        }

        if (Authors.Length == 0)
        {
            throw new PackerException("authors must contain at least one value.");
        }

        if (string.IsNullOrWhiteSpace(TargetLanguage))
        {
            throw new PackerException("targetLanguage must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(ContentRoot))
        {
            throw new PackerException("contentRoot must not be empty.");
        }

        GameTranslation?.Validate();

        if (string.IsNullOrWhiteSpace(OutputDirectory))
        {
            throw new PackerException("outputDirectory must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(OutputFileNameTemplate))
        {
            throw new PackerException("outputFileNameTemplate must not be empty.");
        }

        if (!OutputFileNameTemplate.Contains("{version}", StringComparison.Ordinal))
        {
            throw new PackerException("outputFileNameTemplate must contain '{version}'.");
        }

        if (!string.Equals(VersionSelectionStrategy, "highest-semver", StringComparison.OrdinalIgnoreCase))
        {
            throw new PackerException(
                $"Unsupported versionSelectionStrategy '{VersionSelectionStrategy}'. Only 'highest-semver' is supported.");
        }
    }

    public string ResolveOutputFileName()
    {
        return OutputFileNameTemplate.Replace("{version}", PackageVersion, StringComparison.Ordinal);
    }

    public HashSet<string> GetExcludedProjectsSet() => new(ExcludedProjects, StringComparer.OrdinalIgnoreCase);

    public HashSet<string> GetExcludedModIdsSet() => new(ExcludedModIds, StringComparer.OrdinalIgnoreCase);

    public HashSet<string> GetExcludedVersionsSet() => new(ExcludedVersions, StringComparer.OrdinalIgnoreCase);

    private static string NormalizeOrDefault(string? value, string defaultValue)
    {
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
    }

    private static string[] NormalizeList(IEnumerable<string>? values, params string[] fallback)
    {
        var normalized = values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalized is { Length: > 0 })
        {
            return normalized;
        }

        return fallback;
    }
}
