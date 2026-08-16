using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VscnLanguagePackUpdater;

public sealed class LanguagePackUpdaterModSystem : ModSystem
{
    private ICoreClientAPI? api;
    private CancellationTokenSource? cancellationTokenSource;
    private bool checkStarted;

    public override bool ShouldLoad(EnumAppSide forSide)
    {
        return forSide == EnumAppSide.Client;
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        this.api = api;
        cancellationTokenSource = new CancellationTokenSource();

        var config = LoadConfig(api);
        if (!config.Enabled)
        {
            api.Logger.Notification("[VSCN 汉化包更新器] 自动更新已关闭。");
            return;
        }

        api.Event.RegisterCallback(_ => StartUpdateCheck(config), config.CheckDelayMilliseconds, permittedWhilePaused: true);
    }

    public override void Dispose()
    {
        cancellationTokenSource?.Cancel();
        cancellationTokenSource?.Dispose();
        cancellationTokenSource = null;
        api = null;
        base.Dispose();
    }

    private static UpdaterConfig LoadConfig(ICoreClientAPI api)
    {
        UpdaterConfig config;
        try
        {
            config = api.LoadModConfig<UpdaterConfig>(UpdaterConstants.ConfigFileName) ?? new UpdaterConfig();
        }
        catch
        {
            config = new UpdaterConfig();
        }

        config.Normalize();
        api.StoreModConfig(config, UpdaterConstants.ConfigFileName);
        return config;
    }

    private void StartUpdateCheck(UpdaterConfig config)
    {
        if (checkStarted || api is null || cancellationTokenSource is null)
        {
            return;
        }

        checkStarted = true;
        var token = cancellationTokenSource.Token;
        _ = Task.Run(() => CheckAndUpdateAsync(config, token), token);
    }

    private async Task CheckAndUpdateAsync(UpdaterConfig config, CancellationToken cancellationToken)
    {
        if (api is null)
        {
            return;
        }

        try
        {
            var currentVersion = DetectInstalledVersion(config);
            using var releaseClient = new GitHubReleaseClient(TimeSpan.FromSeconds(config.HttpTimeoutSeconds));
            var latestReleaseUrls = config.CreateGithubUrls(config.ReleasesLatestUrl)
                .Concat(config.CreateGithubUrls(UpdaterConstants.VscnStudioLatestUrl))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var latestRelease = await releaseClient.GetLatestReleaseAsync(
                config.CreateGithubUrls(config.ReleasesApiUrl),
                latestReleaseUrls,
                tagName => CreateFallbackReleaseAssets(tagName, config),
                (url, message) => LogChannelFailure("获取发布信息", url, message),
                cancellationToken);
            api.Logger.Notification(
                "[VSCN 汉化包更新器] Release 信息获取成功：{0}，来源：{1}。",
                latestRelease.TagName,
                DescribeChannel(latestRelease.SourceUrl));

            if (!PackageVersion.TryParse(latestRelease.TagName, out var latestVersion) || latestVersion is null)
            {
                throw new InvalidOperationException($"Release tag '{latestRelease.TagName}' is not a supported package version.");
            }

            var asset = SelectLanguagePackAsset(latestRelease, latestVersion, config)
                ?? throw new InvalidOperationException($"Release '{latestRelease.TagName}' does not contain a language pack zip asset.");

            if (currentVersion is not null && latestVersion.CompareTo(currentVersion) <= 0)
            {
                if (config.NotifyWhenUpToDate)
                {
                    PostChatMessage($"[VSCN] 汉化包已是最新版本：{currentVersion}");
                }

                api.Logger.Notification("[VSCN 汉化包更新器] 汉化包已是最新版本：{0}。", currentVersion);
                return;
            }

            PackageInstaller.EnsureModsDirectory();
            var targetPath = PackageInstaller.ResolvePackagePath(asset.Name);

            if (File.Exists(targetPath))
            {
                PackageInstaller.ValidateLanguagePackZip(targetPath, latestVersion);
                if (config.DeleteOldPackages)
                {
                    PackageInstaller.DeleteOldPackages(targetPath, config.AssetFilePrefix, config.AssetFileSuffix, LogCleanupFailure);
                }

                PostChatMessage($"[VSCN] 已找到新版汉化包 {latestVersion}，请重启游戏生效。");
                api.Logger.Notification("[VSCN 汉化包更新器] 已存在最新版汉化包：{0}。", targetPath);
                return;
            }

            var tempPath = PackageInstaller.CreateTemporaryPackagePath(asset.Name);
            string? usedDownloadUrl = null;
            try
            {
                usedDownloadUrl = await releaseClient.DownloadFileAsync(
                    config.CreateGithubUrls(asset.DownloadUrl),
                    tempPath,
                    asset.Sha256,
                    (url, message) => LogChannelFailure("下载汉化包", url, message),
                    cancellationToken);
                PackageInstaller.ValidateLanguagePackZip(tempPath, latestVersion);
                PackageInstaller.InstallDownloadedPackage(tempPath, targetPath);
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                }
            }

            if (config.DeleteOldPackages)
            {
                PackageInstaller.DeleteOldPackages(targetPath, config.AssetFilePrefix, config.AssetFileSuffix, LogCleanupFailure);
            }

            var previousText = currentVersion is null ? "未安装" : currentVersion.ToString();
            PostChatMessage($"[VSCN] 汉化包已从 {previousText} 更新到 {latestVersion}，请重启游戏生效。");
            api.Logger.Notification(
                "[VSCN 汉化包更新器] 汉化包已从 {0} 更新到 {1}，下载通道：{2}。",
                previousText,
                latestVersion,
                DescribeChannel(usedDownloadUrl!));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            api.Logger.Warning("[VSCN 汉化包更新器] 更新检查失败：{0}", ex);
            if (config.NotifyOnFailure)
            {
                PostChatMessage("[VSCN] 汉化包自动更新检查失败：" + ex.Message);
            }
        }
    }

    private PackageVersion? DetectInstalledVersion(UpdaterConfig config)
    {
        var loadedVersion = api?.ModLoader.Mods
            .Select(mod => mod.Info)
            .FirstOrDefault(info => string.Equals(info.ModID, UpdaterConstants.LanguagePackModId, StringComparison.OrdinalIgnoreCase))
            ?.Version;

        if (PackageVersion.TryParse(loadedVersion, out var version))
        {
            return version;
        }

        if (!Directory.Exists(PackageInstaller.ModsPath))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(PackageInstaller.ModsPath, config.AssetFilePrefix + "*" + config.AssetFileSuffix, SearchOption.TopDirectoryOnly)
            .Select(path => PackageInstaller.TryGetPackageVersionFromFileName(Path.GetFileName(path), config.AssetFilePrefix, config.AssetFileSuffix))
            .Where(version => version is not null)
            .OrderDescending()
            .FirstOrDefault();
    }

    private static GitHubReleaseAsset? SelectLanguagePackAsset(GitHubRelease release, PackageVersion latestVersion, UpdaterConfig config)
    {
        var expectedName = config.AssetFilePrefix + latestVersion + config.AssetFileSuffix;
        var exact = release.Assets.FirstOrDefault(asset =>
            string.Equals(asset.Name, expectedName, StringComparison.OrdinalIgnoreCase) &&
            IsTrustedReleaseAssetUrl(asset.DownloadUrl));
        if (exact is not null)
        {
            return exact;
        }

        return release.Assets
            .Select(asset => new
            {
                Asset = asset,
                Version = PackageInstaller.TryGetPackageVersionFromFileName(asset.Name, config.AssetFilePrefix, config.AssetFileSuffix)
            })
            .Where(item => item.Version is not null && item.Version.CompareTo(latestVersion) == 0)
            .Where(item => IsTrustedReleaseAssetUrl(item.Asset.DownloadUrl))
            .OrderByDescending(item => item.Version)
            .Select(item => item.Asset)
            .FirstOrDefault();
    }

    private static IReadOnlyList<GitHubReleaseAsset> CreateFallbackReleaseAssets(string tagName, UpdaterConfig config)
    {
        if (!PackageVersion.TryParse(tagName, out var latestVersion) || latestVersion is null)
        {
            return [];
        }

        var assetName = config.AssetFilePrefix + latestVersion + config.AssetFileSuffix;
        var downloadUrl = config.CreateReleaseAssetDownloadUrl(tagName, assetName);
        return [new GitHubReleaseAsset(assetName, downloadUrl)];
    }

    private static bool IsTrustedReleaseAssetUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) &&
            uri.IsDefaultPort &&
            string.IsNullOrEmpty(uri.UserInfo) &&
            uri.AbsolutePath.StartsWith(
                "/vscn-studio/VintageStory-Chinese-Language-Package/releases/download/",
                StringComparison.OrdinalIgnoreCase);
    }

    private void PostChatMessage(string message)
    {
        var clientApi = api;
        if (clientApi is null || clientApi.IsShuttingDown)
        {
            return;
        }

        clientApi.Event.EnqueueMainThreadTask(() =>
        {
            if (!clientApi.IsShuttingDown)
            {
                clientApi.ShowChatMessage(message);
            }
        }, "vscnlangpackupdater-chat");
    }

    private void LogCleanupFailure(string message)
    {
        api?.Logger.Warning("[VSCN 汉化包更新器] 无法移除旧汉化包：{0}", message);
    }

    private void LogChannelFailure(string operation, string url, string message)
    {
        api?.Logger.Warning("[VSCN 汉化包更新器] {0}通道失败（{1}）：{2}", operation, DescribeChannel(url), message);
    }

    private static string DescribeChannel(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "自定义通道";
    }
}
