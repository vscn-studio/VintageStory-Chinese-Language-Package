using NuGet.Versioning;

namespace Packer;

public sealed class GameTranslationConfig
{
    [System.Text.Json.Serialization.JsonPropertyName("contentRoot")]
    public string ContentRoot { get; set; } = "projects/game";

    [System.Text.Json.Serialization.JsonPropertyName("targetVersion")]
    public string TargetVersion { get; set; } = string.Empty;

    public void ApplyDefaults()
    {
        ContentRoot = string.IsNullOrWhiteSpace(ContentRoot) ? "projects/game" : ContentRoot.Trim();
        TargetVersion = TargetVersion?.Trim() ?? string.Empty;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ContentRoot))
        {
            throw new PackerException("gameTranslation.contentRoot must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(TargetVersion))
        {
            throw new PackerException("gameTranslation.targetVersion must not be empty.");
        }

        var normalizedVersion = TargetVersion.StartsWith('v') || TargetVersion.StartsWith('V')
            ? TargetVersion[1..]
            : TargetVersion;
        if (!NuGetVersion.TryParse(normalizedVersion, out _))
        {
            throw new PackerException(
                $"gameTranslation.targetVersion must be a valid game version, got '{TargetVersion}'.");
        }

        if (TargetVersion.Contains(Path.DirectorySeparatorChar) ||
            TargetVersion.Contains(Path.AltDirectorySeparatorChar) ||
            TargetVersion is "." or "..")
        {
            throw new PackerException("gameTranslation.targetVersion must be a single version directory name.");
        }
    }
}
