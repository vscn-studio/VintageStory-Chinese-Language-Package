namespace VscnLanguagePackUpdater;

public sealed class UpdaterConfig
{
    public bool Enabled { get; set; } = true;

    public int CheckDelayMilliseconds { get; set; } = 3000;

    public int HttpTimeoutSeconds { get; set; } = 30;

    public bool NotifyWhenUpToDate { get; set; }

    public bool NotifyOnFailure { get; set; } = true;

    public bool DeleteOldPackages { get; set; } = true;

    public string ReleasesApiUrl { get; set; } = UpdaterConstants.ReleasesApiUrl;

    public string AssetFilePrefix { get; set; } = UpdaterConstants.LanguagePackAssetPrefix;

    public string AssetFileSuffix { get; set; } = UpdaterConstants.LanguagePackAssetSuffix;

    public void Normalize()
    {
        if (CheckDelayMilliseconds < 0)
        {
            CheckDelayMilliseconds = 0;
        }

        if (HttpTimeoutSeconds < 5)
        {
            HttpTimeoutSeconds = 5;
        }

        if (string.IsNullOrWhiteSpace(ReleasesApiUrl))
        {
            ReleasesApiUrl = UpdaterConstants.ReleasesApiUrl;
        }

        if (string.IsNullOrWhiteSpace(AssetFilePrefix))
        {
            AssetFilePrefix = UpdaterConstants.LanguagePackAssetPrefix;
        }

        if (string.IsNullOrWhiteSpace(AssetFileSuffix))
        {
            AssetFileSuffix = UpdaterConstants.LanguagePackAssetSuffix;
        }
    }
}
