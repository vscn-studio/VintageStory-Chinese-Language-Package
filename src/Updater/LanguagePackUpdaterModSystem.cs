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
            api.Logger.Notification("[VSCN Language Pack Updater] Auto update is disabled.");
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
            var latestRelease = await releaseClient.GetLatestReleaseAsync(config.ReleasesApiUrl, cancellationToken);

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

                api.Logger.Notification("[VSCN Language Pack Updater] Language pack is up to date: {0}.", currentVersion);
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
                api.Logger.Notification("[VSCN Language Pack Updater] Latest language pack already exists at {0}.", targetPath);
                return;
            }

            var tempPath = targetPath + ".download";
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            await releaseClient.DownloadFileAsync(asset.DownloadUrl, tempPath, cancellationToken);
            PackageInstaller.ValidateLanguagePackZip(tempPath, latestVersion);
            PackageInstaller.InstallDownloadedPackage(tempPath, targetPath);

            if (config.DeleteOldPackages)
            {
                PackageInstaller.DeleteOldPackages(targetPath, config.AssetFilePrefix, config.AssetFileSuffix, LogCleanupFailure);
            }

            var previousText = currentVersion is null ? "未安装" : currentVersion.ToString();
            PostChatMessage($"[VSCN] 汉化包已更新：{previousText} -> {latestVersion}，请重启游戏生效。");
            api.Logger.Notification("[VSCN Language Pack Updater] Updated language pack from {0} to {1}.", previousText, latestVersion);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            api.Logger.Warning("[VSCN Language Pack Updater] Update check failed: {0}", ex);
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
        var exact = release.Assets.FirstOrDefault(asset => string.Equals(asset.Name, expectedName, StringComparison.OrdinalIgnoreCase));
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
            .OrderByDescending(item => item.Version)
            .Select(item => item.Asset)
            .FirstOrDefault();
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
        api?.Logger.Warning("[VSCN Language Pack Updater] Could not remove old language pack: {0}", message);
    }
}
